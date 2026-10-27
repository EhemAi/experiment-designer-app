using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed class AnimalEditDialog : Form
{
    internal ProjectInputs? Candidate { get; private set; }
    internal string Reason { get; private set; } = "";
    internal AnimalEditDialog(ProjectInputs input, string recordId)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Text = "Correct animal record"; Font = Theme.Body(); ClientSize = new(650, 640); StartPosition = FormStartPosition.CenterParent;
        var animal = input.Study.Pool.Single(x => x.RecordId == recordId); var details = input.Animals.Single(x => x.RecordId == recordId);
        var values = new[] { animal.AnimalId, animal.Cohort, Ui.Number(animal.WeightGrams), animal.OldLocation, details.MaternalId, details.PaternalId, details.Notes, "" };
        var names = new[] { "Animal ID", "Cohort", "BW (g)", "Old location", "Maternal ID", "Paternal ID", "Notes", "Correction reason" };
        var boxes = values.Select((v, i) => { var box = Ui.Text(names[i], i >= 6); box.Text = v; box.Dock = DockStyle.Fill; return box; }).ToArray();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new(20) };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 150)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < names.Length; i++) { layout.RowStyles.Add(new(SizeType.Absolute, i >= 6 ? 72 : 50)); layout.Controls.Add(Ui.Label(names[i]), 0, i); layout.Controls.Add(boxes[i], 1, i); }
        var origin = details.Origin; var app = input.Imports.FirstOrDefault(x => x.Id == origin?.ApplicationId);
        var message = Ui.Label(origin is null ? "Manual record • Immutable record ID: " + recordId : $"Original observation: {app!.Table}, source row {origin.Row}. Record ID: {recordId}"); message.Dock = DockStyle.Fill;
        layout.Controls.Add(message, 0, 8); layout.SetColumnSpan(message, 2);
        var apply = Ui.Button("Save correction", () =>
        {
            try
            {
                if (!decimal.TryParse(boxes[2].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) || weight <= 0 || weight > 1_000_000)
                    throw new ArgumentException("Require a positive BW in grams, at most 1,000,000, using '.'.");
                if (string.IsNullOrWhiteSpace(boxes[0].Text) || string.IsNullOrWhiteSpace(boxes[7].Text)) throw new ArgumentException("Animal ID and correction reason are required.");
                var replacement = animal with { AnimalId = boxes[0].Text.Trim(), Cohort = boxes[1].Text.Trim(), WeightGrams = weight, OldLocation = boxes[3].Text.Trim() };
                if (!input.Study.Demonstration && input.Study.Pool.Any(x => x.RecordId != recordId && x.AnimalId == replacement.AnimalId && x.Cohort == replacement.Cohort))
                    throw new ArgumentException("This identity already exists within the chosen cohort.");
                Candidate = input with
                {
                    Study = input.Study with { Pool = input.Study.Pool.Replace(animal, replacement) },
                    Animals = input.Animals.Replace(details,
                    details with { MaternalId = boxes[4].Text.Trim(), PaternalId = boxes[5].Text.Trim(), Notes = boxes[6].Text })
                };
                Reason = boxes[7].Text.Trim(); DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex) { message.Text = ex.Message; }
        }); ((StyledButton)apply).Primary = true;
        layout.Controls.Add(Ui.Bar(apply, Ui.Button("Cancel", () => DialogResult = DialogResult.Cancel)), 1, 9);
        Controls.Add(layout); Theme.Apply(this);
    }
}

