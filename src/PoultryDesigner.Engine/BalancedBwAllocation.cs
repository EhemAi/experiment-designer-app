using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

// Rank subgroups follow the macro: each pen receives one animal from each
// eligible BW tier, so distributions remain comparable as means are balanced.
internal static class BalancedBwAllocation
{
    internal const double AutomaticCvTarget = (double)AllocationEngine.AutomaticBwCvTargetPercent;
    private sealed record Tier(Animal[] Animals, int[] Pens, int Number);
    private sealed record Candidate(int[][] Assignments, double Cv, decimal MaxRange, decimal SumRange);

    private static double Cv(decimal[] totals)
    {
        decimal mean = totals.Sum() / totals.Length;
        return totals.All(w => w == totals[0]) ? 0 : 100 * Math.Sqrt(totals.Sum(w => Math.Pow((double)(w - mean) / (double)mean, 2)) / (totals.Length - 1));
    }

    private static bool Better(Candidate next, Candidate best, double target)
    {
        bool pass = next.Cv <= target, previousPass = best.Cv <= target;
        if (pass != previousPass) return pass;
        if (!pass && next.Cv != best.Cv) return next.Cv < best.Cv;
        if (next.MaxRange != best.MaxRange) return next.MaxRange < best.MaxRange;
        if (next.SumRange != best.SumRange) return next.SumRange < best.SumRange;
        return next.Cv < best.Cv;
    }

    internal static RunResult Run(Study study, ulong seed, CancellationToken cancellation, Action<int, int>? progress)
    {
        var traces = ImmutableArray.CreateBuilder<AttemptTrace>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var selected = study.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
            var animals = study.Pool.Where(a => selected.Contains(a.RecordId)).OrderBy(a => a.RecordId, StringComparer.Ordinal).ToArray();
            StableRandom.Stream(seed, "balanced-bw-ties:v1").Shuffle(animals);
            animals = animals.OrderByDescending(a => a.WeightGrams).ToArray();
            bool sexAware = study.SexMode != SexAllocationMode.Ignore;
            var males = sexAware ? animals.Where(a => a.Sex == AnimalSex.Male).ToArray() : [];
            var females = sexAware ? animals.Where(a => a.Sex == AnimalSex.Female).ToArray() : [];
            int t = study.Treatments.Length, p = study.AnimalsPerPen, maleOffset = 0, femaleOffset = 0;
            int[,]? plan = sexAware ? SexAllocation.MalePlan(study, males.Length, seed) : null;
            var positions = ImmutableArray.CreateBuilder<Position>();
            double target = study.FilterBalance ? (double)study.CvLimitPercent : AutomaticCvTarget;
            for (int rep = 0; rep < study.Replicates; rep++)
            {
                cancellation.ThrowIfCancellationRequested();
                int[] capacities = Enumerable.Range(0, t).Select(j => sexAware ? plan![rep, j] : p).ToArray();
                var tiers = new List<Tier>();
                void AddTiers(Animal[] pool, ref int offset, int[] counts)
                {
                    for (int depth = 0; depth < counts.Max(); depth++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int[] pens = Enumerable.Range(0, t).Where(j => counts[j] > depth).ToArray();
                        tiers.Add(new(pool.AsSpan(offset, pens.Length).ToArray(), pens, tiers.Count + 1));
                        offset += pens.Length;
                    }
                }
                if (sexAware)
                {
                    AddTiers(males, ref maleOffset, capacities);
                    AddTiers(females, ref femaleOffset, capacities.Select(m => p - m).ToArray());
                }
                else
                {
                    int offset = rep * t * p;
                    AddTiers(animals, ref offset, capacities);
                }
                var random = StableRandom.Stream(seed, $"balanced-bw-candidates:v1:{rep}");
                Candidate? best = null; double bestCv = double.PositiveInfinity;
                int attempts = 0;
                for (int attempt = 0; attempt < study.MaximumAttemptsPerBlock; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var assignments = new int[tiers.Count][];
                    var totals = new decimal[t];
                    var minimum = Enumerable.Repeat(decimal.MaxValue, t).ToArray();
                    var maximum = new decimal[t];
                    int[] order = Enumerable.Range(0, tiers.Count).ToArray();
                    if (attempt > 0) random.Shuffle(order);
                    foreach (int index in order)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var tier = tiers[index]; int[] pens = (int[])tier.Pens.Clone();
                        random.Shuffle(pens);
                        // First candidate is the macro's random tier permutation.
                        // Later candidates pair larger BW with smaller running totals;
                        // randomized tier order/ties explore different distributions.
                        if (attempt > 0) pens = pens.OrderBy(j => totals[j]).ToArray();
                        assignments[index] = pens;
                        for (int k = 0; k < pens.Length; k++)
                        {
                            int pen = pens[k]; decimal weight = tier.Animals[k].WeightGrams;
                            totals[pen] += weight;
                            minimum[pen] = Math.Min(minimum[pen], weight);
                            maximum[pen] = Math.Max(maximum[pen], weight);
                        }
                    }
                    decimal[] ranges = Enumerable.Range(0, t).Select(j => maximum[j] - minimum[j]).ToArray();
                    var candidate = new Candidate(assignments, Cv(totals), ranges.Max(), ranges.Sum());
                    if (best is null || Better(candidate, best, target)) best = candidate;
                    bestCv = Math.Min(bestCv, candidate.Cv); attempts++;
                    progress?.Invoke(rep + 1, attempts);
                    if (best.Cv == 0 && best.MaxRange == 0) break;
                }
                // The final treatment permutation makes treatment labels exchangeable
                // without changing tier membership, sex counts, ranges or BW balance.
                int[] treatmentMap = Enumerable.Range(0, t).ToArray();
                var allocationRandom = StableRandom.Stream(seed, $"balanced-bw-treatments:v1:{rep}");
                foreach (var group in Enumerable.Range(0, t).GroupBy(j => capacities[j]).OrderBy(g => g.Key))
                {
                    int[] source = group.ToArray(), destinations = group.ToArray();
                    allocationRandom.Shuffle(destinations);
                    for (int k = 0; k < source.Length; k++) treatmentMap[source[k]] = destinations[k];
                }
                traces.Add(new(rep + 1, attempts, bestCv, best!.Cv));
                if (study.FilterBalance && !AllocationEngine.Passes(best.Cv, study.CvLimitPercent))
                    return new(RunStatus.Exhausted, null, ["The block could not meet its BW balance limit while preserving ranked BW subgroups and sex counts. No allocation was committed."], traces.ToImmutable());
                for (int index = 0; index < tiers.Count; index++)
                {
                    var tier = tiers[index];
                    for (int k = 0; k < tier.Animals.Length; k++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        positions.Add(new(tier.Animals[k].RecordId, treatmentMap[best.Assignments[index][k]], rep + 1, rep + 1, tier.Number, rep + 1));
                    }
                }
            }
            cancellation.ThrowIfCancellationRequested();
            var final = positions.ToImmutable();
            SexAllocation.RequireComposition(study, final);
            var metrics = AllocationEngine.Statistics(study, final);
            var allocation = new Allocation(AllocationEngine.Version, AllocationEngine.Fingerprint(study), seed,
                final, metrics.Pens, metrics.Blocks, traces.ToImmutable(), [], [], null, 1);
            return new(RunStatus.Success, allocation, [], traces.ToImmutable());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { return new(RunStatus.Cancelled, null, [], traces.ToImmutable()); }
    }
}
