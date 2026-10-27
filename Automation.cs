using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace TunerFest;

public sealed class Vision
{
    readonly Catalog catalog;
    readonly Profile profile;
    readonly OcrEngine engine;
    readonly SessionLog? trace;
    string diagnosticZone = "";
    List<(string Text, Rectangle Box)> words = [];
    readonly VisionTemplates templates;

    record CachedOcr(string Text, List<(string Text, Rectangle Box)> Words);

    readonly Dictionary<string, CachedOcr> cache = [];
    readonly Queue<string> cacheOrder = [];

    public Vision(Catalog c, Profile p, SessionLog? diagnostics = null, VisionTemplates? model = null)
    {
        templates = model ?? VisionTemplates.Shared.Value;
        trace = diagnostics;
        catalog = c;
        profile = p;
        engine =
            OcrEngine.TryCreateFromLanguage(new Language("en-US"))
            ?? OcrEngine.TryCreateFromLanguage(new Language("en-GB"))
            ?? throw new InvalidOperationException("Додайте English OCR у мовних компонентах Windows.");
    }

    public static string[] Languages() => OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToArray();

    public async Task<string> Text(Bitmap frame, string zone)
    {
        if (!profile.Zones.TryGetValue(zone, out var z) || !z.Valid)
            throw new InvalidOperationException($"Налаштуйте зону {zone}.");
        var r = z.Pixels(frame.Size);
        if (r.Width < 8 || r.Height < 8)
            throw new InvalidOperationException("Зона надто мала.");
        diagnosticZone = zone;
        trace?.Write("ocr_zone", new { zone, bounds = r });
        using var crop = frame.Clone(r, PixelFormat.Format32bppArgb);
        return await Text(crop);
    }

