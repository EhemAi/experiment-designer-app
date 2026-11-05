using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

public enum AnimalSex { Unknown, Male, Female }
public enum SexAllocationMode { Ignore, Balance, Separate }
public enum SexBalancePattern { SameInEveryPen, TwoReplicateGroups }
public sealed record SexAllocationSettings(SexBalancePattern Pattern, int MaleReplicates,
    int MalesPerPen, int FirstGroupReplicates, int SecondGroupMalesPerPen);

public static class SexAllocation
{
    public static bool UsesSpecifiedCounts(Study study) => study.SexMode != SexAllocationMode.Ignore && study.SexSettings is not null;

    public static ImmutableArray<string> ValidateSettings(Study study)
    {
        if (!UsesSpecifiedCounts(study)) return [];
        var options = study.SexSettings!;
        if (study.SexMode == SexAllocationMode.Separate)
            return options.MaleReplicates < 0 || options.MaleReplicates > study.Replicates
                ? ["Sex settings: male replicates must be between zero and the total replicate count."] : [];
        if (!Enum.IsDefined(options.Pattern)) return ["Sex settings: choose a valid sex composition pattern."];
        if (options.MalesPerPen < 0 || options.MalesPerPen > study.AnimalsPerPen)
            return ["Sex settings: males per pen must be between zero and the total animals per pen."];
        if (options.Pattern == SexBalancePattern.TwoReplicateGroups && (options.FirstGroupReplicates < 1
            || options.FirstGroupReplicates >= study.Replicates || options.SecondGroupMalesPerPen < 0
            || options.SecondGroupMalesPerPen > study.AnimalsPerPen))
            return ["Sex settings: both replicate groups must be nonempty, and group 2 males per pen must be between zero and the total animals per pen."];
        return [];
    }

    public static (long Males, long Females) RequiredCounts(Study study)
    {
        var options = study.SexSettings ?? throw new ArgumentException("Specify sex counts first.");
        return RequiredCounts(study.Treatments.Length, study.Replicates, study.AnimalsPerPen, study.SexMode, options);
    }

    public static (long Males, long Females) RequiredCounts(int treatments, int replicates, int animalsPerPen, SexAllocationMode mode, SexAllocationSettings options)
    {
        checked
        {
            long malePerTreatment = mode == SexAllocationMode.Separate
                ? (long)options.MaleReplicates * animalsPerPen
                : options.Pattern == SexBalancePattern.TwoReplicateGroups
                    ? (long)options.FirstGroupReplicates * options.MalesPerPen + (long)(replicates - options.FirstGroupReplicates) * options.SecondGroupMalesPerPen
                    : (long)replicates * options.MalesPerPen;
            long males = malePerTreatment * treatments;
            return (males, (long)treatments * replicates * animalsPerPen - males);
        }
    }

    public static string Describe(Study study)
    {
        var settings = study.SexSettings!;
        if (study.SexMode == SexAllocationMode.Separate)
            return $"{settings.MaleReplicates} all-male replicates followed by {study.Replicates - settings.MaleReplicates} all-female replicates; every replicate includes all treatments. Heaviest animals are selected within the specified sex counts.";
        if (settings.Pattern == SexBalancePattern.TwoReplicateGroups)
            return $"Replicates 1–{settings.FirstGroupReplicates}: {settings.MalesPerPen} male + {study.AnimalsPerPen - settings.MalesPerPen} female per pen. Replicates {settings.FirstGroupReplicates + 1}–{study.Replicates}: {settings.SecondGroupMalesPerPen} male + {study.AnimalsPerPen - settings.SecondGroupMalesPerPen} female per pen. Heaviest animals are selected within the specified sex counts.";
        return $"Every pen: {settings.MalesPerPen} male + {study.AnimalsPerPen - settings.MalesPerPen} female. Heaviest animals are selected within the specified sex counts.";
    }

    public static AnimalSex Parse(string value) => value.Trim().ToUpperInvariant() switch
    {
        "" or "U" or "UNK" or "UNKNOWN" or "NA" or "N/A" => AnimalSex.Unknown,
        "M" or "MALE" or "B" => AnimalSex.Male,
        "F" or "FEMALE" or "G" => AnimalSex.Female,
        _ => throw new ArgumentException("Sex: use Male/M/B, Female/F/G, or Unknown; blank means Unknown.")
    };

