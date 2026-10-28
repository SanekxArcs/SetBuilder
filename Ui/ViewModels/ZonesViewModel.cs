using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace TunerFest.Ui.ViewModels;

/// <summary>
/// Tab "Зони екрана": one card per screen zone, picked with the WinForms ZonePicker over the game window, and an
/// example panel: the reference screenshot of the screen the zone belongs to (Assets/reference, 3440×1440 captures
/// scaled to 1376×576) with the default zone framed in yellow and the current zone dashed when it differs.
/// </summary>
public sealed class ZonesViewModel : PageViewModel
{
    /// <summary>A reference screen: the image file in Assets/reference and the title shown over the example.</summary>
    public sealed record ReferenceScreen(string Image, string Title, string Where);

    public static readonly IReadOnlyDictionary<string, ReferenceScreen> Screens = new Dictionary<string, ReferenceScreen>
    {
        ["ready"] = new(
            "ready.jpg",
            "Основний екран RECALIBER",
            "PROFILE / VEHICLES / UPGRADE / RECALIBER: деталь відкрита, жовта рамка на одному з двох перків."
        ),
        ["popup-left"] = new(
            "popup-left.jpg",
            "Меню кидка лівого слота",
            "На екрані RECALIBER натиснуто Enter на лівому перку: над ним відкрилося меню з трьох блоків."
        ),
        ["popup-right"] = new(
            "popup-right.jpg",
            "Меню кидка правого слота",
            "На екрані RECALIBER натиснуто Enter на правому перку: над ним відкрилося меню з трьох блоків."
        ),
        ["parts"] = new(
            "parts.jpg",
            "Екран UPGRADE із сімома деталями",
            "PROFILE / VEHICLES / UPGRADE: ряд семи деталей унизу, рамка на одній із них."
        ),
        ["inventory"] = new(
            "inventory.jpg",
            "Екран UPGRADE зі списком категорії",
            "Той самий UPGRADE після відкриття категорії: заголовок (BRAKES, GEARBOX…) над рядом деталей."
        ),
    };

    // Inventory order (tab 1 of the old UI); the keys match Profile.Zones and Profile.Defaults().
    static readonly (string Key, string Label, SymbolRegular Icon, string Screen, string Hint)[] Table =
    [
        (
            "left",
            "Лівий перк",
            SymbolRegular.RectangleLandscape24,
            "ready",
            "Ліва картка в блоці AFFIX STATS цілком: верхня рамка, іконка, назва, опис зі значенням і діапазоном [мін%–макс%]. Ціну під карткою (монети) не включай."
        ),
        (
            "right",
            "Правий перк",
            SymbolRegular.RectangleLandscape24,
            "ready",
            "Права картка в блоці AFFIX STATS цілком, як і ліва: рамка, іконка, назва, опис із [мін%–макс%]. Без ціни."
        ),
        (
            "set",
            "Назва сету",
            SymbolRegular.RectangleLandscape24,
            "ready",
            "Рядок із назвою сету в блоці SET BONUS (наприклад LOOT DIGGER) разом з іконкою. Рядки 3/7 і 7/7 під ним не потрібні."
        ),
        (
            "part",
            "Заголовок деталі",
            SymbolRegular.RectangleLandscape24,
            "ready",
            "Напис над зображенням деталі: рідкість і назва, наприклад LEGENDARY GEARBOX. Іконки й число 960 під ним не включай."
        ),
        (
            "topLeft",
            "Лівий: новий перк зверху",
            SymbolRegular.Crop24,
            "popup-left",
            "Верхній блок меню кидка над лівим перком: нова пропозиція з рамкою, іконкою, назвою й описом із [мін%–макс%]. Підпис Enter під блоком не потрібен."
        ),
        (
            "middleLeft",
            "Лівий: повторний кидок посередині",
            SymbolRegular.Crop24,
            "popup-left",
            "Середній блок того самого меню: повторний кидок поточного перка (та сама назва, нове значення). Нижній блок збігається із зоною «Лівий перк»."
        ),
        (
            "topRight",
            "Правий: новий перк зверху",
            SymbolRegular.Crop24,
            "popup-right",
            "Верхній блок меню кидка над правим перком: нова пропозиція з рамкою, іконкою, назвою й описом із [мін%–макс%]."
        ),
        (
            "middleRight",
            "Правий: повторний кидок посередині",
            SymbolRegular.Crop24,
            "popup-right",
            "Середній блок меню над правим перком: повторний кидок поточного перка. Нижній блок збігається із зоною «Правий перк»."
        ),
        (
            "parts",
            "Ряд усіх семи деталей",
            SymbolRegular.RectangleLandscape24,
            "parts",
            "Увесь ряд семи деталей від першої до сьомої разом із рамками й числами 960. Зона ділиться на сім рівних частин, тому краї мають збігатися з першою і останньою плиткою."
        ),
        (
            "breadcrumb",
            "Шлях меню вгорі",
            SymbolRegular.RectangleLandscape24,
            "ready",
            "Рядок PROFILE / VEHICLES / UPGRADE / RECALIBER у лівому верхньому куті. Залиш запас праворуч: на екрані UPGRADE рядок коротший, а назва може подовжитися."
        ),
        (
            "inventoryHeader",
            "Заголовок списку, напр. BRAKES",
            SymbolRegular.RectangleLandscape24,
            "inventory",
            "Заголовок категорії над рядом деталей після відкриття списку на екрані UPGRADE: BRAKES, GEARBOX тощо. За ним програма відрізняє список від ряду семи деталей."
        ),
    ];

