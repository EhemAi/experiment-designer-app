using System.ComponentModel;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal static class Components
{
    internal static readonly ToolTip Help = new() { AutoPopDelay = 16000, InitialDelay = 450, ShowAlways = true };
    internal static Label Message(string text, Color? color = null) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        ForeColor = color ?? Theme.Muted,
        Padding = new(8),
        TextAlign = ContentAlignment.MiddleLeft,
        AccessibleName = text
    };
    internal static Panel Card(string heading, Control content)
    {
        var card = new RepaintPanel { Dock = DockStyle.Fill, Padding = new(12, 8, 12, 8), Margin = new(4), BackColor = Theme.Panel, Tag = "surface" };
        var label = Ui.Label(heading); label.Font = Theme.Heading(10.5f); label.AutoSize = false; label.Height = 30; label.Dock = DockStyle.Top; label.TextAlign = ContentAlignment.MiddleLeft;
        content.Dock = DockStyle.Fill; card.Controls.Add(content); card.Controls.Add(label);
        card.Paint += (_, e) => { if (card.Width < 2 || card.Height < 2) return; e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var pen = new Pen(Theme.Border); using var path = Theme.Round(new(0, 0, card.Width - 1, card.Height - 1), 7); e.Graphics.DrawPath(pen, path); };
        return card;
    }
    internal static TableLayoutPanel Fields(params (string Label, Control Control)[] fields)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = fields.Length, AutoScroll = false };
        table.ColumnStyles.Add(new(SizeType.Percent, 55)); table.ColumnStyles.Add(new(SizeType.Percent, 45));
        for (int i = 0; i < fields.Length; i++)
        {
            table.RowStyles.Add(new(SizeType.Absolute, fields[i].Control is TextBox { Multiline: true } ? 64 : 46));
            var label = Message(fields[i].Label); label.Padding = new(0, 4, 8, 4); label.Font = Theme.Body(9);
            fields[i].Control.Dock = DockStyle.Fill; fields[i].Control.Margin = new(4, 2, 4, 2);
            fields[i].Control.AccessibleName = fields[i].Label;
            table.Controls.Add(label, 0, i); table.Controls.Add(fields[i].Control, 1, i);
        }
        return table;
    }
    internal static Panel Empty(string title, string explanation, params Control[] actions)
    {
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new(16) };
        var heading = Ui.Label(title); heading.Font = Theme.Heading(15);
        var detail = Message(explanation); detail.Height = 62; detail.Dock = DockStyle.Top;
        content.Controls.Add(heading); content.Controls.Add(detail); content.Controls.Add(Ui.Bar(actions));
        var panel = new Panel { Dock = DockStyle.Fill }; panel.Controls.Add(content); return panel;
    }
    internal static void Tip(Control control, string text)
    {
        // Grid tracking tooltips compete with native cell hover/scroll handling.
        // Keep instructions accessible without creating a tooltip for the grid.
        if (control is DataGridView grid) { grid.ShowCellToolTips = false; grid.AccessibleDescription = text; return; }
        if (Help.GetToolTip(control) != text) Help.SetToolTip(control, text);
    }
}

// Border painting depends on the entire current bounds, so resizing must repaint
// the whole surface rather than leave previous border positions on screen.
internal sealed class RepaintPanel : Panel
{
    internal RepaintPanel() => SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
        ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
}

internal sealed class StatStrip : TableLayoutPanel
{
    private (string Label, string Value)[] displayed = [];
    internal StatStrip() { Dock = DockStyle.Top; Height = 88; Margin = new(0); RowCount = 1; RowStyles.Add(new(SizeType.Percent, 100)); }
    internal void ShowValues(params (string Label, string Value)[] values)
    {
        if (displayed.SequenceEqual(values)) return;
        if (displayed.Length == values.Length && displayed.Select(v => v.Label).SequenceEqual(values.Select(v => v.Label)))
        {
            for (int i = 0; i < values.Length; i++) GetControlFromPosition(i, 0)!.Controls["stat-value"]!.Text = values[i].Value;
            displayed = values.ToArray(); return;
        }
        SuspendLayout(); foreach (Control old in Controls.Cast<Control>().ToArray()) old.Dispose(); Controls.Clear(); ColumnStyles.Clear();
        ColumnCount = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            ColumnStyles.Add(new(SizeType.Percent, 100f / values.Length));
            var cell = new RepaintPanel { Dock = DockStyle.Fill, Padding = new(12, 6, 12, 6), BackColor = Theme.Panel, Margin = new(4) };
            cell.Paint += (_, e) => { if (cell.Width < 2 || cell.Height < 2) return; using var pen = new Pen(Theme.Border); using var path = Theme.Round(new(0, 0, cell.Width - 1, cell.Height - 1), 7); e.Graphics.DrawPath(pen, path); };
            var label = Components.Message(values[i].Label); label.Name = "stat-label"; label.Dock = DockStyle.Bottom; label.Padding = new(0); label.Font = Theme.Body(9);
            label.Height = Math.Max(24, (int)Math.Ceiling(label.Font.GetHeight(DeviceDpi)) + 4);
            var number = Components.Message(values[i].Value, Theme.Text); number.Name = "stat-value"; number.Padding = new(0); number.Font = Theme.Heading(14); number.Dock = DockStyle.Fill;
            cell.Controls.Add(number); cell.Controls.Add(label); Controls.Add(cell, i, 0);
        }
        displayed = values.ToArray(); ResumeLayout();
    }
}