    public static ImmutableArray<string> Validate(Study study)
    {
        if (study.SexMode == SexAllocationMode.Ignore) return [];
        var selected = study.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
        var animals = study.Pool.Where(a => selected.Contains(a.RecordId)).ToArray();
        if (UsesSpecifiedCounts(study))
        {
            var needs = RequiredCounts(study);
            int selectedMales = animals.Count(a => a.Sex == AnimalSex.Male), selectedFemales = animals.Count(a => a.Sex == AnimalSex.Female);
            if (selectedMales != needs.Males || selectedFemales != needs.Females)
            {
                int availableMales = study.Pool.Count(a => a.Sex == AnimalSex.Male), availableFemales = study.Pool.Count(a => a.Sex == AnimalSex.Female);
                return [$"Sex allocation: the specified composition requires {needs.Males:N0} males and {needs.Females:N0} females. The pool contains {availableMales:N0} known males and {availableFemales:N0} known females; currently selected: {selectedMales:N0} males and {selectedFemales:N0} females. Import enough animals of each required sex, correct sex records, or adjust the composition. Unknown sex cannot fill a male/female quota."];
            }
            return [];
        }
        int unknown = animals.Count(a => a.Sex == AnimalSex.Unknown);
        if (unknown > 0) return [$"Sex allocation: {unknown:N0} selected animals have Unknown sex. Correct their sex or choose Ignore sex. Heaviest-overall selection is unchanged."];
        if (study.SexMode == SexAllocationMode.Separate)
        {
            long blockSize = (long)study.Treatments.Length * study.AnimalsPerPen;
            int males = animals.Count(a => a.Sex == AnimalSex.Male);
            if (blockSize > 0 && males % blockSize != 0)
                return [$"Sex allocation: Separate sexes needs male and female counts that fill whole replicate blocks ({blockSize:N0} animals each). The heaviest selected group has {males:N0} males and {animals.Length - males:N0} females. Change the design, correct sex records, or choose Balance/Ignore. No lighter animals have been substituted."];
        }
        return [];
    }

    internal static int[,] MalePlan(Study study, int males, ulong seed)
    {
        int t = study.Treatments.Length, r = study.Replicates, p = study.AnimalsPerPen;
        var plan = new int[r, t];
        if (UsesSpecifiedCounts(study))
        {
            var settings = study.SexSettings!;
            for (int rep = 0; rep < r; rep++)
            {
                int malesPerPen = study.SexMode == SexAllocationMode.Separate
                    ? rep < settings.MaleReplicates ? p : 0
                    : settings.Pattern == SexBalancePattern.TwoReplicateGroups && rep >= settings.FirstGroupReplicates
                        ? settings.SecondGroupMalesPerPen : settings.MalesPerPen;
                for (int trt = 0; trt < t; trt++) plan[rep, trt] = malesPerPen;
            }
            return plan;
        }
        if (study.SexMode == SexAllocationMode.Separate)
        {
            int maleReplicates = males / (t * p);
            for (int rep = 0; rep < maleReplicates; rep++)
                for (int trt = 0; trt < t; trt++) plan[rep, trt] = p;
            return plan;
        }
        // Each pen differs by at most one male; both treatment and replicate
        // totals differ by at most one. Seeded orders avoid a fixed treatment bias.
        int baseline = males / (r * t), extras = males % (r * t), next = 0;
        int[] treatments = Enumerable.Range(0, t).ToArray(), replicates = Enumerable.Range(0, r).ToArray();
        var random = StableRandom.Stream(seed, "sex-composition");
        random.Shuffle(treatments); random.Shuffle(replicates);
        for (int i = 0; i < r; i++)
        {
            int count = extras / r + (i < extras % r ? 1 : 0);
            for (int j = 0; j < t; j++) plan[replicates[i], j] = baseline;
            for (int j = 0; j < count; j++) plan[replicates[i], treatments[next++ % t]]++;
        }
        return plan;
    }

