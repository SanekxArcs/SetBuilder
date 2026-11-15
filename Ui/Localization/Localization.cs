using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace TunerFest;

/// <summary>App copy uses the original Ukrainian text as its stable translation key.
/// Diagnostic files retain their original messages so older log readers keep working.</summary>
public static class L
{
    static readonly IReadOnlyDictionary<string, string> English = Load();
    static readonly (string Source, string Target, Regex Pattern)[] Templates = English
        .Where(p => Regex.IsMatch(p.Key, @"\{\d+\}"))
        .OrderByDescending(p => Regex.Replace(p.Key, @"\{\d+\}", "").Length)
        .Select(p => (p.Key, p.Value, new Regex(
            "\\A" + Regex.Replace(Regex.Escape(p.Key), @"\\\{(\d+)}", m => "(?<p" + m.Groups[1].Value + ">.*?)") + "\\z",
            RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50))))
        .ToArray();
    static ResourceDictionary? resources;

    public static string Language { get; private set; } = "en";
    public static bool IsUkrainian => Language == "uk";
    public static string Normalize(string? language) => language?.ToLowerInvariant() is "uk" or "ua" ? "uk" : "en";

    public static void Configure(string? language)
    {
        Language = Normalize(language);
        var culture = CultureInfo.GetCultureInfo(IsUkrainian ? "uk-UA" : "en-US");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static void ApplyResources()
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        if (resources != null)
            dictionaries.Remove(resources);
        resources = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/TunerFest Set Builder;component/Ui/Localization/Strings.{Language}.xaml"),
        };
        dictionaries.Add(resources);
    }

    public static string T(string source) => !IsUkrainian && English.TryGetValue(source, out var value) ? value : source;

    public static string F(string source, params object[] values) => string.Format(
        CultureInfo.CurrentCulture, T(source), values.Select(v => v is string text ? Display(text) : v).ToArray());

    /// <summary>Translate rendered diagnostic messages at the display boundary only.</summary>
    public static string Display(string source) => Display(source, 0);

    static string Display(string source, int depth)
    {
        if (IsUkrainian || depth > 3 || string.IsNullOrEmpty(source))
            return source;
        if (English.TryGetValue(source, out var exact))
            return exact;
        foreach (var (_, target, pattern) in Templates)
        {
            var match = pattern.Match(source);
            if (!match.Success)
                continue;
            return Regex.Replace(target, @"\{(\d+)\}", m => Display(match.Groups["p" + m.Groups[1].Value].Value, depth + 1));
        }
        // Messages with appended paths / exception details have a translated prefix.
        foreach (var entry in English.Where(p => p.Key.EndsWith(": ") || p.Key.EndsWith(". ")).OrderByDescending(p => p.Key.Length))
            if (source.StartsWith(entry.Key, StringComparison.Ordinal))
                return entry.Value + Display(source[entry.Key.Length..], depth + 1);
        return source;
    }

    static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("TunerFest.Ui.Localization.en.json")
            ?? throw new InvalidOperationException("English translations are missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
