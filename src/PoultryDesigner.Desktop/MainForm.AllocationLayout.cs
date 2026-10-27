using System.Data;
using PoultryDesigner.Engine;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private FlowLayoutPanel allocationRevisionBar = null!, allocationExportBar = null!;
    private FlowLayoutPanel allocationRunBar = null!, allocationHousingBar = null!;
    private readonly Label balanceNotice = Components.Message("");
    private readonly TableLayoutPanel balanceNoticePanel = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new(4), Margin = new(4) };
    private Button repeatAllocation = null!;
    private void BuildAllocation()
    {
        seed.Text = "202612"; seed.Width = 160;
        generate = Ui.Button("Generate allocation", () => _ = RunAsync()); ((StyledButton)generate).Primary = true;
        var runBar = allocationRunBar = Ui.Bar(Ui.Label("Seed"), seed, generate);
        Components.Tip(seed, "This explicit unsigned seed reproduces the allocation using its saved inputs and versioned generator. It is never regenerated automatically.");
        revisions.Width = 360;
        activateRevision = Ui.Button("Use this result", () => Guard(() =>
        {
            if (chosen is null || chosen.ResultId == Workspace.Document.ActiveResultId) return;
            string? reason = Ui.Ask(this, "Activate allocation revision", "Activates this recorded revision; all previous results remain. Supply a reason.");
            if (reason is not null) { Workspace.Activate(chosen.ResultId, reason); Reload(); Notify("Revision activated"); }
        }));
        previewSwap = Ui.Button("Swap animals", Swap); mapUnits = Ui.Button("Assign housing", Map);
        var revisionBar = allocationRevisionBar = Ui.Bar(Ui.Label("Result"), revisions, activateRevision, previewSwap, mapUnits);
        resultLabel.Dock = DockStyle.Fill; resultLabel.AutoSize = false; resultLabel.Font = Theme.Body(9); resultLabel.Margin = new(0, 2, 0, 2);
        balanceNotice.AutoSize = true; balanceNotice.Dock = DockStyle.Fill; balanceNotice.MinimumSize = new(0, 44); balanceNotice.Padding = new(10, 6, 10, 6);
        balanceNotice.Font = Theme.Body(9, FontStyle.Bold);
        repeatAllocation = Ui.Button("Repeat with next seed", () => _ = RepeatAllocationAsync());
        repeatAllocation.Anchor = AnchorStyles.Right; balanceNoticePanel.ColumnStyles.Add(new(SizeType.Percent, 100));
        balanceNoticePanel.ColumnStyles.Add(new(SizeType.AutoSize)); balanceNoticePanel.Controls.Add(balanceNotice, 0, 0); balanceNoticePanel.Controls.Add(repeatAllocation, 1, 0);
        balanceNoticePanel.SizeChanged += (_, _) => balanceNotice.MaximumSize = new(Math.Max(1, balanceNoticePanel.ClientSize.Width - repeatAllocation.Width - 24), 0);
        var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 6, AutoSize = true };
        top.ColumnStyles.Add(new(SizeType.Percent, 100));
        top.RowStyles.Add(new(SizeType.Absolute, 88));
        top.RowStyles.Add(new(SizeType.AutoSize));
        top.RowStyles.Add(new(SizeType.AutoSize)); top.RowStyles.Add(new(SizeType.AutoSize));
        top.RowStyles.Add(new(SizeType.AutoSize));
        top.RowStyles.Add(new(SizeType.Absolute, 28));
        allocationHousingBar = BuildHousingControls();
        top.Controls.Add(allocationStats, 0, 0); top.Controls.Add(balanceNoticePanel, 0, 1); top.Controls.Add(runBar, 0, 2); top.Controls.Add(allocationHousingBar, 0, 3); top.Controls.Add(revisionBar, 0, 4); top.Controls.Add(resultLabel, 0, 5);
        var overview = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        allocationFeedback.Dock = DockStyle.Top; allocationFeedback.AutoSize = true; allocationFeedback.MinimumSize = new(0, 36);
        overview.SizeChanged += (_, _) => allocationFeedback.MaximumSize = new(Math.Max(1, overview.ClientSize.Width - 24), 0);
        plotCard = Components.Card("Mean starting weight by treatment", treatmentPlot); plotCard.Dock = DockStyle.Top; plotCard.Height = 210;
        overview.Controls.Add(plotCard); overview.Controls.Add(qualityStats); overview.Controls.Add(allocationFeedback);
        allocationViews = Ui.Tabs(("Overview", overview), ("Assignments", movement), ("Pen BW", pens), ("Treatment balance", balance), ("Diagnostics", blocks), ("Changes", edits), ("Housing", maps), ("Prepared layout", preparedGrid));
        allocationEmpty = Components.Empty("No allocation yet", "Complete valid Study settings and exact animal selection, then generate an allocation using an explicit seed.", Ui.Button("Review animal selection", () => SetStage(1)));
        stages[2].Controls.Add(allocationViews); stages[2].Controls.Add(allocationEmpty); stages[2].Controls.Add(top);
        exportReport = Ui.Button("Export / print", Reports);
        ((StyledButton)exportReport).Primary = true;
        var export = allocationExportBar = Ui.Bar(exportReport); export.Dock = DockStyle.Bottom; stages[2].Controls.Add(export);
        revisions.SelectedIndexChanged += (_, _) => { if (!loading && revisions.SelectedItem is RevisionOption option) { ShowChosen(Workspace.Choose(option.Id)); RefreshPresentation(); } };
        allocationViews.Controls.OfType<TabControl>().Single().SelectedIndexChanged += (_, _) => { if (!loading) RefreshPresentation(); };
        Components.Tip(revisions, "Active selects the current result. Historical results retain their own saved scientific inputs. Reviewing a revision does not activate it.");
    }

}