    public async Task<string> Text(Bitmap crop, bool alternate = false, bool threshold = false)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string? key = profile.FastRecognition ? ImageKey(crop, alternate, threshold) : null;
        if (key != null && cache.TryGetValue(key, out var hit))
        {
            words = hit.Words;
            trace?.Write(
                "ocr",
                new
                {
                    zone = diagnosticZone,
                    variant = threshold ? "threshold"
                    : alternate ? "inverted-grayscale"
                    : "original",
                    source = "pixel-cache",
                    width = crop.Width,
                    height = crop.Height,
                    elapsedMs = watch.Elapsed.TotalMilliseconds,
                    text = hit.Text,
                    words = words.Select(w => new { w.Text, w.Box }),
                }
            );
            return hit.Text;
        }
        double scale = Math.Min(
            2.5,
            Math.Min(
                (double)OcrEngine.MaxImageDimension / crop.Width,
                (double)OcrEngine.MaxImageDimension / crop.Height
            )
        );
        using var image = new Bitmap((int)(crop.Width * scale), (int)(crop.Height * scale));
        using (var g = Graphics.FromImage(image))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            if (!alternate)
                g.DrawImage(crop, new Rectangle(Point.Empty, image.Size));
            else
            {
                // Inverted grayscale provides a second OCR view of the same pixels.
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(
                    new ColorMatrix(
                        new float[][]
                        {
                            [-.299f, -.299f, -.299f, 0, 0],
                            [-.587f, -.587f, -.587f, 0, 0],
                            [-.114f, -.114f, -.114f, 0, 0],
                            [0, 0, 0, 1, 0],
                            [1, 1, 1, 0, 1],
                        }
                    )
                );
                if (threshold)
                    attributes.SetThreshold(.65f);
                g.DrawImage(
                    crop,
                    new Rectangle(Point.Empty, image.Size),
                    0,
                    0,
                    crop.Width,
                    crop.Height,
                    GraphicsUnit.Pixel,
                    attributes
                );
            }
        }
        // Feed BGRA pixels directly to Windows OCR; avoid PNG compression and decoding on every read.
        using var pixels = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            image.Width,
            image.Height,
            BitmapAlphaMode.Premultiplied
        );
        var locked = image.LockBits(
            new Rectangle(Point.Empty, image.Size),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb
        );
        byte[] rawPixels = new byte[Math.Abs(locked.Stride) * image.Height];
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(locked.Scan0, rawPixels, 0, rawPixels.Length);
        }
        finally
        {
            image.UnlockBits(locked);
        }
        using (var writer = new DataWriter())
        {
            writer.WriteBytes(rawPixels);
            pixels.CopyFromBuffer(writer.DetachBuffer());
        }
        var result = await engine.RecognizeAsync(pixels);
        words = result
            .Lines.SelectMany(l => l.Words)
            .Select(w =>
                (
                    w.Text,
                    new Rectangle(
                        (int)(w.BoundingRect.X / scale),
                        (int)(w.BoundingRect.Y / scale),
                        (int)Math.Ceiling(w.BoundingRect.Width / scale),
                        (int)Math.Ceiling(w.BoundingRect.Height / scale)
                    )
                )
            )
            .ToList();
        var raw = string.Join(" ", result.Lines.Select(l => l.Text));
        trace?.Write(
            "ocr",
            new
            {
                zone = diagnosticZone,
                variant = threshold ? "threshold"
                : alternate ? "inverted-grayscale"
                : "original",
                width = crop.Width,
                height = crop.Height,
                elapsedMs = watch.ElapsedMilliseconds,
                text = raw,
                words = words.Select(w => new { w.Text, w.Box }),
            }
        );
        if (key != null)
        {
            cache[key] = new(raw, words);
            cacheOrder.Enqueue(key);
            if (cacheOrder.Count > 256)
                cache.Remove(cacheOrder.Dequeue());
        }
        return raw;
    }

    static string ImageKey(Bitmap image, bool alternate, bool threshold)
    {
        var data = image.LockBits(
            new Rectangle(Point.Empty, image.Size),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );
        int length = Math.Abs(data.Stride) * image.Height;
        byte[] pixels = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, length);
            return $"{image.Width}:{image.Height}:{alternate}:{threshold}:"
                + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels.AsSpan(0, length)));
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(pixels);
            image.UnlockBits(data);
        }
    }

    bool TryName(Bitmap frame, string zone, out Affix? affix)
    {
        affix = null;
        if (!profile.FastRecognition || !profile.Zones.TryGetValue(zone, out var area))
            return false;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (!templates.Match(frame, area.Pixels(frame.Size), out var name, out double score))
            return false;
        affix = catalog.Affixes.FirstOrDefault(a => a.Title == name);
        if (affix == null)
            return false;
        trace?.Write(
            "name_template",
            new
            {
                zone,
                name,
                confidence = score,
                elapsedMs = timer.Elapsed.TotalMilliseconds,
            }
        );
        return true;
    }

    bool LooksLikeCard(Bitmap frame, string zone)
    {
        var r = profile.Zones[zone].Pixels(frame.Size);
        int dark = 0,
            total = 0;
        for (int y = r.Y + r.Height / 8; y < r.Bottom - r.Height / 8; y += Math.Max(1, r.Height / 9))
        for (int x = r.X + r.Width / 16; x < r.Right - r.Width / 16; x += Math.Max(1, r.Width / 15))
        {
            var c = frame.GetPixel(x, y);
            total++;
            if (c.R < 65 && c.G < 65 && c.B < 65)
                dark++;
        }
        return total > 0 && (double)dark / total > .75;
    }

    Reading Parsed(string value, Catalog ignored)
    {
        var reading = Recognition.Parse(value, catalog);
        trace?.Write(
            "reading",
            new
            {
                zone = diagnosticZone,
                affix = reading.Affix.Title,
                reading.Value,
                reading.Minimum,
                reading.Maximum,
            }
        );
        return reading;
    }

    public async Task<Reading> Read(Bitmap frame, string zone)
    {
        var raw = await Text(frame, zone);
        var valueWord = words.FirstOrDefault(w =>
            System.Text.RegularExpressions.Regex.IsMatch(w.Text, @"^\d[\d.,]*(?:%|0/0)$")
        );
        try
        {
            return Parsed(raw, catalog);
        }
        catch (InvalidOperationException first)
        {
            trace?.Write(
                "ocr_parse_failed",
                new
                {
                    zone,
                    raw,
                    error = first.Message,
                }
            );
            using var crop = frame.Clone(profile.Zones[zone].Pixels(frame.Size), PixelFormat.Format32bppArgb);
            trace?.Evidence(crop, "ocr-crop-" + zone);
            try
            {
                return Parsed(await Text(crop, true), catalog);
            }
            catch (InvalidOperationException)
            {
                try
                {
                    return Parsed(await Text(crop, true, true), catalog);
                }
                catch (InvalidOperationException)
                {
                    // Isolate the description: gray ranges can disappear when OCR groups them with a bright title.
                    using var description = crop.Clone(
                        new Rectangle(crop.Width / 40, crop.Height * 48 / 100, crop.Width * 94 / 100, crop.Height / 2),
                        PixelFormat.Format32bppArgb
                    );
                    try
                    {
                        return Parsed(
                            Recognition.Name(raw, catalog).Title + " " + await Text(description, true, true),
                            catalog
                        );
                    }
                    catch (InvalidOperationException)
                    {
                        // Read the faint bracket on its own, immediately to the right of the recognized value.
                        if (valueWord.Box.Width > 0)
                        {
                            var bounds = Rectangle.Intersect(
                                new Rectangle(
                                    valueWord.Box.Right,
                                    Math.Max(0, valueWord.Box.Y - 5),
                                    crop.Width / 3,
                                    valueWord.Box.Height + 10
                                ),
                                new Rectangle(Point.Empty, crop.Size)
                            );
                            if (bounds.Width > 8 && bounds.Height > 8)
                            {
                                using var range = crop.Clone(bounds, PixelFormat.Format32bppArgb);
                                foreach (bool contrast in new[] { false, true })
                                {
                                    try
                                    {
                                        return Parsed(
                                            Recognition.Name(raw, catalog).Title
                                                + " "
                                                + valueWord.Text
                                                + " "
                                                + await Text(range, contrast, contrast),
                                            catalog
                                        );
                                    }
                                    catch (InvalidOperationException) { }
                                }
                            }
                        }
                        throw new InvalidOperationException(
                            first.Message + " Додаткове зчитування також не підтвердило значення."
                        );
                    }
                }
            }
        }
    }

    public async Task<Affix> ReadName(Bitmap frame, string zone)
    {
        if (TryName(frame, zone, out var cachedName))
            return cachedName!;
        try
        {
            return Recognition.Name(await Text(frame, zone), catalog);
        }
        catch (InvalidOperationException)
        {
            using var crop = frame.Clone(profile.Zones[zone].Pixels(frame.Size), PixelFormat.Format32bppArgb);
            trace?.Evidence(crop, "ocr-crop-" + zone);
            return Recognition.Name(await Text(crop, true, true), catalog);
        }
    }

    public async Task<RollChoice> Decide(Bitmap frame, string[] rows, Reading current, string wanted)
    {
        var bottom = await Read(frame, rows[2]);
        if (!Recognition.Equal(bottom, current))
            throw new InvalidOperationException("Нижній варіант не відповідає збереженому перку.");
        var name = (await ReadName(frame, rows[0])).Title;
        // Only the option that could actually be accepted requires numeric OCR.
        var fresh = current.Affix.Title != wanted && name == wanted ? await Read(frame, rows[0]) : null;
        var reroll = current.Affix.Title == wanted ? await Read(frame, rows[1]) : null;
        if (Focus(frame, rows) < 0)
            throw new InvalidOperationException("Не знайдено жовту рамку вибору.");
        var plan = Recognition.Plan(current, name, wanted, fresh, reroll);
        trace?.Write(
            "decision",
            new
            {
                wanted,
                current = current.ToString(),
                freshName = name,
                fresh = fresh?.ToString(),
                reroll = reroll?.ToString(),
                choice = plan.Index + 1,
                expected = plan.Expected.ToString(),
            }
        );
        return plan;
    }

    public async Task<PerkCard> Card(Bitmap frame, string zone, bool requireStats = false)
    {
        if (TryName(frame, zone, out var quick))
        {
            var r = profile.Zones[zone].Pixels(frame.Size);
            var bounds = Rectangle.Intersect(
                new Rectangle(r.X + r.Width / 40, r.Y + r.Height / 2, r.Width * 95 / 100, r.Height * 48 / 100),
                new Rectangle(Point.Empty, frame.Size)
            );
            using var description = frame.Clone(bounds, PixelFormat.Format32bppArgb);
            diagnosticZone = zone;
            var text = await Text(description);
            try
            {
                return new(quick!.Title, Parsed(quick.Title + " " + text, catalog));
            }
            catch (InvalidOperationException)
            {
                if (requireStats)
                    return new(quick!.Title, await Read(frame, zone));
                return new(quick!.Title, null);
            }
        }
        var raw = await Text(frame, zone);
        string name;
        try
        {
            name = Recognition.Name(raw, catalog).Title;
        }
        catch (InvalidOperationException)
        {
            name = (await ReadName(frame, zone)).Title;
        }
        Reading? stats = null;
        try
        {
            stats = Parsed(raw, catalog);
        }
        catch (InvalidOperationException)
        {
            if (requireStats)
                stats = await Read(frame, zone);
        }
        return new(name, stats);
    }

    public async Task<PopupSelection> Select(
        Bitmap frame,
        string[] rows,
        PerkCard current,
        RollPhase phase,
        string[] targets
    )
    {
        var bottom = await Card(frame, rows[2], current.Stats != null && targets.Contains(current.Name));
        if (!PerkCard.Same(bottom, current))
            throw new InvalidOperationException("Нижній варіант не відповідає поточному перку.");
        async Task<PerkCard?> Optional(string zone)
        {
            try
            {
                return await Card(frame, zone);
            }
            catch (InvalidOperationException e)
            {
                trace?.Write("analytics_ocr_unknown", new { zone, error = e.Message });
                return null;
            }
        }
        PerkCard? fresh = null,
            reroll = null;
        PerkCard expected;
        int index;
        if (phase == RollPhase.Search)
        {
            // Always take the top new offer. Only desired names require full percentage validation.
            fresh = await Card(frame, rows[0]);
            if (targets.Contains(fresh.Name) && fresh.Stats == null)
                fresh = new(fresh.Name, await Read(frame, rows[0]));
            expected = fresh;
            reroll = await Optional(rows[1]);
            index = 0;
        }
        else
        {
            reroll = await Card(frame, rows[1], true);
            expected = reroll;
            fresh = await Optional(rows[0]);
            index = 1;
            if (
                expected.Name != current.Name
                || current.Stats == null
                || expected.Stats!.Minimum != current.Stats.Minimum
                || expected.Stats.Maximum != current.Stats.Maximum
            )
                throw new InvalidOperationException("Середній кидок не відповідає перку, який покращуємо.");
        }
        if (Focus(frame, rows) < 0)
            throw new InvalidOperationException("Не знайдено жовту рамку вибору.");
        trace?.Write(
            "decision",
            new
            {
                phase = phase.ToString(),
                targets,
                current = current.ToString(),
                choice = index + 1,
                expected = expected.ToString(),
            }
        );
        return new(index, expected, phase, fresh, reroll, bottom);
    }

    public async Task<GameScreen> ScreenState(Bitmap frame)
    {
        string breadcrumb = Recognition.Normalize(await Text(frame, "breadcrumb"));
        if (!breadcrumb.Contains("UPGRADE"))
            return GameScreen.Unknown;
        if (!breadcrumb.Contains("RECALIBER"))
        {
            if (GridFocus(frame) < 0)
                return GameScreen.Unknown;
            var header = (await Text(frame, "inventoryHeader")).Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(header, @"^[A-Z][A-Z ]{2,28}$")
                ? GameScreen.Inventory
                : GameScreen.Parts;
        }
        int popup = await PopupSide(frame);
        if (popup >= 0)
            return popup == 0 ? GameScreen.PopupLeft : GameScreen.PopupRight;
        try
        {
            await ReadName(frame, "left");
            await ReadName(frame, "right");
        }
        catch (InvalidOperationException)
        {
            return GameScreen.Unknown;
        }
        return Focus(frame, ["left", "right"]) >= 0 ? GameScreen.Ready : GameScreen.Overview;
    }

    public int GridFocus(Bitmap frame)
    {
        var grid = profile.Zones["parts"];
        var keys = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            string name = "__grid" + i;
            keys.Add(name);
            profile.Zones[name] = new(grid.X + i * grid.W / 7, grid.Y, grid.W / 7 * .96, grid.H);
        }
        try
        {
            return Focus(frame, keys);
        }
        finally
        {
            foreach (var key in keys)
                profile.Zones.Remove(key);
        }
    }

    public int Focus(Bitmap frame, IReadOnlyList<string> zones)
    {
        var matches = new List<int>();
        for (int i = 0; i < zones.Count; i++)
        {
            var r = profile.Zones[zones[i]].Pixels(frame.Size);
            // Adjacent cards share one border. Require both vertical edges, away from corners.
            bool Edge(int edge)
            {
                double best = 0;
                for (int x = Math.Max(0, edge - 8); x < Math.Min(frame.Width, edge + 9); x++)
                {
                    int yellow = 0,
                        total = 0;
                    for (int y = r.Y + r.Height / 5; y < r.Bottom - r.Height / 5; y += 3)
                    {
                        var p = frame.GetPixel(x, y);
                        total++;
                        if (p.R > 165 && p.G > 150 && p.B < 135)
                            yellow++;
                    }
                    best = Math.Max(best, (double)yellow / Math.Max(1, total));
                }
                return best > .72;
            }
            if (Edge(r.X) && Edge(r.Right))
                matches.Add(i);
        }
        int selected = matches.Count == 1 ? matches[0] : -1;
        trace?.Write(
            "focus",
            new
            {
                zones,
                selectedIndex = selected,
                matches,
            }
        );
        return selected;
    }

    public async Task<int> PopupSide(Bitmap f)
    {
        for (int side = 0; side < 2; side++)
        {
            string zone = side == 0 ? "topLeft" : "topRight";
            if (TryName(f, zone, out _))
                return side;
            bool card = LooksLikeCard(f, zone);
            // Structural check: a yellow frame on the top or middle row exists only in the roll menu,
            // so the menu is recognized even when the new perk's name is unreadable.
            if (card)
            {
                int focus = Focus(
                    f,
                    side == 0 ? ["topLeft", "middleLeft", "left"] : ["topRight", "middleRight", "right"]
                );
                if (focus is 0 or 1)
                {
                    trace?.Write("popup_structure", new { side, focus });
                    return side;
                }
            }
            if (profile.FastRecognition && !card)
                continue;
            try
            {
                await ReadName(f, zone);
                return side;
            }
            catch (InvalidOperationException) { }
        }
        return -1;
    }

    public async Task<string> SetName(Bitmap f)
    {
        var raw = Recognition.Normalize(await Text(f, "set"));
        var names = catalog.Sets.Where(s => raw.Contains(Recognition.Normalize(s.Title))).ToList();
        if (names.Count != 1)
            throw new InvalidOperationException("Не розпізнано сет деталі. Перевірте зону назви сету.");
        return names[0].Title;
    }

    public async Task<string> PartName(Bitmap f)
    {
        string title = (await Text(f, "part")).Trim();
        if (
            !System.Text.RegularExpressions.Regex.IsMatch(
                title,
                @"\b(LEGENDARY|EPIC|RARE|COMMON)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
            || title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2
        )
            throw new InvalidOperationException("Не розпізнано заголовок деталі. Відкрийте екран RECALIBER.");
        return Recognition.Normalize(title);
    }
}

