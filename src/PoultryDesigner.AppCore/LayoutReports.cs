using System.Collections.Immutable;
using PoultryDesigner.Engine;
namespace PoultryDesigner.AppCore;

public static class LayoutReports
{
    public static AllocationReport ForExcel(AllocationReport report)
    {
        var layout = report.Tables.FirstOrDefault(t => t.Name == "Layout");
        return report.Context.StartsWith("Prepared layout", StringComparison.Ordinal) && layout is not null
            ? report with { Tables = [layout] } : report;
    }
    public static AllocationReport Create(string projectName, HousingPlan plan)
    {
        string number = plan.Unit == "cage" ? "Cage number" : "Pen number";
        var locations = plan.Locations.OrderBy(l => int.Parse(l.PhysicalPenId, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        ReportTable Table(string name, string description, string[] columns, IEnumerable<object?[]> rows) => new(name, description,
            columns.Select(c => new ReportColumn(c)).ToImmutableArray(), rows.Select(r => r.ToImmutableArray()).ToImmutableArray());
        return new(projectName, $"Prepared layout · {plan.Unit}s {plan.First}–{plan.Last} · No animals allocated", [
            Table("Layout", "Treatment locations prepared before animal allocation.", [number, "Replicate", "Treatment ID", "Treatment", "Animals per unit"],
                locations.Select(l => new object?[] { AllocationReports.NumericIdentifier(l.PhysicalPenId), l.Replicate, AllocationReports.NumericIdentifier(plan.Treatments[l.TreatmentIndex].Id), plan.Treatments[l.TreatmentIndex].DisplayLabel, plan.AnimalsPerUnit })),
            Table("Allotment sheets", "Write or import animal IDs, body weights and optional sex on allocation day. These fields are intentionally blank.",
                [number, "Replicate", "Treatment ID", "Treatment", "Animal slot", "Animal ID", "BW", "Sex"],
                locations.SelectMany(l => Enumerable.Range(1, plan.AnimalsPerUnit).Select(slot => new object?[] { AllocationReports.NumericIdentifier(l.PhysicalPenId), l.Replicate, AllocationReports.NumericIdentifier(plan.Treatments[l.TreatmentIndex].Id), plan.Treatments[l.TreatmentIndex].DisplayLabel, slot, "", "", "" }))),
            Table("Housing labels", "One label entry per pen or cage. Print this table before allocation day.", [number, "Replicate", "Treatment ID", "Treatment"],
                locations.Select(l => new object?[] { AllocationReports.NumericIdentifier(l.PhysicalPenId), l.Replicate, AllocationReports.NumericIdentifier(plan.Treatments[l.TreatmentIndex].Id), plan.Treatments[l.TreatmentIndex].DisplayLabel }))
        ], DateTimeOffset.UtcNow);
    }
}
