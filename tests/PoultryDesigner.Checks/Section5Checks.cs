using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using PoultryDesigner.AppCore;
using PoultryDesigner.Engine;

internal static class Section5Checks
{
    private sealed class InlineProgress(Action<(int Block, int Attempt)> callback) : IProgress<(int Block, int Attempt)>
    { public void Report((int Block, int Attempt) value) => callback(value); }
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception("Section 5: " + message); }
        void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException) { rejected = true; } Check(rejected, message); }
        string Json(object value) => JsonSerializer.Serialize(value);
        var workspace = new DesktopWorkspace();
        Check(!workspace.IsDirty && !workspace.IsReady && workspace.FilePath is null, "Initial empty workspace is clean and incomplete.");
        var study = new Study(Design.Rcbd, [], [], [new("T1", "Control"), new("T2", "Diet")], 3, 2);
        workspace.Revise(ProjectInputs.FromStudy(study, "Initial controls"), "Configure study");
        var source = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW\n" + string.Join("\n", Enumerable.Range(1, 13).Select(i => $"{i:D3},{100 + i}"))), "controller.csv");
        var batch = DataImport.Animals(source, "Delimited", 1, 2, 14, new(1, 2)).Value!;
        workspace.ImportAnimals(source, batch, false, "Researcher imported source");
        Check(workspace.IsDirty && !workspace.IsReady && workspace.Inputs.Study.SelectedRecordIds.IsEmpty, "Imported pool has no automatically selected records.");
        var decisions = batch.Animals.Select((x, i) => new SelectionDecision(x.RecordId, i < 12 ? SelectionChoice.Include : SelectionChoice.Pending,
            i < 12 ? "Researcher included" : "Pending capacity review")).ToImmutableArray();
        workspace.SaveSelectionDraft(decisions, "Save incomplete selection review");
        Check(!workspace.IsReady && workspace.Inputs.Study.SelectedRecordIds.Length == 12, "Correct count still cannot bypass pending decisions.");
        var pending = workspace.RunAsync(0, "Blocked run").GetAwaiter().GetResult();
        Check(pending.Status == RunStatus.Invalid && workspace.Document.ActiveResultId is null, "Controller respects unresolved selection.");
        Reject(() => workspace.SelectExactPool("Choose all"), "Oversized pool cannot use exact-pool shortcut.");
        decisions = decisions.SetItem(12, decisions[12] with { Choice = SelectionChoice.Exclude, Reason = "Researcher exclusion" });
        workspace.Select(decisions, "Commit researcher-selected twelve");
        Check(workspace.IsReady, "Exact explicit subset becomes ready.");
        var result = workspace.RunAsync(14, "Initial completed run").GetAwaiter().GetResult();
        Check(result.Status == RunStatus.Success && !workspace.IsBusy, "Background run commits and releases busy state.");
        string originalId = workspace.Document.ActiveResultId!; var chosen = workspace.Choose(originalId);
        var a = chosen.Allocation.Positions[0]; var b = chosen.Allocation.Positions.First(x => x.Replicate != a.Replicate);
        string before = Json(workspace.Document);
        var preview = workspace.PreviewSwap(a.AnimalRecordId, b.AnimalRecordId, "Preview only");
        Check(preview.Revision == chosen.Allocation.Revision + 1 && Json(workspace.Document) == before, "Swap preview cannot commit an edit.");
        workspace.Swap(a.AnimalRecordId, b.AnimalRecordId, "Researcher applied swap");
        Check(workspace.Document.ActiveResultId != originalId && chosen.Allocation.Edits.IsEmpty, "Applied swap logs new immutable revision.");
        before = Json(workspace.Document);
        Reject(() => workspace.Swap(a.AnimalRecordId, b.AnimalRecordId, "Stale preview", originalId), "Swap application rejects a changed preview parent.");
        Check(before == Json(workspace.Document), "Stale preview leaves the active revision and history unchanged.");
        var physical = Enumerable.Range(1, 6).Select(i => new PhysicalPen($"P{i}", $"Room A/Pen {i}", 2, (i - 1) / 2 + 1)).ToImmutableArray();
        before = Json(workspace.Document); workspace.PreviewMap(physical, 4);
        Check(before == Json(workspace.Document), "Physical-map preview is also uncommitted.");
        workspace.Map(physical, 4, "Reviewed physical map");
        var mapped = workspace.Choose(workspace.Document.ActiveResultId!);
        Check(mapped.PhysicalPens.Length == 6 && mapped.Allocation.Locations.Length == 6, "Mapped output retains physical provenance.");
        workspace.Activate(originalId, "Review historical allocation");
        Check(workspace.Document.ActiveResultId == originalId && mapped.Allocation.Locations.Length == 6, "Historical activation cannot alter an already chosen output.");

        string output = Path.Combine(root, "analysis", "desktop-validation"); Directory.CreateDirectory(output);
        string path = Path.Combine(output, "controller-test-" + Guid.NewGuid().ToString("N") + ".poultryproj");
        workspace.SaveAsync(path).GetAwaiter().GetResult();
        Check(!workspace.IsDirty && workspace.FilePath == Path.GetFullPath(path), "Save updates the saved checkpoint and filename.");
        string oldHash = ProjectStore.FileDigest(path);
        workspace.RunAsync(15, "Second completed run").GetAwaiter().GetResult();
        Check(workspace.IsDirty, "A completed run marks the project dirty.");
        before = Json(workspace.Document);
        Reject(() => workspace.New("Other"), "Dirty new-project transition needs explicit discard.");
        Reject(() => workspace.OpenAsync(path).GetAwaiter().GetResult(), "Dirty open transition also needs explicit discard.");
        Check(before == Json(workspace.Document) && workspace.IsDirty, "Rejected transitions preserve unsaved project.");
        workspace.SaveAsync().GetAwaiter().GetResult();
        Check(!workspace.IsDirty && ProjectStore.FileDigest(path) != oldHash, "Second save refreshes the conflict digest and checkpoint.");
        var opened = new DesktopWorkspace(); opened.OpenAsync(path).GetAwaiter().GetResult();
        Check(!opened.IsDirty && Json(opened.Document) == Json(workspace.Document), "Opening an existing project starts from exactly the saved state.");
        string malformed = path + ".malformed.poultryproj"; File.WriteAllText(malformed, "{ truncated");
        before = Json(opened.Document);
        Reject(() => opened.OpenAsync(malformed).GetAwaiter().GetResult(), "Malformed candidate open fails.");
        Check(!opened.IsDirty && opened.FilePath == path && Json(opened.Document) == before, "Failed open retains original session, path and saved state.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); bool hit = false;
            try { opened.OpenAsync(path, cancellation: cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { hit = true; }
            Check(hit && !opened.IsBusy && before == Json(opened.Document), "Cancelled open retains previous workspace.");
        }
        opened.RunAsync(16, "Unsaved run").GetAwaiter().GetResult(); before = Json(opened.Document);
        File.WriteAllText(path, "Externally truncated project");
        Reject(() => opened.SaveAsync().GetAwaiter().GetResult(), "Disk changes cannot be overwritten by stale desktop state.");
        Check(opened.IsDirty && before == Json(opened.Document) && File.ReadAllText(path) == "Externally truncated project", "Failed save retains dirty project and external target.");
        var recovery = new DesktopWorkspace(); recovery.OpenAsync(path, recoverBackup: true).GetAwaiter().GetResult();
        Check(recovery.IsDirty && recovery.FilePath is null && recovery.Document.ActiveResultId == originalId, "Backup opens explicitly as unsaved recovered work.");
        Reject(() => recovery.SaveAsync(path).GetAwaiter().GetResult(), "Recovered project must use a new filename.");
        string restored = path + ".restored.poultryproj"; recovery.SaveAsync(restored).GetAwaiter().GetResult();
        Check(!recovery.IsDirty && ProjectStore.Load(restored).ActiveResultId == originalId, "Recovered work saves to a new validated project.");

        var concurrent = new DesktopWorkspace(); concurrent.OpenAsync(restored).GetAwaiter().GetResult();
        var currentResult = concurrent.Document.ActiveResultId;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var cancelRun = new CancellationTokenSource();
        var running = concurrent.RunAsync(20, "Cancellation during run", cancelRun.Token, new InlineProgress(_ =>
        {
            entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Progress test timed out.");
        }));
        Check(entered.Wait(TimeSpan.FromSeconds(15)) && concurrent.IsBusy, "Background run advertises busy state while native UI remains available.");
        try { Reject(() => concurrent.New("Cannot overlap", true), "Concurrent mutation is rejected before touching locked session."); cancelRun.Cancel(); }
        finally { release.Set(); }
        Check(running.GetAwaiter().GetResult().Status == RunStatus.Cancelled && !concurrent.IsBusy && concurrent.Document.ActiveResultId == currentResult,
            "During-run cancellation restores idle state and retains completed result.");
        Check(concurrent.IsDirty, "Failed/cancelled attempt history is still an unsaved change.");
        var oldInputs = concurrent.Inputs;
        concurrent.Revise(oldInputs with { Study = oldInputs.Study with { Pool = oldInputs.Study.Pool.SetItem(0, oldInputs.Study.Pool[0] with { WeightGrams = 900 }) } }, "Researcher BW correction");
        Check(concurrent.IsStale && concurrent.HasEarlierInputRevision, "Changed BW visibly marks retained allocation stale.");
        Reject(() => concurrent.PreviewSwap(a.AnimalRecordId, b.AnimalRecordId, "Old result edit"), "Preview refuses to rebind historical data to corrected inputs.");
        using (var cancelledSave = new CancellationTokenSource())
        {
            cancelledSave.Cancel(); bool hit = false; before = Json(concurrent.Document);
            try { concurrent.SaveAsync(cancellation: cancelledSave.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { hit = true; }
            Check(hit && concurrent.IsDirty && !concurrent.IsBusy && before == Json(concurrent.Document), "Cancelled save retains unsaved history and releases busy state.");
        }
        foreach (string file in new[] { path, path + ".bak", malformed, restored }) File.Delete(file);
        File.WriteAllText(Path.Combine(output, "controller-check-results.txt"), $"PASS: {checks} Section 5 controller assertions.\n");
        Console.WriteLine($"PASS: Section 5 desktop lifecycle, exact selection, previews, busy/cancellation and recovery ({checks} assertions).");
        return checks;
    }
}
