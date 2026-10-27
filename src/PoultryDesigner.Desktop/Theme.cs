using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace PoultryDesigner.Desktop;

internal enum ButtonVariant { Secondary, Ghost, Danger }

internal static class Theme
{
    // Explicit theme changes only recolor existing controls; animal bindings and drafts stay intact.
    internal static bool IsDark { get; private set; } = ReadDarkPreference();
    private static Color ColorFor(string light, string dark) => ColorTranslator.FromHtml(IsDark ? dark : light);
    internal static Color Navy => ColorFor("#E8EEEB", "#111719");
    internal static Color Canvas => ColorFor("#F3F6F5", "#17191B");
    internal static Color Panel => ColorFor("#FFFFFF", "#1E2325");
    internal static Color Text => ColorFor("#1B2925", "#EBEEEF");
    internal static Color Blue => ColorFor("#446379", "#A5C6DB");
    internal static Color Teal => ColorFor("#337C67", "#6BD0AE");
    internal static Color Muted => ColorFor("#576A62", "#A8B9B5");
    internal static Color Secondary => ColorFor("#475A52", "#C3D0CE");
    internal static Color Warning => ColorFor("#81531A", "#E0B45E");
    internal static Color Primary => Color.FromArgb(34, 112, 92);
    internal static Color Pale => ColorFor("#FFFFFF", "#242A2C");
    internal static Color Table => ColorFor("#FFFFFF", "#202628");
    internal static Color Stripe => ColorFor("#F7F9F8", "#242B2D");
    internal static Color Border => ColorFor("#D4DFD9", "#3D4A4C");
    internal static Color Divider => ColorFor("#E2E9E5", "#344043");
    internal static Color Selected => ColorFor("#DDEEE6", "#294B42");
    internal static Color Hover => ColorFor("#EAF2EE", "#2D3839");
    internal static Color Disabled => ColorFor("#58685F", "#A0ADA8");
    internal static Color DisabledBackground => ColorFor("#E8EEEB", "#2C3435");
    internal static Color Red => ColorFor("#A23F4B", "#F498A6");
    internal static Color ErrorBackground => ColorFor("#FAE8EC", "#39272D");
    internal static Color WarningBackground => ColorFor("#FBF1DE", "#342C20");
    internal static Color SuccessBackground => ColorFor("#E2F1E9", "#203C31");
    internal static Color Success => ColorFor("#246A55", "#7AD5B2");
    internal const int ControlHeight = 34, TableRowHeight = 38, TableHeaderHeight = 40;
    private static bool ReadDarkPreference()
    {
        // Test override also allows deterministic screenshots of both palettes.
        string? requested = Environment.GetEnvironmentVariable("EXPERIMENT_DESIGNER_THEME");
        if (requested is "light" or "dark") return requested == "dark";
        if (AppearancePreferences.Load() is bool saved) return saved;
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is not int value || value == 0; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return true; }
    }
    internal static int Pixels(int value, int dpi) => (int)Math.Ceiling(value * dpi / 96d);
    private static Color[] Palette() => [Navy, Canvas, Panel, Text, Blue, Teal, Muted, Secondary, Warning,
        Primary, Pale, Table, Stripe, Border, Divider, Selected, Hover, Disabled, DisabledBackground,
        Red, ErrorBackground, WarningBackground, SuccessBackground, Success];
    internal static void SetDark(bool dark, Control root)
    {
        var previous = Palette(); IsDark = dark; var current = Palette();
        Color Map(Color color)
        {
            int index = Array.FindIndex(previous, old => old.ToArgb() == color.ToArgb());
            return index < 0 ? color : current[index];
        }
        void Recolor(Control control)
        {
            control.SuspendLayout();
            try
            {
                control.BackColor = control is TextBox or ComboBox or NumericUpDown ? Table : Map(control.BackColor);
                control.ForeColor = Map(control.ForeColor);
                if (control is RoundedPanel panel) panel.Surface = Map(panel.Surface);
                if (control is DataGridView grid)
                {
                    grid.BackgroundColor = Canvas; grid.GridColor = Border;
                }
                if (control.ContextMenuStrip is { } menu)
                {
                    menu.BackColor = Pale; menu.ForeColor = Text;
                    foreach (ToolStripItem item in menu.Items) { item.BackColor = Pale; item.ForeColor = Text; }
                }
                foreach (Control child in control.Controls) Recolor(child);
            }
            finally { control.ResumeLayout(false); }
            control.Invalidate(true);
        }
        Recolor(root);
        // Replace whole cell-style objects to invalidate the native grid's
        // cached palette, retaining fonts, padding, columns and data bindings.
        void FinishGrids(Control control)
        {
            if (control is DataGridView grid)
            {
                grid.ForeColor = Text;
                grid.DefaultCellStyle = new(grid.DefaultCellStyle)
                {
                    ForeColor = Text,
                    BackColor = Table,
                    SelectionForeColor = Text,
                    SelectionBackColor = Selected
                };
                grid.ColumnHeadersDefaultCellStyle = new(grid.ColumnHeadersDefaultCellStyle)
                {
                    ForeColor = Secondary,
                    BackColor = Pale,
                    SelectionForeColor = Text,
                    SelectionBackColor = Pale
                };
                grid.AlternatingRowsDefaultCellStyle = new(grid.AlternatingRowsDefaultCellStyle) { BackColor = Stripe };
                grid.Invalidate();
            }
            foreach (Control child in control.Controls) FinishGrids(child);
        }
        FinishGrids(root);
    }
    private static readonly PrivateFontCollection Fonts = LoadFonts();
    private static readonly ConditionalWeakTable<Form, object> SizedForms = new();
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);
    private static PrivateFontCollection LoadFonts()
    {
        var collection = new PrivateFontCollection();
        foreach (string name in new[] { "Manrope-Regular.otf", "Manrope-Bold.otf", "Poppins-Regular.otf", "Poppins-SemiBold.otf" })
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", name);
            if (!File.Exists(path) || AddFontResourceEx(path, 0x10, IntPtr.Zero) == 0) throw new InvalidOperationException("Bundled font could not load: " + name);
            collection.AddFontFile(path);
        }
        return collection;
    }
    internal static string BodyFamily => "Manrope";
    internal static string HeadingFamily => "Poppins";
    internal static Font Body(float size = 9.5f, FontStyle style = FontStyle.Regular) => new(Fonts.Families.Single(x => x.Name == BodyFamily), size, style);
    internal static Font Heading(float size = 20) => new(Fonts.Families.Single(x => x.Name == HeadingFamily), size, FontStyle.Regular);
    internal static void Apply(Control root)
    {
        if (root is Form form && !SizedForms.TryGetValue(form, out _))
        {
            SizedForms.Add(form, new object());
            form.Shown += (_, _) => SetInteractiveSizes(form);
            form.DpiChanged += (_, _) => form.BeginInvoke(() => { SetInteractiveSizes(form); form.PerformLayout(); });
            if (!form.KeyPreview)
            {
                IEnumerable<Control> Descend(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descend(x)));
                form.AcceptButton ??= Descend(form).OfType<StyledButton>().FirstOrDefault(x => x.Primary);
                form.CancelButton ??= Descend(form).OfType<Button>().FirstOrDefault(x => x.Text is "Cancel" or "Close");
            }
        }
        if (root is not StyledButton && root is not DataGridView)
        {
            root.BackColor = root is TextBox or ComboBox or NumericUpDown ? Table : Equals(root.Tag, "sidebar") ? Navy : Equals(root.Tag, "surface") ? Panel : root is Form or TabPage or TabControl ? Canvas : root.Parent?.BackColor ?? Canvas;
            root.ForeColor = Text;
        }
        if (root is TextBox text)
        {
            text.BorderStyle = BorderStyle.FixedSingle;
            if (text.Parent is not UpDownBase) { text.AutoSize = false; text.MinimumSize = new(0, ControlHeight); if (!text.Multiline) text.Height = ControlHeight; }
        }
        if (root is NumericUpDown number) { number.AutoSize = false; number.MinimumSize = new(0, ControlHeight); number.Height = Math.Max(number.Height, ControlHeight); }
        if (root is Button button) button.MinimumSize = new(button.MinimumSize.Width, Math.Max(button.MinimumSize.Height, ControlHeight));
        if (root is StyledButton destructive && destructive.Text is "Remove selected" or "Remove installation") destructive.Variant = ButtonVariant.Danger;
        if (root is ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            if (combo.DrawMode != DrawMode.OwnerDrawFixed)
            {
                combo.DrawMode = DrawMode.OwnerDrawFixed; combo.ItemHeight = 28;
                combo.DrawItem += (_, e) =>
                {
                    using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Selected : Table); e.Graphics.FillRectangle(brush, e.Bounds);
                    string text = e.Index < 0 ? combo.Text : combo.Items[e.Index]?.ToString() ?? "";
                    var bounds = Rectangle.Inflate(e.Bounds, -6, 0);
                    TextRenderer.DrawText(e.Graphics, text, combo.Font, bounds, combo.Enabled ? Text : Disabled, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); e.DrawFocusRectangle();
                };
            }
        }
        if (root is TabPage tab) tab.UseVisualStyleBackColor = false;
        foreach (Control child in root.Controls) Apply(child);
    }
    // Enforce the desktop targets after Windows finishes initial DPI/font scaling.
    private static void SetInteractiveSizes(Control root)
    {
        int height = Pixels(ControlHeight, root.DeviceDpi), rowHeight = Pixels(TableRowHeight, root.DeviceDpi);
        if (root is Form form) form.Font = Body();
        if (root is TextBox text && text.Parent is not UpDownBase)
        {
            text.Font = Body(); text.AutoSize = false; text.MinimumSize = new(0, height); if (!text.Multiline) text.Height = height;
        }
        if (root is NumericUpDown number) { number.Font = Body(); number.AutoSize = false; number.MinimumSize = new(0, height); number.Height = Math.Max(height, number.Height); }
        if (root is ComboBox combo) { combo.Font = Body(); combo.ItemHeight = Pixels(28, root.DeviceDpi); }
        if (root is Button button) { button.Font = Body(9, FontStyle.Bold); button.MinimumSize = new(button.MinimumSize.Width, height); button.Height = Math.Max(height, button.Height); }
        if (root is DataGridView grid)
        {
            grid.Font = Body(); grid.RowTemplate.Height = grid.RowTemplate.MinimumHeight = rowHeight; grid.ColumnHeadersHeight = Pixels(TableHeaderHeight, root.DeviceDpi);
            grid.DefaultCellStyle.Padding = grid.ColumnHeadersDefaultCellStyle.Padding = new(Pixels(9, root.DeviceDpi), Pixels(5, root.DeviceDpi), Pixels(9, root.DeviceDpi), Pixels(5, root.DeviceDpi)); grid.ColumnHeadersDefaultCellStyle.Font = Body(9, FontStyle.Bold);
            foreach (DataGridViewRow row in grid.Rows) { row.MinimumHeight = rowHeight; row.Height = rowHeight; }
        }
        foreach (Control child in root.Controls) SetInteractiveSizes(child);
        // ComboBox.PreferredSize can still report its pre-owner-draw height.
        // Use the actual scaled targets so auto-sized toolbars never crop them.
        if (root is FlowLayoutPanel { AutoSize: true } bar)
        {
            int target = bar.Controls.Cast<Control>().Select(c => c.Height + c.Margin.Vertical).DefaultIfEmpty(0).Max() + bar.Padding.Vertical;
            bar.MinimumSize = new(bar.MinimumSize.Width, target);
        }
    }
    internal static GraphicsPath Round(Rectangle rect, int radius)
    {
        var path = new GraphicsPath(); int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Radius { get; set; } = 14;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color Surface { get; set; } = Theme.Pale;
    internal RoundedPanel() { DoubleBuffered = true; BackColor = Theme.Navy; Padding = new(16); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var brush = new SolidBrush(Surface); e.Graphics.FillPath(brush, path); base.OnPaint(e);
    }
}

