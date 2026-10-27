namespace TunerFest;

public record PerkCard(string Name, Reading? Stats)
{
    public bool Maxed => Stats?.Maxed == true;

    public override string ToString() => Stats?.ToString() ?? Name;

    public static bool Same(PerkCard a, PerkCard b) =>
        a.Name == b.Name && (a.Stats == null || b.Stats == null || Recognition.Equal(a.Stats, b.Stats));
}

public record PartView(string Id, PerkCard[] Cards, int Focus);

public enum RollPhase
{
    Search,
    Maximize,
}

public record PopupSelection(
    int Index,
    PerkCard Expected,
    RollPhase Phase,
    PerkCard? Fresh = null,
    PerkCard? Reroll = null,
    PerkCard? Current = null
);

public enum GameScreen
{
    Unknown,
    Parts,
    Inventory,
    Overview,
    Ready,
    PopupLeft,
    PopupRight,
}

public interface IBuildGame
{
    Task Prepare(CancellationToken ct);
    Task<PartView> ReadPart(CancellationToken ct);
    Task<PerkCard> Roll(int slot, PerkCard current, RollPhase phase, string[] targets, CancellationToken ct);
    Task ExitToParts(CancellationToken ct);
    Task NextPart(CancellationToken ct);
    async Task Advance(CancellationToken ct)
    {
        await ExitToParts(ct);
        await NextPart(ct);
    }
}

public sealed class BuildWorkflow(IBuildGame game, Profile profile, Action<string> log)
{
    public async Task Run(CancellationToken ct)
    {
        await game.Prepare(ct);
        var visited = new HashSet<string>();
        string[] wanted = [profile.First, profile.Second];
        for (int part = 0; part < profile.Parts; part++)
        {
            ct.ThrowIfCancellationRequested();
            var state = await game.ReadPart(ct);
            if (!visited.Add(state.Id))
                throw new InvalidOperationException("Повторно відкрита вже оброблена деталь.");
            log($"Деталь {part + 1}/{profile.Parts}: {state.Id}");
            var cards = state.Cards.ToArray();
            log($"Шукаю пару: {profile.First} + {profile.Second}. Порядок слотів довільний.");
            int firstSlot = Array.FindIndex(cards, c => wanted.Contains(c.Name));
            if (firstSlot < 0)
            {
                firstSlot = state.Focus is 0 or 1 ? state.Focus : 0;
                log($"Слот {firstSlot + 1}: шукаю будь-який — {string.Join(" або ", wanted)}");
                while (!wanted.Contains(cards[firstSlot].Name))
                    cards[firstSlot] = await game.Roll(firstSlot, cards[firstSlot], RollPhase.Search, wanted, ct);
            }
            string found = cards[firstSlot].Name;
            int secondSlot = 1 - firstSlot;
            string remaining = wanted.Single(n => n != found);
            log($"{found} знайдено у слоті {firstSlot + 1}. Закріплюю {found} у слоті {firstSlot + 1}.");
            if (cards[secondSlot].Name != remaining)
                log($"Шукаю лише {remaining} у слоті {secondSlot + 1}; {found} у слоті {firstSlot + 1} залишаю.");
            while (cards[secondSlot].Name != remaining)
                cards[secondSlot] = await game.Roll(secondSlot, cards[secondSlot], RollPhase.Search, [remaining], ct);
            log(
                $"{remaining} знайдено у слоті {secondSlot + 1}. Пара зібрана: слот 1 — {cards[0].Name}, слот 2 — {cards[1].Name}."
            );
            // First acquire both names, then maximize the second-found slot and finally the first-found slot.
            foreach (int slot in new[] { secondSlot, firstSlot })
            {
                log(
                    cards[slot].Maxed
                        ? $"Слот {slot + 1}: {cards[slot]} — максимум уже є."
                        : $"Покращую {cards[slot].Name} у слоті {slot + 1} до максимуму: {cards[slot]}."
                );
                string name = cards[slot].Name;
                while (!cards[slot].Maxed)
                {
                    cards[slot] = await game.Roll(slot, cards[slot], RollPhase.Maximize, [name], ct);
                    if (cards[slot].Name != name)
                        throw new InvalidOperationException("Під час покращення змінилася назва перка.");
                }
            }
            var final = await game.ReadPart(ct);
            if (
                final.Id != state.Id
                || final.Cards.Any(c => !c.Maxed)
                || !final.Cards.Select(c => c.Name).Order().SequenceEqual(wanted.Order())
            )
                throw new InvalidOperationException("Підсумкова перевірка двох перків не пройшла.");
            log($"✓ Деталь {part + 1}: {final.Cards[0]} + {final.Cards[1]}");
            if (part + 1 < profile.Parts)
                await game.Advance(ct);
            else
                await game.ExitToParts(ct);
        }
        log($"✓ Готово: {profile.Set}, {profile.Parts} деталей. Обидва перки на максимумі.");
    }
}

public static class NavigationRoute
{
    public static readonly string[] Open = ["Confirm", "Recalibrate", "Confirm"];
    public static readonly string[] Exit = ["Back", "Back", "Back"];
    public static readonly string[] QuickNext = ["Back", "Back", "Down", "Recalibrate", "Confirm"];
}

public record PadChoice(string Code, string Label)
{
    public override string ToString() => Label;
}

public static class PartTransition
{
    public static readonly string[] Categories =
    [
        "BRAKES",
        "GEARBOX",
        "MOTORCORE",
        "ECU",
        "EXHAUST",
        "SUSPENSION",
        "TIRES",
    ];

    public static int Category(string name)
    {
        string normalized = Recognition.Normalize(name);
        for (int i = 0; i < Categories.Length; i++)
            if (normalized == Categories[i] || normalized.EndsWith(Categories[i], StringComparison.Ordinal))
                return i;
        return -1;
    }

    public static async Task Run(
        bool quick,
        Func<Task> fast,
        Func<Task> classic,
        Func<Task> fallback,
        Action<Exception> report,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!quick)
        {
            await classic();
            return;
        }
        try
        {
            await fast();
        }
        catch (InvalidOperationException e) when (e is not FocusLostException)
        {
            ct.ThrowIfCancellationRequested();
            report(e);
            await fallback();
        }
    }
}
