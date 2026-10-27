using System.Collections.Immutable;
using PoultryDesigner.Engine;

namespace PoultryDesigner.AppCore;

// UI-independent lifecycle. Candidate opens and failed saves/runs cannot replace completed work.
public sealed class DesktopWorkspace
{
    private ProjectSession session = Empty("Untitled study");
    private ProjectDocument? saved;
    private int busy;
    private string? diskDigest;
    private string? recoveredFrom;
    public bool IsBusy => Volatile.Read(ref busy) != 0;
    public ProjectDocument Document => session.Document;
    public ProjectInputs Inputs => session.CurrentInput.Inputs;
    public string? FilePath { get; private set; }
    public bool IsDirty => !ReferenceEquals(saved, Document);
    public bool IsReady => session.IsReady;
    public bool IsStale => session.IsStale;
    public bool HasEarlierInputRevision => session.HasEarlierInputRevision;
    public DesktopWorkspace() { saved = Document; }
    private static ProjectSession Empty(string name) => ProjectSession.Create(name,
        ProjectInputs.FromStudy(new Study(Design.Rcbd, [], [], [], 0, 0), "Initial draft"), "Create study");
    private void Enter()
    { if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException("Wait for the current operation or cancel it first."); }
    private void Leave() => Volatile.Write(ref busy, 0);
    private void RequireDiscard(bool discard)
    { if (IsDirty && !discard) throw new InvalidOperationException("Save or explicitly discard unsaved changes first."); }
    private void Change(Action<ProjectSession> action)
    { Enter(); try { action(session); } finally { Leave(); } }
    public void New(string name, bool discardChanges = false)
    {
        Enter(); try { RequireDiscard(discardChanges); var candidate = Empty(name); session = candidate;
            FilePath = null; diskDigest = null; recoveredFrom = null; saved = null; } finally { Leave(); }
    }
    public async Task OpenAsync(string path, bool discardChanges = false, CancellationToken cancellation = default, bool recoverBackup = false)
    {
        Enter();
        try
        {
            RequireDiscard(discardChanges);
            var candidate = await Task.Run(() => ProjectStore.OpenWithDigest(recoverBackup ? path + ".bak" : path, cancellation)).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            session = candidate.Session; FilePath = recoverBackup ? null : Path.GetFullPath(path);
            diskDigest = recoverBackup ? null : candidate.FileDigest; saved = recoverBackup ? null : Document;
            recoveredFrom = recoverBackup ? Path.GetFullPath(path) : null;
        }
        finally { Leave(); }
    }
    public async Task SaveAsync(string? path = null, CancellationToken cancellation = default)
    {
        Enter();
        try
        {
            string target = Path.GetFullPath(path ?? FilePath ?? throw new ArgumentException("Choose a project filename."));
            if (recoveredFrom is not null && (string.Equals(target, recoveredFrom, StringComparison.OrdinalIgnoreCase)
                || string.Equals(target, recoveredFrom + ".bak", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Save recovered work to a new project filename.");
            var snapshot = Document;
            string? expected = string.Equals(target, FilePath, StringComparison.OrdinalIgnoreCase) ? diskDigest : null;
            string digest = await Task.Run(() => ProjectStore.Save(snapshot, target, cancellation, expected)).ConfigureAwait(false);
            saved = snapshot; FilePath = target; diskDigest = digest; recoveredFrom = null;
        }
        finally { Leave(); }
    }
    public void Revise(ProjectInputs inputs, string reason) => Change(s => s.Revise(inputs, reason));
    public void ImportAnimals(ImportSource source, AnimalImport import, bool append, string reason) => Change(s => s.ImportAnimals(source, import, append, reason));
    public void ImportAutomaticAnimalPool(ImportSource source, AnimalImport import, bool append, string reason) => Change(s => {
        s.ImportAnimals(source, import, append, reason);
        if (!s.CurrentInput.Inputs.AutomaticPoolSelection)
            s.Revise(s.CurrentInput.Inputs with { AutomaticPoolSelection = true }, "Automatically select heaviest animals for allocation");
    });
    public void ImportTreatments(ImportSource source, TreatmentImport import, string reason) => Change(s => s.ImportTreatments(source, import, reason));
    public void Select(ImmutableArray<SelectionDecision> decisions, string reason) => Change(s => s.Select(decisions, reason));
    public void SaveSelectionDraft(ImmutableArray<SelectionDecision> decisions, string reason) => Change(s => s.Revise(s.CurrentInput.Inputs with {
        Selection = decisions, Study = s.CurrentInput.Inputs.Study with {
            SelectedRecordIds = decisions.Where(x => x.Choice == SelectionChoice.Include).Select(x => x.RecordId).ToImmutableArray() } }, reason));
    public void SelectExactPool(string reason) => Change(s => s.SelectExactPool(reason));
    public ChosenRevision Choose(string id) => session.Choose(id);
    public void Activate(string id, string reason) => Change(s => s.Activate(id, reason));
    public Allocation PreviewSwap(string first, string second, string reason)
    {
        var chosen = Choose(Document.ActiveResultId ?? throw new InvalidOperationException("No active allocation."));
        if (chosen.Input.Id != Document.CurrentInputId) throw new InvalidOperationException("Run the current inputs before editing.");
        return AllocationEngine.Swap(chosen.Input.Inputs.Study, chosen.Allocation, first, second, reason);
    }
    public void Swap(string first, string second, string reason, string? expectedResultId = null) => Change(s => {
        if (expectedResultId is not null && s.Document.ActiveResultId != expectedResultId) throw new InvalidOperationException("Active result changed. Preview the swap again.");
        s.Swap(first, second, reason);
    });
    public Allocation PreviewMap(ImmutableArray<PhysicalPen> pens, ulong seed)
    {
        var chosen = Choose(Document.ActiveResultId ?? throw new InvalidOperationException("No active allocation."));
        if (chosen.Input.Id != Document.CurrentInputId) throw new InvalidOperationException("Run the current inputs before mapping.");
        return AllocationEngine.MapPens(chosen.Input.Inputs.Study, chosen.Allocation, pens, seed);
    }
    public void Map(ImmutableArray<PhysicalPen> pens, ulong seed, string reason, string? expectedResultId = null) => Change(s => {
        if (expectedResultId is not null && s.Document.ActiveResultId != expectedResultId) throw new InvalidOperationException("Active result changed. Preview the mapping again.");
        s.MapPens(pens, seed, reason);
    });
    public async Task<RunResult> RunAsync(ulong seed, string reason, CancellationToken cancellation = default, IProgress<(int Block, int Attempt)>? progress = null)
    {
        Enter(); try { return await Task.Run(() => session.Run(seed, reason, cancellation, (b, a) => progress?.Report((b, a)))).ConfigureAwait(false); }
        finally { Leave(); }
    }
}
