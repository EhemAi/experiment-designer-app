using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PoultryDesigner.Engine;
using PoultryDesigner.AppCore;

internal static class CompactCageChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception("Compact cages: " + reason); }
        string Signature(IEnumerable<decimal> values) => string.Join("|", values.OrderByDescending(x => x).Select(x => x.ToString("G29", CultureInfo.InvariantCulture)));
        double Spread(Study s, Allocation a)
        {
            var weights = s.Pool.ToDictionary(x => x.RecordId, x => x.WeightGrams);
            return a.Positions.GroupBy(p => (p.Replicate, p.TreatmentIndex)).Sum(g =>
            {
                var w = g.Select(p => weights[p.AnimalRecordId]).ToArray(); decimal m = w.Average();
                return w.Sum(v => Math.Pow((double)(v - m), 2));
            });
        }
        // Independent synthetic descending BW blocks, five animals per cage.
        decimal[] first = Enumerable.Range(0, 25).Select(i => 52m + i * 0.075m).ToArray();
        decimal[] second = Enumerable.Range(0, 25).Select(i => 48m + i * 0.06m).ToArray();
        var pool = first.Concat(second).Select((w, i) => new Animal("u" + i, (i + 1).ToString(), w, OldLocation: (401 + i % 12).ToString()) { Sex = AnimalSex.Male })
            .Append(new Animal("extra", "EX", 1) { Sex = AnimalSex.Male }).ToImmutableArray();
        var treatments = Enumerable.Range(1, 5).Select(i => new Treatment((2600 + i).ToString(), "Diet " + i)).ToImmutableArray();
        Study Make(SexAllocationMode mode) => AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(new Study(Design.Rcbd, pool, [], treatments, 2, 5)
        {
            SexMode = mode,
            SexSettings = mode == SexAllocationMode.Separate ? new(SexBalancePattern.SameInEveryPen, 2, 0, 0, 0) : null,
            CompactCages = true
        }, "Synthetic fixture") with
        { AutomaticPoolSelection = true }).Study;
        var treatmentAssignments = new HashSet<string>();
        foreach (SexAllocationMode mode in new[] { SexAllocationMode.Ignore, SexAllocationMode.Separate })
        {
            var s = Make(mode); var selected = s.SelectedRecordIds.ToHashSet(); var weights = s.Pool.ToDictionary(a => a.RecordId, a => a.WeightGrams);
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var run = AllocationEngine.Run(s, seed); Check(run.Status == RunStatus.Success, "Synthetic RCBD grouping succeeds"); var a = run.Allocation!;
                Check(a.Positions.Select(p => p.AnimalRecordId).ToHashSet().SetEquals(selected) && !selected.Contains("extra"), "Selected pool stays fixed and EX stays out");
                foreach (var rep in a.Positions.GroupBy(p => p.Replicate))
                {
                    var expected = (rep.Key == 1 ? first : second).OrderByDescending(x => x).Chunk(5).Select(Signature).Order().ToArray();
                    var actual = rep.GroupBy(p => p.TreatmentIndex).Select(g => Signature(g.Select(p => weights[p.AnimalRecordId]))).Order().ToArray();
                    Check(actual.SequenceEqual(expected), "Cages consist of consecutive closest BW ranks inside the original block");
                    Check(rep.GroupBy(p => p.TreatmentIndex).Count() == 5 && rep.GroupBy(p => p.TreatmentIndex).All(g => g.Count() == 5), "Each block has one complete cage per treatment");
                }
                Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(AllocationEngine.Run(s, seed).Allocation), "Compact grouping and treatment randomization replay exactly");
                treatmentAssignments.Add(string.Join(",", a.Pens.OrderBy(p => p.Replicate).ThenBy(p => p.TreatmentIndex).Select(p => p.MeanWeightGrams.ToString("G29", CultureInfo.InvariantCulture))));
            }
        }
        Check(treatmentAssignments.Count > 1, "Complete cages receive different treatment permutations across seeds");
        for (int maleCount = 0; maleCount <= 30; maleCount++)
        {
            var legacyPool = Enumerable.Range(0, 30).Select(i => new Animal("legacy" + i, "legacy" + i, 10 + i / 10m) { Sex = i < maleCount ? AnimalSex.Male : AnimalSex.Female }).ToImmutableArray();
            var legacyMix = new Study(Design.Rcbd, legacyPool, legacyPool.Select(a => a.RecordId).ToImmutableArray(), treatments.Take(3).ToImmutableArray(), 2, 5) { CompactCages = true, SexMode = SexAllocationMode.Balance };
            var run = AllocationEngine.Run(legacyMix, 9);
            Check(run.Status == RunStatus.Success && run.Allocation!.Positions.Length == 30, "Previous automatic sex mixes support compact cages for every available male count");
            Check(run.Allocation!.Blocks.All(b => b.CvPercent == run.Attempts.Single(a => a.Block == b.Block).LastCvPercent), "Compact block CV diagnostics match the intact cages exactly");
        }
        var compact = Make(SexAllocationMode.Ignore); var result = AllocationEngine.Run(compact, 8).Allocation!;
        var old = AllocationEngine.Run(compact with { CompactCages = false }, 8).Allocation!;
        Check(Spread(compact, result) < Spread(compact, old), "Synthetic cages have less within-cage spread than the previous mean-balancing method");
        decimal[] obvious = [8.2m, 8.1m, 8m, 8m, 7.9m, 6.2m, 6.1m, 6m, 6m, 5.9m, 5.2m, 5.1m, 5m, 5m, 4.9m, 4.2m, 4.1m, 4m, 4m, 3.9m];
        var obviousPool = obvious.Select((w, i) => new Animal("a" + i, "a" + i, w)).ToImmutableArray();
        var gap = new Study(Design.Rcbd, obviousPool, obviousPool.Select(a => a.RecordId).ToImmutableArray(), [new("A", "A"), new("B", "B")], 2, 5) { CompactCages = true };
        var gapRun = AllocationEngine.Run(gap, 5); Check(gapRun.Status == RunStatus.Success, "Separated size cohorts allocate");
        var gapWeights = obviousPool.ToDictionary(a => a.RecordId, a => a.WeightGrams);
        Check(gapRun.Allocation!.Positions.GroupBy(p => (p.Replicate, p.TreatmentIndex)).All(g => g.Max(p => gapWeights[p.AnimalRecordId]) - g.Min(p => gapWeights[p.AnimalRecordId]) <= 0.3m), "The 8-size and 6-size animals are not mixed merely to match cage means");

        foreach (var pattern in new[] { SexBalancePattern.SameInEveryPen, SexBalancePattern.TwoReplicateGroups })
        {
            var mixedPool = Enumerable.Range(1, 60).Select(i => new Animal("M" + i, "M" + i, 100 + i) { Sex = AnimalSex.Male })
                .Concat(Enumerable.Range(1, 60).Select(i => new Animal("F" + i, "F" + i, 200 + i) { Sex = AnimalSex.Female })).ToImmutableArray();
            var mixed = AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(new Study(Design.Rcbd, mixedPool, [], treatments, 4, 4)
            {
                CompactCages = true,
                SexMode = SexAllocationMode.Balance,
                SexSettings = new(pattern, 0, 1, 2, 3)
            }, "Mixed fixture") with
            { AutomaticPoolSelection = true }).Study;
            var run = AllocationEngine.Run(mixed, 42); Check(run.Status == RunStatus.Success, "Both explicit sex compositions support compact cages");
            var byId = mixedPool.ToDictionary(a => a.RecordId);
            foreach (var rep in run.Allocation!.Positions.GroupBy(p => p.Replicate))
            {
                int m = pattern == SexBalancePattern.TwoReplicateGroups && rep.Key > 2 ? 3 : 1;
                Check(rep.GroupBy(p => p.TreatmentIndex).All(g => g.Count(p => byId[p.AnimalRecordId].Sex == AnimalSex.Male) == m), "Every intact cage keeps its specified sex composition");
                foreach (var sex in new[] { AnimalSex.Male, AnimalSex.Female })
                {
                    int count = sex == AnimalSex.Male ? m : 4 - m;
                    var expected = rep.Where(p => byId[p.AnimalRecordId].Sex == sex).Select(p => byId[p.AnimalRecordId].WeightGrams).OrderByDescending(x => x).Chunk(count).Select(Signature).Order();
                    var actual = rep.GroupBy(p => p.TreatmentIndex).Select(g => Signature(g.Where(p => byId[p.AnimalRecordId].Sex == sex).Select(p => byId[p.AnimalRecordId].WeightGrams))).Order();
                    Check(actual.SequenceEqual(expected), "Each sex component uses adjacent weight ranks rather than alternating heavy/light animals");
                }
            }
        }
        var separatedPool = Enumerable.Range(0, 40).Select(i => new Animal("m" + i, "m" + i, 8 + i / 100m) { Sex = AnimalSex.Male })
            .Concat(Enumerable.Range(0, 40).Select(i => new Animal("f" + i, "f" + i, 20 + i / 100m) { Sex = AnimalSex.Female })).ToImmutableArray();
        var separated = AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(new Study(Design.Rcbd, separatedPool, [], [new("A", "A"), new("B", "B")], 4, 5)
        {
            CompactCages = true,
            SexMode = SexAllocationMode.Separate,
            SexSettings = new(SexBalancePattern.SameInEveryPen, 2, 0, 0, 0)
        }, "Sex order") with
        { AutomaticPoolSelection = true }).Study;
        var sepRun = AllocationEngine.Run(separated, 1); var sexLookup = separatedPool.ToDictionary(a => a.RecordId, a => a.Sex);
        Check(sepRun.Status == RunStatus.Success && sepRun.Allocation!.Positions.All(p => sexLookup[p.AnimalRecordId] == (p.Replicate <= 2 ? AnimalSex.Male : AnimalSex.Female)), "Male blocks still precede female blocks even when females are heavier");
        Check(AllocationEngine.Run(compact with { FilterBalance = true }, 1).Status == RunStatus.Invalid, "A block-mean CV gate cannot silently break apart compact cages");
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Check(AllocationEngine.Run(compact, 1, cancel.Token).Status == RunStatus.Cancelled, "Compact allocation can be cancelled"); }
        var session = ProjectSession.Create("Synthetic BW - compact cages", ProjectInputs.FromStudy(compact with { CompactCages = false }, "Legacy selection") with { AutomaticPoolSelection = true }, "Legacy study");
        session.Run(8, "Previous grouping"); string previous = session.Document.ActiveResultId!;
        var layout = HousingLayouts.Create(session.CurrentInput.Inputs, 1, 10, 25);
        session.Revise(session.CurrentInput.Inputs with { Study = session.CurrentInput.Inputs.Study with { CompactCages = true }, PreparedLayout = layout }, "Closest-weight cages");
        session.Run(8, "Compact grouping"); session.MapPens(layout.Pens, layout.Seed, "Keep prepared layout");
        string output = Path.Combine(root, "analysis", "compact-cage-validation"); Directory.CreateDirectory(output);
        string path = Path.Combine(output, "compact-" + Guid.NewGuid().ToString("N") + ".experimentproj"); ProjectStore.Save(session.Document, path); var reopened = ProjectStore.Open(path);
        Check(JsonSerializer.Serialize(reopened.Document) == JsonSerializer.Serialize(session.Document) && reopened.Choose(previous).Input.Inputs.Study.CompactCages == false, "Old and new algorithms coexist and replay in one saved project");
        var report = AllocationReports.Create(session.Choose(session.Document.ActiveResultId!), false, false);
        Check(report.Tables.SelectMany(t => t.Columns).All(c => !c.Name.Contains("(g)")) && report.Tables.Single(t => t.Name == "Summary").Rows.Any(r => r[0] is "Allocation rule" && r[1] is string text && text.Contains("intact cages")), "Exports are unit-neutral and record the new allocation rule");
        ReportExport.Excel(report, Path.Combine(output, "compact-synthetic-data.xlsx"), true);
        var raw = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW\n1,8.780\n2,8.800\n3,9.000\n4,9.200\n"), "raw-bw.csv");
        var rawBatch = DataImport.Animals(raw, "Delimited", 1, 2, 5, new(1, 2)).Value!;
        Check(rawBatch.Application.WeightUnit == "raw" && rawBatch.Animals[0].WeightGrams == 8.780m, "Default import stores 8.780 as 8.780 without conversion");
        var legacyBatch = DataImport.Animals(raw, "Delimited", 1, 2, 5, new(1, 2), "kg").Value!;
        Check(legacyBatch.Animals[0].WeightGrams == 8780m, "Legacy kg import replay keeps the original conversion semantics");
        var rawSession = ProjectSession.Create("Raw numbers", ProjectInputs.FromStudy(new Study(Design.Rcbd, [], [], [new("A", "A"), new("B", "B")], 2, 1) { CompactCages = true }, "Setup") with { AutomaticPoolSelection = true }, "Setup");
        rawSession.ImportAnimals(raw, rawBatch, false, "Raw BW import"); rawSession.Run(1, "Raw BW allocation");
        string rawPath = Path.Combine(output, "raw-" + Guid.NewGuid().ToString("N") + ".experimentproj"); ProjectStore.Save(rawSession.Document, rawPath);
        Check(ProjectStore.Open(rawPath).CurrentInput.Inputs.Study.Pool[0].WeightGrams == 8.780m, "Raw values survive save and replay unchanged");
        Console.WriteLine($"PASS: closest-weight RCBD cages and unit-neutral BW ({checks} assertions)."); return checks;
    }
}
