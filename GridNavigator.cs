namespace TunerFest;

public static class GridNavigator
{
    // Moves the seven-parts cursor to the target tile. Gray and pink tiles are not selectable in the game, so one
    // arrow can jump over several of them; a landing past the target is accepted when that tile is selectable
    // (gold) and the caller then works with the returned index instead of the requested one.
    public static async Task<int> Ensure(
        int target,
        Func<Task<int>> observe,
        Func<string, CancellationToken, Task> press,
        int attempts,
        int retryMs,
        Action<string, int, int>? retry,
        CancellationToken ct,
        Func<int, bool>? selectable = null
    )
    {
        if (target < 0 || target > 6 || attempts < 2 || retryMs < 1)
            throw new ArgumentOutOfRangeException(nameof(target));
        async Task<int> Stable(int? previous)
        {
            int last = -1,
                prior = -1;
            for (int n = 1; n <= attempts; n++)
            {
                ct.ThrowIfCancellationRequested();
                last = await observe();
                if (last >= 0 && last <= 6 && last == prior && last != previous)
                    return last;
                prior = last;
                retry?.Invoke("waiting", n, last);
                if (n < attempts)
                    await Task.Delay(retryMs, ct);
            }
            throw new FocusReadException("Рамку деталі не підтверджено після очікування.", last);
        }
        int current = await Stable(null);
        for (int move = 0; current != target && move < 6; move++)
        {
            int previous = current,
                direction = target > current ? 1 : -1;
            string key = direction > 0 ? "Right" : "Left";
            for (int keyTry = 0; keyTry < 2; keyTry++)
            {
                ct.ThrowIfCancellationRequested();
                await press(key, ct);
                try
                {
                    current = await Stable(previous);
                    break;
                }
                catch (FocusReadException e) when (keyTry == 0 && e.LastFocus == previous)
                {
                    retry?.Invoke("retry-key", 1, previous);
                }
            }
            if ((current - previous) * direction <= 0)
                throw new InvalidOperationException("Стрілка вибрала неочікувану деталь; зупиняю перехід.");
            bool passed = direction > 0 ? current > target : current < target;
            if (!passed)
                continue;
            if (selectable?.Invoke(current) == true)
            {
                retry?.Invoke("skipped-target", move + 1, current);
                return current;
            }
            throw new InvalidOperationException("Не вдалося вибрати потрібну деталь.");
        }
        if (current != target)
            throw new InvalidOperationException("Не вдалося вибрати потрібну деталь.");
        return current;
    }
}
