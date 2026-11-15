using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TunerFest;

/// <summary>
/// Packs every recorded session into one small ZIP that a player can send to the author (Discord, feedback form, any file share)
/// to build global statistics. Only text and roll data go in: no screenshots, no per-frame event dumps (except for legacy
/// sessions that have no rolls.jsonl), no duplicated catalog. The Windows user folder, user name and computer name are
/// replaced (see <see cref="Scrubber"/>). Nothing is sent anywhere; the player decides what to do with the file.
/// </summary>
public static class LogExport
{
    /// <summary>Files copied from each session folder. events.jsonl is added only when rolls.jsonl is missing (legacy sessions).</summary>
    static readonly string[] SessionFiles =
    [
        "session.json",
        "summary.json",
        "spending.json",
        "settings.json",
        "effective-settings.json",
        "rolls.jsonl",
        "rolls.csv",
        "legacy-rolls.csv",
        "legacy-spending.json",
        "session.log",
    ];

    /// <summary>Optimal instead of SmallestSize: text compresses almost as well and it costs a fraction of the CPU while the game runs.</summary>
    const CompressionLevel Level = CompressionLevel.Optimal;

    static readonly UTF8Encoding Utf8 = new(false);

    public sealed record Result(string Path, int Sessions, int Rolls, long Bytes);

    /// <summary>No session this installation recorded can be exported yet (no runs, or only internal tests).</summary>
    public sealed class NothingToExportException() : InvalidOperationException("No sessions to export.");

    public static string DefaultFileName() => $"SetBuilder-logs-{DateTime.Now:yyyy-MM-dd}.zip";

    /// <summary>Cheap check before asking where to save: whether the Logs folder holds any session at all.</summary>
    public static bool HasSessions(string? root = null)
    {
        root ??= SessionLog.Root;
        try
        {
            return Directory.Exists(root)
                && Directory.EnumerateDirectories(root).Any(d => File.Exists(Path.Combine(d, "session.json")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs <see cref="Create"/> on its own below-normal-priority background thread, so a game running next to the app
    /// keeps its CPU time while the archive is compressed. The await resumes on the caller's context.
    /// </summary>
    public static Task<Result> CreateInBackground(string destination, Catalog catalog, string? root = null)
    {
        var done = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Create(destination, catalog, root));
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "Log export",
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>
    /// Writes the archive to destination + ".partial" and moves it into place only when complete; on any failure the
    /// partial file is removed. Exactly the sessions the Analytics loader accepts go in, so the session count in the
    /// README, statistics.json and the result always agree with the folders inside the archive.
    /// </summary>
    public static Result Create(string destination, Catalog catalog, string? root = null, Scrubber? scrubber = null)
    {
        root ??= SessionLog.Root;
        scrubber ??= Scrubber.Current;
        var data = AnalyticsStore.Load(catalog, [root]);
        if (data.Sessions.Count == 0)
            throw new NothingToExportException();
        var folders = data.Sessions.Select(s => s.Folder).Order(StringComparer.Ordinal).ToList();
        var stats = Statistics(data, catalog);

        string temp = destination + ".partial";
        try
        {
            if (File.Exists(temp))
                File.Delete(temp);
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                Text(zip, "README.txt", Readme(stats.Sessions));
                Text(zip, "statistics.json", JsonSerializer.Serialize(stats, Catalog.JsonOptions));
                foreach (string folder in folders)
                {
                    string name = Path.GetFileName(folder);
                    bool hasRolls = File.Exists(Path.Combine(folder, "rolls.jsonl"));
                    foreach (string file in hasRolls ? SessionFiles : [.. SessionFiles, "events.jsonl"])
                    {
                        string path = Path.Combine(folder, file);
                        if (!File.Exists(path))
                            continue;
                        FileStream source;
                        try
                        {
                            source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            continue; // locked or protected; the rest of the session still goes in
                        }
                        using (source)
                            CopyScrubbed(source, zip, $"sessions/{name}/{file}", scrubber);
                    }
                }
            }
            File.Move(temp, destination, true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return new Result(destination, stats.Sessions, stats.Rolls, new FileInfo(destination).Length);
    }

    public sealed record Stats(
        string App,
        string Version,
        DateTimeOffset ExportedAt,
        int Sessions,
        int Rolls,
        long Bucks,
        long SpareParts,
        int NewOffersRecognized,
        int NoNameOrOffer,
        int UnconfirmedInputs,
        int CompletedParts,
        double? AverageRollsPerPart,
        double? AverageBucksPerPart,
        double? AverageSparePartsPerPart,
        double? SessionSeconds,
        double? AverageSecondsPerPart,
        List<PerkFrequency> Leaderboard
    );

    /// <summary>The same totals the Analytics page shows for all sessions.</summary>
    public static Stats Statistics(AnalyticsData data, Catalog catalog)
    {
        var rolls = data.Sessions.SelectMany(s => s.Rolls).ToList();
        var paid = rolls.Where(r => r.Paid).ToList();
        var leaderboard = AnalyticsData.Leaderboard(catalog, paid);
        int known = leaderboard.Sum(p => p.Count);
        var items = ItemAnalytics.Summary(data.Sessions);
        return new Stats(
            "TunerFest Set Builder",
            Ui.AppInfo.Version,
            DateTimeOffset.Now,
            data.Sessions.Count,
            paid.Count,
            paid.Sum(r => r.Bucks),
            paid.Sum(r => r.SpareParts),
            known,
            paid.Count - known,
            rolls.Count(r => r.Requested && !r.Paid && !r.NotSent),
            items.CompletedItems,
            items.AverageRolls,
            items.AverageBucks,
            items.AverageSpareParts,
            items.TotalDurationSeconds,
            items.AverageDurationSeconds,
            leaderboard
        );
    }

    /// <summary>
    /// Streams a file into the archive line by line (line endings kept), so a large legacy events.jsonl never sits in memory whole.
    /// Paths and names never span lines, so scrubbing per line is exact.
    /// </summary>
    static void CopyScrubbed(Stream source, ZipArchive zip, string name, Scrubber scrubber)
    {
        using var reader = new StreamReader(source, Utf8, true, 1 << 16);
        var entry = zip.CreateEntry(name, Level);
        using var writer = new StreamWriter(entry.Open(), Utf8, 1 << 16);
        var buffer = new char[1 << 16];
        var line = new StringBuilder();
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            int start = 0;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != '\n')
                    continue;
                line.Append(buffer, start, i - start + 1);
                writer.Write(scrubber.Apply(line.ToString()));
                line.Clear();
                start = i + 1;
            }
            line.Append(buffer, start, read - start);
        }
        if (line.Length > 0)
            writer.Write(scrubber.Apply(line.ToString()));
    }

    static void Text(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, Level);
        using var writer = new StreamWriter(entry.Open(), Utf8);
        writer.Write(content);
    }

    static string Readme(int sessions) =>
        $"""
        TunerFest Set Builder {Ui.AppInfo.Version}: exported logs
        Exported: {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}
        Sessions: {sessions}

        This file was made by the player with "Export logs" in the app. The app never sends anything by itself.
        It is meant for the global perk and cost statistics that will be published on https://tunerfest.app.

        Inside:
          statistics.json         totals, averages and the perk frequency table across all sessions
          sessions/<id>/          per-run text logs, roll ledger, spending and the settings used

        Not inside: screenshots, per-frame event dumps (except old sessions without a roll ledger), account data.
        The Windows user folder is replaced with %USERPROFILE%, the user name with %USERNAME%
        and the computer name with %COMPUTERNAME%.
        """;
}

/// <summary>
/// Removes what identifies the player from exported text: the profile folder in its long and 8.3 short forms (plain,
/// JSON-escaped and forward-slash), then the profile folder name, the Windows user name and the computer name wherever
/// they stand as a separate word (UNC paths, OneDrive folders, plain mentions). Case-insensitive.
/// </summary>
public sealed class Scrubber
{
    /// <summary>Names shorter than this are not replaced as words: "Al" would also hit ordinary text.</summary>
    const int MinNameLength = 3;

