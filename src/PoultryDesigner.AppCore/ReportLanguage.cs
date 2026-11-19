using System.Collections.Immutable;

namespace PoultryDesigner.AppCore;

public static class ReportLanguage
{
    private static readonly HashSet<string> ResearcherSummaryValues = new(StringComparer.Ordinal)
    {
        "Project", "Project ID", "Chosen result ID", "Input revision ID", "Full input digest", "Engine version", "Allocation fingerprint",
        "Allocation seed (exact text)", "Input revision reason", "Study notes"
    };
    public static AllocationReport Display(AllocationReport report)
    {
        if (AppText.Language == AppLanguage.English) return report;
        return report with { Context = AppText.T(report.Context), Tables = report.Tables.Select(Table).ToImmutableArray() };
    }
    private static ReportTable Table(ReportTable table)
    {
        object? Cell(ImmutableArray<object?> row, int index)
        {
            object? value = row[index];
            if (value is not string text) return value;
            string column = table.Columns[index].Name;
            bool translate = column is "Sex" or "Disposition" or "Source" || table.Name == "Swap log" && index >= 4;
            if (table.Name is "Summary" or "Treatment ANOVA")
                translate = index == 0 || index == 1 && row[0] is string field && !ResearcherSummaryValues.Contains(field);
            return translate ? AppText.T(text) : value;
        }
        return table with
        {
            Name = AppText.T(table.Name),
            Description = AppText.T(table.Description),
            Columns = table.Columns.Select(c => c with { Name = AppText.T(c.Name) }).ToImmutableArray(),
            Rows = table.Rows.Select(row => row.Select((_, i) => Cell(row, i)).ToImmutableArray()).ToImmutableArray(),
            SidePanel = table.SidePanel is null ? null : Table(table.SidePanel)
        };
    }
}
