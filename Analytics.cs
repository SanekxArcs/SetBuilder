using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TunerFest;

public sealed class AnalyticsRoll
{
    public DateTimeOffset? Time { get; set; }
    public int Id { get; set; }
    public bool Requested { get; set; }
    public bool Paid { get; set; }
    public bool NotSent { get; set; }
    public string Phase { get; set; } = "Unknown";
    public string Part { get; set; } = "";
    public int Slot { get; set; }
    public string? Fresh { get; set; }
    public double? Value { get; set; }
    public double? Maximum { get; set; }
    public string? Other { get; set; }
    public long Bucks { get; set; }
    public long SpareParts { get; set; }
}

public sealed class AnalyticsSession
{
    public string Mode { get; set; } = "automation";
    public string Id { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Version { get; set; } = "";
    public string Set { get; set; } = "";
    public double? DurationSeconds { get; set; }
    public DateTimeOffset Started { get; set; }
    public string Outcome { get; set; } = "running";
    public bool Legacy { get; set; }
    public string DataWarning { get; set; } = "";
    public int CompletedParts { get; set; }
    public string Text { get; set; } = "";
    public List<AnalyticsRoll> Rolls { get; set; } = [];
    public int Pending => Rolls.Count(r => r.Requested && !r.Paid && !r.NotSent);
}

public record PerkFrequency(
    int Rank,
    string Name,
    int Count,
    double Percentage,
    int NumericSamples,
    int MaxCount,
    double? MaxPercentage
);

public sealed class AnalyticsData
{
    public List<AnalyticsSession> Sessions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public static List<PerkFrequency> Leaderboard(Catalog catalog, IEnumerable<AnalyticsRoll> rows)
    {
        var allowed = catalog.Affixes.Select(a => a.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paid = rows.Where(r => r.Paid && r.Fresh != null && allowed.Contains(r.Fresh)).ToList();
        int total = paid.Count;
        return catalog
            .Affixes.Select(a =>
            {
                var hits = paid.Where(r => string.Equals(r.Fresh, a.Title, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                int numeric = hits.Count(r => r.Value.HasValue && r.Maximum.HasValue),
                    max = hits.Count(r => r.Value.HasValue && r.Maximum.HasValue && r.Value >= r.Maximum - .0001);
                return new PerkFrequency(
                    0,
                    a.Title,
                    hits.Count,
                    total > 0 ? hits.Count * 100.0 / total : 0,
                    numeric,
                    max,
                    numeric > 0 ? max * 100.0 / numeric : null
                );
            })
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Select((p, i) => p with { Rank = i + 1 })
            .ToList();
    }
}

public static class AnalyticsStore
{
    public static string Output => Path.Combine(SessionLog.Root, "Analytics");

    static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static IEnumerable<string> Lines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
            yield return line;
    }

    static string S(JsonElement e, string key, string fallback = "") =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    static int I(JsonElement e, string key, int fallback = 0) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;

    static long L(JsonElement e, string key, long fallback = 0) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : fallback;

    static double? D(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public static List<string> Roots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(SessionLog.Root) };
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 7 && directory != null; i++, directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "SetBuilder.csproj")))
                continue;
            foreach (var child in directory.GetDirectories("dist*"))
            {
                string logs = Path.Combine(child.FullName, "Logs");
                if (Directory.Exists(logs))
                    roots.Add(Path.GetFullPath(logs));
            }
            string local = Path.Combine(directory.FullName, "Logs");
            if (Directory.Exists(local))
                roots.Add(local);
            break;
        }
        string sources = Path.Combine(Output, "sources.json");
        if (File.Exists(sources))
            try
            {
                foreach (string root in JsonSerializer.Deserialize<string[]>(Read(sources)) ?? [])
                    if (Directory.Exists(root))
                        roots.Add(Path.GetFullPath(root));
            }
            catch (JsonException) { }
        return roots.Where(Directory.Exists).Order().ToList();
    }

    public static void AddRoot(string path)
    {
        if (Directory.Exists(Path.Combine(path, "Logs")))
            path = Path.Combine(path, "Logs");
        var roots = Roots();
        roots.Add(Path.GetFullPath(path));
        Directory.CreateDirectory(Output);
        File.WriteAllText(
            Path.Combine(Output, "sources.json"),
            JsonSerializer.Serialize(roots.Distinct(StringComparer.OrdinalIgnoreCase), Catalog.JsonOptions)
        );
    }

    static string Stamp(string folder)
    {
        var parts = new List<string>();
        foreach (
            string name in new[]
            {
                "session.json",
                "settings.json",
                "effective-settings.json",
                "summary.json",
                "session.log",
                File.Exists(Path.Combine(folder, "rolls.jsonl")) ? "rolls.jsonl" : "events.jsonl",
            }
        )
        {
            var f = new FileInfo(Path.Combine(folder, name));
            if (f.Exists)
                parts.Add(name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks);
        }
        return "analytics-v3|" + string.Join('|', parts);
    }

    public static AnalyticsData Load(
        Catalog catalog,
        IEnumerable<string>? roots = null,
        Action<string>? progress = null
    )
    {
        var data = new AnalyticsData();
        var seen = new HashSet<string>();
        var folders = new List<string>();
        foreach (string root in roots ?? Roots())
            try
            {
                folders.AddRange(
                    Directory.GetDirectories(root).Where(d => File.Exists(Path.Combine(d, "session.json")))
                );
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                data.Warnings.Add(root + ": " + e.Message);
            }
        Directory.CreateDirectory(Path.Combine(Output, "Cache"));
        int n = 0;
        foreach (string folder in folders.Order())
        {
            progress?.Invoke($"Читаю журнали {++n}/{folders.Count}");
            try
            {
                using var metadata = JsonDocument.Parse(Read(Path.Combine(folder, "session.json")));
                var meta = metadata.RootElement;
                if (S(meta, "mode") is not ("automation" or "read-only" or "offline-png"))
                    continue; // Exclude internal tests; include real user inspection text.
                string id = Path.GetFileName(folder) + "|" + S(meta, "startedAt");
                if (!seen.Add(id))
                    continue;
                string stamp = Stamp(folder),
                    key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folder))))[..32];
                string cache = Path.Combine(Output, "Cache", key + ".json");
                AnalyticsSession? session = null;
                if (File.Exists(cache))
                    try
                    {
                        using var doc = JsonDocument.Parse(Read(cache));
                        if (S(doc.RootElement, "Stamp") == stamp)
                            session = JsonSerializer.Deserialize<AnalyticsSession>(
                                doc.RootElement.GetProperty("Session").GetRawText()
                            );
                    }
                    catch (JsonException) { }
                if (session == null)
                {
                    session = ReadSession(folder, meta);
                    File.WriteAllText(cache, JsonSerializer.Serialize(new { Stamp = stamp, Session = session }));
                }
                data.Sessions.Add(session);
                if (session.DataWarning != "")
                    data.Warnings.Add(session.Id + ": " + session.DataWarning);
            }
            catch (Exception e)
                when (e
                        is IOException
                            or UnauthorizedAccessException
                            or JsonException
                            or InvalidOperationException
                            or KeyNotFoundException
                            or FormatException
                )
            {
                data.Warnings.Add(Path.GetFileName(folder) + ": " + e.Message);
            }
        }
        data.Sessions = data.Sessions.OrderByDescending(s => s.Started).ToList();
        return data;
    }

    static AnalyticsSession ReadSession(string folder, JsonElement meta)
    {
        var session = new AnalyticsSession
        {
            Mode = S(meta, "mode"),
            Id = Path.GetFileName(folder),
            Folder = folder,
            Version = S(meta, "version"),
            Started = DateTimeOffset.TryParse(S(meta, "startedAt"), out var t)
                ? t
                : new DateTimeOffset(Directory.GetCreationTime(folder)),
            Legacy = I(meta, "schemaVersion", 1) < 2,
        };
        string settings = File.Exists(Path.Combine(folder, "effective-settings.json"))
            ? "effective-settings.json"
            : "settings.json";
        if (File.Exists(Path.Combine(folder, settings)))
            try
            {
                using var doc = JsonDocument.Parse(Read(Path.Combine(folder, settings)));
                session.Set = S(doc.RootElement, "Set");
            }
            catch (JsonException) { }
        int? expectedPaid = null;
        string summary = Path.Combine(folder, "summary.json");
        if (File.Exists(summary))
            try
            {
                using var doc = JsonDocument.Parse(Read(summary));
                session.Outcome = S(doc.RootElement, "outcome", "unknown");
                double? elapsed = D(doc.RootElement, "elapsedMs");
                if (elapsed >= 0)
                    session.DurationSeconds = elapsed / 1000.0;
                if (doc.RootElement.TryGetProperty("spending", out var spending))
                    expectedPaid = I(spending, "confirmedRolls");
            }
            catch (JsonException) { }
        string text = Path.Combine(folder, "session.log");
        if (File.Exists(text))
            session.Text = Read(text);
        if (session.DurationSeconds == null)
        {
            var elapsed = Regex
                .Matches(session.Text, @"\+(\d+)ms\b")
                .Cast<Match>()
                .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 1000.0)
                .ToList();
            if (elapsed.Count > 0)
                session.DurationSeconds = elapsed.Max();
            else
            {
                var times = session
                    .Text.Split('\n')
                    .Select(ItemAnalytics.Timestamp)
                    .Where(t => t.HasValue)
                    .Select(t => t!.Value)
                    .ToList();
                if (times.Count > 0 && times.Max() >= session.Started)
                    session.DurationSeconds = (times.Max() - session.Started).TotalSeconds;
            }
        }
        session.CompletedParts = Regex.Matches(session.Text, @"✓ Деталь \d+: ").Count;
        if (session.CompletedParts == 0 && session.Outcome == "completed")
        {
            var m = Regex.Match(session.Text, @"Готово:.*?, (\d+) деталей");
            if (m.Success)
                session.CompletedParts = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        var rolls = new Dictionary<int, AnalyticsRoll>();
        AnalyticsRoll Roll(int id)
        {
            if (!rolls.TryGetValue(id, out var row))
                rolls[id] = row = new() { Id = id };
            return row;
        }
        string file = Path.Combine(folder, session.Legacy ? "events.jsonl" : "rolls.jsonl");
        if (!File.Exists(file))
        {
            session.Rolls = [];
            if (session.Mode == "automation")
                session.DataWarning = "Відсутній первинний журнал кидків; витрати окремих деталей невідомі.";
            return session;
        }
        int legacyId = 0;
        string legacyPart = "";
        foreach (string line in Lines(file))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }
            using (doc)
            {
                var e = doc.RootElement;
                string kind = S(e, "kind");
                if (!e.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object)
                    continue;
                if (session.Legacy)
                {
                    if (kind == "message")
                    {
                        var start = Regex.Match(S(d, "message"), @"Деталь \d+/\d+: (\S+)");
                        if (start.Success)
                            legacyPart = start.Groups[1].Value;
                        var m = Regex.Match(S(d, "message"), @"слот (\d+) · кидок (\d+): шукаю (.+)$");
                        if (m.Success)
                        {
                            legacyId = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                            if (DateTimeOffset.TryParse(S(e, "time"), out var legacyWhen))
                                Roll(legacyId).Time = legacyWhen;
                            Roll(legacyId).Part = legacyPart;
                            Roll(legacyId).Requested = true;
                            Roll(legacyId).Slot = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        }
                    }
                    else if (kind == "decision" && legacyId > 0)
                    {
                        var row = Roll(legacyId);
                        row.Paid = true;
                        row.Bucks = 1500;
                        row.SpareParts = 150;
                        row.Fresh = S(d, "freshName");
                        var numeric = Regex.Match(S(d, "fresh"), @"([\d.,]+)% / ([\d.,]+)%");
                        if (numeric.Success)
                        {
                            row.Value = double.Parse(
                                numeric.Groups[1].Value.Replace(',', '.'),
                                CultureInfo.InvariantCulture
                            );
                            row.Maximum = double.Parse(
                                numeric.Groups[2].Value.Replace(',', '.'),
                                CultureInfo.InvariantCulture
                            );
                        }
                        row.Phase = S(d, "current").StartsWith(S(d, "wanted") + " ", StringComparison.Ordinal)
                            ? "Maximize"
                            : "Search";
                    }
                    continue;
                }
                int id = I(d, "rollId", I(d, "RollId"));
                if (id < 1)
                    continue;
                var current = Roll(id);
                if (DateTimeOffset.TryParse(S(e, "time"), out var when))
                    current.Time = when;
                if (kind == "requested")
                {
                    current.Requested = true;
                    current.Part = S(d, "part");
                    current.Slot = I(d, "slot");
                    current.Phase = S(d, "phase", "Unknown");
                }
                else if (kind == "not_sent")
                    current.NotSent = true;
                else if (kind == "confirmed")
                {
                    current.Paid = true;
                    current.Bucks = L(d, "bucks", 1500);
                    current.SpareParts = L(d, "spareParts", 150);
                }
                else if (kind == "offer")
                {
                    current.Phase = S(d, "Phase", current.Phase);
                    current.Part = S(d, "Part", current.Part);
                    current.Slot = I(d, "Slot", current.Slot);
                    current.Other = S(d, "OtherPerk", "");
                    if (d.TryGetProperty("Fresh", out var fresh) && fresh.ValueKind == JsonValueKind.Object)
                    {
                        current.Fresh = S(fresh, "Name");
                        if (fresh.TryGetProperty("Stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
                        {
                            current.Value = D(stats, "Value");
                            current.Maximum = D(stats, "Maximum");
                        }
                    }
                }
            }
        }
        session.Rolls = rolls.Values.OrderBy(r => r.Id).ToList();
        if (expectedPaid.HasValue && expectedPaid != session.Rolls.Count(r => r.Paid))
            session.DataWarning =
                $"Підсумок містить {expectedPaid} оплачених кидків, у первинних записах прочитано {session.Rolls.Count(r => r.Paid)}.";
        return session;
    }

    public static string CombinedText(IEnumerable<AnalyticsSession> sessions, string query = "")
    {
        var result = new StringBuilder();
        foreach (var session in sessions.OrderByDescending(s => s.Started))
        {
            var lines = session
                .Text.Split('\n')
                .Where(l => string.IsNullOrWhiteSpace(query) || l.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (lines.Length == 0)
                continue;
            result.AppendLine(
                $"===== {session.Started.ToLocalTime():dd.MM.yyyy HH:mm:ss} · {session.Version} · {session.Set} · {session.Outcome} · {session.Id} ====="
            );
            foreach (string line in lines)
                result.AppendLine(line.TrimEnd('\r'));
            result.AppendLine();
        }
        return result.ToString();
    }

    public static string Export(
        AnalyticsData data,
        Catalog catalog,
        IEnumerable<AnalyticsSession> sessions,
        IEnumerable<AnalyticsRoll> rolls
    )
    {
        Directory.CreateDirectory(Output);
        var list = rolls.Where(r => r.Paid).ToList();
        var ranking = AnalyticsData.Leaderboard(catalog, list);
        File.WriteAllText(
            Path.Combine(Output, "all-text-logs.txt"),
            CombinedText(data.Sessions),
            new UTF8Encoding(true)
        );
        File.WriteAllText(
            Path.Combine(Output, "selected-text-logs.txt"),
            CombinedText(sessions),
            new UTF8Encoding(true)
        );
        File.WriteAllText(
            Path.Combine(Output, "analytics.json"),
            JsonSerializer.Serialize(
                new
                {
                    generatedAt = DateTimeOffset.Now,
                    sessions = sessions.Count(),
                    confirmedRolls = list.Count,
                    recognizedFresh = list.Count(r =>
                        ranking.Any(p => string.Equals(p.Name, r.Fresh, StringComparison.OrdinalIgnoreCase))
                    ),
                    bucks = list.Sum(r => r.Bucks),
                    spareParts = list.Sum(r => r.SpareParts),
                    leaderboard = ranking,
                    itemAverages = ItemAnalytics.Summary(sessions),
                    completedItems = sessions.SelectMany(ItemAnalytics.Completed),
                    itemAverageScope = "All confirmed spending in selected sessions divided by their logged completions, including unfinished attempts. Phase and other-perk filters do not affect item metrics. Total time sums recorded automation-session durations, including countdowns and navigation, excluding gaps between runs. Item time spans its header to completion in that run. Individual item costs cover this session only; prior sessions cannot be linked to a unique car item.",
                    sourceSessions = sessions.Select(s => new
                    {
                        s.Id,
                        s.Folder,
                        s.Version,
                        s.Set,
                        s.Mode,
                        s.Outcome,
                        s.CompletedParts,
                        s.DurationSeconds,
                        s.Legacy,
                        s.DataWarning,
                    }),
                    warnings = data.Warnings,
                    scope = "Observed top/new offers only. Zero observations do not establish impossibility. OCR repeats and preexisting resumed menus are excluded.",
                },
                Catalog.JsonOptions
            )
        );
        static string Cell(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var csv = new StringBuilder("rank,perk,count,observed_percent,numeric_samples,max_count,max_percent\n");
        foreach (var row in ranking)
            csv.AppendLine(
                string.Join(
                    ',',
                    row.Rank.ToString(CultureInfo.InvariantCulture),
                    Cell(row.Name),
                    row.Count.ToString(CultureInfo.InvariantCulture),
                    row.Percentage.ToString("F4", CultureInfo.InvariantCulture),
                    row.NumericSamples.ToString(CultureInfo.InvariantCulture),
                    row.MaxCount.ToString(CultureInfo.InvariantCulture),
                    row.MaxPercentage?.ToString("F4", CultureInfo.InvariantCulture) ?? ""
                )
            );
        File.WriteAllText(Path.Combine(Output, "perk-leaderboard.csv"), csv.ToString(), new UTF8Encoding(true));
        var owners = data
            .Sessions.SelectMany(s => s.Rolls.Select(r => (Row: r, Session: s)))
            .ToDictionary(p => p.Row, p => p.Session);
        var detail = new StringBuilder(
            "session,version,set,roll_id,time,phase,part,slot,other_perk,fresh_perk,value,maximum,bucks,spare_parts,legacy\n"
        );
        foreach (var r in list)
        {
            if (!owners.TryGetValue(r, out var session))
                continue;
            detail.AppendLine(
                string.Join(
                    ',',
                    new[]
                    {
                        session.Id,
                        session.Version,
                        session.Set,
                        r.Id.ToString(CultureInfo.InvariantCulture),
                        r.Time?.ToString("O") ?? "",
                        r.Phase,
                        r.Part,
                        r.Slot.ToString(CultureInfo.InvariantCulture),
                        r.Other ?? "",
                        r.Fresh ?? "",
                        r.Value?.ToString(CultureInfo.InvariantCulture) ?? "",
                        r.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "",
                        r.Bucks.ToString(CultureInfo.InvariantCulture),
                        r.SpareParts.ToString(CultureInfo.InvariantCulture),
                        session.Legacy.ToString(),
                    }.Select(Cell)
                )
            );
        }
        File.WriteAllText(Path.Combine(Output, "selected-rolls.csv"), detail.ToString(), new UTF8Encoding(true));
        var items = new StringBuilder(
            "session,version,set,item_number,part,completed_at,duration_seconds,confirmed_rolls,bucks,spare_parts,cost_scope\n"
        );
        foreach (var item in sessions.SelectMany(ItemAnalytics.Completed))
            items.AppendLine(
                string.Join(
                    ',',
                    new[]
                    {
                        item.Session,
                        item.Version,
                        item.Set,
                        item.Number.ToString(CultureInfo.InvariantCulture),
                        item.Part,
                        item.Time?.ToString("O") ?? "",
                        item.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? "",
                        item.Rolls?.ToString(CultureInfo.InvariantCulture) ?? "",
                        item.Bucks?.ToString(CultureInfo.InvariantCulture) ?? "",
                        item.SpareParts?.ToString(CultureInfo.InvariantCulture) ?? "",
                        item.CostScope,
                    }.Select(Cell)
                )
            );
        File.WriteAllText(Path.Combine(Output, "completed-items.csv"), items.ToString(), new UTF8Encoding(true));
        return Output;
    }
}