    readonly (string Find, string Replace)[] literals;
    readonly Regex? words;
    readonly Dictionary<string, string> wordReplacements = new(StringComparer.OrdinalIgnoreCase);

    static Scrubber? current;

    /// <summary>The scrubber for this Windows account, built once.</summary>
    public static Scrubber Current =>
        current ??= ForProfile(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName,
            Environment.MachineName
        );

    public Scrubber(IEnumerable<string> profilePaths, IEnumerable<string> userNames, IEnumerable<string> machineNames)
    {
        var finds = new List<(string, string)>();
        foreach (string raw in profilePaths)
        {
            string path = raw.TrimEnd('\\', '/');
            if (path.Length < 4)
                continue;
            finds.Add((path.Replace("\\", "\\\\"), "%USERPROFILE%"));
            finds.Add((path, "%USERPROFILE%"));
            finds.Add((path.Replace('\\', '/'), "%USERPROFILE%"));
        }
        // Longest first, so "C:\\Users\\Name" (JSON) is replaced before a shorter form could split it.
        literals = finds.DistinctBy(f => f.Item1, StringComparer.OrdinalIgnoreCase).OrderByDescending(f => f.Item1.Length).ToArray();

        foreach (string name in userNames.Where(n => n.Trim().Length >= MinNameLength))
            wordReplacements.TryAdd(name.Trim(), "%USERNAME%");
        foreach (string name in machineNames.Where(n => n.Trim().Length >= MinNameLength))
            wordReplacements.TryAdd(name.Trim(), "%COMPUTERNAME%");
        if (wordReplacements.Count > 0)
        {
            string alternatives = string.Join("|", wordReplacements.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
            words = new Regex(
                $@"(?<![\p{{L}}\p{{N}}_])(?:{alternatives})(?![\p{{L}}\p{{N}}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
            );
        }
    }

    /// <summary>A scrubber for a profile folder: also covers its 8.3 short path and folder name, which can differ from the user name.</summary>
    public static Scrubber ForProfile(string profile, string userName, string machineName, string? shortProfile = null)
    {
        profile = profile.TrimEnd('\\', '/');
        shortProfile ??= ShortPath(profile);
        string[] paths = [profile, shortProfile];
        string[] names = [Path.GetFileName(profile), Path.GetFileName(shortProfile), userName];
        return new Scrubber(paths, names, [machineName]);
    }

    public string Apply(string text)
    {
        if (text.Length == 0)
            return text;
        foreach (var (find, replace) in literals)
            text = text.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
        return words == null ? text : words.Replace(text, m => wordReplacements.GetValueOrDefault(m.Value, "%USERNAME%"));
    }

    /// <summary>The 8.3 form Windows uses for folders with spaces or long names (C:\Users\SANEKX~1); the input when there is none.</summary>
    static string ShortPath(string path)
    {
        try
        {
            var buffer = new StringBuilder(520);
            uint length = GetShortPathName(path, buffer, (uint)buffer.Capacity);
            return length > 0 && length < buffer.Capacity ? buffer.ToString().TrimEnd('\\') : path;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return path;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetShortPathNameW")]
    static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint bufferLength);
}
