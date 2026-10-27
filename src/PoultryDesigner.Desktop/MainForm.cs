using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm : Form
{
    internal readonly DesktopWorkspace Workspace = new();
    private readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly Panel[] stages = Enumerable.Range(0, 4).Select(_ => new Panel { Dock = DockStyle.Fill, BackColor = Color.White }).ToArray();
    private readonly Label title = Ui.Label("Design your study.");
    private readonly Label subtitle = Ui.Label("");
    private readonly Label status = Ui.Label("New project • Set up your study to get started");
    private readonly Label guideTitle = Ui.Label("A clear starting point");
    private readonly Label guide = Ui.Label("");
    private readonly Label counts = Ui.Label("");
    private FlowLayoutPanel files = null!;
    private FlowLayoutPanel navigation = null!;
    private Button cancel = null!;
    private readonly ComboBox design = new DesktopComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 235 };
    private readonly ComboBox unit = Ui.Combo(["pen", "cage"]);
    private readonly NumericUpDown replicates = new() { Maximum = 100_000, Width = 235 };
    private readonly NumericUpDown perPen = new() { Maximum = 100_000, Width = 235 };
    private readonly TextBox setupReason = Ui.Text("Reason for settings change");
    private readonly DataGridView treatments = Ui.Grid(true);
    private readonly Label method = Ui.Label("");
    private readonly TextBox selectionReason = Ui.Text("Reason for selection decisions");
    private readonly TextBox search = Ui.Text("Find an animal ID or cohort");
    private readonly DataGridView animals = Ui.Grid();
    private readonly Label selectionCount = Ui.Label("");
    private readonly Label pageCount = Ui.Label("");
    private readonly TextBox seed = Ui.Text("Allocation seed");
    private readonly Label resultLabel = Ui.Label("No completed allocation yet.");
    private readonly ComboBox revisions = new DesktopComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
    private readonly PagedGrid movement = new(), pens = new(), balance = new(), blocks = new(), edits = new(), maps = new();
    private readonly PagedGrid history = new(), inputHistory = new(), sources = new();
    private readonly Dictionary<string, SelectionDecision> selection = new(StringComparer.Ordinal);
    private Animal[] filteredAnimals = [];
    private int animalPage, stage;
    private bool loading, setupDirty, selectionDirty, inOperation, allowClose;
    private CancellationTokenSource? operation;
    private ChosenRevision? chosen;
    private string? presentedActiveResultId;
    private DataTable treatmentTable = new();
    private sealed record MethodOption(Design Code, string Label) { public override string ToString() => Label; }
    private sealed record RevisionOption(string Id, string Label) { public override string ToString() => Label; }

    internal MainForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Text = "Experiment Designer"; Icon = Brand.Icon; Font = Theme.Body(); BackColor = Theme.Navy;
        BuildShell(); BuildSetup(); BuildAnimals(); BuildAllocation(); BuildHistory(); Theme.Apply(this); Reload(); SetStage(0);
        FormClosing += async (_, e) =>
        {
            if (allowClose) return;
            if (inOperation) { e.Cancel = true; operation?.Cancel(); status.Text = "Cancelling operation. Close again after it finishes."; return; }
            if (Workspace.IsDirty || setupDirty || selectionDirty) { e.Cancel = true; if (await MayDiscardAsync()) { allowClose = true; Close(); } }
        };
    }

    private void DraftSetup() { if (!loading) { setupDirty = true; UpdateTitle(); } }
    private void Capabilities()
    {
        bool rcbd = (design.SelectedItem as MethodOption)?.Code == Design.Rcbd;
        method.Text = rcbd ? "Animals are grouped by body weight before allocation."
            : "Review treatment body-weight summaries after allocation.";
    }
    private void ApplySetup()
    {
        treatments.EndEdit(); Validate();
        string reason = AuditReason(setupReason, "Apply study settings", "Reason for recording these settings as a new input revision");
        var previous = Workspace.Inputs;
        var trts = treatmentTable.Rows.Cast<DataRow>().Where(x => x.RowState != DataRowState.Deleted)
            .Select(x => new Treatment(Convert.ToString(x["Treatment ID"])!.Trim(), Convert.ToString(x["Label"])!.Trim())).ToImmutableArray();
        var study = previous.Study with
        {
            Design = ((MethodOption)design.SelectedItem!).Code,
            Treatments = trts,
            Replicates = (int)replicates.Value,
            AnimalsPerPen = (int)perPen.Value,
            FilterBalance = previous.Study.FilterBalance && ((MethodOption)design.SelectedItem!).Code == Design.Rcbd
        };
        Workspace.Revise(previous with
        {
            Study = study,
            ExperimentalUnit = (string)unit.SelectedItem!,
            Treatments = trts.Select(t => previous.Treatments.FirstOrDefault(x => x.TreatmentId == t.Id) ?? new TreatmentDetails(t.Id, FactorColumns: [])).ToImmutableArray()
        }, reason);
        setupDirty = false; setupReason.Clear(); Reload(); Notify("Settings applied"); status.Text = "Settings saved as a new input revision. Review selection before allocating.";
    }
    private bool capturingSelection;
    private void CaptureSelection()
    {
        if (Workspace.Inputs.AutomaticPoolSelection) return;
        if (loading || capturingSelection) return;
        capturingSelection = true;
        try
        {
            animals.EndEdit();
            foreach (DataGridViewRow row in animals.Rows)
            {
                string id = Convert.ToString(row.Cells["Record ID"].Value) ?? "";
                if (!selection.ContainsKey(id)) continue;
                if (!Enum.TryParse<SelectionChoice>(Convert.ToString(row.Cells["Decision"].Value), out var choice)) continue;
                selection[id] = new(id, choice, Convert.ToString(row.Cells["Reason"].Value) ?? "");
            }
        }
        finally { capturingSelection = false; }
    }
    private void SaveSelection(bool exact)
    {
        if (setupDirty) ApplySetup(); CaptureSelection(); string reason = AuditReason(selectionReason, "Record selection", "Reason for saving these exact selection decisions");
        var decisions = Workspace.Inputs.Study.Pool.Select(x => selection[x.RecordId]).ToImmutableArray();
        if (exact) Workspace.Select(decisions, reason); else Workspace.SaveSelectionDraft(decisions, reason);
        selectionDirty = false; selectionReason.Clear(); Reload(); Notify(exact ? "Exact selection committed" : "Selection draft saved"); status.Text = exact ? "Exact selection committed. The study is ready when all settings are valid." : "Selection draft saved; unresolved or incorrect counts prevent allocation.";
    }
    private void EditAnimal() => Guard(() =>
    {
        if (animals.SelectedRows.Count != 1) throw new ArgumentException("Highlight one animal record to edit.");
        string id = Convert.ToString(animals.SelectedRows[0].Cells["Record ID"].Value)!;
        FlushDrafts();
        using var dialog = new AnimalEditDialog(Workspace.Inputs, id);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Workspace.Revise(dialog.Candidate!, dialog.Reason); Reload(); status.Text = "Record correction retained as a new input revision. Previous allocations keep their original source values.";
    });
    private void ShowChosen(ChosenRevision snapshot)
    {
        chosen = snapshot; var a = snapshot.Allocation; var s = snapshot.Input.Inputs.Study;
        string state = snapshot.ResultId == Workspace.Document.ActiveResultId ? "Active" : "Historical";
        ShowQuality(snapshot);
        resultLabel.Text = $"{(s.Demonstration ? "Demo · " : "")}Result {a.Revision} · {state}" + (snapshot.Input.Id == Workspace.Document.CurrentInputId ? "" : " · Previous study settings");
        Components.Tip(resultLabel, $"Input revision {snapshot.Input.Number}; allocation seed {a.Seed}; {a.Edits.Length} ordered edits. Full records are available in History.");
        var rows = ResultViews.Movement(s, a); var physical = snapshot.PhysicalPens.ToDictionary(x => x.Id);
        movement.SetRows(["Animal ID", "Treatment", "Replicate", snapshot.Input.Inputs.ExperimentalUnit == "cage" ? "Cage number" : "Pen number", "BW (g)", "Old location", "New location", "Cohort", "Disposition", "Record ID"], rows.Length, i =>
        {
            var r = rows[i]; return [r.AnimalId, r.TreatmentId, r.Replicate, r.PhysicalPenId, Ui.Number(r.WeightGrams), r.OldLocation,
                r.PhysicalPenId is null ? r.Disposition == AnimalDisposition.Allocated ? "Unmapped" : "" : physical[r.PhysicalPenId].Location, r.Cohort, r.Disposition, r.RecordId];
        });
        movement.Grid.Columns["Record ID"]!.Visible = false;
        pens.SetRows(["Treatment", "Unit replicate", "Animals", "Total BW (g)", "Mean BW (g)", "Sample SD (g)"], a.Pens.Length, i =>
        {
            var p = a.Pens[i]; return [s.Treatments[p.TreatmentIndex].Label, p.Replicate, p.Count, Ui.Number(p.TotalWeightGrams), Ui.Number(p.MeanWeightGrams), p.SampleSdGrams?.ToString("G6") ?? "n.a."];
        });
        var overview = ResultViews.Balance(s, a);
        balance.SetRows(["Treatment", "Unit replicates", "Mean BW (g)", "Min pen mean", "Max pen mean", "CV of pen means (%)", "Difference (g)"], overview.Treatments.Length, i =>
        {
            var b = overview.Treatments[i]; return [b.Label, b.PenCount, Ui.Number(b.MeanWeightGrams), Ui.Number(b.MinimumPenMeanGrams), Ui.Number(b.MaximumPenMeanGrams), b.CvOfPenMeansPercent.ToString("G6"), Ui.Number(b.DifferenceFromOverallGrams)];
        });
        blocks.SetRows(["Block", "Current CV (%)", "Attempts", "Best original CV (%)", "Last original CV (%)"], a.Blocks.Length, i =>
        {
            var b = a.Blocks[i]; var t = a.Attempts.Single(x => x.Block == b.Block); return [b.Block, b.CvPercent.ToString("G17"), t.Attempts, t.BestCvPercent.ToString("G17"), t.LastCvPercent.ToString("G17")];
        });
        edits.SetRows(["Order", "First record", "Second record", "Reason", "First before", "First after", "Second before", "Second after"], a.Edits.Length, i =>
        {
            var e = a.Edits[i]; string Slot(Position p) => $"{s.Treatments[p.TreatmentIndex].Label} / Unit {p.Replicate} / Block {p.Block}";
            return [i + 1, e.FirstRecordId, e.SecondRecordId, e.Reason, Slot(e.BeforeFirst), Slot(e.AfterFirst), Slot(e.BeforeSecond), Slot(e.AfterSecond)];
        });
        maps.SetRows(["Treatment", "Unit replicate", "Physical ID", "Scoped location", "Capacity", "Eligible replicate", "Mapping seed"], a.Locations.Length, i =>
        {
            var l = a.Locations[i]; var p = physical[l.PhysicalPenId]; return [s.Treatments[l.TreatmentIndex].Label, l.Replicate, p.Id, p.Location, p.Capacity, p.EligibleBlock, a.LocationSeed];
        });
    }
    private void Swap() => Guard(() =>
    {
        FlushDrafts(); if (chosen is null || chosen.ResultId != Workspace.Document.ActiveResultId) throw new ArgumentException("Choose the active current-input revision to edit.");
        using var dialog = new SwapDialog(Workspace, chosen);
        if (dialog.ShowDialog(this) == DialogResult.OK) { Workspace.Swap(dialog.First, dialog.Second, dialog.Reason, chosen.ResultId); Reload(); Notify("Swap recorded"); }
    });
    private void Reports() => Guard(() =>
    {
        if (chosen is null) throw new ArgumentException("Choose a completed allocation to export.");
        if (chosen.Allocation.Locations.Length != chosen.Allocation.Pens.Length) throw new ArgumentException("This result has no complete housing assignment. Choose the mapped result or assign housing before exporting the final allotment.");
        using var dialog = new ReportDialog(chosen, chosen.ResultId != Workspace.Document.ActiveResultId,
            chosen.Input.Id != Workspace.Document.CurrentInputId, Workspace.FilePath); dialog.ShowDialog(this);
    });
    private void OpenGuide() => Guard(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "User guide.html");
        if (!File.Exists(path)) throw new FileNotFoundException("The user guide is missing from this application package.");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    });
    private void Map() => Guard(() =>
    {
        FlushDrafts(); if (chosen is null || chosen.ResultId != Workspace.Document.ActiveResultId) throw new ArgumentException("Choose the active current-input revision to map.");
        using var dialog = new MappingDialog(Workspace, chosen);
        if (dialog.ShowDialog(this) == DialogResult.OK) { Workspace.Map(dialog.Pens, dialog.Seed, dialog.Reason, chosen.ResultId); Reload(); Notify("Physical units mapped"); }
    });
    private void InspectSource() => Guard(() =>
    {
        if (!sources.Visible) throw new ArgumentException("Open the Source files tab, then highlight one source to inspect.");
        int[] indices = sources.SelectedIndices; if (indices.Length != 1) throw new ArgumentException("Choose one source file in the Source files tab.");
        using var dialog = new SourceDialog(Workspace.Document.Sources[indices[0]]); dialog.ShowDialog(this);
    });
    private void EnterAnimals() => Guard(() =>
    {
        FlushDrafts(); using var entry = new AnimalEntryDialog(Workspace.Inputs.Study.Demonstration);
        if (entry.ShowDialog(this) != DialogResult.OK) return;
        using var review = new ImportDialog(entry.Source!, true, Workspace.Inputs.Study.Demonstration);
        review.ConfigureEnteredAnimals();
        if (review.ShowDialog(this) != DialogResult.OK) return;
        Workspace.ImportAutomaticAnimalPool(review.Source, review.AnimalBatch!, review.Append, "Enter and review animal observations");
        Reload(); SetStage(1); Notify("Animal entries saved. Review and commit your selection.");
    });
    private void Import(bool animal) => Guard(() =>
    {
        FlushDrafts(); using var open = new OpenFileDialog { Filter = "Animal/treatment tables|*.csv;*.tsv;*.xlsx;*.xlsm", Title = animal ? "Choose animal source" : "Choose treatment source" };
        if (open.ShowDialog(this) != DialogResult.OK) return;
        using var dialog = new ImportDialog(DataImport.Read(open.FileName), animal, Workspace.Inputs.Study.Demonstration);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (animal) Workspace.ImportAutomaticAnimalPool(dialog.Source, dialog.AnimalBatch!, dialog.Append, dialog.Reason);
        else Workspace.ImportTreatments(dialog.Source, dialog.TreatmentBatch!, dialog.Reason);
        Reload(); SetStage(animal ? 1 : 0); Notify(animal ? "Animal pool imported and automatically selected" : "Treatment table imported"); status.Text = animal ? "Heaviest animals selected automatically. Unused animals will be EX." : "Ordered treatment table imported with source provenance.";
    });

    private void Reload()
    {
        if (inOperation) return;
        CaptureSelection(); var preserved = selectionDirty ? new Dictionary<string, SelectionDecision>(selection) : null;
        loading = true;
        try
        {
            var input = Workspace.Inputs; var study = input.Study;
            design.SelectedItem = design.Items.Cast<MethodOption>().Single(x => x.Code == study.Design); unit.SelectedItem = input.ExperimentalUnit;
            replicates.Value = study.Replicates; perPen.Value = study.AnimalsPerPen;
            treatmentTable = new DataTable(); treatmentTable.Columns.Add("Treatment ID"); treatmentTable.Columns.Add("Label");
            foreach (var t in study.Treatments) treatmentTable.Rows.Add(t.Id, t.Label); treatments.DataSource = treatmentTable;
            if (treatments.Columns.Contains("Order")) treatments.Columns.Remove("Order");
            treatments.Columns.Insert(0, new DataGridViewTextBoxColumn { Name = "Order", HeaderText = "Order", ReadOnly = true, Width = 85, MinimumWidth = 85, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            foreach (DataGridViewRow row in treatments.Rows) if (!row.IsNewRow) row.Cells["Order"].Value = row.Index + 1;
            foreach (DataGridViewColumn column in treatments.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            selection.Clear(); foreach (var decision in input.Selection) selection[decision.RecordId] = preserved?.GetValueOrDefault(decision.RecordId) ?? decision;
            checkedAnimals.RemoveWhere(id => !selection.ContainsKey(id));
            var document = Workspace.Document;
            string? previousChosen = presentedActiveResultId == document.ActiveResultId && chosen?.ProjectId == document.Id ? chosen.ResultId : null;
            presentedActiveResultId = document.ActiveResultId;
            revisions.Items.Clear(); foreach (var e in document.Events.Where(x => x.Result is not null))
                revisions.Items.Add(new RevisionOption(e.Id, $"Result {e.Result!.Revision} · {e.CreatedUtc.ToLocalTime():dd MMM HH:mm} · {(e.Id == document.ActiveResultId ? "Active" : "Historical")}"));
            if (document.ActiveResultId is not null) revisions.SelectedItem = revisions.Items.Cast<RevisionOption>().Single(x => x.Id == document.ActiveResultId);
            history.SetRows(["Event", "Time", "Action", "Outcome", "Reason", "Input revision", "Result revision", "Diagnostics"], document.Events.Length, i =>
            {
                var e = document.Events[i]; return [e.Number, e.CreatedUtc.ToLocalTime().ToString("g"), e.Action, e.Status?.ToString() ?? "Applied", e.Reason,
                    document.Inputs.Single(x => x.Id == e.InputRevisionId).Number, e.Result?.Revision, string.Join(" ", e.Errors) + " " + string.Join("; ", e.Attempts.Select(t => $"Block {t.Block}: {t.Attempts} attempts; best CV {t.BestCvPercent:G6}; last {t.LastCvPercent:G6}"))];
            });
            inputHistory.SetRows(["Input", "Time", "Reason", "Pool", "Included", "Full-input digest"], document.Inputs.Length, i =>
            {
                var r = document.Inputs[i]; return [r.Number, r.CreatedUtc.ToLocalTime().ToString("g"), r.Reason, r.Inputs.Study.Pool.Length, r.Inputs.Study.SelectedRecordIds.Length, r.FullInputDigest];
            });
            sources.SetRows(["Filename", "Original path", "Captured", "SHA-256", "Tables"], document.Sources.Length, i =>
            {
                var s = document.Sources[i]; return [s.FileName, s.OriginalPath, s.CapturedUtc.ToLocalTime().ToString("g"), s.Sha256, string.Join(", ", s.Tables.Select(x => x.Name))];
            });
            counts.Text = $"{study.Pool.Length:N0}\nanimals in source pool\n\n{study.SelectedRecordIds.Length:N0}\nexplicitly selected\n\n{(long)study.Treatments.Length * study.Replicates:N0}\n{input.ExperimentalUnit}s required";
            if (document.ActiveResultId is not null) ShowChosen(Workspace.Choose(document.ActiveResultId));
            else
            {
                chosen = null; resultLabel.Text = "No completed allocation yet. Prior results appear here only after a successful run.";
                foreach (var grid in new[] { movement, pens, balance, blocks, edits, maps }) grid.SetRows([], 0, _ => []);
            }
            if (chosen is null && input.PreparedLayout is not null)
            {
                resultLabel.Text = "Layout prepared · Animal allocation pending";
                var tabs = allocationViews.Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = tabs.TabPages.Count - 1;
            }
            if (previousChosen is not null && document.Events.Any(e => e.Id == previousChosen && e.Result is not null))
            {
                revisions.SelectedItem = revisions.Items.Cast<RevisionOption>().Single(x => x.Id == previousChosen); ShowChosen(Workspace.Choose(previousChosen));
            }
        }
        finally { loading = false; }
        PopulateFilters(); Capabilities(); FilterAnimals(); UpdateTitle();
    }
    private void SetStage(int index)
    {
        stage = index; for (int i = 0; i < stages.Length; i++) { stages[i].Visible = i == index; ((StyledButton)navigation.Controls[i]).Primary = i == index; navigation.Controls[i].Invalidate(); }
        title.Text = new[] { "Study configuration", "Animal selection", "Allocation", "Project history" }[index];
        subtitle.Text = new[] { "Set the experimental unit and ordered treatments. Every allocation follows these settings.",
            "Review the complete pool and make an explicit choice for each record. Body weight is in grams.",
            "Use a recorded seed, review BW balance, and keep every successful revision.", "Trace source observations, input changes, allocation attempts, and manual edits." }[index];
        guideTitle.Text = new[] { "A clear starting point", "Your selection matters", "Reproducible by design", "History stays intact" }[index];
        guide.Text = new[] { "BW is in grams. The pen or cage is the experimental unit.", "Highlight the exact records you want to include or exclude. Pending choices prevent allocation.",
            "Failure or cancellation retains the previous completed result. All views use the chosen revision.", "Sources, inputs, failed attempts and edits remain recoverable from saved checkpoints." }[index];
        RefreshPresentation();
    }
    private void UpdateTitle()
    {
        Text = $"{Workspace.Document.Name}{(Workspace.IsDirty || setupDirty || selectionDirty ? " *" : "")} — Experiment Designer";
        status.Text = Workspace.Inputs.Study.Demonstration ? "Demonstration study" : "";
        Components.Tip(status, Workspace.FilePath ?? "This study has no saved filename yet.");
        RefreshPresentation();
    }
    private void FlushDrafts() { if (setupDirty) ApplySetup(); if (selectionDirty) SaveSelection(false); }
    private static string Required(string text, string message) => !string.IsNullOrWhiteSpace(text) ? text.Trim() : throw new ArgumentException(message);
    private void Guard(Action action)
    {
        if (inOperation) return; try { action(); }
        catch (OperationCanceledException) { status.Text = "Change cancelled; pending edits retained."; }
        catch (Exception ex) { pageAlert.Text = ex.Message; pageAlert.Visible = true; pageAlert.ForeColor = Theme.Red; status.Text = ex.Message; }
    }
    private async Task<bool> OperationAsync(Func<CancellationToken, Task> action)
    {
        if (inOperation) return false;
        inOperation = true; operation = new(); body.Enabled = navigation.Enabled = false;
        progress.Visible = true; RefreshPresentation();
        foreach (Control control in files.Controls) control.Enabled = ReferenceEquals(control, cancel); cancel.Visible = true;
        status.Text = "Working… you can cancel this operation.";
        string message = ""; bool success = false;
        try { await action(operation.Token); success = true; }
        catch (OperationCanceledException) { message = "Operation cancelled. Previous completed work retained."; }
        catch (Exception ex) { message = ex.Message; }
        finally
        {
            operation.Dispose(); operation = null; inOperation = false; body.Enabled = navigation.Enabled = true;
            foreach (Control control in files.Controls) control.Enabled = true; cancel.Visible = false; progress.Visible = false; Reload(); if (message.Length > 0) { status.Text = message; pageAlert.Text = message; pageAlert.Visible = true; pageAlert.ForeColor = Theme.Red; }
        }
        return success;
    }
    private async Task RunAsync()
    {
        ulong value = 0; string reason = ""; bool valid = false;
        ImmutableArray<PhysicalPen> housing = [];
        ulong housingSeed = 0;
        Guard(() =>
        {
            FlushDrafts(); value = Ui.Seed(seed.Text); reason = "Generate allocation";
            if (!Workspace.IsReady) throw new ArgumentException(Workspace.Inputs.AutomaticPoolSelection ? "Complete valid settings and import enough animals to fill the study." : "Complete valid settings and commit an exact selection first.");
            var plan = Workspace.Inputs.PreparedLayout;
            if (plan is null)
            {
                plan = HousingLayouts.Create(Workspace.Inputs, (int)firstHousing.Value, (int)lastHousing.Value, value);
                Workspace.Revise(Workspace.Inputs with { PreparedLayout = plan }, "Prepare treatment and housing layout");
            }
            if (!HousingLayouts.Matches(plan, Workspace.Inputs) || plan.First != firstHousing.Value || plan.Last != lastHousing.Value)
                throw new ArgumentException("Study settings or housing range changed. Prepare a new layout before allocation.");
            housing = plan.Pens; housingSeed = plan.Seed; valid = true;
        });
        if (!valid) return;
        await OperationAsync(async token =>
        {
            var progress = new Progress<(int Block, int Attempt)>(p => { if (inOperation) status.Text = $"Allocating block {p.Block} • Attempt {p.Attempt} • Cancel remains available"; });
            var result = await Workspace.RunAsync(value, reason, token, progress);
            if (result.Status == RunStatus.Success) Workspace.Map(housing, housingSeed, "Apply prepared housing layout", Workspace.Document.ActiveResultId);
            lastRunOutcome = result; lastRunProject = Workspace.Document.Id;
            BeginInvoke(() =>
            {
                status.Text = $"Run {result.Status}. " + string.Join(" ", result.Errors);
                if (result.Status != RunStatus.Success) { allocationFeedback.Text = $"Run {result.Status}: " + string.Join(" ", result.Errors) + " Previous completed work retained."; allocationFeedback.ForeColor = Theme.Warning; pageAlert.Text = allocationFeedback.Text; }
                else Notify("Allocation completed and recorded");
            });
        });
        SetStage(2);
    }
    private async Task<bool> SaveAsync(bool saveAs = false)
    {
        bool valid = false; Guard(() => { FlushDrafts(); valid = true; }); if (!valid) return false;
        string? path = Workspace.FilePath;
        if (saveAs || path is null || string.Equals(Path.GetExtension(path), ProjectStore.LegacyExtension, StringComparison.OrdinalIgnoreCase))
        {
            string filename = new(Workspace.Document.Name.Take(100).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            using var save = new SaveFileDialog
            {
                Filter = "Experiment Designer project|*" + ProjectStore.ProjectExtension,
                DefaultExt = ProjectStore.ProjectExtension.TrimStart('.'),
                AddExtension = true,
                InitialDirectory = path is null ? "" : Path.GetDirectoryName(path),
                FileName = (path is null ? filename : Path.GetFileNameWithoutExtension(path)) + ProjectStore.ProjectExtension
            };
            if (save.ShowDialog(this) != DialogResult.OK) return false; path = save.FileName;
        }
        saving = true; saveFailed = false; RefreshPresentation();
        bool success = await OperationAsync(token => Workspace.SaveAsync(path, token)); saving = false; saveFailed = !success; RefreshPresentation();
        if (success) Notify("Project saved"); return success && !Workspace.IsDirty;
    }
    private async Task<bool> MayDiscardAsync()
    {
        if (!Workspace.IsDirty && !setupDirty && !selectionDirty) return true;
        var choice = MessageBox.Show(this, "Save your current project and pending edits before continuing?", "Unsaved work", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return false; if (choice == DialogResult.No) return true; return await SaveAsync();
    }
    private async Task NewAsync()
    {
        if (inOperation || !await MayDiscardAsync()) return;
        string? name = Ui.Ask(this, "New study", "Study name"); if (name is null) return;
        Guard(() => { Workspace.New(name, true); setupDirty = selectionDirty = false; setupReason.Clear(); selectionReason.Clear(); Reload(); SetStage(0); });
    }
    private async Task OpenAsync(bool recover)
    {
        if (inOperation || !await MayDiscardAsync()) return;
        using var open = new OpenFileDialog { Filter = "Experiment Designer projects|*.experimentproj;*.poultryproj|Experiment Designer project|*.experimentproj|Legacy projects|*.poultryproj", Title = recover ? "Choose the main project whose .bak file should be recovered" : "Open project" };
        if (open.ShowDialog(this) != DialogResult.OK) return;
        var previous = Workspace.Document;
        await OperationAsync(token => Workspace.OpenAsync(open.FileName, true, token, recover));
        if (!ReferenceEquals(previous, Workspace.Document)) { setupDirty = selectionDirty = false; setupReason.Clear(); selectionReason.Clear(); Reload(); }
    }

    internal void LoadForSmoke(string path) { Workspace.OpenAsync(path).GetAwaiter().GetResult(); setupDirty = selectionDirty = false; Reload(); }
    internal void StageForSmoke(int index) => SetStage(index);
    internal void CloseForSmoke() { allowClose = true; Close(); }
    internal void ExerciseForSmoke(Action<bool, string> check)
    {
        var before = Workspace.Document;
        design.SelectedItem = design.Items.Cast<MethodOption>().Single(x => x.Code == Design.Crd);
        check(method.Text.Contains("body-weight summaries"), "CRD explains review without exposing optimization settings");
        design.SelectedItem = design.Items.Cast<MethodOption>().Single(x => x.Code == Design.Rcbd);
        check(method.Text.Contains("grouped by body weight"), "RCBD describes the built-in allocation workflow");
        setupDirty = false; Reload();
        check(ReferenceEquals(before, Workspace.Document), "Unapplied setup controls cannot mutate the project");
        SetStage(1); Application.DoEvents();
        search.Text = "B048"; Application.DoEvents();
        var matchingRow = animals.Rows.Cast<DataGridViewRow>().Single(x => Convert.ToString(x.Cells["Animal ID"].Value) == "B048");
        check(animals.Rows.Count == filteredAnimals.Length && filteredAnimals.All(x => x.AnimalId.Contains("B048", StringComparison.OrdinalIgnoreCase) || x.RecordId.Contains("B048", StringComparison.OrdinalIgnoreCase)), "Animal search returns matching IDs and preserves exact record binding");
        var id = Convert.ToString(matchingRow.Cells["Record ID"].Value)!;
        matchingRow.Cells["Decision"].Value = "Exclude"; matchingRow.Cells["Reason"].Value = "Synthetic researcher exclusion"; CaptureSelection();
        check(selection[id].Choice == SelectionChoice.Exclude && Workspace.Inputs.Selection.Single(x => x.RecordId == id).Choice == SelectionChoice.Include,
            "Selection edits are staged with immutable ID and cannot silently change allocation inputs");
        selectionDirty = false; search.Clear(); Reload();
        check(chosen is not null && chosen.ResultId == Workspace.Document.ActiveResultId, "Every result page starts from the active immutable snapshot");
        SetStage(2); PerformLayout();
        var reportBar = stages[2].Controls.Cast<Control>().Single(x => x.Dock == DockStyle.Bottom);
        check(reportBar.Visible && reportBar.Height > 20 && reportBar.Bottom <= stages[2].ClientSize.Height,
            "Chosen revision export action is visible inside the allocation stage");
    }
}
