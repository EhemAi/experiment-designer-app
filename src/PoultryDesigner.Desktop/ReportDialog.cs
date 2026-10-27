using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed class ReportDialog : Form
{
    private AllocationReport report;
    private readonly string[] protectedPaths;
    private readonly Label message = Ui.Label("Export or print the chosen revision. All records are included.");
    private readonly FlowLayoutPanel actions;
    private readonly ComboBox order;
    private readonly Label reportHeading;
    private bool working;
    internal ReportDialog(ChosenRevision chosen, bool historical, bool earlierInputs, string? projectPath)
        : this(AllocationReports.Create(chosen, historical, earlierInputs), chosen.Sources.Select(x => x.OriginalPath).Append(projectPath ?? "").ToArray()) { }
    internal ReportDialog(AllocationReport snapshot, string[] sourcePaths)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        report = snapshot;
        protectedPaths = sourcePaths.Where(x => x.Length > 0).Select(Path.GetFullPath).ToArray();
        Text = "Reports - chosen allocation"; Font = Theme.Body(); StartPosition = FormStartPosition.CenterParent; ClientSize = new(960, 470); MinimumSize = new(860, 470);
        var heading = reportHeading = Ui.Label("Allocation reports"); heading.Font = Theme.Heading(18); heading.Name = "report-heading";
        heading.Margin = Padding.Empty; heading.Padding = new(4, 6, 4, 6);
        var context = Ui.Label(report.Context);
        var detail = Ui.Label("Excel: summary, movement, unit BW with treatment ANOVA, treatment balance, physical map and swap log.\nPrint: movement, summaries and ANOVA. PDF uses Microsoft Print to PDF.\nExports are snapshots. Save pending project changes separately.");
        actions = Ui.Bar(Ui.Button("Export Excel", () => _ = ExportAsync(true)), Ui.Button("Export printable HTML", () => _ = ExportAsync(false)),
            Ui.Button("Print preview", () => Print(true)), Ui.Button("Print / PDF", () => Print(false)));
        ((StyledButton)actions.Controls[0]).Primary = true;
        var close = Ui.Button("Close", Close);
        order = Ui.Combo(["Treatment / unit", "Old location", "New location"]); order.Width = 270; order.SelectedIndex = 0;
        var original = report;
        bool prepared = !report.Tables.Any(t => t.Name == "Movement");
        if (prepared)
        {
            order.Items.Clear(); order.Items.Add("All preparation sheets"); order.Items.AddRange(original.Tables.Select(t => (object)t.Name).ToArray()); order.SelectedIndex = 0;
            heading.Text = "Prepared layout"; detail.Text = "Choose the layout, blank allotment sheets or housing labels to export or print. Animal IDs and body weights will be filled on allocation day. Save the project to retain these treatment locations."; message.Text = "No animals have been allocated in these preparation sheets.";
        }
        order.SelectedIndexChanged += (_, _) => report = prepared ? order.SelectedIndex == 0 ? original
            : original with { Tables = [original.Tables[order.SelectedIndex - 1]] } : AllocationReports.SortMovement(original, (MovementOrder)order.SelectedIndex);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new(22, 10, 22, 10)
        };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(heading, 0, 0); layout.Controls.Add(context, 0, 1); layout.Controls.Add(detail, 0, 2);
        layout.Controls.Add(Ui.Bar(Ui.Label(prepared ? "Print / export" : "Movement order"), order), 0, 3); layout.Controls.Add(actions, 0, 4);
        layout.Controls.Add(message, 0, 5);
        foreach (var label in new[] { heading, context, detail, message }) { label.AutoSize = true; label.Dock = DockStyle.Top; }
        layout.SizeChanged += (_, _) =>
        {
            int width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal - 8);
            foreach (var label in new[] { heading, context, detail, message }) label.MaximumSize = new(width, 0);
        };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(layout);
        var footer = Ui.Bar(close); footer.Dock = DockStyle.Bottom; footer.Padding = new(22, 4, 22, 10);
        Controls.Add(scroll); Controls.Add(footer); Theme.Apply(this);
        FormClosing += (_, e) => { if (working) e.Cancel = true; };
    }
    private async Task ExportAsync(bool excel)
    {
        if (working) return;
        string safe = string.Concat(report.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)); if (safe.Length > 90) safe = safe[..90];
        using var dialog = new SaveFileDialog
        {
            Filter = excel ? "Excel workbook|*.xlsx" : "Printable report|*.html",
            DefaultExt = excel ? "xlsx" : "html",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = safe + (report.Context.StartsWith("Prepared layout") ? " - prepared layout" : " - chosen allocation")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string path = Path.GetFullPath(dialog.FileName);
        if (protectedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) { message.Text = "Choose a new report file. Original sources and project files cannot be overwritten by exports."; return; }
        working = true; actions.Enabled = false; message.Text = "Writing complete chosen-revision export...";
        try { await Task.Run(() => { if (excel) ReportExport.Excel(report, path, true); else ReportExport.Html(report, path, true, Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts")); }); message.Text = "Saved: " + path; }
        catch (Exception ex) { message.Text = "Export failed: " + ex.Message; }
        finally { working = false; actions.Enabled = true; }
    }
    private void Print(bool preview)
    {
        if (working) return;
        try
        {
            using var renderer = new ReportPrinting(report); using var document = renderer.Document();
            if (preview) { using var dialog = Preview(document); dialog.ShowDialog(this); }
            else
            {
                using var dialog = new PrintDialog { Document = document, UseEXDialog = true, AllowSomePages = false };
                if (dialog.ShowDialog(this) == DialogResult.OK) document.Print();
            }
        }
        catch (Exception ex) { message.Text = "Printing unavailable: " + ex.Message + ". You can export printable HTML and print it in a browser."; }
    }
    internal AllocationReport ReportForSmoke => report;
    internal ComboBox ChoiceForSmoke => order;
    internal Label HeadingForSmoke => reportHeading;
    internal static PrintPreviewDialog Preview(System.Drawing.Printing.PrintDocument document)
    {
        var dialog = new PrintPreviewDialog
        {
            Document = document,
            Width = 1200,
            Height = 850,
            MinimumSize = new(800, 600),
            StartPosition = FormStartPosition.CenterParent,
            UseAntiAlias = true
        };
        dialog.PrintPreviewControl.Rows = 1; dialog.PrintPreviewControl.Columns = 1; dialog.PrintPreviewControl.AutoZoom = true;
        dialog.Shown += (_, _) => { dialog.PrintPreviewControl.Rows = 1; dialog.PrintPreviewControl.Columns = 1; dialog.PrintPreviewControl.AutoZoom = true; };
        return dialog;
    }
}
