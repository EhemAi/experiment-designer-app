using System.Runtime.CompilerServices;
using PoultryDesigner.AppCore;

namespace PoultryDesigner.Desktop;

// Scientific values and binding keys stay in their original form. Translation is applied when controls paint.
internal sealed class LocalizedLabel : Label
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Translate { get; set; } = true;
    internal string DisplayText => Translate ? AppText.T(Text) : Text;
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!Translate || AppText.Language == AppLanguage.English) return base.GetPreferredSize(proposedSize);
        int width = MaximumSize.Width > 0 ? MaximumSize.Width : proposedSize.Width > 0 ? proposedSize.Width : int.MaxValue;
        var measured = TextRenderer.MeasureText(DisplayText, Font, new(Math.Max(1, width - Padding.Horizontal), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return new(measured.Width + Padding.Horizontal, measured.Height + Padding.Vertical);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Translate || AppText.Language == AppLanguage.English) { base.OnPaint(e); return; }
        var area = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, ClientSize.Width - Padding.Horizontal), Math.Max(1, ClientSize.Height - Padding.Vertical));
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
        if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
        if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
        flags |= TextAlign switch
        {
            ContentAlignment.MiddleLeft => TextFormatFlags.VerticalCenter,
            ContentAlignment.MiddleCenter => TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight => TextFormatFlags.Right,
            ContentAlignment.MiddleRight => TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
            ContentAlignment.BottomLeft => TextFormatFlags.Bottom,
            ContentAlignment.BottomCenter => TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter,
            ContentAlignment.BottomRight => TextFormatFlags.Bottom | TextFormatFlags.Right,
            _ => TextFormatFlags.Left
        };
        TextRenderer.DrawText(e.Graphics, DisplayText, Font, area, Enabled ? ForeColor : SystemColors.GrayText, flags);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new TextAccessibility(this);
    private sealed class TextAccessibility(LocalizedLabel owner) : ControlAccessibleObject(owner)
    {
        public override string? Name { get => owner.DisplayText; set => base.Name = value; }
    }
}

internal sealed class LocalizedHeader : DataGridViewColumnHeaderCell
{
    protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates state,
        object? value, object? formattedValue, string? errorText, DataGridViewCellStyle style, DataGridViewAdvancedBorderStyle border, DataGridViewPaintParts parts)
        => base.Paint(graphics, clipBounds, cellBounds, rowIndex, state, value, AppText.T(Convert.ToString(formattedValue)), errorText, style, border, parts);
}

