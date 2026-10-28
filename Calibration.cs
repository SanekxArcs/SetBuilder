using System.Text.Json;
using System.Text.RegularExpressions;

namespace TunerFest;

/// <summary>What the automatic zone calibration found on one frame.</summary>
public sealed record CalibrationResult(
    bool Success,
    GameScreen Screen,
    Rectangle UiArea,
    Size FrameSize,
    IReadOnlyDictionary<string, Zone> Zones,
    IReadOnlyList<string> Verified,
    IReadOnlyList<string> Problems
)
{
    public string ScreenTitle => ZoneCalibration.ScreenTitle(Screen);
}

/// <summary>
/// Automatic zones. The game draws its menus inside a 16:9 area: on a wider window the sides are pure black bands,
/// on a taller one the top and bottom are. The shipped zones were taken on 3440×1440 captures where that area is
/// 2560×1440 at x = 440, so every zone is expressed relative to the area and projected into the area found on the
/// current frame. The projected zones are then verified by reading the frame with them: the screen must be
/// recognised and everything visible on it (breadcrumb, perk names, set, part title, grid focus) must read.
/// Nothing is written unless every read succeeds.
/// </summary>
public static class ZoneCalibration
{
    public static readonly Size ReferenceSize = new(3440, 1440);
    public static readonly Rectangle ReferenceUi = new(440, 0, 2560, 1440);

    /// <summary>A pixel is part of a band when every channel is at most this (JPEG noise stays below).</summary>
    const int Black = 12;
    const double Target = 16.0 / 9;

    public static string ScreenTitle(GameScreen screen) =>
        screen switch
        {
            GameScreen.Ready or GameScreen.Overview => "основний екран RECALIBER",
            GameScreen.PopupLeft => "меню кидка лівого слота",
            GameScreen.PopupRight => "меню кидка правого слота",
            GameScreen.Parts => "екран UPGRADE із сімома деталями",
            GameScreen.Inventory => "екран UPGRADE зі списком категорії",
            _ => "невідомий екран",
        };

    /// <summary>The 16:9 area a window of this size would have: centred, pillarboxed when wider, letterboxed when taller.</summary>
    public static Rectangle UiAreaByAspect(Size size)
    {
        double aspect = (double)size.Width / size.Height;
        if (aspect > Target * 1.02)
        {
            int w = (int)Math.Round(size.Height * Target);
            return new Rectangle((size.Width - w) / 2, 0, w, size.Height);
        }
        if (aspect < Target / 1.02)
        {
            int h = (int)Math.Round(size.Width / Target);
            return new Rectangle(0, (size.Height - h) / 2, size.Width, h);
        }
        return new Rectangle(Point.Empty, size);
    }

    /// <summary>
    /// The UI area on this frame: the pure-black bands at the edges are measured; when they are missing or not
    /// symmetric (a loading screen, a cropped capture) the aspect formula is used instead.
    /// </summary>
    public static Rectangle UiArea(Bitmap frame, out string note)
    {
        int w = frame.Width,
            h = frame.Height;
        int left = 0,
            right = 0,
            top = 0,
            bottom = 0;
        while (left < w / 3 && ColumnBlack(frame, left))
            left++;
        while (right < w / 3 && ColumnBlack(frame, w - 1 - right))
            right++;
        while (top < h / 3 && RowBlack(frame, top))
            top++;
        while (bottom < h / 3 && RowBlack(frame, h - 1 - bottom))
            bottom++;
        var byAspect = UiAreaByAspect(frame.Size);
        var detected = new Rectangle(left, top, w - left - right, h - top - bottom);
        bool symmetric =
            Math.Abs(left - right) <= Math.Max(4, w / 100) && Math.Abs(top - bottom) <= Math.Max(4, h / 100);
        if (detected.Width < w / 2 || detected.Height < h / 2 || !symmetric)
        {
            note = $"чорні смуги не впізнано (ліворуч {left}, праворуч {right}, зверху {top}, знизу {bottom} px), беру область за пропорцією 16:9";
            return byAspect;
        }
        if (left + right + top + bottom == 0)
        {
            note = "чорних смуг немає: інтерфейс гри займає весь кадр";
            return detected;
        }
        note = $"чорні смуги: ліворуч {left}, праворуч {right}, зверху {top}, знизу {bottom} px";
        return detected;
    }

    static bool ColumnBlack(Bitmap frame, int x)
    {
        for (int y = 0; y < frame.Height; y += 6)
        {
            var p = frame.GetPixel(x, y);
            if (p.R > Black || p.G > Black || p.B > Black)
                return false;
        }
        return true;
    }

    static bool RowBlack(Bitmap frame, int y)
    {
        for (int x = 0; x < frame.Width; x += 6)
        {
            var p = frame.GetPixel(x, y);
            if (p.R > Black || p.G > Black || p.B > Black)
                return false;
        }
        return true;
    }

