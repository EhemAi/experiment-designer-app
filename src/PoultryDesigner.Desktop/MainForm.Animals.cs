using System.Data;
using PoultryDesigner.Engine;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private readonly ContextMenuStrip animalDecisionMenu = new() { ShowImageMargin = false, BackColor = Theme.Pale, ForeColor = Theme.Text };
    private string? decisionMenuRecord;
    private void BuildAnimalColumns()
    {
        animals.AutoGenerateColumns = false; animals.ReadOnly = false;
        animals.AllowUserToAddRows = animals.AllowUserToDeleteRows = false;
        animals.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Mark",
            HeaderText = "Mark",
            Width = 90,
            MinimumWidth = 90,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        foreach (string name in new[] { "Record ID", "Animal ID", "Cohort", "BW (g)", "Old location", "Source row", "Decision", "Reason" })
        {
            DataGridViewColumn column = name == "Decision" ? new DataGridViewButtonColumn { FlatStyle = FlatStyle.Flat } : new DataGridViewTextBoxColumn();
            column.Name = column.HeaderText = column.DataPropertyName = name;
            column.ReadOnly = name != "Reason"; column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.MinimumWidth = name == "Reason" ? 170 : 75;
            if (name == "BW (g)") column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            animals.Columns.Add(column);
        }
        ApplyAnimalDetails();
        animalDecisionMenu.Font = Theme.Body(9);
        foreach (var (choice, caption) in new[] { (SelectionChoice.Include, "Included"), (SelectionChoice.Exclude, "Excluded"), (SelectionChoice.Pending, "Pending") })
        {
            var item = new ToolStripMenuItem(caption) { Tag = choice, ForeColor = Theme.Text };
            item.Click += (_, _) =>
            {
                if (decisionMenuRecord is null || !selection.ContainsKey(decisionMenuRecord)) return;
                foreach (DataGridViewRow row in animals.Rows)
                    if (Convert.ToString(row.Cells["Record ID"].Value) == decisionMenuRecord) { row.Cells["Decision"].Value = choice.ToString(); break; }
            };
            animalDecisionMenu.Items.Add(item);
        }
        Disposed += (_, _) => animalDecisionMenu.Dispose();
    }
    private void ShowAnimalDecisionMenu(int rowIndex)
    {
        if (loading || Workspace.Inputs.AutomaticPoolSelection || rowIndex < 0 || rowIndex >= animals.Rows.Count) return;
        CaptureSelection(); decisionMenuRecord = Convert.ToString(animals.Rows[rowIndex].Cells["Record ID"].Value);
        animalDecisionMenu.BackColor = Theme.Pale; animalDecisionMenu.ForeColor = Theme.Text;
        foreach (ToolStripItem item in animalDecisionMenu.Items) { item.BackColor = Theme.Pale; item.ForeColor = Theme.Text; }
        var bounds = animals.GetCellDisplayRectangle(animals.Columns["Decision"]!.Index, rowIndex, true);
        animalDecisionMenu.Show(animals, new Point(bounds.Left, bounds.Bottom));
    }
    private void FilterAnimals()
    {
        string query = search.Text.Trim(); filteredAnimals = Workspace.Inputs.Study.Pool.Where(x => MatchesFilters(x) && (query.Length == 0
            || x.AnimalId.Contains(query, StringComparison.OrdinalIgnoreCase) || x.RecordId.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Cohort.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        animalPage = 0; RenderAnimals();
    }
    private void AnimalPage(int change)
    {
        CaptureSelection(); int next = animalPage + change;
        if (next >= 0 && next * animalRowsPerPage < filteredAnimals.Length) { animalPage = next; RenderAnimals(); }
    }
    private void RenderAnimals()
    {
        bool previousLoading = loading; loading = true; animals.SuspendLayout();
        try
        {
            var table = new DataTable(); foreach (string column in new[] { "Record ID", "Animal ID", "Cohort", "BW (g)", "Old location", "Source row", "Decision", "Reason" }) table.Columns.Add(column);
            var input = Workspace.Inputs; var details = input.Animals.ToDictionary(x => x.RecordId); var apps = input.Imports.ToDictionary(x => x.Id);
            var sourceFiles = Workspace.Document.Sources.ToDictionary(x => x.Id);
            foreach (var a in filteredAnimals.Skip(animalPage * animalRowsPerPage).Take(animalRowsPerPage))
            {
                var decision = selection[a.RecordId]; var origin = details[a.RecordId].Origin;
                string sourceRow = origin is null ? "Manual record" : $"{sourceFiles[apps[origin.ApplicationId].SourceId].FileName} / {apps[origin.ApplicationId].Table}:{origin.Row}";
                table.Rows.Add(a.RecordId, a.AnimalId, a.Cohort, Ui.Number(a.WeightGrams), a.OldLocation, sourceRow, decision.Choice.ToString(), decision.Reason);
            }
            // Keep the columns and editing controls stable across pages. Removing bound columns
            // during a cell commit can reenter grid events and repeatedly recalculate fill widths.
            var previousTable = animals.DataSource as DataTable;
            animals.DataSource = null; animals.DataSource = table; previousTable?.Dispose();
            ApplyAnimalDetails();
            foreach (DataGridViewRow row in animals.Rows) row.Cells["Mark"].Value = checkedAnimals.Contains(Convert.ToString(row.Cells["Record ID"].Value)!);
            int start = animalPage * animalRowsPerPage, end = Math.Min(filteredAnimals.Length, start + animalRowsPerPage);
            pageCount.Text = filteredAnimals.Length == 0 ? "No matching records" : $"{start + 1}–{end} of {filteredAnimals.Length:N0} matching";
            animals.ClearSelection();
            animalPagination.UpdatePages(filteredAnimals.Length, animalPage, animalRowsPerPage,
                value => { CaptureSelection(); animalPage = value; RenderAnimals(); }, value => { CaptureSelection(); animalRowsPerPage = value; animalPage = 0; RenderAnimals(); });
        }
        finally { animals.ResumeLayout(); loading = previousLoading; }
        RefreshHighlighted(); UpdateSelectionCount();
    }
    private void UpdateSelectionCount()
    {
        var study = Workspace.Inputs.Study; long required = (long)study.Treatments.Length * study.Replicates * study.AnimalsPerPen;
        selectionCount.Text = $"{selection.Values.Count(x => x.Choice == SelectionChoice.Include):N0} / {required:N0} included • {selection.Values.Count(x => x.Choice == SelectionChoice.Pending):N0} pending";
        RefreshPresentation();
    }
    private void ChangeHighlighted(SelectionChoice choice) => Guard(() =>
    {
        CaptureSelection();
        var ids = HighlightedIds();
        if (ids.Length == 0) throw new ArgumentException("Highlight the exact records you want to include or exclude.");
        string reason = AuditReason(selectionReason, choice == SelectionChoice.Include ? "Include highlighted animals" : "Exclude highlighted animals", "");
        foreach (string id in ids) selection[id] = new(id, choice, reason);
        selectionDirty = true; selectionReason.Clear(); checkedAnimals.Clear(); RenderAnimals(); UpdateTitle();
    });
    private void ApplyAnimalDetails()
    {
        foreach (string name in new[] { "Record ID", "Source row", "Reason" }) if (animals.Columns.Contains(name)) animals.Columns[name]!.Visible = animalDetails.Checked;
    }

}
