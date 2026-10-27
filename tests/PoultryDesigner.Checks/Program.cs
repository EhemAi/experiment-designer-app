using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PoultryDesigner.Engine;

if (args.Length == 3 && args[0] == "--package-check")
{
    try { Section7Checks.RunPublished(args[1], args[2]); return 0; }
    catch (Exception ex) { Console.Error.WriteLine("FAILED: package verification: " + ex.Message); return 1; }
}

int checks = 0;
void Check(bool condition, string message)
{ checks++; if (!condition) throw new Exception(message); }
void Throws(Action action, string message)
{ bool threw = false; try { action(); } catch (ArgumentException) { threw = true; } catch (InvalidOperationException) { threw = true; } Check(threw, message); }
string Json<T>(T value) => JsonSerializer.Serialize(value);
Study Make(Design design = Design.Rcbd, int t = 3, int r = 4, int p = 4, bool equal = false)
{
    var animals = Enumerable.Range(0, t * r * p).Select(i => new Animal($"record-{i:D5}", $"{i:D5}", equal ? 100m : 40m + i * 0.25m)).ToImmutableArray();
    return new(design, animals, animals.Select(a => a.RecordId).ToImmutableArray(),
        Enumerable.Range(1, t).Select(i => new Treatment($"t{i}", $"Treatment {i}")).ToImmutableArray(), r, p);
}
Allocation Success(Study s, ulong seed)
{
    var result = AllocationEngine.Run(s, seed);
    Check(result.Status == RunStatus.Success && result.Allocation is not null, "Run must succeed.");
    return result.Allocation!;
}
void Invariants(Study s, Allocation a)
{
    Check(a.Positions.Length == s.SelectedRecordIds.Length, "Animal count.");
    Check(a.Positions.Select(x => x.AnimalRecordId).Order().SequenceEqual(s.SelectedRecordIds.Order()), "Every selected animal exactly once.");
    Check(a.Pens.Length == s.Treatments.Length * s.Replicates && a.Pens.All(x => x.Count == s.AnimalsPerPen), "Pen sizes and replication.");
    Check(a.Pens.GroupBy(x => x.TreatmentIndex).All(g => g.Count() == s.Replicates), "Treatment replication.");
    if (s.Design != Design.Crd)
    {
        Check(a.Positions.GroupBy(x => (x.Block, x.OriginalSubblock)).All(g => g.Select(x => x.TreatmentIndex).Distinct().Count() == s.Treatments.Length), "Fresh blocked subblocks.");
        Check(a.Blocks.Length == s.Replicates, "Block count.");
        var selection = s.SelectedRecordIds.ToHashSet(StringComparer.Ordinal);
        var ranked = s.Pool.Where(x => selection.Contains(x.RecordId)).OrderByDescending(x => x.WeightGrams).ToArray();
        if (ranked.Select(x => x.WeightGrams).Distinct().Count() == ranked.Length)
        {
            var ranks = ranked.Select((x, i) => (x.RecordId, Rank: i)).ToDictionary(x => x.RecordId, x => x.Rank);
            Check(a.Positions.All(x => x.OriginalBlock == ranks[x.AnimalRecordId] / (s.Treatments.Length * s.AnimalsPerPen) + 1
                && x.OriginalSubblock == ranks[x.AnimalRecordId] / s.Treatments.Length % s.AnimalsPerPen + 1), "Fresh blocks and subblocks preserve BW ranks.");
        }
    }
    else Check(a.Positions.All(x => x.Block == 0) && a.Blocks.IsEmpty, "CRD must have no BW blocks.");
    var weights = s.Pool.ToDictionary(x => x.RecordId, x => (double)x.WeightGrams);
    foreach (var pen in a.Pens)
    {
        double[] values = a.Positions.Where(x => x.TreatmentIndex == pen.TreatmentIndex && x.Replicate == pen.Replicate).Select(x => weights[x.AnimalRecordId]).ToArray();
        double mean = values.Average();
        Check(Math.Abs(mean - (double)pen.MeanWeightGrams) < 1e-9, "Independent pen mean.");
        Check(Math.Abs(values.Sum() - (double)pen.TotalWeightGrams) < 1e-8, "Independent pen total.");
        if (values.Length == 1) Check(pen.SampleSdGrams is null, "One bird SD is unavailable.");
        else Check(Math.Abs(Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1)) - pen.SampleSdGrams!.Value) < 1e-9, "Independent sample SD.");
    }
    foreach (var block in a.Blocks)
    {
        double[] means = a.Pens.Where(p => p.Replicate == block.Block).Select(p => (double)p.MeanWeightGrams).ToArray();
        double average = means.Average();
        double cv = 100 * Math.Sqrt(means.Sum(v => (v - average) * (v - average)) / (means.Length - 1)) / average;
        Check(Math.Abs(cv - block.CvPercent) < 1e-10, "Independent block CV.");
    }
}

