using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace PoultryDesigner.Engine;

// Each mutation builds an immutable candidate before committing it under the session lock.
public sealed class ProjectSession
{
    private readonly object gate = new();
    private ProjectDocument document;
    private bool running;
    public ProjectDocument Document { get { lock (gate) return document; } private set { document = value; } }
    public InputRevision CurrentInput { get { lock (gate) return document.Inputs.Single(x => x.Id == document.CurrentInputId); } }
    public bool IsReady { get { lock (gate) return !CurrentInput.Inputs.Selection.Any(x => x.Choice == SelectionChoice.Pending)
        && AllocationEngine.Validate(CurrentInput.Inputs.Study).IsEmpty; } }
    public bool IsStale { get { lock (gate) return Document.ActiveResultId is not null &&
        (!IsReady || Choose(Document.ActiveResultId).Allocation.InputFingerprint != AllocationEngine.Fingerprint(CurrentInput.Inputs.Study)); } }
    public bool HasEarlierInputRevision { get { lock (gate) return Document.ActiveResultId is not null
        && Choose(Document.ActiveResultId).Input.Id != CurrentInput.Id; } }

    private ProjectSession(ProjectDocument document) { this.document = document; }
    internal static ProjectSession RestoreValidated(ProjectDocument document) => new(document);

    public static ProjectSession Create(string name, ProjectInputs inputs, string reason)
    {
        RequireReason(name); RequireReason(reason); inputs = AutomaticPoolSelection.Apply(inputs); ProjectValidation.ValidateInputs(inputs, []);
        var revision = Revision(inputs, 1, reason);
        return new(new(Guid.NewGuid().ToString("N"), name.Trim(), ProjectStore.SchemaVersion, [], [revision], [], revision.Id, null));
    }