public sealed class Runner : IBuildGame
{
    readonly Catalog catalog;
    readonly Profile p;
    readonly IntPtr window;
    readonly Vision vision;
    readonly Action<string> log;
    readonly Rectangle originalBounds;
    readonly SessionLog? trace;
    Input? input;
    int rolls,
        gridIndex = -1;
    RunLedger ledger;
    PartView? lastPart;
    bool autoResolved;
    readonly Action<Build>? autoApplied;
    Bitmap? lastFrame;
    long pausedMs;

    public Runner(
        Catalog c,
        Profile profile,
        IntPtr w,
        Action<string> status,
        SessionLog? diagnostics = null,
        Action<Build>? onAutoSet = null
    )
    {
        catalog = c;
        p = profile;
        window = w;
        trace = diagnostics;
        autoApplied = onAutoSet;
        vision = new(c, p, trace);
        ledger = trace?.Ledger ?? new RunLedger();
        log = status;
        originalBounds = Desktop.Bounds(w);
    }

    // The runner owns the frames: each capture disposes the previous one, and the last frame is handed
    // to the session log when the run ends so it can be saved on failure without copying every frame.
    async Task<Bitmap> Frame(
        CancellationToken ct,
        string[]? zones = null,
        [System.Runtime.CompilerServices.CallerMemberName] string stage = ""
    )
    {
        await EnsureFocus(ct);
        if (Desktop.Bounds(window) != originalBounds)
            throw new InvalidOperationException("Змінилися розмір або положення гри.");
        Rectangle? region = zones == null ? null : Region(zones);
        var frame = Desktop.Capture(window, region);
        lastFrame?.Dispose();
        lastFrame = frame;
        trace?.Observe(frame, stage, retain: false, region: region);
        return frame;
    }

