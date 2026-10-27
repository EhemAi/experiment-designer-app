namespace PoultryDesigner.Desktop;

internal static class RedesignChecks
{
    internal static void CheckLayout(Control root, Action<bool, string> check)
    {
        IEnumerable<Control> Descend(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descend(x)));
        foreach (var bar in Descend(root).OfType<FlowLayoutPanel>().Where(p => p.Visible && p.AutoSize))
        {
            var targets = bar.Controls.Cast<Control>().Where(c => c.Visible && c is Button or ComboBox).ToArray();
            if (targets.Length > 0) check(targets.All(c => c.Top >= 0 && c.Bottom <= bar.ClientSize.Height), "Toolbar click targets are not cropped: " + bar.ClientSize + ": " + string.Join("; ", targets.Select(c => c.Text + " " + c.Bounds)));
        }
        foreach (var table in Descend(root).OfType<TableLayoutPanel>())
        {
            var inputs = table.Controls.Cast<Control>().Where(c => c.Visible && c is TextBox or ComboBox or NumericUpDown).ToArray();
            if (inputs.Length == 0) continue;
            check(inputs.All(c => c.Left >= 0 && c.Right <= table.ClientSize.Width), "Form inputs remain within their column width");
            check(inputs.All(c => inputs.All(other => ReferenceEquals(c, other) || !c.Bounds.IntersectsWith(other.Bounds))), "Form inputs do not overlap after layout and DPI scaling: " + string.Join("; ", inputs.Select(c => c.AccessibleName + " " + c.Bounds)));
        }
    }
    internal static void CheckTheme(Action<bool, string> check)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte v) { double n = v / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        static double Contrast(Color a, Color b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
        foreach (var pair in new[] {
            ("primary action", Color.White, Theme.Primary), ("table", Theme.Text, Theme.Table),
            ("selection", Theme.Text, Theme.Selected), ("caption", Theme.Muted, Theme.Canvas),
            ("disabled", Theme.Disabled, Theme.DisabledBackground), ("warning", Theme.Warning, Theme.WarningBackground),
            ("error", Theme.Red, Theme.ErrorBackground), ("success", Theme.Success, Theme.SuccessBackground)
        }) check(Contrast(pair.Item2, pair.Item3) >= 4.5, (Theme.IsDark ? "Dark" : "Light") + " theme readable " + pair.Item1);
        using var splash = new SplashForm();
        check(splash.Loading.AccessibleDescription == LaunchArtwork.CreatorCredit && LaunchArtwork.CreatorCredit.Contains("Emmanuel Alagbe"), "Launch artwork carries the visible and accessible creator credit");
    }
}
