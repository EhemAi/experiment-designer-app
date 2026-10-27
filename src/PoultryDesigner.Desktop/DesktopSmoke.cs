using System.Collections.Immutable;
using System.Drawing.Imaging;
using System.Text;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal static class DesktopSmoke
{
    internal static int Run(string root)
    {
        int result = 1;
        using var host = new Form { StartPosition = FormStartPosition.Manual, Location = new(-25000, -25000), Size = new(100, 100), ShowInTaskbar = false };
        host.Shown += async (_, _) => { result = await RunAsync(root); host.Close(); };
        Application.Run(host); return result;
    }
    private static async Task<int> RunAsync(string root)
    {
        string output = Path.Combine(root, "analysis", "desktop-validation"); Directory.CreateDirectory(output);
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        void Render(Form form, string name)
        {
            form.StartPosition = FormStartPosition.Manual; form.Location = new(-25000, -25000);
            form.Show(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
            form.Refresh(); Application.DoEvents();
            RedesignChecks.CheckLayout(form, Check);
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            using var full = new Bitmap(form.Width, form.Height); form.DrawToBitmap(full, new Rectangle(0, 0, form.Width, form.Height));
            // Render the client only so the platform titlebar does not obscure the app's palette.
            int border = (form.Width - form.ClientSize.Width) / 2;
            using (var graphics = Graphics.FromImage(bitmap)) graphics.DrawImage(full, new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                new Rectangle(border, form.Height - form.ClientSize.Height - border, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
            bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            IEnumerable<Control> Descend(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descend(x)));
            File.WriteAllLines(Path.Combine(output, name + "-layout.txt"), Descend(form).Where(c => c is TextBox && c.Parent is not UpDownBase)
                .Select(c => $"{c.AccessibleName}: bounds={c.Bounds}, min={c.MinimumSize}, font={c.Font.Size}, autosize={c.AutoSize}"));
            File.WriteAllLines(Path.Combine(output, name + "-controls.txt"), Descend(form).Where(c => c.Visible)
                .Select(c => $"{c.GetType().Name} {c.Text.Replace("\n", " / ")}: bounds={c.Bounds}, parent={c.Parent?.ClientSize}, preferred={c.PreferredSize}"));
            Check(bitmap.Width >= (form is SplashForm ? 350 : 500) && bitmap.Height >= (form is SplashForm ? 220 : 400), name + " has a rendered desktop surface");
        }
        try
        {
            RedesignChecks.CheckTheme(Check);
            using (var splash = new SplashForm())
            {
                splash.Loading.PreviewFrame(500); Render(splash, "startup-logo");
                Check(splash.Icon is not null && Brand.Logo.Width >= 256, "Branded launch screen and application icon load");
                Check(SplashForm.MinimumDisplayMilliseconds == 5000 && LaunchArtwork.OrbitAngle(1100) > LaunchArtwork.OrbitAngle(500),
                    "Five-second launch screen uses a smooth logo orbit animation");
                splash.Loading.PreviewFrame(1100); Render(splash, "startup-loading-right");
                foreach (float factor in new[] { 1.25f, 1.5f, 2f })
                {
                    splash.ClientSize = new((int)(600 * factor), (int)(380 * factor)); Render(splash, $"startup-scaled-{factor:0.##}");
                }
            }
            var workspace = new DesktopWorkspace();
            workspace.New("Autumn feeding study • Demonstration");
            var study = new Study(Design.Rcbd, [], [], [new("T01", "Control"), new("T02", "Diet A"), new("T03", "Diet B")], 4, 4, Demonstration: true);
            workspace.Revise(ProjectInputs.FromStudy(study, "Synthetic setup"), "Desktop smoke fixture");
            string csv = "Animal ID,BW,Cohort,Old location,Maternal ID,Paternal ID,Notes\n" + string.Join("\n", Enumerable.Range(1, 48).Select(i => $"B{i:D3},{48m + i / 4m},Demo,Room A / Source {(i - 1) / 4 + 1},M{i / 6},P{i / 8},Synthetic example"));
            var source = DataImport.Read(Encoding.UTF8.GetBytes(csv), "synthetic-desktop.csv");
            var batch = DataImport.Animals(source, "Delimited", 1, 2, 49, new(1, 2, 3, 4, 5, 6, 7), demonstration: true).Value!;
            workspace.ImportAnimals(source, batch, false, "Import synthetic pool"); workspace.SelectExactPool("Explicit synthetic complete-pool selection");
            Check(workspace.RunAsync(849, "Synthetic allocation").GetAwaiter().GetResult().Status == RunStatus.Success, "Desktop sample allocation");
            var physical = Enumerable.Range(1, 12).Select(i => new PhysicalPen($"P{i:D2}", $"Room A / Pen {i:D2}", 4, (i - 1) / 3 + 1)).ToImmutableArray();
            workspace.Map(physical, 234, "Declared physical pool");
            string file = Path.Combine(output, "desktop-example-" + Guid.NewGuid().ToString("N") + ProjectStore.ProjectExtension); workspace.SaveAsync(file).GetAwaiter().GetResult();
            using var main = new MainForm(); main.StartPosition = FormStartPosition.Manual; main.Location = new(-25000, -25000); main.Show(); Application.DoEvents();
            for (int i = 0; i < 4; i++) { main.StageForSmoke(i); Render(main, new[] { "study-empty", "animals-empty", "allocation-empty", "history-empty" }[i]); }
            main.LoadForSmoke(file); main.ExerciseCorrectionsForSmoke(file, output, Check, Render); main.ExerciseForSmoke(Check); await main.ExerciseReviewForSmoke(file, Check, Render); await main.ExerciseAnimalPagingForSmoke(file, Check);
            await main.ExerciseBalanceNoticeForSmoke(file, Check, Render);
            main.ExerciseAllTabsForSmoke(Check, Render);
            for (int i = 0; i < 4; i++) { main.StageForSmoke(i); Render(main, new[] { "study", "animals", "allocations", "history" }[i]); }
            main.ClientSize = new(1200, 780);
            for (int i = 0; i < 4; i++) { main.StageForSmoke(i); Render(main, new[] { "study-minimum", "animals-minimum", "allocations-minimum", "history-minimum" }[i]); }
            main.ClientSize = new(1450, 770);
            for (int i = 0; i < 4; i++) { main.StageForSmoke(i); Render(main, new[] { "study-laptop", "animals-laptop", "allocations-laptop", "history-laptop" }[i]); }
            foreach (var size in new[] { new Size(1366, 768), new Size(1440, 900) })
            {
                main.ClientSize = size;
                for (int i = 0; i < 4; i++) { main.StageForSmoke(i); Render(main, $"redesign-{(Theme.IsDark ? "dark" : "light")}-{size.Width}x{size.Height}-stage-{i}"); }
            }
            main.ClientSize = new(1920, 1080);
            for (int i = 0; i < 4; i++) { main.StageForSmoke(i); Render(main, new[] { "study-desktop", "animals-desktop", "allocations-desktop", "history-desktop" }[i]); }
            foreach (float factor in new[] { 1.25f, 1.5f, 2f })
            {
                using var scaled = new MainForm(); scaled.StartPosition = FormStartPosition.Manual; scaled.Location = new(-25000, -25000); scaled.Show(); scaled.LoadForSmoke(file);
                scaled.Scale(new SizeF(factor, factor)); scaled.ClientSize = new((int)(1200 * factor), (int)(780 * factor));
                foreach (int stage in new[] { 0, 1, 2, 3 }) { scaled.StageForSmoke(stage); Render(scaled, $"scaled-{factor:0.##}-stage-{stage}"); }
            }
            using (var imported = new ImportDialog(source, true, true)) { Render(imported, "import"); await imported.ExerciseForSmoke(Check).WaitAsync(TimeSpan.FromSeconds(30)); Render(imported, "import-reviewed"); }
            var treatmentSource = DataImport.Read(Encoding.UTF8.GetBytes("Treatment ID,Label\nT01,Control\nT02,Diet A\n"), "synthetic-treatments.csv");
            using (var imported = new ImportDialog(treatmentSource, false, true)) { Render(imported, "treatment-import"); await imported.ExerciseForSmoke(Check); Render(imported, "treatment-import-reviewed"); }
            var chosen = workspace.Choose(workspace.Document.ActiveResultId!);
            using (var swap = new SwapDialog(workspace, chosen)) { Render(swap, "swap"); swap.ExerciseForSmoke(workspace, Check); }
            using (var map = new MappingDialog(workspace, chosen)) { Render(map, "mapping"); map.ExerciseForSmoke(workspace, Check); }
            using (var edit = new AnimalEditDialog(workspace.Inputs, batch.Animals[0].RecordId)) Render(edit, "animal-edit");
            using (var reports = new ReportDialog(chosen, false, false, file))
            {
                Render(reports, "reports");
                var report = reports.ReportForSmoke;
                ReportExport.Excel(report, Path.Combine(output, "example-allocation.xlsx"), true);
                ReportExport.Html(report, Path.Combine(output, "example-allocation.html"), true, Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts"));
                using var printing = new ReportPrinting(report);
                Check(printing.Pages.Count > report.Tables.Length / 2, "Print report paginates all tables.");
                for (int i = 0; i < printing.Pages.Count; i++) { using var page = printing.Render(i); page.Save(Path.Combine(output, $"report-page-{i + 1:D3}.png")); }
                int movementLines = printing.Pages.Where(x => x.Table.Name == "Movement").SelectMany(x => x.Rows).Count(x => !x.Header && !x.Continuation);
                Check(movementLines == 48, "Printed movement includes every animal without duplicate rows.");
                Check(printing.Pages.All(p => p.Rows[0].Header), "Print headers repeat on every page.");
                using var document = printing.Document(); Check(document.DefaultPageSettings.Landscape, "Native print uses landscape.");
                using var preview = ReportDialog.Preview(document);
                Check(preview.PrintPreviewControl.AutoZoom && preview.PrintPreviewControl.Rows == 1 && preview.PrintPreviewControl.Columns == 1,
                    "Print preview opens with one complete page fitted to its viewport");
                foreach (var pageBounds in new[] { new Rectangle(0, 0, 1100, 850), new Rectangle(0, 0, 1169, 827), new Rectangle(0, 0, 850, 1100) })
                {
                    var place = ReportPrinting.PagePlacement(pageBounds, PointF.Empty);
                    Check(Math.Abs(place.Width / place.Height - 792f / 612) < .0001 && place.X >= 0 && place.Y >= 0
                        && place.Right <= pageBounds.Width * .72f + .01 && place.Bottom <= pageBounds.Height * .72f + .01,
                        "Printer page fitting preserves aspect ratio and all four edges: " + pageBounds.Size);
                }
                if (document.PrinterSettings.IsValid)
                {
                    var controller = new System.Drawing.Printing.PreviewPrintController { UseAntiAlias = true };
                    document.PrintController = controller; document.Print();
                    var nativePages = controller.GetPreviewPageInfo();
                    Check(nativePages.Length == printing.Pages.Count, "Native preview controller generates every report page without a printer job");
                    using var image = new Bitmap(1584, 1224); using (var g = Graphics.FromImage(image)) { g.Clear(Color.White); g.DrawImage(nativePages[0].Image, new Rectangle(0, 0, image.Width, image.Height)); }
                    image.Save(Path.Combine(output, "native-preview-page.png"));
                    foreach (var nativePage in nativePages) nativePage.Image.Dispose();
                }
                var originalMovement = report.Tables.Single(x => x.Name == "Movement");
                var longer = report with
                {
                    Tables = report.Tables.Replace(originalMovement,
                    originalMovement with { Rows = Enumerable.Range(0, 12).SelectMany(_ => originalMovement.Rows).ToImmutableArray() })
                };
                using var longerPrint = new ReportPrinting(longer);
                Check(longerPrint.Pages.Where(x => x.Table.Name == "Movement").SelectMany(x => x.Rows).Count(x => !x.Header && !x.Continuation) == 576,
                    "Print pagination retains more than one display page of animals");
                var firstTable = report.Tables[0]; string longNote = new string('W', 10000);
                var taller = report with { Tables = report.Tables.SetItem(0, firstTable with { Rows = [["Long note", longNote]] }) };
                using var tallPrint = new ReportPrinting(taller);
                var tallRows = tallPrint.Pages.Where(x => x.Table.Name == "Summary").SelectMany(x => x.Rows).Where(x => !x.Header).ToArray();
                Check(tallRows.Length > 1 && tallRows.Skip(1).All(x => x.Continuation), "Oversize print row continues onto additional pages");
                Check(string.Concat(tallRows.SelectMany(x => x.Cells[1].Skip(x.FirstLine).Take(x.LineCount))) == longNote, "Continued print row loses no text");
                using var tallPage = tallPrint.Render(1); tallPage.Save(Path.Combine(output, "report-long-row.png"));
            }
            Check(Theme.BodyFamily == "Manrope" && Theme.HeadingFamily == "Poppins", "Professional requested font families are installed and selected");
            var preparation = ProjectInputs.FromStudy(new Study(Design.Rcbd, [], [],
                Enumerable.Range(1, 6).Select(i => new Treatment($"T{i}", i == 1 ? "Control" : $"Diet {i - 1}")).ToImmutableArray(), 8, 5), "Prepared print fixture");
            var preparedReport = LayoutReports.Create("Prepared layout demonstration", HousingLayouts.Create(preparation, 101, 148, 25));
            using (var preparedPrint = new ReportPrinting(preparedReport with { Tables = [preparedReport.Tables.Single(t => t.Name == "Layout")] }))
            {
                Check(preparedPrint.Pages.SelectMany(p => p.Rows).Count(r => !r.Header && !r.Continuation) == 48, "Preparation print paginates all 48 pens without dropped or repeated rows");
                using var document = preparedPrint.Document();
                if (document.PrinterSettings.IsValid)
                {
                    var controller = new System.Drawing.Printing.PreviewPrintController { UseAntiAlias = true }; document.PrintController = controller; document.Print();
                    var native = controller.GetPreviewPageInfo(); Check(native.Length == preparedPrint.Pages.Count, "Prepared 48-pen layout generates every native preview page");
                    using var image = new Bitmap(1584, 1224); using (var g = Graphics.FromImage(image)) { g.Clear(Color.White); g.DrawImage(native[0].Image, new Rectangle(0, 0, image.Width, image.Height)); }
                    image.Save(Path.Combine(output, "prepared-native-preview-page.png")); foreach (var page in native) page.Image.Dispose();
                }
            }
            File.WriteAllText(Path.Combine(output, "check-results.txt"), $"PASS: {checks} desktop smoke assertions. Manrope body / Poppins headings.\nOwn-form renders: four main stages at 1200x780, 1450x770, 1920x1080; empty/invalid/no-results/failed/cancelled states; animal/treatment import, swap, mapping, correction and report dialogs.\n125/150/200% programmatic layout-scale renders and DPI size-token checks (96/120/144/192 DPI); physical monitor transitions were not automated.\nChecks include save/workflow state, draft-only reorder/metadata, Record-ID filters/highlights, paging offsets, historical tab/revision preservation, failed/cancelled result retention, and guarded discard.\nReports verified with native page renders, 576 movement rows and a 10,000-character continued row. No printer jobs were dispatched.\nSynthetic fixture: {file}\n");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "check-results.txt"), $"FAILED after {checks} desktop assertions: {ex}\n"); return 1; }
    }
}
