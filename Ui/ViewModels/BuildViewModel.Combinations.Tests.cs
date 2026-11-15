using System.Text.Json;

namespace TunerFest.Ui.ViewModels;

public sealed partial class BuildViewModel
{
    void VerifyCombinations(ICollection<string> passed)
    {
        // An isolated preview profile: no personal combinations or game input are touched.
        var profile = new Profile
        {
            First = "Legend", Second = "Rich", FirstAlternative = "Pure", SecondAlternative = "Extra Pump",
            SecondOptional = true, IgnoreSet = true, Parts = 3,
        };
        var preview = new PreviewShell(catalog, profile);
        var vm = new BuildViewModel(preview);
        int catalogCount = catalog.Builds.Count;
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Personal combinations: " + message);
        }

        vm.CombinationName = "  My reward set  ";
        vm.SaveCombinationCommand.Execute(null);
        Check(profile.CustomCombinations.Count == 1, "save should add one preset");
        var saved = profile.CustomCombinations.Single();
        Check(saved.Name == "My reward set" && saved.Matches(profile), "save must snapshot all target settings");
        Check(vm.SelectedCombination?.Id == saved.Id && vm.Builds.Count == catalogCount + 2, "saved preset should be selected in the build list");

        vm.CombinationName = "my REWARD set";
        vm.SaveCombinationCommand.Execute(null);
        Check(profile.CustomCombinations.Count == 1 && vm.CombinationFeedback != "", "duplicate names must not overwrite a preset");

        // Changing targets leaves the immutable saved snapshot alone until Update is chosen.
        vm.Parts = 6;
        vm.SecondOptional = false;
        vm.FirstAlternative = NoAlternative;
        Check(saved.Parts == 3 && saved.SecondOptional && saved.FirstAlternative == "Pure", "editing must not mutate the saved preset");
        vm.CombinationName = "Updated reward set";
        vm.UpdateCombinationCommand.Execute(null);
        Check(profile.CustomCombinations.Single().Id == saved.Id && profile.CustomCombinations.Single().Parts == 6,
            "update must keep identity and save changed settings");

        // Persisted data and selection survive a restart, with all alternatives and flags restored on selection.
        var loaded = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile))!;
        var restarted = new BuildViewModel(new PreviewShell(catalog, loaded));
        Check(restarted.SelectedCombination?.Name == "Updated reward set", "restart should restore the selected personal preset");
        restarted.SelectedBuild = catalog.Builds.First(b => b.Title == "Nitro Chemist");
        Check(restarted.SelectedCombination == null && !restarted.DeleteCombinationCommand.CanExecute(null), "catalog builds must not be deletable");
        restarted.SelectedBuild = restarted.personalBuilds.Keys.Single();
        Check(loaded.SecondAlternative == "Extra Pump" && loaded.FirstAlternative == "" && loaded.IgnoreSet && loaded.Parts == 6
            && !loaded.FirstOptional && !loaded.SecondOptional && loaded.First == "Legend" && loaded.Second == "Rich" && !loaded.AutoSet,
            "selection must restore the saved target configuration");

        var busy = new PreviewShell(catalog, loaded) { IsBusy = true };
        var busyVm = new BuildViewModel(busy) { CombinationName = "Busy" };
        busyVm.SaveCombinationCommand.Execute(null);
        busyVm.DeleteCombinationCommand.Execute(null);
        Check(loaded.CustomCombinations.Count == 1 && !busyVm.SaveCombinationCommand.CanExecute(null), "mutations must be refused during a run");

        restarted.DeleteCombinationCommand.Execute(null);
        Check(loaded.CustomCombinations.Count == 0 && loaded.First == "Legend" && loaded.SelectedCombinationId == ""
            && catalog.Builds.Count == catalogCount, "delete must keep current targets and the public catalog");
        restarted.CombinationName = "Invalid pair";
        restarted.Second = restarted.First;
        restarted.SaveCombinationCommand.Execute(null);
        Check(loaded.CustomCombinations.Count == 0, "invalid perk combinations must not be saved");
        Check(JsonSerializer.Deserialize<Profile>("{}")!.CustomCombinations.Count == 0, "older profiles must load with no personal presets");
        passed.Add("PASS personal combinations: create, duplicate rejection, immutable edits, update, profile round-trip, restart selection, restore all targets, busy guard, delete and validation");
    }
}
