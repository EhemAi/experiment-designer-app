using System.Collections.Immutable;
using System.Globalization;
using PoultryDesigner.Engine;

namespace PoultryDesigner.AppCore;

public sealed record ReportColumn(string Name, double Width = 20);
public sealed record ReportTable(string Name, string Description, ImmutableArray<ReportColumn> Columns,
    ImmutableArray<ImmutableArray<object?>> Rows)
{
    public ReportTable? SidePanel { get; init; }
}
public sealed record AllocationReport(string Title, string Context, ImmutableArray<ReportTable> Tables, DateTimeOffset GeneratedUtc);
public enum MovementOrder { TreatmentUnit, OldLocation, NewLocation }

// A report is a value snapshot. No live workspace lookups, randomness or mutations occur during export.
public static class AllocationReports
{
    public static AllocationReport Create(ChosenRevision chosen, bool historical, bool earlierInputs, DateTimeOffset? generatedUtc = null)
    {
        var input = chosen.Input.Inputs; var s = input.Study; var a = chosen.Allocation;
        var generated = generatedUtc ?? DateTimeOffset.UtcNow;
        var movementRows = ResultViews.Movement(s, a);
        var positions = a.Positions.ToDictionary(x => x.AnimalRecordId, StringComparer.Ordinal);
        var physical = chosen.PhysicalPens.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var locations = a.Locations.ToDictionary(x => (x.TreatmentIndex, x.Replicate), x => physical[x.PhysicalPenId]);
        var assignments = a.Locations.ToDictionary(x => x.PhysicalPenId, StringComparer.Ordinal);
        var balance = ResultViews.Balance(s, a);
        string unit = input.ExperimentalUnit;
        string housingNumber = unit == "cage" ? "Cage number" : "Pen number";
        string context = $"{(s.Demonstration ? "DEMONSTRATION DATA. " : "")}{(historical ? "Historical" : "Active")} chosen result. "
            + (earlierInputs ? "Based on earlier inputs. " : "") + $"Input revision {chosen.Input.Number}; allocation revision {a.Revision}; seed {a.Seed}.";
        var tables = ImmutableArray.CreateBuilder<ReportTable>();
        void Add(string name, string description, string[] headers, IEnumerable<object?[]> rows, double[]? widths = null) => tables.Add(new(name, description,
            headers.Select((x, i) => new ReportColumn(x, widths?[i] ?? 20)).ToImmutableArray(), rows.Select(x => x.ToImmutableArray()).ToImmutableArray()));
        void Summary(string field, object? value, List<object?[]> rows) => rows.Add([field, value]);
        var summary = new List<object?[]>();
        Summary("Project", chosen.ProjectName, summary); Summary("Result status", context, summary);
        Summary("Export generated UTC", generated.ToUniversalTime().ToString("O"), summary);
        Summary("Input revision created UTC", chosen.Input.CreatedUtc.ToString("O"), summary);
        Summary("Project ID", chosen.ProjectId, summary); Summary("Chosen result ID", chosen.ResultId, summary);
        Summary("Input revision ID", chosen.Input.Id, summary); Summary("Full input digest", chosen.Input.FullInputDigest, summary);
        Summary("Engine version", a.EngineVersion, summary); Summary("Allocation fingerprint", a.InputFingerprint, summary);
        Summary("Generator", "Versioned SplitMix64 with independent allocation and physical-map streams. This is not Excel RAND; seed-for-seed equivalence with the macro is not claimed.", summary);
        Summary("Design", s.Design switch { Design.Rcbd => "RCBD - BW blocks", Design.Crd => "Completely randomized", _ => "Conventional serpentine" }, summary);
        Summary("Allocation rule", s.Design switch {
            Design.Rcbd => "Selected animals are sorted by descending BW with seeded tie ordering. Replicate BW blocks contain sub-blocks of treatment count; each sub-block independently shuffles treatment assignments.",
            Design.Crd => "Selected animals are shuffled and assigned to equal-size treatment units in one pass. RCBD retry filtering is not used.",
            _ => "Selected animals are sorted by descending BW with seeded tie ordering. One treatment starting order is randomized and reversed on alternating sub-blocks. Allocation runs once." }, summary);
        Summary("Experimental unit", unit, summary); Summary("Treatments", s.Treatments.Length, summary);
        Summary("Units per treatment", s.Replicates, summary); Summary("Animals per unit", s.AnimalsPerPen, summary);
        Summary("Source pool animals", s.Pool.Length, summary); Summary("Selected animals", s.SelectedRecordIds.Length, summary);
        Summary(input.AutomaticPoolSelection ? "Extra animals (EX)" : "Excluded animals", s.Pool.Length - s.SelectedRecordIds.Length, summary);
        if (input.AutomaticPoolSelection) Summary("Animal selection", "Heaviest animals first. Unused animals are EX. Weight ties use Animal ID then immutable Record ID.", summary);
        Summary("Allocation seed (exact text)", a.Seed.ToString(CultureInfo.InvariantCulture), summary);
        Summary("Physical mapping seed (exact text)", a.LocationSeed?.ToString(CultureInfo.InvariantCulture) ?? "Unmapped", summary);
        Summary("Mapped units", a.Locations.Length, summary); Summary("Ordered swaps", a.Edits.Length, summary);
        Summary("Declared physical units", chosen.PhysicalPens.Length, summary);
        Summary("Overall selected mean BW (g)", balance.OverallMeanGrams, summary);
        Summary("Range of unit mean BW (g)", balance.PenMeanRangeGrams, summary);
        Summary("Block CV filtering", s.FilterBalance ? "Enabled" : "Disabled", summary);
        Summary("Block CV limit (%)", s.FilterBalance ? s.CvLimitPercent : null, summary);
        Summary("Maximum attempts per block", s.FilterBalance ? s.MaximumAttemptsPerBlock : 1, summary);
        Summary("Input revision reason", chosen.Input.Reason, summary); Summary("Study notes", input.Notes, summary);
        Summary("Numeric precision", "Excel supports about 15 significant digits. Plain integer animal, treatment and housing IDs use numeric cells when exact; leading-zero, long and alphanumeric IDs retain text. Seeds and immutable record IDs remain text. Full precision and replay evidence remain in the project.", summary);
        Add("Summary", "Static results from the chosen immutable revision. Edits in this export do not change the project.", ["Field", "Value"], summary, [34, 95]);
        var movement = movementRows.OrderBy(x => positions.TryGetValue(x.RecordId, out var p) ? p.TreatmentIndex : int.MaxValue)
            .ThenBy(x => x.Replicate).ThenBy(x => x.Cohort, StringComparer.Ordinal).ThenBy(x => x.AnimalId, StringComparer.Ordinal).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToArray();
        Add("Movement", input.AutomaticPoolSelection ? "Complete source pool. Unused extra animals are EX and have blank destinations; Unmapped means an allocated unit has no physical location."
            : "Complete source pool, including explicit researcher exclusions. Blank destination means the animal is excluded; Unmapped means an allocated unit has no physical location.",
            ["Animal ID", "Cohort", "BW (g)", "Old location", "Disposition", "Treatment ID", "Unit replicate", "Block", housingNumber, "New location", "Record ID"], movement.Select(r => {
                positions.TryGetValue(r.RecordId, out var p); PhysicalPen? location = p is null ? null : locations.GetValueOrDefault((p.TreatmentIndex, p.Replicate));
                return new object?[] { NumericIdentifier(r.AnimalId), r.Cohort, r.WeightGrams, r.OldLocation,
                    input.AutomaticPoolSelection && r.Disposition == AnimalDisposition.ResearcherExcluded ? "EX" : r.Disposition.ToString(), NumericIdentifier(r.TreatmentId), r.Replicate, p?.Block,
                    NumericIdentifier(location?.Id), p is null ? "" : location?.Location ?? "Unmapped", r.RecordId }; }));
        Add("Unit BW", $"Descriptive BW for each experimental {unit}. SD is the sample SD of animal weights within the unit.",
            ["Treatment ID", "Treatment label", "Unit replicate", "Animals", "Total BW (g)", "Mean BW (g)", "Sample SD (g)", housingNumber, "Scoped location"],
            a.Pens.OrderBy(x => x.TreatmentIndex).ThenBy(x => x.Replicate).Select(p => {
                var l = locations.GetValueOrDefault((p.TreatmentIndex, p.Replicate)); return new object?[] { NumericIdentifier(s.Treatments[p.TreatmentIndex].Id), s.Treatments[p.TreatmentIndex].Label,
                    p.Replicate, p.Count, p.TotalWeightGrams, p.MeanWeightGrams, p.SampleSdGrams, NumericIdentifier(l?.Id), l?.Location ?? "Unmapped" }; }));
        var anova = BodyWeightAnova.Calculate(s, a.Pens);
        tables[^1] = tables[^1] with { SidePanel = new("Treatment ANOVA", "Treatment comparison of experimental unit mean BW (g).",
            [new("Treatment ANOVA", 30), new("Value", 48)], [
                ["Model", anova.Model], ["Treatment p-value", anova.PValue], ["Balance target", "p > 0.6"], ["Target status", anova.TargetStatus],
                ["Experimental units", anova.Units], ["Treatment df", anova.TreatmentDf], ["Error df", anova.ErrorDf],
                ["F statistic", anova.F], ["Interpretation", anova.Note]
            ]) };
        Add("Treatment balance", "Descriptive balance based on experimental unit means. No significance tests. SD is sample SD across unit means; CV is SD / mean x 100. Differences use the overall selected BW mean.",
            ["Treatment ID", "Label", "Unit count", "Mean BW (g)", "Minimum unit mean (g)", "Maximum unit mean (g)", "SD of unit means (g)", "CV of unit means (%)", "Difference (g)", "Difference (%)"],
            balance.Treatments.Select(b => new object?[] { NumericIdentifier(b.TreatmentId), b.Label, b.PenCount, b.MeanWeightGrams, b.MinimumPenMeanGrams, b.MaximumPenMeanGrams,
                b.SampleSdOfPenMeansGrams, b.CvOfPenMeansPercent, b.DifferenceFromOverallGrams, b.DifferenceFromOverallPercent }));
        Add("Physical map", "Complete declared physical pool and constraints. Mapping requires exactly one physical unit per allocated unit. Empty table means physical mapping has not been applied.",
            ["Treatment ID", "Treatment label", "Unit replicate", housingNumber, "Scoped location", "Capacity", "Eligible replicate", "Mapping seed"],
            chosen.PhysicalPens.OrderBy(x => x.Location, StringComparer.Ordinal).Select(p => { var l = assignments.GetValueOrDefault(p.Id);
                return new object?[] { l is null ? "" : NumericIdentifier(s.Treatments[l.TreatmentIndex].Id), l is null ? "Unassigned physical unit" : s.Treatments[l.TreatmentIndex].Label, l?.Replicate, NumericIdentifier(p.Id), p.Location, p.Capacity,
                    p.EligibleBlock, a.LocationSeed?.ToString(CultureInfo.InvariantCulture) }; }));
        string Slot(Position p) => $"{s.Treatments[p.TreatmentIndex].Id} / {unit} {p.Replicate} / block {p.Block}";
        Add("Swap log", "Ordered applied swaps from this allocation revision. Original run seeds alone do not reproduce edited allocations.",
            ["Order", "First record ID", "Second record ID", "Reason", "First before", "First after", "Second before", "Second after"],
            a.Edits.Select((e, i) => new object?[] { i + 1, e.FirstRecordId, e.SecondRecordId, e.Reason, Slot(e.BeforeFirst), Slot(e.AfterFirst), Slot(e.BeforeSecond), Slot(e.AfterSecond) }));
        foreach (var t in tables.Skip(1).ToArray()) summary.Add(["About " + t.Name, t.Description]);
        tables[0] = tables[0] with { Rows = summary.Select(x => x.ToImmutableArray()).ToImmutableArray() };
        return new(chosen.ProjectName, context, tables.ToImmutable(), generated);
    }