internal sealed class ScientificPlot : Control
{
    private (string Label, decimal Value)[] values = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal (string Label, decimal Value)[] Values { get => values; set { values = value; Invalidate(); } }
    internal ScientificPlot() { DoubleBuffered = true; Dock = DockStyle.Fill; AccessibleName = "Mean initial body weight by treatment; exact values are in Treatment balance"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(Parent?.BackColor ?? Theme.Panel);
        if (values.Length == 0) return;
        using var font = Theme.Body(9); using var brush = new SolidBrush(Theme.Primary);
        decimal maximum = values.Max(x => x.Value); int height = Math.Min(44, Math.Max(24, (Height - 16) / values.Length));
        for (int i = 0; i < values.Length && (i + 1) * height <= Height; i++)
        {
            int y = 8 + i * height; int labelWidth = Math.Min(180, Width / 3), area = Math.Max(1, Width - labelWidth - 110);
            TextRenderer.DrawText(e.Graphics, values[i].Label, font, new Rectangle(8, y, labelWidth - 12, height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int barWidth = maximum == 0 ? 0 : (int)((values[i].Value / maximum) * area);
            if (barWidth > 0) { using var bar = Theme.Round(new(labelWidth, y + 8, barWidth, height - 16), 3); e.Graphics.FillPath(brush, bar); }
            TextRenderer.DrawText(e.Graphics, values[i].Value.ToString("0.###") + " g", font, new Rectangle(labelWidth + area + 8, y, 98, height), Theme.Text, TextFormatFlags.VerticalCenter);
        }
    }
}

internal static class PresentationState
{
    internal static long Required(Study study) => (long)study.Treatments.Length * study.Replicates * study.AnimalsPerPen;
    internal static bool SetupValid(Study s) => s.Treatments.Length >= 2 && s.Replicates >= 2 && s.AnimalsPerPen >= 1
        && Required(s) <= AllocationEngine.MaximumAnimals && s.Treatments.All(t => !string.IsNullOrWhiteSpace(t.Id) && !string.IsNullOrWhiteSpace(t.Label))
        && s.Treatments.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() == s.Treatments.Length
        && s.Treatments.Select(t => t.Label).Distinct(StringComparer.Ordinal).Count() == s.Treatments.Length
        && s.CvLimitPercent is >= 0 and <= 100 && s.MaximumAttemptsPerBlock is >= 1 and <= 1000000
        && (!s.FilterBalance || s.Design == Design.Rcbd);
    internal static string SelectionMessage(Study s, IEnumerable<SelectionDecision> decisions)
    {
        var rows = decisions.ToArray(); int included = rows.Count(x => x.Choice == SelectionChoice.Include), pending = rows.Count(x => x.Choice == SelectionChoice.Pending);
        int missingReasons = rows.Count(x => string.IsNullOrWhiteSpace(x.Reason));
        return $"{Required(s):N0} animals required; {included:N0} selected; {pending:N0} pending." + (missingReasons > 0 ? $" {missingReasons:N0} decisions need an audit reason." : "");
    }
    // Use the existing engine's eight-ULP boundary tolerance when describing its CV limits.
    internal static bool CvExceeds(double value, decimal limit)
    {
        if (limit == 0) return value != 0;
        double boundary = (double)limit;
        for (int i = 0; i < 8; i++) boundary = Math.BitIncrement(boundary);
        return value > boundary;
    }
    internal static string SaveState(bool saving, bool failed, bool dirty, string? path) => saving ? "Saving…" : failed ? "Save failed" : dirty ? "Unsaved changes" : path is null ? "Not saved to file" : "Saved";
}
