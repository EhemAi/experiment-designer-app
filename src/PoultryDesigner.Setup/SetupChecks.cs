using PoultryDesigner.AppCore;

namespace PoultryDesigner.Setup;

internal static class SetupChecks
{
    internal static int Run(string package, string workspace)
    {
        string output = Path.Combine(workspace, "analysis", "setup-wizard-validation");
        Directory.CreateDirectory(output);
        int result = 1;
        using var runner = new Form { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
        runner.Shown += (_, _) => runner.BeginInvoke(() =>
        {
            try
            {
                int count = SetupForm.CheckWizard(package, output);
                File.WriteAllText(Path.Combine(output, "check-results.txt"), $"PASS: {count} setup wizard assertions across English, Spanish (Spain), Spanish (Mexico), four display scales, installation, cancellation and retry.\n");
                result = 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "check-results.txt"), "FAIL: " + ex); }
            finally { runner.Close(); }
        });
        Application.Run(runner);
        return result;
    }
}

internal sealed partial class SetupForm
{
    internal static int CheckWizard(string package, string output)
    {
        int checks = 0;
        void Check(bool passed, string message)
        {
            checks++;
            if (!passed) throw new InvalidOperationException(message);
        }
        void Pump(Func<bool> done)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!done())
            {
                Application.DoEvents();
                if (timer.Elapsed > TimeSpan.FromSeconds(90)) throw new TimeoutException("Setup workflow timed out.");
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }
        static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        void Render(SetupForm form, string filename)
        {
            form.PerformLayout();
            Application.DoEvents();
            var viewport = form.RectangleToScreen(form.ClientRectangle);
            foreach (var control in Descendants(form).Where(c => c.Visible && c is Button or RadioButton or CheckBox or ComboBox or TextBox or RichTextBox))
            {
                var rectangle = control.RectangleToScreen(control.ClientRectangle);
                Check(viewport.Contains(rectangle), $"Cropped setup control at {filename}: {control.Text}");
                if (control is Button or CheckBox or RadioButton)
                {
                    var required = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine);
                    int extra = control is Button ? 12 : 24;
                    Check(control.Width >= required.Width + extra - 6, $"Truncated setup action at {filename}: {control.Text}");
                }
            }
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, filename), System.Drawing.Imaging.ImageFormat.Png);
        }
        foreach (SetupLanguage locale in Enum.GetValues<SetupLanguage>())
        {
            var localized = new SetupStrings(locale);
            Check(SetupStrings.Keys.All(key => !string.IsNullOrWhiteSpace(localized[key])), "Missing translation.");
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                string root = Path.Combine(Path.GetTempPath(), "ED-wizard-" + Guid.NewGuid().ToString("N"));
                using var form = new SetupForm(root, packageRoot: package) { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
                form.Show();
                Application.DoEvents();
                form.language.SelectedIndex = (int)locale;
                if (scale != 1)
                {
                    form.Scale(new SizeF(scale, scale));
                    form.ClientSize = new((int)(820 * scale), (int)(590 * scale));
                    form.FitClientToWindow();
                }
                string prefix = localized.Code + "-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Check(form.language.Items.Count == 3 && form.Language == locale, "Exactly three setup languages.");
                Render(form, prefix + "-language.png");
                form.next.PerformClick(); Application.DoEvents();
                Check(form.Page == SetupPage.License && !form.next.Enabled, "MIT acceptance gates installation.");
                Check(form.license.Text.Contains("Copyright (c) 2026 Emmanuel O. Alagbe") && form.license.Text.Contains("MIT License"), "Canonical MIT license and developer credit.");
                Render(form, prefix + "-license.png");
                form.accept.Checked = true;
                form.next.PerformClick(); Application.DoEvents();
                Check(form.Page == SetupPage.Destination, "Accepted license advances to destination.");
                form.destination.Text = "relative-folder";
                form.next.PerformClick(); Application.DoEvents();
                Check(form.Page == SetupPage.Destination && form.error.Visible, "Relative destinations refused before installation.");
                form.destination.Text = root;
                form.ShowPage(SetupPage.Destination);
                Render(form, prefix + "-destination.png");
                form.next.PerformClick(); Application.DoEvents();
                Check(form.Page == SetupPage.StartMenu, "Absolute destination accepted.");
                form.menu.Text = "../outside";
                form.next.PerformClick(); Application.DoEvents();
                Check(form.Page == SetupPage.StartMenu && form.error.Visible, "Start Menu traversal refused.");
                form.menu.Text = "Research tools";
                form.createMenu.Checked = false;
                Check(!form.menu.Enabled, "Optional Start Menu shortcut can be disabled.");
                form.createMenu.Checked = true;
                form.ShowPage(SetupPage.StartMenu);
                Render(form, prefix + "-start-menu.png");
                form.next.PerformClick(); Application.DoEvents();
                form.desktop.Checked = false;
                Render(form, prefix + "-tasks.png");
                form.next.PerformClick(); Application.DoEvents();
                Check(form.Page == SetupPage.Ready && form.summary.Text.Contains(root) && form.summary.Text.Contains("Research tools"), "Ready page reflects destination and shortcut choices.");
                Render(form, prefix + "-ready.png");
                form.back.PerformClick(); Application.DoEvents();
                Check(!form.desktop.Checked && form.Page == SetupPage.Tasks, "Back preserves options.");
                form.back.PerformClick(); Application.DoEvents();
                form.back.PerformClick(); Application.DoEvents();
                form.back.PerformClick(); Application.DoEvents();
                Check(form.Page == SetupPage.License && form.accept.Checked, "Back preserves license acceptance.");
                form.decline.Checked = true;
                Check(!form.next.Enabled, "Declining again disables Next.");
                form.ShowPage(SetupPage.Installing);
                form.progress.Value = 50;
                form.progressText.Text = localized["Copying"];
                Render(form, prefix + "-installing.png");
                form.ShowPage(SetupPage.Complete);
                Render(form, prefix + "-complete.png");
            }

            // Run the actual wizard entry points with an isolated Windows registration.
            string fixture = Path.Combine(Path.GetTempPath(), "ED-wizard-install-" + Guid.NewGuid().ToString("N"));
            string installRoot = Path.Combine(fixture, "Installed with spaces – á");
            string programs = Path.Combine(fixture, "Programs"), desktopFolder = Path.Combine(fixture, "Desktop");
            var integration = new WindowsIntegration("ExperimentDesigner.Test-" + Guid.NewGuid().ToString("N"), programs, desktopFolder, "Research tools", localized.Code);
            string appPreference = Path.Combine(fixture, "language.json");
            using var install = new SetupForm(installRoot, packageRoot: package, integration: integration, languagePreferencePath: appPreference) { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
            install.Show(); Application.DoEvents();
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            install.language.SelectedIndex = (int)locale;
            install.accept.Checked = true;
            install.menu.Text = "Research tools";
            install.desktop.Checked = locale != SetupLanguage.SpanishSpain;
            install.ShowPage(SetupPage.Ready);
            install.next.PerformClick();
            Check(install.busy && !install.next.Enabled, "Installing disables duplicate submissions.");
            install.cancellation!.Cancel();
            Pump(() => !install.busy);
            Check(install.Page == SetupPage.Ready && Installation.ReadInstalled(installRoot) is null, "Cancel rolls back and permits retry.");
            Check(!File.Exists(appPreference), "Cancelled installation does not change the app language preference.");
            install.next.PerformClick();
            Pump(() => !install.busy);
            Check(LanguagePreference.Load(appPreference) == AppText.FromCode(localized.Code) && File.Exists(appPreference), "Successful installation sets the initial application language.");
            Check(install.Page == SetupPage.Complete && install.error.Text.Length == 0, "Retry completes the real install. " + install.Page + " " + install.error.Text + " " + install.error.Tag);
            Check(File.Exists(Path.Combine(programs, "Research tools", "Experiment Designer.lnk")), "Chosen Start Menu folder is used.");
            Check(File.Exists(Path.Combine(desktopFolder, "Experiment Designer.lnk")) == install.desktop.Checked, "Desktop shortcut follows the choice.");
            var preferences = WindowsIntegration.Preferences(installRoot);
            Check(preferences.MenuFolder == "Research tools" && preferences.Language == localized.Code, "Maintenance remembers menu folder and language.");
            string study = Path.Combine(installRoot, "study.experimentproj");
            File.WriteAllText(study, "preserve my study");
            try
            {
                using var removal = new SetupForm(installRoot, uninstallOnly: true) { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
                removal.Show(); Application.DoEvents();
                Check(removal.Page == SetupPage.Removal && removal.Language == locale, "Uninstaller uses the installation language.");
                Render(removal, localized.Code + "-removal.png");
            }
            finally
            {
                Installation.Uninstall(installRoot);
                WindowsIntegration.ForRemoval(installRoot, test: true).Unregister(installRoot);
            }
            Check(File.ReadAllText(study) == "preserve my study" && !File.Exists(Path.Combine(programs, "Research tools", "Experiment Designer.lnk")), "Uninstall preserves study and removes custom shortcut.");
        }
        return checks;
    }
}