    // Excel numbers are exact only up to 15 significant digits. Preserve leading-zero,
    // long and alphanumeric identifiers as text rather than silently changing identity.
    internal static object? NumericIdentifier(string? value) => value is not null && value.Length <= 15
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long number)
        && number.ToString(CultureInfo.InvariantCulture) == value ? number : value;

    public static string Text(object? value) => value switch { null => "", decimal d => d.ToString("G29", CultureInfo.InvariantCulture),
        double d => d.ToString("G17", CultureInfo.InvariantCulture), _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
    public static string PrintedText(object? value) => value switch { decimal d => d.ToString("0.###", CultureInfo.InvariantCulture),
        double d => d.ToString("0.000", CultureInfo.InvariantCulture), _ => Text(value) };
    public static AllocationReport SortMovement(AllocationReport report, MovementOrder order)
    {
        if (!Enum.IsDefined(order)) throw new ArgumentException("Unknown movement order.");
        var movement = report.Tables.Single(x => x.Name == "Movement");
        var rows = order == MovementOrder.TreatmentUnit ? movement.Rows
            : movement.Rows.OrderBy(r => Text(r[order == MovementOrder.OldLocation ? 3 : 9]), StringComparer.Ordinal)
                .ThenBy(r => Text(r[1]), StringComparer.Ordinal).ThenBy(r => Text(r[0]), StringComparer.Ordinal).ThenBy(r => Text(r[10]), StringComparer.Ordinal).ToImmutableArray();
        return report with { Tables = report.Tables.Replace(movement, movement with { Rows = rows, Description = movement.Description + " Sorted by " + order + "." }) };
    }

    // Printed tables retain all rows but omit the machine/audit columns carried in Excel.
    public static AllocationReport Printable(AllocationReport report)
    {
        if (!report.Tables.Any(t => t.Name == "Movement")) return report;
        ReportTable Select(string name, int[] columns, string? description = null) { var t = report.Tables.Single(x => x.Name == name);
            return t with { SidePanel = null, Description = description ?? t.Description, Columns = columns.Select(i => t.Columns[i]).ToImmutableArray(),
                Rows = t.Rows.Select(r => columns.Select(i => r[i]).ToImmutableArray()).ToImmutableArray() }; }
        return report with { Tables = [Select("Summary", [0, 1], "Static chosen-revision results. Printed BW and statistics are rounded to three decimal places. Full values remain in Excel and the project."), Select("Movement", [0, 1, 2, 3, 4, 5, 6, 8, 9]),
            Select("Unit BW", [0, 1, 2, 3, 5, 6, 8]), Select("Treatment balance", [0, 1, 2, 3, 6, 7, 8]),
            report.Tables.Single(x => x.Name == "Unit BW").SidePanel!, Select("Physical map", [0, 1, 2, 3, 4, 5, 6]),
            Select("Swap log", [0, 1, 2, 3])] };
    }
}
