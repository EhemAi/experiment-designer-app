using System.Data;
using System.Globalization;

namespace PoultryDesigner.Desktop;

internal static class Ui
{
    internal static Color Ink => Theme.Text;
    internal static Color Accent => Theme.Teal;
    internal static Button Button(string text, Action action)
    {
        var button = new StyledButton
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new(72, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Canvas,
            ForeColor = Ink,
            Margin = new(4, 2, 4, 2),
            AccessibleName = text
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.Click += (_, _) => action(); return button;
    }
    internal static Label Label(string text) => new() { Text = text, AutoSize = true, ForeColor = Ink, Margin = new(4, 9, 4, 3) };
    internal static FlowLayoutPanel Bar(params Control[] controls)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new(2, 4, 2, 4), BackColor = Theme.Canvas };
        bar.Controls.AddRange(controls); return bar;
    }
    internal static DataGridView Grid(bool edit = false) => new()
    {
        Dock = DockStyle.Fill,
        BackgroundColor = Theme.Canvas,
        BorderStyle = BorderStyle.None,
        ReadOnly = !edit,
        AllowUserToAddRows = edit,
        AllowUserToDeleteRows = edit,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        AutoGenerateColumns = true,
        ShowCellToolTips = false,
        ColumnHeadersHeight = 40,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        RowTemplate = { Height = 38, MinimumHeight = 38 },
        GridColor = Theme.Border,
        EnableHeadersVisualStyles = false,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        ColumnHeadersDefaultCellStyle = new() { BackColor = Theme.Pale, ForeColor = Theme.Secondary, Font = Theme.Body(9, FontStyle.Bold), Padding = new(9, 5, 9, 5), SelectionBackColor = Theme.Pale, SelectionForeColor = Ink },
        DefaultCellStyle = new() { BackColor = Theme.Table, ForeColor = Ink, SelectionBackColor = Theme.Selected, SelectionForeColor = Ink, Padding = new(9, 5, 9, 5) },
        AlternatingRowsDefaultCellStyle = new() { BackColor = Theme.Stripe }
    };
    internal static TextBox Text(string placeholder = "", bool multiline = false) => new()
    {
        Width = 260,
        PlaceholderText = placeholder,
        AutoSize = false,
        MinimumSize = new(0, 34),
        Multiline = multiline,
        Height = multiline ? 65 : 34,
        Margin = new(4),
        AccessibleName = placeholder,
        ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
    };
    internal static ComboBox Combo(IEnumerable<string> values)
    { var combo = new DesktopComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 }; combo.Items.AddRange(values.Cast<object>().ToArray()); return combo; }
    internal static Panel Tabs(params (string Name, Control View)[] pages)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        var tabs = new DarkTabs { Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed, ItemSize = new(125, 34), Padding = new(10, 5), AccessibleName = "Result views" };
        tabs.DrawItem += (_, e) =>
        {
            bool active = tabs.SelectedIndex == e.Index; using var brush = new SolidBrush(active ? Theme.Selected : Theme.Canvas); e.Graphics.FillRectangle(brush, e.Bounds);
            using var font = Theme.Body(9, active ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, font, e.Bounds, active ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (active) { using var marker = new SolidBrush(Theme.Teal); e.Graphics.FillRectangle(marker, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3); }
            if (tabs.Focused && active) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -4, -4));
        };
        foreach (var page in pages) { var tab = new TabPage(page.Name) { BackColor = Theme.Canvas, Padding = new(4, 8, 4, 4) }; page.View.Dock = DockStyle.Fill; tab.Controls.Add(page.View); tabs.TabPages.Add(tab); }
        panel.Controls.Add(tabs); return panel;
    }
    internal static string? Ask(IWin32Window owner, string title, string label, string initial = "")
    {
        using var form = new Form
        {
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new(96, 96),
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new(490, 180),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Theme.Body()
        };
        var box = Text(); box.Text = initial; box.Dock = DockStyle.Fill;
        var ok = Button("Continue", () => { if (!string.IsNullOrWhiteSpace(box.Text)) form.DialogResult = DialogResult.OK; });
        var cancel = Button("Cancel", () => form.DialogResult = DialogResult.Cancel);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), RowCount = 3, ColumnCount = 1 };
        var explanation = Components.Message(label); explanation.AutoSize = true; explanation.MaximumSize = new(450, 0); explanation.MinimumSize = new(0, 48);
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Absolute, 40)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(explanation, 0, 0); layout.Controls.Add(box, 0, 1); layout.Controls.Add(Bar(ok, cancel), 0, 2); form.Controls.Add(layout);
        form.AcceptButton = ok; form.CancelButton = cancel; Theme.Apply(form);
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
    internal static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
    internal static ulong Seed(string value) => ulong.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seed)
        ? seed : throw new ArgumentException("Seed must be an unsigned whole number from 0 to 18446744073709551615.");
}

// Owner-drawn native dropdowns can retain a pre-DPI PreferredSize. FlowLayout
// must reserve their real height, including when controls wrap onto new rows.
internal sealed class DesktopComboBox : ComboBox
{
    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        return new(size.Width, Math.Max(size.Height, Math.Max(Height, Theme.Pixels(Theme.ControlHeight, DeviceDpi))));
    }
}

// Preserve native tab selection, keyboard navigation and accessibility while painting the surrounding dark surface.
internal sealed class DarkTabs : TabControl
{
    internal DarkTabs() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Canvas);
        using var border = new Pen(Theme.Border);
        var area = DisplayRectangle; area.Inflate(2, 2);
        e.Graphics.DrawLine(border, area.Left, area.Top, area.Right, area.Top);
        for (int i = 0; i < TabCount; i++) OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, GetTabRect(i), i,
            i == SelectedIndex ? DrawItemState.Selected : DrawItemState.None));
    }
}
