using System.Collections.Immutable;
using System.Data;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private readonly Label projectIdentity = Components.Message("Untitled study", Theme.Text);
    private readonly Label saveState = Components.Message("Not saved to file");
    private readonly Label workflow = Components.Message("");
    private readonly Label pageAlert = Components.Message("");
    private readonly Label allocationFeedback = Components.Message("");
    private readonly Label auditTimeline = Components.Message("");
    private readonly StatStrip animalStats = new(), allocationStats = new(), qualityStats = new();
    private readonly ScientificPlot treatmentPlot = new();
    private readonly ComboBox decisionFilter = Ui.Combo(["All decisions", "Include", "Exclude", "Pending"]);
    private readonly ComboBox cohortFilter = Ui.Combo([]), locationFilter = Ui.Combo([]), sourceFilter = Ui.Combo([]);
    private Panel animalEmpty = null!, allocationEmpty = null!, allocationViews = null!;
    private FlowLayoutPanel decisionActions = null!;
    private Button generate = null!, commitSelection = null!, usePool = null!, exportReport = null!;
    private Button activateRevision = null!, previewSwap = null!, mapUnits = null!;
    private readonly Label selectedRows = Ui.Label("");
    private bool saveFailed, saving;
    private readonly StyledButton appearance = new() { AutoSize = true, MinimumSize = new(112, 34), Margin = new(4, 2, 4, 2) };
    private readonly System.Windows.Forms.Timer toastTimer = new() { Interval = 4500 };
    private readonly Label toast = Components.Message("", Theme.Success);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Width = 100, Height = 8, Visible = false };
    private readonly Pagination animalPagination = new();
    private int animalRowsPerPage = PagedGrid.PageSize;
    private Panel animalNoResults = null!, plotCard = null!;
    private readonly HashSet<string> checkedAnimals = [];
    private RunResult? lastRunOutcome;
    private string? lastRunProject;
    private readonly Label treatmentHeading = Ui.Label("");
    private readonly ErrorProvider fieldErrors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly CheckBox animalDetails = new() { Text = "Show details", AutoSize = true };

    private void BuildShell()
    {
        ClientSize = new(1440, 900); MinimumSize = new(1200, 760); Padding = new(12);
        cancel = Ui.Button("Cancel operation", () => operation?.Cancel()); cancel.Visible = false;
        var file = Ui.Button("File ▾", () => { }); ((StyledButton)file).Variant = ButtonVariant.Ghost;
        var menu = new ContextMenuStrip { BackColor = Theme.Pale, ForeColor = Theme.Text, Font = Theme.Body() };
        file.ContextMenuStrip = menu;
        menu.Items.Add("New study  Ctrl+N", null, (_, _) => _ = NewAsync());
        menu.Items.Add("Open  Ctrl+O", null, (_, _) => _ = OpenAsync(false)); menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Save as", null, (_, _) => _ = SaveAsync(true)); menu.Items.Add("Recover backup", null, (_, _) => _ = OpenAsync(true));
        file.Click += (_, _) => menu.Show(file, new Point(0, file.Height));
        var save = Ui.Button("Save", () => _ = SaveAsync()); ((StyledButton)save).Primary = true;
        saveState.AutoSize = true; saveState.MinimumSize = new(0, 36); saveState.Dock = DockStyle.None;
        appearance.Text = Theme.IsDark ? "Light mode" : "Dark mode"; appearance.AccessibleName = "Switch color theme";
        appearance.Click += (_, _) => ToggleAppearance();
        files = Ui.Bar(file, save, appearance, saveState, progress, cancel); files.WrapContents = false; files.Dock = DockStyle.Fill;
        projectIdentity.AutoEllipsis = true; projectIdentity.Font = Theme.Body(9); projectIdentity.Height = 44; projectIdentity.Dock = DockStyle.Top;
        navigation = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 204, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        for (int i = 0; i < 4; i++)
        {
            int index = i; var button = (StyledButton)Ui.Button(new[] { "1  Study", "2  Animals", "3  Allocation", "4  History" }[i], () => SetStage(index));
            button.Navigation = true; button.AutoSize = false; button.Size = new(172, 38); navigation.Controls.Add(button);
        }
        var brand = new TableLayoutPanel { Height = 76, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Padding = new(4, 8, 0, 8) };
        brand.ColumnStyles.Add(new(SizeType.Absolute, 40)); brand.ColumnStyles.Add(new(SizeType.Percent, 100));
        var brandName = Components.Message("Experiment\nDesigner", Theme.Text); brandName.Font = Theme.Heading(10.5f); brandName.Padding = new(0); brandName.Dock = DockStyle.Fill;
        var brandLogo = new PictureBox { Image = Brand.Logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new(0, 16, 8, 16) };
        brand.Controls.Add(brandLogo, 0, 0); brand.Controls.Add(brandName, 1, 0);
        var sidebarTop = new Panel { Dock = DockStyle.Top, Height = 324 }; sidebarTop.Controls.Add(navigation); sidebarTop.Controls.Add(projectIdentity); sidebarTop.Controls.Add(brand);
        counts.Dock = DockStyle.Fill; counts.AutoSize = false; counts.Font = Theme.Body(9); counts.Padding = new(8); counts.ForeColor = Theme.Muted;
        var studyStatus = Components.Card("Study status", counts); studyStatus.Dock = DockStyle.Bottom; studyStatus.Height = 132;
        var helpButton = Ui.Button("User guide", OpenGuide);
        var help = Ui.Bar(helpButton); help.Dock = DockStyle.Bottom;
        var sidebar = new RepaintPanel { Dock = DockStyle.Fill, Padding = new(4), BackColor = Theme.Navy, Tag = "sidebar" };
        sidebar.Controls.Add(studyStatus); sidebar.Controls.Add(help); sidebar.Controls.Add(sidebarTop);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new(16, 0, 0, 0) };
        main.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) main.RowStyles.Add(new(SizeType.AutoSize));
        main.RowStyles.Add(new(SizeType.Percent, 100)); main.RowStyles.Add(new(SizeType.AutoSize));
        title.Font = Theme.Heading(18); title.AutoSize = false; title.Dock = DockStyle.Fill; title.TextAlign = ContentAlignment.MiddleLeft;
        subtitle.Dock = DockStyle.Fill; subtitle.AutoSize = false; subtitle.ForeColor = Theme.Muted;
        subtitle.Visible = false;
        title.MinimumSize = new(0, 42); subtitle.MinimumSize = new(0, 28);
        workflow.MinimumSize = new(0, 30); pageAlert.MinimumSize = new(0, 30);
        toast.MinimumSize = new(0, 32);
        foreach (var label in new[] { workflow, pageAlert, subtitle, toast }) { label.AutoSize = true; label.Dock = DockStyle.Top; }
        main.SizeChanged += (_, _) =>
        {
            foreach (var label in new[] { workflow, pageAlert, subtitle, toast }) label.MaximumSize = new(Math.Max(1, main.ClientSize.Width - main.Padding.Horizontal - 8), 0);
        };
        pageAlert.Font = Theme.Body(9); pageAlert.Padding = new(4); workflow.Font = Theme.Body(9, FontStyle.Bold);
        toast.Visible = false;
        toastTimer.Tick += (_, _) => { toast.Text = ""; toast.Visible = false; toastTimer.Stop(); }; Disposed += (_, _) => toastTimer.Dispose();
        foreach (var panel in stages) body.Controls.Add(panel);
        main.Controls.Add(files, 0, 0); main.Controls.Add(workflow, 0, 1); main.Controls.Add(title, 0, 2); main.Controls.Add(subtitle, 0, 3);
        main.Controls.Add(pageAlert, 0, 4); main.Controls.Add(body, 0, 5); main.Controls.Add(toast, 0, 6);
        foreach (Control child in main.Controls) child.Margin = Padding.Empty;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new(SizeType.Absolute, 194)); root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 32));
        status.AutoSize = false; status.Dock = DockStyle.Fill; status.Font = Theme.Body(9); status.ForeColor = Theme.Muted;
        root.Controls.Add(sidebar, 0, 0); root.Controls.Add(main, 1, 0); root.Controls.Add(status, 1, 1); Controls.Add(root);
        KeyPreview = true; KeyDown += (_, e) =>
        {
            if (!e.Control || inOperation) return;
            if (e.KeyCode == Keys.S) { e.SuppressKeyPress = true; _ = SaveAsync(); }
            if (e.KeyCode == Keys.O) { e.SuppressKeyPress = true; _ = OpenAsync(false); }
            if (e.KeyCode == Keys.N) { e.SuppressKeyPress = true; _ = NewAsync(); }
        };
        fieldErrors.ContainerControl = this;
        Disposed += (_, _) => fieldErrors.Dispose();
        Components.Tip(workflow, "Complete valid settings and exact selection, generate an allocation, then review/export its recorded revision. History is available separately.");
        Shown += (_, _) =>
        {
            title.MinimumSize = new(0, title.PreferredSize.Height);
            subtitle.MinimumSize = new(0, subtitle.PreferredSize.Height);
            brand.Height = Math.Max(brand.Height, brand.PreferredSize.Height);
            PerformLayout();
        };
    }

    private void ToggleAppearance(bool persist = true)
    {
        Theme.SetDark(!Theme.IsDark, this);
        appearance.Text = Theme.IsDark ? "Light mode" : "Dark mode";
        RefreshPresentation(); Invalidate(true);
        if (persist)
        {
            try { AppearancePreferences.Save(Theme.IsDark); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                status.Text = "Theme changed for this session. Preference could not be saved: " + ex.Message;
            }
        }
    }

    private string AuditReason(TextBox field, string caption, string explanation)
    {
        if (string.IsNullOrWhiteSpace(field.Text))
        {
            field.Text = caption;
        }
        return Required(field.Text, "An audit reason is required.");
    }
    private void Notify(string message) { toast.Text = message; toast.Visible = true; toastTimer.Stop(); toastTimer.Start(); }
    private void RefreshPresentation()
    {
        if (generate is null) return;
        var s = Workspace.Inputs.Study; bool setup = PresentationState.SetupValid(s) && !setupDirty;
        bool ready = Workspace.IsReady && !setupDirty && !selectionDirty;
        bool current = Workspace.Document.ActiveResultId is not null && !Workspace.HasEarlierInputRevision && !Workspace.IsStale;
        string shortName = Workspace.Document.Name.Length > 22 ? Workspace.Document.Name[..19] + "…" : Workspace.Document.Name;
        projectIdentity.Text = shortName; Components.Tip(projectIdentity, Workspace.Document.Name + "\nProject ID: " + Workspace.Document.Id);
        counts.Text = $"{s.Pool.Length:N0}  Source animals\n{s.SelectedRecordIds.Length:N0}  Selected\n{(long)s.Treatments.Length * s.Replicates:N0}  {Workspace.Inputs.ExperimentalUnit}s required";
        saveState.Text = PresentationState.SaveState(saving, saveFailed, Workspace.IsDirty || setupDirty || selectionDirty, Workspace.FilePath);
        saveState.ForeColor = saveFailed ? Theme.Red : Workspace.IsDirty || setupDirty || selectionDirty ? Theme.Warning : Theme.Muted;
        int currentStep = stage == 2 && chosen is not null ? 3 : stage == 3 ? -1 : stage;
        string Marker(int step) => currentStep == step ? "▶ " : "";
        workflow.Text = $"{Marker(0)}1 Study    ›    {Marker(1)}2 Animals    ›    {Marker(2)}3 Allocation    ›    {Marker(3)}4 Review / Export";
        bool housingValid = RefreshHousingControls();
        generate.Enabled = ready && housingValid && !inOperation; exportReport.Enabled = chosen is not null && !inOperation;
        balanceNoticePanel.Visible = chosen is not null;
        repeatAllocation.Enabled = generate.Enabled && chosen?.ResultId == Workspace.Document.ActiveResultId && current;
        allocationRevisionBar.Visible = allocationExportBar.Visible = chosen is not null;
        int resultTab = allocationViews.Controls.OfType<TabControl>().Single().SelectedIndex;
        allocationRunBar.Visible = allocationHousingBar.Visible = chosen is null || resultTab is 0 or 7;
        bool canEdit = chosen is not null && chosen.ResultId == Workspace.Document.ActiveResultId && current && !setupDirty && !selectionDirty && !inOperation;
        previewSwap.Enabled = mapUnits.Enabled = canEdit;
        activateRevision.Enabled = chosen is not null && chosen.ResultId != Workspace.Document.ActiveResultId && !inOperation;
        string editHelp = canEdit ? "Preview changes to the active allocation using its current committed inputs." : "Choose the active result using current committed inputs before editing or mapping. Historical output remains reviewable and exportable.";
        Components.Tip(previewSwap, editHelp); Components.Tip(mapUnits, editHelp + " Mapping requires scoped unique unit IDs, sufficient capacity and block eligibility.");
        long required = PresentationState.Required(s); int included = selection.Values.Count(x => x.Choice == SelectionChoice.Include), pending = selection.Values.Count(x => x.Choice == SelectionChoice.Pending);
        bool automatic = Workspace.Inputs.AutomaticPoolSelection;
        manualSelectionActions.Visible = !automatic;
        foreach (var button in decisionActions.Controls.OfType<Button>().Where(b => b.Text is "Include" or "Exclude")) button.Visible = !automatic;
        animals.Columns["Decision"]!.HeaderText = automatic ? "Status" : "Decision";
        commitSelection.Enabled = setup && included == required && pending == 0 && selection.Values.All(x => !string.IsNullOrWhiteSpace(x.Reason));
        usePool.Enabled = setup && s.Pool.Length == required;
        animalStats.ShowValues(("Source animals", s.Pool.Length.ToString("N0")), ("Required", required.ToString("N0")), ("Selected", included.ToString("N0")),
            automatic ? ("Extras (EX)", (s.Pool.Length - included).ToString("N0")) : ("Excluded / Pending", $"{selection.Count - included - pending:N0} / {pending:N0}"));
        var displayed = chosen?.Input.Inputs ?? Workspace.Inputs; var ds = displayed.Study;
        allocationStats.ShowValues((chosen is null ? "Selected animals" : "Result animals", ds.SelectedRecordIds.Length.ToString("N0")), ("Treatments", ds.Treatments.Length.ToString("N0")), ($"{displayed.ExperimentalUnit}s / treatment", ds.Replicates.ToString("N0")), ("Animals / unit", ds.AnimalsPerPen.ToString("N0")));
        animalEmpty.Visible = s.Pool.IsEmpty; animalNoResults.Visible = !s.Pool.IsEmpty && filteredAnimals.Length == 0; animals.Visible = !s.Pool.IsEmpty && filteredAnimals.Length > 0;
        allocationEmpty.Visible = chosen is null && Workspace.Inputs.PreparedLayout is null; allocationViews.Visible = chosen is not null || Workspace.Inputs.PreparedLayout is not null;
        pageAlert.ForeColor = stage == 0 && !setup || stage == 1 && !ready || stage == 2 && !ready ? Theme.Warning : Theme.Muted;
        pageAlert.Text = stage switch
        {
            0 => setupDirty ? "Apply your changes to continue." : setup ? "" : "Add at least two treatments and two replicates.",
            1 => automatic ? s.Pool.Length < required ? $"Import {required - s.Pool.Length:N0} more animals to fill the study." : $"Heaviest {included:N0} animals selected automatically; {s.Pool.Length - included:N0} extras (EX). Continue to Allocation."
                : !ready || selectionDirty ? PresentationState.SelectionMessage(s, selection.Values) + (selectionDirty ? " Save or commit your changes." : "") : "",
            2 => !ready ? setupDirty ? "Apply settings before allocation." : selectionDirty ? "Commit animal selection before allocation." : PresentationState.SelectionMessage(s, selection.Values) : Workspace.HasEarlierInputRevision ? "This result uses previous study settings." : "",
            _ => ""
        };
        if (stage == 2 && lastRunProject == Workspace.Document.Id && lastRunOutcome is { Status: not RunStatus.Success })
        {
            pageAlert.Text = $"Last run: {lastRunOutcome.Status}. " + string.Join(" ", lastRunOutcome.Errors) + " Previous completed work retained.";
            pageAlert.ForeColor = lastRunOutcome.Status == RunStatus.Invalid ? Theme.Red : Theme.Warning;
        }
        if (stage == 2 && ready && !housingValid) { pageAlert.Text = housingCount.Text + ". Adjust the range before generating."; pageAlert.ForeColor = Theme.Warning; }
        Components.Tip(generate, ready ? "Generate a new recorded allocation with the entered seed." : pageAlert.Text);
        Components.Tip(commitSelection, PresentationState.SelectionMessage(s, selection.Values));
        treatmentHeading.Text = $"Treatments · {treatmentTable.Rows.Cast<DataRow>().Count(x => x.RowState != DataRowState.Deleted)}";
        RenderDraftValidation();
        pageAlert.Visible = pageAlert.Text.Length > 0;
        var inputItems = Workspace.Document.Inputs.Select(x => (Time: x.CreatedUtc, Text: $"Input {x.Number} · {x.Reason}"));
        var eventItems = Workspace.Document.Events.Select(x => (Time: x.CreatedUtc, Text: $"Event {x.Number} · {x.Action} · {x.Status?.ToString() ?? "Applied"} · {x.Reason}"));
        auditTimeline.Text = string.Join("\n", inputItems.Concat(eventItems).OrderByDescending(x => x.Time).Take(4).Select(x => $"{x.Time.ToLocalTime():dd MMM HH:mm}  {x.Text}"));
    }
}