    /// <summary>Every shipped zone projected from the reference UI area into <paramref name="ui"/> on a frame of <paramref name="frame"/> pixels.</summary>
    public static Dictionary<string, Zone> Map(Size frame, Rectangle ui)
    {
        var result = new Dictionary<string, Zone>();
        foreach (var (key, z) in Profile.Defaults())
        {
            double px = z.X * ReferenceSize.Width,
                py = z.Y * ReferenceSize.Height,
                pw = z.W * ReferenceSize.Width,
                ph = z.H * ReferenceSize.Height;
            double ux = (px - ReferenceUi.X) / ReferenceUi.Width,
                uy = (py - ReferenceUi.Y) / ReferenceUi.Height,
                uw = pw / ReferenceUi.Width,
                uh = ph / ReferenceUi.Height;
            double x = (ui.X + ux * ui.Width) / frame.Width,
                y = (ui.Y + uy * ui.Height) / frame.Height,
                width = uw * ui.Width / frame.Width,
                height = uh * ui.Height / frame.Height;
            result[key] = new Zone(Math.Round(x, 4), Math.Round(y, 4), Math.Round(width, 4), Math.Round(height, 4));
        }
        return result;
    }

    /// <summary>Projects the zones onto the frame and verifies them by reading it. <paramref name="settings"/> supplies everything but the zones.</summary>
    public static async Task<CalibrationResult> Run(
        Bitmap frame,
        Catalog catalog,
        Profile settings,
        Action<string>? log = null
    )
    {
        var ui = UiArea(frame, out string note);
        log?.Invoke($"Кадр {frame.Width}×{frame.Height} · {note} · область {ui.Width}×{ui.Height} від ({ui.X}, {ui.Y})");
        var zones = Map(frame.Size, ui);
        var candidate =
            JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(settings, Catalog.JsonOptions), Catalog.JsonOptions)
            ?? new Profile();
        candidate.Zones = new Dictionary<string, Zone>(zones);
        var vision = new Vision(catalog, candidate);
        var verified = new List<string>();
        var problems = new List<string>();

        async Task Check(string label, Func<Task<string>> read)
        {
            try
            {
                verified.Add(label + ": " + await read());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                problems.Add(label + " — " + e.Message);
            }
        }

        GameScreen screen;
        try
        {
            screen = await vision.ScreenState(frame);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            screen = GameScreen.Unknown;
            problems.Add("екран — " + e.Message);
        }
        log?.Invoke("Екран: " + ScreenTitle(screen));

        await Check(
            "шлях меню",
            async () =>
            {
                string text = (await vision.Text(frame, "breadcrumb")).Trim();
                if (Vision.BreadcrumbIsRecaliber(Recognition.Normalize(text)) == null)
                    throw new InvalidOperationException($"немає UPGRADE чи RECALIBER: «{text}»");
                return text;
            }
        );
        switch (screen)
        {
            case GameScreen.Ready or GameScreen.Overview:
                await Check("лівий перк", async () => (await vision.ReadName(frame, "left")).Title);
                await Check("правий перк", async () => (await vision.ReadName(frame, "right")).Title);
                await CheckSetAndPart();
                if (screen == GameScreen.Ready)
                    verified.Add("жовта рамка: слот " + (vision.Focus(frame, ["left", "right"]) + 1));
                break;
            case GameScreen.PopupLeft or GameScreen.PopupRight:
                bool leftSide = screen == GameScreen.PopupLeft;
                string top = leftSide ? "topLeft" : "topRight",
                    middle = leftSide ? "middleLeft" : "middleRight",
                    other = leftSide ? "right" : "left";
                await Check("новий перк зверху", async () => (await vision.ReadName(frame, top)).Title);
                await Check("повторний кидок посередині", async () => (await vision.ReadName(frame, middle)).Title);
                await Check(leftSide ? "правий перк" : "лівий перк", async () => (await vision.ReadName(frame, other)).Title);
                await CheckSetAndPart();
                break;
            case GameScreen.Parts:
                CheckGrid();
                break;
            case GameScreen.Inventory:
                await Check(
                    "заголовок списку",
                    async () =>
                    {
                        string text = (await vision.Text(frame, "inventoryHeader")).Trim();
                        if (!Regex.IsMatch(text, @"^[A-Z][A-Z ]{2,28}$"))
                            throw new InvalidOperationException($"«{text}» не схоже на назву категорії");
                        return text;
                    }
                );
                CheckGrid();
                break;
            default:
                problems.Add(
                    "екран не впізнано: відкрий у грі деталь (RECALIBER), меню кидка або UPGRADE із сімома деталями й повтори"
                );
                break;
        }
        bool success = screen != GameScreen.Unknown && problems.Count == 0 && verified.Count >= 2;
        return new CalibrationResult(success, screen, ui, frame.Size, zones, verified, problems);

        async Task CheckSetAndPart()
        {
            await Check("сет", () => vision.SetName(frame));
            await Check(
                "деталь",
                async () =>
                {
                    await vision.PartName(frame);
                    return (await vision.Text(frame, "part")).Trim();
                }
            );
        }

        void CheckGrid()
        {
            int focus = vision.GridFocus(frame);
            if (focus < 0)
                problems.Add("ряд деталей — жовту рамку не знайдено в жодній із семи плиток");
            else
                verified.Add($"ряд деталей: рамка на плитці {focus + 1} з 7");
        }
    }
}
