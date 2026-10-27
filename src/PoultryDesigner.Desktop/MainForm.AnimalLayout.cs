using System.Data;
using PoultryDesigner.Engine;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private FlowLayoutPanel manualSelectionActions = null!;
    private void BuildAnimals()
    {
        BuildAnimalColumns();
        search.PlaceholderText = "Animal ID / Record ID / cohort"; search.Width = 250;
        foreach (var combo in new[] { cohortFilter, locationFilter, sourceFilter }) combo.Items.Add("All");
        foreach (var combo in new[] { decisionFilter, cohortFilter, locationFilter, sourceFilter }) { combo.SelectedIndex = 0; combo.Width = 145; }
        var controls = Ui.Bar(Ui.Button("Import animals", () => Import(true)), Ui.Button("Enter animals", EnterAnimals), Ui.Label("Find"), search, decisionFilter, animalDetails);
        animalDetails.CheckedChanged += (_, _) => ApplyAnimalDetails();
        var filters = Ui.Bar(Ui.Label("Cohort"), cohortFilter, Ui.Label("Source"), sourceFilter, Ui.Label("Location"), locationFilter);
        decisionActions = Ui.Bar(selectedRows, Ui.Button("Include", () => ChangeHighlighted(SelectionChoice.Include)),
            Ui.Button("Exclude", () => ChangeHighlighted(SelectionChoice.Exclude)), Ui.Button("Clear highlight", () => { CaptureSelection(); checkedAnimals.Clear(); RenderAnimals(); }), Ui.Button("Edit record", EditAnimal));
        decisionActions.Visible = true;
        selectedRows.MinimumSize = new(185, 0);
        commitSelection = Ui.Button("Commit exact selection", () => Guard(() => SaveSelection(true))); ((StyledButton)commitSelection).Primary = true;
        usePool = Ui.Button("Use exact pool", () => Guard(() =>
        {
            FlushDrafts(); Workspace.SelectExactPool(AuditReason(selectionReason, "Use exact pool", "Reason for including this entire exact-sized pool")); selectionReason.Clear(); Reload(); Notify("Exact pool selected");
        }));
        var actions = manualSelectionActions = Ui.Bar(Ui.Button("Save selection draft", () => Guard(() => SaveSelection(false))), usePool, commitSelection); actions.Dock = DockStyle.Top;
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1 }; footer.Controls.Add(actions); footer.Controls.Add(animalPagination);
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        top.Controls.Add(animalStats); top.Controls.Add(controls); top.Controls.Add(filters); top.Controls.Add(decisionActions);
        animalEmpty = Components.Empty("No animals entered", "Enter IDs and weights, or import CSV, TSV, XLSX or XLSM observations.",
            Ui.Button("Import animals", () => Import(true)), Ui.Button("Enter animals", EnterAnimals), Ui.Button("View import guidance", OpenGuide));
        animalNoResults = Components.Empty("No animals match these filters", "Clear the filters to return to the complete source pool. No selection decisions have been changed.", Ui.Button("Clear filters", ClearAnimalFilters));
        stages[1].Controls.Add(animals); stages[1].Controls.Add(animalEmpty); stages[1].Controls.Add(animalNoResults); stages[1].Controls.Add(top); stages[1].Controls.Add(footer);
        search.TextChanged += (_, _) => { if (!loading) { CaptureSelection(); FilterAnimals(); } };
        foreach (var combo in new[] { decisionFilter, cohortFilter, locationFilter, sourceFilter }) combo.SelectedIndexChanged += (_, _) => { if (!loading) { CaptureSelection(); FilterAnimals(); } };
        animals.SelectionChanged += (_, _) => { if (!loading) RefreshHighlighted(); };
        animals.CellContentClick += (_, e) => { if (e.ColumnIndex >= 0 && animals.Columns[e.ColumnIndex].Name == "Decision") ShowAnimalDecisionMenu(e.RowIndex); };
        animals.CellValueChanged += (_, e) =>
        {
            if (!loading && e.RowIndex >= 0 && e.RowIndex < animals.Rows.Count && e.ColumnIndex >= 0 && e.ColumnIndex < animals.Columns.Count)
            {
                if (animals.Columns[e.ColumnIndex].Name == "Mark")
                {
                    var row = animals.Rows[e.RowIndex]; string id = Convert.ToString(row.Cells["Record ID"].Value)!;
                    if (Equals(row.Cells["Mark"].Value, true)) checkedAnimals.Add(id); else checkedAnimals.Remove(id); RefreshHighlighted(); return;
                }
                if (animals.Columns[e.ColumnIndex].Name == "Decision")
                {
                    string action = "Set animal decision to " + Convert.ToString(animals.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
                    animals.Rows[e.RowIndex].Cells["Reason"].Value = action;
                }
                string recordId = Convert.ToString(animals.Rows[e.RowIndex].Cells["Record ID"].Value) ?? "";
                if (selection.ContainsKey(recordId) && Enum.TryParse<SelectionChoice>(Convert.ToString(animals.Rows[e.RowIndex].Cells["Decision"].Value), out var choice))
                    selection[recordId] = new(recordId, choice, Convert.ToString(animals.Rows[e.RowIndex].Cells["Reason"].Value) ?? "");
                selectionDirty = true; UpdateSelectionCount(); UpdateTitle();
            }
        };
        animals.CurrentCellDirtyStateChanged += (_, _) => { if (!loading && !capturingSelection && animals.IsCurrentCellDirty) animals.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        animals.DataError += (_, e) => { e.ThrowException = false; pageAlert.Text = "Choose Include, Exclude or Pending and enter a reason."; };
        animals.CellFormatting += (_, e) =>
        {
            if (loading || e.RowIndex < 0 || e.RowIndex >= animals.Rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= animals.Columns.Count || animals.Columns[e.ColumnIndex].Name != "Decision") return;
            string value = Convert.ToString(animals.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";
            bool automatic = Workspace.Inputs.AutomaticPoolSelection;
            e.Value = automatic ? value == "Include" ? "Selected" : "EX" : (value == "Include" ? "Included" : value == "Exclude" ? "Excluded" : "Pending") + " ▾"; e.FormattingApplied = true;
            e.CellStyle!.ForeColor = value == "Pending" ? Theme.Warning : value == "Include" ? Theme.Success : Theme.Muted;
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
        };
        // Avoid native tracking tooltips on the scrolling surface. The registered
        // whole-grid tooltip can block in ToolTip.SetTool after a hover, stopping
        // the UI message pump. Details remain available in the explicit columns.
        animals.AccessibleDescription = "Animal IDs, body weights and selection status. Show details for record IDs, source observations and decision reasons.";
    }

}