    public const string NotSet = "Не задано";

    static readonly Dictionary<string, ImageSource?> images = [];
    string? exampleKey;
    ImageSource? exampleImage;
    double imageWidth = 1376,
        imageHeight = 576;
    Rect defaultRect,
        currentRect;
    bool showCurrent;

    public ZonesViewModel(IShellContext shell)
        : base(shell)
    {
        foreach (var (key, label, icon, screen, hint) in Table)
            Zones.Add(
                new ZoneRow(
                    key,
                    label,
                    icon,
                    screen,
                    hint,
                    new AsyncCommand(() => PickAsync(key, label)),
                    new RelayCommand(() => ShowExample(key))
                )
            );
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        HideExampleCommand = new RelayCommand(HideExample);
        RefreshRows();
        ShowExample("left");
    }

    public ObservableCollection<ZoneRow> Zones { get; } = [];

    public ICommand RestoreDefaultsCommand { get; }
    public ICommand HideExampleCommand { get; }

    // The example panel.
    public bool HasExample => exampleKey != null;
    public ZoneRow? ExampleRow => Zones.FirstOrDefault(r => r.Key == exampleKey);
    public string ExampleTitle => ExampleRow?.Label ?? "";
    public string ExampleHint => ExampleRow?.Hint ?? "";
    public string ExampleScreen => ExampleRow is { } row ? Screens[row.Screen].Title : "";
    public string ExampleWhere => ExampleRow is { } row ? Screens[row.Screen].Where : "";
    public ImageSource? ExampleImage => exampleImage;
    public bool ExampleMissing => HasExample && exampleImage == null;
    public double ImageWidth => imageWidth;
    public double ImageHeight => imageHeight;

    /// <summary>Default zone (Profile.Defaults) on the example image, in image pixels; framed in yellow.</summary>
    public Rect DefaultRect => defaultRect;

    /// <summary>The zone from the current profile, in image pixels; dashed, shown only when it differs from the default.</summary>
    public Rect CurrentRect => currentRect;
    public bool ShowCurrent => showCurrent;

    /// <summary>Where the yellow label sits: above the default frame, or just inside it at the top edge of the image.</summary>
    public double LabelTop => defaultRect.Y >= 30 ? defaultRect.Y - 30 : defaultRect.Y + 4;
    public double LabelLeft => defaultRect.X;

    public static string Format(Zone? z) => z == null ? NotSet : $"{z.X:P1} / {z.Y:P1} · {z.W:P1} × {z.H:P1}";

    /// <summary>Re-reads every zone from the profile (UpdateZoneLabels of the old UI).</summary>
    public void RefreshRows()
    {
        foreach (var row in Zones)
            row.Value = Format(Shell.Profile.Zones.GetValueOrDefault(row.Key));
        RefreshExampleRects();
    }

    /// <summary>"Відновити зони зі скріншотів": Profile.Defaults(), refresh, save.</summary>
    public void RestoreDefaults()
    {
        Shell.Profile.Zones = Profile.Defaults();
        RefreshRows();
        Shell.SaveProfile();
    }

