using System.Collections.Immutable;

namespace PoultryDesigner.Engine;

internal static class ProjectValidation
{
    internal static void ValidateAnimalOrigins(IEnumerable<ProjectInputs> previousInputs, ProjectInputs candidate)
    {
        var origins = previousInputs.SelectMany(x => x.Animals).GroupBy(x => x.RecordId)
            .ToDictionary(x => x.Key, x => x.First().Origin, StringComparer.Ordinal);
        if (candidate.Animals.Any(x => origins.TryGetValue(x.RecordId, out var original) && x.Origin != original))
            throw new ArgumentException("Animal record/source-row binding is immutable. Import a new source row as a new record; retain prior revisions.");
    }
    internal static AnimalImport ReimportAnimals(ImportSource source, ImportApplication app)
    {
        int? Column(string name) => app.Columns.SingleOrDefault(x => x.Field == name)?.Column;
        var preview = DataImport.Animals(source, app.Table, app.HeaderRow, app.FirstRow, app.LastRow,
            new(Column("AnimalId") ?? 0, Column("Weight") ?? 0, Column("Cohort"), Column("OldLocation"),
                Column("MaternalId"), Column("PaternalId"), Column("Notes")), app.WeightUnit, app.Demonstration, app.AllowIntegerIds);
        var imported = preview.Value ?? throw new ArgumentException("Retained animal import has invalid source rows.");
        if (ProjectSession.Digest(imported.Application with { Id = app.Id }) != ProjectSession.Digest(app))
            throw new ArgumentException("Animal import mapping is inconsistent.");
        return imported;
    }
    internal static TreatmentImport ReimportTreatments(ImportSource source, ImportApplication app)
    {
        int? Column(string name) => app.Columns.SingleOrDefault(x => x.Field == name)?.Column;
        var preview = DataImport.Treatments(source, app.Table, app.HeaderRow, app.FirstRow, app.LastRow,
            new(Column("TreatmentId") ?? 0, Column("Label") ?? 0,
                app.Columns.Where(x => x.Field.StartsWith("Factor", StringComparison.Ordinal)).Select(x => x.Column).ToImmutableArray()), app.AllowIntegerIds);
        var imported = preview.Value ?? throw new ArgumentException("Retained treatment import has invalid source rows.");
        if (ProjectSession.Digest(imported.Application with { Id = app.Id }) != ProjectSession.Digest(app))
            throw new ArgumentException("Treatment import mapping is inconsistent.");
        return imported;
    }
    internal static void ValidateAnimalImport(ImportSource source, AnimalImport batch)
    {
        var replay = ReimportAnimals(source, batch.Application);
        if (batch.Animals.IsDefault || batch.Details.IsDefault || batch.Animals.Length != replay.Animals.Length || batch.Details.Length != replay.Details.Length)
            throw new ArgumentException("Incomplete animal import.");
        for (int i = 0; i < batch.Animals.Length; i++)
            if (batch.Animals[i] with { RecordId = replay.Animals[i].RecordId } != replay.Animals[i]
                || batch.Details[i].RecordId != batch.Animals[i].RecordId
                || batch.Details[i].Origin?.ApplicationId != batch.Application.Id
                || batch.Details[i] with { RecordId = replay.Details[i].RecordId, Origin = batch.Details[i].Origin is null ? null :
                    batch.Details[i].Origin! with { ApplicationId = replay.Application.Id } } != replay.Details[i])
                throw new ArgumentException("Imported animal values or provenance disagree with original source rows.");
    }
    internal static void ValidateTreatmentImport(ImportSource source, TreatmentImport batch)
    {
        var replay = ReimportTreatments(source, batch.Application);
        if (!batch.Treatments.SequenceEqual(replay.Treatments) || batch.Details.Length != replay.Details.Length)
            throw new ArgumentException("Treatment import disagrees with original source rows.");
        for (int i = 0; i < batch.Details.Length; i++)
            if (batch.Details[i].Origin?.ApplicationId != batch.Application.Id || ProjectSession.Digest(batch.Details[i] with { Origin = batch.Details[i].Origin is null ? null :
                batch.Details[i].Origin! with { ApplicationId = replay.Application.Id } }) != ProjectSession.Digest(replay.Details[i]))
                throw new ArgumentException("Treatment provenance disagrees with original source rows.");
    }