    // Union of the requested zones with a margin that covers the focus-edge scan and OCR fallback crops.
    Rectangle Region(string[] zones)
    {
        var full = new Rectangle(Point.Empty, originalBounds.Size);
        Rectangle? union = null;
        foreach (string name in zones)
        {
            if (!p.Zones.TryGetValue(name, out var zone))
                continue;
            var r = zone.Pixels(full.Size);
            union = union == null ? r : Rectangle.Union(union.Value, r);
        }
        if (union == null)
            return full;
        var area = union.Value;
        area.Inflate(24, 24);
        return Rectangle.Intersect(area, full);
    }

    bool GameFocused => Desktop.GetForegroundWindow() == window;

    // Pause instead of abort: no key is sent while the game is in the background, and the run continues
    // once focus returns. State is re-verified by the next screen read, so a stale frame is never trusted.
    async Task EnsureFocus(CancellationToken ct)
    {
        if (!Desktop.IsGame(window))
            throw new InvalidOperationException("Обране вікно не належить The Crew Motorfest.");
        if (GameFocused)
            return;
        int waitSeconds = p.FocusLostWaitSeconds;
        if (waitSeconds <= 0)
            throw new InvalidOperationException("Зупинено: гра втратила фокус.");
        var wait = System.Diagnostics.Stopwatch.StartNew();
        trace?.Write("focus_lost", new { waitSeconds });
        log($"Гра втратила фокус. Натискання зупинено; чекаю повернення до {waitSeconds} с · F10 — скасувати");
        try
        {
            while (!GameFocused)
            {
                if (wait.Elapsed.TotalSeconds >= waitSeconds)
                {
                    trace?.Write("focus_wait_timeout", new { waitSeconds });
                    throw new InvalidOperationException($"Зупинено: гра не повернула фокус за {waitSeconds} с.");
                }
                await Task.Delay(250, ct);
            }
            // Let the game redraw after regaining focus before any frame is trusted.
            await Task.Delay(400, ct);
        }
        finally
        {
            pausedMs += wait.ElapsedMilliseconds;
        }
        trace?.Write("focus_resumed", new { pausedMs = wait.ElapsedMilliseconds });
        log("Фокус повернувся. Перевіряю екран і продовжую.");
    }

