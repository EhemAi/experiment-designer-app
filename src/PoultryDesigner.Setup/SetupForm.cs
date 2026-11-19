using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using PoultryDesigner.AppCore;

namespace PoultryDesigner.Setup;

internal enum SetupPage { Language, License, Destination, StartMenu, Tasks, Ready, Installing, Complete, Removal }

internal sealed partial class SetupForm : Form
{
    private static readonly Color Accent = Color.FromArgb(34, 112, 92);
    private readonly Dictionary<Control, string> translations = [];
    private readonly Dictionary<SetupPage, Control> pages = [];
    private readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new(32, 22, 32, 16), BackColor = Color.White };
    private readonly Label title = new() { AutoSize = false, Dock = DockStyle.Top, Height = 36 };
    private readonly Label introduction = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Label error = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 58, ForeColor = Color.FromArgb(157, 41, 32), Padding = new(32, 4, 32, 4), Visible = false };
    private readonly Button back = ActionButton();
    private readonly Button next = ActionButton();
    private readonly Button cancel = ActionButton();
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Width = 340 };
    private readonly TextBox destination = new() { Dock = DockStyle.Fill };
    private readonly TextBox menu = new() { Dock = DockStyle.Top, Text = "Experiment Designer" };
    private readonly CheckBox createMenu = new() { AutoSize = true, Checked = true };
    private readonly CheckBox desktop = new() { AutoSize = true, Checked = true };
    private readonly CheckBox launch = new() { AutoSize = true, Checked = true };
    private readonly RadioButton accept = new() { AutoSize = true };
    private readonly RadioButton decline = new() { AutoSize = true, Checked = true };
    private readonly RichTextBox license = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, TabStop = true };
    private readonly TextBox summary = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ScrollBars = ScrollBars.Vertical };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly Label progressText = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly bool uninstallOnly;
    private readonly bool licenseAvailable;
    private readonly string package;
    private readonly WindowsIntegration? checkIntegration;
    private readonly string? languagePreferencePath;
    private readonly List<Font> ownedFonts = [];
    private readonly Image logo;
    private readonly Label version;
    private SetupStrings text = new(SetupLanguage.English);
    private SetupPage page;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private bool registering;
    private bool removalComplete;
    private InstallReceipt? installed;
    private string? committedRoot;

    internal SetupPage Page => page;
    internal SetupLanguage Language => text.Language;
    internal SetupForm(string? installRoot = null, bool uninstallOnly = false, string? packageRoot = null, WindowsIntegration? integration = null, string? languagePreferencePath = null)
    {
        this.uninstallOnly = uninstallOnly;
        package = packageRoot ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        checkIntegration = integration;
        this.languagePreferencePath = languagePreferencePath;
        Text = text["Setup"];
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new(96, 96);
        Font = NewFont(9.5f);
        ClientSize = new(820, 590);
        MinimumSize = new(800, 580);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(31, 42, 38);
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "experiment-designer.ico"));
        logo = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "experiment-designer.png"));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(32, 18, 32, 10), BackColor = Color.White, ColumnCount = 2, RowCount = 2 };
        heading.ColumnStyles.Add(new(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new(SizeType.Absolute, 70));
        heading.RowStyles.Add(new(SizeType.Absolute, 42));
        heading.RowStyles.Add(new(SizeType.Percent, 100));
        title.Dock = DockStyle.Fill;
        title.Font = NewFont(17, FontStyle.Bold);
        heading.Controls.Add(title, 0, 0);
        heading.Controls.Add(introduction, 0, 1);
        var logoView = new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new(12, 5, 0, 5) };
        heading.Controls.Add(logoView, 1, 0);
        heading.SetRowSpan(logoView, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new(0), Margin = new(0) };
        buttons.Controls.AddRange([back, next, cancel]);
        next.BackColor = Accent;
        next.ForeColor = Color.White;
        next.FlatStyle = FlatStyle.Flat;
        next.FlatAppearance.BorderSize = 0;
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(242, 245, 243), Padding = new(32, 18, 24, 16) };
        version = new Label { AutoSize = true, Dock = DockStyle.Left, ForeColor = Color.FromArgb(90, 104, 98), Padding = new(0, 10, 0, 0) };
        footer.Controls.Add(buttons);
        footer.Controls.Add(version);
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(content);
        body.Controls.Add(error);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new(0), Padding = new(0) };
        shell.ColumnStyles.Add(new(SizeType.Percent, 100));
        shell.RowStyles.Add(new(SizeType.Absolute, 112));
        shell.RowStyles.Add(new(SizeType.Percent, 100));
        shell.RowStyles.Add(new(SizeType.Absolute, 78));
        heading.Margin = body.Margin = footer.Margin = new(0);
        shell.Controls.Add(heading, 0, 0);
        shell.Controls.Add(body, 0, 1);
        shell.Controls.Add(footer, 0, 2);
        Controls.Add(shell);

        destination.Text = installRoot ?? WindowsIntegration.DefaultRoot;
        destination.ReadOnly = uninstallOnly;
        var preferences = WindowsIntegration.Preferences(destination.Text);
        menu.Text = preferences.MenuFolder.Length == 0 ? "Experiment Designer" : preferences.MenuFolder;
        createMenu.Checked = preferences.MenuFolder.Length > 0;
        language.Items.AddRange(["English", "Español (España)", "Español (México)"]);
        var initial = !uninstallOnly && File.Exists(LanguagePreference.DefaultPath) ? (SetupLanguage)(int)LanguagePreference.Load() : preferences.Language == "en" ? CurrentLanguage() : SetupStrings.FromCode(preferences.Language);
        language.SelectedIndex = (int)initial;
        language.SelectedIndexChanged += (_, _) => { text = new((SetupLanguage)language.SelectedIndex); Translate(); };
        text = new(initial);

        string licensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        licenseAvailable = File.Exists(licensePath);
        license.Text = licenseAvailable ? File.ReadAllText(licensePath) : text["LicenseMissing"];
        license.Font = NewFont(9.5f);
        BuildPages();
        createMenu.CheckedChanged += (_, _) => menu.Enabled = createMenu.Checked;
        menu.Enabled = createMenu.Checked;
        accept.CheckedChanged += (_, _) => UpdateActions();
        back.Click += (_, _) => MoveBack();
        next.Click += async (_, _) => await MoveNextAsync();
        cancel.Click += (_, _) => Close();
        AcceptButton = next;
        CancelButton = cancel;
        FormClosing += ConfirmClosing;
        Shown += (_, _) => { FitClientToWindow(); next.Focus(); };
        DpiChanged += (_, _) => BeginInvoke(FitClientToWindow);
        Translate();
        ShowPage(uninstallOnly ? SetupPage.Removal : SetupPage.Language);
    }

    private static SetupLanguage CurrentLanguage() => CultureInfo.CurrentUICulture.Name switch
    {
        "es-MX" => SetupLanguage.SpanishMexico,
        var name when name.StartsWith("es", StringComparison.OrdinalIgnoreCase) => SetupLanguage.SpanishSpain,
        _ => SetupLanguage.English
    };
    private Font NewFont(float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font("Segoe UI", size, style);
        ownedFonts.Add(font);
        return font;
    }
    private static Button ActionButton() => new() { Width = 118, Height = 40, Margin = new(8, 0, 0, 0), UseVisualStyleBackColor = true };
    private T Bind<T>(T control, string key) where T : Control { translations.Add(control, key); control.Text = text[key]; return control; }
    private Label Message(string key) => Bind(new Label { AutoSize = true, Dock = DockStyle.Top, Margin = new(0, 0, 0, 16) }, key);
    private static TableLayoutPanel PageLayout() => new() { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, Padding = new(0), Margin = new(0) };
    private static void Row(TableLayoutPanel panel, Control control, SizeType sizing = SizeType.AutoSize, float height = 0)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new(sizing, height));
        panel.Controls.Add(control, 0, row);
        if (panel.ColumnStyles.Count == 0) panel.ColumnStyles.Add(new(SizeType.Percent, 100));
    }
    private TableLayoutPanel PageFor(SetupPage which)
    {
        var view = PageLayout();
        pages.Add(which, view);
        return view;
    }
    private void BuildPages()
    {
        var first = PageFor(SetupPage.Language);
        Row(first, Message("Welcome"));
        Row(first, Message("LanguageLabel"));
        Row(first, language, SizeType.Absolute, 48);
        Row(first, Message("Developer"));

        var agreement = PageFor(SetupPage.License);
        var notice = Message("LicenseNote"); notice.Font = NewFont(10, FontStyle.Bold);
        Row(agreement, notice);
        Row(agreement, Message("OriginalLicense"));
        Row(agreement, license, SizeType.Percent, 100);
        accept.Margin = new(0, 14, 0, 7);
        decline.Margin = new(0, 0, 0, 0);
        Row(agreement, Bind(accept, "AcceptLicense"));
        Row(agreement, Bind(decline, "DeclineLicense"));

        var folder = PageFor(SetupPage.Destination);
        Row(folder, Message("Destination"));
        var path = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = 42, Margin = new(0, 0, 0, 18) };
        path.ColumnStyles.Add(new(SizeType.Percent, 100));
        path.ColumnStyles.Add(new(SizeType.Absolute, 118));
        destination.Margin = new(0, 7, 12, 7);
        var browse = Bind(ActionButton(), "Browse"); browse.Dock = DockStyle.Fill; browse.Margin = new(0);
        browse.Click += (_, _) => Browse();
        path.Controls.Add(destination, 0, 0); path.Controls.Add(browse, 1, 0);
        Row(folder, path, SizeType.Absolute, 60);
        Row(folder, Message("DestinationHelp"));
        var existing = Message("Existing"); existing.Visible = Installation.ReadInstalled(destination.Text) is not null; Row(folder, existing);

        var start = PageFor(SetupPage.StartMenu);
        createMenu.Margin = new(0, 0, 0, 18);
        Row(start, Bind(createMenu, "CreateMenu"));
        Row(start, Message("MenuLabel"));
        var menuRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = 42, Margin = new(0, 0, 0, 18) };
        menuRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        menuRow.ColumnStyles.Add(new(SizeType.Absolute, 118));
        menu.Dock = DockStyle.Fill;
        menu.Margin = new(0, 7, 12, 7);
        var browseMenu = Bind(ActionButton(), "Browse");
        browseMenu.Dock = DockStyle.Fill;
        browseMenu.Margin = new(0);
        browseMenu.Enabled = createMenu.Checked;
        browseMenu.Click += (_, _) => BrowseMenu();
        createMenu.CheckedChanged += (_, _) => browseMenu.Enabled = createMenu.Checked;
        menuRow.Controls.Add(menu, 0, 0);
        menuRow.Controls.Add(browseMenu, 1, 0);
        Row(start, menuRow, SizeType.Absolute, 60);
        Row(start, Message("MenuHelp"));

        var tasks = PageFor(SetupPage.Tasks);
        desktop.Margin = new(0, 0, 0, 24);
        Row(tasks, Bind(desktop, "Desktop"));
        Row(tasks, Message("TasksHelp"));

        var review = PageFor(SetupPage.Ready);
        Row(review, summary, SizeType.Percent, 100);
        var installing = PageFor(SetupPage.Installing);
        Row(installing, progressText, SizeType.Absolute, 50);
        Row(installing, progress, SizeType.Absolute, 38);
        var complete = PageFor(SetupPage.Complete);
        Row(complete, Message("CompleteHelp"));
        Row(complete, Bind(launch, "Launch"));
        var removal = PageFor(SetupPage.Removal);
        Row(removal, Message("RemoveHelp"));
        Row(removal, new TextBox { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Top, Text = destination.Text }, SizeType.Absolute, 44);
    }
    private void Translate()
    {
        Text = text["Setup"];
        foreach (var (control, key) in translations) control.Text = text[key];
        version.Text = text["Version"] + " " + typeof(SetupForm).Assembly.GetName().Version?.ToString(3);
        back.Text = text["Back"];
        cancel.Text = text["Cancel"];
        SetHeading();
        UpdateActions();
    }
    private void SetHeading()
    {
        string stem = page switch
        {
            SetupPage.Language => "Language",
            SetupPage.License => "License",
            SetupPage.Destination => "Destination",
            SetupPage.StartMenu => "Menu",
            SetupPage.Tasks => "Tasks",
            SetupPage.Ready => "Ready",
            SetupPage.Installing => "Installing",
            SetupPage.Complete => "Complete",
            _ => "Remove"
        };
        title.Text = text[stem + "Title"];
        introduction.Text = removalComplete ? text["Removed"] : text[stem + "Intro"];
    }
    private void ShowPage(SetupPage target)
    {
        page = target;
        content.Controls.Clear();
        content.Controls.Add(pages[target]);
        error.Visible = false;
        error.Text = "";
        if (target == SetupPage.Ready) summary.Text = Summary();
        SetHeading();
        UpdateActions();
        if (Visible) next.Focus();
    }
    private string Summary() => string.Join(Environment.NewLine + Environment.NewLine,
        text["Destination"] + Environment.NewLine + destination.Text,
        text["MenuLabel"] + Environment.NewLine + (createMenu.Checked ? menu.Text : text["None"]),
        text["Desktop"] + Environment.NewLine + text[desktop.Checked ? "Yes" : "No"],
        text["LanguageLabel"] + Environment.NewLine + language.SelectedItem);
    private void UpdateActions()
    {
        back.Visible = !uninstallOnly && page is > SetupPage.Language and < SetupPage.Installing;
        back.Enabled = !busy && installed is null;
        next.Enabled = !busy && (page != SetupPage.License || licenseAvailable && accept.Checked);
        next.Visible = page != SetupPage.Installing;
        next.Text = text[page switch { SetupPage.Ready => installed is null ? "Install" : "Retry", SetupPage.Complete => "Finish", SetupPage.Removal => removalComplete ? "Close" : "Remove", _ => "Next" }];
        cancel.Visible = page != SetupPage.Complete && !removalComplete;
        cancel.Enabled = !registering;
    }
    private void MoveBack()
    {
        if (busy || installed is not null || page <= SetupPage.Language || page >= SetupPage.Installing) return;
        ShowPage(page - 1);
    }
    private async Task MoveNextAsync()
    {
        if (!next.Enabled || busy) return;
        switch (page)
        {
            case SetupPage.Ready: await InstallAsync(); return;
            case SetupPage.Complete: if (!launch.Checked || OpenInstalled()) Close(); return;
            case SetupPage.Removal: if (removalComplete) Close(); else await RemoveAsync(); return;
            case SetupPage.Destination:
                if (!ValidateDestination()) return;
                break;
            case SetupPage.StartMenu:
                try { WindowsIntegration.ValidateMenuFolder(createMenu.Checked ? menu.Text : ""); }
                catch (ArgumentException) { ShowError(text["MenuInvalid"]); return; }
                break;
        }
        if (page < SetupPage.Ready) ShowPage(page + 1);
    }
    private bool ValidateDestination()
    {
        if (string.IsNullOrWhiteSpace(destination.Text) || !Path.IsPathFullyQualified(destination.Text))
        { ShowError(text["FolderRequired"]); return false; }
        try
        {
            string root = Path.GetFullPath(destination.Text);
            if (File.Exists(root) || Path.GetPathRoot(root) == root || root.StartsWith(package.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), package.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(text["FolderInvalid"]);
            WindowsIntegration.ValidateRoot(root, typeof(SetupForm).Assembly.GetName().Version!.ToString(3));
            _ = Installation.ReadInstalled(root);
            destination.Text = root;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { ShowError(text["FolderInvalid"], ex); return false; }
    }
    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = text["DestinationIntro"], UseDescriptionForTitle = true, SelectedPath = destination.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
    }
    private void BrowseMenu()
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        using var dialog = new FolderBrowserDialog { Description = text["MenuIntro"], UseDescriptionForTitle = true, SelectedPath = programs };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string selected = Path.GetRelativePath(programs, dialog.SelectedPath);
        try { WindowsIntegration.ValidateMenuFolder(selected); menu.Text = selected; }
        catch (ArgumentException) { ShowError(text["MenuInvalid"]); }
    }
    private WindowsIntegration Integration() => checkIntegration ?? WindowsIntegration.ForInstallation(createMenu.Checked ? menu.Text : "", text.Code);
    private async Task InstallAsync()
    {
        if (!licenseAvailable || !accept.Checked || !ValidateDestination()) return;
        string root = committedRoot ?? destination.Text;
        WindowsIntegration integration;
        PackageManifest manifest;
        try { integration = Integration(); manifest = Installation.ReadPackage(package); integration.ValidateRegistration(root, desktop.Checked); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { ShowError(text["InstallFailed"], ex); return; }
        ShowPage(SetupPage.Installing);
        busy = true;
        progress.Value = 0;
        progress.Style = ProgressBarStyle.Marquee;
        progressText.Text = text["Verifying"];
        cancellation = new();
        UpdateActions();
        var token = cancellation.Token;
        int copied = 0;
        var updates = new Progress<string>(message =>
        {
            if (IsDisposed || page != SetupPage.Installing || token.IsCancellationRequested) return;
            if (message.StartsWith("Installing ", StringComparison.Ordinal))
            {
                progress.Style = ProgressBarStyle.Continuous;
                progress.Value = Math.Min(95, ++copied * 95 / manifest.Files.Length);
                progressText.Text = text["Copying"];
            }
        });
        try
        {
            installed ??= await Task.Run(() => Installation.Install(package, root, updates, token));
            committedRoot = root;
            registering = true;
            progress.Style = ProgressBarStyle.Marquee;
            progressText.Text = text["Registering"];
            UpdateActions();
            await Task.Run(() => integration.Register(package, root, installed, desktop.Checked));
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 100;
            if (checkIntegration is null || languagePreferencePath is not null) LanguagePreference.Save(AppText.FromCode(text.Code), languagePreferencePath);
            ShowPage(SetupPage.Complete);
        }
        catch (OperationCanceledException) { ShowPage(SetupPage.Ready); ShowError(text["Cancelled"]); }
        catch (Exception ex) { ShowPage(SetupPage.Ready); ShowError(text["InstallFailed"], ex); }
        finally { busy = registering = false; cancellation.Dispose(); cancellation = null; UpdateActions(); }
    }
    private async Task RemoveAsync()
    {
        if (MessageBox.Show(this, text["RemoveQuestion"], text["RemoveTitle"], MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        busy = registering = true;
        UpdateActions();
        try
        {
            string root = Path.GetFullPath(destination.Text);
            var integration = WindowsIntegration.ForRemoval(root);
            introduction.Text = text["Removing"];
            await Task.Run(() => { Installation.Uninstall(root); integration.Unregister(root); });
            removalComplete = true;
            introduction.Text = text["Removed"];
        }
        catch (Exception ex) { ShowError(text["RemoveFailed"], ex); }
        finally { busy = registering = false; UpdateActions(); }
    }
    private void ShowError(string message, Exception? exception = null)
    {
        error.Tag = exception;
        error.Text = exception is null ? message : message + Environment.NewLine + text["Details"] + " · " + exception.Message;
        error.Visible = true;
        error.AccessibleName = message;
    }
    private bool OpenInstalled()
    {
        try
        {
            string root = committedRoot ?? destination.Text;
            var receipt = Installation.ReadInstalled(root) ?? throw new IOException();
            Process.Start(new ProcessStartInfo(Installation.Executable(root, receipt)) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { ShowError(text["OpenFailed"], ex); return false; }
    }
    private void ConfirmClosing(object? sender, FormClosingEventArgs e)
    {
        if (registering) { e.Cancel = true; return; }
        if (busy)
        {
            e.Cancel = true;
            if (cancellation is not null && !cancellation.IsCancellationRequested &&
                MessageBox.Show(this, text["CancelInstall"], Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { cancellation.Cancel(); progressText.Text = text["Cancelling"]; cancel.Enabled = false; }
            return;
        }
        if (!uninstallOnly && page != SetupPage.Complete && e.CloseReason != CloseReason.WindowsShutDown)
            e.Cancel = MessageBox.Show(this, text["CancelQuestion"], Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ClientBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out ClientBounds bounds);
    internal void FitClientToWindow()
    {
        if (GetClientRect(Handle, out var bounds))
        {
            var actual = new Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
            if (actual.Width > 0 && actual.Height > 0 && (ClientSize.Width > actual.Width || ClientSize.Height > actual.Height)) ClientSize = actual;
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var view in pages.Values) view.Dispose();
            logo.Dispose();
            foreach (var font in ownedFonts) font.Dispose();
        }
        base.Dispose(disposing);
    }
}
