using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class SexAllocationChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception("Sex allocation: " + message); }
        void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, message); }
        Study Make(Design design, SexAllocationMode mode, int males, int t = 3, int r = 4, int p = 4)
        {
            var animals = Enumerable.Range(0, t * r * p).Select(i => new Animal("R" + i.ToString("D5"), (1000 + i).ToString(), 200 - i)
            { Sex = i < males ? AnimalSex.Male : AnimalSex.Female }).ToImmutableArray();
            return new(design, animals, animals.Select(a => a.RecordId).ToImmutableArray(), Enumerable.Range(1, t).Select(i => new Treatment("T" + i, "Diet " + i)).ToImmutableArray(), r, p) { SexMode = mode };
        }
        foreach (var (text, expected) in new[] { ("m", AnimalSex.Male), (" Male ", AnimalSex.Male), ("B", AnimalSex.Male), ("f", AnimalSex.Female), ("G", AnimalSex.Female), ("female", AnimalSex.Female), ("", AnimalSex.Unknown), ("unknown", AnimalSex.Unknown), ("N/A", AnimalSex.Unknown) })
            Check(SexAllocation.Parse(text) == expected, "Import alias " + text);
        Reject(() => SexAllocation.Parse("1"), "Unrecognized sex is not guessed");
        var imported = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW,Gender\n001,123,M\n002,124,G\n003,125,\n"), "sex.csv");
        var batch = DataImport.Animals(imported, "Delimited", 1, 2, 4, new(1, 2, Sex: 3));
        Check(batch.Success && batch.Value!.Animals.Select(a => a.Sex).SequenceEqual(new[] { AnimalSex.Male, AnimalSex.Female, AnimalSex.Unknown }), "Mapped import normalizes sex and retains blank");
        var bad = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW,Sex\n001,123,typo\n"), "bad.csv");
        Check(DataImport.Animals(bad, "Delimited", 1, 2, 2, new(1, 2, Sex: 3)).Issues.Single().Field == "Sex", "Invalid sex produces row/field preview error");

        foreach (Design design in Enum.GetValues<Design>())
        {
            foreach (int males in Enumerable.Range(0, 49))
            {
                var s = Make(design, SexAllocationMode.Balance, males);
                var run = AllocationEngine.Run(s, (ulong)males);
                Check(run.Status == RunStatus.Success, $"{design} balance with {males} males succeeds");
                var a = run.Allocation!;
                Check(a.Positions.Select(x => x.AnimalRecordId).Order().SequenceEqual(s.SelectedRecordIds.Order()), "Selected membership exactly preserved");
                Check(a.Pens.Length == 12 && a.Pens.All(p => p.Count == 4), "All pens keep their size");
                var sex = s.Pool.ToDictionary(x => x.RecordId, x => x.Sex);
                int Count(IEnumerable<Position> rows) => rows.Count(p => sex[p.AnimalRecordId] == AnimalSex.Male);
                bool Even(IEnumerable<int> counts) => counts.Max() - counts.Min() <= 1;
                Check(Even(a.Positions.GroupBy(x => (x.TreatmentIndex, x.Replicate)).Select(Count)), "Pen compositions differ by at most one");
                Check(Even(a.Positions.GroupBy(x => x.TreatmentIndex).Select(Count)), "Treatment sex totals differ by at most one");
                Check(Even(a.Positions.GroupBy(x => x.Replicate).Select(Count)), "Replicate sex totals differ by at most one");
                Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(AllocationEngine.Run(s, (ulong)males).Allocation), "Seeded sex allocation replays");
                if (males % 12 == 0)
                {
                    var separate = s with { SexMode = SexAllocationMode.Separate };
                    var result = AllocationEngine.Run(separate, 12);
                    Check(result.Status == RunStatus.Success && result.Allocation!.Positions.GroupBy(x => x.Replicate).All(g => g.Select(x => sex[x.AnimalRecordId]).Distinct().Count() == 1), "Separate uses whole single-sex replicates");
                    Check(result.Allocation!.Pens.All(x => x.Count == 4), "Separate preserves capacity");
                }
                else Check(AllocationEngine.Run(s with { SexMode = SexAllocationMode.Separate }, 12).Status == RunStatus.Invalid, "Incompatible separate counts are rejected");
            }
        }
        // Unequal dimensions and odd pen sizes exercise remainder distribution.
        foreach (var dims in new[] { (2, 3, 1), (4, 3, 5), (5, 2, 3) })
            for (int males = 0; males <= dims.Item1 * dims.Item2 * dims.Item3; males++)
                Check(AllocationEngine.Run(Make(Design.Rcbd, SexAllocationMode.Balance, males, dims.Item1, dims.Item2, dims.Item3), 17).Status == RunStatus.Success, "Remainders work for varied dimensions");

        var study = Make(Design.Rcbd, SexAllocationMode.Balance, 24);
        var unknown = study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { Sex = AnimalSex.Unknown }) };
        Check(AllocationEngine.Run(unknown, 1).Status == RunStatus.Invalid, "Selected Unknown blocks sex-aware allocation");
        Check(AllocationEngine.Run(unknown with { SexMode = SexAllocationMode.Ignore }, 1).Status == RunStatus.Success, "Ignore accepts Unknown");
        var ignore = study with { SexMode = SexAllocationMode.Ignore };
        var noSex = ignore with { Pool = ignore.Pool.Select(a => a with { Sex = AnimalSex.Unknown }).ToImmutableArray() };
        Check(JsonSerializer.Serialize(AllocationEngine.Run(ignore, 5)) == JsonSerializer.Serialize(AllocationEngine.Run(noSex, 5)), "Ignore reproduces the original allocation and fingerprint exactly");
        Check(!JsonSerializer.Serialize(noSex).Contains("Sex"), "Legacy default serialization remains unchanged");
        Check(AllocationEngine.Fingerprint(study) != AllocationEngine.Fingerprint(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { Sex = AnimalSex.Female }) }), "Sex-aware fingerprint includes sex");
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Check(AllocationEngine.Run(study, 4, cancel.Token).Status == RunStatus.Cancelled, "Sex allocation cancellation"); }
        var filtered = study with { FilterBalance = true, CvLimitPercent = 100, MaximumAttemptsPerBlock = 2 };
        Check(AllocationEngine.Run(filtered, 1).Status == RunStatus.Success, "Sex-aware RCBD obeys enabled filtering");
        var equal = study with { Pool = study.Pool.Select(a => a with { WeightGrams = 100 }).ToImmutableArray(), FilterBalance = true, CvLimitPercent = 0 };
        Check(AllocationEngine.Run(equal, 1).Status == RunStatus.Success, "Zero-CV exact equality still passes");
        var resultBalanced = AllocationEngine.Run(study, 1).Allocation!;
        var lookup = study.Pool.ToDictionary(a => a.RecordId);
        var male = resultBalanced.Positions.First(x => lookup[x.AnimalRecordId].Sex == AnimalSex.Male);
        var female = resultBalanced.Positions.First(x => lookup[x.AnimalRecordId].Sex == AnimalSex.Female && (x.TreatmentIndex, x.Replicate) != (male.TreatmentIndex, male.Replicate));
        Reject(() => AllocationEngine.Swap(study, resultBalanced, male.AnimalRecordId, female.AnimalRecordId, "Invalid composition"), "Swap cannot break sex balance");
        var secondMale = resultBalanced.Positions.First(x => lookup[x.AnimalRecordId].Sex == AnimalSex.Male && x.AnimalRecordId != male.AnimalRecordId);
        Check(AllocationEngine.Swap(study, resultBalanced, male.AnimalRecordId, secondMale.AnimalRecordId, "Same sex").Edits.Length == 1, "Same-sex swaps remain available");

        // A lighter known-sex animal must never replace a selected Unknown animal.
        var extras = unknown.Pool.Add(new Animal("EX", "9999", 1) { Sex = AnimalSex.Male });
        var input = ProjectInputs.FromStudy(unknown with { Pool = extras }, "Feature checks") with { AutomaticPoolSelection = true };
        var session = ProjectSession.Create("Sex feature", input, "Feature checks");
        Check(session.CurrentInput.Inputs.Study.SelectedRecordIds.SequenceEqual(unknown.SelectedRecordIds) && !session.IsReady, "Heaviest overall retains selected Unknown and lighter EX");
        session.Revise(session.CurrentInput.Inputs with { Study = study with { Pool = study.Pool.Add(extras[^1]) } }, "Correct sex record");
        var layout = HousingLayouts.Create(session.CurrentInput.Inputs, 1, 12, 25);
        session.Revise(session.CurrentInput.Inputs with { PreparedLayout = layout }, "Prepare fixed layout");
        Check(session.Run(12, "First sex allocation").Status == RunStatus.Success, "Project allocation succeeds");
        session.MapPens(layout.Pens, layout.Seed, "Map prepared pens");
        var firstLocations = session.Choose(session.Document.ActiveResultId!).Allocation.Locations;
        Check(session.Run(13, "Rerun").Status == RunStatus.Success, "Rerun succeeds");
        session.MapPens(layout.Pens, layout.Seed, "Map same prepared pens");
        var chosen = session.Choose(session.Document.ActiveResultId!);
        Check(firstLocations.SequenceEqual(chosen.Allocation.Locations), "Prepared pen layout stays fixed across reruns");
        var report = AllocationReports.Create(chosen, false, false);
        var movement = report.Tables.Single(t => t.Name == "Movement");
        Check(movement.Columns[^1].Name == "Sex" && movement.Rows.All(r => r[^1] is "Male" or "Female" or "Unknown"), "Animal export includes normalized Sex including EX");
        Check(movement.Rows.Count(r => r[4] is "EX") == 1 && movement.Rows.Single(r => r[4] is "EX")[0] is long id && id == 9999, "EX and numeric identifiers preserved");
        Check(AllocationReports.Printable(report).Tables.Single(t => t.Name == "Movement").Columns.Any(c => c.Name == "Sex"), "Printed movement includes Sex");
        string output = Path.Combine(root, "analysis", "sex-validation"); Directory.CreateDirectory(output);
        string file = Path.Combine(output, "sex-feature-" + Guid.NewGuid().ToString("N") + ".experimentproj");
        ProjectStore.Save(session.Document, file);
        var restored = ProjectStore.Open(file);
        Check(restored.IsReady && JsonSerializer.Serialize(restored.Document) == JsonSerializer.Serialize(session.Document), "Sex, source-independent edits, history and prepared layout save/reopen/replay");
        string legacyDirectory = Path.Combine(output, "legacy"); Directory.CreateDirectory(legacyDirectory);
        foreach (string legacy in Directory.EnumerateFiles(legacyDirectory, "*.poultryproj"))
        {
            var previous = ProjectStore.Open(legacy);
            Check(previous.CurrentInput.Inputs.Study.SexMode == SexAllocationMode.Ignore && previous.CurrentInput.Inputs.Study.Pool.All(a => a.Sex == AnimalSex.Unknown), "Original saved project opens and replays without Sex fields");
            string roundtrip = Path.Combine(output, "legacy-roundtrip-" + Guid.NewGuid().ToString("N") + ".experimentproj");
            ProjectStore.Save(previous.Document, roundtrip);
            Check(JsonSerializer.Serialize(ProjectStore.Open(roundtrip).Document) == JsonSerializer.Serialize(previous.Document), "Legacy history survives save/reopen with new app");
        }
        ReportExport.Excel(report, Path.Combine(output, "sex-feature.xlsx"), true);
        ReportExport.Html(report, Path.Combine(output, "sex-feature.html"), true);
        var importSession = ProjectSession.Create("Sex import provenance", ProjectInputs.FromStudy(
            new Study(Design.Crd, [], [], [new("T1", "A"), new("T2", "B")], 2, 1) { SexMode = SexAllocationMode.Balance }, "Setup"), "Setup");
        var sexSource = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW,Sex\n0001,100,M\n0002,101,F\n0003,102,B\n0004,103,G\n0005,1,\n"), "mapped-sex.csv");
        var sexBatch = DataImport.Animals(sexSource, "Delimited", 1, 2, 6, new(1, 2, Sex: 3)).Value!;
        importSession.ImportAnimals(sexSource, sexBatch, false, "Reviewed sex mapping");
        importSession.Revise(importSession.CurrentInput.Inputs with { AutomaticPoolSelection = true }, "Heaviest overall");
        Check(importSession.IsReady && importSession.Run(3, "Mapped sex run").Status == RunStatus.Success, "Unknown in unused extras is allowed");
        string mappedFile = Path.Combine(output, "mapped-sex-" + Guid.NewGuid().ToString("N") + ".experimentproj"); ProjectStore.Save(importSession.Document, mappedFile);
        Check(ProjectStore.Open(mappedFile).CurrentInput.Inputs.Study.Pool.Select(a => a.Sex).SequenceEqual(sexBatch.Animals.Select(a => a.Sex)), "Retained sex mapping, aliases and original source replay on reopen");
        Console.WriteLine($"PASS: animal sex feature ({checks} assertions).");
        return checks;
    }
}
