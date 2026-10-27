using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

public enum AnimalDisposition { Allocated, ResearcherExcluded, SelectedUnallocated }
public sealed record MovementRow(string RecordId, string AnimalId, string Cohort,
    decimal WeightGrams, string OldLocation, AnimalDisposition Disposition,
    string? TreatmentId, int? Replicate, string? PhysicalPenId);
public sealed record TreatmentBalance(string TreatmentId, string Label, int PenCount,
    decimal MeanWeightGrams, decimal MinimumPenMeanGrams, decimal MaximumPenMeanGrams,
    double SampleSdOfPenMeansGrams, double CvOfPenMeansPercent,
    decimal DifferenceFromOverallGrams, double DifferenceFromOverallPercent);
public sealed record BalanceOverview(decimal OverallMeanGrams, decimal PenMeanRangeGrams,
    ImmutableArray<TreatmentBalance> Treatments);

public static class ResultViews
{
    public static ImmutableArray<MovementRow> Movement(Study s, Allocation? result)
    {
        if (!AllocationEngine.Validate(s).IsEmpty) throw new ArgumentException("Study is invalid.");
        if (result is not null) AllocationEngine.RequireCurrent(s, result);
        var selected = s.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
        var positions = result?.Positions.ToDictionary(x => x.AnimalRecordId, StringComparer.Ordinal);
        var locations = result?.Locations.ToDictionary(x => (x.TreatmentIndex, x.Replicate), x => x.PhysicalPenId);
        return s.Pool.OrderBy(x => x.RecordId, StringComparer.Ordinal).Select(animal =>
        {
            Position? position = null;
            if (positions is not null) positions.TryGetValue(animal.RecordId, out position);
            if (result is not null && selected.Contains(animal.RecordId) && position is null)
                throw new ArgumentException("A selected animal is missing from the completed allocation.");
            string? location = null;
            if (position is not null && locations is not null) locations.TryGetValue((position.TreatmentIndex, position.Replicate), out location);
            return new MovementRow(animal.RecordId, animal.AnimalId, animal.Cohort, animal.WeightGrams,
                animal.OldLocation, position is not null ? AnimalDisposition.Allocated : selected.Contains(animal.RecordId)
                    ? AnimalDisposition.SelectedUnallocated : AnimalDisposition.ResearcherExcluded,
                position is null ? null : s.Treatments[position.TreatmentIndex].Id, position?.Replicate, location);
        }).ToImmutableArray();
    }
    public static BalanceOverview Balance(Study s, Allocation result)
    {
        AllocationEngine.RequireCurrent(s, result);
        var pens = AllocationEngine.Statistics(s, result.Positions).Pens;
        decimal overall = pens.Sum(x => x.MeanWeightGrams) / pens.Length;
        var treatments = pens.GroupBy(x => x.TreatmentIndex).OrderBy(g => g.Key).Select(g =>
        {
            decimal[] means = g.Select(x => x.MeanWeightGrams).ToArray();
            decimal mean = means.Sum() / means.Length;
            double sd = Math.Sqrt(means.Sum(x => Math.Pow((double)(x - mean), 2)) / (means.Length - 1));
            return new TreatmentBalance(s.Treatments[g.Key].Id, s.Treatments[g.Key].Label, means.Length,
                mean, means.Min(), means.Max(), sd, 100 * sd / (double)mean, mean - overall,
                100 * (double)(mean - overall) / (double)overall);
        }).ToImmutableArray();
        return new(overall, pens.Max(x => x.MeanWeightGrams) - pens.Min(x => x.MeanWeightGrams), treatments);
    }
}
