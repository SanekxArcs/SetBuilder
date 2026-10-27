namespace TunerFest;

public sealed class FocusReadException(string message, int lastFocus) : InvalidOperationException(message)
{
    public int LastFocus { get; } = lastFocus;
}

public static class FocusNavigator
{
    public static async Task Ensure(
        int desired,
        Func<Task<int>> observe,
        Func<string, CancellationToken, Task> press,
        int readAttempts,
        int retryMs,
        Action<string, int, int>? retry,
        CancellationToken ct,
        int known = -1
    )
    {
        if (desired < 0 || desired > 2 || readAttempts < 1 || readAttempts > 100 || retryMs < 10 || retryMs > 1000)
            throw new ArgumentOutOfRangeException(nameof(readAttempts));
        async Task<int> Read(int? previous)
        {
            int last = -1;
            for (int attempt = 1; attempt <= readAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                last = await observe();
                if (last >= 0 && last != previous)
                    return last;
                retry?.Invoke(
                    previous.HasValue && last == previous ? "waiting-for-movement" : "waiting-for-frame",
                    attempt,
                    last
                );
                if (attempt < readAttempts)
                    await Task.Delay(retryMs, ct);
            }
            throw new FocusReadException($"Рамку не підтверджено після {readAttempts} зчитувань.", last);
        }
        // A focus index confirmed by the caller's last read skips one capture.
        int focus = known >= 0 && known <= 2 ? known : await Read(null);
        for (int moves = 0; moves < 4; moves++)
        {
            if (focus == desired)
                return;
            int previous = focus;
            string direction = focus < desired ? "Down" : "Up";
            for (int keyAttempt = 0; keyAttempt < 2; keyAttempt++)
            {
                await press(direction, ct);
                try
                {
                    focus = await Read(previous);
                    break;
                }
                catch (FocusReadException e) when (keyAttempt == 0 && e.LastFocus == previous)
                {
                    retry?.Invoke("retry-navigation-key", keyAttempt + 1, previous);
                }
            }
        }
        if (focus != desired)
            throw new InvalidOperationException("Не вдалося перейти до потрібного варіанта після 4 переміщень.");
    }
}
