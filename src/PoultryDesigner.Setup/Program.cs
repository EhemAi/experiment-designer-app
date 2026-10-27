using System.Diagnostics;
using System.Runtime.InteropServices;
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
        if (args.Length == 2 && args[0] == "--uninstall") { Application.Run(new SetupForm(args[1], uninstallOnly: true)); return 0; }
        if (args.Length is 2 or 3 && args[0] == "--render")
        {
            using var form = new SetupForm { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
            form.Show(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
            if (args.Length == 3) { float scale = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture); form.Scale(new SizeF(scale, scale)); form.ClientSize = new((int)(1000 * scale), (int)(620 * scale)); form.FitClientToWindow(); form.PerformLayout(); Application.DoEvents(); }
            IEnumerable<Control> Descend(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descend(c)));
            foreach (var target in Descend(form).Where(c => c.Visible && c is Button or TextBox))
                if (target.Left < 0 || target.Top < 0 || target.Right > target.Parent!.ClientSize.Width || target.Bottom > target.Parent.ClientSize.Height) throw new InvalidOperationException("Setup target is cropped: " + target.Text);
            foreach (var button in Descend(form).OfType<Button>().Where(c => c.Visible))
                if (!form.ClientRectangle.Contains(form.RectangleToClient(button.Parent!.RectangleToScreen(button.Bounds)))) throw new InvalidOperationException("Setup action leaves the visible window: " + button.Text);
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new(0, 0, form.Width, form.Height));
            bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); form.Close(); return 0;
        }
        if (args.Length == 3 && args[0] == "--verify")
        {
            try { var manifest = Installation.ReadPackage(args[1]); Installation.Verify(args[1], manifest); File.WriteAllText(args[2], "PASS: offline installer verified every payload hash.\n"); return 0; }
            catch (Exception ex) { File.WriteAllText(args[2], "FAIL: " + ex); return 1; }
        }
        Application.Run(new SetupForm()); return 0;
    }
}
internal sealed class SetupForm : Form
{
    private readonly TextBox destination = Ui.Text("Installation folder");
    private readonly CheckBox shortcut = new() { Text = "Create a desktop shortcut", Checked = true, AutoSize = true };
    private readonly Label status = Ui.Label("Install for your Windows account. Administrator access is not required.");
    private readonly FlowLayoutPanel actions;
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Visible = false };
    private readonly bool uninstallOnly;
    private bool busy;
    [StructLayout(LayoutKind.Sequential)] private struct ClientBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out ClientBounds bounds);
    internal void FitClientToWindow()
    {
        // Windows can cap an oversized high-DPI window. Match the layout to its
        // actual client viewport, keeping the pinned action rows reachable.
        if (GetClientRect(Handle, out var bounds))
        {
            var actual = new Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            if (actual.Width > 0 && actual.Height > 0 && (ClientSize.Width > actual.Width || ClientSize.Height > actual.Height)) ClientSize = actual;
        }
    }
    internal SetupForm(string? installRoot = null, bool uninstallOnly = false)
    {
        this.uninstallOnly = uninstallOnly;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96); Font = Theme.Body(); Text = "Experiment Designer Setup";
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "experiment-designer.ico"));
        ClientSize = new(1000, 620); MinimumSize = new(920, 500); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false;
        destination.Anchor = AnchorStyles.Left | AnchorStyles.Right; destination.Text = installRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExperimentDesigner");
        destination.ReadOnly = uninstallOnly;
        var title = Ui.Label(uninstallOnly ? "Remove Experiment Designer" : "Install Experiment Designer"); title.Font = Theme.Heading(18); title.Dock = DockStyle.Fill; title.AutoSize = false;
        var explanation = Components.Message(uninstallOnly ? "Remove the application from this Windows account.\nYour saved studies and modified files are preserved." : "A complete research workspace for your Windows account.\nAvailable in Start and Installed apps after installation."); explanation.Padding = Padding.Empty;
        var install = Ui.Button("Install / update", () => _ = InstallAsync()); ((StyledButton)install).Primary = true; install.Visible = !uninstallOnly;
        var remove = Ui.Button("Remove installation", () => _ = RemoveAsync()); ((StyledButton)remove).Variant = ButtonVariant.Danger;
        var open = Ui.Button("Open installed app", OpenInstalled); open.Visible = !uninstallOnly;
        actions = Ui.Bar(install, open);
        var secondary = Ui.Bar(remove, Ui.Button("Close", Close));
        var actionRows = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 112, Padding = new(28, 0, 24, 8), ColumnCount = 1, RowCount = 2 };
        actionRows.RowStyles.Add(new(SizeType.Percent, 50)); actionRows.RowStyles.Add(new(SizeType.Percent, 50)); actionRows.Controls.Add(actions, 0, 0); actionRows.Controls.Add(secondary, 0, 1);
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 464, Padding = new(28, 20, 24, 8), RowCount = 7, ColumnCount = 1 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 58, 64, 114, 44, 64, 14, 78 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.Controls.Add(title, 0, 0); layout.Controls.Add(explanation, 0, 1);
        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new(0) };
        folderRow.RowStyles.Add(new(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new(SizeType.Absolute, 100));
        var browse = Ui.Button("Browse", Browse); browse.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        browse.Enabled = !uninstallOnly;
        folderRow.ColumnCount = 2; folderRow.Controls.Add(destination, 0, 0); folderRow.Controls.Add(browse, 1, 0);
        var folder = Components.Card("Install location", folderRow); layout.Controls.Add(folder, 0, 2);
        shortcut.Visible = !uninstallOnly; layout.Controls.Add(shortcut, 0, 3);
        var included = Components.Message("Start menu entry  ·  Windows uninstall support\nBundled runtime  ·  Offline user guide", Theme.Secondary); included.Padding = Padding.Empty; layout.Controls.Add(included, 0, 4);
        layout.Controls.Add(progress, 0, 5);
        status.AutoSize = false; status.Dock = DockStyle.Fill; status.Padding = new(4, 8, 4, 4); layout.Controls.Add(status, 0, 6);
        var scrolling = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scrolling.Controls.Add(layout);
        var main = new Panel { Dock = DockStyle.Fill }; main.Controls.Add(scrolling); main.Controls.Add(actionRows);
        var brand = new Panel { Dock = DockStyle.Fill, Padding = new(24), Tag = "sidebar" };
        var logoImage = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "experiment-designer.png"));
        var logo = new PictureBox { Image = logoImage, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Top, Height = 100 };
        Disposed += (_, _) => logoImage.Dispose();
        var brandTitle = Components.Message("Experiment\nDesigner", Theme.Text); brandTitle.Font = Theme.Heading(14); brandTitle.Dock = DockStyle.Top; brandTitle.Height = 104;
        var version = Components.Message("DESKTOP SETUP\nVersion " + typeof(SetupForm).Assembly.GetName().Version?.ToString(3)); version.Dock = DockStyle.Top; version.Height = 60;
        var credit = Components.Message("Created by\nEmmanuel Alagbe"); credit.Dock = DockStyle.Bottom; credit.Height = 64; credit.Font = Theme.Body(8.5f);
        brand.Controls.Add(version); brand.Controls.Add(brandTitle); brand.Controls.Add(logo); brand.Controls.Add(credit);
        var shell = new Panel { Dock = DockStyle.Fill };
        brand.Dock = DockStyle.Left; brand.Width = 242; shell.Controls.Add(main); shell.Controls.Add(brand);
        Controls.Add(shell); Theme.Apply(this);
        Shown += (_, _) => FitClientToWindow();
        DpiChanged += (_, _) => BeginInvoke(FitClientToWindow);
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        if (uninstallOnly) Shown += async (_, _) => await RemoveAsync();
        else Shown += (_, _) => install.Select();
    }
    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the application's installation folder", SelectedPath = destination.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
    }
    private async Task InstallAsync()
    {
        SetBusy(true);
        try
        {
            string root = Path.GetFullPath(destination.Text);
            var updates = new Progress<string>(message => { if (!IsDisposed) status.Text = message.StartsWith("Installing ", StringComparison.Ordinal) ? "Installing application files…" : message; });
            var receipt = await Task.Run(() => Installation.Install(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), root, updates));
            WindowsIntegration.CurrentUser.Register(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")), root, receipt, shortcut.Checked);
            status.Text = "Installed version " + receipt.Version + ". Open it below or find Experiment Designer in Start. Windows Installed apps includes its uninstall option.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    private async Task RemoveAsync()
    {
        if (MessageBox.Show(this, "Remove this program's verified installed files? Study projects, unknown files and modified files are preserved.", "Remove Experiment Designer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true); bool removed = false;
        try
        {
            string root = Path.GetFullPath(destination.Text); _ = Installation.ReadInstalled(root) ?? throw new InvalidDataException("No recognized installation."); var integration = WindowsIntegration.ForRemoval(root);
            await Task.Run(() => Installation.Uninstall(root)); integration.Unregister(root); removed = true; status.Text = "Program removed from Windows. Project files and any unknown or modified files remain.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { SetBusy(false); }
        if (removed && uninstallOnly) Close();
    }
    private void SetBusy(bool value) { busy = value; actions.Parent!.Enabled = destination.Parent!.Enabled = shortcut.Enabled = !value; progress.Visible = value; }
    private void OpenInstalled()
    {
        try
        {
            string root = Path.GetFullPath(destination.Text); var receipt = Installation.ReadInstalled(root) ?? throw new InvalidDataException("Install the application first.");
            Process.Start(new ProcessStartInfo(Installation.Executable(root, receipt)) { UseShellExecute = true });
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