    /// <summary>"Приклад" on a zone card: shows that zone on its reference screen.</summary>
    public void ShowExample(string key)
    {
        var row = Zones.FirstOrDefault(r => r.Key == key);
        if (row == null)
            return;
        exampleKey = key;
        exampleImage = ReferenceImage(Screens[row.Screen].Image);
        if (exampleImage is BitmapSource bitmap && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
        {
            imageWidth = bitmap.PixelWidth;
            imageHeight = bitmap.PixelHeight;
        }
        else
        {
            imageWidth = 1376;
            imageHeight = 576;
        }
        foreach (var r in Zones)
            r.IsExample = r.Key == key;
        RefreshExampleRects();
        RaiseExample();
    }

    public void HideExample()
    {
        exampleKey = null;
        exampleImage = null;
        foreach (var r in Zones)
            r.IsExample = false;
        RaiseExample();
    }

    void RefreshExampleRects()
    {
        if (exampleKey == null)
            return;
        var size = new System.Drawing.Size((int)imageWidth, (int)imageHeight);
        var fallback = Profile.Defaults().GetValueOrDefault(exampleKey);
        defaultRect = fallback == null ? Rect.Empty : ToRect(fallback.Pixels(size));
        var current = Shell.Profile.Zones.GetValueOrDefault(exampleKey);
        currentRect = current == null ? Rect.Empty : ToRect(current.Pixels(size));
        showCurrent = current != null && fallback != null && current != fallback;
        OnPropertyChanged(nameof(DefaultRect));
        OnPropertyChanged(nameof(CurrentRect));
        OnPropertyChanged(nameof(ShowCurrent));
        OnPropertyChanged(nameof(LabelTop));
        OnPropertyChanged(nameof(LabelLeft));
    }

    static Rect ToRect(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    void RaiseExample()
    {
        foreach (
            var name in new[]
            {
                nameof(HasExample),
                nameof(ExampleRow),
                nameof(ExampleTitle),
                nameof(ExampleHint),
                nameof(ExampleScreen),
                nameof(ExampleWhere),
                nameof(ExampleImage),
                nameof(ExampleMissing),
                nameof(ImageWidth),
                nameof(ImageHeight),
                nameof(DefaultRect),
                nameof(CurrentRect),
                nameof(ShowCurrent),
                nameof(LabelTop),
                nameof(LabelLeft),
            }
        )
            OnPropertyChanged(name);
    }

    /// <summary>A reference screenshot from Assets/reference next to the executable, cached and frozen; null when missing.</summary>
    public static ImageSource? ReferenceImage(string file)
    {
        if (images.TryGetValue(file, out var cached))
            return cached;
        ImageSource? result = null;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "reference", file);
            if (File.Exists(path))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                result = image;
            }
        }
        catch
        {
            result = null;
        }
        images[file] = result;
        return result;
    }

    /// <summary>
    /// Pick(key, label) of the old UI: hide the window, let ZonePicker cover the game client area,
    /// store the selection as fractions of that area and save. In preview mode (no window) only logs.
    /// </summary>
    public async Task PickAsync(string key, string label)
    {
        if (Shell.IsBusy)
            return;
        if (Shell.Game is not GameWindow game)
        {
            Shell.Log("Вибери вікно гри.");
            return;
        }
        var window = Shell.Window;
        if (window == null)
        {
            Shell.Log("Немає вікна програми — зону можна виділити лише в запущеному застосунку.");
            return;
        }
        try
        {
            window.Hide();
            await Task.Delay(300);
            using var picker = new ZonePicker(Desktop.Bounds(game.Handle), label);
            if (picker.ShowDialog() == Forms.DialogResult.OK && picker.Selection != null)
            {
                Shell.Profile.Zones[key] = picker.Selection;
                Shell.SaveProfile();
                RefreshRows();
                ShowExample(key);
            }
        }
        catch (Exception e)
        {
            Shell.Log(e.Message);
        }
        finally
        {
            window.Show();
        }
    }

    public override void Refresh()
    {
        base.Refresh();
        RefreshRows();
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        var defaults = Profile.Defaults();
        var left = defaults["left"];
        string expected = $"{left.X:P1} / {left.Y:P1} · {left.W:P1} × {left.H:P1}";
        var leftRow = Zones.First(r => r.Key == "left");
        if (leftRow.Value != expected || !leftRow.Value.Contains(" / ") || !leftRow.Value.Contains(" · ") || !leftRow.Value.Contains(" × "))
            throw new InvalidOperationException($"Zone row 'left' shows '{leftRow.Value}', expected '{expected}'");
        if (Zones.Count != 11 || Zones.Select(r => r.Key).Distinct().Count() != 11)
            throw new InvalidOperationException("Expected 11 distinct zone rows");
        Shell.Profile.Zones.Remove("part");
        RefreshRows();
        var partRow = Zones.First(r => r.Key == "part");
        if (partRow.Value != NotSet)
            throw new InvalidOperationException($"Missing zone shows '{partRow.Value}' instead of '{NotSet}'");
        passed.Add("PASS zone rows show X / Y · W × H in the current percent format and 'Не задано' for a missing zone");

        var modified = new Zone(0.1, 0.2, 0.3, 0.4);
        Shell.Profile.Zones["set"] = modified;
        RefreshRows();
        var setRow = Zones.First(r => r.Key == "set");
        if (setRow.Value != Format(modified))
            throw new InvalidOperationException("Zone row did not follow a modified zone");
        RestoreDefaults();
        if (Shell.Profile.Zones["set"] != defaults["set"] || !Shell.Profile.Zones.ContainsKey("part"))
            throw new InvalidOperationException("Restoring defaults did not rewrite the modified zone");
        if (setRow.Value != Format(defaults["set"]) || partRow.Value != Format(defaults["part"]))
            throw new InvalidOperationException("Zone rows were not refreshed after restoring defaults");
        passed.Add("PASS restoring default zones rewrites a modified zone and refreshes every row");

        // Examples: every zone has a reference screen, a hint and a default frame inside the image.
        foreach (var row in Zones)
        {
            ShowExample(row.Key);
            if (!HasExample || ExampleTitle != row.Label || ExampleHint.Length < 40 || ExampleScreen == "")
                throw new InvalidOperationException($"Example of '{row.Key}' has no title, hint or screen");
            if (ExampleImage == null)
                throw new InvalidOperationException($"Reference image '{Screens[row.Screen].Image}' is missing next to the executable");
            if (ImageWidth < 100 || ImageHeight < 100)
                throw new InvalidOperationException($"Reference image of '{row.Key}' has no size");
            var r = DefaultRect;
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0 || r.Right > ImageWidth + 1 || r.Bottom > ImageHeight + 1)
                throw new InvalidOperationException($"Default frame of '{row.Key}' is outside the image: {r}");
            if (ShowCurrent)
                throw new InvalidOperationException($"Default zones must not show a dashed current frame ('{row.Key}')");
            if (!row.IsExample || Zones.Count(z => z.IsExample) != 1)
                throw new InvalidOperationException("Exactly one zone row must be marked as the shown example");
        }
        if (Screens.Values.Select(s => s.Image).Distinct().Count() != 5 || Zones.Select(z => z.Screen).Distinct().Count() != 5)
            throw new InvalidOperationException("Zones must cover the five reference screens");
        Shell.Profile.Zones["parts"] = modified;
        ShowExample("parts");
        if (!ShowCurrent || CurrentRect.Width <= 0 || CurrentRect == DefaultRect)
            throw new InvalidOperationException("A modified zone must show a dashed current frame next to the default one");
        RestoreDefaults();
        if (ShowCurrent)
            throw new InvalidOperationException("Restoring defaults did not hide the dashed current frame");
        HideExample();
        if (HasExample || ExampleImage != null || Zones.Any(z => z.IsExample))
            throw new InvalidOperationException("Hiding the example did not clear it");
        ShowExample("left");
        passed.Add("PASS every zone shows its reference screenshot with the default frame inside the image; a changed zone adds a dashed frame");

        var preview = Shell as PreviewShell;
        int before = preview?.Lines.Count ?? 0;
        var game = Shell.Game;
        Shell.Game = null;
        PickAsync("left", "Лівий перк").GetAwaiter().GetResult();
        Shell.Game = game;
        if (preview != null && (preview.Lines.Count != before + 1 || preview.Lines[^1] != "Вибери вікно гри."))
            throw new InvalidOperationException("Pick without a game window must log 'Вибери вікно гри.'");
        if (preview != null)
        {
            preview.IsBusy = true;
            try
            {
                PickAsync("left", "Лівий перк").GetAwaiter().GetResult();
            }
            finally
            {
                preview.IsBusy = false;
            }
            if (preview.Lines.Count != before + 1)
                throw new InvalidOperationException("Pick must do nothing while busy");
        }
        passed.Add("PASS zone pick without a game window logs 'Вибери вікно гри.' and is ignored while busy");
    }

    public override IReadOnlyList<PreviewVariant> PreviewVariants =>
        [
            new PreviewVariant("ui-zones-list.png", HideExample, () => ShowExample("left")),
        ];
}

/// <summary>One zone card: key in Profile.Zones, the inventory's label, the formatted current value, the reference screen and the hint, the pick and example commands.</summary>
public sealed class ZoneRow(
    string key,
    string label,
    SymbolRegular icon,
    string screen,
    string hint,
    ICommand pickCommand,
    ICommand exampleCommand
) : ObservableObject
{
    string value = ZonesViewModel.NotSet;
    bool isExample;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public SymbolRegular Icon { get; } = icon;

    /// <summary>Key into ZonesViewModel.Screens: which reference screenshot shows this zone.</summary>
    public string Screen { get; } = screen;

    /// <summary>What must be inside the selection.</summary>
    public string Hint { get; } = hint;
    public ICommand PickCommand { get; } = pickCommand;
    public ICommand ExampleCommand { get; } = exampleCommand;

    public string Value
    {
        get => value;
        set => SetProperty(ref this.value, value);
    }

    /// <summary>True for the row whose example is shown below the list.</summary>
    public bool IsExample
    {
        get => isExample;
        set => SetProperty(ref isExample, value);
    }
}
