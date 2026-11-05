using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

// RCBD with cages as the experimental units. Form compact cages inside each
// existing BW/sex block, then randomize complete cages to treatments.
internal static class CompactCageAllocation
{
    private sealed record Cage(Animal[] Animals, int Males, int SourceGroup);

    internal static RunResult Run(Study study, ulong seed, CancellationToken cancellation, Action<int, int>? progress)
    {
        var traces = ImmutableArray.CreateBuilder<AttemptTrace>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var ids = study.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
            var animals = study.Pool.Where(a => ids.Contains(a.RecordId)).OrderBy(a => a.RecordId, StringComparer.Ordinal).ToArray();
            StableRandom.Stream(seed, "compact-bw-ties:v1").Shuffle(animals);
            animals = animals.OrderByDescending(a => a.WeightGrams).ToArray();
            bool sexAware = study.SexMode != SexAllocationMode.Ignore;
            var males = sexAware ? animals.Where(a => a.Sex == AnimalSex.Male).ToArray() : [];
            var females = sexAware ? animals.Where(a => a.Sex == AnimalSex.Female).ToArray() : [];
            int t = study.Treatments.Length, p = study.AnimalsPerPen, maleOffset = 0, femaleOffset = 0;
            int[,]? plan = sexAware ? SexAllocation.MalePlan(study, males.Length, seed) : null;
            var positions = ImmutableArray.CreateBuilder<Position>();
            for (int rep = 0; rep < study.Replicates; rep++)
            {
                cancellation.ThrowIfCancellationRequested();
                int[] capacities = Enumerable.Range(0, t).Select(j => sexAware ? plan![rep, j] : 0).ToArray();
                int[] order = Enumerable.Range(0, t).ToArray();
                var formation = StableRandom.Stream(seed, $"compact-formation:v1:{rep}");
                // Explicit sex settings give every treatment the same composition.
                // Older automatic mixes can differ by one; try bounded capacity
                // orders, still taking consecutive ranks from each sex pool.
                int attempts = sexAware && capacities.Distinct().Count() > 1 ? Math.Min(32, t * 2) : 1;
                Cage[]? bestCages = null; double bestSpread = double.PositiveInfinity;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (attempt > 0) formation.Shuffle(order);
                    int nextMale = maleOffset, nextFemale = femaleOffset;
                    var cages = new Cage[t]; double spread = 0;
                    for (int group = 0; group < t; group++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int maleCount = capacities[order[group]];
                        Animal[] members;
                        if (!sexAware) members = animals.AsSpan((rep * t + group) * p, p).ToArray();
                        else
                        {
                            members = new Animal[p];
                            males.AsSpan(nextMale, maleCount).CopyTo(members);
                            females.AsSpan(nextFemale, p - maleCount).CopyTo(members.AsSpan(maleCount));
                            nextMale += maleCount; nextFemale += p - maleCount;
                        }
                        decimal mean = members.Sum(a => a.WeightGrams) / p;
                        spread += members.Sum(a => Math.Pow((double)(a.WeightGrams - mean), 2));
                        cages[group] = new(members, maleCount, group + 1);
                    }
                    if (spread < bestSpread) { bestSpread = spread; bestCages = cages; }
                    if (bestSpread == 0) break;
                }
                // Permute complete cages only. For legacy uneven compositions,
                // randomization stays inside matching sex-capacity strata.
                var assignment = StableRandom.Stream(seed, $"compact-treatment:v1:{rep}");
                var totals = new decimal[t];
                foreach (var stratum in bestCages!.GroupBy(c => c.Males).OrderBy(g => g.Key))
                {
                    var cages = stratum.ToArray();
                    int[] treatments = Enumerable.Range(0, t).Where(j => capacities[j] == stratum.Key).ToArray();
                    assignment.Shuffle(treatments);
                    for (int j = 0; j < cages.Length; j++)
                    {
                        totals[treatments[j]] = cages[j].Animals.Sum(a => a.WeightGrams);
                        foreach (var animal in cages[j].Animals)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            positions.Add(new(animal.RecordId, treatments[j], rep + 1, rep + 1, cages[j].SourceGroup, rep + 1));
                        }
                    }
                }
                maleOffset += capacities.Sum(); femaleOffset += t * p - capacities.Sum();
                // CV is descriptive: permuting intact cages cannot change it.
                decimal meanTotal = totals.Sum() / t;
                double cv = totals.All(w => w == totals[0]) ? 0 : 100 * Math.Sqrt(totals.Sum(w => Math.Pow((double)(w - meanTotal) / (double)meanTotal, 2)) / (t - 1));
                traces.Add(new(rep + 1, 1, cv, cv)); progress?.Invoke(rep + 1, 1);
            }
            cancellation.ThrowIfCancellationRequested();
            var final = positions.ToImmutable();
            SexAllocation.RequireComposition(study, final);
            var statistics = AllocationEngine.Statistics(study, final);
            var allocation = new Allocation(AllocationEngine.Version, AllocationEngine.Fingerprint(study), seed,
                final, statistics.Pens, statistics.Blocks, traces.ToImmutable(), [], [], null, 1);
            return new(RunStatus.Success, allocation, [], traces.ToImmutable());
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { return new(RunStatus.Cancelled, null, [], traces.ToImmutable()); }
    }
}
