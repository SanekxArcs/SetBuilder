namespace TunerFest;
public static class AutoSetResolver
{
    public static Build Resolve(Catalog catalog,string set)
    {
        string key=Recognition.Normalize(set);
        var candidates=catalog.Builds.Where(b=>Recognition.Normalize(b.Set)==key&&b.Affixes.Length==2&&b.Affixes.Distinct().Count()==2&&b.Affixes.All(n=>catalog.Affixes.Any(a=>a.Title==n))).ToList();
        if(candidates.Count==0)throw new InvalidOperationException("Для сету "+set+" немає повного профілю TunerFest. Обери перки вручну.");
        return candidates.FirstOrDefault(b=>Recognition.Normalize(b.Title)==key)??candidates.OrderBy(b=>b.Title,StringComparer.Ordinal).First();
    }
}
