using System.Data;
using PoultryDesigner.Engine;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private void BuildHistory()
    {
        auditTimeline.AutoSize = false; auditTimeline.Height = 118; auditTimeline.Font = Theme.Body(9);
        var summary = Components.Card("Recent recorded actions", auditTimeline); summary.Dock = DockStyle.Top; summary.Height = 162;
        stages[3].Controls.Add(Ui.Tabs(("Runs and edits", history), ("Input revisions", inputHistory), ("Source files", sources)));
        stages[3].Controls.Add(summary);
        var tools = Ui.Bar(Ui.Button("Inspect selected source", InspectSource)); tools.Dock = DockStyle.Bottom; stages[3].Controls.Add(tools);
    }

}
