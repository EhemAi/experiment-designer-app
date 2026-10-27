using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PoultryDesigner.Engine;
using PoultryDesigner.AppCore;

internal static class HousingChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool valid, string message) { checks++; if (!valid) throw new Exception("Housing: " + message); }
        void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, message); }
        string dir = Path.Combine(root, "analysis", "housing-validation", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        foreach (var design in Enum.GetValues<Design>()) foreach (var unit in new[] { "pen", "cage" })
        {
            var study = new Study(design, [], [], [new("T1", "Control"), new("T2", "Diet A"), new("T3", "Diet B")], 4, 2);
            var input = ProjectInputs.FromStudy(study, "Preparation") with { ExperimentalUnit = unit };
            var plan = HousingLayouts.Create(input, 101, 112, 25);
            Check(plan.Locations.Length == 12 && plan.Locations.Select(x => x.PhysicalPenId).Distinct().Count() == 12, "Every unit has one unique number before animals exist");
            Check(plan.Locations.All(l => (int.Parse(l.PhysicalPenId) - 101) / 3 + 1 == l.Replicate), "Randomization stays within replicate range");
            Check(plan.Locations.SequenceEqual(HousingLayouts.Create(input, 101, 112, 25).Locations), "Preparation seed reproduces exact labels");
            Check(Enumerable.Range(26, 10).Any(seed => !plan.Locations.SequenceEqual(HousingLayouts.Create(input, 101, 112, (ulong)seed).Locations)), "Seed varies housing layout");
            Reject(() => HousingLayouts.Create(input, 101, 113, 25), "Oversized range rejected");
            Reject(() => HousingLayouts.Create(input, 0, 11, 25), "Nonpositive housing numbers rejected");
            var w = new DesktopWorkspace(); w.New("Prepared " + design + " " + unit);
            w.Revise(input with { PreparedLayout = plan }, "Prepare housing layout");
            string path = Path.Combine(dir, design + "-" + unit + ".poultryproj"); w.SaveAsync(path).GetAwaiter().GetResult();
            var restored = new DesktopWorkspace(); restored.OpenAsync(path).GetAwaiter().GetResult();
            Check(restored.Inputs.PreparedLayout!.Locations.SequenceEqual(plan.Locations) && restored.Document.Events.IsEmpty && !restored.IsReady, "Incomplete study saves and reopens without fake animals or results");
            var sheets = LayoutReports.Create(w.Document.Name, plan);
            Check(sheets.Tables.Single(t => t.Name == "Allotment sheets").Rows.Length == 24 && sheets.Tables.Single(t => t.Name == "Allotment sheets").Rows.All(r => Equals(r[5], "") && Equals(r[6], "")), "Every planned animal slot has blank ID and BW");
            Check(sheets.Tables.Single(t => t.Name == "Housing labels").Rows.Length == 12, "One preparation label per housing unit");
            string html = Path.Combine(dir, design + "-" + unit + "-prepared.html"), xlsx = Path.ChangeExtension(html, ".xlsx");
            ReportExport.Html(sheets, html, true); ReportExport.Excel(sheets, xlsx, true);
            Check(File.ReadAllText(html).Contains("Allotment sheets") && File.ReadAllText(html).Contains("Housing labels"), "Preparation sheets and labels included in HTML");
            using (var zip = ZipFile.OpenRead(xlsx)) Check(zip.GetEntry("xl/worksheets/sheet3.xml") is not null, "Preparation Excel contains labels worksheet");
            var source = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW\n" + string.Join("\n", Enumerable.Range(1, 24).Select(i => $"A{i},{100 + i}"))), "allocation-day.csv");
            var imported = DataImport.Animals(source, "Delimited", 1, 2, 25, new(1, 2)).Value!;
            restored.ImportAnimals(source, imported, false, "Allocation day import"); restored.SelectExactPool("Include all imported animals");
            Check(HousingLayouts.Matches(restored.Inputs.PreparedLayout!, restored.Inputs), "Animal import and exact selection retain prepared layout");
            var run = restored.RunAsync(986, "Final animal allocation").GetAwaiter().GetResult();
            restored.Map(plan.Pens, plan.Seed, "Apply prepared layout"); var chosen = restored.Choose(restored.Document.ActiveResultId!);
            Check(chosen.Allocation.Locations.SequenceEqual(plan.Locations), "Final allocation seed cannot reshuffle prepared locations");
            Check(chosen.Allocation.Positions.SequenceEqual(run.Allocation!.Positions), "Housing mapping preserves animal allocation");
            Check(design != Design.Crd || chosen.Allocation.Blocks.IsEmpty, "CRD remains free of BW blocks");
            var report = AllocationReports.Create(chosen, false, false); var movement = report.Tables.Single(t => t.Name == "Movement");
            Check(movement.Columns[8].Name == (unit == "cage" ? "Cage number" : "Pen number") && movement.Rows.All(r => r[8] is long && plan.Pens.Any(p => p.Id == AllocationReports.Text(r[8]))), "Every final animal exports its actual numeric pen or cage number");
            Check(AllocationReports.Printable(report).Tables.Single(t => t.Name == "Movement").Columns.Any(c => c.Name == movement.Columns[8].Name), "Printed final allotment explicitly includes housing numbers");
            string final = Path.Combine(dir, design + "-" + unit + "-final.poultryproj"); restored.SaveAsync(final).GetAwaiter().GetResult();
            Check(ProjectStore.Open(final).Choose(restored.Document.ActiveResultId!).Allocation.Locations.SequenceEqual(plan.Locations), "Saved final allocation replays prepared mapping");
            Check(!HousingLayouts.Matches(plan, input with { Study = study with { AnimalsPerPen = 3 } }), "Capacity change invalidates preparation");
            Check(!HousingLayouts.Matches(plan, input with { Study = study with { Treatments = study.Treatments.Reverse().ToImmutableArray() } }), "Treatment reorder invalidates preparation");
            Reject(() => w.Revise(input with { PreparedLayout = plan with { Locations = plan.Locations.SetItem(0, plan.Locations[0] with { PhysicalPenId = "999" }) } }, "Corrupt plan"), "Tampered plan rejected before mutation");
            var old = ProjectSession.Create("Legacy no layout", input, "Create"); string legacy = Path.Combine(dir, "legacy-" + design + "-" + unit + ".poultryproj");
            ProjectStore.Save(old.Document, legacy, default);
            Check(!File.ReadAllText(legacy).Contains("PreparedLayout") && ProjectStore.Open(legacy).CurrentInput.Inputs.PreparedLayout is null, "Legacy field shape and digest preserved when no plan exists");
        }
        Console.WriteLine($"PASS: prepared layouts, blank sheets, allocation-day import, housing numbers and replay ({checks} assertions).");
        return checks;
    }
}
