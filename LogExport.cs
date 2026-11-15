using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace TunerFest;

/// <summary>
/// Packs every recorded session into one small ZIP that a player can send to the author (Discord, feedback form, any file share)
/// to build global statistics. Only text and roll data go in: no screenshots, no per-frame event dumps (except for legacy
/// sessions that have no rolls.jsonl), no duplicated catalog. The Windows user folder is replaced with %USERPROFILE%.
/// Nothing is sent anywhere; the player decides what to do with the file.
/// </summary>
public static class LogExport
{
    /// <summary>Files copied from each session folder. events.jsonl is added only when rolls.jsonl is missing (legacy sessions).</summary>
    static readonly string[] SessionFiles =
    [
        "session.json",
        "summary.json",
        "spending.json",
        "settings.json",
        "effective-settings.json",
        "rolls.jsonl",
        "rolls.csv",
        "legacy-rolls.csv",
        "legacy-spending.json",
        "session.log",
    ];

    public sealed record Result(string Path, int Sessions, int Rolls, long Bytes);

    public static string DefaultFileName() => $"SetBuilder-logs-{DateTime.Now:yyyy-MM-dd}.zip";

    /// <summary>Session folders of this installation only (never logs imported from other players through Analytics).</summary>
    public static List<string> Sessions(string root) =>
        Directory.Exists(root)
            ? Directory
                .GetDirectories(root)
                .Where(d => File.Exists(Path.Combine(d, "session.json")))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];

    public static Result Create(string destination, Catalog catalog, string? root = null)
    {
        root ??= SessionLog.Root;
        var sessions = Sessions(root);
        var data = AnalyticsStore.Load(catalog, [root]);
        var stats = Statistics(data, catalog);

        string temp = destination + ".partial";
        if (File.Exists(temp))
            File.Delete(temp);
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            Text(zip, "README.txt", Readme(sessions.Count));
            Text(zip, "statistics.json", JsonSerializer.Serialize(stats, Catalog.JsonOptions));
            foreach (string folder in sessions)
            {
                string name = Path.GetFileName(folder);
                bool hasRolls = File.Exists(Path.Combine(folder, "rolls.jsonl"));
                foreach (string file in hasRolls ? SessionFiles : [.. SessionFiles, "events.jsonl"])
                {
                    string path = Path.Combine(folder, file);
                    if (!File.Exists(path))
                        continue;
                    string content;
                    try
                    {
                        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(stream);
                        content = reader.ReadToEnd();
                    }
                    catch (IOException)
                    {
                        continue; // a file being written by a live run; the rest of the session still goes in
                    }
                    Text(zip, $"sessions/{name}/{file}", Scrub(content));
                }
            }
        }
        File.Move(temp, destination, true);
        return new Result(destination, sessions.Count, stats.Rolls, new FileInfo(destination).Length);
    }

    public sealed record Stats(
        string App,
        string Version,
        DateTimeOffset ExportedAt,
        int Sessions,
        int Rolls,
        long Bucks,
        long SpareParts,
        int NewOffersRecognized,
        int NoNameOrOffer,
        int UnconfirmedInputs,
        int CompletedParts,
        double? AverageRollsPerPart,
        double? AverageBucksPerPart,
        double? AverageSparePartsPerPart,
        double? SessionSeconds,
        double? AverageSecondsPerPart,
        List<PerkFrequency> Leaderboard
    );

    /// <summary>The same totals the Analytics page shows for all sessions.</summary>
    public static Stats Statistics(AnalyticsData data, Catalog catalog)
    {
        var rolls = data.Sessions.SelectMany(s => s.Rolls).ToList();
        var paid = rolls.Where(r => r.Paid).ToList();
        var leaderboard = AnalyticsData.Leaderboard(catalog, paid);
        int known = leaderboard.Sum(p => p.Count);
        var items = ItemAnalytics.Summary(data.Sessions);
        return new Stats(
            "TunerFest Set Builder",
            Ui.AppInfo.Version,
            DateTimeOffset.Now,
            data.Sessions.Count,
            paid.Count,
            paid.Sum(r => r.Bucks),
            paid.Sum(r => r.SpareParts),
            known,
            paid.Count - known,
            rolls.Count(r => r.Requested && !r.Paid && !r.NotSent),
            items.CompletedItems,
            items.AverageRolls,
            items.AverageBucks,
            items.AverageSpareParts,
            items.TotalDurationSeconds,
            items.AverageDurationSeconds,
            leaderboard
        );
    }

    /// <summary>Replaces the Windows profile path (plain, JSON-escaped and forward-slash forms) so the file carries no user name.</summary>
    public static string Scrub(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        if (profile.Length < 4)
            return text;
        return text.Replace(profile.Replace("\\", "\\\\"), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(profile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    static void Text(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    static string Readme(int sessions) =>
        $"""
        TunerFest Set Builder {Ui.AppInfo.Version}: exported logs
        Exported: {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}
        Sessions: {sessions}

        This file was made by the player with "Export logs" in the app. The app never sends anything by itself.
        It is meant for the global perk and cost statistics that will be published on https://tunerfest.app.

        Inside:
          statistics.json         totals, averages and the perk frequency table across all sessions
          sessions/<id>/          per-run text logs, roll ledger, spending and the settings used

        Not inside: screenshots, per-frame event dumps (except old sessions without a roll ledger), account data.
        The Windows user folder is replaced with %USERPROFILE%.
        """;
}
