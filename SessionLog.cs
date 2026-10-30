using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace TunerFest;

// One local recording per run. Text is flushed immediately; PNG encoding runs off the UI thread.
public sealed class SessionLog
{
    // One Logs folder for every build: the project root when the executable sits inside the project tree
    // (dist-v*, bin), otherwise Logs beside the executable. All versions write and read the same place.
    public static string Root { get; } = FindRoot();

    static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 7 && directory != null; i++, directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SetBuilder.csproj")))
                return Path.Combine(directory.FullName, "Logs");
        return Path.Combine(AppContext.BaseDirectory, "Logs");
    }
    public string Folder { get; }
    public RunLedger Ledger { get; }
    public string CurrentFrame { get; private set; } = "none";
    public string? WriteError { get; private set; }
    readonly object gate = new();
    readonly StreamWriter events,
        text,
        rolls,
        csv;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Channel<Shot> queue = Channel.CreateBounded<Shot>(
        new BoundedChannelOptions(4)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        }
    );
    readonly Task worker;
    readonly bool screenshots;
    readonly int maxImages;
    readonly long maxBytes;
    Bitmap? lastSafeFrame;
    int frameNumber,
        imageNumber,
        queued,
        saved,
        skipped,
        errors,
        eventNumber;
    long bytes;
    bool completed;

    record Shot(Bitmap Image, string File, string Frame, string Label);

    static string Safe(string label) =>
        Regex.Replace(label, "[^a-zA-Z0-9_-]", "_")[..Math.Min(70, Regex.Replace(label, "[^a-zA-Z0-9_-]", "_").Length)];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public SessionLog(Profile profile, string mode, object? window = null, string? root = null)
    {
        Folder = Path.Combine(
            root ?? (IsSelfTest(mode) ? Path.Combine(Root, "Tests") : Root),
            DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Guid.NewGuid().ToString("N")[..6]
        );
        Directory.CreateDirectory(Path.Combine(Folder, "screenshots"));
        Ledger = new RunLedger(this);
        screenshots = profile.DiagnosticScreenshots;
        maxImages = Math.Clamp(profile.DiagnosticMaxImages, 1, 2000);
        maxBytes = (long)Math.Clamp(profile.DiagnosticMaxMb, 1, 2048) * 1024 * 1024;
        events = Writer(Path.Combine(Folder, "events.jsonl"));
        text = Writer(Path.Combine(Folder, "session.log"));
        rolls = Writer(Path.Combine(Folder, "rolls.jsonl"));
        csv = Writer(Path.Combine(Folder, "rolls.csv"));
        csv.WriteLine(
            "roll_id,time,part,slot,phase,targets,other_perk,fresh_name,fresh_value,fresh_min,fresh_max,reroll_name,reroll_value,reroll_min,reroll_max,current_name,current_value,choice,expected_name,expected_value,bucks,spare_parts,frame"
        );
        File.WriteAllText(
            Path.Combine(Folder, "settings.json"),
            JsonSerializer.Serialize(profile, Catalog.JsonOptions)
        );
        File.WriteAllText(
            Path.Combine(Folder, "session.json"),
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 2,
                    version = typeof(SessionLog).Assembly.GetName().Version?.ToString(),
                    mode,
                    startedAt = DateTimeOffset.Now,
                    window,
                    os = Environment.OSVersion.ToString(),
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    screenshots,
                    maxImages,
                    maxBytes,
                },
                Catalog.JsonOptions
            )
        );
        string catalog = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        if (File.Exists(catalog))
            File.Copy(catalog, Path.Combine(Folder, "catalog.json"));
        File.WriteAllText(
            Path.Combine(Folder, "README.txt"),
            "Local diagnostic recording. Nothing is uploaded.\nsettings.json: exact profile and zones. session.json: app and run metadata.\nsession.log: readable messages. events.jsonl: ordered OCR, parsed values, choices, inputs and screenshot links.\nEach frame has an ID; screenshots contain the captured game window or OCR crop only.\nscreenshot_saved confirms a PNG exists; screenshot_skipped explains sampling/size/queue limits.\nsummary.json records completion, cancellation, errors and screenshot totals.\nText is flushed while the app runs; if it crashes, existing files remain readable.\n"
        );
        worker = Task.Run(WriteImages);
        Write(
            "session_started",
            new
            {
                mode,
                screenshots,
                maxImages,
                maxBytes,
            }
        );
    }

    // Offline verification runs (Test.ps1, --self-test, --verify-*) go to Logs/Tests so the shared history
    // holds only real runs and screen reads. "offline-png" is a user's own PNG inspection and stays with them.
    static bool IsSelfTest(string mode) =>
        mode.StartsWith("offline-", StringComparison.Ordinal) && mode != "offline-png";

    static StreamWriter Writer(string path) =>
        new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };

    public void Write(string kind, object? data = null)
    {
        lock (gate)
        {
            if (completed)
                return;
            try
            {
                events.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            sequence = ++eventNumber,
                            time = DateTimeOffset.Now,
                            elapsedMs = clock.ElapsedMilliseconds,
                            frame = CurrentFrame,
                            kind,
                            data,
                        },
                        Json
                    )
                );
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                WriteError = e.Message;
            }
        }
    }

    public void Message(string message)
    {
        lock (gate)
        {
            if (completed)
                return;
            try
            {
                text.WriteLine($"{DateTimeOffset.Now:O}  +{clock.ElapsedMilliseconds}ms  {message}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                WriteError = e.Message;
            }
            Write("message", new { message });
        }
    }

    // True while another full-size PNG still fits the session budget; callers can skip purely diagnostic captures.
    public bool ScreenshotBudgetAvailable =>
        screenshots && Volatile.Read(ref queued) < maxImages && Interlocked.Read(ref bytes) < maxBytes;

    // retain=false lets the caller keep ownership of the frame and hand it over later via Retain(),
    // which avoids one 20 MB copy per captured frame.
    public void Observe(Bitmap frame, string stage, bool retain = true, Rectangle? region = null)
    {
        CurrentFrame = $"frame-{++frameNumber:D6}";
        Write(
            "frame",
            new
            {
                stage,
                width = frame.Width,
                height = frame.Height,
                region,
            }
        );
        if (!screenshots)
            return;
        if (retain)
        {
            lastSafeFrame?.Dispose();
            lastSafeFrame = new Bitmap(frame);
        }
        Evidence(frame, stage);
    }

    // Takes ownership of the latest captured frame so it can be saved as last-safe-frame.png on failure.
    public void Retain(Bitmap? frame)
    {
        if (!screenshots || completed)
        {
            frame?.Dispose();
            return;
        }
        lastSafeFrame?.Dispose();
        lastSafeFrame = frame;
    }

    public void Evidence(Bitmap image, string label)
    {
        if (!screenshots)
            return;
        string file = $"screenshots/{CurrentFrame}_{++imageNumber:D6}_{Safe(label)}.png";
        if (Volatile.Read(ref queued) >= maxImages || Interlocked.Read(ref bytes) >= maxBytes)
        {
            Interlocked.Increment(ref skipped);
            Write("screenshot_skipped", new { label, reason = "session_limit" });
            return;
        }
        var copy = new Bitmap(image);
        var shot = new Shot(copy, file, CurrentFrame, label);
        if (!queue.Writer.TryWrite(shot))
        {
            copy.Dispose();
            Interlocked.Increment(ref skipped);
            Write("screenshot_skipped", new { label, reason = "encoder_queue_full" });
            return;
        }
        Interlocked.Increment(ref queued);
        Write(
            "screenshot_queued",
            new
            {
                file,
                label,
                width = image.Width,
                height = image.Height,
            }
        );
    }

    async Task WriteImages()
    {
        await foreach (var shot in queue.Reader.ReadAllAsync())
        {
            using (shot.Image)
            {
                if (Interlocked.Read(ref bytes) >= maxBytes)
                {
                    Interlocked.Increment(ref skipped);
                    Write("screenshot_skipped", new { file = shot.File, reason = "session_size_limit" });
                    continue;
                }
                Save(shot, false);
            }
        }
    }

    void Save(Shot shot, bool final)
    {
        try
        {
            using var memory = new MemoryStream();
            shot.Image.Save(memory, ImageFormat.Png);
            if (!final && Interlocked.Read(ref bytes) + memory.Length > maxBytes)
            {
                Interlocked.Increment(ref skipped);
                Write("screenshot_skipped", new { file = shot.File, reason = "session_size_limit" });
                return;
            }
            File.WriteAllBytes(
                Path.Combine(Folder, shot.File.Replace('/', Path.DirectorySeparatorChar)),
                memory.ToArray()
            );
            Interlocked.Add(ref bytes, memory.Length);
            Interlocked.Increment(ref saved);
            Write(
                "screenshot_saved",
                new
                {
                    file = shot.File,
                    sourceFrame = shot.Frame,
                    label = shot.Label,
                    bytes = memory.Length,
                    final,
                }
            );
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            Interlocked.Increment(ref errors);
            WriteError = e.Message;
            Write("screenshot_error", new { file = shot.File, error = e.ToString() });
        }
    }

    public async Task Finish(string outcome, Exception? error = null)
    {
        if (completed)
            return;
        SaveSpending(Ledger.Snapshot());
        Write("session_ending", new { outcome, error = error?.ToString() });
        queue.Writer.TryComplete();
        await worker;
        // Retain the last known game frame on failure even when the normal PNG budget was exhausted.
        if (screenshots && lastSafeFrame != null && outcome != "completed")
        {
            var shot = new Shot(lastSafeFrame, "screenshots/last-safe-frame.png", CurrentFrame, "last-safe-frame");
            await Task.Run(() => Save(shot, true));
        }
        lastSafeFrame?.Dispose();
        lastSafeFrame = null;
        Write(
            "session_finished",
            new
            {
                outcome,
                frames = frameNumber,
                saved,
                skipped,
                errors,
                bytes,
                error = error?.ToString(),
            }
        );
        try
        {
            File.WriteAllText(
                Path.Combine(Folder, "summary.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        outcome,
                        finishedAt = DateTimeOffset.Now,
                        elapsedMs = clock.ElapsedMilliseconds,
                        frames = frameNumber,
                        screenshots = saved,
                        skipped,
                        errors,
                        bytes,
                        error = error?.ToString(),
                        writeError = WriteError,
                        spending = Ledger.Snapshot(),
                    },
                    Catalog.JsonOptions
                )
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            WriteError = e.Message;
        }
        lock (gate)
        {
            completed = true;
            events.Dispose();
            text.Dispose();
            rolls.Dispose();
            csv.Dispose();
        }
    }

    public void AppendRoll(string kind, object data)
    {
        lock (gate)
        {
            if (completed)
                return;
            try
            {
                rolls.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            time = DateTimeOffset.Now,
                            frame = CurrentFrame,
                            kind,
                            data,
                        },
                        Json
                    )
                );
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                WriteError = e.Message;
            }
            Write("roll_" + kind, data);
        }
    }

    public void AppendOffer(RollSample sample)
    {
        static string Cell(object? value)
        {
            string s = value switch
            {
                null => "",
                IFormattable n => n.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        object?[] cells =
        [
            sample.RollId,
            sample.Time.ToString("O"),
            sample.Part,
            sample.Slot,
            sample.Phase,
            string.Join(" / ", sample.Targets),
            sample.OtherPerk,
            sample.Fresh?.Name,
            sample.Fresh?.Stats?.Value,
            sample.Fresh?.Stats?.Minimum,
            sample.Fresh?.Stats?.Maximum,
            sample.Reroll?.Name,
            sample.Reroll?.Stats?.Value,
            sample.Reroll?.Stats?.Minimum,
            sample.Reroll?.Stats?.Maximum,
            sample.Current?.Name,
            sample.Current?.Stats?.Value,
            sample.Choice,
            sample.Expected.Name,
            sample.Expected.Stats?.Value,
            RunLedger.BucksPerRoll,
            RunLedger.SparePartsPerRoll,
            sample.Frame,
        ];
        lock (gate)
        {
            try
            {
                csv.WriteLine(string.Join(',', cells.Select(Cell)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                WriteError = e.Message;
            }
        }
    }

    public void SaveSpending(object snapshot)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Folder, "spending.json"),
                JsonSerializer.Serialize(snapshot, Catalog.JsonOptions)
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            WriteError = e.Message;
        }
    }

    public void SaveEffectiveProfile(Profile profile)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Folder, "effective-settings.json"),
                JsonSerializer.Serialize(profile, Catalog.JsonOptions)
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            WriteError = e.Message;
        }
    }

    public static string? Latest() =>
        Directory.Exists(Root)
            ? Directory
                .GetDirectories(Root)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "session.json")))
            : null;

    public static string Export(string session)
    {
        var destination = Path.Combine(Root, "Archives");
        Directory.CreateDirectory(destination);
        var path = Path.Combine(
            destination,
            Path.GetFileName(session) + "_" + DateTime.Now.ToString("HHmmssfff") + ".zip"
        );
        ZipFile.CreateFromDirectory(session, path, CompressionLevel.Fastest, false);
        return path;
    }
}