    internal static void ValidateSource(ImportSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Id) || source.Tables.IsDefault || source.CapturedUtc == default)
            throw new ArgumentException("Invalid source metadata.");
        byte[] bytes = Convert.FromBase64String(source.ContentBase64);
        var parsed = DataImport.Read(bytes, source.FileName, source.OriginalPath);
        if (source.Sha256 != parsed.Sha256 || source.Format != parsed.Format
            || ProjectSession.Digest(source.Tables) != ProjectSession.Digest(parsed.Tables))
            throw new ArgumentException("Source bytes, digest and parsed tables disagree.");
    }

    internal static void ValidateInputs(ProjectInputs input, ImmutableArray<ImportSource> sources)
    {
        if (input.PreparedLayout is not null) HousingLayouts.Validate(input.PreparedLayout);
        if (input.Animals.IsDefault || input.Treatments.IsDefault || input.Selection.IsDefault || input.Imports.IsDefault
            || input.ExperimentalUnit is not ("pen" or "cage") || input.Notes is null)
            throw new ArgumentException("Initialize project metadata and declare pen or cage as the experimental unit.");
        var study = input.Study;
        if (study.Pool.IsDefault || study.Treatments.IsDefault || study.SelectedRecordIds.IsDefault)
            throw new ArgumentException("Pool, treatments and selection must be initialized.");
        if (study.Replicates < 0 || study.Replicates > AllocationEngine.MaximumAnimals
            || study.AnimalsPerPen < 0 || study.AnimalsPerPen > AllocationEngine.MaximumAnimals)
            throw new ArgumentException("Draft counts must be nonnegative and within the supported limits.");
        long required = (long)study.Treatments.Length * study.Replicates * study.AnimalsPerPen;
        // Setup-incomplete drafts can be saved. Readiness and Run still use the full engine validation.
        var errors = AllocationEngine.Validate(study).Where(x => x !=
            "Select exactly the required number of animals; automatic selection is unavailable."
            && x != "Require at least two treatments, two replicates and one animal per pen."
            && !(required == 0 && x == $"Required animal count must be between 1 and {AllocationEngine.MaximumAnimals}.")).ToArray();
        if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
        if (input.AutomaticPoolSelection) {
            var expected = AutomaticPoolSelection.Apply(input);
            if (!input.Study.SelectedRecordIds.SequenceEqual(expected.Study.SelectedRecordIds) || !input.Selection.SequenceEqual(expected.Selection))
                throw new ArgumentException("Automatic allocation pool must use the recorded heaviest-first selection rule.");
        }
        if (study.Pool.Any(x => x.OldLocation is null)) throw new ArgumentException("Animal location metadata must be initialized.");
        bool Same(IEnumerable<string> first, IEnumerable<string> second) => first.Order(StringComparer.Ordinal).SequenceEqual(second.Order(StringComparer.Ordinal));
        if (input.Selection.Any(x => x is null || !Enum.IsDefined(x.Choice) || string.IsNullOrWhiteSpace(x.Reason))
            || !Same(input.Selection.Select(x => x.RecordId), study.Pool.Select(x => x.RecordId))
            || !Same(input.Selection.Where(x => x.Choice == SelectionChoice.Include).Select(x => x.RecordId), study.SelectedRecordIds))
            throw new ArgumentException("Selection requires one explicit decision per pool record, reasons and an exact match to included IDs.");
        if (input.Animals.Any(x => x is null || x.MaternalId is null || x.PaternalId is null || x.Notes is null)
            || !Same(input.Animals.Select(x => x.RecordId), study.Pool.Select(x => x.RecordId))
            || input.Treatments.Any(x => x is null || x.FactorColumns.IsDefault)
            || !Same(input.Treatments.Select(x => x.TreatmentId), study.Treatments.Select(x => x.Id)))
            throw new ArgumentException("Animal/treatment metadata must cover each record exactly once.");
        if (input.Imports.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id))
            || input.Imports.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != input.Imports.Length)
            throw new ArgumentException("Import application IDs must be unique.");
        foreach (var app in input.Imports)
        {
            var source = sources.SingleOrDefault(x => x.Id == app.SourceId) ?? throw new ArgumentException("Missing original import source.");
            var table = source.Tables.SingleOrDefault(x => x.Name == app.Table) ?? throw new ArgumentException("Missing original source table.");
            if (app.Columns.IsDefault || app.Columns.Length < 2 || app.Columns.Any(x => x is null || string.IsNullOrWhiteSpace(x.Field) || x.Column < 1 || x.Column > 16_384)
                || app.Columns.Select(x => x.Field).Distinct().Count() != app.Columns.Length
                || app.Columns.Select(x => x.Column).Distinct().Count() != app.Columns.Length
                || app.HeaderRow < 1 || app.FirstRow <= app.HeaderRow || app.LastRow < app.FirstRow
                || app.LastRow - (long)app.FirstRow >= AllocationEngine.MaximumAnimals
                || !table.Rows.Any(x => x.Number == app.HeaderRow)
                || app.WeightUnit is not ("" or "g" or "kg")) throw new ArgumentException("Invalid retained import mapping.");
            if (app.WeightUnit.Length > 0) ReimportAnimals(source, app);
            else ReimportTreatments(source, app);
        }
        void Origin(RowOrigin? origin, bool animal)
        {
            if (origin is null) return;
            var app = input.Imports.SingleOrDefault(x => x.Id == origin.ApplicationId) ?? throw new ArgumentException("Missing row import application.");
            if (origin.Row < app.FirstRow || origin.Row > app.LastRow
                || !sources.Single(x => x.Id == app.SourceId).Tables.Single(x => x.Name == app.Table).Rows.Any(x => x.Number == origin.Row)
                || !app.Columns.Any(x => x.Field == (animal ? "AnimalId" : "TreatmentId")))
                throw new ArgumentException("Invalid source-row provenance.");
        }
        foreach (var animal in input.Animals) Origin(animal.Origin, true);
        foreach (var treatment in input.Treatments)
        {
            Origin(treatment.Origin, false);
            if (treatment.FactorColumns.Length > 0 && (treatment.Origin is null || treatment.FactorColumns.Any(x =>
                !input.Imports.Single(a => a.Id == treatment.Origin.ApplicationId).Columns.Contains(x))))
                throw new ArgumentException("Factor metadata must refer to retained source columns.");
        }
    }

    internal static void Validate(ProjectDocument project, CancellationToken cancellation = default)
    {
        if (project.SchemaVersion != ProjectStore.SchemaVersion) throw new ArgumentException("Unsupported project schema version.");
        if (string.IsNullOrWhiteSpace(project.Id) || string.IsNullOrWhiteSpace(project.Name) || project.Sources.IsDefault
            || project.Inputs.IsDefaultOrEmpty || project.Events.IsDefault || project.Inputs.Length > 10_000 || project.Events.Length > 100_000)
            throw new ArgumentException("Invalid project structure or history limits.");
        if (project.Sources.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != project.Sources.Length
            || project.Inputs.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != project.Inputs.Length
            || project.Events.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != project.Events.Length)
            throw new ArgumentException("Duplicate source/input/event IDs.");
        foreach (var source in project.Sources) { cancellation.ThrowIfCancellationRequested(); ValidateSource(source); }
        for (int i = 0; i < project.Inputs.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); var input = project.Inputs[i];
            if (string.IsNullOrWhiteSpace(input.Id) || input.Number != i + 1 || input.CreatedUtc == default
                || string.IsNullOrWhiteSpace(input.Reason) || input.FullInputDigest != ProjectSession.Digest(input.Inputs))
                throw new ArgumentException("Input revision sequence or full-input digest is invalid.");
            ValidateInputs(input.Inputs, project.Sources);
            ValidateAnimalOrigins(project.Inputs.Take(i).Select(x => x.Inputs), input.Inputs);
        }
        if (project.CurrentInputId != project.Inputs[^1].Id) throw new ArgumentException("Current input must be the latest retained revision.");
        if (project.Inputs.SelectMany(x => x.Inputs.Imports).GroupBy(x => x.Id)
            .Any(g => g.Select(ProjectSession.Digest).Distinct(StringComparer.Ordinal).Count() != 1))
            throw new ArgumentException("Retained import application IDs cannot change across input revisions.");
        var results = new Dictionary<string, ProjectEvent>(StringComparer.Ordinal);
        string? active = null;
        for (int i = 0; i < project.Events.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); var entry = project.Events[i];
            var input = project.Inputs.SingleOrDefault(x => x.Id == entry.InputRevisionId) ?? throw new ArgumentException("Missing event input revision.");
            if (string.IsNullOrWhiteSpace(entry.Id) || entry.Number != i + 1 || entry.CreatedUtc == default
                || string.IsNullOrWhiteSpace(entry.Reason) || !Enum.IsDefined(entry.Action)
                || entry.Errors.IsDefault || entry.Attempts.IsDefault || entry.PhysicalPens.IsDefault)
                throw new ArgumentException("Invalid event sequence or metadata.");
            var study = input.Inputs.Study;
            Allocation? replay = null;
            if (entry.Result is not null && entry.Result.EngineVersion != AllocationEngine.Version)
                throw new ArgumentException("Unsupported engine version; preserve the project for its matching engine.");
            if (entry.Action == ProjectAction.Run)
            {
                if (entry.ParentResultId is not null || entry.Status is null || !Enum.IsDefined(entry.Status.Value)
                    || entry.FirstRecordId is not null || entry.SecondRecordId is not null || !entry.PhysicalPens.IsEmpty)
                    throw new ArgumentException("Invalid run event fields.");
                if (entry.Status == RunStatus.Cancelled)
                {
                    if (entry.Result is not null || !entry.Errors.IsEmpty || !AllocationEngine.Validate(study).IsEmpty
                        || input.Inputs.Selection.Any(x => x.Choice == SelectionChoice.Pending)
                        || entry.Attempts.Any(x => x.Block < 1 || x.Block > study.Replicates || x.Attempts < 1
                            || x.Attempts > study.MaximumAttemptsPerBlock || !double.IsFinite(x.BestCvPercent)
                            || !double.IsFinite(x.LastCvPercent) || x.BestCvPercent < 0 || x.BestCvPercent > x.LastCvPercent)
                        || !entry.Attempts.Select(x => x.Block).SequenceEqual(Enumerable.Range(1, entry.Attempts.Length)))
                        throw new ArgumentException("Invalid cancellation record.");
                    // Cancellation timing is external; completed attempt traces are retained, not re-executed.
                }
                else
                {
                    var run = input.Inputs.Selection.Any(x => x.Choice == SelectionChoice.Pending)
                        ? new RunResult(RunStatus.Invalid, null, ["Resolve pending researcher selection before allocation."], [])
                        : AllocationEngine.Run(study, entry.Seed, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    if (run.Status != entry.Status || !run.Errors.SequenceEqual(entry.Errors) || !run.Attempts.SequenceEqual(entry.Attempts))
                        throw new ArgumentException("Saved run diagnostics do not replay.");
                    replay = run.Allocation;
                }
            }
            else
            {
                if (entry.ParentResultId is null || !results.TryGetValue(entry.ParentResultId, out var parent)
                    || parent.InputRevisionId != input.Id || entry.Status is not null || !entry.Errors.IsEmpty || !entry.Attempts.IsEmpty)
                    throw new ArgumentException("Missing or inconsistent parent revision.");
                if (entry.Action == ProjectAction.ChooseRevision)
                {
                    if (entry.Result is not null || !entry.PhysicalPens.IsEmpty || entry.FirstRecordId is not null || entry.SecondRecordId is not null)
                        throw new ArgumentException("Invalid chosen revision event.");
                    active = parent.Id; continue;
                }
                if (entry.Action == ProjectAction.Swap)
                {
                    if (entry.FirstRecordId is null || entry.SecondRecordId is null || !entry.PhysicalPens.IsEmpty)
                        throw new ArgumentException("Swap requires two records.");
                    replay = AllocationEngine.Swap(study, parent.Result!, entry.FirstRecordId, entry.SecondRecordId, entry.Reason);
                }
                else
                {
                    if (entry.FirstRecordId is not null || entry.SecondRecordId is not null) throw new ArgumentException("Invalid mapping event.");
                    replay = AllocationEngine.MapPens(study, parent.Result!, entry.PhysicalPens, entry.Seed);
                }
            }
            if ((replay is null) != (entry.Result is null) || (replay is not null && ProjectSession.Digest(replay) != ProjectSession.Digest(entry.Result)))
                throw new ArgumentException("Saved allocation, original provenance, metrics or edit sequence does not replay.");
            if (entry.Result is not null) { results.Add(entry.Id, entry); active = entry.Id; }
        }
        if (active != project.ActiveResultId) throw new ArgumentException("Active revision disagrees with retained history.");
    }
}
