using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

public enum SelectionChoice { Pending, Include, Exclude }
public sealed record SelectionDecision(string RecordId, SelectionChoice Choice, string Reason);
public sealed record ProjectInputs(Study Study, ImmutableArray<AnimalDetails> Animals,
    ImmutableArray<TreatmentDetails> Treatments, ImmutableArray<ImportApplication> Imports,
    ImmutableArray<SelectionDecision> Selection, string ExperimentalUnit = "pen", string Notes = "")
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public HousingPlan? PreparedLayout { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutomaticPoolSelection { get; init; }
    // This helper makes an already explicit engine selection reviewable in the project ledger.
    public static ProjectInputs FromStudy(Study study, string reason) => new(study,
        study.Pool.Select(x => new AnimalDetails(x.RecordId)).ToImmutableArray(),
        study.Treatments.Select(x => new TreatmentDetails(x.Id, FactorColumns: [])).ToImmutableArray(), [],
        study.Pool.Select(x => new SelectionDecision(x.RecordId, study.SelectedRecordIds.Contains(x.RecordId)
            ? SelectionChoice.Include : SelectionChoice.Exclude, reason)).ToImmutableArray());
}
public sealed record InputRevision(string Id, int Number, DateTimeOffset CreatedUtc, string Reason,
    string FullInputDigest, ProjectInputs Inputs);
public enum ProjectAction { Run, Swap, MapPens, ChooseRevision }
public sealed record ProjectEvent(string Id, int Number, DateTimeOffset CreatedUtc, string InputRevisionId,
    ProjectAction Action, string Reason, string? ParentResultId, ulong Seed, RunStatus? Status,
    ImmutableArray<string> Errors, ImmutableArray<AttemptTrace> Attempts, Allocation? Result,
    string? FirstRecordId, string? SecondRecordId, ImmutableArray<PhysicalPen> PhysicalPens);
public sealed record ProjectDocument(string Id, string Name, int SchemaVersion,
    ImmutableArray<ImportSource> Sources, ImmutableArray<InputRevision> Inputs,
    ImmutableArray<ProjectEvent> Events, string CurrentInputId, string? ActiveResultId);
public sealed record ChosenRevision(string ProjectId, string ProjectName, string ResultId,
    InputRevision Input, Allocation Allocation, ImmutableArray<PhysicalPen> PhysicalPens,
    ImmutableArray<ImportSource> Sources);
