using System.Diagnostics;
using System.Text.Json;

namespace TunerFest;

public static class VisionTraining
{
    record Sample(string Frame, string Zone, Rectangle Bounds, string Name, string Raw);

    public record LoggedCard(string File, string Zone, Rectangle Bounds, string Name, string Raw);

    public static List<LoggedCard> Read(string session, Catalog catalog)
    {
        var files = new Dictionary<string, string>();
        var zones = new Dictionary<(string, string), Rectangle>();
        var samples = new Dictionary<(string, string), Sample>();
        foreach (string line in File.ReadLines(Path.Combine(session, "events.jsonl")))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }
            using (doc)
            {
                var e = doc.RootElement;
                string kind = e.GetProperty("kind").GetString()!;
                string frame = e.GetProperty("frame").GetString()!;
                var data = e.GetProperty("data");
                if (kind == "screenshot_saved")
                {
                    string label = data.GetProperty("label").GetString()!;
                    if (label.StartsWith("ocr-crop"))
                        continue;
                    string source = data.GetProperty("sourceFrame").GetString()!;
                    string file = Path.Combine(session, data.GetProperty("file").GetString()!);
                    if (File.Exists(file))
                        files[source] = file;
                }
                else if (kind == "ocr_zone")
                {
                    string zone = data.GetProperty("zone").GetString()!;
                    var b = data.GetProperty("bounds");
                    zones[(frame, zone)] = new(
                        b.GetProperty("X").GetInt32(),
                        b.GetProperty("Y").GetInt32(),
                        b.GetProperty("Width").GetInt32(),
                        b.GetProperty("Height").GetInt32()
                    );
                }
                else if (kind == "ocr" && data.GetProperty("variant").GetString() == "original")
                {
                    string zone = data.GetProperty("zone").GetString()!;
                    if (
                        zone is not ("left" or "right" or "topLeft" or "middleLeft" or "topRight" or "middleRight")
                        || !zones.TryGetValue((frame, zone), out var bounds)
                    )
                        continue;
                    if (
                        data.GetProperty("width").GetInt32() != bounds.Width
                        || data.GetProperty("height").GetInt32() != bounds.Height
                    )
                        continue;
                    string raw = data.GetProperty("text").GetString()!;
                    try
                    {
                        string name = Recognition.Name(raw, catalog).Title;
                        if (
                            Recognition.Normalize(raw).IndexOf(Recognition.Normalize(name), StringComparison.Ordinal)
                            > 4
                        )
                            continue;
                        samples[(frame, zone)] = new(frame, zone, bounds, name, raw);
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
        return samples
            .Values.Where(s => files.ContainsKey(s.Frame))
            .Select(s => new LoggedCard(files[s.Frame], s.Zone, s.Bounds, s.Name, s.Raw))
            .ToList();
    }

    public static async Task<int> Train(string logs, string output)
    {
        var catalog = Catalog.Load();
        var sessions = Directory
            .GetDirectories(logs)
            .Where(d => File.Exists(Path.Combine(d, "session.json")))
            .Where(d =>
                JsonDocument
                    .Parse(File.ReadAllText(Path.Combine(d, "session.json")))
                    .RootElement.GetProperty("mode")
                    .GetString() == "automation"
            )
            .Order()
            .ToArray();
        if (sessions.Length < 2)
            throw new InvalidOperationException("Need separate training and held-out sessions");
        var model = new VisionTemplates();
        var training = new List<string>();
        int trainingCards = 0;
        foreach (string session in sessions[..^1])
        {
            training.Add(Path.GetFileName(session));
            var samples = Read(session, catalog);
            trainingCards += samples.Count;
            foreach (var group in samples.GroupBy(s => s.File))
            {
                using var source = new Bitmap(group.Key);
                using var image = new Bitmap(
                    source.Width,
                    source.Height,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb
                );
                using (var g = Graphics.FromImage(image))
                    g.DrawImageUnscaled(source, 0, 0);
                foreach (var sample in group)
                    model.Learn(image, sample.Bounds, sample.Name);
            }
        }
        var heldOut = Read(sessions[^1], catalog);
        int matches = 0,
            wrong = 0,
            fallbacks = 0,
            numericChecked = 0,
            numericWrong = 0;
        var numericProfile = new Profile { FastRecognition = true };
        var numericVision = new Vision(catalog, numericProfile, null, model);
        double totalMs = 0;
        var failures = new List<object>();
        foreach (var group in heldOut.GroupBy(s => s.File))
        {
            using var source = new Bitmap(group.Key);
            using var image = new Bitmap(
                source.Width,
                source.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb
            );
            using (var g = Graphics.FromImage(image))
                g.DrawImageUnscaled(source, 0, 0);
            foreach (var sample in group)
            {
                var timer = Stopwatch.StartNew();
                bool found = model.Match(image, sample.Bounds, out var name, out double confidence);
                totalMs += timer.Elapsed.TotalMilliseconds;
                Reading? reference = null;
                try
                {
                    reference = Recognition.Parse(sample.Raw, catalog);
                }
                catch (InvalidOperationException) { }
                if (reference != null)
                {
                    numericChecked++;
                    numericProfile.Zones[sample.Zone] = new(
                        (double)sample.Bounds.X / image.Width,
                        (double)sample.Bounds.Y / image.Height,
                        (double)sample.Bounds.Width / image.Width,
                        (double)sample.Bounds.Height / image.Height
                    );
                    try
                    {
                        var result = await numericVision.Card(image, sample.Zone, true);
                        if (result.Stats == null || !Recognition.Equal(reference, result.Stats))
                        {
                            numericWrong++;
                            failures.Add(
                                new
                                {
                                    sample.File,
                                    sample.Zone,
                                    expected = reference.ToString(),
                                    actual = result.ToString(),
                                    type = "percentages",
                                }
                            );
                        }
                    }
                    catch (InvalidOperationException e)
                    {
                        numericWrong++;
                        failures.Add(
                            new
                            {
                                sample.File,
                                sample.Zone,
                                error = e.Message,
                                type = "percentages",
                            }
                        );
                    }
                }
                if (!found)
                {
                    fallbacks++;
                    continue;
                }
                matches++;
                if (name != sample.Name)
                {
                    wrong++;
                    failures.Add(
                        new
                        {
                            sample.File,
                            sample.Zone,
                            expected = sample.Name,
                            actual = name,
                            confidence,
                        }
                    );
                }
            }
        }
        if (wrong == 0 && numericWrong == 0)
            model.Save(output);
        var report = new
        {
            trainingSessions = training,
            trainingCards,
            patterns = model.Patterns.Count,
            trainedNames = model.Patterns.Select(p => p.Name).Distinct().Order().ToArray(),
            heldOutSession = Path.GetFileName(sessions[^1]),
            heldOutCards = heldOut.Count,
            matches,
            wrong,
            fallbacks,
            numericChecked,
            numericWrong,
            meanTemplateMs = totalMs / Math.Max(1, heldOut.Count),
            failures,
            modelSaved = wrong == 0 && numericWrong == 0,
        };
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "vision-training.json"),
            JsonSerializer.Serialize(report, Catalog.JsonOptions)
        );
        if (wrong > 0 || numericWrong > 0)
            return 1;
        // Time OCR and the trained matcher on representative held-out cards. No game input.
        var bench = heldOut.GroupBy(s => s.Name).Select(g => g.First()).Take(20).ToArray();
        double oldMs = 0,
            newMs = 0,
            oldCardMs = 0,
            newCardMs = 0,
            oldRepeatMs = 0,
            newRepeatMs = 0;
        int agree = 0,
            cardAgree = 0;
        foreach (var sample in bench)
        {
            using var source = new Bitmap(sample.File);
            using var image = new Bitmap(
                source.Width,
                source.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb
            );
            using (var g = Graphics.FromImage(image))
                g.DrawImageUnscaled(source, 0, 0);
            var zone = new Zone(
                (double)sample.Bounds.X / image.Width,
                (double)sample.Bounds.Y / image.Height,
                (double)sample.Bounds.Width / image.Width,
                (double)sample.Bounds.Height / image.Height
            );
            var p1 = new Profile { FastRecognition = false };
            p1.Zones[sample.Zone] = zone;
            var p2 = new Profile { FastRecognition = true };
            p2.Zones[sample.Zone] = zone;
            var a = new Vision(catalog, p1);
            var b = new Vision(catalog, p2, null, model);
            var timer = Stopwatch.StartNew();
            var before = await a.ReadName(image, sample.Zone);
            oldMs += timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var after = await b.ReadName(image, sample.Zone);
            newMs += timer.Elapsed.TotalMilliseconds;
            if (before.Title == after.Title && after.Title == sample.Name)
                agree++;
            timer.Restart();
            var oldCard = await a.Card(image, sample.Zone);
            oldCardMs += timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            var newCard = await b.Card(image, sample.Zone);
            newCardMs += timer.Elapsed.TotalMilliseconds;
            if (oldCard.Name == newCard.Name && newCard.Name == sample.Name)
                cardAgree++;
            timer.Restart();
            await a.Card(image, sample.Zone);
            oldRepeatMs += timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            await b.Card(image, sample.Zone);
            newRepeatMs += timer.Elapsed.TotalMilliseconds;
        }
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "vision-benchmark.json"),
            JsonSerializer.Serialize(
                new
                {
                    cards = bench.Length,
                    agree,
                    baselineTotalMs = oldMs,
                    fastTotalMs = newMs,
                    baselineMeanMs = oldMs / Math.Max(1, bench.Length),
                    fastMeanMs = newMs / Math.Max(1, bench.Length),
                    cardAgree,
                    baselineCardMeanMs = oldCardMs / Math.Max(1, bench.Length),
                    fastCardMeanMs = newCardMs / Math.Max(1, bench.Length),
                    baselineRepeatMeanMs = oldRepeatMs / Math.Max(1, bench.Length),
                    fastRepeatMeanMs = newRepeatMs / Math.Max(1, bench.Length),
                    scope = "Decoded in-memory held-out cards, matching live screen buffers. Name/card/repeated-card recognition only; game animations and input delays excluded.",
                },
                Catalog.JsonOptions
            )
        );
        return agree == bench.Length && cardAgree == bench.Length ? 0 : 1;
    }
}
