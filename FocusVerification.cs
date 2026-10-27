namespace TunerFest;

public static class FocusVerification
{
    public static async Task<int> Run()
    {
        var results = new List<string>();
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            results.Add("PASS " + name);
        }
        async Task<(List<string> keys, int retries)> Scenario(int[] values, int attempts)
        {
            var queue = new Queue<int>(values);
            int last = values[^1],
                retries = 0;
            var keys = new List<string>();
            await FocusNavigator.Ensure(
                0,
                () => queue.Count > 0 ? last = queue.Dequeue() : last,
                (key, _) =>
                {
                    keys.Add(key);
                    return Task.CompletedTask;
                },
                attempts,
                10,
                (_, _, _) => retries++,
                CancellationToken.None
            );
            return (keys, retries);
        }
        var fade = await Scenario([1, -1, -1, 0], 5);
        Check(
            fade.keys.SequenceEqual(new[] { "Up" }) && fade.retries == 2,
            "fading border after Up is reread without extra key presses"
        );
        var movement = await Scenario([1, 1, -1, 0], 5);
        Check(movement.keys.Count == 1, "old highlight during movement does not trigger another Up");
        var ignored = await Scenario([1, 1, 1, 1, 0], 3);
        Check(ignored.keys.Count == 2, "ignored arrow is retried only after known unchanged selection times out");
        int reads = 0,
            presses = 0;
        bool stopped = false;
        try
        {
            await FocusNavigator.Ensure(
                0,
                () =>
                {
                    reads++;
                    return -1;
                },
                (_, _) =>
                {
                    presses++;
                    return Task.CompletedTask;
                },
                4,
                10,
                null,
                CancellationToken.None
            );
        }
        catch (FocusReadException)
        {
            stopped = true;
        }
        Check(stopped && reads == 4 && presses == 0, "missing highlight stops at read limit without blind input");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        stopped = false;
        try
        {
            await FocusNavigator.Ensure(0, () => 1, (_, _) => Task.CompletedTask, 4, 10, null, cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        Check(stopped, "focus retry responds to cancellation");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "focus-test.txt"), results);
        return 0;
    }

    public static async Task<int> Replay(string before, string faded, string stable)
    {
        var vision = new Vision(Catalog.Load(), new Profile());
        var values = new List<int>();
        foreach (string file in new[] { before, faded, stable })
        {
            using var image = new Bitmap(file);
            values.Add(vision.Focus(image, ["topLeft", "middleLeft", "left"]));
        }
        var queue = new Queue<int>(values);
        int last = values[^1],
            retries = 0;
        var keys = new List<string>();
        await FocusNavigator.Ensure(
            0,
            () => queue.Count > 0 ? last = queue.Dequeue() : last,
            (key, _) =>
            {
                keys.Add(key);
                return Task.CompletedTask;
            },
            12,
            10,
            (_, _, _) => retries++,
            CancellationToken.None
        );
        var result = new
        {
            source = "real logged screenshots; no game input",
            values,
            keys,
            retries,
        };
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "focus-replay.json"),
            System.Text.Json.JsonSerializer.Serialize(result, Catalog.JsonOptions)
        );
        return values.SequenceEqual(new[] { 1, -1, 0 }) && keys.SequenceEqual(new[] { "Up" }) && retries == 1 ? 0 : 1;
    }

    public static int Frames(string[] paths)
    {
        var p = new Profile();
        var vision = new Vision(Catalog.Load(), p);
        var data = new List<object>();
        foreach (string path in paths)
        {
            using var image = new Bitmap(path);
            int focus = vision.Focus(image, ["topLeft", "middleLeft", "left"]);
            data.Add(new { path, focus });
        }
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "focus-frames.json"),
            System.Text.Json.JsonSerializer.Serialize(data, Catalog.JsonOptions)
        );
        return 0;
    }
}
