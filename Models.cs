using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TunerFest;

public record Zone(double X, double Y, double W, double H)
{
    public Rectangle Pixels(Size s) =>
        new(
            (int)Math.Round(X * s.Width),
            (int)Math.Round(Y * s.Height),
            (int)Math.Round(W * s.Width),
            (int)Math.Round(H * s.Height)
        );

    public bool Valid => X >= 0 && Y >= 0 && W > 0 && H > 0 && X + W <= 1.001 && Y + H <= 1.001;
}

public sealed class Affix
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public double BonusPercentageRangeFrom { get; set; }
    public double BonusPercentageRangeTo { get; set; }
    public string Icon { get; set; } = "";

    public override string ToString() => Title;
}

public sealed class SetBonus
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";

    public override string ToString() => Title;
}

public sealed class Build
{
    public string Title { get; set; } = "";
    public string Set { get; set; } = "";
    public string[] Affixes { get; set; } = [];

    public override string ToString() => Title;
}

public sealed class Catalog
{
    public List<Affix> Affixes { get; set; } = [];
    public List<SetBonus> Sets { get; set; } = [];
    public List<Build> Builds { get; set; } = [];
    public static JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static Catalog Load() =>
        JsonSerializer.Deserialize<Catalog>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json")),
            JsonOptions
        )!;
}

public sealed class Profile
{
    public bool AutoSet { get; set; } = false;
    public string Set { get; set; } = "NITRO CHEMIST";
    public string First { get; set; } = "Pure";
    public string Second { get; set; } = "Extra Pump";
    public bool Xbox { get; set; } = false;
    public int Parts { get; set; } = 7;
    public int Delay { get; set; } = 450;
    public bool FastPartNavigation { get; set; } = false;
    public int LogPanelHeight { get; set; } = 220;
    public int LogFontSize { get; set; } = 11;
    public int ExitPauseMs { get; set; } = 450;
    public bool CompletionSoundEnabled { get; set; } = true;
    public bool StopSoundEnabled { get; set; } = true;
    public string CompletionSound { get; set; } = "Asterisk";
    public string StopSound { get; set; } = "Exclamation";
    public bool FastRecognition { get; set; } = true;
    public bool RandomPause { get; set; } = true;
    public int PauseMinMs { get; set; } = 50;
    public int PauseMaxMs { get; set; } = 500;
    public int MaxRolls { get; set; } = 500;
    public int FocusReadAttempts { get; set; } = 12;
    public int FocusRetryMs { get; set; } = 75;
    public bool DiagnosticsEnabled { get; set; } = true;
    public bool DiagnosticScreenshots { get; set; } = true;
    public int DiagnosticMaxImages { get; set; } = 400;
    public int DiagnosticMaxMb { get; set; } = 256;
    public string Confirm { get; set; } = "Return";
    public string Back { get; set; } = "Escape";
    public string Recalibrate { get; set; } = "F9";
    public string XboxRecalibrate { get; set; } = "KeyboardF9";
    public string Left { get; set; } = "Left";
    public string Right { get; set; } = "Right";
    public string Up { get; set; } = "Up";
    public string Down { get; set; } = "Down";
    public Dictionary<string, Zone> Zones { get; set; } = Defaults();

    public static Dictionary<string, Zone> Defaults() =>
        new()
        {
            ["left"] = new(.190, .738, .216, .145),
            ["right"] = new(.405, .738, .216, .145),
            ["set"] = new(.690, .751, .145, .044),
            ["part"] = new(.460, .202, .105, .040),
            ["topLeft"] = new(.190, .455, .216, .143),
            ["middleLeft"] = new(.190, .598, .216, .140),
            ["topRight"] = new(.405, .455, .216, .143),
            ["middleRight"] = new(.405, .598, .216, .140),
            ["inventoryHeader"] = new(.185, .663, .22, .044),
            ["breadcrumb"] = new(.184, .036, .33, .05),
            ["parts"] = new(.1855, .707, .672, .175),
        };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TunerFestSetBuilder");
    public static string PathName => Path.Combine(Folder, "profile.json");

    public static Profile Load()
    {
        var result = File.Exists(PathName)
            ? JsonSerializer.Deserialize<Profile>(File.ReadAllText(PathName), Catalog.JsonOptions) ?? new()
            : new Profile();
        foreach (var (key, zone) in Defaults())
            result.Zones.TryAdd(key, zone);
        return result;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathName, JsonSerializer.Serialize(this, Catalog.JsonOptions));
    }
}

