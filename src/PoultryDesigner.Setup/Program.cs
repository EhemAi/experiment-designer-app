using PoultryDesigner.AppCore;
using PoultryDesigner.Desktop;

namespace PoultryDesigner.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 3 && args[0] == "--integration-check")
        {
            try { WindowsIntegration.Check(args[1], args[2]); return 0; }
            catch (Exception ex) { Directory.CreateDirectory(Path.Combine(args[2], "analysis", "package-validation")); File.WriteAllText(Path.Combine(args[2], "analysis", "package-validation", "windows-integration.txt"), "FAIL: " + ex); return 1; }
        }
        if (args.Length == 2 && args[0] == "--uninstall-check")
        {
            try { var integration = WindowsIntegration.ForRemoval(args[1], test: true); Installation.Uninstall(args[1]); integration.Unregister(args[1]); File.WriteAllText(Path.Combine(args[1], "uninstall-check.txt"), "PASS"); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "uninstall-check.txt"), "FAIL: " + ex); return 1; }
        }
        if (args.Length == 3 && args[0] == "--wizard-check") return SetupChecks.Run(args[1], args[2]);
        if (args.Length == 2 && args[0] == "--uninstall") { Application.Run(new SetupForm(args[1], uninstallOnly: true)); return 0; }
        if (args.Length is 2 or 3 && args[0] == "--render")
        {
            using var form = new SetupForm { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
            form.Show(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
            if (args.Length == 3) { float scale = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture); form.Scale(new SizeF(scale, scale)); form.ClientSize = new((int)(1000 * scale), (int)(620 * scale)); form.FitClientToWindow(); form.PerformLayout(); Application.DoEvents(); }
            IEnumerable<Control> Descend(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descend(c)));
            File.WriteAllLines(args[1] + ".layout.txt", Descend(form).Where(c => c.Visible).Select(c => $"{c.GetType().Name} {c.Text} bounds={c.Bounds} parent={c.Parent!.ClientSize}"));
            using (var preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new(0, 0, form.Width, form.Height)); preview.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
            foreach (var target in Descend(form).Where(c => c.Visible && c is Button or TextBox))
                if (target.Left < 0 || target.Top < 0 || target.Right > target.Parent!.ClientSize.Width || target.Bottom > target.Parent.ClientSize.Height) throw new InvalidOperationException("Setup target is cropped: " + target.Text);
            foreach (var button in Descend(form).OfType<Button>().Where(c => c.Visible))
                if (!form.ClientRectangle.Contains(form.RectangleToClient(button.Parent!.RectangleToScreen(button.Bounds)))) throw new InvalidOperationException("Setup action leaves the visible window: " + button.Text);
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new(0, 0, form.Width, form.Height));
            bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); form.Dispose(); return 0;
        }
        if (args.Length == 3 && args[0] == "--verify")
        {
            try { var manifest = Installation.ReadPackage(args[1]); Installation.Verify(args[1], manifest); File.WriteAllText(args[2], "PASS: offline installer verified every payload hash.\n"); return 0; }
            catch (Exception ex) { File.WriteAllText(args[2], "FAIL: " + ex); return 1; }
        }
        Application.Run(new SetupForm()); return 0;
    }
}
