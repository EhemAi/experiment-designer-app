using System.Collections.Immutable;
using System.Text.Json;
using System.Text;
using System.Globalization;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class SexSettingsChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception("Sex settings: " + reason); }
        var pool = Enumerable.Range(1, 70).Select(i => new Animal($"M{i:D3}", $"M{i:D3}", 300 + i) { Sex = AnimalSex.Male })
            .Concat(Enumerable.Range(1, 70).Select(i => new Animal($"F{i:D3}", $"F{i:D3}", 100 + i) { Sex = AnimalSex.Female }))
            .Append(new Animal("U", "U", 999) { Sex = AnimalSex.Unknown }).ToImmutableArray();
        var treatments = ImmutableArray.Create(new Treatment("T1", "A"), new Treatment("T2", "B"), new Treatment("T3", "C"));
        Study Make(Design design, SexAllocationMode mode, SexAllocationSettings settings) =>
            AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(new(design, pool, [], treatments, 4, 4) { SexMode = mode, SexSettings = settings }, "Settings test")
                with
            { AutomaticPoolSelection = true }).Study;
        Allocation Run(Study s, ulong seed = 12)
        {
            var result = AllocationEngine.Run(s, seed);
            Check(result.Status == RunStatus.Success, "Specified composition allocates: " + string.Join(" ", result.Errors));
            var expected = SexAllocation.RequiredCounts(s); var selected = s.Pool.Where(a => s.SelectedRecordIds.Contains(a.RecordId)).ToArray();
            Check(selected.Count(a => a.Sex == AnimalSex.Male) == expected.Males && selected.Count(a => a.Sex == AnimalSex.Female) == expected.Females, "Exact selected male/female requirements");
            foreach (AnimalSex sex in new[] { AnimalSex.Male, AnimalSex.Female })
            {
                var chosen = selected.Where(a => a.Sex == sex).ToArray();
                var unused = s.Pool.Where(a => a.Sex == sex && !s.SelectedRecordIds.Contains(a.RecordId)).ToArray();
                Check(chosen.Length == 0 || unused.Length == 0 || chosen.Min(a => a.WeightGrams) >= unused.Max(a => a.WeightGrams), "Heaviest animals within each specified sex count");
            }
            var a = result.Allocation!; var sexes = s.Pool.ToDictionary(x => x.RecordId, x => x.Sex);
            Check(a.Positions.Length == 48 && a.Positions.Select(p => p.AnimalRecordId).Distinct().Count() == 48 && a.Pens.All(p => p.Count == 4), "Membership and all pen capacities preserved");
            foreach (var pen in a.Positions.GroupBy(p => (p.TreatmentIndex, p.Replicate)))
            {
                var options = s.SexSettings!;
                int maleCount = s.SexMode == SexAllocationMode.Separate ? pen.Key.Replicate <= options.MaleReplicates ? 4 : 0
                    : options.Pattern == SexBalancePattern.TwoReplicateGroups && pen.Key.Replicate > options.FirstGroupReplicates ? options.SecondGroupMalesPerPen : options.MalesPerPen;
                Check(pen.Count(p => sexes[p.AnimalRecordId] == AnimalSex.Male) == maleCount, "Every pen follows the composition for its replicate range");
            }
            Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(AllocationEngine.Run(s, seed).Allocation), "Settings and seed replay exactly");
            return a;
        }
        foreach (Design design in Enum.GetValues<Design>())
        {
            for (int males = 0; males <= 4; males++) Run(Make(design, SexAllocationMode.Balance, new(SexBalancePattern.SameInEveryPen, 0, males, 0, 0)));
            for (int split = 1; split <= 3; split++)
                for (int males1 = 0; males1 <= 4; males1++)
                    for (int males2 = 0; males2 <= 4; males2++) Run(Make(design, SexAllocationMode.Balance, new(SexBalancePattern.TwoReplicateGroups, 0, males1, split, males2)));
            for (int maleReps = 0; maleReps <= 4; maleReps++) Run(Make(design, SexAllocationMode.Separate, new(SexBalancePattern.SameInEveryPen, maleReps, 0, 0, 0)));
        }
        var grouped = Make(Design.Rcbd, SexAllocationMode.Balance, new(SexBalancePattern.TwoReplicateGroups, 0, 1, 2, 3));
        var allocation = Run(grouped);
        var changed = grouped with { SexSettings = grouped.SexSettings! with { MalesPerPen = 3, SecondGroupMalesPerPen = 1 } };
        Check(SexAllocation.RequiredCounts(grouped) == SexAllocation.RequiredCounts(changed) && AllocationEngine.Fingerprint(grouped) != AllocationEngine.Fingerprint(changed), "Changing group placement invalidates allocation even when sex totals match");
        var byId = grouped.Pool.ToDictionary(a => a.RecordId);
        var male = allocation.Positions.First(p => p.Replicate == 1 && byId[p.AnimalRecordId].Sex == AnimalSex.Male);
        var female = allocation.Positions.First(p => p.Replicate == 3 && byId[p.AnimalRecordId].Sex == AnimalSex.Female);
        bool rejected = false;
        try { AllocationEngine.Swap(grouped, allocation, male.AnimalRecordId, female.AnimalRecordId, "Break group composition"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Swap cannot violate the specified group composition");
        foreach (var invalid in new[] {
            grouped with { SexSettings = grouped.SexSettings! with { MalesPerPen = 5 } },
            grouped with { SexSettings = grouped.SexSettings! with { SecondGroupMalesPerPen = -1 } },
            grouped with { SexSettings = grouped.SexSettings! with { FirstGroupReplicates = 0 } },
            grouped with { SexSettings = grouped.SexSettings! with { FirstGroupReplicates = 4 } },
            grouped with { SexSettings = grouped.SexSettings! with { Pattern = (SexBalancePattern)100 } },
            grouped with { SexMode = SexAllocationMode.Separate, SexSettings = grouped.SexSettings! with { MaleReplicates = 5 } }
        }) Check(AllocationEngine.Run(invalid, 1).Status == RunStatus.Invalid, "Invalid settings cannot allocate");
        var shortage = grouped with { Pool = grouped.Pool.Where(a => a.Sex != AnimalSex.Female || a.WeightGrams >= 166).ToImmutableArray() };
        var shortageInput = AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(shortage, "Shortage") with { AutomaticPoolSelection = true });
        var shortageRun = AllocationEngine.Run(shortageInput.Study, 1);
        Check(shortageRun.Status == RunStatus.Invalid && shortageRun.Errors.Any(e => e.Contains("known females")), "Insufficient female pool gives an actionable quota message");
        Check(shortageInput.Study.SelectedRecordIds.Length < 48 && !shortageInput.Study.SelectedRecordIds.Contains("U"), "Unknown or excess males never fill missing females");

        var session = ProjectSession.Create("Specified sex counts", ProjectInputs.FromStudy(grouped, "Setup") with { AutomaticPoolSelection = true }, "Setup");
        var layout = HousingLayouts.Create(session.CurrentInput.Inputs, 101, 112, 25);
        session.Revise(session.CurrentInput.Inputs with { PreparedLayout = layout }, "Prepare fixed locations");
        Check(session.Run(10, "Group composition").Status == RunStatus.Success, "Configured project runs");
        session.MapPens(layout.Pens, layout.Seed, "Fixed locations");
        var before = session.Choose(session.Document.ActiveResultId!).Allocation.Locations;
        session.Revise(session.CurrentInput.Inputs with { Study = session.CurrentInput.Inputs.Study with { SexSettings = changed.SexSettings } }, "Reverse group composition");
        Check(HousingLayouts.Matches(layout, session.CurrentInput.Inputs), "Changing sex composition preserves housing compatibility");
        Check(session.Run(11, "Revised groups").Status == RunStatus.Success, "Changed group composition runs");
        session.MapPens(layout.Pens, layout.Seed, "Keep physical locations");
        Check(before.SequenceEqual(session.Choose(session.Document.ActiveResultId!).Allocation.Locations), "Prepared treatment locations remain fixed across sex composition changes and reruns");
        string output = Path.Combine(root, "analysis", "sex-settings-validation"); Directory.CreateDirectory(output);
        string path = Path.Combine(output, "configured-" + Guid.NewGuid().ToString("N") + ".experimentproj");
        ProjectStore.Save(session.Document, path); var reopened = ProjectStore.Open(path);
        Check(JsonSerializer.Serialize(reopened.Document) == JsonSerializer.Serialize(session.Document), "Settings and both historical compositions save/reopen/replay");
        var report = AllocationReports.Create(session.Choose(session.Document.ActiveResultId!), false, false);
        Check(report.Tables.Single(t => t.Name == "Summary").Rows.Any(r => r[0] is "Sex allocation rule" && r[1] is string text && text.Contains("Replicates 1–2: 3 male + 1 female")), "Export records exact ranges and pen compositions");
        ReportExport.Excel(report, Path.Combine(output, "configured-sex-counts.xlsx"), true);
        var draftSession = ProjectSession.Create("Quota shortage draft", shortageInput, "Save incomplete quota study");
        string draftPath = Path.Combine(output, "shortage-" + Guid.NewGuid().ToString("N") + ".experimentproj");
        ProjectStore.Save(draftSession.Document, draftPath);
        Check(!ProjectStore.Open(draftPath).IsReady, "Quota shortage can be saved as a draft without becoming ready");
        // Exercise the screenshot's six-replicate, 18-animal pen size, with a
        // retained import that also serves as a runnable synthetic preview study.
        string csv = "ID,BW,Sex\n" + string.Join("\n", Enumerable.Range(1, 360).Select(i =>
            $"{1631000 + i},{(50m + i % 41 / 10m).ToString("0.0", CultureInfo.InvariantCulture)},{(i % 2 == 1 ? "M" : "F")}")) + "\n";
        var source = DataImport.Read(Encoding.UTF8.GetBytes(csv), "sex-composition-demo.csv");
        var imported = DataImport.Animals(source, "Delimited", 1, 2, 361, new(1, 2, Sex: 3)).Value!;
        var macroStudy = new Study(Design.Rcbd, [], [], treatments, 6, 18)
        {
            SexMode = SexAllocationMode.Separate,
            SexSettings = new(SexBalancePattern.SameInEveryPen, 3, 0, 0, 0)
        };
        var demo = ProjectSession.Create("Sex composition — synthetic 6-replicate example",
            ProjectInputs.FromStudy(macroStudy, "Screenshot-style setup") with { AutomaticPoolSelection = true }, "Screenshot-style setup");
        demo.ImportAnimals(source, imported, false, "Import synthetic 360-animal pool");
        var demoLayout = HousingLayouts.Create(demo.CurrentInput.Inputs, 101, 118, 25);
        demo.Revise(demo.CurrentInput.Inputs with { PreparedLayout = demoLayout }, "Prepare fixed locations");
        foreach (var (mode, options) in new[] {
            (SexAllocationMode.Separate, new SexAllocationSettings(SexBalancePattern.SameInEveryPen, 3, 0, 0, 0)),
            (SexAllocationMode.Balance, new SexAllocationSettings(SexBalancePattern.SameInEveryPen, 0, 9, 0, 0)),
            (SexAllocationMode.Balance, new SexAllocationSettings(SexBalancePattern.TwoReplicateGroups, 0, 12, 3, 6)) })
        {
            demo.Revise(demo.CurrentInput.Inputs with { Study = demo.CurrentInput.Inputs.Study with { SexMode = mode, SexSettings = options } }, "Set screenshot-style composition");
            var run = demo.Run(77, "Screenshot-style allocation");
            Check(run.Status == RunStatus.Success && run.Allocation!.Pens.All(p => p.Count == 18)
                && demo.CurrentInput.Inputs.Study.SelectedRecordIds.Length == 324, "Six replicates with 18 animals per pen allocate exactly 324 animals");
            var sexes = demo.CurrentInput.Inputs.Study.Pool.ToDictionary(a => a.RecordId, a => a.Sex);
            Check(run.Allocation!.Positions.GroupBy(p => (p.TreatmentIndex, p.Replicate)).All(g =>
                g.Count(p => sexes[p.AnimalRecordId] == AnimalSex.Male) == (mode == SexAllocationMode.Separate ? g.Key.Replicate <= 3 ? 18 : 0
                    : options.Pattern == SexBalancePattern.SameInEveryPen ? 9 : g.Key.Replicate <= 3 ? 12 : 6)), "Screenshot-style per-pen male/female counts match in every replicate");
            demo.MapPens(demoLayout.Pens, demoLayout.Seed, "Keep the prepared locations");
        }
        string demoPath = Path.Combine(output, "screenshot-style-demo-" + Guid.NewGuid().ToString("N") + ".experimentproj");
        ProjectStore.Save(demo.Document, demoPath);
        Check(ProjectStore.Open(demoPath).IsReady, "Imported screenshot-style example saves and replays all three explicit compositions");
        string previousDirectory = Path.Combine(output, "previous-preview"); Directory.CreateDirectory(previousDirectory);
        foreach (string previous in Directory.EnumerateFiles(previousDirectory, "*.experimentproj"))
        {
            var legacy = ProjectStore.Open(previous);
            Check(legacy.CurrentInput.Inputs.Study.SexSettings is null, "Previous sex preview projects replay without settings migration");
            string saved = Path.Combine(output, "previous-preview-" + Guid.NewGuid().ToString("N") + ".experimentproj");
            ProjectStore.Save(legacy.Document, saved); ProjectStore.Open(saved);
            Check(true, "Previous preview history survives save/reopen unchanged");
        }
        Console.WriteLine($"PASS: explicit sex composition controls ({checks} assertions)."); return checks;
    }
}