public record Reading(Affix Affix, double Value, double Minimum, double Maximum)
{
    public bool Maxed => Value >= Maximum - .0001;

    public override string ToString() => $"{Affix.Title} {Value:0.##}% / {Maximum:0.##}%";
}

public record RollChoice(int Index, Reading Expected, string FreshName);

public static class Recognition
{
    public static string Normalize(string s) => Regex.Replace(s.ToUpperInvariant(), "[^A-Z0-9]", "");

    // Require an unambiguous title and three complete percentages; correct only known numeric OCR glyphs.
    public static Reading Parse(string text, Catalog catalog)
    {
        text = Regex.Replace(text, @"(?<=\d)0\s*/\s*0", "%");
        var affix = Name(text, catalog);
        // Windows OCR may split decimals and read 1 as l/I/|, or 0 as O.
        // Restrict correction to a complete numeric token immediately before %, never names.
        text = Regex.Replace(
            text,
            @"(?<![\w.,])(?<n>[0-9Il|Oo](?:\s*[0-9Il|Oo])*(?:\s*[.,]\s*[0-9Il|Oo](?:\s*[0-9Il|Oo])*)?)\s*%",
            m =>
                Regex
                    .Replace(m.Groups["n"].Value, @"\s+", "")
                    .Replace('l', '1')
                    .Replace('I', '1')
                    .Replace('|', '1')
                    .Replace('O', '0')
                    .Replace('o', '0') + "%"
        );
        var matches = Regex.Matches(text, @"(?<![\w.,])(\d+(?:[.,]\d+)?)\s*%");
        var numbers = matches
            .Select(m => double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture))
            .ToArray();
        if (numbers.Length != 3)
            throw new InvalidOperationException($"Очікується значення і діапазон [мін%–макс%]: {text}");
        var a = affix;
        if (
            numbers[1] <= 0
            || numbers[2] < numbers[1]
            || numbers[0] < numbers[1]
            || numbers[0] > numbers[2]
            || numbers[2] > 100
        )
            throw new InvalidOperationException($"Неправдоподібні відсотки: {text}");
        // In-game bracket is authoritative (ranges can differ by part/category).
        return new(a, numbers[0], numbers[1], numbers[2]);
    }

    public static Affix Name(string text, Catalog catalog)
    {
        text = Regex.Replace(text, @"^\s*Bende\b", "Bended", RegexOptions.IgnoreCase);
        var clean = Normalize(text);
        var candidates = catalog.Affixes.Where(a => clean.Contains(Normalize(a.Title))).ToList();
        if (candidates.Count != 1)
            throw new InvalidOperationException($"Не розпізнано назву перка: {text}");
        return candidates[0];
    }

    public static string[] Targets(Reading[] values, string first, string second) =>
        values[0].Affix.Title == second || values[1].Affix.Title == first ? [second, first] : [first, second];

    public static RollChoice Plan(
        Reading current,
        string freshName,
        string wanted,
        Reading? fresh = null,
        Reading? reroll = null
    )
    {
        if (current.Affix.Title == wanted)
        {
            if (
                reroll == null
                || reroll.Affix.Title != current.Affix.Title
                || reroll.Minimum != current.Minimum
                || reroll.Maximum != current.Maximum
            )
                throw new InvalidOperationException("Не підтверджено відсотки повторного кидка потрібного перка.");
            return reroll.Value > current.Value ? new(1, reroll, freshName) : new(2, current, freshName);
        }
        if (freshName == wanted)
        {
            if (fresh == null || fresh.Affix.Title != wanted)
                throw new InvalidOperationException("Не підтверджено відсотки нового потрібного перка.");
            return new(0, fresh, freshName);
        }
        return new(2, current, freshName);
    }

    public static int Choose(Reading current, Reading fresh, Reading reroll, string wanted)
    {
        if (
            reroll.Affix.Title != current.Affix.Title
            || reroll.Minimum != current.Minimum
            || reroll.Maximum != current.Maximum
        )
            throw new InvalidOperationException("Середній варіант не відповідає поточному перку.");
        if (current.Affix.Title == wanted)
            return reroll.Value > current.Value ? 1 : 2;
        return fresh.Affix.Title == wanted ? 0 : 2;
    }

    public static bool Equal(Reading a, Reading b) =>
        a.Affix.Title == b.Affix.Title
        && Math.Abs(a.Value - b.Value) < .0001
        && a.Maximum == b.Maximum
        && a.Minimum == b.Minimum;
}