internal sealed class SwapDialog : Form
{
    private readonly ComboBox first = Ui.Combo([]), second = Ui.Combo([]);
    private readonly TextBox reason = Ui.Text("Reason for swapping these two records");
    private readonly Label message = Ui.Label("Select two allocated records, then preview the affected units and blocks.");
    private readonly PagedGrid preview = new();
    private readonly Button apply, inspect;
    internal string First => ((AnimalOption)first.SelectedItem!).Id;
    internal string Second => ((AnimalOption)second.SelectedItem!).Id;
    internal string Reason => reason.Text.Trim();
    private sealed record AnimalOption(string Id, string Label) { public override string ToString() => Label; }
    internal SwapDialog(DesktopWorkspace workspace, ChosenRevision chosen)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Text = "Preview manual swap"; Font = Theme.Body(); ClientSize = new(1060, 650); MinimumSize = new(930, 560); StartPosition = FormStartPosition.CenterParent;
        var selected = chosen.Input.Inputs.Study.SelectedRecordIds.ToHashSet();
        var animals = chosen.Input.Inputs.Study.Pool.Where(x => selected.Contains(x.RecordId)).OrderBy(x => x.AnimalId, StringComparer.Ordinal)
            .Select(x => new AnimalOption(x.RecordId, $"{x.AnimalId} • {x.Cohort} • record {x.RecordId}")).ToArray();
        first.Items.AddRange(animals.Cast<object>().ToArray()); second.Items.AddRange(animals.Cast<object>().ToArray()); first.Width = second.Width = 460;
        apply = Ui.Button("Apply previewed swap", () => DialogResult = DialogResult.OK); ((StyledButton)apply).Primary = true; apply.Enabled = false;
        void InvalidatePreview() { apply.Enabled = false; message.Text = "Selection or reason changed. Preview again before applying."; }
        first.SelectedIndexChanged += (_, _) => InvalidatePreview(); second.SelectedIndexChanged += (_, _) => InvalidatePreview(); reason.TextChanged += (_, _) => InvalidatePreview();
        inspect = Ui.Button("Preview affected units", () =>
        {
            try
            {
                if (first.SelectedIndex < 0 || second.SelectedIndex < 0) throw new ArgumentException("Choose both records.");
                var candidate = workspace.PreviewSwap(First, Second, Reason); var a = chosen.Allocation;
                var positions = a.Positions.Where(p => p.AnimalRecordId == First || p.AnimalRecordId == Second).ToArray();
                var keys = positions.Select(p => (p.TreatmentIndex, p.Replicate)).ToHashSet();
                var affected = candidate.Pens.Where(p => keys.Contains((p.TreatmentIndex, p.Replicate))).ToArray();
                preview.SetRows(["Treatment", "Unit", "Before mean (g)", "After mean (g)", "Block CV before (%)", "Block CV after (%)"], affected.Length, i =>
                {
                    var p = affected[i]; var before = a.Pens.Single(x => x.TreatmentIndex == p.TreatmentIndex && x.Replicate == p.Replicate);
                    return [chosen.Input.Inputs.Study.Treatments[p.TreatmentIndex].Label, p.Replicate, Ui.Number(before.MeanWeightGrams), Ui.Number(p.MeanWeightGrams),
                        a.Blocks.FirstOrDefault(x => x.Block == p.Replicate)?.CvPercent.ToString("G17") ?? "n.a. (CRD)", candidate.Blocks.FirstOrDefault(x => x.Block == p.Replicate)?.CvPercent.ToString("G17") ?? "n.a. (CRD)"];
                });
                apply.Enabled = true; message.Text = "Preview passed all identity, capacity and enabled CV limits. Apply creates a logged revision; this is not committed yet.";
            }
            catch (Exception ex) { apply.Enabled = false; message.Text = ex.Message; }
        });
        var footer = Ui.Bar(reason, inspect, apply, Ui.Button("Cancel", () => DialogResult = DialogResult.Cancel)); footer.Dock = DockStyle.Bottom;
        message.Dock = DockStyle.Bottom; message.AutoSize = false; message.Height = 62;
        var participants = new TableLayoutPanel { Dock = DockStyle.Top, Height = 64, ColumnCount = 4, RowCount = 1, Padding = new(6) };
        participants.ColumnStyles.Add(new(SizeType.Absolute, 82)); participants.ColumnStyles.Add(new(SizeType.Percent, 50));
        participants.ColumnStyles.Add(new(SizeType.Absolute, 96)); participants.ColumnStyles.Add(new(SizeType.Percent, 50));
        first.Dock = second.Dock = DockStyle.Top;
        var firstCaption = Ui.Label("First"); var secondCaption = Ui.Label("Second");
        firstCaption.AutoSize = secondCaption.AutoSize = false; firstCaption.Dock = secondCaption.Dock = DockStyle.Fill;
        participants.Controls.Add(firstCaption, 0, 0); participants.Controls.Add(first, 1, 0);
        participants.Controls.Add(secondCaption, 2, 0); participants.Controls.Add(second, 3, 0);
        Controls.Add(preview); Controls.Add(participants); Controls.Add(message); Controls.Add(footer); Theme.Apply(this);
    }
    internal void ExerciseForSmoke(DesktopWorkspace workspace, Action<bool, string> check)
    {
        var before = workspace.Document;
        first.SelectedIndex = 0; second.SelectedIndex = 12; reason.Text = "Preview synthetic cross-block swap"; inspect.PerformClick();
        check(apply.Enabled && ReferenceEquals(before, workspace.Document), "UI swap preview passes without committing");
        second.SelectedIndex = 0; check(!apply.Enabled, "Changing a swap participant invalidates its preview"); inspect.PerformClick();
        check(!apply.Enabled && ReferenceEquals(before, workspace.Document), "Same-record swap is rejected in the actual dialog");
    }
}