internal sealed class StyledButton : Button
{
    private bool hovered;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal ButtonVariant Variant { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Navigation { get; set; }
    internal StyledButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Font = Theme.Body(9, FontStyle.Bold); BackColor = Theme.Canvas; Cursor = Cursors.Hand;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new(Math.Max(MinimumSize.Width, text.Width + 24 + Padding.Horizontal), Math.Max(MinimumSize.Height, text.Height + 14 + Padding.Vertical));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Canvas); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 7);
        using var brush = new SolidBrush(!Enabled ? Theme.DisabledBackground : Variant == ButtonVariant.Danger ? Theme.ErrorBackground : Primary && !Navigation ? Theme.Primary : Navigation && Primary ? Theme.Selected : hovered ? Theme.Hover : Variant == ButtonVariant.Ghost || Navigation ? Parent?.BackColor ?? Theme.Canvas : Theme.Pale);
        e.Graphics.FillPath(brush, path);
        if (!Primary && !Navigation && Variant != ButtonVariant.Ghost) { using var pen = new Pen(Theme.Border); e.Graphics.DrawPath(pen, path); }
        if (Navigation && Primary) { using var marker = new SolidBrush(Theme.Teal); e.Graphics.FillRectangle(marker, 1, 9, 3, Height - 18); }
        var textArea = Navigation ? Rectangle.Inflate(ClientRectangle, -15, 0) : ClientRectangle;
        TextRenderer.DrawText(e.Graphics, Text, Font, textArea, !Enabled ? Theme.Disabled : Variant == ButtonVariant.Danger ? Theme.Red : Primary && !Navigation ? Color.White : Theme.Text,
            (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) { using var focusPath = Theme.Round(Rectangle.Inflate(ClientRectangle, -4, -4), 4); using var pen = new Pen(Theme.Teal, 2); e.Graphics.DrawPath(pen, focusPath); }
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
}
