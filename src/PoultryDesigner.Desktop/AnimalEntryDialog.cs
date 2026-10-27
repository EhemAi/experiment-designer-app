using System.Data;
using System.Text;
using PoultryDesigner.Engine;
namespace PoultryDesigner.Desktop;

internal sealed class AnimalEntryDialog : Form
{
    private readonly DataGridView grid = Ui.Grid(true);
    private readonly DataTable entries = new();
    private readonly bool demonstration;
    private readonly Label message = Ui.Label("Enter IDs and body weights in grams. Cohort and old location are optional.");
    internal ImportSource? Source { get; private set; }
    internal AnimalEntryDialog(bool demonstration = false)
    {
        this.demonstration = demonstration;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96); Font = Theme.Body();
        Text = "Enter animals"; ClientSize = new(860, 520); MinimumSize = new(700, 440); StartPosition = FormStartPosition.CenterParent;
        foreach (string name in new[] { "Animal ID", "BW (g)", "Cohort", "Old location" }) entries.Columns.Add(name, typeof(string));
        grid.DataSource = entries;
        var heading = Ui.Label("Enter animals"); heading.Font = Theme.Heading(18);
        var footer = Ui.Bar(Ui.Button("Review entries", Review), Ui.Button("Cancel", () => DialogResult = DialogResult.Cancel)); footer.Dock = DockStyle.Bottom;
        message.Dock = DockStyle.Bottom; message.AutoSize = false; message.Height = 54;
        Controls.Add(grid); Controls.Add(Ui.Bar(heading)); Controls.Add(message); Controls.Add(footer); Theme.Apply(this);
    }
    private void Review()
    {
        try
        {
            grid.EndEdit(); BindingContext![entries]!.EndCurrentEdit();
            Source = CreateSource();
            var preview = DataImport.Animals(Source, "Delimited", 1, 2, Source.Tables[0].Rows.Max(r => r.Number), new(1, 2, Cohort: 3, OldLocation: 4), demonstration: demonstration);
            if (!preview.Success) throw new ArgumentException(string.Join(" ", preview.Issues.Take(3).Select(i => $"Row {i.Row}: {i.Field} — {i.Message}")));
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { message.Text = ex.Message; message.ForeColor = Theme.Red; }
    }
    private ImportSource CreateSource()
    {
        var rows = entries.Rows.Cast<DataRow>().Where(r => r.RowState != DataRowState.Deleted && r.ItemArray.Any(v => !string.IsNullOrWhiteSpace(Convert.ToString(v)))).ToArray();
        if (rows.Length == 0) throw new ArgumentException("Enter at least one animal ID and weight.");
        string Quote(object? value) => "\"" + (Convert.ToString(value) ?? "").Replace("\"", "\"\"") + "\"";
        var csv = "ID,BW,Cohort,Old location\n" + string.Join("\n", rows.Select(r => string.Join(",", r.ItemArray.Select(Quote))));
        return DataImport.Read(Encoding.UTF8.GetBytes(csv), "entered-animals.csv");
    }
    internal ImportSource ExerciseForSmoke()
    {
        entries.Rows.Add("000123", "125.5", "Day 1", "Source A"); entries.Rows.Add("A,002", "130", "Day 1", "Source B");
        return CreateSource();
    }
}
