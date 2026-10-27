using System.Collections.Immutable;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal class SourceDialog : Form
{
    internal readonly ImportSource Source;
    protected readonly ComboBox sheet = Ui.Combo([]);
    protected readonly PagedGrid raw = new();
    protected readonly NumericUpDown startColumn = new() { Minimum = 1, Maximum = 16_384, Value = 1, Width = 80 };
    internal SourceDialog(ImportSource source)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Source = source; Text = "Source observations • " + source.FileName; Font = Theme.Body(); ClientSize = new(1120, 680);
        MinimumSize = new(960, 640); StartPosition = FormStartPosition.CenterParent;
        sheet.Items.AddRange(source.Tables.Select(x => x.Name).Cast<object>().ToArray());
        sheet.SelectedIndexChanged += (_, _) => ShowRaw(); startColumn.ValueChanged += (_, _) => ShowRaw();
        Controls.Add(raw); Controls.Add(Ui.Bar(Ui.Label("Choose table / sheet"), sheet, Ui.Label("First visible column"), startColumn,
            Ui.Label("25 columns per view • Original source values")));
        Theme.Apply(this);
    }
    protected void ShowRaw()
    {
        if (sheet.SelectedItem is not string name) { raw.SetRows([], 0, _ => []); return; }
        var table = Source.Tables.Single(x => x.Name == name);
        int first = (int)startColumn.Value, max = table.Rows.SelectMany(x => x.Cells).Select(x => x.Column).DefaultIfEmpty(1).Max();
        int last = Math.Min(max, first + 24); if (last < first) last = first;
        var columns = Enumerable.Range(first, last - first + 1).ToArray();
        raw.SetRows(new[] { "Source row" }.Concat(columns.Select(x => "Column " + x)).ToArray(), table.Rows.Length, i =>
        {
            var row = table.Rows[i]; var cells = row.Cells.ToDictionary(x => x.Column);
            return new object?[] { row.Number }.Concat(columns.Select(c => cells.TryGetValue(c, out var cell)
                ? cell.Formula is null ? cell.Value : "=" + cell.Formula + " (cached: " + cell.Value + ")" : "")).ToArray();
        });
    }
}

