using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;

namespace TunerFest.Ui.ViewModels;

/// <summary>One of the seven "deталей за запуск" buttons.</summary>
public sealed record PartOption(int Count, bool Selected)
{
    public string AccessibleName => $"Обробити {Count} деталей";
    public string Hint => Selected ? "Обрано" : "";
}

/// <summary>
/// The build page: catalog build, set, two perks with optional flags and alternatives, the game window and the part count.
/// Selections are written straight into Shell.Profile; the optional toggles and the part count save at once, the rest at start.
/// </summary>
public sealed class BuildViewModel : PageViewModel
{
    public static readonly Affix NoAlternative = new() { Title = "— без альтернативи —" };
    public static readonly Build CustomBuild = new() { Title = "Власна збірка" };

    readonly Catalog catalog;
    Build? selectedBuild;

    public BuildViewModel(IShellContext shell)
        : base(shell)
    {
        catalog = shell.Catalog;
        Builds = new List<Build> { CustomBuild }.Concat(catalog.Builds).ToList();
        Alternatives = new List<Affix> { NoAlternative }.Concat(catalog.Affixes).ToList();
        PartCounts = Enumerable.Range(1, 7).ToList();
        selectedBuild =
            catalog.Builds.FirstOrDefault(b =>
                b.Set == Profile.Set && b.Affixes.SequenceEqual(new[] { Profile.First, Profile.Second })
            ) ?? CustomBuild;

        RefreshWindowsCommand = new RelayCommand(RefreshWindows);
        PickPartsCommand = new RelayCommand<int>(n => Parts = n);
        ReadScreenCommand = new RelayCommand(() => Shell.Arm(false));
        InspectPngCommand = new AsyncCommand(InspectPng);
        OpenSetsCommand = new RelayCommand(() => Shell.OpenSite(Links.Sets));
        OpenSupportCommand = new RelayCommand(() => Shell.OpenSite(Links.Support));

        Shell.AutoSetApplied += OnAutoSetApplied;
        Shell.PropertyChanged += OnShellPropertyChanged;
        RefreshWindows();
    }

    Profile Profile => Shell.Profile;

    // Lists for the selectors.
    public IReadOnlyList<Build> Builds { get; }
    public IReadOnlyList<SetBonus> Sets => catalog.Sets;
    public IReadOnlyList<Affix> Affixes => catalog.Affixes;
    public IReadOnlyList<Affix> Alternatives { get; }
    public IReadOnlyList<int> PartCounts { get; }
    public ObservableCollection<GameWindow> GameWindows { get; } = [];

    /// <summary>Selectors of the set, perks, alternatives and build are editable while nothing runs and auto mode is off.</summary>
    public bool CanEditTargets => CanEdit && !AutoSet;

