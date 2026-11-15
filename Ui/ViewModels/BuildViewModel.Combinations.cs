namespace TunerFest.Ui.ViewModels;

public sealed partial class BuildViewModel
{
    readonly Dictionary<Build, SavedCombination> personalBuilds = [];
    string combinationName = "";
    string combinationFeedback = "";
    bool rebuildingCombinations;

    public RelayCommand SaveCombinationCommand { get; private set; } = null!;
    public RelayCommand UpdateCombinationCommand { get; private set; } = null!;
    public RelayCommand DeleteCombinationCommand { get; private set; } = null!;

    public string CombinationName
    {
        get => combinationName;
        set
        {
            if (SetProperty(ref combinationName, value ?? ""))
                RefreshCombinationCommands();
        }
    }

    public string CombinationFeedback
    {
        get => combinationFeedback;
        private set => SetProperty(ref combinationFeedback, value);
    }

    SavedCombination? SelectedCombination => selectedBuild != null && personalBuilds.TryGetValue(selectedBuild, out var saved)
        ? saved : null;

    void InitializeCombinations()
    {
        Profile.CustomCombinations ??= [];
        SaveCombinationCommand = new RelayCommand(() => SaveCombination(false), () => CanEditTargets && !string.IsNullOrWhiteSpace(CombinationName));
        UpdateCombinationCommand = new RelayCommand(() => SaveCombination(true), () => CanEditTargets && SelectedCombination != null && !string.IsNullOrWhiteSpace(CombinationName));
        DeleteCombinationCommand = new RelayCommand(DeleteCombination, () => CanEditTargets && SelectedCombination != null);
        FillBuildList();
    }

    void FillBuildList()
    {
        rebuildingCombinations = true;
        try
        {
            Builds.Clear();
            personalBuilds.Clear();
            Builds.Add(CustomBuild);
            foreach (var saved in Profile.CustomCombinations)
            {
                var item = new Build
                {
                    Title = saved.Name + " · " + L.T("Моя комбінація"),
                    Set = saved.Set,
                    Affixes = [saved.First, saved.Second],
                    Description = DescribeCombination(saved),
                };
                personalBuilds.Add(item, saved);
                Builds.Add(item);
            }
            foreach (var build in catalog.Builds)
                Builds.Add(build);
        }
        finally { rebuildingCombinations = false; }
    }

    Build? SavedBuildAtStartup()
    {
        var item = personalBuilds.FirstOrDefault(p => p.Value.Id == Profile.SelectedCombinationId && p.Value.Matches(Profile));
        if (item.Key != null)
            CombinationName = item.Value.Name;
        return item.Key;
    }

    void SelectBuild(Build? value)
    {
        if (rebuildingCombinations || !CanEditTargets || value == null || ReferenceEquals(value, selectedBuild))
            return;
        selectedBuild = value;
        CombinationFeedback = "";
        if (personalBuilds.TryGetValue(value, out var saved))
        {
            saved.Apply(Profile);
            CombinationName = saved.Name;
        }
        else
        {
            Profile.SelectedCombinationId = "";
            CombinationName = "";
            if (value.Set != "")
            {
                Profile.Set = value.Set;
                if (value.Affixes.Length == 2)
                {
                    Profile.First = value.Affixes[0];
                    Profile.Second = value.Affixes[1];
                    Profile.FirstAlternative = Profile.SecondAlternative = "";
                }
            }
        }
        Shell.SaveProfile();
        Refresh();
    }

    void SaveCombination(bool update)
    {
        if (!CanEditTargets || (update && SelectedCombination == null))
            return;
        var name = CombinationName.Trim();
        if (name.Length is 0 or > 80)
        {
            CombinationFeedback = L.T("Введіть назву комбінації (1–80 символів).");
            return;
        }
        var selectedId = update ? SelectedCombination!.Id : null;
        if (Profile.CustomCombinations.Any(s => s.Id != selectedId && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            CombinationFeedback = L.T("Така назва вже є. Оберіть іншу або натисніть «Оновити вибрану».");
            return;
        }
        try
        {
            ProfileRules.Validate(Profile);
            if ((!Profile.IgnoreSet && Set == null) || First == null || Second == null)
                throw new InvalidOperationException(L.T("Оберіть сет і два перки перед збереженням."));
        }
        catch (InvalidOperationException error)
        {
            CombinationFeedback = error.Message;
            return;
        }

        var saved = SavedCombination.Capture(Profile, name, selectedId);
        var previous = Profile.CustomCombinations;
        var previousId = Profile.SelectedCombinationId;
        Profile.CustomCombinations = update
            ? previous.Select(s => s.Id == saved.Id ? saved : s).ToList()
            : [.. previous, saved];
        Profile.SelectedCombinationId = saved.Id;
        if (!PersistCombinations(previous, previousId))
            return;
        RebuildSelection(saved.Id);
        CombinationFeedback = L.T(update ? "Комбінацію оновлено." : "Комбінацію збережено. Обирайте її зі списку збірок.");
    }

    void DeleteCombination()
    {
        if (!CanEditTargets || SelectedCombination is not { } saved)
            return;
        var previous = Profile.CustomCombinations;
        var previousId = Profile.SelectedCombinationId;
        Profile.CustomCombinations = previous.Where(s => s.Id != saved.Id).ToList();
        Profile.SelectedCombinationId = "";
        if (!PersistCombinations(previous, previousId))
            return;
        RebuildSelection("");
        CombinationFeedback = L.T("Комбінацію видалено. Поточні перки залишилися вибраними.");
    }

    bool PersistCombinations(List<SavedCombination> previous, string previousId)
    {
        try
        {
            if (!Shell.Preview)
                Profile.Save();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Profile.CustomCombinations = previous;
            Profile.SelectedCombinationId = previousId;
            CombinationFeedback = L.T("Не вдалося зберегти комбінацію: ") + error.Message;
            return false;
        }
    }

    void RebuildSelection(string id)
    {
        FillBuildList();
        selectedBuild = personalBuilds.FirstOrDefault(p => p.Value.Id == id).Key ?? CustomBuild;
        CombinationName = SelectedCombination?.Name ?? "";
        Refresh();
    }

    void RefreshCombinationCommands()
    {
        SaveCombinationCommand?.RaiseCanExecuteChanged();
        UpdateCombinationCommand?.RaiseCanExecuteChanged();
        DeleteCombinationCommand?.RaiseCanExecuteChanged();
    }

    static string DescribeCombination(SavedCombination saved)
    {
        static string Goal(string main, string alternative, bool optional) =>
            string.Join(L.T(" або "), new[] { main, alternative }.Where(n => n != ""))
            + (optional ? L.T(" (необов'язковий)") : "");
        return Goal(saved.First, saved.FirstAlternative, saved.FirstOptional) + " + "
            + Goal(saved.Second, saved.SecondAlternative, saved.SecondOptional);
    }
}