internal sealed class MappingDialog : Form
{
    private readonly DataGridView definitions = Ui.Grid(true);
    private readonly PagedGrid preview = new();
    private readonly TextBox seed = Ui.Text("Independent mapping seed"), reason = Ui.Text("Mapping reason");
    private readonly Label message = Ui.Label("Provide exactly one physical pen/cage per allocation unit, with full scoped locations.");
    private readonly Button apply, inspect;
    internal ImmutableArray<PhysicalPen> Pens { get; private set; } = [];
    internal ulong Seed { get; private set; }
    internal string Reason => reason.Text.Trim();
    internal MappingDialog(DesktopWorkspace workspace, ChosenRevision chosen)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Text = "Map physical units"; Font = Theme.Body(); ClientSize = new(1040, 700); MinimumSize = new(960, 610); StartPosition = FormStartPosition.CenterParent;
        var table = new DataTable(); foreach (string column in new[] { "Physical ID", "Scoped location", "Capacity", "Eligible replicate" }) table.Columns.Add(column);
        foreach (var p in chosen.PhysicalPens) table.Rows.Add(p.Id, p.Location, p.Capacity, p.EligibleBlock?.ToString() ?? ""); definitions.DataSource = table;
        seed.Text = chosen.Allocation.LocationSeed?.ToString() ?? "202613";
        apply = Ui.Button("Apply previewed mapping", () => DialogResult = DialogResult.OK); ((StyledButton)apply).Primary = true; apply.Enabled = false;
        void Invalidate() { apply.Enabled = false; }
        definitions.CellValueChanged += (_, _) => Invalidate(); definitions.RowsRemoved += (_, _) => Invalidate(); seed.TextChanged += (_, _) => Invalidate(); reason.TextChanged += (_, _) => Invalidate();
        definitions.DataError += (_, e) => { e.ThrowException = false; message.Text = "Correct the physical unit value."; };
        inspect = Ui.Button("Preview mapping", () =>
        {
            try
            {
                definitions.EndEdit(); Validate(); Seed = Ui.Seed(seed.Text);
                if (string.IsNullOrWhiteSpace(Reason)) throw new ArgumentException("Supply a mapping reason.");
                Pens = table.Rows.Cast<DataRow>().Where(x => x.RowState != DataRowState.Deleted).Select(row =>
                {
                    int capacity = int.Parse(Convert.ToString(row["Capacity"])!, CultureInfo.InvariantCulture);
                    string block = Convert.ToString(row["Eligible replicate"])!.Trim();
                    return new PhysicalPen(Convert.ToString(row["Physical ID"])!.Trim(), Convert.ToString(row["Scoped location"])!.Trim(), capacity,
                        block.Length == 0 ? null : int.Parse(block, CultureInfo.InvariantCulture));
                }).ToImmutableArray();
                var candidate = workspace.PreviewMap(Pens, Seed); var byId = Pens.ToDictionary(x => x.Id);
                preview.SetRows(["Treatment", "Unit replicate", "Physical ID", "Scoped location"], candidate.Locations.Length, i =>
                {
                    var location = candidate.Locations[i]; return [chosen.Input.Inputs.Study.Treatments[location.TreatmentIndex].Label, location.Replicate, location.PhysicalPenId, byId[location.PhysicalPenId].Location];
                });
                apply.Enabled = true; message.Text = "Mapping passed uniqueness, capacity and eligibility checks. Review the Mapping preview tab before applying.";
            }
            catch (Exception ex) { apply.Enabled = false; message.Text = ex.Message; }
        });
        string rule = chosen.Input.Inputs.Study.Design switch
        {
            Design.Rcbd => "RCBD requires a complete eligible physical pool for each block.",
            Design.Crd => "CRD can use a global pool or complete pools for each replicate.",
            _ => "Conventional allocation can use a global pool or complete declared block pools."
        };
        var footer = Ui.Bar(Ui.Label("Mapping seed"), seed, reason, inspect, apply, Ui.Button("Cancel", () => DialogResult = DialogResult.Cancel)); footer.Dock = DockStyle.Bottom;
        message.Dock = DockStyle.Bottom; message.AutoSize = false; message.Height = 55;
        Controls.Add(Ui.Tabs(("Physical unit definitions", definitions), ("Mapping preview", preview))); Controls.Add(Ui.Bar(Ui.Label(rule))); Controls.Add(message); Controls.Add(footer); Theme.Apply(this);
    }
    internal void ExerciseForSmoke(DesktopWorkspace workspace, Action<bool, string> check)
    {
        var before = workspace.Document; reason.Text = "Preview declared physical pool"; inspect.PerformClick();
        check(apply.Enabled && Pens.Length == 12 && ReferenceEquals(before, workspace.Document), "UI mapping preview validates the complete physical pool without committing");
        definitions.Rows[0].Cells["Capacity"].Value = "1"; check(!apply.Enabled, "Changing capacity invalidates the earlier mapping preview"); inspect.PerformClick();
        check(!apply.Enabled && ReferenceEquals(before, workspace.Document), "Insufficient capacity is rejected by the actual mapping dialog");
    }
}