internal sealed class ImportDialog : SourceDialog
{
    private readonly bool animal, demonstration;
    private readonly NumericUpDown header = new() { Minimum = 1, Maximum = 1_048_576, Value = 1, Width = 85 };
    private readonly NumericUpDown first = new() { Minimum = 2, Maximum = 1_048_576, Value = 2, Width = 85 };
    private readonly NumericUpDown last = new() { Minimum = 2, Maximum = 1_048_576, Value = 2, Width = 85 };
    private readonly ComboBox weightUnit = Ui.Combo(["g", "kg"]);
    private readonly CheckBox numericIds = new() { Text = "Explicitly allow stored integer numeric IDs (display formatting is ignored)", AutoSize = true };
    private readonly CheckBox append = new() { Text = "Append to existing animal pool", AutoSize = true };
    private readonly TextBox reason = Ui.Text("Reason for importing this source");
    private readonly Dictionary<string, ComboBox> fields = [];
    private readonly PagedGrid preview = new();
    private readonly Button apply, inspect;
    private readonly Label message = Ui.Label("Choose the source sheet, exact rows and columns. Preview before applying.");
    private bool refreshing, working;
    private readonly ProgressBar busyIndicator = new() { Style = ProgressBarStyle.Marquee, Width = 64, Height = 8, Visible = false };
    private sealed record ColumnOption(int Number, string Caption) { public override string ToString() => Caption; }
    internal AnimalImport? AnimalBatch { get; private set; }
    internal TreatmentImport? TreatmentBatch { get; private set; }
    internal bool Append => append.Checked;
    internal string Reason => reason.Text.Trim();
    internal ImportDialog(ImportSource source, bool animal, bool demonstration) : base(source)
    {
        this.animal = animal; this.demonstration = demonstration; Text = (animal ? "Import animals • " : "Import treatments • ") + source.FileName;
        reason.Text = animal ? "Import animals" : "Import treatments";
        Controls.Clear();
        apply = Ui.Button("Apply reviewed import", () =>
        {
            if (working || (AnimalBatch is null && TreatmentBatch is null)) return;
            if (string.IsNullOrWhiteSpace(Reason)) { message.Text = "Enter an import reason."; return; }
            DialogResult = DialogResult.OK;
        }); ((StyledButton)apply).Primary = true; apply.Enabled = false;
        inspect = Ui.Button("Validate and preview", () => _ = ValidateAsync());
        var mappings = new TableLayoutPanel { Dock = DockStyle.Top, Height = 88, ColumnCount = animal ? 7 : 6, RowCount = 1, Margin = Padding.Empty };
        mappings.RowStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < mappings.ColumnCount; i++) mappings.ColumnStyles.Add(new(SizeType.Percent, 100f / mappings.ColumnCount));
        foreach (string name in animal ? new[] { "Animal ID", "BW", "Cohort", "Old location", "Maternal ID", "Paternal ID", "Notes" } : new[] { "Treatment ID", "Label", "Factor 1", "Factor 2", "Factor 3", "Factor 4" })
        {
            var combo = Ui.Combo([]); combo.Dock = DockStyle.Top; fields.Add(name, combo);
            var field = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new(4) };
            field.ColumnStyles.Add(new(SizeType.Percent, 100));
            field.RowStyles.Add(new(SizeType.Absolute, 30)); field.RowStyles.Add(new(SizeType.Percent, 100));
            var label = Ui.Label(name); label.Font = Theme.Body(8.5f, FontStyle.Bold);
            label.AutoSize = false; label.Dock = DockStyle.Fill; label.Margin = new(4, 4, 4, 0);
            field.Controls.Add(label, 0, 0); field.Controls.Add(combo, 0, 1); mappings.Controls.Add(field, mappings.Controls.Count, 0);
            combo.SelectedIndexChanged += (_, _) => InvalidatePreview();
        }
        weightUnit.Width = 70; weightUnit.SelectedItem = "g";
        var range = Ui.Bar(Ui.Label("Sheet"), sheet, Ui.Label("Header"), header, Ui.Label("First row"), first, Ui.Label("Last row"), last);
        var unitLabel = Ui.Label("BW unit"); unitLabel.Visible = animal;
        var options = Ui.Bar(unitLabel, weightUnit, numericIds, append);
        weightUnit.Visible = animal; append.Visible = animal;
        var sourceControls = Ui.Bar(Ui.Label("First visible source column"), startColumn, Ui.Label("25 columns per view. CSV row numbers identify record start lines."));
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new(10) };
        top.ColumnStyles.Add(new(SizeType.Percent, 100));
        top.Controls.Add(range); top.Controls.Add(mappings); top.Controls.Add(options); top.Controls.Add(sourceControls);
        var views = Ui.Tabs(("Original source", raw), ("Validated preview / errors", preview));
        var bottom = Ui.Bar(inspect, apply, busyIndicator, Ui.Button("Cancel", () => DialogResult = DialogResult.Cancel)); bottom.Dock = DockStyle.Bottom;
        message.Dock = DockStyle.Bottom; message.AutoSize = false; message.Height = 56;
        Controls.Add(views); Controls.Add(top); Controls.Add(message); Controls.Add(bottom);
        sheet.SelectedIndexChanged += (_, _) => PopulateColumns(); header.ValueChanged += (_, _) => PopulateColumns();
        first.ValueChanged += (_, _) => InvalidatePreview(); last.ValueChanged += (_, _) => InvalidatePreview();
        numericIds.CheckedChanged += (_, _) => InvalidatePreview(); weightUnit.SelectedIndexChanged += (_, _) => InvalidatePreview();
        Theme.Apply(this);
        FormClosing += (_, e) => { if (working) { e.Cancel = true; message.Text = "Finish the source validation before closing this dialog."; } };
    }
    private void InvalidatePreview()
    {
        if (refreshing || working) return; AnimalBatch = null; TreatmentBatch = null; apply.Enabled = false;
        message.Text = demonstration ? "DEMONSTRATION DATA • Duplicate displayed animal IDs are permitted. Validate the chosen mapping." : "Mapping changed. Validate before applying.";
    }
    private void PopulateColumns()
    {
        if (sheet.SelectedItem is not string name) return;
        var table = Source.Tables.Single(x => x.Name == name);
        var cells = table.Rows.SingleOrDefault(x => x.Number == (int)header.Value)?.Cells ?? [];
        int max = table.Rows.SelectMany(x => x.Cells).Select(x => x.Column).DefaultIfEmpty(1).Max();
        refreshing = true;
        try
        {
            foreach (var (field, combo) in fields)
            {
                combo.Items.Clear(); bool required = field is "Animal ID" or "BW" or "Treatment ID" or "Label";
                if (!required) combo.Items.Add(new ColumnOption(0, "(unmapped)"));
                for (int column = 1; column <= max; column++)
                    combo.Items.Add(new ColumnOption(column, $"{column}: {cells.SingleOrDefault(x => x.Column == column)?.Value ?? "(no header)"}"));
                combo.SelectedIndex = required ? -1 : 0;
            }
        }
        finally { refreshing = false; }
        InvalidatePreview();
    }
    private int? Column(string name) => (fields[name].SelectedItem as ColumnOption)?.Number is > 0 and var number ? number : null;
    internal void ConfigureEnteredAnimals()
    {
        if (!animal) throw new InvalidOperationException("Animal entry requires animal mappings.");
        sheet.SelectedIndex = 0; header.Value = 1; first.Value = 2;
        last.Value = Source.Tables[0].Rows.Max(r => r.Number);
        foreach (var (name, column) in new[] { ("Animal ID", 1), ("BW", 2), ("Cohort", 3), ("Old location", 4) })
            fields[name].SelectedItem = fields[name].Items.Cast<ColumnOption>().Single(c => c.Number == column);
        weightUnit.SelectedItem = "g"; numericIds.Checked = false; reason.Text = "Enter and review animal observations";
    }
    internal async Task ExerciseEnteredAnimalsForSmoke(Action<bool, string> check)
    {
        ConfigureEnteredAnimals(); await ValidateAsync();
        check(apply.Enabled && AnimalBatch?.Animals.Length == 2 && AnimalBatch.Animals[0].Cohort == "Day 1" && AnimalBatch.Animals[1].OldLocation == "Source B",
            "Entered animals use fixed reviewed mappings without dropping optional fields");
    }
    private async Task ValidateAsync()
    {
        if (working) return;
        if (sheet.SelectedItem is not string table) { message.Text = "Choose a sheet explicitly."; return; }
        int id = Column(animal ? "Animal ID" : "Treatment ID") ?? 0, value = Column(animal ? "BW" : "Label") ?? 0;
        if (id == 0 || value == 0) { message.Text = "Choose both required columns explicitly."; return; }
        var cols = animal ? new AnimalColumns(id, value, Column("Cohort"), Column("Old location"), Column("Maternal ID"), Column("Paternal ID"), Column("Notes")) : null;
        var factors = animal ? ImmutableArray<int>.Empty : Enumerable.Range(1, 4).Select(i => Column("Factor " + i)).Where(x => x.HasValue).Select(x => x!.Value).ToImmutableArray();
        int h = (int)header.Value, f = (int)first.Value, l = (int)last.Value; bool integer = numericIds.Checked; string unit = (string)weightUnit.SelectedItem!;
        working = true; inspect.Enabled = apply.Enabled = false; foreach (var combo in fields.Values) combo.Enabled = false;
        sheet.Enabled = header.Enabled = first.Enabled = last.Enabled = numericIds.Enabled = weightUnit.Enabled = false;
        busyIndicator.Visible = true; message.Text = "Validating source records…";
        try
        {
            ImmutableArray<ImportIssue> issues;
            if (animal)
            {
                var result = await Task.Run(() => DataImport.Animals(Source, table, h, f, l, cols!, unit, demonstration, integer));
                AnimalBatch = result.Value; issues = result.Issues;
                if (result.Success)
                {
                    var batch = result.Value!; preview.SetRows(["Source row", "Animal ID", "Cohort", "BW (g)", "Old location", "Maternal ID", "Paternal ID", "Notes"], batch.Animals.Length,
                    i => [batch.Details[i].Origin!.Row, batch.Animals[i].AnimalId, batch.Animals[i].Cohort, Ui.Number(batch.Animals[i].WeightGrams), batch.Animals[i].OldLocation, batch.Details[i].MaternalId, batch.Details[i].PaternalId, batch.Details[i].Notes]);
                }
            }
            else
            {
                var result = await Task.Run(() => DataImport.Treatments(Source, table, h, f, l, new(id, value, factors), integer));
                TreatmentBatch = result.Value; issues = result.Issues;
                if (result.Success)
                {
                    var batch = result.Value!; preview.SetRows(["Source row", "Treatment ID", "Label", "Factor columns retained"], batch.Treatments.Length,
                    i => [batch.Details[i].Origin!.Row, batch.Treatments[i].Id, batch.Treatments[i].Label, string.Join(", ", batch.Details[i].FactorColumns.Select(x => x.Column))]);
                }
            }
            apply.Enabled = issues.IsEmpty && (AnimalBatch is not null || TreatmentBatch is not null);
            if (!issues.IsEmpty) preview.SetRows(["Source row", "Field", "Problem"], issues.Length, i => [issues[i].Row, issues[i].Field, issues[i].Message]);
            message.Text = apply.Enabled ? $"{(demonstration ? "DEMONSTRATION DATA • " : "")}{AnimalBatch?.Animals.Length ?? TreatmentBatch!.Treatments.Length:N0} valid records. Review the preview tab. " + (animal ? "Heaviest animals will be selected automatically; unused animals become EX." : "Treatment order and source provenance are retained.")
                : $"{issues.Length:N0} invalid records. Correct the source or mapping. Nothing has been imported.";
        }
        catch (Exception ex) { message.Text = ex.Message; AnimalBatch = null; TreatmentBatch = null; apply.Enabled = false; }
        finally
        {
            busyIndicator.Visible = false; working = false; inspect.Enabled = true; foreach (var combo in fields.Values) combo.Enabled = true;
            sheet.Enabled = header.Enabled = first.Enabled = last.Enabled = numericIds.Enabled = weightUnit.Enabled = true;
        }
    }
    internal async Task ReviewPendingPoolForSmoke(Action<bool, string> check, int lastRow = 290)
    {
        sheet.SelectedIndex = 0; header.Value = 1; first.Value = 2; last.Value = lastRow;
        numericIds.Checked = true;
        fields["Animal ID"].SelectedItem = fields["Animal ID"].Items.Cast<ColumnOption>().Single(x => x.Number == 1);
        fields["BW"].SelectedItem = fields["BW"].Items.Cast<ColumnOption>().Single(x => x.Number == 2);
        await ValidateAsync();
        check(apply.Enabled && AnimalBatch?.Animals.Length == lastRow - 1, "Native reviewed import dialog accepts the complete animal workbook");
        apply.PerformClick();
    }

    internal async Task ExerciseForSmoke(Action<bool, string> check)
    {
        check(reason.Parent is null && Reason == (animal ? "Import animals" : "Import treatments"), "Import records a mode-specific action without requiring a reason field");
        if (!animal)
        {
            check(!weightUnit.Visible && !append.Visible && !fields.ContainsKey("BW"), "Treatment import hides animal-only mappings and options");
            sheet.SelectedIndex = 0; first.Value = 2; last.Value = 3;
            fields["Treatment ID"].SelectedItem = fields["Treatment ID"].Items.Cast<ColumnOption>().Single(x => x.Number == 1);
            fields["Label"].SelectedItem = fields["Label"].Items.Cast<ColumnOption>().Single(x => x.Number == 2);
            reason.Text = "Reviewed ordered treatment source"; await ValidateAsync();
            check(TreatmentBatch?.Treatments.Length == 2 && apply.Enabled && !message.Text.Contains("animal import"), "Treatment import validates explicit ordered mappings with mode-specific guidance");
            return;
        }
        check(sheet.SelectedIndex == -1 && !apply.Enabled, "Source sheet and fields must be chosen explicitly");
        sheet.SelectedIndex = 0; first.Value = 2; last.Value = 49;
        foreach (var (name, column) in new[] { ("Animal ID", 1), ("BW", 2), ("Cohort", 3), ("Old location", 4), ("Maternal ID", 5), ("Paternal ID", 6), ("Notes", 7) })
            fields[name].SelectedItem = fields[name].Items.Cast<ColumnOption>().Single(x => x.Number == column);
        reason.Text = "Reviewed synthetic source";
        await ValidateAsync();
        check(AnimalBatch?.Animals.Length == 48 && apply.Enabled, "UI import preview validates the complete chosen source range");
        check(AnimalBatch!.Details[0].Origin!.Row == 2 && AnimalBatch.Application.WeightUnit == "g", "UI mapping retains original row and weight unit");
        fields["BW"].SelectedItem = fields["BW"].Items.Cast<ColumnOption>().Single(x => x.Number == 1);
        check(!apply.Enabled && AnimalBatch is null, "Changed mapping invalidates the earlier import preview");
        await ValidateAsync(); check(!apply.Enabled && AnimalBatch is null, "Invalid repeated mapping cannot be applied from the dialog");
        fields["BW"].SelectedItem = fields["BW"].Items.Cast<ColumnOption>().Single(x => x.Number == 2);
        await ValidateAsync(); check(apply.Enabled, "Corrected mapping can be previewed again");
    }
}
