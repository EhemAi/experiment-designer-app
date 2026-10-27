using PoultryDesigner.Engine;
using PoultryDesigner.AppCore;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private readonly Dictionary<string, string> animalSourceNames = [];
    private void ClearAnimalFilters() { loading = true; search.Clear(); foreach (var combo in new[] { cohortFilter, sourceFilter, locationFilter, decisionFilter }) combo.SelectedIndex = 0; loading = false; CaptureSelection(); FilterAnimals(); }
    private string[] HighlightedIds() => checkedAnimals.Concat(animals.SelectedRows.Cast<DataGridViewRow>().Select(x => Convert.ToString(x.Cells["Record ID"].Value)!)).Distinct(StringComparer.Ordinal).ToArray();
    private void RefreshHighlighted()
    {
        int count = HighlightedIds().Length;
        selectedRows.Text = $"{count:N0} animals highlighted";
        // SelectionChanged runs inside native grid processing. Keep its geometry
        // stable; showing/hiding this bar here reenters parent and grid layout.
        foreach (var button in decisionActions.Controls.OfType<Button>()) button.Enabled = count > 0;
    }
    private void MoveTreatment(int direction) => Guard(() =>
    {
        treatments.EndEdit(); if (treatments.SelectedRows.Count != 1 || treatments.SelectedRows[0].IsNewRow) throw new ArgumentException("Highlight one treatment to reorder.");
        int index = treatments.SelectedRows[0].Index, destination = index + direction; if (destination < 0 || destination >= treatmentTable.Rows.Count) return;
        object?[] values = treatmentTable.Rows[index].ItemArray; treatmentTable.Rows.RemoveAt(index); var row = treatmentTable.NewRow(); row.ItemArray = values; treatmentTable.Rows.InsertAt(row, destination);
        treatments.ClearSelection(); treatments.Rows[destination].Selected = true; DraftSetup();
    });
    private void PopulateFilters()
    {
        loading = true;
        try
        {
            void Fill(ComboBox combo, IEnumerable<string> values)
            {
                string previous = combo.SelectedItem?.ToString() ?? "All";
                combo.Items.Clear(); combo.Items.Add("All"); combo.Items.AddRange(values.Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Cast<object>().ToArray());
                int match = combo.Items.IndexOf(previous); combo.SelectedIndex = match >= 0 ? match : 0;
            }
            Fill(cohortFilter, Workspace.Inputs.Study.Pool.Select(a => a.Cohort));
            Fill(locationFilter, Workspace.Inputs.Study.Pool.Select(a => a.OldLocation));
            Fill(sourceFilter, Workspace.Document.Sources.Select(s => s.FileName));
            var imported = Workspace.Inputs.Imports.ToDictionary(x => x.Id, x => x.SourceId);
            var names = Workspace.Document.Sources.ToDictionary(x => x.Id, x => x.FileName);
            animalSourceNames.Clear();
            foreach (var record in Workspace.Inputs.Animals) animalSourceNames[record.RecordId] = record.Origin is null ? "" : names[imported[record.Origin.ApplicationId]];
        }
        finally { loading = false; }
    }
    private bool MatchesFilters(Animal animal)
    {
        bool Match(ComboBox combo, string value) => combo.SelectedIndex <= 0 || string.Equals(combo.SelectedItem?.ToString(), value, StringComparison.Ordinal);
        if (!Match(cohortFilter, animal.Cohort) || !Match(locationFilter, animal.OldLocation) || !Match(decisionFilter, selection[animal.RecordId].Choice.ToString())) return false;
        return Match(sourceFilter, animalSourceNames.GetValueOrDefault(animal.RecordId) ?? "");
    }
    private void ShowQuality(ChosenRevision snapshot)
    {
        var a = snapshot.Allocation; var s = snapshot.Input.Inputs.Study;
        var treatmentBalance = ResultViews.Balance(s, a);
        var anova = BodyWeightAnova.Calculate(s, a.Pens);
        ShowBalanceNotice(anova);
        string cvValue = a.Blocks.IsEmpty ? "Not applicable" : a.Blocks.Max(x => x.CvPercent).ToString("0.###") + " %";
        decimal range = treatmentBalance.Treatments.Max(x => x.MeanWeightGrams) - treatmentBalance.Treatments.Min(x => x.MeanWeightGrams);
        qualityStats.ShowValues(("Animals allocated", a.Positions.Length.ToString("N0")), ("Maximum block BW CV", cvValue), ("Treatment mean BW range", range.ToString("0.###") + " g"));
        Components.Tip(qualityStats, "Original block attempts: " + (a.Attempts.IsEmpty ? "Not applicable" : a.Attempts.Sum(x => (long)x.Attempts).ToString("N0")) + ". See Diagnostics for the complete records.");
        int exceeded = s.FilterBalance ? a.Blocks.Count(b => PresentationState.CvExceeds(b.CvPercent, s.CvLimitPercent)) : 0;
        allocationFeedback.ForeColor = exceeded > 0 ? Theme.Warning : Theme.Success;
        allocationFeedback.Text = exceeded > 0 ? $"Warning: {exceeded} current blocks exceed the configured {s.CvLimitPercent}% CV limit. Review current metrics and the audit log."
            : s.FilterBalance ? "Allocation complete. BW balance meets the CV limit." : "Allocation complete.";
        if (anova.PValue is <= .6) { allocationFeedback.Text = "Allocation recorded. Review the treatment BW balance above before export."; allocationFeedback.ForeColor = Theme.Warning; }
        if (snapshot.Input.Id != Workspace.Document.CurrentInputId) allocationFeedback.Text += " Uses previous study settings.";
        treatmentPlot.Values = treatmentBalance.Treatments.Select(x => (x.Label, x.MeanWeightGrams)).ToArray();
        plotCard.Height = Math.Max(170, 44 + treatmentPlot.Values.Length * 40);
    }

    private void ShowBalanceNotice(BodyWeightAnovaResult anova)
    {
        bool repeat = anova.PValue is <= 0.6;
        string value = anova.PValue is double p ? p.ToString(p is > 0 and < .0001 ? "0.###E+0" : "0.0000", System.Globalization.CultureInfo.InvariantCulture) : "Not estimable";
        balanceNotice.Text = anova.PValue is null ? "Treatment ANOVA · p not estimable. Review the unit body weights before export."
            : $"Treatment ANOVA · p = {value}\n" + (repeat ? "Below balance target. Repeat with a different seed before exporting." : "Meets your balance target (p > 0.6).");
        balanceNotice.ForeColor = anova.PValue is null || repeat ? Theme.Warning : Theme.Success;
        balanceNoticePanel.BackColor = anova.PValue is null || repeat ? Theme.WarningBackground : Theme.SuccessBackground;
        balanceNotice.BackColor = balanceNoticePanel.BackColor;
        repeatAllocation.Visible = repeat;
        Components.Tip(balanceNotice, anova.Model + ". " + anova.Note);
    }

    private async Task RepeatAllocationAsync()
    {
        if (!generate.Enabled || chosen is null) return;
        ulong previous = chosen.Allocation.Seed;
        seed.Text = (previous == ulong.MaxValue ? 0 : previous + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await RunAsync();
    }
}
