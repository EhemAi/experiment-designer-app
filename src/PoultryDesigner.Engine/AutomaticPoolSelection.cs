using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

public static class AutomaticPoolSelection
{
    public static ProjectInputs Apply(ProjectInputs input)
    {
        if (!input.AutomaticPoolSelection) return input;
        var study = input.Study;
        long required = (long)study.Treatments.Length * study.Replicates * study.AnimalsPerPen;
        int count = required > 0 ? (int)Math.Min(study.Pool.Length, required) : study.Pool.Length;
        var selected = study.Pool.OrderByDescending(a => a.WeightGrams)
            .ThenBy(a => a.AnimalId, StringComparer.Ordinal).ThenBy(a => a.RecordId, StringComparer.Ordinal)
            .Take(count).Select(a => a.RecordId).ToHashSet(StringComparer.Ordinal);
        return input with {
            Study = study with { SelectedRecordIds = study.Pool.Where(a => selected.Contains(a.RecordId)).Select(a => a.RecordId).ToImmutableArray() },
            Selection = study.Pool.Select(a => new SelectionDecision(a.RecordId, selected.Contains(a.RecordId) ? SelectionChoice.Include : SelectionChoice.Exclude,
                selected.Contains(a.RecordId) ? "Automatic selection: heaviest animals first" : "EX: unused extra animal")).ToImmutableArray()
        };
    }
}
