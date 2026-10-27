using System.Text.Json;

namespace TunerFest;

public static class RecoveryVerification
{
    public static async Task<int> Tests()
    {
        var checks = new List<string>();
        void Check(bool ok, string text)
        {
            if (!ok)
                throw new Exception(text);
            checks.Add("PASS " + text);
        }
        int current = 6;
        var keys = new List<string>();
        int end = await GridNavigator.Ensure(
            0,
            () => Task.FromResult(current),
            (key, _) =>
            {
                keys.Add(key);
                current += key == "Left" ? -1 : 1;
                return Task.CompletedTask;
            },
            4,
            1,
            null,
            CancellationToken.None
        );
        Check(
            end == 0 && keys.SequenceEqual(Enumerable.Repeat("Left", 6)),
            "last grid item returns left six times instead of assuming right wraps to first"
        );
        current = 0;
        keys.Clear();
        end = await GridNavigator.Ensure(
            1,
            () => Task.FromResult(current),
            (key, _) =>
            {
                keys.Add(key);
                if (keys.Count == 2)
                    current = 1;
                return Task.CompletedTask;
            },
            4,
            1,
            null,
            CancellationToken.None
        );
        Check(end == 1 && keys.Count == 2, "one ignored arrow retries once after confirmed unchanged focus");
        var frames = new Queue<int>(new[] { 3, -1, 4, 4 });
        keys.Clear();
        end = await GridNavigator.Ensure(
            4,
            () => Task.FromResult(frames.Dequeue()),
            (key, _) =>
            {
                keys.Add(key);
                return Task.CompletedTask;
            },
            5,
            1,
            null,
            CancellationToken.None
        );
        Check(
            end == 4 && keys.Count == 0,
            "transient or ambiguous grid selection waits for two stable frames without input"
        );
        current = 0;
        keys.Clear();
        bool stopped = false;
        try
        {
            await GridNavigator.Ensure(
                1,
                () => Task.FromResult(current),
                (key, _) =>
                {
                    keys.Add(key);
                    current = -1;
                    return Task.CompletedTask;
                },
                3,
                1,
                null,
                CancellationToken.None
            );
        }
        catch (FocusReadException)
        {
            stopped = true;
        }
        Check(stopped && keys.Count == 1, "missing focus after input does not trigger blind repeated arrows");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        keys.Clear();
        stopped = false;
        try
        {
            await GridNavigator.Ensure(
                1,
                () => Task.FromResult(0),
                (key, _) =>
                {
                    keys.Add(key);
                    return Task.CompletedTask;
                },
                3,
                1,
                null,
                cancel.Token
            );
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        Check(stopped && keys.Count == 0, "grid cancellation stops before sending input");
        var catalog = Catalog.Load();
        Check(
            Recognition.Parse("Bende Increase the player's lean bonus by 6.3% [3%-8%] for BIKES", catalog).Affix.Title
                == "Bended",
            "observed Bende OCR alias maps exactly to Bended"
        );
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "recovery-test.txt"), checks);
        return 0;
    }

    public static async Task<int> BendedLog(string folder)
    {
        var profile =
            JsonSerializer.Deserialize<Profile>(
                File.ReadAllText(Path.Combine(folder, "settings.json")),
                Catalog.JsonOptions
            ) ?? new();
        var vision = new Vision(Catalog.Load(), profile);
        using var image = new Bitmap(Path.Combine(folder, "screenshots", "last-safe-frame.png"));
        int side = await vision.PopupSide(image);
        var fresh = await vision.Card(image, "topRight", true);
        var current = await vision.Read(image, "right");
        var selection = await vision.Select(
            image,
            ["topRight", "middleRight", "right"],
            new(current.Affix.Title, current),
            RollPhase.Maximize,
            [current.Affix.Title]
        );
        var result = new
        {
            source = folder,
            noGameInput = true,
            side,
            fresh = fresh.ToString(),
            selection = selection.Expected.ToString(),
            index = selection.Index,
        };
        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, "replay-" + Path.GetFileName(folder) + ".json"),
            JsonSerializer.Serialize(result, Catalog.JsonOptions)
        );
        return side == 1 && fresh.Name == "Bended" && selection.Index == 1 && selection.Expected.Name == "Skilled"
            ? 0
            : 1;
    }
}