internal static class UiLanguage
{
    private sealed class Original(string text) { internal string Text = text; internal bool Updating; }
    private static readonly ConditionalWeakTable<Control, Original> originals = new();
    private static readonly ConditionalWeakTable<Form, object> attached = new();
    private static readonly ConditionalWeakTable<Control, Original> accessibleNames = new();
    private static readonly ConditionalWeakTable<Control, Original> accessibleDescriptions = new();
    private static readonly ConditionalWeakTable<Control, Original> tips = new();
    internal static string ComboText(ComboBox combo, int index, string value) =>
        combo is DesktopComboBox desktop && !desktop.TranslateItems && !(index == 0 && desktop.TranslateFirstItem) ? value : AppText.T(value);
    internal static void Tip(Control control, string original)
    {
        tips.GetValue(control, _ => new(original)).Text = original;
        Components.Help.SetToolTip(control, AppText.T(original));
    }
    internal static void Attach(Form form)
    {
        if (attached.TryGetValue(form, out _)) return;
        attached.Add(form, new());
        void Refresh(object? sender, EventArgs e)
        {
            if (form.IsDisposed) return;
            form.SuspendLayout();
            Apply(form);
            form.ResumeLayout(true);
            form.PerformLayout();
            form.Invalidate(true);
        }
        AppText.Changed += Refresh;
        form.Disposed += (_, _) => AppText.Changed -= Refresh;
        form.Shown += Refresh;
        Apply(form);
    }
    private static void TranslateText(Control control)
    {
        if (!originals.TryGetValue(control, out var original))
        {
            original = new(control.Text);
            originals.Add(control, original);
            control.TextChanged += (_, _) =>
            {
                if (original.Updating) return;
                original.Text = control.Text;
                original.Updating = true;
                try { control.Text = AppText.T(original.Text); }
                finally { original.Updating = false; }
            };
        }
        original.Updating = true;
        try { control.Text = AppText.T(original.Text); }
        finally { original.Updating = false; }
    }
    internal static void Apply(Control root)
    {
        if (root is Form or CheckBox or RadioButton) TranslateText(root);
        if (root.AccessibleName is { Length: > 0 } name)
        {
            var original = accessibleNames.GetValue(root, _ => new(name));
            root.AccessibleName = AppText.T(original.Text);
        }
        if (root.AccessibleDescription is { Length: > 0 } description)
        {
            var original = accessibleDescriptions.GetValue(root, _ => new(description));
            root.AccessibleDescription = AppText.T(original.Text);
        }
        if (root is TextBox input && input.Parent is not UpDownBase)
        {
            var original = originals.GetValue(input, c => new(((TextBox)c).PlaceholderText));
            input.PlaceholderText = AppText.T(original.Text);
            input.AccessibleName ??= AppText.T(original.Text);
        }
        if (tips.TryGetValue(root, out var tip)) Components.Help.SetToolTip(root, AppText.T(tip.Text));
        if (root.ContextMenuStrip is { } menu) ApplyItems(menu.Items);
        if (root is ToolStrip strip) ApplyItems(strip.Items);
        if (root is TabControl tabs)
        {
            int width = tabs.TabPages.Cast<TabPage>().Select(p => TextRenderer.MeasureText(AppText.T(p.Text), tabs.Font).Width + 24).DefaultIfEmpty(125).Max();
            tabs.Multiline = AppText.Language != AppLanguage.English;
            tabs.SizeMode = AppText.Language == AppLanguage.English ? TabSizeMode.Normal : TabSizeMode.Fixed;
            tabs.ItemSize = new(Math.Max(125, width), tabs.ItemSize.Height);
        }
        if (root is DataGridView grid)
        {
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.HeaderCell is not LocalizedHeader) column.HeaderCell = new LocalizedHeader { Value = column.HeaderText };
            }
            grid.Invalidate();
        }
        if (root is LocalizedLabel label && label.Dock == DockStyle.Top && Equals(label.Parent?.Tag, "surface"))
        {
            int width = Math.Max(1, label.Parent!.ClientSize.Width - label.Parent.Padding.Horizontal);
            label.Height = Math.Max(30, label.GetPreferredSize(new(width, 0)).Height + 4);
        }
        if (root is LocalizedLabel { Text: "Study status" } status && status.Parent is { } card)
        {
            var body = card.Controls.OfType<LocalizedLabel>().FirstOrDefault(c => c != status);
            int width = Math.Max(1, card.ClientSize.Width - card.Padding.Horizontal);
            int height = body?.GetPreferredSize(new(width, 0)).Height ?? 0;
            card.Height = Math.Max(Theme.Pixels(132, card.DeviceDpi), status.Height + height + card.Padding.Vertical + 4);
        }
        foreach (Control child in root.Controls) Apply(child);
        root.PerformLayout();
        root.Invalidate();
    }
    private static readonly ConditionalWeakTable<ToolStripItem, Original> menuText = new();
    private static void ApplyItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            var original = menuText.GetValue(item, i => new(i.Text ?? ""));
            item.Text = AppText.T(original.Text);
            if (item is ToolStripMenuItem submenu) ApplyItems(submenu.DropDownItems);
        }
    }
    internal static void Grid(DataGridView grid)
    {
        grid.ColumnAdded += (_, e) => e.Column.HeaderCell = new LocalizedHeader { Value = e.Column.HeaderText };
        grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex < 0 || e.Value is null) return;
            string name = grid.Columns[e.ColumnIndex].Name;
            if (name is "Sex" or "Gender" or "Decision" or "Disposition" or "Status" or "Design" or "Action" or "Outcome" or "Problem" or "Field")
                e.Value = AppText.T(Convert.ToString(e.Value));
        };
    }
    internal static DialogResult Show(string message, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(null, message, caption, buttons, icon);
    internal static DialogResult Show(IWin32Window? owner, string message, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        if (AppText.Language == AppLanguage.English) return MessageBox.Show(owner, message, caption, buttons, icon, defaultButton);
        using var dialog = new Form { Text = caption, Font = Theme.Body(), ClientSize = new(620, 280), MinimumSize = new(520, 250), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false };
        var label = Ui.Label(message); label.Dock = DockStyle.Top; label.Padding = new(22); label.MaximumSize = new(600, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(label);
        var actions = Ui.Bar(); actions.Dock = DockStyle.Bottom; actions.Padding = new(18, 8, 18, 14);
        (string Name, DialogResult Result)[] choices = buttons switch
        {
            MessageBoxButtons.YesNo => [("Yes", DialogResult.Yes), ("No", DialogResult.No)],
            MessageBoxButtons.YesNoCancel => [("Yes", DialogResult.Yes), ("No", DialogResult.No), ("Cancel", DialogResult.Cancel)],
            MessageBoxButtons.OKCancel => [("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel)],
            MessageBoxButtons.RetryCancel => [("Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel)],
            _ => [("OK", DialogResult.OK)]
        };
        foreach (var choice in choices)
        {
            var button = Ui.Button(choice.Name, () => { dialog.DialogResult = choice.Result; dialog.Close(); });
            actions.Controls.Add(button);
            if (choice.Result is DialogResult.No or DialogResult.Cancel) dialog.CancelButton = button;
        }
        dialog.AcceptButton = actions.Controls.OfType<Button>().ElementAt(Math.Min((int)defaultButton / 256, choices.Length - 1));
        dialog.Controls.Add(scroll); dialog.Controls.Add(actions); Theme.Apply(dialog);
        return dialog.ShowDialog(owner);
    }
}
