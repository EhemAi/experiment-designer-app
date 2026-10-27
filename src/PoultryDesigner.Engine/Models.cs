using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

public enum Design { Rcbd, Crd, Conventional }
public sealed record Animal(string RecordId, string AnimalId, decimal WeightGrams,
    string Cohort = "", string OldLocation = "");
public sealed record Treatment(string Id, string Label);
public sealed record Study(Design Design, ImmutableArray<Animal> Pool,
    ImmutableArray<string> SelectedRecordIds, ImmutableArray<Treatment> Treatments,
    int Replicates, int AnimalsPerPen, bool FilterBalance = false,
    decimal CvLimitPercent = 0.5m, int MaximumAttemptsPerBlock = 30, bool Demonstration = false);
public sealed record Position(string AnimalRecordId, int TreatmentIndex, int Replicate, int Block,
    int OriginalSubblock, int OriginalBlock = 0);
public sealed record PenStatistics(int TreatmentIndex, int Replicate, int Count,
    decimal TotalWeightGrams, decimal MeanWeightGrams, double? SampleSdGrams);
public sealed record BlockStatistics(int Block, double CvPercent);
public sealed record AttemptTrace(int Block, int Attempts, double BestCvPercent, double LastCvPercent);
public sealed record PhysicalPen(string Id, string Location, int Capacity, int? EligibleBlock = null);
public sealed record PenLocation(int TreatmentIndex, int Replicate, string PhysicalPenId);
public sealed record SwapEdit(string FirstRecordId, string SecondRecordId, string Reason,
    Position BeforeFirst, Position BeforeSecond, Position AfterFirst, Position AfterSecond);
public sealed record Allocation(string EngineVersion, string InputFingerprint, ulong Seed,
    ImmutableArray<Position> Positions, ImmutableArray<PenStatistics> Pens,
    ImmutableArray<BlockStatistics> Blocks, ImmutableArray<AttemptTrace> Attempts,
    ImmutableArray<SwapEdit> Edits, ImmutableArray<PenLocation> Locations,
    ulong? LocationSeed, int Revision);
public enum RunStatus { Success, Invalid, Exhausted, Cancelled }
public sealed record RunResult(RunStatus Status, Allocation? Allocation,
    ImmutableArray<string> Errors, ImmutableArray<AttemptTrace> Attempts);
