using System.Drawing.Drawing2D;

namespace TunerFest;

/// <summary>
/// --verify-calibration FOLDER: runs the automatic zone calibration on the five reference frames (3440×1440 captures of
/// every screen) and on variants made from them: the 16:9 area cut out (2560×1440), scaled to 1920×1080, and letterboxed
/// to 1920×1200. Every variant must find the UI area, recognise the expected screen and read everything on it.
/// Writes calibration-test.txt next to the executable; exit code 1 when anything fails.
/// </summary>
public static class CalibrationVerification
{
    static readonly (string File, GameScreen Screen)[] Cases =
    [
        ("ready.jpg", GameScreen.Ready),
        ("popup-left.jpg", GameScreen.PopupLeft),
        ("popup-right.jpg", GameScreen.PopupRight),
        ("parts.jpg", GameScreen.Parts),
        ("inventory.jpg", GameScreen.Inventory),
    ];

    public static async Task<int> Run(string folder)
    {
        var catalog = Catalog.Load();
        var settings = new Profile();
        var lines = new List<string>();
        int failed = 0;
        var started = DateTime.Now;

        void Pass(string text) => lines.Add("PASS " + text);
        void Fail(string text)
        {
            failed++;
            lines.Add("FAIL " + text);
        }

        // Pure geometry first.
        var defaults = Profile.Defaults();
        var mapped = ZoneCalibration.Map(ZoneCalibration.ReferenceSize, ZoneCalibration.ReferenceUi);
        double worst = defaults.Max(d => Diff(d.Value, mapped[d.Key]));
        if (worst < .0006)
            Pass("reference area maps every zone back onto the shipped defaults");
        else
            Fail($"reference mapping drifts by {worst}");
        if (
            ZoneCalibration.UiAreaByAspect(new Size(3440, 1440)) == new Rectangle(440, 0, 2560, 1440)
            && ZoneCalibration.UiAreaByAspect(new Size(1920, 1080)) == new Rectangle(0, 0, 1920, 1080)
            && ZoneCalibration.UiAreaByAspect(new Size(1920, 1200)) == new Rectangle(0, 60, 1920, 1080)
            && ZoneCalibration.UiAreaByAspect(new Size(2560, 1080)) == new Rectangle(320, 0, 1920, 1080)
        )
            Pass("aspect formula: 21:9 pillarbox, 16:9 full, 16:10 letterbox");
        else
            Fail("aspect formula gives a wrong area");

        foreach (var (file, expected) in Cases)
        {
            string path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                Fail($"{file}: missing in {folder}");
                continue;
            }
            using var original = new Bitmap(path);
            var ui = ZoneCalibration.UiArea(original, out _);
            using var cut = Crop(original, ui);
            using var hd = Scale(cut, 1920, 1080);
            using var boxed = Letterbox(hd, 1920, 1200);
            var variants = new (string Name, Bitmap Frame, Rectangle ExpectedUi)[]
            {
                ("3440×1440 (оригінал)", original, new Rectangle(440, 0, 2560, 1440)),
                ("2560×1440 (область 16:9 вирізано)", cut, new Rectangle(0, 0, 2560, 1440)),
                ("1920×1080", hd, new Rectangle(0, 0, 1920, 1080)),
                ("1920×1200 (смуги зверху й знизу)", boxed, new Rectangle(0, 60, 1920, 1080)),
            };
            foreach (var (name, frame, expectedUi) in variants)
            {
                var log = new List<string>();
                var result = await ZoneCalibration.Run(frame, catalog, settings, log.Add);
                string label = $"{file} · {name}";
                if (!Near(result.UiArea, expectedUi, 3))
                {
                    Fail($"{label}: UI area {result.UiArea}, expected {expectedUi}");
                    continue;
                }
                if (ReferenceEquals(frame, original))
                {
                    double drift = defaults.Max(d => Diff(d.Value, result.Zones[d.Key]));
                    if (drift >= .002)
                    {
                        Fail($"{label}: zones drift from the defaults by {drift}");
                        continue;
                    }
                }
                bool screenOk = result.Screen == expected || (expected == GameScreen.Ready && result.Screen == GameScreen.Overview);
                if (!screenOk || !result.Success)
                {
                    Fail(
                        $"{label}: screen {result.Screen} (expected {expected}), success {result.Success}; "
                            + string.Join(" | ", result.Problems)
                            + " || verified: "
                            + string.Join(" | ", result.Verified)
                    );
                    continue;
                }
                Pass($"{label}: {result.ScreenTitle}, {result.Verified.Count} reads — {string.Join(" · ", result.Verified)}");
            }
        }
        lines.Add($"Calibration verification: {lines.Count(l => l.StartsWith("PASS "))} passed, {failed} failed, {(DateTime.Now - started).TotalSeconds:0.0} s");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "calibration-test.txt"), string.Join("\n", lines) + "\n");
        foreach (var line in lines.Where(l => l.StartsWith("FAIL ")))
            Console.Error.WriteLine(line);
        return failed == 0 ? 0 : 1;
    }

    static double Diff(Zone a, Zone b) =>
        Math.Max(Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)), Math.Max(Math.Abs(a.W - b.W), Math.Abs(a.H - b.H)));

    static bool Near(Rectangle a, Rectangle b, int tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance
        && Math.Abs(a.Y - b.Y) <= tolerance
        && Math.Abs(a.Width - b.Width) <= 2 * tolerance
        && Math.Abs(a.Height - b.Height) <= 2 * tolerance;

    static Bitmap Crop(Bitmap source, Rectangle area)
    {
        var result = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(result);
        g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area, GraphicsUnit.Pixel);
        return result;
    }

    static Bitmap Scale(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, width, height);
        return result;
    }

    static Bitmap Letterbox(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.Black);
        g.DrawImage(source, (width - source.Width) / 2, (height - source.Height) / 2, source.Width, source.Height);
        return result;
    }
}