    async Task<T> Until<T>(
        Func<Bitmap, Task<T>> read,
        Func<T, T, bool> same,
        CancellationToken ct,
        string[]? zones = null,
        [System.Runtime.CompilerServices.CallerMemberName] string stage = "",
        int timeoutMs = 9000
    )
    {
        int attempt = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long pausedBefore = pausedMs;
        T? previous = default;
        bool have = false;
        Exception? last = null;
        trace?.Write("state_wait", new { stage });
        // Time spent paused for lost focus does not count against the timeout.
        while (clock.ElapsedMilliseconds - (pausedMs - pausedBefore) < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var frame = await Frame(ct, zones, stage + "-attempt-" + (++attempt));
                var value = await read(frame);
                ct.ThrowIfCancellationRequested();
                if (!GameFocused)
                {
                    // Another window may have covered the capture; discard it and wait for focus.
                    have = false;
                    trace?.Write(
                        "state_retry",
                        new
                        {
                            stage,
                            attempt,
                            error = "focus lost during read",
                        }
                    );
                    await EnsureFocus(ct);
                    continue;
                }
                if (have && same(previous!, value))
                {
                    trace?.Write("state_confirmed", new { stage, attempt });
                    return value;
                }
                previous = value;
                have = true;
            }
            catch (InvalidOperationException e)
            {
                last = e;
                have = false;
                trace?.Write(
                    "state_retry",
                    new
                    {
                        stage,
                        attempt,
                        error = e.Message,
                    }
                );
            }
            await Task.Delay(p.FastRecognition ? 50 : 120, ct);
        }
        trace?.Write(
            "state_timeout",
            new
            {
                stage,
                attempt,
                error = last?.ToString(),
            }
        );
        throw new InvalidOperationException($"Не підтверджено екран ({stage}) за {timeoutMs} мс. " + last?.Message);
    }

    async Task<GameScreen> Screen(CancellationToken ct) =>
        await Until(
            async f =>
            {
                var screen = await vision.ScreenState(f);
                trace?.Write("screen", new { screen = screen.ToString() });
                if (screen == GameScreen.Unknown)
                    throw new InvalidOperationException("Очікую сім деталей, огляд перків або меню кидка.");
                return screen;
            },
            (a, b) => a == b,
            ct
        );

    public async Task Run(CancellationToken ct)
    {
        try
        {
            using var device = new Input(window, p, trace);
            input = device;
            await new BuildWorkflow(this, p, log).Run(ct);
            log($"Кидків за запуск: {rolls}");
        }
        finally
        {
            if (trace != null)
                trace.Retain(lastFrame);
            else
                lastFrame?.Dispose();
            lastFrame = null;
            log(ledger.Text());
        }
    }

    public async Task Prepare(CancellationToken ct)
    {
        var screen = await Screen(ct);
        if (screen == GameScreen.Parts)
        {
            await OpenFromGrid(ct);
            return;
        }
        if (screen == GameScreen.Inventory)
        {
            input!.ValidateRecalibrate();
            await Press("Recalibrate", ct);
            await Press("Confirm", ct);
            screen = await Screen(ct);
        }
        if (screen == GameScreen.Overview)
        {
            log("Перки видно без рамки. Активую вибір.");
            await Press("Confirm", ct);
            screen = await Screen(ct);
        }
        if (screen is GameScreen.PopupLeft or GameScreen.PopupRight)
            await RecoverPopup(screen == GameScreen.PopupLeft ? 0 : 1, ct);
        await ReadPart(ct);
    }

    async Task OpenFromGrid(CancellationToken ct)
    {
        gridIndex = await Until(
            async frame =>
            {
                if (await vision.ScreenState(frame) != GameScreen.Parts)
                    throw new InvalidOperationException("Очікую екран семи деталей перед відкриттям.");
                int index = vision.GridFocus(frame);
                if (index < 0)
                    throw new InvalidOperationException("Очікую стабільну рамку вибраної деталі.");
                return index;
            },
            (a, b) => a == b,
            ct
        );
        input!.ValidateRecalibrate();
        log($"Відкриваю деталь {gridIndex + 1}: Enter → F9 → Enter");
        foreach (string action in NavigationRoute.Open)
            await Press(action, ct);
        var screen = await Screen(ct);
        if (screen == GameScreen.Overview)
        {
            await Press("Confirm", ct);
            screen = await Screen(ct);
        }
        if (screen is GameScreen.PopupLeft or GameScreen.PopupRight)
            await RecoverPopup(screen == GameScreen.PopupLeft ? 0 : 1, ct);
        await ReadPart(ct);
    }

    async Task<PartView> Part(CancellationToken ct, bool popup = false)
    {
        if (p.AutoSet && !autoResolved)
        {
            string detected = await Until(
                async frame =>
                {
                    await vision.PartName(frame);
                    return await vision.SetName(frame);
                },
                (a, b) => a == b,
                ct,
                ["part", "set"]
            );
            var choice = AutoSetResolver.Resolve(catalog, detected);
            p.Set = detected;
            p.First = choice.Affixes[0];
            p.Second = choice.Affixes[1];
            autoResolved = true;
            trace?.SaveEffectiveProfile(p);
            trace?.Write(
                "auto_set_detected",
                new
                {
                    set = detected,
                    preset = choice.Title,
                    first = p.First,
                    second = p.Second,
                }
            );
            log($"Автосет: {detected} → {p.First} + {p.Second}. Профіль: {choice.Title}.");
            autoApplied?.Invoke(choice);
        }
        return await Until(
            async frame =>
            {
                if (!popup && await vision.PopupSide(frame) >= 0)
                    throw new InvalidOperationException("Меню кидка ще закривається.");
                string title = await vision.PartName(frame);
                string set = await vision.SetName(frame);
                if (set != p.Set)
                    throw new InvalidOperationException($"Деталь має сет {set}, очікується {p.Set}.");
                var cards = new PerkCard[2];
                for (int i = 0; i < 2; i++)
                {
                    string zone = i == 0 ? "left" : "right";
                    cards[i] = await vision.Card(frame, zone);
                    if ((cards[i].Name == p.First || cards[i].Name == p.Second) && cards[i].Stats == null)
                        cards[i] = new(cards[i].Name, await vision.Read(frame, zone));
                }
                int focus = vision.Focus(frame, ["left", "right"]);
                if (focus < 0 && !popup)
                    throw new InvalidOperationException("Очікую жовту рамку вибору перка.");
                return new PartView(title, cards, focus);
            },
            (a, b) =>
                a.Id == b.Id && a.Focus == b.Focus && a.Cards.Zip(b.Cards).All(v => PerkCard.Same(v.First, v.Second)),
            ct,
            ["part", "set", "left", "right", "topLeft", "topRight"]
        );
    }

    public async Task<PartView> ReadPart(CancellationToken ct)
    {
        lastPart = await Part(ct);
        return lastPart;
    }

    public async Task<PerkCard> Roll(
        int slot,
        PerkCard current,
        RollPhase phase,
        string[] targets,
        CancellationToken ct
    )
    {
        if (rolls >= p.MaxRolls)
            throw new InvalidOperationException($"Досягнуто ліміту {p.MaxRolls} кидків. Прогрес залишився в грі.");
        await FocusMain(slot, ct);
        int rollId = ledger.Request(lastPart?.Id ?? "unknown", slot, phase);
        try
        {
            await Press("Confirm", ct);
        }
        catch
        {
            if (input?.LastSent != true)
                ledger.NotSent(rollId);
            throw;
        }
        var paidDeadline = DateTime.UtcNow.AddSeconds(9);
        bool paid = false;
        while (DateTime.UtcNow < paidDeadline)
        {
            ct.ThrowIfCancellationRequested();
            var paidFrame = await Frame(ct, ["topLeft", "topRight"], "paid-roll-observation");
            if (await vision.PopupSide(paidFrame) == slot)
            {
                ledger.Confirm(rollId);
                paid = true;
                break;
            }
            await Task.Delay(p.FastRecognition ? 50 : 100, ct);
        }
        if (!paid)
            throw new InvalidOperationException(
                "Новий оплачений кидок не підтверджено. Натискання записано як непідтверджене."
            );
        rolls = ledger.Confirmed;
        log(
            $"Кидок {rolls} · слот {slot + 1} · "
                + (phase == RollPhase.Search ? $"пошук {string.Join(" / ", targets)}" : $"максимум {current.Name}")
        );
        string[] rows = Rows(slot);
        var choice = await Selection(rows, current, phase, targets, ct);
        ledger.Offer(
            new RollSample(
                rollId,
                DateTimeOffset.Now,
                lastPart?.Id ?? "unknown",
                slot + 1,
                phase.ToString(),
                targets,
                lastPart?.Cards[1 - slot].Name,
                choice.Fresh,
                choice.Reroll,
                choice.Current,
                choice.Index + 1,
                choice.Expected,
                trace?.CurrentFrame ?? "none"
            )
        );
        log($"Варіант {choice.Index + 1}: {choice.Expected}");
        await FocusRows(rows, choice.Index, ct);
        await Press("Confirm", ct);
        var saved = await ReadPart(ct);
        if (!PerkCard.Same(saved.Cards[slot], choice.Expected))
            throw new InvalidOperationException("Гра не підтвердила обраний варіант.");
        ledger.Saved(rollId, saved.Cards[slot]);
        log("Збережено: " + saved.Cards[slot]);
        return saved.Cards[slot];
    }

    Task<PopupSelection> Selection(
        string[] rows,
        PerkCard current,
        RollPhase phase,
        string[] targets,
        CancellationToken ct
    ) =>
        Until(
            f => vision.Select(f, rows, current, phase, targets),
            (a, b) => a.Index == b.Index && PerkCard.Same(a.Expected, b.Expected),
            ct,
            rows
        );

    static string[] Rows(int slot) =>
        slot == 0 ? ["topLeft", "middleLeft", "left"] : ["topRight", "middleRight", "right"];

    async Task RecoverPopup(int side, CancellationToken ct)
    {
        var part = await Part(ct, true);
        var current = part.Cards[side];
        string[] wanted = [p.First, p.Second];
        PopupSelection choice;
        if (wanted.Contains(current.Name))
        {
            bool both =
                part.Cards.Select(c => c.Name).Distinct().Count() == 2 && part.Cards.All(c => wanted.Contains(c.Name));
            choice =
                both && !current.Maxed
                    ? await Selection(Rows(side), current, RollPhase.Maximize, [current.Name], ct)
                    : new(2, current, RollPhase.Search);
        }
        else
            choice = await Selection(Rows(side), current, RollPhase.Search, wanted, ct);
        ledger.Resume(part.Id, side, choice);
        log($"Продовжую відкритий кидок · слот {side + 1} · варіант {choice.Index + 1}: {choice.Expected}");
        await FocusRows(Rows(side), choice.Index, ct);
        await Press("Confirm", ct);
        var actual = await ReadPart(ct);
        if (!PerkCard.Same(actual.Cards[side], choice.Expected))
            throw new InvalidOperationException("Не підтверджено результат відкритого кидка.");
    }

    async Task BackWithPause(CancellationToken ct)
    {
        await Press("Back", ct);
        trace?.Write(
            "navigation_wait",
            new
            {
                action = "Back",
                reason = "exit-menu-animation",
                delayMs = p.ExitPauseMs,
            }
        );
        await Task.Delay(p.ExitPauseMs, ct);
    }

    async Task<int> InventoryCategory(CancellationToken ct, int expected)
    {
        return await Until(
            async frame =>
            {
                if (await vision.ScreenState(frame) != GameScreen.Inventory)
                    throw new InvalidOperationException("Очікую список доступних деталей.");
                int category = PartTransition.Category(await vision.Text(frame, "inventoryHeader"));
                if (category < 0 || category != expected)
                    throw new InvalidOperationException(
                        "Не підтверджено категорію " + PartTransition.Categories[expected] + " у списку деталей."
                    );
                return category;
            },
            (a, b) => a == b,
            ct,
            timeoutMs: 3000
        );
    }

    public async Task Advance(CancellationToken ct)
    {
        int previous = PartTransition.Category(lastPart?.Id ?? "");
        if (previous < 0)
        {
            log("Категорію для швидкого переходу не визначено. Використовую класичний маршрут.");
            await ExitToParts(ct);
            await NextPart(ct);
            return;
        }
        int expected = (previous + 1) % 7;
        await PartTransition.Run(
            p.FastPartNavigation,
            async () =>
            {
                trace?.Write(
                    "navigation_route",
                    new
                    {
                        route = "quick",
                        from = previous,
                        to = expected,
                    }
                );
                log("Швидкий перехід: Esc ×2 → вниз → Crafting → Enter");
                await BackWithPause(ct);
                await BackWithPause(ct);
                await InventoryCategory(ct, previous);
                await Press("Down", ct);
                await InventoryCategory(ct, expected);
                input!.ValidateRecalibrate();
                await Press("Recalibrate", ct);
                await Press("Confirm", ct);
                var screen = await Screen(ct);
                if (screen == GameScreen.Overview)
                {
                    await Press("Confirm", ct);
                    screen = await Screen(ct);
                }
                if (screen != GameScreen.Ready)
                    throw new InvalidOperationException("Після Crafting не відкрився екран двох перків.");
                var next = await ReadPart(ct);
                if (PartTransition.Category(next.Id) != expected)
                    throw new InvalidOperationException("Швидкий перехід відкрив іншу категорію деталі.");
                gridIndex = expected;
                trace?.Write("navigation_quick_confirmed", new { expected, part = next.Id });
            },
            async () =>
            {
                await ExitToParts(ct);
                await NextPart(ct);
            },
            async () =>
            {
                await RecoverToGrid(ct);
                await SelectGridCategory(expected, ct);
                await OpenFromGrid(ct);
                if (PartTransition.Category(lastPart?.Id ?? "") != expected)
                    throw new InvalidOperationException("Резервний маршрут не підтвердив очікувану деталь.");
            },
            error =>
            {
                trace?.Write("navigation_fallback", new { expected, error = error.Message });
                log("Швидкий перехід не підтверджено. Повертаюся до класичного маршруту: " + error.Message);
            },
            ct
        );
    }

    async Task RecoverToGrid(CancellationToken ct)
    {
        for (int step = 0; step <= 3; step++)
        {
            var screen = await Until(
                async frame => (await vision.ScreenState(frame), vision.GridFocus(frame)),
                (a, b) => a == b,
                ct,
                timeoutMs: 3000
            );
            if (screen.Item1 == GameScreen.Parts && screen.Item2 >= 0)
            {
                gridIndex = screen.Item2;
                return;
            }
            if (step == 3)
                break;
            await BackWithPause(ct);
        }
        throw new InvalidOperationException("Не вдалося повернутися до екрана семи деталей для резервного маршруту.");
    }

    async Task SelectGridCategory(int target, CancellationToken ct)
    {
        gridIndex = await GridNavigator.Ensure(
            target,
            async () =>
            {
                var frame = await Frame(ct, null, "grid-navigation-read");
                if (await vision.ScreenState(frame) != GameScreen.Parts)
                    return -1;
                return vision.GridFocus(frame);
            },
            Press,
            24,
            75,
            (reason, attempt, index) =>
            {
                trace?.Write(
                    "grid_navigation_retry",
                    new
                    {
                        reason,
                        attempt,
                        index,
                        target,
                    }
                );
                if (reason == "retry-key")
                    log("Стрілка не змінила вибрану деталь. Повторюю один раз.");
            },
            ct
        );
    }

    public async Task ExitToParts(CancellationToken ct)
    {
        log($"Повертаюся до семи деталей: Esc ×3 з додатковою паузою {p.ExitPauseMs} мс після кожного натискання");
        foreach (string action in NavigationRoute.Exit)
        {
            await BackWithPause(ct);
        }
        gridIndex = await Until(
            async frame =>
            {
                if (await vision.ScreenState(frame) != GameScreen.Parts)
                    throw new InvalidOperationException("Очікую екран семи деталей після Esc ×3.");
                int index = vision.GridFocus(frame);
                if (index < 0)
                    throw new InvalidOperationException("Не знайдено рамку деталі.");
                return index;
            },
            (a, b) => a == b,
            ct
        );
    }

    public async Task NextPart(CancellationToken ct)
    {
        if (gridIndex < 0)
            throw new InvalidOperationException("Спочатку треба повернутися до семи деталей.");
        int expected = (gridIndex + 1) % 7;
        await SelectGridCategory(expected, ct);
        await OpenFromGrid(ct);
    }

    async Task Press(string action, CancellationToken ct)
    {
        await EnsureFocus(ct);
        trace?.Write("input_context", new { action, rolls });
        await input!.Press(action, ct);
        await EnsureFocus(ct);
        // The after-press frame is purely diagnostic; skip the capture once the PNG budget is spent.
        if (trace?.ScreenshotBudgetAvailable == true)
            await Frame(ct, null, "after-" + action);
    }

    async Task FocusMain(int desired, CancellationToken ct)
    {
        var current = await Frame(ct, ["left", "right"]);
        int focus = vision.Focus(current, ["left", "right"]);
        if (focus < 0)
            focus = await Until(
                frame =>
                {
                    int value = vision.Focus(frame, ["left", "right"]);
                    if (value < 0)
                        throw new InvalidOperationException("Очікую рамку перка.");
                    return Task.FromResult(value);
                },
                (a, b) => a == b,
                ct,
                ["left", "right"]
            );
        if (focus == desired)
            return;
        await Press(desired == 0 ? "Left" : "Right", ct);
        await Until(
            frame =>
            {
                int value = vision.Focus(frame, ["left", "right"]);
                if (value != desired)
                    throw new InvalidOperationException("Не підтверджено вибір слота.");
                return Task.FromResult(value);
            },
            (a, b) => a == b,
            ct,
            ["left", "right"]
        );
    }

    async Task FocusRows(string[] rows, int desired, CancellationToken ct)
    {
        await FocusNavigator.Ensure(
            desired,
            async () =>
            {
                var frame = await Frame(ct, rows, "FocusRows-read");
                return vision.Focus(frame, rows);
            },
            Press,
            p.FocusReadAttempts,
            p.FocusRetryMs,
            (reason, attempt, focus) =>
            {
                trace?.Write(
                    "focus_retry",
                    new
                    {
                        reason,
                        attempt,
                        limit = p.FocusReadAttempts,
                        retryMs = p.FocusRetryMs,
                        selectedIndex = focus,
                        desiredIndex = desired,
                        rows,
                    }
                );
                if (reason == "retry-navigation-key")
                    log("Рамка залишилася на старому рядку після очікування. Повторюю стрілку один раз.");
                else if (attempt == 1 || attempt == p.FocusReadAttempts)
                    log($"Рамка: повторне зчитування {attempt}/{p.FocusReadAttempts}; очікую варіант {desired + 1}.");
            },
            ct
        );
    }
}
