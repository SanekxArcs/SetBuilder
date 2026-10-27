using System.Globalization;
using System.Text.RegularExpressions;

namespace TunerFest;

public record CompletedItem(
    string Session,
    string Version,
    string Set,
    int Number,
    string Part,
    DateTimeOffset? Time,
    int? Rolls,
    long? Bucks,
    long? SpareParts,
    string CostScope,
    double? DurationSeconds = null
);

public record ItemAverages(
    int CompletedItems,
    int ConfirmedRolls,
    long Bucks,
    long SpareParts,
    double? AverageRolls,
    double? AverageBucks,
    double? AverageSpareParts,
    double? TotalDurationSeconds,
    double? AverageDurationSeconds,
    int TimedSessions,
    int UntimedSessions
);

public static class ItemAnalytics
{
    public static DateTimeOffset? Timestamp(string line) =>
        DateTimeOffset.TryParse(
            line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value
        )
            ? value
            : null;

    static double? Elapsed(string line)
    {
        var m = Regex.Match(line, @"\+(\d+)ms\b");
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    public static string Duration(double? seconds)
    {
        if (!seconds.HasValue)
            return "—";
        var time = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds.Value)));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours} год {time.Minutes:00} хв {time.Seconds:00} с"
            : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} хв {time.Seconds:00} с"
            : $"{time.Seconds} с";
    }

    public static List<CompletedItem> Completed(AnalyticsSession session)
    {
        var entries = new List<CompletedItem>();
        var seen = new HashSet<int>();
        string part = "";
        int current = 0;
        DateTimeOffset? started = null;
        double? startedElapsed = null;
        foreach (string line in session.Text.Split('\n'))
        {
            var start = Regex.Match(line, @"Деталь (\d+)/\d+: (\S+)");
            if (start.Success)
            {
                current = int.Parse(start.Groups[1].Value, CultureInfo.InvariantCulture);
                part = start.Groups[2].Value;
                started = Timestamp(line);
                startedElapsed = Elapsed(line);
            }
            var done = Regex.Match(line, @"✓ Деталь (\d+): ");
            if (!done.Success)
                continue;
            int number = int.Parse(done.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!seen.Add(number))
                continue;
            string id = current == number ? part : "";
            var paid = session.Rolls.Where(r => r.Paid).ToList();
            bool known = id != "" && session.DataWarning == "" && paid.All(r => r.Part != "");
            var matching = paid.Where(r => r.Part == id).ToList();
            DateTimeOffset? time = Timestamp(line);
            double? duration = null;
            double? elapsed = Elapsed(line);
            if (current == number)
            {
                if (startedElapsed.HasValue && elapsed >= startedElapsed)
                    duration = (elapsed - startedElapsed) / 1000.0;
                else if (started.HasValue && time >= started)
                    duration = (time - started)?.TotalSeconds;
            }
            entries.Add(
                new(
                    session.Id,
                    session.Version,
                    session.Set,
                    number,
                    id != "" ? id : $"Деталь {number}",
                    time,
                    known ? matching.Count : null,
                    known ? matching.Sum(r => r.Bucks) : null,
                    known ? matching.Sum(r => r.SpareParts) : null,
                    known ? "session-only" : "unknown",
                    duration
                )
            );
        }
        if (entries.Count == 0)
            for (int i = 1; i <= session.CompletedParts; i++)
                entries.Add(
                    new(session.Id, session.Version, session.Set, i, $"Деталь {i}", null, null, null, null, "unknown")
                );
        return entries;
    }

    public static ItemAverages Summary(IEnumerable<AnalyticsSession> sessions)
    {
        var runs = sessions.ToList();
        int finished = runs.Sum(s => Completed(s).Count);
        var paid = runs.SelectMany(s => s.Rolls).Where(r => r.Paid).ToList();
        long bucks = paid.Sum(r => r.Bucks),
            spares = paid.Sum(r => r.SpareParts);
        var automation = runs.Where(s => s.Mode == "automation").ToList();
        var timed = automation.Where(s => s.DurationSeconds.HasValue).ToList();
        double? total = timed.Count > 0 ? timed.Sum(s => s.DurationSeconds!.Value) : null;
        int missing = automation.Count - timed.Count;
        return new(
            finished,
            paid.Count,
            bucks,
            spares,
            finished > 0 ? (double)paid.Count / finished : null,
            finished > 0 ? (double)bucks / finished : null,
            finished > 0 ? (double)spares / finished : null,
            total,
            finished > 0 && missing == 0 ? total / finished : null,
            timed.Count,
            missing
        );
    }
}
