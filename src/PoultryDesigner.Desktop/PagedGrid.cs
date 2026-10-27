using System.Data;
using System.Globalization;

namespace PoultryDesigner.Desktop;

internal sealed class PagedGrid : UserControl
{
    internal readonly DataGridView Grid = Ui.Grid();
    internal const int PageSize = 500;
    private int page, total, rowsPerPage = PageSize;
    private string[] headers = [];
    private Func<int, object?[]> row = _ => [];
    private readonly FlowLayoutPanel footer = Ui.Bar();
    private readonly Label count = Ui.Label("");
    private readonly Panel empty = Components.Empty("No entries", "This view will show recorded data when they are available.");
    private readonly ComboBox size = Ui.Combo(["100", "250", "500"]);
    internal int[] SelectedIndices => Grid.SelectedRows.Cast<DataGridViewRow>().Where(x => x.Index >= 0).Select(x => page * rowsPerPage + x.Index).ToArray();
    internal PagedGrid()
    {
        Dock = DockStyle.Fill; size.Width = 80; size.SelectedIndex = 2;
        size.SelectedIndexChanged += (_, _) => { rowsPerPage = int.Parse(size.Text, CultureInfo.InvariantCulture); page = 0; Render(); };
        footer.Dock = DockStyle.Bottom; Controls.Add(Grid); Controls.Add(empty); Controls.Add(footer);
    }
    internal void SetRows(string[] names, int length, Func<int, object?[]> get)
    { bool same = headers.SequenceEqual(names); headers = names; total = length; row = get; page = same ? Math.Min(page, Math.Max(0, (total - 1) / rowsPerPage)) : 0; Render(); }
    private void Render()
    {
        var table = new DataTable(); foreach (string header in headers) table.Columns.Add(header, typeof(string));
        int start = page * rowsPerPage, end = Math.Min(total, start + rowsPerPage);
        for (int i = start; i < end; i++) table.Rows.Add(row(i).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "").Cast<object>().ToArray());
        Grid.DataSource = table;
        var sizing = Grid.AutoSizeColumnsMode; Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        foreach (DataGridViewColumn column in Grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.MinimumWidth = Math.Clamp(TextRenderer.MeasureText(column.HeaderText, Grid.ColumnHeadersDefaultCellStyle.Font ?? Font).Width + 40, 110, 250);
            if (new[] { "BW", "CV", "Mean", "Animals", "Attempts", "Capacity", "Total", "Difference", "Sample SD" }.Any(x => column.HeaderText.Contains(x, StringComparison.OrdinalIgnoreCase)))
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        Grid.AutoSizeColumnsMode = sizing;
        Grid.Visible = total > 0; empty.Visible = total == 0; footer.Visible = total > 0;
        footer.SuspendLayout(); foreach (Control control in footer.Controls.Cast<Control>().Where(c => c != count && c != size).ToArray()) control.Dispose(); footer.Controls.Clear();
        int pages = Math.Max(1, (total + rowsPerPage - 1) / rowsPerPage);
        var previous = Ui.Button("Previous", () => { page--; Render(); }); previous.Enabled = page > 0; footer.Controls.Add(previous);
        foreach (int index in Enumerable.Range(Math.Max(0, page - 2), Math.Min(pages, page + 3) - Math.Max(0, page - 2)))
        {
            int target = index; var button = (StyledButton)Ui.Button((index + 1).ToString(), () => { page = target; Render(); }); button.MinimumSize = new(36, 32); button.Primary = index == page; footer.Controls.Add(button);
        }
        var next = Ui.Button("Next", () => { page++; Render(); }); next.Enabled = page + 1 < pages; footer.Controls.Add(next);
        count.Text = $"{start + 1:N0}–{end:N0} of {total:N0}"; footer.Controls.Add(count);
        if (total > 100) footer.Controls.Add(size); footer.ResumeLayout();
    }
}
