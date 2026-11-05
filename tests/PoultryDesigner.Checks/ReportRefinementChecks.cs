using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class ReportRefinementChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception("Report/BW refinement: " + reason); }
        string[] oldPens = ["2", "10", "001", "Room A / 3", "1234567890123456"];
        var pool = Enumerable.Range(1, 24).Select(i => new Animal("r" + i, i.ToString(), 100 + i, OldLocation: oldPens[(i - 1) % oldPens.Length]))
            .Append(new Animal("very-light", "very-light", 1, OldLocation: "2")).ToImmutableArray();
        var study = AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(new Study(Design.Rcbd, pool, [],
            [new("T1", "Control"), new("T2", "Diet A"), new("T3", "Diet B")], 3, 2,
            FilterBalance: true, CvLimitPercent: 0, MaximumAttemptsPerBlock: 1000), "Fixture") with
        { AutomaticPoolSelection = true }).Study;
        var selected = study.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
        Check(study.Pool.Where(a => selected.Contains(a.RecordId)).Select(a => a.WeightGrams).Order().SequenceEqual(Enumerable.Range(107, 18).Select(i => (decimal)i)), "The heaviest required animals are selected; the smallest pool animals stay EX");
        bool retried = false;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var run = AllocationEngine.Run(study, seed);
            Check(run.Status == RunStatus.Success, "Exact-balance RCBD fixture completes");
            retried |= run.Attempts.Any(t => t.Attempts > 1);
            var positions = run.Allocation!.Positions;
            Check(positions.Select(p => p.AnimalRecordId).ToHashSet().SetEquals(selected), "Retry balancing never imports lighter EX animals");
            foreach (var rep in positions.GroupBy(p => p.Replicate))
            {
                decimal expectedMin = 119 - 6 * (rep.Key - 1), expectedMax = expectedMin + 5;
                var weights = rep.Select(p => pool.Single(a => a.RecordId == p.AnimalRecordId).WeightGrams).ToArray();
                Check(weights.Min() == expectedMin && weights.Max() == expectedMax, "Each replicate retains its consecutive descending BW block");
                foreach (var cage in rep.GroupBy(p => p.TreatmentIndex))
                {
                    var cageWeights = cage.Select(p => pool.Single(a => a.RecordId == p.AnimalRecordId).WeightGrams).ToArray();
                    Check(cageWeights.All(w => w >= expectedMin && w <= expectedMax) && cageWeights.Max() - cageWeights.Min() <= 5,
                        "Animals within a cage remain inside the replicate's narrow BW interval");
                    Check(cageWeights.Average() == expectedMin + 2.5m, "Cages across treatments have the same initial mean in the exact-balance fixture");
                }
            }
        }
        Check(retried, "The membership check covers actual balance retries");

        var sexPool = Enumerable.Range(1, 40).Select(i => new Animal("M" + i, "M" + i, 100 + i) { Sex = AnimalSex.Male })
            .Concat(Enumerable.Range(1, 40).Select(i => new Animal("F" + i, "F" + i, 90 + i) { Sex = AnimalSex.Female })).ToImmutableArray();
        var sexStudy = study with
        {
            Pool = sexPool,
            SelectedRecordIds = [],
            AnimalsPerPen = 4,
            FilterBalance = false,
            SexMode = SexAllocationMode.Balance,
            SexSettings = new(SexBalancePattern.SameInEveryPen, 0, 2, 0, 0)
        };
        var sexSession = ProjectSession.Create("Sex-stratified RCBD weight bands", ProjectInputs.FromStudy(sexStudy, "Fixture") with { AutomaticPoolSelection = true }, "Fixture");
        var sexInput = sexSession.CurrentInput.Inputs.Study;
        Check(sexInput.Pool.Where(a => sexInput.SelectedRecordIds.Contains(a.RecordId) && a.Sex == AnimalSex.Male).Min(a => a.WeightGrams) == 123
            && sexInput.Pool.Where(a => sexInput.SelectedRecordIds.Contains(a.RecordId) && a.Sex == AnimalSex.Female).Min(a => a.WeightGrams) == 113,
            "Sex requirements select the heaviest required animals in each sex pool");
        var sexRun = sexSession.Run(14, "Verify sex-specific BW blocks");
        Check(sexRun.Status == RunStatus.Success, "Sex-stratified RCBD fixture allocates");
        var sexById = sexInput.Pool.ToDictionary(a => a.RecordId);
        foreach (var rep in sexRun.Allocation!.Positions.GroupBy(p => p.Replicate))
        {
            foreach (AnimalSex sex in new[] { AnimalSex.Male, AnimalSex.Female })
            {
                decimal expectedMin = (sex == AnimalSex.Male ? 135 : 125) - 6 * (rep.Key - 1);
                var weights = rep.Where(p => sexById[p.AnimalRecordId].Sex == sex).Select(p => sexById[p.AnimalRecordId].WeightGrams).ToArray();
                Check(weights.Length == 6 && weights.Min() == expectedMin && weights.Max() == expectedMin + 5, "RCBD keeps consecutive descending weight bands within each required sex");
            }
        }
        var sexReport = AllocationReports.Create(sexSession.Choose(sexSession.Document.ActiveResultId!), false, false);
        var sexReplicateRows = sexReport.Tables.Single(t => t.Name == "Replicate BW").Rows;
        Check(sexReplicateRows.Select(r => (decimal)r[2]!).SequenceEqual(new[] { 132.5m, 126.5m, 120.5m })
            && sexReplicateRows.All(r => r[6] is 6 && r[7] is 6 && r[8] is 0), "Replicate BW reconciles both sex-specific bands and their composition");

        var session = ProjectSession.Create("Replicate weight demonstration", ProjectInputs.FromStudy(study, "Fixture") with { AutomaticPoolSelection = true }, "Fixture");
        Check(session.Run(1, "Allocation").Status == RunStatus.Success, "Report fixture allocates");
        var chosen = session.Choose(session.Document.ActiveResultId!); string before = JsonSerializer.Serialize(session.Document);
        var report = AllocationReports.Create(chosen, false, false);
        Check(report.Tables.Select(t => t.Name).SequenceEqual(new[] { "Summary", "Movement", "Unit BW", "Treatment balance", "Replicate BW" }), "Unedited result has five useful sheets and no physical map or empty swap log");
        var replicate = report.Tables.Single(t => t.Name == "Replicate BW");
        Check(replicate.Rows.Select(r => (decimal)r[2]!).SequenceEqual(new[] { 121.5m, 115.5m, 109.5m }), "Replicate means expose the descending BW gradient");
        foreach (var row in replicate.Rows)
        {
            int rep = (int)row[0]!; decimal min = 119 - 6 * (rep - 1);
            Check(row[1] is 6 && (decimal)row[3]! == min && (decimal)row[4]! == min + 5
                && Math.Abs((double)row[5]! - Math.Sqrt(3.5)) < 1e-12 && row[6] is 0 && row[7] is 0 && row[8] is 6,
                "Replicate animal counts, limits, independent sample SD and sex counts are correct");
        }
        var units = report.Tables.Single(t => t.Name == "Unit BW");
        Check(units.Rows.All(r => (decimal)r[12]! <= (decimal)r[5]! && (decimal)r[13]! >= (decimal)r[5]!), "Unit summary exposes each cage's actual BW range");
        var movement = report.Tables.Single(t => t.Name == "Movement");
        Check(movement.Rows.Where(r => r[3] is long).All(r => r[3] is 2L or 10L), "Canonical old pen values export as numeric values");
        Check(movement.Rows.Any(r => r[3] is "001") && movement.Rows.Any(r => r[3] is "Room A / 3") && movement.Rows.Any(r => r[3] is "1234567890123456"), "Leading-zero, text and long old pen identifiers retain their spelling");
        var numericOldPens = AllocationReports.SortMovement(report, MovementOrder.OldLocation).Tables.Single(t => t.Name == "Movement").Rows.Select(r => r[3]).OfType<long>().ToArray();
        Check(numericOldPens.SequenceEqual(numericOldPens.Order()), "Old-pen report sorting is numeric for numeric values");
        string output = Path.Combine(root, "analysis", "report-weight-validation"); Directory.CreateDirectory(output);
        string excel = Path.Combine(output, "replicate-and-cage-weights.xlsx"); ReportExport.Excel(report, excel, true);
        using (var zip = ZipFile.OpenRead(excel))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XDocument Xml(string name) { using var part = zip.GetEntry(name)!.Open(); return XDocument.Load(part); }
            var names = Xml("xl/workbook.xml").Descendants(ns + "sheet").Select(s => (string)s.Attribute("name")!).ToArray();
            Check(names.SequenceEqual(report.Tables.Select(t => t.Name)), "Actual workbook contains exactly the simplified result sheets");
            var cells = Xml("xl/worksheets/sheet2.xml").Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r")!);
            for (int row = 0; row < movement.Rows.Length; row++)
            {
                var value = movement.Rows[row][3]; var cell = cells["D" + (row + 2)];
                Check(value is long ? cell.Attribute("t") is null && cell.Element(ns + "v")?.Value == AllocationReports.Text(value)
                    : cell.Attribute("t")?.Value == "inlineStr" && string.Concat(cell.Descendants(ns + "t").Select(t => t.Value)) == AllocationReports.Text(value),
                    "Old pen cells have the correct numeric/text XLSX type");
            }
            var replicateSheet = Xml("xl/worksheets/sheet5.xml");
            Check(replicateSheet.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "C2").Element(ns + "v")?.Value == "121.5", "Replicate means are numeric Excel cells");
        }
        Check(!AllocationReports.Printable(report).Tables.Any(t => t.Name is "Physical map" or "Swap log"), "Print output also omits unnecessary result tables");
        ReportExport.Html(report, Path.Combine(output, "replicate-and-cage-weights.html"), true);
        Check(JsonSerializer.Serialize(session.Document) == before, "Summary generation and export leave the chosen allocation unchanged");

        var unfiltered = study with { FilterBalance = false };
        var edits = ProjectSession.Create("Actual swap", ProjectInputs.FromStudy(unfiltered, "Fixture") with { AutomaticPoolSelection = true }, "Fixture");
        edits.Run(1, "Allocation"); var original = edits.Choose(edits.Document.ActiveResultId!);
        var first = original.Allocation.Positions[0]; var second = original.Allocation.Positions.First(p => p.Replicate != first.Replicate);
        edits.Swap(first.AnimalRecordId, second.AnimalRecordId, "Reviewed cross-replicate swap");
        var edited = edits.Choose(edits.Document.ActiveResultId!); var editedReport = AllocationReports.Create(edited, false, false);
        Check(editedReport.Tables.Single(t => t.Name == "Swap log").Rows.Length == 1 && AllocationReports.Printable(editedReport).Tables.Any(t => t.Name == "Swap log"), "An actual swap retains its useful log");
        foreach (var row in editedReport.Tables.Single(t => t.Name == "Replicate BW").Rows)
        {
            var ids = edited.Allocation.Positions.Where(p => p.Replicate == (int)row[0]!).Select(p => p.AnimalRecordId).ToHashSet();
            Check((decimal)row[2]! == pool.Where(a => ids.Contains(a.RecordId)).Average(a => a.WeightGrams), "Replicate summaries follow the current allocation after manual swaps");
        }
        var plan = HousingLayouts.Create(chosen.Input.Inputs, 101, 109, 25);
        var preparation = LayoutReports.Create("Prepared layout demonstration", plan);
        string prepExcel = Path.Combine(output, "layout-only.xlsx"); ReportExport.Excel(preparation, prepExcel, true);
        using (var zip = ZipFile.OpenRead(prepExcel))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using var part = zip.GetEntry("xl/workbook.xml")!.Open(); var names = XDocument.Load(part).Descendants(ns + "sheet").Select(s => (string)s.Attribute("name")!).ToArray();
            Check(names.SequenceEqual(new[] { "Layout" }), "Prepared-layout Excel contains only Layout");
        }
        Check(preparation.Tables.Length == 3, "Separate print views remain available without adding Excel worksheets");
        Console.WriteLine($"PASS: concise exports and RCBD selection/BW boundaries ({checks} assertions)."); return checks;
    }
}
