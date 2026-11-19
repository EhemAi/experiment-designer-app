using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    internal static int CheckLocalization(string root)
    {
        int result = 1, checks = 0;
        string output = Path.Combine(root, "analysis", "localization-validation"); Directory.CreateDirectory(output);
        void Check(bool pass, string reason) { checks++; if (!pass) throw new InvalidOperationException(reason); }
        using var host = new Form { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
        host.Shown += async (_, _) =>
        {
            try
            {
                var fixture = new DesktopWorkspace();
                fixture.New("Summary");
                var study = new Study(Design.Rcbd, [], [], [new("Male", "Save"), new("Female", "History")], 3, 2);
                fixture.Revise(ProjectInputs.FromStudy(study, "Save"), "History");
                string csv = "ID,BW,Cohort,Old location,Sex\n" + string.Join("\n", Enumerable.Range(1, 12).Select(i => $"{(i == 1 ? "Male" : i.ToString())},{8m + i / 10m},Summary,401,{(i % 2 == 0 ? "Macho" : "Hembra")}"));
                var source = DataImport.Read(Encoding.UTF8.GetBytes(csv), "synthetic-locales.csv");
                var batch = DataImport.Animals(source, "Delimited", 1, 2, 13, new(1, 2, 3, 4, Sex: 5));
                Check(batch.Value is not null, "Spanish sex observations import.");
                fixture.ImportAnimals(source, batch.Value!, false, "Save");
                fixture.SelectExactPool("History");
                Check((await fixture.RunAsync(123, "Save")).Status == RunStatus.Success, "Localized fixture allocation succeeds.");
                var physical = Enumerable.Range(1, 6).Select(i => new PhysicalPen((100 + i).ToString(), "Summary / " + i, 2, (i - 1) / 2 + 1)).ToImmutableArray();
                fixture.Map(physical, 321, "History");
                string file = Path.Combine(output, "language-roundtrip-" + Guid.NewGuid().ToString("N") + ".experimentproj");
                await fixture.SaveAsync(file);
                var snapshot = JsonSerializer.Serialize(fixture.Document);
                var chosen = fixture.Choose(fixture.Document.ActiveResultId!);
                var canonical = AllocationReports.Create(chosen, false, false);
                string preferences = Path.Combine(output, "language.json");
                File.WriteAllText(preferences, "{broken");
                Check(LanguagePreference.Load(preferences) == AppLanguage.English, "Damaged language preference safely falls back to English.");
                foreach (AppLanguage locale in Enum.GetValues<AppLanguage>())
                {
                    AppText.Select(locale, remember: false);
                    Check(AppText.ResourceKeys.All(key => AppText.T(key).Length > 0), "No empty localized resources.");
                    Check(AppText.ResourceKeys.All(key => Regex.Matches(key, @"\{\d+\}").Select(m => m.Value).Order().SequenceEqual(
                        Regex.Matches(AppText.T(key), @"\{\d+\}").Select(m => m.Value).Order())), "Every language resource preserves its substitution fields.");
                    string guidePath = Path.Combine(root, locale == AppLanguage.English ? "User guide.html" : "User guide." + AppText.Code + ".html");
                    Check(File.Exists(Path.Combine(AppContext.BaseDirectory, Path.GetFileName(guidePath))), "Each language guide is copied to normal build and published app outputs.");
                    Check(File.Exists(guidePath) && File.ReadAllText(guidePath).Contains("id=\"language\""), "Each language has an offline guide covering the selector.");
                    if (locale != AppLanguage.English)
                    {
                        Check(AppText.T("Import 7 more animals to fill the study.").Contains("7"), "Interpolated message preserves numeric arguments.");
                        Check(AppText.T("Import 7 more animals to fill the study.") != "Import 7 more animals to fill the study.", "Interpolated messages translate.");
                        string anova = "Treatment ANOVA · p = 0.4321\nBelow balance target. Repeat with a different seed before exporting.";
                        Check(!AppText.T(anova).Contains("Below balance target") && AppText.T(anova).Contains("0.4321"), "Multiline ANOVA messages translate both lines without changing the p value.");
                        Check(!AppText.T("Below target (p <= 0.6)").Contains("â", StringComparison.Ordinal), "The balance diagnostic contains a valid mathematical symbol.");
                        Check(AppText.T("Males per pen: enter a whole number from 0 to 3.").StartsWith(AppText.T("Males per pen"), StringComparison.Ordinal), "Dynamic validation field names are translated.");
                        Check(AppText.T("1 cages required").EndsWith("1 jaula", StringComparison.Ordinal), "One cage is displayed with singular agreement.");
                        string reviewedImport = AppText.T("DEMONSTRATION DATA • Valid records: 1. Review the preview tab. Heaviest animals will be selected automatically; unused animals become EX.");
                        Check(reviewedImport.StartsWith("DATOS DE DEMOSTRACIÓN", StringComparison.Ordinal) && !reviewedImport.Contains("Valid records", StringComparison.Ordinal), "Complete import-review clauses and demonstration flags translate together.");
                        Check(AppText.T("Save already exists. Replace it?").StartsWith("Save "), "Names matching a translation key survive interpolated messages.");
                    }
                    using var main = new MainForm(preferences, Path.Combine(output, "appearance.json")) { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), ShowInTaskbar = false };
                    main.Show(); Application.DoEvents(); main.LoadForSmoke(file);
                    Check(!main.HasUnsavedWork, "Locale starts with a clean saved study.");
                    using (var unchanged = main.CreateSettingsDialog())
                    {
                        unchanged.StartPosition = FormStartPosition.Manual; unchanged.Location = new(-25000, -25000); unchanged.ShowInTaskbar = false;
                        unchanged.Show(main); Application.DoEvents(); unchanged.SaveButton.PerformClick();
                        Check(unchanged.DialogResult == DialogResult.OK && File.Exists(preferences) && File.Exists(main.appearancePreferencePath)
                            && LanguagePreference.Load(preferences) == locale && AppearancePreferences.Load(main.appearancePreferencePath) == Theme.IsDark
                            && !main.HasUnsavedWork, "Saving the current settings explicitly persists both preferences without marking the study dirty.");
                    }
                    var before = JsonSerializer.Serialize(main.Workspace.Document);
                    main.replicates.Value += 1;
                    string typed = "Save / Male / Summary";
                    main.setupReason.Text = typed; main.search.Text = "Male";
                    var destinationLocale = locale == AppLanguage.English ? AppLanguage.SpanishSpain : AppLanguage.English;
                    bool originalTheme = Theme.IsDark;
                    var preferenceBefore = File.ReadAllText(preferences);
                    Check(!main.files.Controls.OfType<ComboBox>().Any() && !main.files.Controls.Cast<Control>().Any(c => c.Text is "Light mode" or "Dark mode"), "Application preferences are absent from the workflow toolbar.");
                    Check(main.settingsButton.Visible && main.settingsButton.Enabled, "Settings is available in the sidebar.");
                    void ShowSettings(SettingsDialog dialog)
                    {
                        dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-25000, -25000); dialog.ShowInTaskbar = false;
                        dialog.Show(main); Application.DoEvents(); RedesignChecks.CheckLayout(dialog, Check);
                    }
                    using (var cancelled = main.CreateSettingsDialog())
                    {
                        ShowSettings(cancelled);
                        cancelled.LanguageSelector.SelectedIndex = (int)destinationLocale;
                        cancelled.AppearanceSelector.SelectedIndex = originalTheme ? 0 : 1;
                        Check(AppText.Language == locale && Theme.IsDark == originalTheme && File.ReadAllText(preferences) == preferenceBefore, "Editing settings stages preferences without applying or persisting them.");
                        cancelled.CancelChanges.PerformClick();
                        Check(cancelled.DialogResult == DialogResult.Cancel && AppText.Language == locale && Theme.IsDark == originalTheme, "Cancel leaves the application preferences unchanged.");
                    }
                    using (var settings = main.CreateSettingsDialog())
                    {
                        ShowSettings(settings);
                        settings.LanguageSelector.SelectedIndex = (int)destinationLocale;
                        settings.AppearanceSelector.SelectedIndex = originalTheme ? 0 : 1;
                        settings.SaveButton.PerformClick(); Application.DoEvents();
                        Check(settings.DialogResult == DialogResult.OK && Theme.IsDark != originalTheme && AppearancePreferences.Load(main.appearancePreferencePath) == !originalTheme, "Settings saves and applies the selected appearance.");
                    }
                    Check(AppText.Language == destinationLocale && LanguagePreference.Load(preferences) == destinationLocale, "Settings saves and remembers the language.");
                    Check(main.setupDirty && main.setupReason.Text == typed && main.search.Text == "Male" && JsonSerializer.Serialize(main.Workspace.Document) == before, "Saving preferences preserves uncommitted work and immutable project data.");
                    using (var restored = main.CreateSettingsDialog())
                    {
                        ShowSettings(restored);
                        Check(restored.LanguageSelector.SelectedIndex == (int)destinationLocale && restored.AppearanceSelector.SelectedIndex == (originalTheme ? 0 : 1), "Reopened Settings shows the current preferences.");
                        restored.LanguageSelector.SelectedIndex = (int)locale; restored.AppearanceSelector.SelectedIndex = originalTheme ? 1 : 0;
                        restored.SaveButton.PerformClick(); Application.DoEvents();
                    }
                    string appearanceFile = main.appearancePreferencePath!;
                    var originalAttributes = File.GetAttributes(appearanceFile);
                    try
                    {
                        File.SetAttributes(appearanceFile, originalAttributes | FileAttributes.ReadOnly);
                        using var failed = main.CreateSettingsDialog(); ShowSettings(failed);
                        failed.LanguageSelector.SelectedIndex = (int)destinationLocale;
                        failed.AppearanceSelector.SelectedIndex = originalTheme ? 0 : 1;
                        failed.SaveButton.PerformClick(); Application.DoEvents();
                        Check(failed.Visible && failed.DialogResult == DialogResult.None && AppText.Language == locale && Theme.IsDark == originalTheme,
                            "A failed preference write keeps Settings open and leaves live preferences unchanged.");
                        Check(LanguagePreference.Load(preferences) == locale, "A failed appearance save rolls back the saved language preference.");
                        failed.CancelChanges.PerformClick();
                    }
                    finally { File.SetAttributes(appearanceFile, originalAttributes); }
                    // Exercise the actual sidebar entry, including modal ownership and Escape/Cancel behavior.
                    using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                    {
                        timer.Tick += (_, _) =>
                        {
                            var opened = Application.OpenForms.OfType<SettingsDialog>().FirstOrDefault();
                            if (opened is null) return;
                            Check(opened.Owner == main, "Sidebar Settings opens as a dialog owned by the existing study window.");
                            opened.CancelChanges.PerformClick(); timer.Stop();
                        };
                        timer.Start(); main.settingsButton.PerformClick(); timer.Stop();
                    }
                    main.setupDirty = false; main.Reload();
                    for (int stage = 0; stage < 4; stage++)
                    {
                        main.SetStage(stage); Application.DoEvents();
                        RedesignChecks.CheckLayout(main, Check);
                        using var image = new Bitmap(main.Width, main.Height);
                        main.DrawToBitmap(image, new(0, 0, image.Width, image.Height));
                        image.Save(Path.Combine(output, AppText.Code + "-stage-" + stage + ".png"));
                    }
                    if (locale != AppLanguage.English)
                        Check(((LocalizedLabel)main.title).DisplayText != main.title.Text, "Stage title is rendered in the selected language.");
                    main.Size = main.MinimumSize;
                    for (int stage = 0; stage < 4; stage++)
                    {
                        main.SetStage(stage); Application.DoEvents(); RedesignChecks.CheckLayout(main, Check);
                        Check(!main.counts.Parent!.Bounds.IntersectsWith(main.navigation.Parent!.Bounds)
                            && !main.counts.Parent!.Bounds.IntersectsWith(main.settingsButton.Parent!.Bounds), $"Sidebar layout at {AppText.Code}: status {main.counts.Parent!.Bounds}; navigation {main.navigation.Parent!.Bounds}; settings {main.settingsButton.Parent!.Bounds}; viewport {main.counts.Parent!.Parent!.ClientSize}");
                        if (stage == 0)
                        {
                            using var minimum = new Bitmap(main.Width, main.Height);
                            main.DrawToBitmap(minimum, new(0, 0, minimum.Width, minimum.Height));
                            minimum.Save(Path.Combine(output, AppText.Code + "-minimum-study.png"));
                        }
                    }
                    main.SetStage(0);
                    main.replicates.Value = 4;
                    foreach (int mode in new[] { 1, 2 })
                    {
                        main.sexMode.SelectedIndex = mode;
                        if (mode == 1) main.sexPattern.SelectedIndex = 1;
                        Application.DoEvents(); RedesignChecks.CheckLayout(main, Check);
                        Check(main.sexSettingsPanel.Visible, "Sex-specific options stay available in each language at the minimum window size.");
                    }
                    main.setupDirty = false; main.Reload();
                    Check(main.treatmentTable.Columns.Contains("Treatment ID") && main.treatmentTable.Rows[0]["Label"].ToString() == "Save", "Binding keys and researcher labels remain exact.");
                    main.CloseForSmoke();
                    var dialogs = new (string Name, Form Form)[]
                    {
                        ("settings", main.CreateSettingsDialog()),
                        ("entry", new AnimalEntryDialog()), ("correction", new AnimalEditDialog(fixture.Inputs, fixture.Inputs.Study.Pool[0].RecordId)),
                        ("import", new ImportDialog(source, true, false)), ("swap", new SwapDialog(fixture, chosen)),
                        ("mapping", new MappingDialog(fixture, chosen)), ("reports", new ReportDialog(canonical, []))
                    };
                    foreach (var (name, dialog) in dialogs)
                    {
                        using (dialog)
                        {
                            string originalTitle = dialog.Text;
                            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(-25000, -25000); dialog.ShowInTaskbar = false;
                            dialog.Show(); Application.DoEvents(); RedesignChecks.CheckLayout(dialog, Check);
                            if (locale != AppLanguage.English) Check(dialog.Text == AppText.T(originalTitle), "Dialog caption follows the selected language: " + name);
                            using var image = new Bitmap(dialog.Width, dialog.Height); dialog.DrawToBitmap(image, new(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(output, AppText.Code + "-" + name + ".png")); dialog.Close();
                        }
                    }
                    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
                    {
                        using var settings = new SettingsDialog(locale, Theme.IsDark, (_, _) => { });
                        settings.StartPosition = FormStartPosition.Manual; settings.Location = new(-25000, -25000); settings.ShowInTaskbar = false;
                        settings.Show(); Application.DoEvents();
                        if (scale != 1)
                        {
                            settings.Scale(new SizeF(scale, scale));
                            settings.ClientSize = new((int)(660 * scale), (int)(440 * scale));
                        }
                        Application.DoEvents(); RedesignChecks.CheckLayout(settings, Check);
                        var bounds = settings.RectangleToScreen(settings.ClientRectangle);
                        Check(bounds.Contains(settings.SaveButton.RectangleToScreen(settings.SaveButton.ClientRectangle))
                            && bounds.Contains(settings.CancelChanges.RectangleToScreen(settings.CancelChanges.ClientRectangle)), "Settings actions remain visible at each display scale.");
                        using var bitmap = new Bitmap(settings.Width, settings.Height);
                        settings.DrawToBitmap(bitmap, new(0, 0, bitmap.Width, bitmap.Height));
                        bitmap.Save(Path.Combine(output, AppText.Code + "-settings-scale-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png"));
                        bool palette = Theme.IsDark;
                        try
                        {
                            Theme.SetDark(!palette, settings); RedesignChecks.CheckLayout(settings, Check);
                            using var alternate = new Bitmap(settings.Width, settings.Height);
                            settings.DrawToBitmap(alternate, new(0, 0, alternate.Width, alternate.Height));
                            if (scale == 1) alternate.Save(Path.Combine(output, AppText.Code + "-settings-alternate-theme.png"));
                        }
                        finally { Theme.SetDark(palette, settings); }
                        settings.Close();
                    }
                    if (locale != AppLanguage.English)
                    {
                        Check(AppText.T("About Treatment balance") == "Acerca de " + AppText.T("Treatment balance"), "Short report-summary prefixes translate together with the table name.");
                        Check(ReportLanguage.Display(canonical).Context.StartsWith("Resultado elegido activo.", StringComparison.Ordinal), "Complete report context, revisions and seed captions translate.");
                        foreach (bool demonstration in new[] { false, true })
                            foreach (bool historical in new[] { false, true })
                                foreach (bool earlier in new[] { false, true })
                                {
                                    string context = (demonstration ? "DEMONSTRATION DATA. " : "") + (historical ? "Historical" : "Active") + " chosen result. "
                                        + (earlier ? "Based on earlier inputs. " : "") + "Input revision 4; allocation revision 2; seed 123.";
                                    Check(!AppText.T(context).Contains("chosen result") && AppText.T(context).Contains("123"), "All report-context combinations translate without changing the seed.");
                                }
                    }
                    string excel = Path.Combine(output, AppText.Code + ".xlsx");
                    ReportExport.Excel(canonical, excel, overwrite: true);
                    using (var archive = ZipFile.OpenRead(excel))
                    {
                        using var workbookStream = archive.GetEntry("xl/workbook.xml")!.Open();
                        var workbook = XDocument.Load(workbookStream);
                        Check(workbook.Descendants().Any(e => e.Name.LocalName == "sheet" && (string?)e.Attribute("name") == AppText.T("Movement")), "Workbook sheet headings follow the language.");
                        var values = new List<string>(); bool numericOldLocation = false;
                        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
                        {
                            using var stream = entry.Open(); var worksheet = XDocument.Load(stream);
                            values.AddRange(worksheet.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
                            numericOldLocation |= worksheet.Descendants().Any(e => e.Name.LocalName == "c" && e.Attribute("t") is null && e.Elements().Any(v => v.Name.LocalName == "v" && v.Value == "401"));
                        }
                        Check(numericOldLocation, "Old locations remain numeric sortable Excel cells in every language.");
                        Check(values.Contains("Save") && values.Contains("History") && values.Contains("Summary"), "Researcher treatment labels, cohort and project name survive localized Excel.");
                    }
                    string html = Path.Combine(output, AppText.Code + ".html");
                    ReportExport.Html(canonical, html, overwrite: true);
                    Check(File.ReadAllText(html).Contains("lang=\"" + AppText.Code + "\""), "Printable HTML declares its language.");
                    using var print = new ReportPrinting(canonical);
                    using (var page = print.Render(0)) page.Save(Path.Combine(output, AppText.Code + "-report.png"));
                    Check(JsonSerializer.Serialize(fixture.Document) == snapshot, "Localized reports never change allocation or project data.");
                    var reopened = ProjectStore.Open(file);
                    Check(JsonSerializer.Serialize(reopened.Document) == snapshot, "Project round trip is language independent.");
                }
                File.WriteAllText(Path.Combine(output, "check-results.txt"), $"PASS: {checks} localization assertions across all three languages, Settings Save/Cancel, write-failure recovery, both themes, four settings layout scales, live draft preservation, Spanish imports, localized Excel/HTML/native reports and project replay.\n");
                result = 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "check-results.txt"), $"FAIL after {checks} assertions: " + ex); }
            finally { AppText.Select(AppLanguage.English, remember: false); host.Close(); }
        };
        Application.Run(host);
        return result;
    }
}
