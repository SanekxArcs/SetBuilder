using System.Drawing.Imaging;

namespace TunerFest;

public static class Verification
{
    public static async Task<int> RecognitionCache(string first, string second)
    {
        var p = new Profile { FastRecognition = true };
        var c = Catalog.Load();
        var session = new SessionLog(p, "offline-recognition-cache", new { simulated = true, noGameInput = true });
        using var a = new Bitmap(first);
        using var b = new Bitmap(second);
        var v = new Vision(c, p, session);
        try
        {
            session.Observe(a, "first-4.4");
            var one = await v.Card(a, "left", true);
            var two = await v.Card(a, "left", true);
            session.Observe(b, "changed-6");
            var three = await v.Card(b, "left", true);
            if (one.Stats?.Value != 4.4 || two.Stats?.Value != 4.4 || three.Stats?.Value != 6)
                throw new Exception("Recognition cache returned stale percentages");
            await session.Finish("completed");
        }
        catch (Exception e)
        {
            await session.Finish("error", e);
            throw;
        }
        var lines = File.ReadAllText(Path.Combine(session.Folder, "events.jsonl"));
        if (!lines.Contains("pixel-cache"))
            throw new Exception("Identical pixels did not use OCR cache");
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "recognition-cache-test.txt"),
            "PASS repeated card reuses OCR\nPASS changed 4.4% -> 6% invalidates cached reading\nPASS percentage maxima remain validated\n"
        );
        return 0;
    }

    public static async Task<int> Diagnostics(string path)
    {
        string ReadActive(string file)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var checks = new List<string>();
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception("Diagnostics: " + label);
            checks.Add("PASS " + label);
        }
        var c = Catalog.Load();
        var p = new Profile();
        using var frame = new Bitmap(path);
        var session = new SessionLog(p, "offline-verification", new { simulated = true, noGameInput = true });
        try
        {
            session.Message("Офлайн-перевірка діагностики. Жодних натискань у грі.");
            Check(
                ReadActive(Path.Combine(session.Folder, "session.log")).Contains("Офлайн"),
                "text is readable immediately while recording"
            );
            session.Observe(frame, "offline-reference");
            var v = new Vision(c, p, session);
            var current = await v.Read(frame, "left");
            await v.Decide(frame, ["topLeft", "middleLeft", "left"], current, "Clean Driver");
            Check(
                ReadActive(Path.Combine(session.Folder, "events.jsonl")).Contains("ocr_parse_failed"),
                "OCR failure and fallback are persisted during recording"
            );
            var card = new PerkCard(current.Affix.Title, current);
            var sampleId = session.Ledger.Request("OFFLINE", 0, RollPhase.Search);
            session.Ledger.Confirm(sampleId);
            session.Ledger.Confirm(sampleId);
            session.Ledger.Offer(
                new RollSample(
                    sampleId,
                    DateTimeOffset.Now,
                    "OFFLINE",
                    1,
                    "Search",
                    ["Rich"],
                    "Bulldozer",
                    new("Clean Driver", null),
                    card,
                    card,
                    1,
                    new("Clean Driver", null),
                    session.CurrentFrame
                )
            );
            Check(File.Exists(Path.Combine(session.Folder, "spending.json")), "spending is saved during the run");
            await session.Finish("completed");
        }
        catch (Exception e)
        {
            await session.Finish("error", e);
            throw;
        }
        var lines = File.ReadAllLines(Path.Combine(session.Folder, "events.jsonl"))
            .Select(l => System.Text.Json.JsonDocument.Parse(l))
            .ToList();
        try
        {
            Check(
                lines.Any(l => l.RootElement.GetProperty("kind").GetString() == "decision"),
                "selected option and expected percentages recorded"
            );
            Check(
                lines.Any(l => l.RootElement.GetProperty("kind").GetString() == "reading"),
                "validated readings recorded"
            );
            var images = lines
                .Where(l => l.RootElement.GetProperty("kind").GetString() == "screenshot_saved")
                .Select(l => l.RootElement.GetProperty("data").GetProperty("file").GetString()!)
                .ToArray();
            Check(
                images.Length > 0 && images.All(f => File.Exists(Path.Combine(session.Folder, f))),
                "all saved screenshot references resolve"
            );
            using var png = new Bitmap(Path.Combine(session.Folder, images[0]));
            Check(png.Size == frame.Size, "full-resolution game PNG is readable");
            Check(
                lines
                    .Select(l => l.RootElement.GetProperty("sequence").GetInt32())
                    .SequenceEqual(Enumerable.Range(1, lines.Count)),
                "event sequence is contiguous"
            );
        }
        finally
        {
            foreach (var line in lines)
                line.Dispose();
        }
        using (
            var summary = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(session.Folder, "summary.json"))
            )
        )
            Check(summary.RootElement.GetProperty("outcome").GetString() == "completed", "successful outcome recorded");
        Check(
            File.Exists(Path.Combine(session.Folder, "settings.json"))
                && File.Exists(Path.Combine(session.Folder, "catalog.json")),
            "profile and catalog snapshots included"
        );
        Check(
            File.ReadAllLines(Path.Combine(session.Folder, "rolls.csv")).Length == 2,
            "one CSV row per observed roll despite duplicate confirmation"
        );
        using (
            var spending = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(session.Folder, "spending.json"))
            )
        )
            Check(
                spending.RootElement.GetProperty("bucksSpent").GetInt64() == 1500
                    && spending.RootElement.GetProperty("sparePartsSpent").GetInt64() == 150,
                "saved spending matches fixed per-roll cost"
            );
        Check(
            File.ReadAllText(Path.Combine(session.Folder, "rolls.jsonl")).Contains("offer"),
            "raw roll history retained separately from screenshot limits"
        );
        var archive = SessionLog.Export(session.Folder);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(archive))
            Check(
                zip.Entries.Any(e => e.FullName == "events.jsonl")
                    && zip.Entries.Any(e => e.FullName.StartsWith("screenshots/")),
                "ZIP contains logs and PNGs"
            );
        using var tiny = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(tiny))
            g.Clear(Color.Gold);
        var limited = new SessionLog(
            new Profile { DiagnosticMaxImages = 1, DiagnosticMaxMb = 1 },
            "offline-verification-error-reserve",
            new { simulated = true }
        );
        limited.Observe(tiny, "first");
        limited.Observe(tiny, "second");
        limited.Observe(tiny, "third");
        await limited.Finish("error", new InvalidOperationException("Intentional self-test error; no game input."));
        Check(
            File.Exists(Path.Combine(limited.Folder, "screenshots", "last-safe-frame.png")),
            "error keeps last frame after normal screenshot limit"
        );
        using (
            var summary = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(limited.Folder, "summary.json"))
            )
        )
            Check(
                summary.RootElement.GetProperty("outcome").GetString() == "error"
                    && summary.RootElement.GetProperty("skipped").GetInt32() > 0
                    && summary.RootElement.GetProperty("error").GetString()!.Contains("Intentional"),
                "error, skipped frames and exception recorded"
            );
        var noImages = new SessionLog(
            new Profile { DiagnosticScreenshots = false },
            "offline-verification-text-only",
            new { simulated = true }
        );
        noImages.Observe(tiny, "not-saved");
        await noImages.Finish("cancelled");
        Check(
            !Directory.EnumerateFiles(Path.Combine(noImages.Folder, "screenshots")).Any(),
            "screenshots can be disabled"
        );
        using (
            var summary = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(noImages.Folder, "summary.json"))
            )
        )
            Check(
                summary.RootElement.GetProperty("outcome").GetString() == "cancelled",
                "cancellation outcome recorded"
            );
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "diagnostics-test.txt"), checks);
        return 0;
    }

    public static async Task<int> Inspect(string path)
    {
        var c = Catalog.Load();
        var p = Profile.Load();
        var v = new Vision(c, p);
        using var frame = new Bitmap(path);
        var result = new Dictionary<string, object>();
        foreach (var zone in p.Zones.Keys)
        {
            var raw = await v.Text(frame, zone);
            string parsed;
            try
            {
                parsed = (await v.Read(frame, zone)).ToString();
            }
            catch (Exception e)
            {
                parsed = e.Message;
            }
            result[zone] = new { raw, parsed };
        }
        try
        {
            string set = await v.SetName(frame);
            var build = AutoSetResolver.Resolve(c, set);
            result["autoSet"] = new
            {
                set,
                preset = build.Title,
                affixes = build.Affixes,
            };
        }
        catch (Exception e)
        {
            result["autoSetError"] = e.Message;
        }
        result["popup"] = await v.PopupSide(frame);
        result["focusMain"] = v.Focus(frame, ["left", "right"]);
        result["focusPopup"] = v.Focus(frame, ["topLeft", "middleLeft", "left"]);
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "inspect-result.json"),
            System.Text.Json.JsonSerializer.Serialize(result, Catalog.JsonOptions)
        );
        return 0;
    }

    // Reads one saved card crop as if it were the "left" zone and compares the result to the expected text.
    public static async Task<int> ValueCrop(string path, string expected)
    {
        var p = new Profile();
        p.Zones["left"] = new(0, 0, 1, 1);
        var session = new SessionLog(p, "offline-value-crop", new { simulated = true, noGameInput = true });
        var v = new Vision(Catalog.Load(), p, session);
        using var crop = new Bitmap(path);
        using var frame = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(frame))
            g.DrawImageUnscaled(crop, 0, 0);
        string actual;
        try
        {
            actual = (await v.Read(frame, "left")).ToString();
        }
        catch (InvalidOperationException e)
        {
            actual = "ERROR: " + e.Message;
        }
        await session.Finish(actual == expected ? "completed" : "error");
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "value-crop-test.txt"),
            (actual == expected ? "PASS " : "FAIL ")
                + Path.GetFileName(path)
                + " -> "
                + actual
                + " (expected "
                + expected
                + ")\n"
        );
        return actual == expected ? 0 : 1;
    }


    // Experiment: OCR the isolated yellow value under several renderings and report what each one returns.
    public static async Task<int> ProbeYellow(string[] paths)
    {
        var p = new Profile { FastRecognition = false };
        var v = new Vision(Catalog.Load(), p);
        var lines = new List<string>();
        foreach (string path in paths)
        {
            using var crop = new Bitmap(path);
            using var isolated = Vision.YellowText(crop, crop.Height * 55 / 100);
            if (isolated == null)
            {
                lines.Add(path + ": no yellow");
                continue;
            }
            lines.Add(path + ": isolated " + isolated.Width + "x" + isolated.Height);
            foreach (var (label, image) in Vision.YellowVariants(isolated, "[2%-4%]"))
            {
                using (image)
                {
                    string text = await v.Text(image);
                    lines.Add($"  {label,-28} {image.Width}x{image.Height} -> '{text}'");
                }
            }
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "probe-yellow.txt"), lines);
        return 0;
    }

    public static async Task<int> Stuck(string path)
    {
        var c = Catalog.Load();
        var p = new Profile();
        var v = new Vision(c, p);
        using var f = new Bitmap(path);
        var current = await v.Read(f, "left");
        var focus = v.Focus(f, ["topLeft", "middleLeft", "left"]);
        var search = await v.Decide(f, ["topLeft", "middleLeft", "left"], current, "Rich");
        var upgrade = await v.Decide(f, ["topLeft", "middleLeft", "left"], current, "Iron Grip");
        var accept = await v.Decide(f, ["topLeft", "middleLeft", "left"], current, "Clean Driver");
        var result = new
        {
            accepted = accept.ToString(),
            current = current.ToString(),
            focus,
            search = search.ToString(),
            upgrade = upgrade.ToString(),
            popup = await v.PopupSide(f),
            cleanDriverRaw = await v.Text(f, "topLeft"),
        };
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "stuck-regression.json"),
            System.Text.Json.JsonSerializer.Serialize(result, Catalog.JsonOptions)
        );
        return
            current.Value == 4.4
            && current.Affix.Title == "Iron Grip"
            && focus == 1
            && search.Index == 2
            && search.Expected.Value == 4.4
            && upgrade.Index == 1
            && upgrade.Expected.Value == 6
            && await v.PopupSide(f) == 0
            && accept.Index == 0
            && accept.Expected.Value == 2.1
            && accept.Expected.Minimum == 1
            && accept.Expected.Maximum == 3
            ? 0
            : 1;
    }

    public static int Tests()
    {
        var c = Catalog.Load();
        var results = new List<string>();
        void Assert(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            results.Add("PASS " + name);
        }
        Reading Parse(string s) => Recognition.Parse(s, c);
        var current = Parse("Iron Grip Decrease GRIP loss by 6% [4%-6%] with Grip loss modifier");
        var fresh = Parse("Rich Increase BUCKS gains by 1% [1%-3%]");
        var roll = Parse("Iron Grip Decrease GRIP loss by 5.5% [4%-6%]");
        Assert(Parse("Skilled Increase scoring by 10% [50/0-100/0]").Maxed, "real screenshot percent glyph correction");
        var trawler = Parse("Trawler Increase SPEED in WATER by 1.3% [l .2%-2%] for ground vehicles");
        Assert(
            trawler.Value == 1.3 && trawler.Minimum == 1.2 && trawler.Maximum == 2,
            "reported Trawler l .2 OCR regression"
        );
        Assert(
            Parse("Trawler Increase SPEED in WATER by I .3% [1 .2%-2%]").Value == 1.3,
            "spaced value and uppercase I correction"
        );
        Assert(Parse("Drifty 1% [O .8%-2%]").Minimum == .8, "zero glyph in range");
        Assert(
            Vision.BreadcrumbIsRecaliber(Recognition.Normalize("PROFILE/VEHICLES/UPGRADE")) == false
                && Vision.BreadcrumbIsRecaliber(Recognition.Normalize("PROFILE/VEHICLES/UPGRADE/RECALIBER")) == true
                && Vision.BreadcrumbIsRecaliber(Recognition.Normalize("RECALIBER/PROFILE/VEHICLES/UPGRADE")) == false
                && Vision.BreadcrumbIsRecaliber(Recognition.Normalize("RECALIBER/PROFILE/VEHICLES/UPGRADE/RECALIBER")) == true
                && Vision.BreadcrumbIsRecaliber(Recognition.Normalize("PROFILE/VEHICLES")) == null,
            "breadcrumb: last segment decides the screen (stale RECALIBER prefix, 28.10 21:02)"
        );
        var partialName = Recognition
            .Name("Clean Driver Decrease BUCKS loss by 2.1% with CLEAN DRIVING modifier", c)
            .Title;
        Assert(Recognition.Plan(roll, partialName, "Rich").Index == 2, "ignore missing range on unwanted new perk");
        Assert(
            Recognition.Plan(roll, partialName, "Iron Grip", reroll: current).Index == 1,
            "upgrade wanted perk despite unreadable irrelevant offer"
        );
        Assert(
            Recognition.Plan(current, partialName, "Iron Grip", reroll: roll).Index == 2,
            "reject downgrade with irrelevant offer unreadable"
        );
        bool required = false;
        try
        {
            Recognition.Plan(roll, "Rich", "Rich");
        }
        catch (InvalidOperationException)
        {
            required = true;
        }
        Assert(required, "wanted new perk still requires confirmed percentages");
        required = false;
        try
        {
            Recognition.Plan(roll, partialName, "Iron Grip");
        }
        catch (InvalidOperationException)
        {
            required = true;
        }
        Assert(required, "wanted reroll still requires confirmed percentages");
        Assert(current.Maxed, "maximum from in-game bracket");
        Assert(Recognition.Choose(current, fresh, roll, "Rich") == 0, "take wanted new perk");
        Assert(Recognition.Choose(current, fresh, roll, "Iron Grip") == 2, "never downgrade current maximum");
        Assert(Recognition.Choose(roll, fresh, current, "Iron Grip") == 1, "take higher reroll");
        Assert(Recognition.Choose(current, fresh, roll, "Pure") == 2, "keep existing while searching");
        Assert(Parse("Extra Pump Increase NITRO refill speed by 4,7% [2,5%–5%]").Value == 4.7, "comma decimals");
        Assert(
            Parse("Skilled Increase scoring by 10% [5%-10%] in all point based FEATS").Maxed,
            "ignore later game description"
        );
        foreach (
            var text in new[]
            {
                "Rich 1% [1%-3%] Pure",
                "Rich 10% [1%-3%]",
                "Rich 1% [1%-3]",
                "Unknown 1% [1%-3%]",
                "Rich 1% [3%-1%]",
                "Trawler 1.3% [l .%-2%]",
                "Trawler 1.3% [l 2%-2%]",
                "Trawler 1.3% [l .2%-?%]",
            }
        )
        {
            bool failed = false;
            try
            {
                Parse(text);
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            Assert(failed, "reject ambiguous/invalid OCR: " + text);
        }
        bool mismatch = false;
        try
        {
            Recognition.Choose(current, fresh, fresh, "Rich");
        }
        catch (InvalidOperationException)
        {
            mismatch = true;
        }
        Assert(mismatch, "reject unexpected reroll identity");
        foreach (double v in new[] { 1.0, 1.2, 1.4, 1.6, 1.8, 2.1, 2.3, 2.5, 2.7, 3.0 })
            Assert(
                Recognition.OnTierGrid(Parse($"Rich Increase BUCKS gains by {v}% [1%-3%]")),
                "tier grid accepts Rich " + v
            );
        foreach (double v in new[] { 5, 5.5, 6.1, 6.6, 7.2, 7.7, 8.3, 8.8, 9.4, 10 })
            Assert(
                Recognition.OnTierGrid(Parse($"Skilled Increase scoring by {v}% [5%-10%]")),
                "tier grid accepts Skilled " + v
            );
        foreach (double v in new[] { 1.1, 1.5, 2.0, 2.9 })
            Assert(
                !Recognition.OnTierGrid(Parse($"Rich Increase BUCKS gains by {v}% [1%-3%]")),
                "tier grid rejects Rich " + v
            );
        // Fuzzy titles: one or two wrong glyphs at the start of a card still resolve to a unique perk.
        Assert(
            Recognition.Name("Bende Increase the player's lean bonus by 4.6% [3%-8%] for BIKES", c).Title == "Bended",
            "fuzzy: observed Bende OCR resolves to Bended without an alias"
        );
        Assert(
            Recognition.Name("Treasure Hurt Increase RADAR range by 6% [5%-10%]", c).Title == "Treasure Hunt",
            "fuzzy: one wrong glyph in a long title"
        );
        Assert(
            Recognition.Name("TrawIer Increase SPEED in WATER by 1.3% [1.2%-2%]", c).Title == "Trawler",
            "fuzzy: l read as I still picks Trawler over Trailer"
        );
        foreach (var affix in c.Affixes)
        {
            Assert(
                Recognition.Name(affix.Title + " Increase X by 1% [1%-3%]", c).Title == affix.Title,
                "exact title: " + affix.Title
            );
            string damaged = affix.Title[..^1] + "#";
            Assert(
                Recognition.Name(damaged + " Increase X by 1% [1%-3%]", c).Title == affix.Title,
                "fuzzy last glyph: " + affix.Title
            );
        }
        foreach (
            var text in new[]
            {
                "Zzzzzzz Increase X by 1% [1%-3%]",
                "Rich Pure 1% [1%-3%]",
                "Tra 1% [1%-3%]",
                "Increase X by 1% [1%-3%]",
            }
        )
        {
            bool rejected = false;
            try
            {
                Recognition.Name(text, c);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Assert(rejected, "fuzzy rejects unknown or ambiguous: " + text);
        }
        Assert(
            c.Builds.All(b =>
                b.Affixes.Length == 2
                && b.Affixes.All(a => c.Affixes.Any(x => x.Title == a))
                && c.Sets.Any(s => s.Title == b.Set)
            ),
            "catalog references resolve"
        );
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), results);
        return 0;
    }

    public static async Task<int> Screens(string[] paths)
    {
        var c = Catalog.Load();
        var p = new Profile();
        var vision = new Vision(c, p);
        var results = new List<object>();
        int failed = 0;
        foreach (var path in paths)
        {
            using var frame = new Bitmap(path);
            var entries = new Dictionary<string, object>();
            foreach (
                var name in new[] { "part", "set", "left", "right", "topLeft", "middleLeft", "topRight", "middleRight" }
            )
            {
                var text = await vision.Text(frame, name);
                string parsed = "";
                try
                {
                    if (name is "left" or "right" or "topLeft" or "middleLeft" or "topRight" or "middleRight")
                        parsed = Recognition.Parse(text, c).ToString();
                }
                catch (InvalidOperationException e)
                {
                    parsed = e.Message;
                }
                entries[name] = new { text, parsed };
            }
            // Every supplied screenshot must yield the two installed affixes, set and part.
            try
            {
                await vision.Read(frame, "left");
                await vision.Read(frame, "right");
                await vision.SetName(frame);
                await vision.PartName(frame);
            }
            catch (Exception e)
            {
                failed++;
                entries["error"] = e.Message;
            }
            try
            {
                var a = await vision.Read(frame, "left");
                var b = await vision.Read(frame, "right");
                bool score = path.Contains("5_47_");
                if (
                    score
                        ? a.Affix.Title != "Skilled"
                            || a.Value != 10
                            || !a.Maxed
                            || b.Affix.Title != "Extra Pump"
                            || b.Value != 4.7
                            || b.Maximum != 5
                        : a.Affix.Title != "Iron Grip"
                            || a.Value != 6
                            || !a.Maxed
                            || b.Affix.Title != "Bulldozer"
                            || b.Value != 6
                            || b.Maximum != 12
                )
                    throw new Exception("Reference values differ");
                if (path.Contains("5_48_36") || path.Contains("5_48_38"))
                {
                    var fresh = await vision.Read(frame, "topLeft");
                    var reroll = await vision.Read(frame, "middleLeft");
                    if (
                        fresh.Affix.Title != "Rich"
                        || fresh.Value != 1
                        || reroll.Affix.Title != "Iron Grip"
                        || reroll.Value != 5.5
                        || Recognition.Choose(a, fresh, reroll, "Rich") != 0
                        || Recognition.Choose(a, fresh, reroll, "Iron Grip") != 2
                    )
                        throw new Exception("Reference popup decisions differ");
                }
            }
            catch (Exception e)
            {
                failed++;
                entries["referenceError"] = e.Message;
            }
            int popup = await vision.PopupSide(frame);
            bool expectedPopup = path.Contains("5_48_36") || path.Contains("5_48_38");
            if (popup != (expectedPopup ? 0 : -1))
            {
                failed++;
                entries["popupError"] = "Popup detection mismatch";
            }
            int expectedFocus =
                path.Contains("5_48_36") ? 1
                : path.Contains("5_48_38") ? 0
                : path.Contains("5_48_32") ? 0
                : 1;
            int actualFocus = expectedPopup
                ? vision.Focus(frame, ["topLeft", "middleLeft", "left"])
                : vision.Focus(frame, ["left", "right"]);
            if (actualFocus != expectedFocus)
            {
                failed++;
                entries["focusError"] = "Focus mismatch";
            }
            entries["popupSide"] = popup;
            entries["focusMain"] = vision.Focus(frame, ["left", "right"]);
            entries["focusPopupLeft"] = vision.Focus(frame, ["topLeft", "middleLeft", "left"]);
            results.Add(new { path, entries });
        }
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "reference-results.json"),
            System.Text.Json.JsonSerializer.Serialize(new { failed, results }, Catalog.JsonOptions)
        );
        return failed == 0 ? 0 : 1;
    }
}
