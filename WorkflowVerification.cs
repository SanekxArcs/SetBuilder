namespace TunerFest;

public static class WorkflowVerification
{
    record Step(int Slot, RollPhase Phase, PerkCard Result, string[] Targets);

    sealed class FakeGame(
        PartView[] parts,
        Queue<Step> steps,
        bool repeat = false,
        Action? prepared = null,
        bool quick = false
    ) : IBuildGame
    {
        int index;
        public List<string> Actions { get; } = [];
        public int Rolls;

        public Task Prepare(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            prepared?.Invoke();
            Actions.AddRange(NavigationRoute.Open);
            return Task.CompletedTask;
        }

        public Task<PartView> ReadPart(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(parts[index] with { Id = repeat ? "SAMEPART" : parts[index].Id });
        }

        public Task<PerkCard> Roll(int slot, PerkCard current, RollPhase phase, string[] targets, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (steps.Count == 0)
                throw new Exception("Unexpected roll");
            var next = steps.Dequeue();
            if (next.Slot != slot || next.Phase != phase || !next.Targets.SequenceEqual(targets))
                throw new Exception($"Unexpected phase/slot/targets: {slot}, {phase}, {string.Join('/', targets)}");
            parts[index].Cards[slot] = next.Result;
            Rolls++;
            Actions.Add($"roll:{slot}:{phase}");
            return Task.FromResult(next.Result);
        }

        public Task ExitToParts(CancellationToken ct)
        {
            Actions.AddRange(NavigationRoute.Exit);
            return Task.CompletedTask;
        }

        public Task NextPart(CancellationToken ct)
        {
            Actions.Add("Right");
            index++;
            Actions.AddRange(NavigationRoute.Open);
            return Task.CompletedTask;
        }

        public async Task Advance(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (quick)
            {
                Actions.AddRange(NavigationRoute.QuickNext);
                index++;
            }
            else
            {
                await ExitToParts(ct);
                await NextPart(ct);
            }
        }
    }