    internal static RunResult Run(Study study, ulong seed, CancellationToken cancellation, Action<int, int>? progress)
    {
        var traces = ImmutableArray.CreateBuilder<AttemptTrace>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var selected = study.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
            var animals = study.Pool.Where(a => selected.Contains(a.RecordId)).OrderBy(a => a.RecordId, StringComparer.Ordinal).ToArray();
            StableRandom.Stream(seed, "bw-ties").Shuffle(animals);
            animals = animals.OrderByDescending(a => a.WeightGrams).ToArray();
            var males = animals.Where(a => a.Sex == AnimalSex.Male).ToArray();
            var females = animals.Where(a => a.Sex == AnimalSex.Female).ToArray();
            var plan = MalePlan(study, males.Length, seed);
            var rng = StableRandom.Stream(seed, "allocation-sex-v1");
            if (study.Design == Design.Crd) { rng.Shuffle(males); rng.Shuffle(females); }
            int t = study.Treatments.Length, p = study.AnimalsPerPen, maleOffset = 0, femaleOffset = 0;
            int[] conventional = Enumerable.Range(0, t).ToArray();
            if (study.Design == Design.Conventional) rng.Shuffle(conventional);
            var positions = ImmutableArray.CreateBuilder<Position>();
            for (int rep = 0; rep < study.Replicates; rep++)
            {
                int maleCount = Enumerable.Range(0, t).Sum(trt => plan[rep, trt]);
                int femaleCount = t * p - maleCount;
                int max = study.FilterBalance ? study.MaximumAttemptsPerBlock : 1;
                bool accepted = false;
                double best = double.PositiveInfinity, last = 0;
                for (int attempt = 1; attempt <= max; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var candidate = ImmutableArray.CreateBuilder<Position>();
                    for (int sex = 0; sex < 2; sex++)
                    {
                        var pool = sex == 0 ? males : females;
                        int offset = sex == 0 ? maleOffset : femaleOffset;
                        for (int sub = 0; sub < p; sub++)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            int[] order = study.Design == Design.Conventional ? (int[])conventional.Clone() : Enumerable.Range(0, t).ToArray();
                            if (study.Design == Design.Conventional)
                            {
                                if ((rep * p + sub) % 2 == 1) Array.Reverse(order);
                            }
                            else rng.Shuffle(order);
                            foreach (int trt in order)
                            {
                                int capacity = sex == 0 ? plan[rep, trt] : p - plan[rep, trt];
                                if (capacity <= sub) continue;
                                bool crd = study.Design == Design.Crd;
                                candidate.Add(new(pool[offset++].RecordId, trt, rep + 1, crd ? 0 : rep + 1,
                                    crd ? 0 : sex * p + sub + 1, crd ? 0 : rep + 1));
                            }
                        }
                    }
                    var proposed = candidate.ToImmutable();
                    var stats = AllocationEngine.Statistics(study, proposed);
                    last = stats.Blocks.IsEmpty ? 0 : stats.Blocks[0].CvPercent;
                    best = Math.Min(best, last);
                    progress?.Invoke(rep + 1, attempt);
                    cancellation.ThrowIfCancellationRequested();
                    if (!study.FilterBalance || AllocationEngine.Passes(last, study.CvLimitPercent))
                    {
                        positions.AddRange(proposed);
                        if (study.Design != Design.Crd) traces.Add(new(rep + 1, attempt, best, last));
                        accepted = true; break;
                    }
                    if (attempt == max) traces.Add(new(rep + 1, attempt, best, last));
                }
                if (!accepted) return new(RunStatus.Exhausted, null, ["The block exhausted its balance attempts; no allocation was committed."], traces.ToImmutable());
                maleOffset += maleCount; femaleOffset += femaleCount;
            }
            cancellation.ThrowIfCancellationRequested();
            var final = positions.ToImmutable();
            RequireComposition(study, final);
            var metrics = AllocationEngine.Statistics(study, final);
            var allocation = new Allocation(AllocationEngine.Version, AllocationEngine.Fingerprint(study), seed,
                final, metrics.Pens, metrics.Blocks, traces.ToImmutable(), [], [], null, 1);
            return new(RunStatus.Success, allocation, [], traces.ToImmutable());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { return new(RunStatus.Cancelled, null, [], traces.ToImmutable()); }
    }

    internal static void RequireComposition(Study study, ImmutableArray<Position> positions)
    {
        if (study.SexMode == SexAllocationMode.Ignore) return;
        var sexes = study.Pool.ToDictionary(a => a.RecordId, a => a.Sex, StringComparer.Ordinal);
        if (positions.Any(p => sexes[p.AnimalRecordId] == AnimalSex.Unknown))
            throw new ArgumentException("Sex allocation: selected animals must have known sex.");
        var pens = positions.GroupBy(p => (p.Replicate, p.TreatmentIndex))
            .Select(g => new { g.Key.Replicate, g.Key.TreatmentIndex, Males = g.Count(p => sexes[p.AnimalRecordId] == AnimalSex.Male) }).ToArray();
        if (UsesSpecifiedCounts(study))
        {
            var plan = MalePlan(study, 0, 0);
            if (pens.Any(p => p.Males != plan[p.Replicate - 1, p.TreatmentIndex]))
                throw new ArgumentException("Sex allocation: this swap would violate the specified male/female composition for a pen or replicate group.");
            return;
        }
        if (study.SexMode == SexAllocationMode.Separate)
        {
            int maleReplicates = positions.Count(p => sexes[p.AnimalRecordId] == AnimalSex.Male) / (study.Treatments.Length * study.AnimalsPerPen);
            if (pens.Any(p => p.Males != (p.Replicate <= maleReplicates ? study.AnimalsPerPen : 0)))
                throw new ArgumentException("Sex allocation: this swap would violate the separate all-male/all-female replicate blocks.");
        }
        else
        {
            static bool Uneven(IEnumerable<int> counts) { var values = counts.ToArray(); return values.Length > 0 && values.Max() - values.Min() > 1; }
            if (Uneven(pens.Select(p => p.Males)) || Uneven(pens.GroupBy(p => p.TreatmentIndex).Select(g => g.Sum(p => p.Males)))
                || Uneven(pens.GroupBy(p => p.Replicate).Select(g => g.Sum(p => p.Males))))
                throw new ArgumentException("Sex allocation: this swap would violate sex balance across pens, treatments or replicates.");
        }
    }
}
