using System.Data;
using PoultryDesigner.Engine;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private void BuildSetup()
    {
        design.Items.AddRange(new object[] { new MethodOption(Design.Rcbd, "RCBD • BW blocks"), new MethodOption(Design.Crd, "Completely randomized"), new MethodOption(Design.Conventional, "Conventional serpentine") });
        var designFields = Components.Fields(("Allocation method", design), ("Experimental unit", unit), ("Replicates per treatment", replicates), ("Animals per experimental unit", perPen));
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, Height = 300 };
        stack.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int size in new[] { 240, 60 }) stack.RowStyles.Add(new(SizeType.Absolute, size));
        stack.Controls.Add(Components.Card("Experimental design", designFields), 0, 0);
        method.Dock = DockStyle.Fill; method.AutoSize = false; method.Font = Theme.Body(9); method.Padding = new(12, 6, 12, 6); stack.Controls.Add(method, 0, 1);
        var treatmentHost = new Panel { Dock = DockStyle.Fill };
        var up = Ui.Button("↑", () => MoveTreatment(-1)); var down = Ui.Button("↓", () => MoveTreatment(1));
        foreach (var button in new[] { up, down }) { button.MinimumSize = new(36, 32); button.Padding = new(0); }
        up.AccessibleName = "Move treatment up"; down.AccessibleName = "Move treatment down";
        Components.Tip(up, "Move selected treatment up"); Components.Tip(down, "Move selected treatment down");
        var reorder = Ui.Bar(up, down); reorder.Dock = DockStyle.None; reorder.WrapContents = false; reorder.Padding = Padding.Empty;
        var importTreatments = Ui.Button("Import…", () => Import(false)); importTreatments.AccessibleName = "Import treatments";
        Components.Tip(importTreatments, "Import a treatment table");
        var tools = Ui.Bar(Ui.Button("Add treatment", () => { treatments.EndEdit(); treatmentTable.Rows.Add("", ""); DraftSetup(); }),
            Ui.Button("Remove selected", () =>
            {
                var rows = treatments.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => (DataRowView)r.DataBoundItem!).ToArray();
                foreach (var row in rows) row.Row.Delete(); DraftSetup();
            }), importTreatments, reorder);
        treatmentHost.Controls.Add(treatments); treatmentHost.Controls.Add(tools);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new(SizeType.Percent, 49)); split.ColumnStyles.Add(new(SizeType.Percent, 51));
        var treatmentCard = Components.Card("Treatments", treatmentHost); treatmentHeading.Dock = DockStyle.Top; treatmentHeading.Height = 28; treatmentHeading.Font = Theme.Heading(12);
        treatmentCard.Controls.OfType<Label>().Single().Visible = false; treatmentCard.Controls.Add(treatmentHeading);
        var scrollingFields = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scrollingFields.Controls.Add(stack);
        split.Controls.Add(scrollingFields, 0, 0); split.Controls.Add(treatmentCard, 1, 0);
        var apply = Ui.Button("Apply settings", () => Guard(ApplySetup)); ((StyledButton)apply).Primary = true;
        var footer = Ui.Bar(apply); footer.Dock = DockStyle.Bottom;
        stages[0].Controls.Add(split); stages[0].Controls.Add(footer);
        foreach (var control in new[] { replicates, perPen }) control.ValueChanged += (_, _) => DraftSetup();
        design.SelectedIndexChanged += (_, _) => { DraftSetup(); Capabilities(); }; unit.SelectedIndexChanged += (_, _) => DraftSetup();
        treatments.CellValueChanged += (_, e) => { if (e.ColumnIndex >= 0 && treatments.Columns[e.ColumnIndex].DataPropertyName is "Treatment ID" or "Label") DraftSetup(); };
        treatments.RowsRemoved += (_, _) => DraftSetup();
        treatments.DataError += (_, e) => { e.ThrowException = false; pageAlert.Text = "Correct the treatment value before applying settings."; };
    }

}
