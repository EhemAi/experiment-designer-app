using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private readonly NumericUpDown firstHousing = new() { Minimum = 1, Maximum = 1_000_000, Value = 1, Width = 110 };
    private readonly NumericUpDown lastHousing = new() { Minimum = 1, Maximum = 1_000_000, Value = 1, Width = 110 };
    private readonly Label housingLabel = Ui.Label("Pen range"), housingCount = Ui.Label("");
    private bool updatingHousing, customHousingEnd;
    private Button prepareLayout = null!, printLayout = null!;
    private readonly PagedGrid preparedGrid = new();
    private HousingPlan? displayedPlan;
    private void PrepareLayout() => Guard(() =>
    {
        FlushDrafts();
        var plan = HousingLayouts.Create(Workspace.Inputs, (int)firstHousing.Value, (int)lastHousing.Value, Ui.Seed(seed.Text));
        if (Workspace.Inputs.PreparedLayout is not null && MessageBox.Show(this,
            "Replace the saved layout? Previously printed sheets and labels may no longer match. Keep a copy of the old project if needed.",
            "Replace prepared layout", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Workspace.Revise(Workspace.Inputs with { PreparedLayout = plan }, "Prepare treatment and housing layout");
        Reload(); SetStage(2); var tabs = allocationViews.Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = tabs.TabPages.Count - 1; Notify("Layout prepared. Save the project and print your sheets.");
    });
    private void PrintLayout() => Guard(() =>
    {
        var plan = Workspace.Inputs.PreparedLayout ?? throw new ArgumentException("Prepare a layout first.");
        if (setupDirty || !HousingLayouts.Matches(plan, Workspace.Inputs)) throw new ArgumentException("Study settings have changed. Prepare a new layout before printing.");
        using var dialog = new ReportDialog(LayoutReports.Create(Workspace.Document.Name, plan),
            Workspace.Document.Sources.Select(s => s.OriginalPath).Append(Workspace.FilePath ?? "").ToArray());
        dialog.ShowDialog(this);
    });
    private FlowLayoutPanel BuildHousingControls()
    {
        firstHousing.AccessibleName = "First pen or cage number"; lastHousing.AccessibleName = "Last pen or cage number";
        firstHousing.ValueChanged += (_, _) => { if (updatingHousing) return; customHousingEnd = false; RefreshPresentation(); };
        lastHousing.ValueChanged += (_, _) => { if (updatingHousing) return; customHousingEnd = true; RefreshPresentation(); };
        prepareLayout = Ui.Button("Prepare layout", PrepareLayout); printLayout = Ui.Button("Print layout / labels", PrintLayout);
        var row = Ui.Bar(housingLabel, firstHousing, Ui.Label("to"), lastHousing, housingCount, prepareLayout, printLayout);
        Components.Tip(row, "Each replicate uses a consecutive group of treatment-count pens or cages. Treatment units are randomly assigned inside that group, using an independent stream of the entered seed.");
        return row;
    }
    private bool RefreshHousingControls()
    {
        var s = Workspace.Inputs.Study; long required = (long)s.Treatments.Length * s.Replicates;
        var plan = Workspace.Inputs.PreparedLayout;
        updatingHousing = true;
        try
        {
            if (!ReferenceEquals(displayedPlan, plan))
            {
                if (plan is not null) { firstHousing.Value = Math.Clamp(plan.First, 1, firstHousing.Maximum); lastHousing.Value = Math.Clamp(plan.Last, 1, lastHousing.Maximum); customHousingEnd = true; }
                else { firstHousing.Value = 1; customHousingEnd = false; }
                displayedPlan = plan;
            }
            if (!customHousingEnd)
                lastHousing.Value = Math.Clamp(firstHousing.Value + Math.Max(1, required) - 1, lastHousing.Minimum, lastHousing.Maximum);
        }
        finally { updatingHousing = false; }
        string unit = Workspace.Inputs.ExperimentalUnit;
        housingLabel.Text = unit == "cage" ? "Cage range" : "Pen range";
        long available = (long)(lastHousing.Value - firstHousing.Value + 1);
        bool valid = required > 0 && available == required;
        housingCount.Text = valid ? $"{required:N0} {unit}s · {s.Treatments.Length} per replicate" : $"{required:N0} {unit}s required";
        housingCount.ForeColor = valid ? Theme.Muted : Theme.Warning;
        bool matches = plan is null || HousingLayouts.Matches(plan, Workspace.Inputs) && plan.First == firstHousing.Value && plan.Last == lastHousing.Value;
        prepareLayout.Enabled = valid && !setupDirty && !inOperation;
        printLayout.Enabled = plan is not null && HousingLayouts.Matches(plan, Workspace.Inputs) && !setupDirty && !inOperation;
        if (!matches) { housingCount.Text = "Prepare a new layout for these settings"; housingCount.ForeColor = Theme.Warning; }
        if (plan is not null)
        {
            var rows = plan.Locations.OrderBy(x => int.Parse(x.PhysicalPenId)).ToArray();
            preparedGrid.SetRows([plan.Unit == "cage" ? "Cage number" : "Pen number", "Replicate", "Treatment", "Animals / unit"], rows.Length, i =>
            {
                var l = rows[i]; return [l.PhysicalPenId, l.Replicate, plan.Treatments[l.TreatmentIndex].Label, plan.AnimalsPerUnit];
            });
        }
        else preparedGrid.SetRows([], 0, _ => []);
        return valid && matches;
    }
}
