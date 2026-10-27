using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PoultryDesigner.Engine;

public static class AllocationEngine
{
    public const string Version = "1.0.0-splitmix64";
    // Bounds protect arithmetic and keep accidental oversized requests out of the desktop app.
    public const int MaximumAnimals = 100_000;
    public static ImmutableArray<string> Validate(Study s)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (!Enum.IsDefined(s.Design)) errors.Add("Unknown design.");
        if (s.Pool.IsDefault || s.SelectedRecordIds.IsDefault || s.Treatments.IsDefault)
            return ["Pool, selection and treatments must be initialized."];
        int t = s.Treatments.Length;
        if (t < 2 || s.Replicates < 2 || s.AnimalsPerPen < 1) errors.Add("Require at least two treatments, two replicates and one animal per pen.");
        long required;
        try { required = checked((long)t * s.Replicates * s.AnimalsPerPen); }
        catch (OverflowException) { required = -1; }
        if (required < 1 || required > MaximumAnimals) errors.Add($"Required animal count must be between 1 and {MaximumAnimals}.");
        if (s.Pool.Length > MaximumAnimals) errors.Add("Pool exceeds the supported resource limit.");
        if (s.SelectedRecordIds.Length != required) errors.Add("Select exactly the required number of animals; automatic selection is unavailable.");
        if (s.CvLimitPercent < 0 || s.CvLimitPercent > 100 || s.MaximumAttemptsPerBlock < 1 || s.MaximumAttemptsPerBlock > 1_000_000)
            errors.Add("CV must be 0–100 percent and attempts must be 1–1,000,000.");
        if (s.FilterBalance && s.Design != Design.Rcbd) errors.Add("Balance filtering is supported only for RCBD.");
        static bool Bad(string? v) => string.IsNullOrWhiteSpace(v) || v != v.Trim();
        if (s.Treatments.Any(x => x is null || Bad(x.Id) || Bad(x.Label))) errors.Add("Treatment IDs and labels must be nonblank and trimmed.");
        else if (s.Treatments.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != t || s.Treatments.Select(x => x.Label).Distinct(StringComparer.Ordinal).Count() != t)
            errors.Add("Treatment IDs and labels must be unique.");
        if (s.Pool.Any(x => x is null || Bad(x.RecordId) || Bad(x.AnimalId) || x.Cohort is null || x.Cohort != x.Cohort.Trim() || x.WeightGrams <= 0 || x.WeightGrams > 1_000_000m))
            errors.Add("Animal identities must be nonblank and trimmed; BW must be positive and at most 1,000,000 grams.");
        else
        {
            var ids = s.Pool.Select(x => x.RecordId).ToHashSet(StringComparer.Ordinal);
            if (ids.Count != s.Pool.Length) errors.Add("Internal record IDs must be unique.");
            if (!s.Demonstration && s.Pool.Select(x => (x.Cohort, x.AnimalId)).Distinct().Count() != s.Pool.Length)
                errors.Add("Animal identities must be unique within each cohort.");
            if (s.SelectedRecordIds.Any(x => x is null || !ids.Contains(x))) errors.Add("Selection contains an unknown record.");
        }
        if (s.SelectedRecordIds.Distinct(StringComparer.Ordinal).Count() != s.SelectedRecordIds.Length) errors.Add("Selection contains duplicate records.");
        return errors.ToImmutable();
    }

    public static string Fingerprint(Study s)
    {
        var selected = s.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
        var payload = new { Version, s.Design, s.Replicates, s.AnimalsPerPen, s.FilterBalance,
            s.CvLimitPercent, s.MaximumAttemptsPerBlock, s.Demonstration, s.Treatments,
            Animals = s.Pool.Where(a => selected.Contains(a.RecordId)).OrderBy(a => a.RecordId, StringComparer.Ordinal)
                .Select(a => new { a.RecordId, a.AnimalId, a.Cohort, Weight = a.WeightGrams.ToString("G29", CultureInfo.InvariantCulture) }) };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }

    public static RunResult Run(Study study, ulong seed, CancellationToken cancellation = default,
        Action<int, int>? progress = null)
    {
        var errors = Validate(study);
        if (!errors.IsEmpty) return new(RunStatus.Invalid, null, errors, []);
        var traces = ImmutableArray.CreateBuilder<AttemptTrace>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var selection = study.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
            Animal[] animals = study.Pool.Where(a => selection.Contains(a.RecordId)).OrderBy(a => a.RecordId, StringComparer.Ordinal).ToArray();
            var ties = StableRandom.Stream(seed, "bw-ties");
            ties.Shuffle(animals);
            animals = animals.OrderByDescending(a => a.WeightGrams).ToArray(); // stable tie order
            int t = study.Treatments.Length, p = study.AnimalsPerPen;
            var positions = ImmutableArray.CreateBuilder<Position>();
            var rng = StableRandom.Stream(seed, "allocation");
            if (study.Design == Design.Crd)
            {
                rng.Shuffle(animals);
                for (int i = 0; i < animals.Length; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int slot = i / p;
                    positions.Add(new(animals[i].RecordId, slot % t, slot / t + 1, 0, 0));
                }
            }
            else
            {
                int[] conventional = Enumerable.Range(0, t).ToArray();
                if (study.Design == Design.Conventional) rng.Shuffle(conventional);
                for (int block = 1; block <= study.Replicates; block++)
                {
                    int max = study.FilterBalance ? study.MaximumAttemptsPerBlock : 1;
                    double best = double.PositiveInfinity, last = 0;
                    bool accepted = false;
                    for (int attempt = 1; attempt <= max; attempt++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var candidate = ImmutableArray.CreateBuilder<Position>();
                        for (int sub = 0; sub < p; sub++)
                        {
                            int[] order = study.Design == Design.Conventional ? (int[])conventional.Clone() : Enumerable.Range(0, t).ToArray();
                            if (study.Design == Design.Rcbd) rng.Shuffle(order);
                            else if (((block - 1) * p + sub) % 2 == 1) Array.Reverse(order);
                            for (int j = 0; j < t; j++) candidate.Add(new(animals[((block - 1) * p + sub) * t + j].RecordId, order[j], block, block, sub + 1, block));
                        }
                        var stats = Statistics(study, candidate.ToImmutable());
                        last = stats.Blocks[0].CvPercent;
                        best = Math.Min(best, last);
                        progress?.Invoke(block, attempt);
                        cancellation.ThrowIfCancellationRequested();
                        if (!study.FilterBalance || Passes(last, study.CvLimitPercent))
                        {
                            positions.AddRange(candidate);
                            traces.Add(new(block, attempt, best, last));
                            accepted = true;
                            break;
                        }
                        if (attempt == max) traces.Add(new(block, attempt, best, last));
                    }
                    if (!accepted) return new(RunStatus.Exhausted, null, ["The block exhausted its balance attempts; no allocation was committed."], traces.ToImmutable());
                }
            }
            cancellation.ThrowIfCancellationRequested();
            var final = positions.ToImmutable();
            var metrics = Statistics(study, final);
            var allocation = new Allocation(Version, Fingerprint(study), seed, final, metrics.Pens,
                metrics.Blocks, traces.ToImmutable(), [], [], null, 1);
            return new(RunStatus.Success, allocation, [], traces.ToImmutable());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { return new(RunStatus.Cancelled, null, [], traces.ToImmutable()); }
    }

    internal static bool Passes(double cv, decimal limit)
    {
        if (limit == 0) return cv == 0;
        double boundary = (double)limit;
        for (int i = 0; i < 8; i++) boundary = Math.BitIncrement(boundary);
        return cv <= boundary;
    }

    public static (ImmutableArray<PenStatistics> Pens, ImmutableArray<BlockStatistics> Blocks)
        Statistics(Study s, ImmutableArray<Position> positions)
    {
        var lookup = s.Pool.ToDictionary(a => a.RecordId, StringComparer.Ordinal);
        var pens = positions.GroupBy(x => (x.TreatmentIndex, x.Replicate)).OrderBy(x => x.Key.Replicate).ThenBy(x => x.Key.TreatmentIndex)
            .Select(g =>
            {
                decimal[] w = g.Select(x => lookup[x.AnimalRecordId].WeightGrams).ToArray();
                decimal total = w.Sum(), mean = total / w.Length;
                double? sd = w.Length < 2 ? null : Math.Sqrt(w.Sum(x => Math.Pow((double)(x - mean), 2)) / (w.Length - 1));
                return new PenStatistics(g.Key.TreatmentIndex, g.Key.Replicate, w.Length, total, mean, sd);
            }).ToImmutableArray();
        var blocks = s.Design == Design.Crd ? [] : pens.GroupBy(x => x.Replicate).OrderBy(g => g.Key).Select(g =>
        {
            // All pens have common P: CV of totals equals CV of means. Totals avoid
            // rounding a tiny difference away while dividing decimals by P.
            decimal[] totals = g.Select(x => x.TotalWeightGrams).ToArray();
            decimal mean = totals.Sum() / totals.Length;
            double cv = totals.All(x => x == totals[0]) ? 0 : 100 * Math.Sqrt(totals.Sum(x => Math.Pow((double)(x - mean) / (double)mean, 2)) / (totals.Length - 1));
            return new BlockStatistics(g.Key, cv);
        }).ToImmutableArray();
        return (pens, blocks);
    }

    internal static void RequireCurrent(Study s, Allocation result)
    {
        var errors = Validate(s);
        if (!errors.IsEmpty || result.EngineVersion != Version || result.InputFingerprint != Fingerprint(s))
            throw new ArgumentException("Allocation does not match a valid current study.");
        if (result.Positions.Length != s.SelectedRecordIds.Length || !result.Positions.Select(x => x.AnimalRecordId).Order(StringComparer.Ordinal).SequenceEqual(s.SelectedRecordIds.Order(StringComparer.Ordinal)))
            throw new ArgumentException("Allocation animal membership is invalid.");
        if (result.Positions.Any(x => x.TreatmentIndex < 0 || x.TreatmentIndex >= s.Treatments.Length || x.Replicate < 1 || x.Replicate > s.Replicates || x.Block != (s.Design == Design.Crd ? 0 : x.Replicate)))
            throw new ArgumentException("Allocation positions are invalid.");
        var pens = result.Positions.GroupBy(x => (x.TreatmentIndex, x.Replicate)).ToArray();
        if (pens.Length != s.Treatments.Length * s.Replicates || pens.Any(g => g.Count() != s.AnimalsPerPen))
            throw new ArgumentException("Allocation pen counts are invalid.");
        var metrics = Statistics(s, result.Positions);
        if (!metrics.Pens.SequenceEqual(result.Pens) || !metrics.Blocks.SequenceEqual(result.Blocks)
            || (s.FilterBalance && metrics.Blocks.Any(x => !Passes(x.CvPercent, s.CvLimitPercent))))
            throw new ArgumentException("Allocation statistics or enabled balance limits are invalid.");
    }

    public static Allocation Swap(Study s, Allocation result, string first, string second, string reason)
    {
        RequireCurrent(s, result);
        if (first == second || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Select two distinct animals and supply an edit reason.");
        var positions = result.Positions.ToArray();
        int a = Array.FindIndex(positions, x => x.AnimalRecordId == first), b = Array.FindIndex(positions, x => x.AnimalRecordId == second);
        if (a < 0 || b < 0) throw new ArgumentException("Both swap animals must be allocated.");
        var oldA = positions[a]; var oldB = positions[b];
        // Membership provenance stays with each animal; only its current assignment moves.
        positions[a] = oldA with { TreatmentIndex = oldB.TreatmentIndex, Replicate = oldB.Replicate, Block = oldB.Block };
        positions[b] = oldB with { TreatmentIndex = oldA.TreatmentIndex, Replicate = oldA.Replicate, Block = oldA.Block };
        var candidate = positions.ToImmutableArray(); var metrics = Statistics(s, candidate);
        if (s.FilterBalance && metrics.Blocks.Any(x => !Passes(x.CvPercent, s.CvLimitPercent))) throw new InvalidOperationException("Swap would violate an enabled BW balance limit.");
        return result with { Positions = candidate, Pens = metrics.Pens, Blocks = metrics.Blocks,
            Edits = result.Edits.Add(new(first, second, reason.Trim(), oldA, oldB, positions[a], positions[b])), Revision = result.Revision + 1 };
    }

    public static Allocation MapPens(Study s, Allocation result, ImmutableArray<PhysicalPen> physical, ulong seed)
    {
        RequireCurrent(s, result);
        if (physical.IsDefault || physical.Length != result.Pens.Length || physical.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Location) || x.Capacity < s.AnimalsPerPen)
            || physical.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != physical.Length || physical.Select(x => x.Location).Distinct(StringComparer.Ordinal).Count() != physical.Length)
            throw new ArgumentException("Provide unique physical IDs and scoped locations, sufficient capacity and exactly one physical pen per unit.");
        bool restricted = physical.Any(x => x.EligibleBlock.HasValue);
        if (restricted && physical.Any(x => !x.EligibleBlock.HasValue || x.EligibleBlock < 1 || x.EligibleBlock > s.Replicates))
            throw new ArgumentException("Replicate eligibility must be complete and refer to an existing replicate.");
        // EligibleBlock is the persisted legacy field name. It restricts housing to a replicate;
        // for blocked designs that replicate is also the BW block. CRD still has no BW blocks.
        var pools = physical.GroupBy(x => restricted ? x.EligibleBlock!.Value : 0)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
        var mapping = ImmutableArray.CreateBuilder<PenLocation>();
        foreach (var group in result.Pens.GroupBy(x => restricted ? x.Replicate : 0).OrderBy(x => x.Key))
        {
            var available = pools.GetValueOrDefault(group.Key) ?? [];
            if (available.Length != group.Count()) throw new ArgumentException("Each replicate must have exactly its required physical pens.");
            if (s.Design == Design.Rcbd && !restricted) throw new ArgumentException("RCBD requires declared block-specific physical pen pools.");
            StableRandom.Stream(seed, $"physical-pens:{group.Key}").Shuffle(available);
            int i = 0;
            foreach (var pen in group.OrderBy(x => x.Replicate).ThenBy(x => x.TreatmentIndex)) mapping.Add(new(pen.TreatmentIndex, pen.Replicate, available[i++].Id));
        }
        return result with { Locations = mapping.ToImmutable(), LocationSeed = seed, Revision = result.Revision + 1 };
    }
}

