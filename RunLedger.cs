using System.Globalization;

namespace TunerFest;

public record RollSample(
    int RollId,
    DateTimeOffset Time,
    string Part,
    int Slot,
    string Phase,
    string[] Targets,
    string? OtherPerk,
    PerkCard? Fresh,
    PerkCard? Reroll,
    PerkCard? Current,
    int Choice,
    PerkCard Expected,
    string Frame
);

public sealed class RunLedger(SessionLog? trace = null)
{
    public const int BucksPerRoll = 1500,
        SparePartsPerRoll = 150;
    readonly HashSet<int> requested = [],
        confirmed = [],
        notSent = [],
        observed = [];
    readonly Dictionary<string, int> freshCounts = new();
    int next,
        resumed,
        saved;
    public int Confirmed => confirmed.Count;
    public int Pending => requested.Count(id => !confirmed.Contains(id) && !notSent.Contains(id));

    public int Request(string part, int slot, RollPhase phase)
    {
        int id = ++next;
        requested.Add(id);
        Emit(
            "requested",
            new
            {
                rollId = id,
                part,
                slot = slot + 1,
                phase = phase.ToString(),
            }
        );
        return id;
    }

    public void NotSent(int id)
    {
        if (notSent.Add(id))
            Emit("not_sent", new { rollId = id });
    }

    public void Confirm(int id)
    {
        if (!requested.Contains(id) || notSent.Contains(id))
            throw new InvalidOperationException("Invalid paid roll confirmation");
        if (confirmed.Add(id))
            Emit(
                "confirmed",
                new
                {
                    rollId = id,
                    bucks = BucksPerRoll,
                    spareParts = SparePartsPerRoll,
                    totalConfirmed = Confirmed,
                    totalBucks = (long)Confirmed * BucksPerRoll,
                    totalSpareParts = (long)Confirmed * SparePartsPerRoll,
                }
            );
    }

    public void Offer(RollSample sample)
    {
        if (!confirmed.Contains(sample.RollId))
            throw new InvalidOperationException("Offers require a confirmed paid roll");
        if (!observed.Add(sample.RollId))
            return;
        if (sample.Fresh != null)
        {
            freshCounts.TryGetValue(sample.Fresh.Name, out var n);
            freshCounts[sample.Fresh.Name] = n + 1;
        }
        trace?.AppendOffer(sample);
        Emit("offer", sample);
    }

    public void Saved(int id, PerkCard actual)
    {
        saved++;
        Emit("saved", new { rollId = id, actual = actual.ToString() });
    }

    public void Resume(string part, int slot, PopupSelection selection)
    {
        resumed++;
        Emit(
            "resumed",
            new
            {
                part,
                slot = slot + 1,
                selection,
                bucksChargedInThisSession = 0,
                sparePartsChargedInThisSession = 0,
            }
        );
    }

    void Emit(string kind, object data)
    {
        trace?.AppendRoll(kind, data);
        trace?.SaveSpending(Snapshot());
    }

    public object Snapshot() =>
        new
        {
            confirmedRolls = Confirmed,
            observedRolls = observed.Count,
            missingOfferRecords = Confirmed - observed.Count,
            savedChoices = saved,
            pendingUnconfirmed = Pending,
            resumedMenus = resumed,
            unitBucks = BucksPerRoll,
            unitSpareParts = SparePartsPerRoll,
            bucksSpent = (long)Confirmed * BucksPerRoll,
            sparePartsSpent = (long)Confirmed * SparePartsPerRoll,
            possibleAdditionalBucks = (long)Pending * BucksPerRoll,
            possibleAdditionalSpareParts = (long)Pending * SparePartsPerRoll,
            recognizedFreshOffers = freshCounts.Values.Sum(),
            freshOfferCounts = freshCounts.OrderByDescending(p => p.Value).ToDictionary(p => p.Key, p => p.Value),
            notes = "Counts are confirmed paid menu openings. Repeated OCR frames do not add rolls. Resumed menus predate this session. Pending presses may have spent currency but were not visually confirmed.",
        };

    public string Text() =>
        $"Витрати: {Confirmed} кидків · {((long)Confirmed * BucksPerRoll).ToString("N0", CultureInfo.InvariantCulture)} Bucks · {((long)Confirmed * SparePartsPerRoll).ToString("N0", CultureInfo.InvariantCulture)} запчастин"
        + (
            Pending > 0
                ? $". Ще {Pending} непідтверджених натискань: можливі додаткові {Pending * BucksPerRoll:N0} Bucks / {Pending * SparePartsPerRoll:N0} запчастин."
                : "."
        );
}
