using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class BalancedBwChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Balanced BW: " + why); }
        // Independent synthetic reference pens: narrow descending blocks within
        // each sex sequence, with similar distributions and matched pen means.
        decimal[] referenceMeans = [52m, 49m, 46m, 43m, 60m, 56m, 52m, 48m];
        string[] example = referenceMeans.Select(mean => string.Join(";", Enumerable.Range(0, 5).Select(pen =>
            string.Join(",", Enumerable.Range(0, 5).Select(slot =>
                (mean + (slot - 2) * 0.2m + (pen - 2) * 0.01m * Math.Sign(slot - 2)).ToString(CultureInfo.InvariantCulture)))))).ToArray();
        var originals = example.Select(line => line.Split(';').Select(pen => pen.Split(',').Select(w => decimal.Parse(w, CultureInfo.InvariantCulture)).ToArray()).ToArray()).ToArray();
        var pool = originals.SelectMany((pens, rep) => pens.SelectMany((pen, trt) => pen.Select((w, slot) => new Animal($"r{rep}t{trt}a{slot}", $"r{rep}t{trt}a{slot}", w, OldLocation: (100 + rep * 10 + trt).ToString()) { Sex = rep < 4 ? AnimalSex.Male : AnimalSex.Female }))).ToImmutableArray();
        var treatments = Enumerable.Range(3323, 5).Select(i => new Treatment(i.ToString(), i.ToString())).ToImmutableArray();
        var study = new Study(Design.Rcbd, pool, pool.Select(a => a.RecordId).ToImmutableArray(), treatments, 8, 5)
        {
            BalancedBwCages = true,
            SexMode = SexAllocationMode.Separate,
            SexSettings = new(SexBalancePattern.SameInEveryPen, 4, 0, 0, 0)
        };
        var weights = pool.ToDictionary(a => a.RecordId, a => a.WeightGrams);
        var sample = AllocationEngine.Run(study, 20261104).Allocation!;
        var distinct = new HashSet<string>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var run = AllocationEngine.Run(study, seed); Check(run.Status == RunStatus.Success, "Full synthetic reference pool generates"); var allocation = run.Allocation!;
            Check(allocation.Blocks.All(b => b.CvPercent <= 0.5), "All eight synthetic-balanced blocks reach automatic CV target");
            Check(allocation.Positions.Select(p => p.AnimalRecordId).ToHashSet().SetEquals(study.SelectedRecordIds), "Every selected animal appears once, with no pool substitution");
            foreach (var block in allocation.Positions.GroupBy(p => p.Replicate))
            {
                int rep = block.Key - 1;
                Check(block.Average(p => weights[p.AnimalRecordId]) == originals[rep].SelectMany(p => p).Average(), "Original within-sex descending block membership and mean are preserved");
                var cages = block.GroupBy(p => p.TreatmentIndex).ToArray();
                Check(cages.Length == 5 && cages.All(g => g.Count() == 5), "Complete RCBD has one five-animal pen per treatment");
                var ranks = originals[rep].SelectMany(p => p).OrderByDescending(w => w).Chunk(5).ToArray();
                foreach (var tier in block.GroupBy(p => p.OriginalSubblock))
                {
                    Check(tier.Select(p => weights[p.AnimalRecordId]).Order().SequenceEqual(ranks[tier.Key - 1].Order()) && tier.Select(p => p.TreatmentIndex).Distinct().Count() == 5,
                        "Every BW rank subgroup contributes exactly once to each treatment");
                }
                decimal oldWorst = originals[rep].Max(p => p.Max() - p.Min());
                decimal newWorst = cages.Max(g => g.Max(p => weights[p.AnimalRecordId]) - g.Min(p => weights[p.AnimalRecordId]));
                Check(newWorst <= oldWorst, "Worst individual pen BW range does not exceed the synthetic reference for the same pool");
                // Quantile matching: every cage's kth ranked weight lies in the
                // same narrow BW tier, preventing unlike pen distributions.
                foreach (var cage in cages)
                {
                    var sorted = cage.Select(p => weights[p.AnimalRecordId]).OrderByDescending(w => w).ToArray();
                    Check(sorted.Select((w, k) => w >= ranks[k].Min() && w <= ranks[k].Max()).All(x => x), "Pen weight distributions are comparable at every rank");
                }
            }
            Check(JsonSerializer.Serialize(allocation) == JsonSerializer.Serialize(AllocationEngine.Run(study, seed).Allocation), "Subgroup policy replays exactly");
            distinct.Add(string.Join('|', allocation.Positions.Select(p => p.TreatmentIndex)));
        }
        Check(distinct.Count > 1, "Seed changes randomized treatment assignment");
        var old = AllocationEngine.Run(study with { BalancedBwCages = false, CompactCages = true }, 20261104).Allocation!;
        Check(sample.Blocks.Max(b => b.CvPercent) < old.Blocks.Max(b => b.CvPercent), "Corrected method balances treatment pens better than contiguous cages");
        var noSex = study with { SexMode = SexAllocationMode.Ignore, SexSettings = null };
        var noSexRun = AllocationEngine.Run(noSex, 7);
        Check(noSexRun.Status == RunStatus.Success && noSexRun.Allocation!.Blocks.All(b => b.CvPercent <= 0.5), "Sex-neutral RCBD uses balanced BW tiers too");
        var changedSex = noSex with { Pool = noSex.Pool.Select(a => a with { Sex = AnimalSex.Unknown }).ToImmutableArray() };
        Check(noSexRun.Allocation!.Positions.SequenceEqual(AllocationEngine.Run(changedSex, 7).Allocation!.Positions), "Ignore-sex grouping and randomization do not depend on recorded sex");
        foreach (var pattern in new[] { SexBalancePattern.SameInEveryPen, SexBalancePattern.TwoReplicateGroups })
        {
            var mixed = AutomaticPoolSelection.Apply(ProjectInputs.FromStudy(study with { Pool = pool.Add(new("extra", "extra", 1) { Sex = AnimalSex.Male }), SelectedRecordIds = [], Replicates = 4, SexMode = SexAllocationMode.Balance, SexSettings = new(pattern, 0, 2, 2, 3) }, "Mixed study") with { AutomaticPoolSelection = true }).Study;
            var run = AllocationEngine.Run(mixed, 3); Check(run.Status == RunStatus.Success, "Both explicit sex balance patterns generate");
            var byId = mixed.Pool.ToDictionary(a => a.RecordId);
            foreach (var pen in run.Allocation!.Positions.GroupBy(p => (p.Replicate, p.TreatmentIndex)))
                Check(pen.Count(p => byId[p.AnimalRecordId].Sex == AnimalSex.Male) == (pattern == SexBalancePattern.TwoReplicateGroups && pen.Key.Replicate > 2 ? 3 : 2), "Exact sex counts survive candidate optimization and treatment randomization");
            Check(!run.Allocation.Positions.Any(p => p.AnimalRecordId == "extra"), "Light EX animal is never introduced to balance a pen");
        }
        for (int male = 0; male <= 30; male++)
        {
            var legacyPool = Enumerable.Range(0, 30).Select(i => new Animal("L" + i, "L" + i, 10 + i / 10m) { Sex = i < male ? AnimalSex.Male : AnimalSex.Female }).ToImmutableArray();
            var legacy = new Study(Design.Rcbd, legacyPool, legacyPool.Select(a => a.RecordId).ToImmutableArray(), treatments.Take(3).ToImmutableArray(), 2, 5) { BalancedBwCages = true, SexMode = SexAllocationMode.Balance };
            Check(AllocationEngine.Run(legacy, 11).Status == RunStatus.Success, "Older implicit sex compositions retain their counts for every male total");
        }
        var impossiblePool = new[] { 10m, 1m, 1m, 1m }.Select((w, i) => new Animal("I" + i, "I" + i, w)).ToImmutableArray();
        var impossible = new Study(Design.Rcbd, impossiblePool, impossiblePool.Select(a => a.RecordId).ToImmutableArray(), treatments.Take(2).ToImmutableArray(), 2, 1) { BalancedBwCages = true };
        var bestEffort = AllocationEngine.Run(impossible, 1);
        Check(bestEffort.Status == RunStatus.Success && bestEffort.Allocation!.Blocks.Any(b => b.CvPercent > 0.5), "Unattainable automatic target retains actual best result and actual CV, without inventing balance");
        Check(AllocationEngine.Run(impossible with { FilterBalance = true, CvLimitPercent = 0.5m }, 1).Status == RunStatus.Exhausted, "Explicit strict balance limit cannot be bypassed");
        Check(AllocationEngine.Run(study with { CompactCages = true }, 1).Status == RunStatus.Invalid && AllocationEngine.Run(study with { Design = Design.Crd }, 1).Status == RunStatus.Invalid, "Incompatible policies and design are rejected");
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Check(AllocationEngine.Run(study, 1, cancel.Token).Status == RunStatus.Cancelled, "New policy supports cancellation"); }
        using (var cancel = new CancellationTokenSource())
        {
            var cancelled = AllocationEngine.Run(study, 1, cancel.Token, (b, a) => { if (b == 2) cancel.Cancel(); });
            Check(cancelled.Status == RunStatus.Cancelled && cancelled.Allocation is null, "Cancellation during candidate search never commits partial allocation");
        }
        var session = ProjectSession.Create("Synthetic-balanced weights", ProjectInputs.FromStudy(study with { BalancedBwCages = false, CompactCages = true }, "Earlier preview") with { AutomaticPoolSelection = true }, "Earlier preview");
        session.Run(20261104, "Earlier contiguous cages"); string previous = session.Document.ActiveResultId!;
        var housing = HousingLayouts.Create(session.CurrentInput.Inputs, 101, 140, 25);
        session.Revise(session.CurrentInput.Inputs with { Study = study, PreparedLayout = housing }, "Ranked BW subgroup balancing");
        session.Run(20261104, "Balanced subgroup allocation"); session.MapPens(housing.Pens, housing.Seed, "Preserve prepared treatment locations");
        string output = Path.Combine(root, "analysis/balanced-bw-validation"); Directory.CreateDirectory(output);
        string project = Path.Combine(output, "synthetic-balanced-balanced.experimentproj"); ProjectStore.Save(session.Document, project); var reopened = ProjectStore.Open(project);
        Check(JsonSerializer.Serialize(reopened.Document) == JsonSerializer.Serialize(session.Document) && reopened.Choose(previous).Input.Inputs.Study.CompactCages, "Earlier contiguous results and new balanced results coexist and replay unchanged");
        var report = AllocationReports.Create(session.Choose(session.Document.ActiveResultId!), false, false);
        Check(report.Tables.Length == 5 && report.Tables.All(t => !t.Columns.Any(c => c.Name.Contains("(g)"))), "Concise unit-neutral exports are preserved");
        Check(report.Tables.Single(t => t.Name == "Summary").Rows.Any(r => r[0] is "Block BW balance" && r[1] is string text && text.StartsWith("All current blocks")), "Report honestly describes automatic CV target attainment");
        ReportExport.Excel(report, Path.Combine(output, "Synthetic-balanced-balanced.xlsx"), true);
        var summary = sample.Pens.GroupBy(p => p.Replicate).Select(g => new { Replicate = g.Key, MinimumPenMean = g.Min(p => p.MeanWeightGrams), MaximumPenMean = g.Max(p => p.MeanWeightGrams), CV = sample.Blocks.Single(b => b.Block == g.Key).CvPercent }).ToArray();
        File.WriteAllText(Path.Combine(output, "synthetic-balanced-comparison.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        var largePool = Enumerable.Range(0, 100000).Select(i => new Animal("P" + i, "P" + i, 100 + i / 1000m)).ToImmutableArray();
        var large = new Study(Design.Rcbd, largePool, largePool.Select(a => a.RecordId).ToImmutableArray(), treatments.Take(5).ToImmutableArray(), 200, 100) { BalancedBwCages = true };
        var watch = Stopwatch.StartNew(); var largeRun = AllocationEngine.Run(large, 11); watch.Stop();
        Check(largeRun.Status == RunStatus.Success && largeRun.Allocation!.Positions.Length == 100000, "Supported maximum-size subgroup allocation completes");
        Console.WriteLine($"PASS: macro-style balanced BW distributions ({checks} assertions); 100,000 animals in {watch.ElapsedMilliseconds} ms.");
        return checks;
    }
}
