namespace TunerFest;

public static class AutoSetResolver
{
    public static Build Resolve(Catalog catalog, string set)
    {
        string key = Recognition.Normalize(set);
        var candidates = catalog
            .Builds.Where(b =>
                Recognition.Normalize(b.Set) == key
                && b.Affixes.Length == 2
                && b.Affixes.Distinct().Count() == 2
                && b.Affixes.All(n => catalog.Affixes.Any(a => a.Title == n))
            )
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "Для сету " + set + " немає повного профілю TunerFest. Обери перки вручну."
            );
        return candidates.FirstOrDefault(b => Recognition.Normalize(b.Title) == key)
            ?? candidates.OrderBy(b => b.Title, StringComparer.Ordinal).First();
    }

    /// <summary>
    /// Writes the detected set and the preset's perks into the profile. With AutoSecondManual the second perk and its
    /// alternative stay as the user chose them: the preset supplies the first perk, taking its other perk when the
    /// user's second is the same as the preset's first. Alternatives of the first perk are cleared; a second
    /// alternative equal to the new first perk is cleared too. Idempotent, so the runner and the shell may both call it.
    /// </summary>
    public static void Apply(Profile p, string set, Build choice)
    {
        p.Set = set;
        p.FirstAlternative = "";
        if (p.AutoSet && p.AutoSecondManual && p.Second != "")
        {
            p.First = choice.Affixes[0] == p.Second ? choice.Affixes[1] : choice.Affixes[0];
            if (p.SecondAlternative == p.First)
                p.SecondAlternative = "";
            return;
        }
        p.First = choice.Affixes[0];
        p.Second = choice.Affixes[1];
        p.SecondAlternative = "";
    }

    /// <summary>One line for the log: the detected set, the goals and where the second perk came from.</summary>
    public static string Describe(Profile p, string set, Build choice)
    {
        string second = p.AutoSet && p.AutoSecondManual ? p.Second + " (вручну)" : p.Second;
        string tail = p.IgnoreSet ? " Сет деталей не перевіряється." : "";
        return $"Автосет: {set} → {p.First} + {second}. Профіль: {choice.Title}.{tail}";
    }
}