    public bool AutoSet
    {
        get => Profile.AutoSet;
        set
        {
            if (Profile.AutoSet == value)
                return;
            Profile.AutoSet = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditTargets));
        }
    }

    /// <summary>The catalog build (not persisted). Picking one fills the set and, for a two-perk build, both perks and clears the alternatives.</summary>
    public Build? SelectedBuild
    {
        get => selectedBuild;
        set
        {
            selectedBuild = value;
            if (value is { Set: not "" } b)
            {
                Set = catalog.Sets.FirstOrDefault(s => s.Title == b.Set);
                if (b.Affixes.Length == 2)
                {
                    First = catalog.Affixes.FirstOrDefault(a => a.Title == b.Affixes[0]);
                    Second = catalog.Affixes.FirstOrDefault(a => a.Title == b.Affixes[1]);
                    FirstAlternative = NoAlternative;
                    SecondAlternative = NoAlternative;
                }
            }
            RaiseBuild();
        }
    }

    public SetBonus? Set
    {
        get => catalog.Sets.FirstOrDefault(s => s.Title == Profile.Set);
        set
        {
            Profile.Set = value?.Title ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(SetIcon));
            OnPropertyChanged(nameof(SetDescription));
        }
    }

    public Affix? First
    {
        get => catalog.Affixes.FirstOrDefault(a => a.Title == Profile.First);
        set
        {
            Profile.First = value?.Title ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstIcon));
            OnPropertyChanged(nameof(FirstDescription));
        }
    }

    public Affix? Second
    {
        get => catalog.Affixes.FirstOrDefault(a => a.Title == Profile.Second);
        set
        {
            Profile.Second = value?.Title ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(SecondIcon));
            OnPropertyChanged(nameof(SecondDescription));
        }
    }

    public bool FirstOptional
    {
        get => Profile.FirstOptional;
        set
        {
            if (Profile.FirstOptional == value)
                return;
            Profile.FirstOptional = value;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    public bool SecondOptional
    {
        get => Profile.SecondOptional;
        set
        {
            if (Profile.SecondOptional == value)
                return;
            Profile.SecondOptional = value;
            Shell.SaveProfile();
            OnPropertyChanged();
        }
    }

    /// <summary>The alternative of the first perk: an Affix or <see cref="NoAlternative"/> (stored as "").</summary>
    public Affix FirstAlternative
    {
        get => catalog.Affixes.FirstOrDefault(a => a.Title == Profile.FirstAlternative) ?? NoAlternative;
        set
        {
            Profile.FirstAlternative = AlternativeName(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstAlternativeIcon));
            OnPropertyChanged(nameof(FirstAlternativeDescription));
        }
    }

    public Affix SecondAlternative
    {
        get => catalog.Affixes.FirstOrDefault(a => a.Title == Profile.SecondAlternative) ?? NoAlternative;
        set
        {
            Profile.SecondAlternative = AlternativeName(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SecondAlternativeIcon));
            OnPropertyChanged(nameof(SecondAlternativeDescription));
        }
    }

    static string AlternativeName(Affix? value) =>
        value is null || ReferenceEquals(value, NoAlternative) ? "" : value.Title;

    /// <summary>Parts per run, 1..7; saved at once.</summary>
    public int Parts
    {
        get => Math.Clamp(Profile.Parts, 1, 7);
        set
        {
            int next = Math.Clamp(value, 1, 7);
            if (Profile.Parts == next)
                return;
            Profile.Parts = next;
            Shell.SaveProfile();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PartOptions));
        }
    }

    public IReadOnlyList<PartOption> PartOptions => PartCounts.Select(n => new PartOption(n, n == Parts)).ToList();

    public GameWindow? SelectedWindow
    {
        get => Shell.Game;
        set
        {
            if (Equals(Shell.Game, value))
                return;
            Shell.Game = value;
            OnPropertyChanged();
        }
    }

    // Icons and one-line descriptions that follow the selection.
    public ImageSource? BuildIcon => Icon(SelectedBuild);
    public string BuildDescription => Describe(SelectedBuild);
    public ImageSource? SetIcon => Icon(Set);
    public string SetDescription => Describe(Set);
    public ImageSource? FirstIcon => Icon(First);
    public string FirstDescription => Describe(First);
    public ImageSource? SecondIcon => Icon(Second);
    public string SecondDescription => Describe(Second);
    public ImageSource? FirstAlternativeIcon => Icon(FirstAlternative);
    public string FirstAlternativeDescription => Describe(FirstAlternative);
    public ImageSource? SecondAlternativeIcon => Icon(SecondAlternative);
    public string SecondAlternativeDescription => Describe(SecondAlternative);

    static ImageSource? Icon(object? item) => PerkIcons.For(PerkIcons.IconTitle(item), 32);

    public ICommand RefreshWindowsCommand { get; }
    public ICommand PickPartsCommand { get; }
    public ICommand ReadScreenCommand { get; }
    public ICommand InspectPngCommand { get; }
    public ICommand OpenSetsCommand { get; }
    public ICommand OpenSupportCommand { get; }

    /// <summary>
    /// Reloads the game windows: every top-level window of a Motorfest process except our own. Keeps the previous handle
    /// when it is still there, otherwise takes the first window whose title contains "Motorfest". Empty lists are fine (previews).
    /// </summary>
    public void RefreshWindows()
    {
        var old = Shell.Game?.Handle;
        IntPtr own = IntPtr.Zero;
        if (Shell.Window is { } window)
        {
            try
            {
                own = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            }
            catch
            {
                own = IntPtr.Zero;
            }
        }
        List<GameWindow> found;
        try
        {
            found = Desktop.Windows().Where(w => w.Handle != own && Desktop.IsGame(w.Handle)).ToList();
        }
        catch (Exception e)
        {
            found = [];
            Shell.Log("Не вдалося отримати список вікон: " + e.Message);
        }
        GameWindows.Clear();
        foreach (var w in found)
            GameWindows.Add(w);
        var next =
            found.FirstOrDefault(w => w.Handle == old)
            ?? found.FirstOrDefault(w => w.Title.Contains("Motorfest", StringComparison.OrdinalIgnoreCase));
        Shell.Game = next;
        OnPropertyChanged(nameof(SelectedWindow));
    }

    async Task InspectPng()
    {
        if (Shell.Window is null)
        {
            Shell.Log("Перевірка PNG: у режимі перегляду діалог не відкривається.");
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PNG screenshots|*.png" };
        if (dialog.ShowDialog(Shell.Window) != true)
            return;
        await Shell.InspectFile(dialog.FileName);
    }

    /// <summary>Auto mode found the set: take the set, both perks and the build from the choice, clear the alternatives and save.</summary>
    void OnAutoSetApplied(Build choice)
    {
        var set = catalog.Sets.FirstOrDefault(s => Recognition.Normalize(s.Title) == Recognition.Normalize(choice.Set));
        if (set != null)
            Profile.Set = set.Title;
        if (choice.Affixes.Length > 0)
            Profile.First = catalog.Affixes.FirstOrDefault(a => a.Title == choice.Affixes[0])?.Title ?? Profile.First;
        if (choice.Affixes.Length > 1)
            Profile.Second = catalog.Affixes.FirstOrDefault(a => a.Title == choice.Affixes[1])?.Title ?? Profile.Second;
        Profile.FirstAlternative = Profile.SecondAlternative = "";
        selectedBuild = choice;
        Shell.SaveProfile();
        Refresh();
    }

    void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IShellContext.IsBusy))
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanEditTargets));
        }
        else if (e.PropertyName is nameof(IShellContext.Game))
            OnPropertyChanged(nameof(SelectedWindow));
    }

    void RaiseBuild()
    {
        OnPropertyChanged(nameof(SelectedBuild));
        OnPropertyChanged(nameof(BuildIcon));
        OnPropertyChanged(nameof(BuildDescription));
    }

    public override void Refresh()
    {
        base.Refresh();
        OnPropertyChanged(nameof(CanEditTargets));
        OnPropertyChanged(nameof(AutoSet));
        RaiseBuild();
        foreach (
            var name in new[]
            {
                nameof(Set),
                nameof(SetIcon),
                nameof(SetDescription),
                nameof(First),
                nameof(FirstIcon),
                nameof(FirstDescription),
                nameof(Second),
                nameof(SecondIcon),
                nameof(SecondDescription),
                nameof(FirstOptional),
                nameof(SecondOptional),
                nameof(FirstAlternative),
                nameof(FirstAlternativeIcon),
                nameof(FirstAlternativeDescription),
                nameof(SecondAlternative),
                nameof(SecondAlternativeIcon),
                nameof(SecondAlternativeDescription),
                nameof(Parts),
                nameof(PartOptions),
                nameof(SelectedWindow),
            }
        )
            OnPropertyChanged(name);
    }

    /// <summary>One line in the style of tunerfest.app: what the item does, its numbers and its tag.</summary>
    public static string Describe(object? item)
    {
        static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        static string Clean(string s) =>
            string.Join(
                " · ",
                s.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())
            );
        static string Tail(string text, string[]? tags)
        {
            string t = string.Join(", ", tags ?? []);
            return t == "" ? text : text + " · " + t;
        }
        return item switch
        {
            Build { Set: "" } => "Сет і перки обираються нижче вручну.",
            Build b => Tail(
                Clean(b.Description) is "" ? b.Set + ": " + string.Join(" + ", b.Affixes) : Clean(b.Description),
                b.Tags
            ),
            SetBonus s => Tail(
                $"{Clean(s.Description)} · 3 деталі: +{Num(s.BonusPercentageThreeParts)}% · повний сет: +{Num(s.BonusPercentageFull)}%",
                s.Tags
            ),
            Affix a when ReferenceEquals(a, NoAlternative) || a.Description == "" => "",
            Affix a => Tail(
                $"{Clean(a.Description)} · {Num(a.BonusPercentageRangeFrom)}–{Num(a.BonusPercentageRangeTo)}%",
                a.Tags
            ),
            _ => "",
        };
    }

    public override void SmokeChecks(ICollection<string> passed)
    {
        var p = Profile;
        Affix A(string title) =>
            catalog.Affixes.FirstOrDefault(a => a.Title == title)
            ?? throw new InvalidOperationException("Catalog has no perk " + title);

        // Part-count buttons.
        PickPartsCommand.Execute(1);
        if (p.Parts != 1 || PartOptions.Single(o => o.Selected).Count != 1)
            throw new InvalidOperationException("Part count button 1 did not update the profile");
        PickPartsCommand.Execute(7);
        if (p.Parts != 7 || PartOptions.Single(o => o.Selected).Count != 7)
            throw new InvalidOperationException("Part count button 7 did not update the profile");
        passed.Add("PASS seven count buttons update selection and saved setting");

        // Optional toggle and alternatives.
        FirstOptional = true;
        if (!p.FirstOptional || p.RequiredNames().Length != 1 || p.RequiredNames()[0] != p.Second)
            throw new InvalidOperationException("Optional perk toggle did not update the profile");
        FirstOptional = false;
        SecondAlternative = A("Rich");
        ProfileRules.Validate(p);
        if (!p.Goals()[1].Names.SequenceEqual(new[] { "Extra Pump", "Rich" }) || p.GoalIndex("Rich") != 1)
            throw new InvalidOperationException("Alternative perk did not form a two-name goal");
        SecondAlternative = A("Pure");
        bool duplicateRejected = false;
        try
        {
            ProfileRules.Validate(p);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        if (!duplicateRejected)
            throw new InvalidOperationException("Duplicate alternative was not rejected");
        SecondAlternative = NoAlternative;
        ProfileRules.Validate(p);
        if (p.SecondAlternative != "" || !ReferenceEquals(SecondAlternative, NoAlternative))
            throw new InvalidOperationException("Alternative did not clear");
        passed.Add("PASS optional toggle and alternative perk goals save, duplicates rejected");

        // Icons and descriptions follow the selection, also in the open list.
        SelectedBuild = catalog.Builds.FirstOrDefault(b => b.Title == "Nitro Chemist")
            ?? throw new InvalidOperationException("Catalog has no build Nitro Chemist");
        if (
            BuildIcon == null
            || BuildDescription == ""
            || !BuildDescription.Contains("NITRO")
            || !SetDescription.Contains("повний сет: +50%")
            || !FirstDescription.Contains("3–5%")
            || !SecondDescription.Contains("speed")
        )
            throw new InvalidOperationException("Build, set or perk description did not follow the selection");
        string cluster = Describe(A("Cluster"));
        if (cluster != "Increase GEARSHIFTING BOOST EFFECT in DRAG RACE events · 1.2–2% · speed")
            throw new InvalidOperationException("Perk description format changed: " + cluster);
        SecondAlternative = A("Rich");
        if (SecondAlternativeIcon == null || !SecondAlternativeDescription.Contains("%"))
            throw new InvalidOperationException("Alternative perk icon or description missing");
        if (new PerkIconConverter().Convert(A("Pure"), typeof(ImageSource), null, System.Globalization.CultureInfo.InvariantCulture) == null)
            throw new InvalidOperationException("Dropdown item icon missing");
        if (PerkIcons.For(PerkIcons.IconTitle(CustomBuild), 20) != null || PerkIcons.For(PerkIcons.IconTitle(NoAlternative), 20) != null)
            throw new InvalidOperationException("Custom build or empty alternative must have no icon");
        SecondAlternative = NoAlternative;
        if (SecondAlternativeIcon != null || SecondAlternativeDescription != "")
            throw new InvalidOperationException("Cleared alternative kept its icon or description");
        passed.Add("PASS build, set and perk icons and descriptions follow the selection, also in the open list");
    }

    public override IReadOnlyList<PreviewVariant> PreviewVariants
    {
        get
        {
            bool prior = false;
            return
            [
                new PreviewVariant(
                    "ui-auto-set.png",
                    () =>
                    {
                        prior = AutoSet;
                        AutoSet = true;
                        if (CanEditTargets)
                            throw new InvalidOperationException("Manual goal fields must be disabled in auto mode");
                    },
                    () => AutoSet = prior
                ),
            ];
        }
    }
}
