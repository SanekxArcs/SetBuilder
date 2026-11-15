namespace TunerFest;

/// <summary>A personal preset, kept in the local profile independently of catalog downloads.</summary>
public sealed record SavedCombination
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Set { get; init; } = "";
    public string First { get; init; } = "";
    public string Second { get; init; } = "";
    public string FirstAlternative { get; init; } = "";
    public string SecondAlternative { get; init; } = "";
    public bool FirstOptional { get; init; }
    public bool SecondOptional { get; init; }
    public bool IgnoreSet { get; init; }
    public int Parts { get; init; } = 7;

    public static SavedCombination Capture(Profile profile, string name, string? id = null) => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
        Name = name.Trim(),
        Set = profile.Set,
        First = profile.First,
        Second = profile.Second,
        FirstAlternative = profile.FirstAlternative,
        SecondAlternative = profile.SecondAlternative,
        FirstOptional = profile.FirstOptional,
        SecondOptional = profile.SecondOptional,
        IgnoreSet = profile.IgnoreSet,
        Parts = Math.Clamp(profile.Parts, 1, 7),
    };

    public void Apply(Profile profile)
    {
        profile.AutoSet = false;
        profile.Set = Set;
        profile.First = First;
        profile.Second = Second;
        profile.FirstAlternative = FirstAlternative;
        profile.SecondAlternative = SecondAlternative;
        profile.FirstOptional = FirstOptional;
        profile.SecondOptional = SecondOptional;
        profile.IgnoreSet = IgnoreSet;
        profile.Parts = Math.Clamp(Parts, 1, 7);
        profile.SelectedCombinationId = Id;
    }

    public bool Matches(Profile profile) => this == Capture(profile, Name, Id);
}
