using System.Collections.ObjectModel;
using System.Windows.Input;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace TunerFest.Ui.ViewModels;

/// <summary>Tab "Зони екрана": one card per screen zone, picked with the WinForms ZonePicker over the game window.</summary>
public sealed class ZonesViewModel : PageViewModel
{
    // Inventory order (tab 1 of the old UI); the keys match Profile.Zones and Profile.Defaults().
    static readonly (string Key, string Label, SymbolRegular Icon)[] Table =
    [
        ("left", "Лівий перк", SymbolRegular.RectangleLandscape24),
        ("right", "Правий перк", SymbolRegular.RectangleLandscape24),
        ("set", "Назва сету", SymbolRegular.RectangleLandscape24),
        ("part", "Заголовок деталі", SymbolRegular.RectangleLandscape24),
        ("topLeft", "Лівий: новий перк зверху", SymbolRegular.Crop24),
        ("middleLeft", "Лівий: повторний кидок посередині", SymbolRegular.Crop24),
        ("topRight", "Правий: новий перк зверху", SymbolRegular.Crop24),
        ("middleRight", "Правий: повторний кидок посередині", SymbolRegular.Crop24),
        ("parts", "Ряд усіх семи деталей", SymbolRegular.RectangleLandscape24),
        ("breadcrumb", "Шлях меню вгорі", SymbolRegular.RectangleLandscape24),
        ("inventoryHeader", "Заголовок списку, напр. BRAKES", SymbolRegular.RectangleLandscape24),
    ];

    public const string NotSet = "Не задано";

    public ZonesViewModel(IShellContext shell)
        : base(shell)
    {
        foreach (var (key, label, icon) in Table)
            Zones.Add(new ZoneRow(key, label, icon, new AsyncCommand(() => PickAsync(key, label))));
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        RefreshRows();
    }

    public ObservableCollection<ZoneRow> Zones { get; } = [];

    public ICommand RestoreDefaultsCommand { get; }

    public static string Format(Zone? z) => z == null ? NotSet : $"{z.X:P1} / {z.Y:P1} · {z.W:P1} × {z.H:P1}";

    /// <summary>Re-reads every zone from the profile (UpdateZoneLabels of the old UI).</summary>
    public void RefreshRows()
    {
        foreach (var row in Zones)
            row.Value = Format(Shell.Profile.Zones.GetValueOrDefault(row.Key));
    }

    /// <summary>"Відновити зони зі скріншотів": Profile.Defaults(), refresh, save.</summary>
    public void RestoreDefaults()
    {
        Shell.Profile.Zones = Profile.Defaults();
        RefreshRows();
        Shell.SaveProfile();
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
}

/// <summary>One zone card: key in Profile.Zones, the inventory's label, the formatted current value and the pick command.</summary>
public sealed class ZoneRow(string key, string label, SymbolRegular icon, ICommand pickCommand) : ObservableObject
{
    string value = ZonesViewModel.NotSet;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public SymbolRegular Icon { get; } = icon;
    public ICommand PickCommand { get; } = pickCommand;

    public string Value
    {
        get => value;
        set => SetProperty(ref this.value, value);
    }
}
