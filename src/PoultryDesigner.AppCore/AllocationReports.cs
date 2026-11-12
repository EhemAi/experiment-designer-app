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
        var animalSex = s.Pool.ToDictionary(x => x.RecordId, x => x.Sex);
        var selectedIds = s.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
        var selectedSexCounts = s.Pool.Where(x => selectedIds.Contains(x.RecordId)).GroupBy(x => x.Sex).ToDictionary(g => g.Key, g => g.Count());
        var penSexCounts = a.Positions.GroupBy(x => (x.TreatmentIndex, x.Replicate)).ToDictionary(g => g.Key,
            g => g.GroupBy(x => animalSex[x.AnimalRecordId]).ToDictionary(x => x.Key, x => x.Count()));
        var positions = a.Positions.ToDictionary(x => x.AnimalRecordId, StringComparer.Ordinal);
        var physical = chosen.PhysicalPens.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var locations = a.Locations.ToDictionary(x => (x.TreatmentIndex, x.Replicate), x => physical[x.PhysicalPenId]);
        var animalsById = s.Pool.ToDictionary(x => x.RecordId, StringComparer.Ordinal);
        var unitWeights = a.Positions.GroupBy(x => (x.TreatmentIndex, x.Replicate)).ToDictionary(g => g.Key,
            g => g.Select(p => animalsById[p.AnimalRecordId].WeightGrams).ToArray());
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
        Summary("Allocation rule", s.BalancedBwCages
            ? "Within each existing BW/sex block, ranked BW subgroups contribute one animal to every eligible pen. Candidate assignments balance pen means and compare within-pen ranges. A final treatment permutation stays inside the block and sex composition. Male/female replicate order is retained."
            : s.CompactCages
            ? "Within each existing BW/sex block, nearby ranks form intact cages first. Complete cages are randomized to eligible treatments within that block. Male/female replicate order is retained. Block CV is descriptive; cages are not mixed to match means."
            : s.SexMode != SexAllocationMode.Ignore
            ? s.Design switch
            {
                Design.Crd => "Selected animals are shuffled within sex and assigned to equal-size units under the recorded sex composition rule. RCBD retry filtering is not used.",
                Design.Rcbd => "Selected animals are ranked by descending BW within sex with seeded tie ordering. Each replicate receives sex-specific BW sub-blocks, with randomized treatment assignments among the eligible sex slots.",
                _ => "Selected animals are ranked by descending BW within sex with seeded tie ordering. One randomized treatment starting order reverses on alternating BW sub-blocks, restricted to eligible sex slots. Allocation runs once."
            }
            : s.Design switch
            {
                Design.Rcbd => "Selected animals are sorted by descending BW with seeded tie ordering. Replicate BW blocks contain sub-blocks of treatment count; each sub-block independently shuffles treatment assignments.",
                Design.Crd => "Selected animals are shuffled and assigned to equal-size treatment units in one pass. RCBD retry filtering is not used.",
                _ => "Selected animals are sorted by descending BW with seeded tie ordering. One treatment starting order is randomized and reversed on alternating sub-blocks. Allocation runs once."
            }, summary);
        Summary("Sex allocation", s.SexMode.ToString(), summary);
        Summary("Sex allocation rule", SexAllocation.UsesSpecifiedCounts(s) ? SexAllocation.Describe(s) : s.SexMode switch
        {
            SexAllocationMode.Balance => "Heaviest overall selected group; male/female counts differ by at most one across pens, treatment totals and replicate totals. BW ranking is within each sex for blocked designs. No fixed sex ratio.",
            SexAllocationMode.Separate => "Heaviest overall selected group; whole all-male replicates first, then all-female replicates, across every treatment. BW ranking is within sex for blocked designs.",
            _ => "Sex is recorded and does not affect selection or allocation. Unknown is allowed."
        }, summary);
        foreach (var sex in Enum.GetValues<AnimalSex>()) Summary("Selected " + sex, selectedSexCounts.GetValueOrDefault(sex), summary);
        Summary("Experimental unit", unit, summary); Summary("Treatments", s.Treatments.Length, summary);
        Summary("Units per treatment", s.Replicates, summary); Summary("Animals per unit", s.AnimalsPerPen, summary);
        Summary("Source pool animals", s.Pool.Length, summary); Summary("Selected animals", s.SelectedRecordIds.Length, summary);
        Summary(input.AutomaticPoolSelection ? "Extra animals (EX)" : "Excluded animals", s.Pool.Length - s.SelectedRecordIds.Length, summary);
        if (input.AutomaticPoolSelection) Summary("Animal selection", SexAllocation.UsesSpecifiedCounts(s) ? "Heaviest animals within specified male/female counts. Unused animals are EX. Weight ties use Animal ID then immutable Record ID." : "Heaviest animals first. Unused animals are EX. Weight ties use Animal ID then immutable Record ID.", summary);
        Summary("Allocation seed (exact text)", a.Seed.ToString(CultureInfo.InvariantCulture), summary);
        Summary("Physical mapping seed (exact text)", a.LocationSeed?.ToString(CultureInfo.InvariantCulture) ?? "Unmapped", summary);
        Summary("Mapped units", a.Locations.Length, summary); Summary("Ordered swaps", a.Edits.Length, summary);
        Summary("Declared physical units", chosen.PhysicalPens.Length, summary);
        Summary("Overall selected mean BW", balance.OverallMeanGrams, summary);
        Summary("Range of unit mean BW", balance.PenMeanRangeGrams, summary);
        Summary("Block CV filtering", s.FilterBalance ? "Enabled" : "Disabled", summary);
        Summary("Block CV limit (%)", s.FilterBalance ? s.CvLimitPercent : null, summary);
        Summary("Maximum attempts per block", s.FilterBalance || s.BalancedBwCages ? s.MaximumAttemptsPerBlock : 1, summary);
        if (s.BalancedBwCages)
        {
            decimal target = s.FilterBalance ? s.CvLimitPercent : AllocationEngine.AutomaticBwCvTargetPercent;
            Summary("Automatic block CV target (%)", target, summary);
            int above = a.Blocks.Count(b => b.CvPercent > (double)target);
            Summary("Block BW balance", above == 0 ? "All current blocks meet the target." : $"{above} current blocks exceed the target. Review pen ranges and block CV; selected animals and sex counts were retained.", summary);
        }
        Summary("Input revision reason", chosen.Input.Reason, summary); Summary("Study notes", input.Notes, summary);
        Summary("Numeric precision", "Excel supports about 15 significant digits. Plain integer animal, treatment and housing IDs use numeric cells when exact; leading-zero, long and alphanumeric IDs retain text. Seeds and immutable record IDs remain text. Full precision and replay evidence remain in the project.", summary);
        Add("Summary", "Static results from the chosen immutable revision. Edits in this export do not change the project.", ["Field", "Value"], summary, [34, 95]);
        var movement = movementRows.OrderBy(x => positions.TryGetValue(x.RecordId, out var p) ? p.TreatmentIndex : int.MaxValue)
            .ThenBy(x => x.Replicate).ThenBy(x => x.Cohort, StringComparer.Ordinal).ThenBy(x => x.AnimalId, StringComparer.Ordinal).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToArray();
        Add("Movement", input.AutomaticPoolSelection ? "Complete source pool. Unused extra animals are EX and have blank destinations; Unmapped means an allocated unit has no physical location."
            : "Complete source pool, including explicit researcher exclusions. Blank destination means the animal is excluded; Unmapped means an allocated unit has no physical location.",
            ["Animal ID", "Cohort", "BW", "Old location", "Disposition", "Treatment ID", "Unit replicate", "Block", housingNumber, "New location", "Record ID", "Sex"], movement.Select(r =>
            {
                positions.TryGetValue(r.RecordId, out var p); PhysicalPen? location = p is null ? null : locations.GetValueOrDefault((p.TreatmentIndex, p.Replicate));
                return new object?[] { NumericIdentifier(r.AnimalId), r.Cohort, r.WeightGrams, NumericIdentifier(r.OldLocation),
                    input.AutomaticPoolSelection && r.Disposition == AnimalDisposition.ResearcherExcluded ? "EX" : r.Disposition.ToString(), NumericIdentifier(r.TreatmentId), r.Replicate, p?.Block,
                    NumericIdentifier(location?.Id), p is null ? "" : location?.Location ?? "Unmapped", r.RecordId, animalSex[r.RecordId].ToString() };
            }));
        Add("Unit BW", $"Descriptive BW for each experimental {unit}. SD is the sample SD of animal weights within the unit.",
            ["Treatment ID", "Treatment label", "Unit replicate", "Animals", "Total BW", "Mean BW", "Sample SD", housingNumber, "Scoped location", "Male", "Female", "Unknown", "Minimum BW", "Maximum BW"],
            a.Pens.OrderBy(x => x.TreatmentIndex).ThenBy(x => x.Replicate).Select(p =>
            {
                var l = locations.GetValueOrDefault((p.TreatmentIndex, p.Replicate)); var counts = penSexCounts[(p.TreatmentIndex, p.Replicate)]; var weights = unitWeights[(p.TreatmentIndex, p.Replicate)]; return new object?[] { NumericIdentifier(s.Treatments[p.TreatmentIndex].Id), s.Treatments[p.TreatmentIndex].DisplayLabel,
                    p.Replicate, p.Count, p.TotalWeightGrams, p.MeanWeightGrams, p.SampleSdGrams, NumericIdentifier(l?.Id), l?.Location ?? "Unmapped", counts.GetValueOrDefault(AnimalSex.Male), counts.GetValueOrDefault(AnimalSex.Female), counts.GetValueOrDefault(AnimalSex.Unknown), weights.Min(), weights.Max() };
            }));
        var anova = BodyWeightAnova.Calculate(s, a.Pens);
        tables[^1] = tables[^1] with
        {
            SidePanel = new("Treatment ANOVA", "Treatment comparison of experimental unit mean BW.",
            [new("Treatment ANOVA", 30), new("Value", 48)], [
                ["Model", anova.Model], ["Treatment p-value", anova.PValue], ["Balance target", "p > 0.6"], ["Target status", anova.TargetStatus],
                ["Experimental units", anova.Units], ["Treatment df", anova.TreatmentDf], ["Error df", anova.ErrorDf],
                ["F statistic", anova.F], ["Interpretation", anova.Note]
            ])
        };
        Add("Treatment balance", "Descriptive balance based on experimental unit means. No significance tests. SD is sample SD across unit means; CV is SD / mean x 100. Differences use the overall selected BW mean.",
            ["Treatment ID", "Label", "Unit count", "Mean BW", "Minimum unit mean", "Maximum unit mean", "SD of unit means", "CV of unit means (%)", "Difference", "Difference (%)"],
            balance.Treatments.Select(b => new object?[] { NumericIdentifier(b.TreatmentId), b.Label, b.PenCount, b.MeanWeightGrams, b.MinimumPenMeanGrams, b.MaximumPenMeanGrams,
                b.SampleSdOfPenMeansGrams, b.CvOfPenMeansPercent, b.DifferenceFromOverallGrams, b.DifferenceFromOverallPercent }));
        Add("Replicate BW", "Body weights of the allocated animals, in replicate order. Read sex counts alongside BW when replicate groups use different compositions.",
            ["Replicate", "Animals", "Mean BW", "Minimum BW", "Maximum BW", "Sample SD", "Male", "Female", "Unknown"],
            a.Positions.GroupBy(p => p.Replicate).OrderBy(g => g.Key).Select(g =>
            {
                var animals = g.Select(p => animalsById[p.AnimalRecordId]).ToArray();
                decimal mean = animals.Sum(x => x.WeightGrams) / animals.Length;
                double? sd = animals.Length < 2 ? null : Math.Sqrt(animals.Sum(x => Math.Pow((double)(x.WeightGrams - mean), 2)) / (animals.Length - 1));
                return new object?[] { g.Key, animals.Length, mean, animals.Min(x => x.WeightGrams), animals.Max(x => x.WeightGrams), sd,
                    animals.Count(x => x.Sex == AnimalSex.Male), animals.Count(x => x.Sex == AnimalSex.Female), animals.Count(x => x.Sex == AnimalSex.Unknown) };
            }), [12, 12, 18, 18, 18, 18, 10, 10, 12]);
        string Slot(Position p) => $"{s.Treatments[p.TreatmentIndex].Id} / {unit} {p.Replicate} / block {p.Block}";
        if (!a.Edits.IsEmpty) Add("Swap log", "Ordered applied swaps from this allocation revision. Original run seeds alone do not reproduce edited allocations.",
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

    public static string Text(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("G29", CultureInfo.InvariantCulture),
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
    public static string PrintedText(object? value) => value switch
    {
        decimal d => d.ToString("0.###", CultureInfo.InvariantCulture),
        double d => d.ToString("0.000", CultureInfo.InvariantCulture),
        _ => Text(value)
    };
    public static AllocationReport SortMovement(AllocationReport report, MovementOrder order)
    {
        if (!Enum.IsDefined(order)) throw new ArgumentException("Unknown movement order.");
        var movement = report.Tables.Single(x => x.Name == "Movement");
        var rows = order == MovementOrder.TreatmentUnit ? movement.Rows
            : movement.Rows.OrderBy(r => r[order == MovementOrder.OldLocation ? 3 : 9] is long ? 0 : 1)
                .ThenBy(r => r[order == MovementOrder.OldLocation ? 3 : 9] is long number ? number : 0)
                .ThenBy(r => Text(r[order == MovementOrder.OldLocation ? 3 : 9]), StringComparer.Ordinal)
                .ThenBy(r => Text(r[1]), StringComparer.Ordinal).ThenBy(r => Text(r[0]), StringComparer.Ordinal).ThenBy(r => Text(r[10]), StringComparer.Ordinal).ToImmutableArray();
        return report with { Tables = report.Tables.Replace(movement, movement with { Rows = rows, Description = movement.Description + " Sorted by " + order + "." }) };
    }

    // Printed tables retain all rows but omit the machine/audit columns carried in Excel.
    public static AllocationReport Printable(AllocationReport report)
    {
        if (!report.Tables.Any(t => t.Name == "Movement")) return report;
        ReportTable Select(string name, int[] columns, string? description = null)
        {
            var t = report.Tables.Single(x => x.Name == name);
            return t with
            {
                SidePanel = null,
                Description = description ?? t.Description,
                Columns = columns.Select(i => t.Columns[i]).ToImmutableArray(),
                Rows = t.Rows.Select(r => columns.Select(i => r[i]).ToImmutableArray()).ToImmutableArray()
            };
        }
        var printed = ImmutableArray.CreateBuilder<ReportTable>();
        printed.Add(Select("Summary", [0, 1], "Static chosen-revision results. Printed BW and statistics are rounded to three decimal places. Full values remain in Excel and the project."));
        printed.Add(Select("Movement", [0, 1, 11, 2, 3, 4, 5, 6, 8, 9]));
        printed.Add(Select("Unit BW", [0, 2, 7, 3, 5, 12, 13, 6]));
        printed.Add(Select("Treatment balance", [0, 1, 2, 3, 6, 7, 8]));
        printed.Add(report.Tables.Single(x => x.Name == "Unit BW").SidePanel!);
        printed.Add(Select("Replicate BW", [0, 1, 2, 3, 4, 5]));
        if (report.Tables.Any(t => t.Name == "Swap log")) printed.Add(Select("Swap log", [0, 1, 2, 3]));
        return report with { Tables = printed.ToImmutable() };
    }
}