try
{
    var random = new StableRandom(0);
    Check(random.NextUInt64() == 0xe220a8397b1dcdafUL, "SplitMix64 first reference vector.");
    Check(random.NextUInt64() == 0x6e789e6aa1b965f4UL, "SplitMix64 second reference vector.");
    Check(random.NextUInt64() == 0x06c45d188009454fUL, "SplitMix64 third reference vector.");
    foreach (var design in Enum.GetValues<Design>())
        foreach (int p in new[] { 1, 3, 4 })
            foreach (ulong seed in Enumerable.Range(0, 100).Select(x => (ulong)x))
            {
                var s = Make(design, p: p); var a = Success(s, seed); Invariants(s, a);
                Check(Json(a) == Json(Success(s, seed)), "Seed replay.");
                var reversed = s with { Pool = s.Pool.Reverse().ToImmutableArray(), SelectedRecordIds = s.SelectedRecordIds.Reverse().ToImmutableArray() };
                Check(Json(a) == Json(Success(reversed, seed)), "Row ordering independence.");
                if (design == Design.Conventional)
                {
                    var lookup = s.Pool.ToDictionary(x => x.RecordId);
                    int[] sequence = a.Positions.OrderByDescending(x => lookup[x.AnimalRecordId].WeightGrams).Select(x => x.TreatmentIndex).ToArray();
                    int t = s.Treatments.Length;
                    for (int sub = 0; sub < sequence.Length / t; sub++) Check(sequence.Skip(sub * t).Take(t).SequenceEqual(sub % 2 == 0 ? sequence.Take(t) : sequence.Take(t).Reverse()), "Global serpentine including odd-sized block boundary.");
                }
            }
    Console.WriteLine("PASS: 900 seeded design/pen-size scenarios, replay and independent mathematics.");

    var study = Make();
    void Invalid(Study s) => Check(AllocationEngine.Run(s, 0).Status == RunStatus.Invalid, "Invalid inputs must be rejected.");
    Invalid(study with { Replicates = 1 }); Invalid(study with { AnimalsPerPen = 0 });
    Invalid(study with { Replicates = int.MaxValue, AnimalsPerPen = int.MaxValue });
    Invalid(study with { Design = (Design)99 });
    Invalid(study with { SelectedRecordIds = study.SelectedRecordIds.RemoveAt(0) });
    Invalid(study with { SelectedRecordIds = study.SelectedRecordIds.SetItem(0, "unknown") });
    Invalid(study with { SelectedRecordIds = study.SelectedRecordIds.SetItem(0, study.SelectedRecordIds[1]) });
    Invalid(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { WeightGrams = 0 }) });
    Invalid(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { WeightGrams = -1 }) });
    Invalid(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { WeightGrams = decimal.MaxValue }) });
    Invalid(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { AnimalId = study.Pool[1].AnimalId }) });
    Invalid(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { RecordId = study.Pool[1].RecordId }) });
    Invalid(study with { Treatments = study.Treatments.SetItem(0, study.Treatments[1]) });
    Invalid(study with { CvLimitPercent = -1 }); Invalid(study with { MaximumAttemptsPerBlock = 0 });
    Invalid(study with { Design = Design.Crd, FilterBalance = true });
    Invalid(study with { Design = Design.Conventional, FilterBalance = true });
    Invalid(study with { Pool = default });
    var largePool = study with { Pool = study.Pool.Add(new("extra", "extra", 99m)) };
    Check(Success(largePool, 5).Positions.All(x => x.AnimalRecordId != "extra"), "Explicit exclusions never allocated.");
    Check(AllocationEngine.Fingerprint(study) == AllocationEngine.Fingerprint(largePool), "Unselected pool does not change allocation inputs.");
    var scoped = study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { AnimalId = study.Pool[1].AnimalId, Cohort = "different" }) };
    Success(scoped, 0);
    Success(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { AnimalId = study.Pool[1].AnimalId }), Demonstration = true }, 0);
    var tied = Make(equal: true); var tiedResult = Success(tied with { FilterBalance = true, CvLimitPercent = 0, MaximumAttemptsPerBlock = 1 }, 0);
    Check(tiedResult.Blocks.All(x => x.CvPercent == 0), "Zero CV equality.");
    Check(Json(tiedResult.Positions) != Json(Success(tied, 1).Positions), "Ties randomize across seeds.");
    var failing = Make(t: 2, p: 1) with { FilterBalance = true, CvLimitPercent = 0, MaximumAttemptsPerBlock = 1 };
    var failure = AllocationEngine.Run(failing, 0);
    Check(failure.Status == RunStatus.Exhausted && failure.Allocation is null && failure.Attempts[0].Attempts == 1, "Single-attempt filtering enforced.");
    var boundaryBase = Make(t: 2, p: 1); var boundaryResult = Success(boundaryBase, 0);
    decimal boundary = (decimal)boundaryResult.Blocks.Max(x => x.CvPercent);
    Success(boundaryBase with { FilterBalance = true, CvLimitPercent = boundary, MaximumAttemptsPerBlock = 1 }, 0);
    Check(AllocationEngine.Run(boundaryBase with { FilterBalance = true, CvLimitPercent = boundary - 0.000001m, MaximumAttemptsPerBlock = 1 }, 0).Status == RunStatus.Exhausted, "Below-boundary rejection.");
    var retry = AllocationEngine.Run(failing with { MaximumAttemptsPerBlock = 7 }, 0);
    Check(retry.Attempts[0].Attempts == 7 && retry.Attempts[0].BestCvPercent <= retry.Attempts[0].LastCvPercent, "Exhaustion diagnostics.");
    var session = new AllocationSession(); session.Run(study, 5); var old = session.Active;
    session.Run(failing, 0); Check(ReferenceEquals(session.Active, old) && session.History.Length == 1, "Failure retains completed result.");
    session.Run(study with { AnimalsPerPen = 0 }, 0); Check(ReferenceEquals(session.Active, old), "Invalid run retains completed result.");
    using (var cancel = new CancellationTokenSource())
    {
        var cancelled = session.Run(study, 9, cancel.Token, (_, _) => cancel.Cancel());
        Check(cancelled.Status == RunStatus.Cancelled && ReferenceEquals(session.Active, old), "Mid-run cancellation is atomic.");
        Check(session.Run(study, 9, cancel.Token).Status == RunStatus.Cancelled, "Pre-run cancellation.");
    }
    Check(session.IsStale(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { WeightGrams = 60 }) }), "Changed BW marks result stale.");
    Check(session.IsStale(study with { Pool = default }), "Incomplete setup marks prior output stale without throwing.");
    var edited = session.Swap(study, old!.Positions[0].AnimalRecordId, old.Positions.First(x => x.Block == 2).AnimalRecordId, "Cross-block correction");
    Check(edited.Revision == 2 && edited.Edits.Length == 1 && session.History.Length == 2, "Cross-block revision history.");
    Check(edited.Positions[0].OriginalBlock == 1 && edited.Positions[0].Block == 2, "Original block retained.");
    var undone = session.Swap(study, edited.Edits[0].FirstRecordId, edited.Edits[0].SecondRecordId, "Undo prior correction");
    Check(Json(undone.Positions) == Json(old.Positions) && undone.Edits.Length == 2, "Undo records reversal without erasing history.");
    Throws(() => AllocationEngine.Swap(study, old, "unknown", old.Positions[0].AnimalRecordId, "reason"), "Unknown swap ID.");
    Throws(() => AllocationEngine.Swap(study, old, old.Positions[0].AnimalRecordId, old.Positions[0].AnimalRecordId, "reason"), "Same animal swap.");
    var tight = Make(t: 2, p: 2) with { FilterBalance = true, CvLimitPercent = 100 };
    var tightResult = Success(tight, 5);
    // Set an inclusive boundary equal to the existing maximum, then search all cross-block swaps.
    tight = tight with { CvLimitPercent = (decimal)tightResult.Blocks.Max(x => x.CvPercent) };
    tightResult = Success(tight, 5); bool rejectedSwap = false;
    foreach (var a in tightResult.Positions.Where(x => x.Block == 1))
        foreach (var b in tightResult.Positions.Where(x => x.Block == 4))
        { try { AllocationEngine.Swap(tight, tightResult, a.AnimalRecordId, b.AnimalRecordId, "test"); } catch (InvalidOperationException) { rejectedSwap = true; } }
    Check(rejectedSwap, "Cross-block balance-breaking swap rejected.");
    var physical = Enumerable.Range(0, old.Pens.Length).Select(i => new PhysicalPen($"P{i:D3}", $"Room A/P{i:D3}", study.AnimalsPerPen, i / study.Treatments.Length + 1)).ToImmutableArray();
    var mapped = AllocationEngine.MapPens(study, old, physical, 99);
    Check(mapped.Locations.Select(x => x.PhysicalPenId).Distinct().Count() == physical.Length, "Unique physical assignments.");
    Check(mapped.Locations.All(x => physical.Single(p => p.Id == x.PhysicalPenId).EligibleBlock == x.Replicate), "Physical block eligibility.");
    Check(Json(mapped.Positions) == Json(old.Positions), "Physical mapping cannot change animal allocation.");
    Check(Json(mapped.Locations) == Json(AllocationEngine.MapPens(study, old, physical.Reverse().ToImmutableArray(), 99).Locations), "Physical mapping reproducibility.");
    Throws(() => AllocationEngine.MapPens(study, old, physical.SetItem(0, physical[0] with { Capacity = 0 }), 99), "Capacity rejection.");
    Throws(() => AllocationEngine.MapPens(study, old, physical.SetItem(0, physical[1]), 99), "Duplicate pen rejection.");
    Throws(() => AllocationEngine.MapPens(study, old, physical.SetItem(0, physical[0] with { EligibleBlock = 2 }), 99), "Unbalanced eligible pools.");
    Throws(() => AllocationEngine.MapPens(study, old, physical.Select(x => x with { EligibleBlock = null }).ToImmutableArray(), 99), "RCBD must declare eligible pools.");
    var crd = Make(Design.Crd); var crdResult = Success(crd, 5);
    var global = physical.Select(x => x with { EligibleBlock = null }).ToImmutableArray();
    Check(AllocationEngine.MapPens(crd, crdResult, global, 99).Locations.Length == physical.Length, "Global CRD physical pool.");
    var restrictedCrd = AllocationEngine.MapPens(crd, crdResult, physical, 99);
    Check(restrictedCrd.Blocks.IsEmpty && restrictedCrd.Locations.All(l => physical.Single(p => p.Id == l.PhysicalPenId).EligibleBlock == l.Replicate), "CRD housing can be scoped by replicate without introducing BW blocks.");
    var unmappedMovement = ResultViews.Movement(largePool, old);
    Check(unmappedMovement.Count(x => x.Disposition == AnimalDisposition.ResearcherExcluded) == 1, "Movement distinguishes researcher exclusions.");
    Check(unmappedMovement.Count(x => x.Disposition == AnimalDisposition.Allocated) == study.SelectedRecordIds.Length, "Movement counts allocated records.");
    Check(ResultViews.Movement(largePool, null).Count(x => x.Disposition == AnimalDisposition.SelectedUnallocated) == study.SelectedRecordIds.Length, "Movement distinguishes selected unallocated records.");
    var movement = ResultViews.Movement(study, mapped);
    Check(movement.All(x => x.PhysicalPenId is not null), "Mapped movement contains current physical IDs.");
    var balance = ResultViews.Balance(study, edited);
    Check(balance.Treatments.All(x => x.PenCount == study.Replicates), "Balance reports experimental-unit replication.");
    Check(balance.OverallMeanGrams == study.Pool.Sum(x => x.WeightGrams) / study.Pool.Length, "Independent overall BW.");
    foreach (var treatment in balance.Treatments)
    {
        int index = Array.FindIndex(study.Treatments.ToArray(), x => x.Id == treatment.TreatmentId);
        decimal[] means = edited.Pens.Where(x => x.TreatmentIndex == index).Select(x => x.MeanWeightGrams).ToArray();
        Check(treatment.MeanWeightGrams == means.Sum() / means.Length && treatment.MinimumPenMeanGrams == means.Min() && treatment.MaximumPenMeanGrams == means.Max(), "Treatment-level diagnostic mean/range.");
    }
    Throws(() => ResultViews.Movement(study with { Pool = study.Pool.SetItem(0, study.Pool[0] with { WeightGrams = 101 }) }, old), "Movement rejects stale study version.");
    Throws(() => AllocationEngine.Swap(study, old with { Positions = old.Positions.RemoveAt(0) }, old.Positions[1].AnimalRecordId, old.Positions[2].AnimalRecordId, "invalid"), "Forged missing membership rejected.");
    Throws(() => AllocationEngine.MapPens(study, old with { Pens = old.Pens.SetItem(0, old.Pens[0] with { MeanWeightGrams = 999 }) }, physical, 1), "Corrupt cached metrics rejected.");
    var replayEdits = Success(study, old.Seed);
    foreach (var edit in undone.Edits) replayEdits = AllocationEngine.Swap(study, replayEdits, edit.FirstRecordId, edit.SecondRecordId, edit.Reason);
    Check(Json(replayEdits) == Json(undone), "Seed plus edit sequence reproduces full edited revision.");
    var tiny = Make(t: 2, r: 2, p: 1, equal: true);
    tiny = tiny with { Pool = tiny.Pool.Select((x, i) => x with { WeightGrams = 1m + i * 0.0000000000000000000000000001m }).ToImmutableArray(), FilterBalance = true, CvLimitPercent = 0, MaximumAttemptsPerBlock = 1 };
    Check(AllocationEngine.Run(tiny, 0).Status == RunStatus.Exhausted, "Zero limit rejects positive CV even far below display precision.");
    var retryStudy = Make() with { FilterBalance = true, CvLimitPercent = 0.2m, MaximumAttemptsPerBlock = 1000 };
    var retrySuccess = Success(retryStudy, 31);
    Check(retrySuccess.Attempts.Any(x => x.Attempts > 1) && retrySuccess.Blocks.All(x => x.CvPercent <= 0.2 + 1e-14), "Retry can eventually succeed while preserving block membership.");
    var goldStudy = Make(Design.Crd, t: 2, r: 2, p: 1);
    var gold = Success(goldStudy, 42);
    Check(string.Join(",", gold.Positions.Select(x => x.AnimalRecordId)) == "record-00003,record-00002,record-00000,record-00001", "Pinned version-1 CRD allocation vector.");
    var priorCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        Check(Json(Success(study, 5)) == Json(old), "Current culture cannot change replay.");
    }
    finally { CultureInfo.CurrentCulture = priorCulture; }
    foreach (var type in typeof(Study).Assembly.GetExportedTypes()) Check(type.GetProperties().All(p => !p.Name.Contains("Sex", StringComparison.OrdinalIgnoreCase)), "No sex properties in engine API.");
    Console.WriteLine("PASS: validation, filtering, cancellation, swaps, provenance and physical maps.");

    // Compare calculations against captured native Excel results; treatment draws deliberately differ.
    string root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
    checks += Section4Checks.Run(root);
    checks += Section5Checks.Run(root);
    checks += Section6Checks.Run(root);
    checks += HousingChecks.Run(root);
    checks += AnovaChecks.Run();
    checks += AutomaticPoolChecks.Run(root);
    checks += Section7Checks.Run(root);
    foreach (string name in new[] { "rcbd-no-sex", "crd-no-sex", "conventional-no-sex", "rcbd-exact-pool", "rcbd-tied-weights" })
    {
        string nativeFixture = Path.Combine(root, "analysis", "validation", "native-cases", name + ".json");
        if (!File.Exists(nativeFixture)) { Console.WriteLine($"SKIP: optional captured Excel fixture {name}; independent synthetic mathematics checks still run."); continue; }
        using var doc = JsonDocument.Parse(File.ReadAllText(nativeFixture));
        var fixture = doc.RootElement;
        var rows = fixture.GetProperty("summary").EnumerateArray().Where(row => row[0].ValueKind == JsonValueKind.Number).ToArray();
        int t = (int)fixture.GetProperty("control")[1][0].GetDecimal(), r = (int)fixture.GetProperty("control")[1][1].GetDecimal(), p = (int)fixture.GetProperty("control")[1][2].GetDecimal();
        var animals = rows.Select((row, i) => new Animal($"native-{i}", row[0].GetRawText(), row[3].GetDecimal())).ToImmutableArray();
        var s = Make(name.StartsWith("crd") ? Design.Crd : name.StartsWith("conventional") ? Design.Conventional : Design.Rcbd, t, r, p) with { Pool = animals, SelectedRecordIds = animals.Select(x => x.RecordId).ToImmutableArray() };
        var positions = rows.Select((row, i) => new Position(animals[i].RecordId, (int)row[7].GetDecimal() - 1, (int)row[6].GetDecimal(), s.Design == Design.Crd ? 0 : (int)row[6].GetDecimal(), 0)).ToImmutableArray();
        var calculated = AllocationEngine.Statistics(s, positions);
        foreach (var row in rows)
        {
            var pen = calculated.Pens.Single(x => x.Replicate == row[6].GetDecimal() && x.TreatmentIndex == row[7].GetDecimal() - 1);
            if (row[9].ValueKind == JsonValueKind.Number) Check(Math.Abs((double)pen.MeanWeightGrams - row[9].GetDouble()) < 1e-9, "Native Excel pen mean parity.");
            if (s.Design != Design.Crd && row[10].ValueKind == JsonValueKind.Number) Check(Math.Abs(calculated.Blocks.Single(x => x.Block == row[6].GetDecimal()).CvPercent - row[10].GetDouble()) < 1e-9, "Native Excel block CV parity.");
        }
        Invariants(s, Success(s, 5));
        Console.WriteLine($"PASS: native mathematical fixture {name}.");
    }
    var watch = Stopwatch.StartNew();
    var studySized = Make(t: 6, r: 12, p: 12) with { FilterBalance = true, CvLimitPercent = 0.5m, MaximumAttemptsPerBlock = 30 };
    var example = AllocationEngine.Run(studySized, 202612);
    watch.Stop(); Check(example.Status == RunStatus.Success, "864-bird example completes within 30 attempts.");
    Invariants(studySized, example.Allocation!);
    Directory.CreateDirectory(Path.Combine(root, "analysis", "engine-validation"));
    File.WriteAllText(Path.Combine(root, "analysis", "engine-validation", "864-bird-example.json"), JsonSerializer.Serialize(example, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(root, "analysis", "engine-validation", "864-bird-input.json"), JsonSerializer.Serialize(studySized, new JsonSerializerOptions { WriteIndented = true }));
    var maxWatch = Stopwatch.StartNew(); Invariants(Make(t: 10, r: 100, p: 100), Success(Make(t: 10, r: 100, p: 100), 1)); maxWatch.Stop();
    Console.WriteLine($"PASS: 864-bird run {watch.ElapsedMilliseconds} ms; 100,000-bird validation/allocation/statistics/checks {maxWatch.ElapsedMilliseconds} ms.");
    Console.WriteLine($"ALL PASS: {checks} assertions.");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine($"FAILED after {checks} assertions."); return 1; }
return 0;
