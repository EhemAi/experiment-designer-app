using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.IO.Compression;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class AutomaticPoolChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Automatic pool: " + message); }
        var workspace = new DesktopWorkspace(); workspace.New("Automatic animal pool");
        workspace.Revise(ProjectInputs.FromStudy(new Study(Design.Rcbd, [], [], [new("T1", "Control"), new("T2", "Diet A"), new("T3", "Diet B")], 8, 12), "Setup"), "Setup");
        var source = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW\n" + string.Join("\n", Enumerable.Range(1, 300).Select(i => $"{1631000 + i},{100 + i}"))), "300-animals.csv");
        var batch = DataImport.Animals(source, "Delimited", 1, 2, 301, new(1, 2)).Value!;
        workspace.ImportAutomaticAnimalPool(source, batch, false, "Import full animal pool");
        Check(workspace.IsReady && workspace.Inputs.Study.Pool.Length == 300 && workspace.Inputs.Study.SelectedRecordIds.Length == 288, "300-record import is immediately ready for 288 places without manual selection");
        var selected = workspace.Inputs.Study.SelectedRecordIds.ToHashSet();
        Check(workspace.Inputs.Study.Pool.Where(a => selected.Contains(a.RecordId)).Min(a => a.WeightGrams) == 113, "The 12 lightest animals are extras; heavier weights are prioritized");
        Check(workspace.Inputs.Selection.All(d => d.Choice != SelectionChoice.Pending), "Automatic import leaves no pending Include/Exclude decisions");
        var plan = HousingLayouts.Create(workspace.Inputs, 21, 44, 44);
        workspace.Revise(workspace.Inputs with { PreparedLayout = plan }, "Prepare housing");
        Check(workspace.RunAsync(44, "Automatic allocation").GetAwaiter().GetResult().Status == RunStatus.Success, "Automatically selected pool allocates normally");
        workspace.Map(plan.Pens, plan.Seed, "Apply prepared housing");
        var chosen = workspace.Choose(workspace.Document.ActiveResultId!);
        var report = AllocationReports.Create(chosen, false, false);
        var movement = report.Tables.Single(t => t.Name == "Movement");
        Check(movement.Rows.Count(r => r[4] is "EX") == 12 && movement.Rows.Count(r => r[4] is "Allocated") == 288, "Final movement contains all 300 animals, 288 allocations and 12 EX");
        Check(movement.Rows.Where(r => r[4] is "EX").All(r => r[5] is null && r[6] is null && r[8] is null && r[9] is ""), "Extras have no invented treatment, replicate or housing");
        Check(movement.Rows.All(r => r[0] is long) && movement.Rows.Where(r => r[4] is "Allocated").All(r => r[6] is int && r[8] is long), "Plain numeric animal, replicate and pen IDs are typed as numbers");
        string output = Path.Combine(root, "analysis", "automatic-pool-validation"); Directory.CreateDirectory(output);
        string excel = Path.Combine(output, "300-animals-288-places.xlsx"); ReportExport.Excel(report, excel, true);
        using (var zip = ZipFile.OpenRead(excel))
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); var xml = XDocument.Load(stream);
            Check(xml.Descendants(ns + "c").Where(c => (string?)c.Attribute("r") is { } address && address[0] == 'A' && address != "A1").All(c => c.Element(ns + "v") is not null && c.Attribute("t") is null), "Excel Animal ID cells contain numbers rather than numeric-looking text");
        }
        string project = Path.Combine(output, "automatic-pool-" + Guid.NewGuid().ToString("N") + ".poultryproj"); workspace.SaveAsync(project).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(output, "latest-project.txt"), project);
        var restored = ProjectStore.Open(project);
        Check(restored.CurrentInput.Inputs.AutomaticPoolSelection && restored.CurrentInput.Inputs.Study.SelectedRecordIds.SequenceEqual(workspace.Inputs.Study.SelectedRecordIds), "Save/reopen retains automatic selection, source pool and EX identities");
        workspace.Revise(workspace.Inputs with { Study = workspace.Inputs.Study with { AnimalsPerPen = 10 } }, "Change capacity");
        Check(workspace.IsReady && workspace.Inputs.Study.SelectedRecordIds.Length == 240 && workspace.Inputs.Selection.Count(d => d.Choice == SelectionChoice.Exclude) == 60, "Settings changes recalculate automatic pool size");
        var small = workspace.Inputs with { Study = workspace.Inputs.Study with { AnimalsPerPen = 20 } };
        workspace.Revise(small, "Insufficient animal pool");
        Check(!workspace.IsReady && workspace.Inputs.Study.SelectedRecordIds.Length == 300, "Insufficient pool remains visible but allocation cannot invent animals");
        var old = ProjectInputs.FromStudy(new Study(Design.Crd, [], [], [], 0, 0), "Old project");
        Check(!JsonSerializer.Serialize(old).Contains("AutomaticPoolSelection"), "Legacy manual-project serialized shape remains unchanged");
        var tiedStudy = new Study(Design.Crd, Enumerable.Range(1, 30).Select(i => new Animal("R" + i, i.ToString("D3"), 100)).ToImmutableArray(), [], [new("T1", "A"), new("T2", "B")], 2, 6);
        var tied = AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(tiedStudy, "Ties") with { AutomaticPoolSelection = true });
        Check(tied.Study.SelectedRecordIds.SequenceEqual(AutomaticPoolSelection.Apply(tied).Study.SelectedRecordIds) && tied.Study.SelectedRecordIds.Length == 24, "Tied weights produce a deterministic exact-sized pool");
        File.WriteAllText(Path.Combine(output, "check-results.txt"), $"PASS: {checks} automatic pool assertions.\n");
        Console.WriteLine($"PASS: automatic heaviest-first pool and EX exports ({checks} assertions)."); return checks;
    }
}