    public static async Task<int> Run()
    {
        var checks = new List<string>();
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            checks.Add("PASS " + label);
        }
        var catalog = Catalog.Load();
        PerkCard Card(string name, double value, double min, double max) =>
            new(name, new(catalog.Affixes.Single(a => a.Title == name), value, min, max));
        var iron = Card("Iron Grip", 4.4, 4, 6);
        var bulldozer = Card("Bulldozer", 6, 6, 12);
        var legend = Card("Legend", 2, 2, 4);
        var legendMax = Card("Legend", 4, 2, 4);
        var rich = Card("Rich", 1, 1, 3);
        var richMax = Card("Rich", 3, 1, 3);
        var parts = Enumerable.Range(0, 7).Select(i => new PartView("PART" + i, [iron, bulldozer], 0)).ToArray();
        var steps = new Queue<Step>();
        for (int i = 0; i < 7; i++)
        {
            steps.Enqueue(new(0, RollPhase.Search, new PerkCard("Clean Driver", null), ["Rich", "Legend"]));
            steps.Enqueue(new(0, RollPhase.Search, legend, ["Rich", "Legend"]));
            steps.Enqueue(new(1, RollPhase.Search, rich, ["Rich"]));
            steps.Enqueue(new(1, RollPhase.Maximize, Card("Rich", 2, 1, 3), ["Rich"]));
            steps.Enqueue(new(1, RollPhase.Maximize, Card("Rich", 1.5, 1, 3), ["Rich"]));
            steps.Enqueue(new(1, RollPhase.Maximize, richMax, ["Rich"]));
            steps.Enqueue(new(0, RollPhase.Maximize, legendMax, ["Legend"]));
        }
        var fake = new FakeGame(parts, steps);
        await new BuildWorkflow(
            fake,
            new Profile
            {
                First = "Rich",
                Second = "Legend",
                Parts = 7,
            },
            _ => { }
        ).Run(CancellationToken.None);
        Check(
            steps.Count == 0 && fake.Rolls == 49,
            "seven parts: accept either target first, find remaining, maximize second then first"
        );
        Check(
            parts.All(p => p.Cards[0].Name == "Legend" && p.Cards[1].Name == "Rich" && p.Cards.All(c => c.Maxed)),
            "reversed target order retained on every completed part"
        );
        Check(
            fake.Actions.Count(a => a == "Recalibrate") == 7
                && fake.Actions.Count(a => a == "Back") == 21
                && fake.Actions.Count(a => a == "Right") == 6,
            "seven open routes, three backs per part, six next-part transitions"
        );
        Check(
            fake.Actions.Take(3).SequenceEqual(new[] { "Confirm", "Recalibrate", "Confirm" })
                && fake.Actions.TakeLast(3).All(a => a == "Back"),
            "starts with Enter-F9-Enter and finishes at item selection"
        );
        var oneSteps = new Queue<Step>([
            new(0, RollPhase.Search, legend, ["Legend"]),
            new(0, RollPhase.Maximize, legendMax, ["Legend"]),
            new(1, RollPhase.Maximize, richMax, ["Rich"]),
        ]);
        var messages = new List<string>();
        var one = new FakeGame([new("EXISTING", [iron, rich], 0)], oneSteps);
        await new BuildWorkflow(
            one,
            new Profile
            {
                First = "Rich",
                Second = "Legend",
                Parts = 1,
            },
            messages.Add
        ).Run(CancellationToken.None);
        Check(oneSteps.Count == 0, "preserve desired right slot; search and maximize the other slot first");
        Check(
            messages.Any(m => m.Contains("Rich знайдено у слоті 2"))
                && messages.Any(m => m.Contains("Шукаю лише Legend у слоті 1"))
                && messages.Any(m => m.Contains("слот 1 — Legend, слот 2 — Rich")),
            "logs explicitly show found and remaining names with actual slot positions"
        );
        var done = new FakeGame([new("DONE", [legendMax, richMax], 0)], new());
        await new BuildWorkflow(
            done,
            new Profile
            {
                First = "Rich",
                Second = "Legend",
                Parts = 1,
            },
            _ => { }
        ).Run(CancellationToken.None);
        Check(
            done.Rolls == 0 && done.Actions.TakeLast(3).All(a => a == "Back"),
            "already-maxed part exits without rerolling"
        );
        var repeat = new FakeGame([new("A", [legendMax, richMax], 0), new("B", [legendMax, richMax], 0)], new(), true);
        bool stopped = false;
        try
        {
            await new BuildWorkflow(
                repeat,
                new Profile
                {
                    First = "Rich",
                    Second = "Legend",
                    Parts = 2,
                },
                _ => { }
            ).Run(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            stopped = true;
        }
        Check(stopped, "repeated part identity stops the run");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        stopped = false;
        try
        {
            await new BuildWorkflow(done, new Profile(), _ => { }).Run(cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        Check(stopped, "cancelled run does not enter workflow");
        var autoProfile = new Profile
        {
            AutoSet = true,
            Set = "NITRO CHEMIST",
            First = "Pure",
            Second = "Extra Pump",
            Parts = 1,
        };
        var autoGame = new FakeGame(
            [new("AUTO", [legendMax, richMax], 0)],
            new(),
            false,
            () =>
            {
                var pick = AutoSetResolver.Resolve(catalog, "BUCKS MAGNET");
                autoProfile.Set = pick.Set;
                autoProfile.First = pick.Affixes[0];
                autoProfile.Second = pick.Affixes[1];
            }
        );
        await new BuildWorkflow(autoGame, autoProfile, _ => { }).Run(CancellationToken.None);
        Check(
            autoGame.Rolls == 0 && autoProfile.Set == "BUCKS MAGNET",
            "workflow uses auto-detected goals after preparation, including reversed slots"
        );

        var fastParts = Enumerable
            .Range(0, 7)
            .Select(i => new PartView("LEGENDARY" + PartTransition.Categories[i], [legendMax, richMax], 0))
            .ToArray();
        var fastGame = new FakeGame(fastParts, new(), quick: true);
        await new BuildWorkflow(
            fastGame,
            new Profile
            {
                First = "Rich",
                Second = "Legend",
                Parts = 7,
                FastPartNavigation = true,
            },
            _ => { }
        ).Run(CancellationToken.None);
        Check(
            fastGame.Rolls == 0
                && fastGame.Actions.Count(a => a == "Down") == 6
                && fastGame.Actions.Count(a => a == "Right") == 0
                && fastGame.Actions.Count(a => a == "Back") == 15,
            "seven quick transitions use down and two backs, final item exits with three backs"
        );
        Check(
            NavigationRoute.QuickNext.SequenceEqual(new[] { "Back", "Back", "Down", "Recalibrate", "Confirm" }),
            "quick route has the exact inventory-crafting-perks sequence"
        );
        Check(
            PartTransition
                .Categories.Select(
                    (name, i) => PartTransition.Category("LEGENDARY" + name) == i && PartTransition.Category(name) == i
                )
                .All(ok => ok)
                && PartTransition.Category("UNKNOWN") == -1,
            "inventory headers and all seven real part names resolve to category indices"
        );
        var routeActions = new List<string>();
        Task RouteAction(string action)
        {
            routeActions.Add(action);
            return Task.CompletedTask;
        }
        await PartTransition.Run(
            false,
            () => RouteAction("quick"),
            () => RouteAction("classic"),
            () => RouteAction("fallback"),
            _ => routeActions.Add("report"),
            CancellationToken.None
        );
        Check(routeActions.SequenceEqual(new[] { "classic" }), "classic selection does not attempt quick input");
        routeActions.Clear();
        await PartTransition.Run(
            true,
            () => RouteAction("quick"),
            () => RouteAction("classic"),
            () => RouteAction("fallback"),
            _ => routeActions.Add("report"),
            CancellationToken.None
        );
        Check(routeActions.SequenceEqual(new[] { "quick" }), "confirmed quick route avoids fallback");
        routeActions.Clear();
        await PartTransition.Run(
            true,
            () => throw new InvalidOperationException("header not confirmed"),
            () => RouteAction("classic"),
            () => RouteAction("fallback"),
            _ => routeActions.Add("report"),
            CancellationToken.None
        );
        Check(
            routeActions.SequenceEqual(new[] { "report", "fallback" }),
            "failed quick recognition reports cause and executes recovery once"
        );
        routeActions.Clear();
        bool navigationCancelled = false;
        try
        {
            await PartTransition.Run(
                true,
                () => throw new OperationCanceledException(),
                () => RouteAction("classic"),
                () => RouteAction("fallback"),
                _ => routeActions.Add("report"),
                CancellationToken.None
            );
        }
        catch (OperationCanceledException)
        {
            navigationCancelled = true;
        }
        Check(navigationCancelled && routeActions.Count == 0, "cancelled quick navigation never sends fallback input");
        bool focusStopped = false;
        try
        {
            await PartTransition.Run(
                true,
                () => throw new InvalidOperationException("transition failed"),
                () => RouteAction("classic"),
                () => RouteAction("fallback"),
                _ => throw new InvalidOperationException("focus lost"),
                CancellationToken.None
            );
        }
        catch (InvalidOperationException)
        {
            focusStopped = true;
        }
        Check(focusStopped && routeActions.Count == 0, "lost-focus guard stops recovery before any further input");
        var ledger = new RunLedger();
        for (int i = 0; i < 49; i++)
        {
            int id = ledger.Request("PART" + (i / 7), i % 2, RollPhase.Search);
            ledger.Confirm(id);
            ledger.Confirm(id);
        }
        Check(ledger.Confirmed == 49 && ledger.Pending == 0, "duplicate popup frames do not double-charge 49 rolls");
        var paid = System.Text.Json.JsonSerializer.SerializeToElement(ledger.Snapshot());
        Check(
            paid.GetProperty("bucksSpent").GetInt64() == 73500
                && paid.GetProperty("sparePartsSpent").GetInt64() == 7350,
            "49 confirmed rotations cost 73500 Bucks and 7350 spare parts"
        );
        int pending = ledger.Request("LAST", 0, RollPhase.Search);
        Check(ledger.Pending == 1, "unconfirmed key press remains separate from confirmed spending");
        ledger.NotSent(pending);
        Check(ledger.Pending == 0 && ledger.Confirmed == 49, "undispatched input does not charge resources");
        var timing = new Profile
        {
            RandomPause = true,
            PauseMinMs = 50,
            PauseMaxMs = 500,
        };
        var seeded = new Random(701);
        var samples = Enumerable.Range(0, 5000).Select(_ => PressTiming.NextDelay(timing, seeded)).ToArray();
        Check(
            samples.All(n => n >= 50 && n <= 500) && samples.Distinct().Count() > 400,
            "random pauses stay inside configured inclusive 50-500 ms range"
        );
        timing.PauseMinMs = timing.PauseMaxMs = 73;
        Check(PressTiming.NextDelay(timing, seeded) == 73, "equal timing bounds produce the exact delay");
        timing.RandomPause = false;
        timing.Delay = 200;
        Check(
            Enumerable.Range(0, 20).All(_ => PressTiming.NextDelay(timing, seeded) == 200),
            "fixed-pause mode preserves configured delay"
        );
        timing.RandomPause = true;
        timing.PauseMinMs = 500;
        timing.PauseMaxMs = 50;
        bool invalid = false;
        try
        {
            PressTiming.NextDelay(timing, seeded);
        }
        catch (InvalidOperationException)
        {
            invalid = true;
        }
        Check(invalid, "reversed timing range is rejected before input");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "workflow-test.txt"), checks);
        return 0;
    }