    internal static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    internal static void RequireReason(string value)
    { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Supply a nonblank name or reason."); }
    private static InputRevision Revision(ProjectInputs inputs, int number, string reason) =>
        new(Guid.NewGuid().ToString("N"), number, DateTimeOffset.UtcNow, reason.Trim(), Digest(inputs), inputs);

    public InputRevision Revise(ProjectInputs inputs, string reason)
    {
        lock (gate) return CommitInputs(inputs, Document.Sources, reason);
    }
    private InputRevision CommitInputs(ProjectInputs inputs, ImmutableArray<ImportSource> sources, string reason)
    {
        EnsureNotRunning();
        inputs = AutomaticPoolSelection.Apply(inputs);
        RequireReason(reason); ProjectValidation.ValidateInputs(inputs, sources);
        ProjectValidation.ValidateAnimalOrigins(Document.Inputs.Select(x => x.Inputs), inputs);
        var previousImports = Document.Inputs.SelectMany(x => x.Inputs.Imports).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        if (inputs.Imports.Any(x => previousImports.TryGetValue(x.Id, out var previous) && Digest(x) != Digest(previous)))
            throw new ArgumentException("Retained import applications are immutable; use a new application ID for a new mapping.");
        var revision = Revision(inputs, Document.Inputs.Length + 1, reason);
        Document = Document with { Sources = sources, Inputs = Document.Inputs.Add(revision), CurrentInputId = revision.Id };
        return revision;
    }

    public InputRevision ImportAnimals(ImportSource source, AnimalImport import, bool append, string reason)
    {
        lock (gate)
        {
            if (import.Application.SourceId != source.Id) throw new ArgumentException("Import/source mismatch.");
            ProjectValidation.ValidateSource(source);
            ProjectValidation.ValidateAnimalImport(source, import);
            var current = CurrentInput.Inputs;
            var pool = append ? current.Study.Pool.AddRange(import.Animals) : import.Animals;
            var details = append ? current.Animals.AddRange(import.Details) : import.Details;
            var input = current with { Study = current.Study with { Pool = pool, SelectedRecordIds = [] }, Animals = details,
                Imports = current.Imports.Add(import.Application),
                Selection = pool.Select(x => new SelectionDecision(x.RecordId, SelectionChoice.Pending, "Awaiting researcher selection")).ToImmutableArray() };
            return CommitInputs(input, AddSource(source), reason);
        }
    }
    public InputRevision ImportTreatments(ImportSource source, TreatmentImport import, string reason)
    {
        lock (gate)
        {
            if (import.Application.SourceId != source.Id) throw new ArgumentException("Import/source mismatch.");
            ProjectValidation.ValidateSource(source);
            ProjectValidation.ValidateTreatmentImport(source, import);
            var current = CurrentInput.Inputs;
            return CommitInputs(current with { Study = current.Study with { Treatments = import.Treatments },
                Treatments = import.Details, Imports = current.Imports.Add(import.Application) }, AddSource(source), reason);
        }
    }
    private ImmutableArray<ImportSource> AddSource(ImportSource source)
    {
        var previous = Document.Sources.SingleOrDefault(x => x.Id == source.Id);
        if (previous is not null && Digest(previous) != Digest(source)) throw new ArgumentException("Source IDs are immutable.");
        return previous is null ? Document.Sources.Add(source) : Document.Sources;
    }

    public InputRevision SelectExactPool(string reason)
    {
        lock (gate)
        {
            var current = CurrentInput.Inputs;
            long required = (long)current.Study.Treatments.Length * current.Study.Replicates * current.Study.AnimalsPerPen;
            if (current.Study.Pool.Length != required) throw new ArgumentException("The complete pool must exactly match the required count.");
            return Select(current.Study.Pool.Select(x => new SelectionDecision(x.RecordId, SelectionChoice.Include, reason)).ToImmutableArray(), reason);
        }
    }
    public InputRevision Select(ImmutableArray<SelectionDecision> decisions, string reason)
    {
        lock (gate)
        {
            var current = CurrentInput.Inputs;
            var candidate = current with { Selection = decisions, Study = current.Study with {
                SelectedRecordIds = decisions.Where(x => x.Choice == SelectionChoice.Include).Select(x => x.RecordId).ToImmutableArray() } };
            ProjectValidation.ValidateInputs(candidate, Document.Sources);
            if (decisions.Any(x => x.Choice == SelectionChoice.Pending) || !AllocationEngine.Validate(candidate.Study).IsEmpty)
                throw new ArgumentException("Explicit selection must resolve every pool record and include exactly the required count.");
            return CommitInputs(candidate, Document.Sources, reason);
        }
    }

    public RunResult Run(ulong seed, string reason, CancellationToken cancellation = default, Action<int, int>? progress = null)
    {
        lock (gate)
        {
            EnsureNotRunning(); RequireReason(reason);
            var input = CurrentInput;
            RunResult result;
            running = true;
            try { result = input.Inputs.Selection.Any(x => x.Choice == SelectionChoice.Pending)
                    ? new RunResult(RunStatus.Invalid, null, ["Resolve pending researcher selection before allocation."], [])
                    : AllocationEngine.Run(input.Inputs.Study, seed, cancellation, progress); }
            finally { running = false; }
            var entry = Event(ProjectAction.Run, input.Id, reason, null, seed, result.Status, result.Errors, result.Attempts, result.Allocation);
            CommitEvent(entry); return result;
        }
    }
    public Allocation Swap(string first, string second, string reason)
    {
        lock (gate)
        {
            EnsureNotRunning(); RequireReason(reason); var parent = ActiveForEdit();
            var result = AllocationEngine.Swap(parent.Input.Inputs.Study, parent.Allocation, first, second, reason);
            CommitEvent(Event(ProjectAction.Swap, parent.Input.Id, reason, parent.ResultId, 0, null, [], [], result, first, second));
            return result;
        }
    }
    public Allocation MapPens(ImmutableArray<PhysicalPen> physical, ulong seed, string reason)
    {
        lock (gate)
        {
            EnsureNotRunning(); RequireReason(reason); var parent = ActiveForEdit();
            var result = AllocationEngine.MapPens(parent.Input.Inputs.Study, parent.Allocation, physical, seed);
            CommitEvent(Event(ProjectAction.MapPens, parent.Input.Id, reason, parent.ResultId, seed, null, [], [], result, physical: physical));
            return result;
        }
    }
    private ChosenRevision ActiveForEdit()
    {
        if (Document.ActiveResultId is null) throw new InvalidOperationException("No completed allocation.");
        var active = Choose(Document.ActiveResultId);
        // Even unchanged scientific fingerprints can belong to different full-source revisions.
        if (active.Input.Id != CurrentInput.Id) throw new InvalidOperationException("Run the current input revision before editing. Historical results remain available.");
        return active;
    }

    public ChosenRevision Choose(string resultId)
    {
        lock (gate)
        {
            var entry = Document.Events.SingleOrDefault(x => x.Id == resultId && x.Result is not null)
                ?? throw new ArgumentException("Choose a completed allocation revision.");
            var physical = entry.PhysicalPens;
            var ancestor = entry;
            while (physical.IsEmpty && ancestor.ParentResultId is not null)
            { ancestor = Document.Events.Single(x => x.Id == ancestor.ParentResultId); physical = ancestor.PhysicalPens; }
            var input = Document.Inputs.Single(x => x.Id == entry.InputRevisionId);
            var sourceIds = input.Inputs.Imports.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal);
            return new(Document.Id, Document.Name, entry.Id, input, entry.Result!, physical,
                Document.Sources.Where(x => sourceIds.Contains(x.Id)).ToImmutableArray());
        }
    }
    public void Activate(string resultId, string reason)
    {
        lock (gate)
        {
            EnsureNotRunning(); RequireReason(reason); var chosen = Choose(resultId);
            var entry = Event(ProjectAction.ChooseRevision, chosen.Input.Id, reason, resultId, 0, null, [], [], null);
            Document = Document with { Events = Document.Events.Add(entry), ActiveResultId = resultId };
        }
    }
    private ProjectEvent Event(ProjectAction action, string input, string reason, string? parent, ulong seed,
        RunStatus? status, ImmutableArray<string> errors, ImmutableArray<AttemptTrace> attempts, Allocation? result,
        string? first = null, string? second = null, ImmutableArray<PhysicalPen> physical = default) =>
        new(Guid.NewGuid().ToString("N"), Document.Events.Length + 1, DateTimeOffset.UtcNow, input, action, reason.Trim(), parent,
            seed, status, errors, attempts, result, first, second, physical.IsDefault ? [] : physical);
    private void CommitEvent(ProjectEvent entry) => Document = Document with {
        Events = Document.Events.Add(entry), ActiveResultId = entry.Result is null ? Document.ActiveResultId : entry.Id };
    private void EnsureNotRunning()
    { if (running) throw new InvalidOperationException("Project mutations are unavailable inside allocation progress callbacks."); }
}
