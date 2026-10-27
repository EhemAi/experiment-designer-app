using System.Collections.Immutable;
using System.Globalization;

namespace PoultryDesigner.Engine;

public sealed record HousingPlan(ImmutableArray<Treatment> Treatments, int Replicates, int AnimalsPerUnit,
    string Unit, int First, int Last, ulong Seed, ImmutableArray<PhysicalPen> Pens, ImmutableArray<PenLocation> Locations);

public static class HousingLayouts
{
    public static HousingPlan Create(ProjectInputs input, int first, int last, ulong seed)
    {
        var s = input.Study;
        long count = (long)s.Treatments.Length * s.Replicates;
        if (input.ExperimentalUnit is not ("pen" or "cage") || s.Treatments.IsDefault || s.Treatments.Length < 2
            || s.Replicates < 2 || s.AnimalsPerPen < 1 || count > AllocationEngine.MaximumAnimals || count * s.AnimalsPerPen > AllocationEngine.MaximumAnimals
            || s.Treatments.Any(t => string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Label))
            || s.Treatments.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != s.Treatments.Length)
            throw new ArgumentException("Complete valid treatments, replicates and animals per unit before preparing a layout.");
        if (first < 1 || last < first || (long)last - first + 1 != count)
            throw new ArgumentException($"Choose exactly {count:N0} {input.ExperimentalUnit}s, one per treatment in each replicate.");
        var pens = Enumerable.Range(0, (int)count).Select(i => {
            string n = ((long)first + i).ToString(CultureInfo.InvariantCulture);
            return new PhysicalPen(n, (input.ExperimentalUnit == "cage" ? "Cage " : "Pen ") + n, s.AnimalsPerPen, i / s.Treatments.Length + 1);
        }).ToImmutableArray();
        var locations = ImmutableArray.CreateBuilder<PenLocation>();
        for (int rep = 1; rep <= s.Replicates; rep++) {
            var pool = pens.Skip((rep - 1) * s.Treatments.Length).Take(s.Treatments.Length).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            StableRandom.Stream(seed, $"physical-pens:{rep}").Shuffle(pool);
            for (int t = 0; t < pool.Length; t++) locations.Add(new(t, rep, pool[t].Id));
        }
        return new(s.Treatments, s.Replicates, s.AnimalsPerPen, input.ExperimentalUnit, first, last, seed, pens, locations.ToImmutable());
    }
    public static bool Matches(HousingPlan plan, ProjectInputs input) => plan.Treatments.SequenceEqual(input.Study.Treatments)
        && plan.Replicates == input.Study.Replicates && plan.AnimalsPerUnit == input.Study.AnimalsPerPen && plan.Unit == input.ExperimentalUnit;
    internal static void Validate(HousingPlan plan)
    {
        var input = ProjectInputs.FromStudy(new(Design.Rcbd, [], [], plan.Treatments, plan.Replicates, plan.AnimalsPerUnit), "Layout validation") with { ExperimentalUnit = plan.Unit };
        var replay = Create(input, plan.First, plan.Last, plan.Seed);
        if (plan.Pens.IsDefault || plan.Locations.IsDefault || !plan.Pens.SequenceEqual(replay.Pens) || !plan.Locations.SequenceEqual(replay.Locations))
            throw new ArgumentException("Prepared housing layout disagrees with its settings and seed.");
    }
}