    public static async Task<int> Screens(string grid, string inventory, string overview, string popup)
    {
        var p = new Profile();
        var v = new Vision(Catalog.Load(), p);
        var checks = new List<object>();
        foreach (
            var (path, expected) in new[]
            {
                (grid, GameScreen.Parts),
                (inventory, GameScreen.Inventory),
                (overview, GameScreen.Overview),
                (popup, GameScreen.PopupLeft),
            }
        )
        {
            using var image = new Bitmap(path);
            var actual = await v.ScreenState(image);
            checks.Add(
                new
                {
                    path,
                    expected = expected.ToString(),
                    actual = actual.ToString(),
                    gridFocus = v.GridFocus(image),
                }
            );
            if (actual != expected)
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "navigation-test.json"),
                    System.Text.Json.JsonSerializer.Serialize(checks, Catalog.JsonOptions)
                );
                return 1;
            }
        }
        using (var image = new Bitmap(popup))
        {
            var current = new PerkCard("Iron Grip", await v.Read(image, "left"));
            var search = await v.Select(
                image,
                ["topLeft", "middleLeft", "left"],
                current,
                RollPhase.Search,
                ["Rich", "Legend"]
            );
            var max = await v.Select(
                image,
                ["topLeft", "middleLeft", "left"],
                current,
                RollPhase.Maximize,
                ["Iron Grip"]
            );
            if (
                search.Index != 0
                || search.Expected.Name != "Clean Driver"
                || max.Index != 1
                || max.Expected.Stats?.Value != 6
            )
                return 1;
            checks.Add(new { search = search.ToString(), maximize = max.ToString() });
        }
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "navigation-test.json"),
            System.Text.Json.JsonSerializer.Serialize(checks, Catalog.JsonOptions)
        );
        return 0;
    }
}