public sealed class AllocationSession
{
    private readonly object gate = new();
    public Allocation? Active { get; private set; }
    public ImmutableArray<Allocation> History { get; private set; } = [];
    public RunResult Run(Study s, ulong seed, CancellationToken cancellation = default, Action<int, int>? progress = null)
    {
        lock (gate)
        {
            var run = AllocationEngine.Run(s, seed, cancellation, progress);
            if (run.Allocation is not null) { Active = run.Allocation; History = History.Add(Active); }
            return run;
        }
    }
    public Allocation Swap(Study s, string first, string second, string reason)
    {
        lock (gate)
        {
            var revised = AllocationEngine.Swap(s, Active ?? throw new InvalidOperationException("No active result."), first, second, reason);
            Active = revised; History = History.Add(revised); return revised;
        }
    }
    public Allocation MapPens(Study s, ImmutableArray<PhysicalPen> physical, ulong seed)
    {
        lock (gate)
        {
            var revised = AllocationEngine.MapPens(s, Active ?? throw new InvalidOperationException("No active result."), physical, seed);
            Active = revised; History = History.Add(revised); return revised;
        }
    }
    public bool IsStale(Study current) => Active is not null &&
        (!AllocationEngine.Validate(current).IsEmpty || Active.InputFingerprint != AllocationEngine.Fingerprint(current));
}
