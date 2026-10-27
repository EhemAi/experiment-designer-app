using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class Section6Checks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Section 6: " + message); }
        void Reject(Action action, string message) { bool caught = false; try { action(); } catch (Exception ex) when (ex is ArgumentException or IOException or System.Xml.XmlException) { caught = true; } Check(caught, message); }
        var w = new DesktopWorkspace(); w.New("Export verification <study>");
        var study = new Study(Design.Rcbd, [], [], [new("T01", "Control"), new("T02", "Diet")], 3, 2, Demonstration: true);
        w.Revise(ProjectInputs.FromStudy(study, "Fixture"), "Fixture");
        var source = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW,Location\n" + string.Join("\n", Enumerable.Range(1, 13).Select(i => $"{(i == 1 ? "00001234567890123456789" : i == 2 ? "=2+2" : i.ToString("D3"))},{100m + i / 3m},Room A / Source {i}"))), "export.csv");
        var import = DataImport.Animals(source, "Delimited", 1, 2, 14, new(1, 2, OldLocation: 3), demonstration: true).Value!;
        w.ImportAnimals(source, import, false, "Fixture import");
        w.Select(import.Animals.Select((a, i) => new SelectionDecision(a.RecordId, i < 12 ? SelectionChoice.Include : SelectionChoice.Exclude,
            i < 12 ? "Researcher includes" : "<script>alert('x')</script>")).ToImmutableArray(), "Exact subset");
        Check(w.RunAsync(ulong.MaxValue, "Fixture run").GetAwaiter().GetResult().Status == RunStatus.Success, "Seed boundary produces export fixture.");
        var unmapped = w.Choose(w.Document.ActiveResultId!);
        var first = unmapped.Allocation.Positions[0]; var second = unmapped.Allocation.Positions.First(x => x.Replicate != first.Replicate);
        w.Swap(first.AnimalRecordId, second.AnimalRecordId, "Reviewed swap");
        w.Map(Enumerable.Range(1, 6).Select(i => new PhysicalPen($"P{i:D2}", $"Room A / Pen {i:D2}", 2, (i - 1) / 2 + 1)).ToImmutableArray(), ulong.MaxValue, "Fixture map");
        var chosen = w.Choose(w.Document.ActiveResultId!); string document = JsonSerializer.Serialize(w.Document);
        var report = AllocationReports.Create(chosen, false, false);
        foreach (var order in new[] { MovementOrder.OldLocation, MovementOrder.NewLocation })
        {
            var sorted = AllocationReports.SortMovement(report, order).Tables.Single(x => x.Name == "Movement"); int index = order == MovementOrder.OldLocation ? 3 : 9;
            Check(sorted.Rows.Select(x => AllocationReports.Text(x[index])).SequenceEqual(sorted.Rows.Select(x => AllocationReports.Text(x[index])).Order(StringComparer.Ordinal)), "Movement sorting works by " + order);
        }
        ReportTable Table(string name) => report.Tables.Single(x => x.Name == name);
        Check(Table("Movement").Rows.Length == 13, "Complete source pool exported without pagination loss.");
        Check(report.Tables.Select(t => t.Name).SequenceEqual(new[] { "Summary", "Movement", "Unit BW", "Treatment balance", "Physical map", "Swap log" }), "Result export contains exactly the six requested worksheets");
        Check(Table("Movement").Rows.Count(x => (string?)x[4] == "ResearcherExcluded") == 1, "Exclusions have a distinct disposition.");
        Check(Table("Movement").Rows.Single(x => (string?)x[4] == "ResearcherExcluded")[9] is "", "Excluded record has no invented destination.");
        Check(Table("Physical map").Rows.Length == 6 && Table("Swap log").Rows.Length == 1, "Complete physical pool and edits reflect the chosen revision.");
        Check(Table("Unit BW").Rows.Sum(x => (decimal)x[4]!) == chosen.Input.Inputs.Study.Pool.Where(x => chosen.Input.Inputs.Study.SelectedRecordIds.Contains(x.RecordId)).Sum(x => x.WeightGrams), "Export unit totals reconcile to selected animals.");
        Check(Table("Summary").Rows.Single(x => (string?)x[0] == "Allocation seed (exact text)")[1] is "18446744073709551615", "Maximum unsigned seed retained exactly as text.");
        Check(chosen.Input.Inputs.Animals.All(x => x.Origin?.Row >= 2) && chosen.Sources.Single().Sha256 == source.Sha256, "Provenance remains in the project despite being omitted from results");
        Check(Table("Unit BW").SidePanel?.Rows.Single(r => (string?)r[0] == "Treatment p-value")[1] is double, "Treatment ANOVA is attached to unit BW");
        var output = Path.Combine(root, "analysis", "report-validation"); Directory.CreateDirectory(output);
        string excel = Path.Combine(output, "chosen-allocation.xlsx"), html = Path.Combine(output, "chosen-allocation.html");
        ReportExport.Excel(report, excel, true); ReportExport.Html(report, html, true);
        using (var zip = ZipFile.OpenRead(excel))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XDocument Xml(string name) { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
            var book = Xml("xl/workbook.xml"); Check(book.Descendants(ns + "sheet").Count() == report.Tables.Length, "Valid workbook namespace and all sheets.");
            Check(book.Descendants(ns + "definedName").Count() == report.Tables.Length, "Print headers repeat on each sheet.");
            for (int i = 0; i < report.Tables.Length; i++)
            {
                var sheet = Xml($"xl/worksheets/sheet{i + 1}.xml");
                Check(sheet.Descendants(ns + "row").Count() == Math.Max(report.Tables[i].Rows.Length, report.Tables[i].SidePanel?.Rows.Length ?? 0) + 1, "Every exported row and side panel is written: " + report.Tables[i].Name);
                Check(!sheet.Descendants(ns + "f").Any(), "User text cannot become formulas: " + report.Tables[i].Name);
                Check(sheet.Descendants(ns + "pane").Single().Attribute("state")?.Value == "frozen" && sheet.Descendants(ns + "autoFilter").Any(), "Scrollable, filterable sheet: " + report.Tables[i].Name);
            }
            var move = Xml("xl/worksheets/sheet2.xml");
            Check(move.Descendants(ns + "c").Any(c => c.Attribute("t")?.Value == "inlineStr" && c.Descendants(ns + "t").Any(t => t.Value == "00001234567890123456789")), "Leading zero long IDs remain text.");
            Check(move.Descendants(ns + "c").Any(c => c.Attribute("t")?.Value == "inlineStr" && c.Descendants(ns + "t").Any(t => t.Value == "=2+2")), "Formula-like IDs remain literal text.");
            Check(move.Descendants(ns + "c").Any(c => c.Attribute("r")?.Value == "C2" && c.Element(ns + "v") is not null), "BW written as a typed number.");
            var unitSheet = Xml("xl/worksheets/sheet3.xml");
            Check(unitSheet.Descendants(ns + "c").Single(c => c.Attribute("r")?.Value == "L3").Element(ns + "v") is not null && unitSheet.Descendants(ns + "autoFilter").Single().Attribute("ref")?.Value == "A1:I7", "ANOVA p-value is numeric on the right and outside the sortable unit table");
            Check(zip.Entries.All(x => !x.FullName.Contains("vba", StringComparison.OrdinalIgnoreCase)), "Macro-free workbook.");
        }
        var read = DataImport.Read(excel); Check(read.Tables.Single(x => x.Name == "Movement").Rows.Length == 14, "Independent engine importer reads all Excel rows.");
        string markup = File.ReadAllText(html);
        Check(markup.Contains("&lt;study&gt;") && !markup.Contains("<study>"), "HTML title escaped.");
        Check(markup.Contains("window.print()") && markup.Contains("table-header-group"), "Browser print and repeating headers present.");
        Check(markup.Contains("ResearcherExcluded") && markup.Contains("00001234567890123456789"), "Printable output keeps exclusions and long identities.");
        Check(JsonSerializer.Serialize(w.Document) == document, "Export leaves project, active result and history unchanged.");
        var older = AllocationReports.Create(unmapped, true, true);
        Check(older.Context.Contains("Historical") && older.Context.Contains("earlier inputs") && older.Tables.Single(x => x.Name == "Physical map").Rows.IsEmpty,
            "Historical report uses its own unmapped snapshot and explicit status.");
        Check(older.Tables.Single(x => x.Name == "Movement").Rows.Count(x => x[9] is "Unmapped") == 12, "Unmapped allocated animals remain visible.");
        Reject(() => ReportExport.Excel(report, excel), "Existing output needs explicit overwrite.");
        Reject(() => ReportExport.Excel(report, Path.Combine(output, "source.xlsm")), "Wrong extension rejected.");
        var invalid = report with { Tables = [report.Tables[0] with { Rows = [["Invalid", new string('x', 32768)]] }] };
        byte[] before = File.ReadAllBytes(excel); Reject(() => ReportExport.Excel(invalid, excel, true), "Overlength cells rejected without silent truncation.");
        Check(before.SequenceEqual(File.ReadAllBytes(excel)), "Failed export leaves previous report intact.");
        var escaped = report with { Title = "<script>in title</script>", Tables = report.Tables.SetItem(0, report.Tables[0] with { Rows = [["<img src=x>", "<script>alert('x')</script>"]] }) };
        string security = Path.Combine(output, "escaping.html"); ReportExport.Html(escaped, security, true);
        Check(!File.ReadAllText(security).Contains("<script>") && File.ReadAllText(security).Contains("&lt;script&gt;"), "Source text cannot inject HTML scripts.");
        byte[] htmlBefore = File.ReadAllBytes(html);
        Reject(() => ReportExport.Html(report, html, true, Path.Combine(output, "missing-fonts")), "Write failure after creating temporary file is reported.");
        Check(htmlBefore.SequenceEqual(File.ReadAllBytes(html)) && !Directory.EnumerateFiles(output, ".report-*.tmp").Any(), "Failed write preserves target and removes temporary file.");
        w.Revise(w.Inputs with { Study = w.Inputs.Study with { Pool = w.Inputs.Study.Pool.SetItem(0, w.Inputs.Study.Pool[0] with { WeightGrams = 999m }) } }, "Later correction");
        Check(JsonSerializer.Serialize(AllocationReports.Create(chosen, false, false, report.GeneratedUtc)) == JsonSerializer.Serialize(report), "Later BW correction cannot affect the captured export.");
        Reject(() => AllocationReports.Create(chosen with { Input = w.Document.Inputs.Last() }, false, false), "Cannot export old allocation joined to corrected weights.");
        var largePool = Enumerable.Range(1, 900).Select(i => new Animal($"R{i:D4}", $"B{i:D4}", 45 + i / 100m)).ToImmutableArray();
        var largeStudy = new Study(Design.Rcbd, largePool, largePool.Take(864).Select(x => x.RecordId).ToImmutableArray(),
            Enumerable.Range(1, 18).Select(i => new Treatment($"T{i:D2}", $"Diet {i}")).ToImmutableArray(), 4, 12, Demonstration: true);
        var large = ProjectSession.Create("Synthetic 864-animal export", ProjectInputs.FromStudy(largeStudy, "Synthetic explicit subset"), "Fixture");
        Check(large.Run(44, "Large fixture").Status == RunStatus.Success, "Large synthetic export allocation succeeds.");
        var largeReport = AllocationReports.Create(large.Choose(large.Document.ActiveResultId!), false, false);
        string largePath = Path.Combine(output, "large-allocation.xlsx"); ReportExport.Excel(largeReport, largePath, true);
        var largeRead = DataImport.Read(largePath).Tables.Single(x => x.Name == "Movement");
        Check(largeRead.Rows.Length == 901 && largeRead.Rows.Skip(1).Count(x => x.Cells.Any(c => c.Column == 5 && c.Value == "ResearcherExcluded")) == 36,
            "Large export preserves 864 allocations and 36 exclusions beyond display page boundaries.");
        // Verify the workbook's actual cell types across final and preparation
        // exports, including IDs whose exact spelling must remain text.
        string[] treatmentIds = ["2", "10", "2632", "T01", "001", "1234567890123456", "=2+2"];
        var numericPool = Enumerable.Range(1, 15).Select(i => new Animal($"numeric-record-{i}", i.ToString(), 100 + i)).ToImmutableArray();
        var numericStudy = new Study(Design.Rcbd, numericPool, numericPool.Take(14).Select(a => a.RecordId).ToImmutableArray(),
            treatmentIds.Select(id => new Treatment(id, "Treatment " + id)).ToImmutableArray(), 2, 1);
        var numericInputs = ProjectInputs.FromStudy(numericStudy, "Numeric treatment export fixture");
        var plan = HousingLayouts.Create(numericInputs, 101, 114, 25);
        var numericSession = ProjectSession.Create("Treatment ID export fixture", numericInputs with { PreparedLayout = plan }, "Fixture");
        Check(numericSession.Run(49, "Numeric treatment fixture").Status == RunStatus.Success, "Numeric and text treatment IDs produce a valid allocation");
        numericSession.MapPens(plan.Pens, plan.Seed, "Fixture map");
        string unchangedProject = JsonSerializer.Serialize(numericSession.Document);
        var finalNumeric = AllocationReports.Create(numericSession.Choose(numericSession.Document.ActiveResultId!), false, false);
        var preparedNumeric = LayoutReports.Create("Treatment ID preparation fixture", plan);
        foreach (var (snapshot, name) in new[] { (finalNumeric, "numeric-treatment-final.xlsx"), (preparedNumeric, "numeric-treatment-prepared.xlsx") })
        {
            string path = Path.Combine(output, name); ReportExport.Excel(snapshot, path, true);
            using var zip = ZipFile.OpenRead(path); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var (table, index) in snapshot.Tables.Select((t, i) => (t, i)).Where(p => p.t.Columns.Any(c => c.Name == "Treatment ID")))
            {
                int column = Enumerable.Range(0, table.Columns.Length).Single(i => table.Columns[i].Name == "Treatment ID");
                Check(table.Rows.Select(r => r[column]).OfType<long>().Distinct().Order().SequenceEqual(new long[] { 2, 10, 2632 }),
                    "Numeric treatment IDs retain numeric sort order in " + table.Name);
                Check(table.Rows.Select(r => r[column]).OfType<string>().Where(s => s.Length > 0).Distinct().Order().SequenceEqual(treatmentIds.Skip(3).Order()),
                    "Leading-zero, long, alphanumeric and formula-like treatment IDs retain exact text in " + table.Name);
                using var stream = zip.GetEntry($"xl/worksheets/sheet{index + 1}.xml")!.Open(); var sheet = XDocument.Load(stream);
                bool numericCells = true, textCells = true, blanks = true;
                for (int row = 0; row < table.Rows.Length; row++)
                {
                    string address = ((char)('A' + column)).ToString() + (row + 2);
                    var cell = sheet.Descendants(ns + "c").SingleOrDefault(c => c.Attribute("r")?.Value == address);
                    object? value = table.Rows[row][column];
                    if (value is long id) numericCells &= cell?.Element(ns + "v")?.Value == id.ToString(System.Globalization.CultureInfo.InvariantCulture) && cell.Attribute("t") is null;
                    else if (value is string text && text.Length > 0) textCells &= cell?.Attribute("t")?.Value == "inlineStr" && cell.Descendants(ns + "t").Single().Value == text;
                    else blanks &= cell is null || string.Concat(cell.Descendants(ns + "t").Select(t => t.Value)).Length == 0;
                }
                Check(numericCells && textCells && blanks && !sheet.Descendants(ns + "f").Any(), "XLSX stores exact numeric/text/blank treatment IDs without formulas in " + table.Name);
            }
        }
        Check(JsonSerializer.Serialize(numericSession.Document) == unchangedProject && numericSession.CurrentInput.Inputs.Study.Treatments.Select(t => t.Id).SequenceEqual(treatmentIds),
            "Numeric treatment export does not change stored identities, prepared layout or allocation history");
        File.WriteAllText(Path.Combine(output, "check-results.txt"), $"PASS: {checks} export assertions. Complete snapshots, typed XLSX, identity safety, provenance, static reports and atomic writes.\n");
        Console.WriteLine($"PASS: Section 6 Excel and printable report snapshots ({checks} assertions)."); return checks;
    }
}
