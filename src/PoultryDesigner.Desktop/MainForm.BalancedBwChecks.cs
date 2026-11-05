using System.Collections.Immutable;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    internal async Task ExerciseBalancedBwForSmoke(string root, Action<bool, string> check, Action<Form, string> render)
    {
        Workspace.New("BW subgroup upgrade", true);
        var pool = Enumerable.Range(1, 50).Select(i => new Animal("bw" + i, "bw" + i, 6 + i / 100m)).ToImmutableArray();
        var study = new Study(Design.Rcbd, pool, [], Enumerable.Range(1, 5).Select(i => new Treatment("T" + i, "Diet " + i)).ToImmutableArray(), 2, 5) { CompactCages = true };
        Workspace.Revise(ProjectInputs.FromStudy(study, "Earlier contiguous preview") with { AutomaticPoolSelection = true }, "Earlier grouping");
        var layout = HousingLayouts.Create(Workspace.Inputs, 101, 110, 55);
        Workspace.Revise(Workspace.Inputs with { PreparedLayout = layout }, "Prepare fixed locations");
        check((await Workspace.RunAsync(7, "Earlier contiguous result")).Status == RunStatus.Success, "Earlier contiguous allocation remains available");
        Workspace.Map(layout.Pens, layout.Seed, "Earlier prepared layout");
        var previous = Workspace.Document.ActiveResultId!;
        var locations = Workspace.Choose(previous).Allocation.Locations;
        selectionDirty = setupDirty = false; Reload(); seed.Text = "8";
        await RunAsync(); Application.DoEvents();
        check(Workspace.Inputs.Study.BalancedBwCages && !Workspace.Inputs.Study.CompactCages, "Generate upgrades the earlier preview to ranked BW subgroup balancing");
        var active = Workspace.Choose(Workspace.Document.ActiveResultId!);
        check(active.Allocation.Blocks.All(b => b.CvPercent <= 0.5) && active.Allocation.Locations.SequenceEqual(locations), "New GUI allocation balances BW and retains prepared treatment pen locations");
        check(Workspace.Choose(previous).Input.Inputs.Study.CompactCages, "The earlier allocation and its policy remain unchanged in history");

        Workspace.New("Unattainable BW target", true);
        var sparse = new[] { 10m, 1m, 1m, 1m }.Select((w, i) => new Animal("s" + i, "s" + i, w)).ToImmutableArray();
        Workspace.Revise(ProjectInputs.FromStudy(new Study(Design.Rcbd, sparse, [], [new("A", "A"), new("B", "B")], 2, 1) { BalancedBwCages = true }, "Sparse fixture") with { AutomaticPoolSelection = true }, "Sparse fixture");
        check((await Workspace.RunAsync(1, "Actual best attainable result")).Status == RunStatus.Success, "Automatic balancing retains actual measurements when the target is unattainable");
        selectionDirty = setupDirty = false; Reload(); SetStage(2);
        check(allocationFeedback.Text.Contains("BW CV target") && allocationFeedback.ForeColor == Theme.Warning
            && balanceNotice.Visible && balanceNotice.Text.Contains("blocks exceed") && balanceNoticePanel.BackColor == Theme.WarningBackground, "Unattainable automatic CV target has a visible warning, including when treatment ANOVA also needs review");
        render(this, "bw-target-warning");

        string example = Path.Combine(root, "analysis/balanced-bw-validation/synthetic-balanced-balanced.experimentproj");
        if (File.Exists(example))
        {
            await Workspace.OpenAsync(example, discardChanges: true); selectionDirty = setupDirty = false; Reload(); SetStage(2);
            var tabs = allocationViews.Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 2;
            render(this, "synthetic-balanced-balanced-pen-bw");
            tabs.SelectedIndex = 3; render(this, "synthetic-balanced-balanced-treatments");
        }
    }
}
