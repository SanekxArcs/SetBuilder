using System.Text.Json;

namespace TunerFest;

public static class AnalyticsVerification
{
    public static int Tests()
    {
        var catalog = Catalog.Load();
        var checks = new List<string>();
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            checks.Add("PASS " + name);
        }
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "test-results",
            "analytics-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        string Create(string id, int schema, string mode = "automation")
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "session.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = schema,
                        version = schema == 1 ? "1.0.3" : "1.2.1",
                        mode,
                        startedAt = "2026-10-27T06:00:00+01:00",
                    }
                )
            );
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"Set\":\"NITRO CHEMIST\"}");
            File.WriteAllText(Path.Combine(dir, "session.log"), id + " message\nRich знайдено у слоті 2\n");
            return dir;
        }
        string modern = Create("MODERN", 2);
        File.WriteAllText(Path.Combine(modern, "effective-settings.json"), "{\"Set\":\"BUCKS MAGNET\"}");
        string Event(string kind, object data) => JsonSerializer.Serialize(new { kind, data });
        string[] events =
        [
            Event("resumed", new { selection = new { Fresh = new { Name = "Pure" } } }),
            Event(
                "requested",
                new
                {
                    rollId = 1,
                    phase = "Search",
                    slot = 1,
                }
            ),
            Event(
                "confirmed",
                new
                {
                    rollId = 1,
                    bucks = 1500,
                    spareParts = 150,
                }
            ),
            Event(
                "confirmed",
                new
                {
                    rollId = 1,
                    bucks = 1500,
                    spareParts = 150,
                }
            ),
            Event(
                "offer",
                new
                {
                    RollId = 1,
                    Phase = "Search",
                    OtherPerk = "Legend",
                    Fresh = new { Name = "Rich", Stats = new { Value = 3, Maximum = 3 } },
                }
            ),
            Event(
                "offer",
                new
                {
                    RollId = 1,
                    Phase = "Search",
                    OtherPerk = "Legend",
                    Fresh = new { Name = "Rich", Stats = new { Value = 3, Maximum = 3 } },
                }
            ),
            Event(
                "requested",
                new
                {
                    rollId = 2,
                    phase = "Maximize",
                    slot = 2,
                }
            ),
            Event(
                "confirmed",
                new
                {
                    rollId = 2,
                    bucks = 1500,
                    spareParts = 150,
                }
            ),
            Event(
                "offer",
                new
                {
                    RollId = 2,
                    Phase = "Maximize",
                    OtherPerk = "Rich",
                    Fresh = new { Name = "Legend", Stats = new { Value = 2, Maximum = 4 } },
                }
            ),
            Event("requested", new { rollId = 3, phase = "Search" }),
            Event("not_sent", new { rollId = 3 }),
            Event("requested", new { rollId = 4, phase = "Search" }),
            Event(
                "confirmed",
                new
                {
                    rollId = 5,
                    bucks = 1500,
                    spareParts = 150,
                }
            ),
        ];
        File.WriteAllLines(Path.Combine(modern, "rolls.jsonl"), events.Concat(new[] { "{partial active record" }));
        string legacy = Create("LEGACY", 1);
        File.WriteAllLines(
            Path.Combine(legacy, "events.jsonl"),
            new[]
            {
                Event("message", new { message = "Деталь 1/1 · слот 1 · кидок 1: шукаю Pure" }),
                Event(
                    "decision",
                    new
                    {
                        wanted = "Pure",
                        current = "Iron Grip 4% / 6%",
                        freshName = "Pure",
                    }
                ),
                Event(
                    "decision",
                    new
                    {
                        wanted = "Pure",
                        current = "Iron Grip 4% / 6%",
                        freshName = "Pure",
                    }
                ),
                Event("message", new { message = "Деталь 1/1 · слот 1 · кидок 2: шукаю Pure" }),
            }
        );
        string fake = Create("INTERNAL-TEST", 2, "offline-verification");
        File.WriteAllLines(
            Path.Combine(fake, "rolls.jsonl"),
            new[]
            {
                Event(
                    "confirmed",
                    new
                    {
                        rollId = 1,
                        bucks = 999999,
                        spareParts = 999999,
                    }
                ),
            }
        );
        string inspect = Create("USER-READING", 2, "read-only");
        File.WriteAllText(Path.Combine(inspect, "rolls.jsonl"), "");
        var data = AnalyticsStore.Load(catalog, [root]);
        var rows = data.Sessions.SelectMany(s => s.Rolls).ToList();
        var ranking = AnalyticsData.Leaderboard(catalog, rows);
        Check(
            data.Sessions.Count == 3 && !data.Sessions.Any(s => s.Id == "INTERNAL-TEST"),
            "internal tests excluded, real read-only text included"
        );
        Check(
            rows.Count(r => r.Paid) == 4
                && rows.Where(r => r.Paid).Sum(r => r.Bucks) == 6000
                && rows.Where(r => r.Paid).Sum(r => r.SpareParts) == 600,
            "duplicate confirmations and OCR decisions do not duplicate costs"
        );
        Check(data.Sessions.Sum(s => s.Pending) == 2, "not-sent input excluded from pending, real pending preserved");
        Check(
            ranking.Count == catalog.Affixes.Count && ranking.Sum(r => r.Count) == 3,
            "every catalog perk shown, only recognized new offers counted"
        );
        Check(
            Math.Abs(ranking.Single(r => r.Name == "Rich").Percentage - 100.0 / 3) < .0001,
            "frequency denominator excludes missing offers and resumed menus"
        );
        Check(
            ranking.Single(r => r.Name == "Rich").MaxPercentage == 100
                && ranking.Single(r => r.Name == "Pure").MaxPercentage == null,
            "max percentage uses numeric sample count, not missing values"
        );
        Check(
            data.Sessions.Single(s => s.Id == "MODERN").Set == "BUCKS MAGNET",
            "effective auto-detected set overrides initial settings for analytics"
        );
        Check(
            AnalyticsData
                .Leaderboard(catalog, rows.Where(r => r.Phase == "Maximize"))
                .Single(r => r.Name == "Legend")
                .Percentage == 100,
            "phase-specific probabilities stay within selected sample"
        );
        Check(
            AnalyticsData.Leaderboard(catalog, rows.Where(r => r.Other == "Rich")).Single(r => r.Name == "Legend").Count
                == 1,
            "other-slot filter supported"
        );
        Check(
            AnalyticsStore.CombinedText(data.Sessions).Contains("LEGACY message")
                && AnalyticsStore.CombinedText(data.Sessions).Contains("MODERN message")
                && !AnalyticsStore.CombinedText(data.Sessions).Contains("INTERNAL-TEST message"),
            "unified text includes real sessions across versions only"
        );
        string output = AnalyticsStore.Export(data, catalog, data.Sessions, rows);
        Check(
            File.Exists(Path.Combine(output, "all-text-logs.txt"))
                && File.ReadAllLines(Path.Combine(output, "perk-leaderboard.csv")).Length == catalog.Affixes.Count + 1,
            "complete TXT and all-perk leaderboard export"
        );
        File.AppendAllText(
            Path.Combine(modern, "rolls.jsonl"),
            "\n"
                + Event(
                    "confirmed",
                    new
                    {
                        rollId = 6,
                        bucks = 1500,
                        spareParts = 150,
                    }
                )
                + "\n"
        );
        Check(
            File.ReadAllLines(Path.Combine(output, "selected-rolls.csv")).Length == 5,
            "detailed export includes every selected paid rotation once"
        );
        var updated = AnalyticsStore.Load(catalog, [root]);
        Check(
            updated.Sessions.SelectMany(s => s.Rolls).Count(r => r.Paid) == 5,
            "cache refreshes when an active roll log grows"
        );
        File.WriteAllText(
            Path.Combine(modern, "summary.json"),
            "{\"outcome\":\"completed\",\"spending\":{\"confirmedRolls\":99}}"
        );
        var incomplete = AnalyticsStore.Load(catalog, [root]);
        Check(
            incomplete.Warnings.Any(w => w.Contains("99")),
            "missing primary paid records are surfaced as a data warning"
        );

        var itemRun = new AnalyticsSession
        {
            Id = "ITEM-RUN",
            Version = "1.4.3",
            Set = "BUCKS MAGNET",
            Text =
                "2026-10-27T10:00:00+01:00 Деталь 1/3: LEGENDARYBRAKES\n2026-10-27T10:01:00+01:00 ✓ Деталь 1: Rich + Legend\n2026-10-27T10:01:00+01:00 ✓ Деталь 1: Rich + Legend\n2026-10-27T10:02:00+01:00 Деталь 2/3: LEGENDARYGEARBOX\n2026-10-27T10:02:01+01:00 ✓ Деталь 2: Rich + Legend\n2026-10-27T10:03:00+01:00 Деталь 3/3: LEGENDARYECU",
        };

        itemRun.Rolls = Enumerable
            .Range(1, 5)
            .Select(i => new AnalyticsRoll
            {
                Id = i,
                Paid = true,
                Part = i < 3 ? "LEGENDARYBRAKES" : "LEGENDARYECU",
                Bucks = 1500,
                SpareParts = 150,
            })
            .ToList();
        var completed = ItemAnalytics.Completed(itemRun);
        var average = ItemAnalytics.Summary([itemRun]);
        Check(
            completed.Count == 2 && completed[0].Rolls == 2 && completed[0].Bucks == 3000 && completed[1].Rolls == 0,
            "duplicate completion lines ignored; unfinished category excluded; zero-roll completed item retained"
        );
        Check(
            average.CompletedItems == 2
                && average.AverageRolls == 2.5
                && average.AverageBucks == 3750
                && average.AverageSpareParts == 375,
            "per-item averages include all five paid rotations, including unfinished attempts"
        );
        var noDone = new AnalyticsSession
        {
            Rolls =
            [
                new()
                {
                    Paid = true,
                    Bucks = 1500,
                    SpareParts = 150,
                },
            ],
        };
        Check(
            ItemAnalytics.Summary([noDone]).AverageRolls == null,
            "no completed items produces unknown average rather than division by zero"
        );
        var oldDone = new AnalyticsSession { CompletedParts = 3 };
        Check(
            ItemAnalytics.Completed(oldDone).Count == 3 && ItemAnalytics.Completed(oldDone).All(i => i.Rolls == null),
            "legacy aggregate completion count retained without inventing per-item spending"
        );
        itemRun.DataWarning = "missing records";
        Check(
            ItemAnalytics.Completed(itemRun).All(i => i.Bucks == null),
            "incomplete primary records do not masquerade as exact item costs"
        );
        itemRun.DataWarning = "";
        var itemData = new AnalyticsData { Sessions = [itemRun] };
        AnalyticsStore.Export(itemData, catalog, [itemRun], itemRun.Rolls);
        Check(
            File.ReadAllLines(Path.Combine(output, "completed-items.csv")).Length == 3,
            "completed-item export includes two real completions once each"
        );
        using (var metrics = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "analytics.json"))))
            Check(
                metrics.RootElement.GetProperty("itemAverages").GetProperty("AverageBucks").GetDouble() == 3750,
                "JSON export includes full-session item averages"
            );

        Check(
            completed[0].DurationSeconds == 60 && completed[1].DurationSeconds == 1,
            "completed item durations use each item header and completion timestamp"
        );
        itemRun.DurationSeconds = 120;
        var interrupted = new AnalyticsSession { DurationSeconds = 30 };
        var readingTime = new AnalyticsSession { Mode = "read-only", DurationSeconds = 1000 };
        var timing = ItemAnalytics.Summary([itemRun, interrupted, readingTime]);
        Check(
            timing.TotalDurationSeconds == 150 && timing.AverageDurationSeconds == 75 && timing.TimedSessions == 2,
            "total and average time include interrupted runs but exclude read-only sessions and gaps"
        );
        var missingTime = ItemAnalytics.Summary([itemRun, new AnalyticsSession()]);
        Check(
            missingTime.UntimedSessions == 1 && missingTime.AverageDurationSeconds == null,
            "unknown session durations are disclosed without understating average time"
        );
        var clockShift = new AnalyticsSession
        {
            Text =
                "2026-10-27T10:00:00+01:00 +1000ms Деталь 1/1: LEGENDARYBRAKES\n2026-10-27T09:00:00+01:00 +4000ms ✓ Деталь 1: Rich + Legend",
        };
        Check(
            ItemAnalytics.Completed(clockShift).Single().DurationSeconds == 3,
            "monotonic elapsed markers keep item timing valid across wall-clock changes"
        );
        Check(
            ItemAnalytics.Duration(3665) == "1 год 01 хв 05 с" && ItemAnalytics.Duration(null) == "—",
            "time format supports hours and unknown values"
        );
        string timed = Create("TIMING-SUMMARY", 2);
        File.WriteAllText(Path.Combine(timed, "summary.json"), "{\"outcome\":\"completed\",\"elapsedMs\":123000}");
        File.WriteAllText(Path.Combine(timed, "rolls.jsonl"), "");
        string activeTime = Create("TIMING-ACTIVE", 2);
        File.WriteAllText(
            Path.Combine(activeTime, "session.log"),
            "2026-10-27T06:00:01+01:00 +1000ms start\n2026-10-27T06:00:04+01:00 +4200ms progress\n"
        );
        File.WriteAllText(Path.Combine(activeTime, "rolls.jsonl"), "");
        var loadedTime = AnalyticsStore.Load(catalog, [root]);
        Check(
            loadedTime.Sessions.Single(s => s.Id == "TIMING-SUMMARY").DurationSeconds == 123
                && loadedTime.Sessions.Single(s => s.Id == "TIMING-ACTIVE").DurationSeconds == 4.2,
            "session durations load from summary or last recorded elapsed marker for active logs"
        );
        foreach (var set in catalog.Sets)
        {
            var build = AutoSetResolver.Resolve(catalog, set.Title);
            Check(build.Set == set.Title && build.Affixes.Length == 2, "auto profile: " + set.Title);
        }
        Check(
            AutoSetResolver.Resolve(catalog, "NITRO CHEMIST").Title == "Nitro Chemist",
            "canonical Nitro profile chosen over alternate builds"
        );
        Check(
            AutoSetResolver.Resolve(catalog, "BUCKS MAGNET").Affixes.SequenceEqual(new[] { "Rich", "Legend" }),
            "Bucks auto profile matches Rich and Legend"
        );
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "analytics-test.txt"), checks);
        return 0;
    }

    public static int Real()
    {
        var catalog = Catalog.Load();
        var data = AnalyticsStore.Load(catalog);
        var rows = data.Sessions.SelectMany(s => s.Rolls).ToList();
        string output = AnalyticsStore.Export(data, catalog, data.Sessions, rows);
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "analytics-real-check.json"),
            JsonSerializer.Serialize(
                new
                {
                    sessions = data.Sessions.Count,
                    paid = rows.Count(r => r.Paid),
                    known = AnalyticsData.Leaderboard(catalog, rows).Sum(p => p.Count),
                    bucks = rows.Where(r => r.Paid).Sum(r => r.Bucks),
                    spareParts = rows.Where(r => r.Paid).Sum(r => r.SpareParts),
                    itemAverages = ItemAnalytics.Summary(data.Sessions),
                    warnings = data.Warnings,
                    top = AnalyticsData.Leaderboard(catalog, rows).Take(8),
                    output,
                },
                Catalog.JsonOptions
            )
        );
        return 0;
    }
}
