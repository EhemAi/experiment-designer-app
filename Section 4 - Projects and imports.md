# Section 4 — Projects and imports

October 3, 2026. Project schema: **1**. Allocation engine: `1.0.0-splitmix64`.

## Implemented scope

The dependency-free C# library now saves and loads versioned `.poultryproj` projects, imports animal and treatment tables, records explicit researcher selection, and recovers saved allocation/edit history. The Windows screens for these operations belong to Section 5.

Configured studies and setup-incomplete drafts can be saved. A draft can have no treatments or zero counts while setup is in progress. Invalid identities, weights, unknown design codes, duplicate treatment labels, unsupported settings and resource violations are still rejected. `IsReady` requires complete engine-valid settings, exactly the required selection and no pending selection decisions. Pen or cage is explicitly stored as the experimental unit.

No sex selection, allotment, blocking, quotas or validation were added. An unmapped legacy column is retained only as source metadata. No source contents or workbook instructions are executed.

## Import contract

`DataImport.Read` supports UTF-8 CSV, TSV, XLSX and XLSM. It reads the workbook package without Excel, VBA or external dependencies. It supports ordinary SpreadsheetML worksheets, shared/rich text and inline strings, stored numeric values, sparse cells and explicit source row numbers. It retains cell type, formula and style references plus the complete original file bytes. All worksheets and unmapped columns remain available as source evidence.

`DataImport.Animals` and `DataImport.Treatments` require an explicit table/sheet, header row, first/last data rows and column mapping. Row and column numbers are one-based. The range includes every chosen record; blank or missing worksheet records produce row-specific errors. CSV records spanning several physical lines retain their starting line number; continuation lines are not interpreted as additional records. There is no inferred first sheet, automatic header guessing, silent blank-row removal or automatic subset selection.

Animal identity and BW are required. Cohort, old location, maternal/paternal identity and notes can be mapped. Treatment identity and label are required; optional factor columns retain their original source headings and values through their column and row references. Treatment order is the source order and remains explicit in the study. A factorial builder is later interface work.

IDs are trimmed text with case and leading zeros preserved. Case-sensitive identity uniqueness is scoped by the explicitly mapped cohort. Duplicate identities are errors, including when appending to an existing pool. There is no first-row-wins merge, suffix-based tag renaming or arbitrary duplicate removal. Correct the source, map a real cohort, or explicitly mark the study/import as demonstration data. Internal animal record IDs are generated once for an accepted import preview and preserved through selection, sorting, runs, edits, saving and loading. Applying an import verifies its values and provenance against the retained source; changing a preview's values cannot bypass parsing.

Numeric workbook IDs are rejected by default. A researcher can explicitly enable `allowIntegerIds` for stored nonnegative integers of at most 15 digits; that choice is saved in the import application. Conversion uses the stored integer, not Excel display formatting. For example, a numeric cell displayed as `0012` imports as `12` under this explicit policy. Store tags as text to retain their intended leading zeros. Numeric dates, custom number formats and already-lost Excel precision cannot be reconstructed as tags by this policy.

BW input unit is explicitly `g` (default) or `kg`, converted to canonical decimal grams. The original value and unit remain recoverable from the source cells and mapping. Blank, zero, negative, NaN/infinite, overflow and weights above 1,000,000 grams are rejected. Decimal parsing uses `.` independently of Windows locale and does not accept thousands separators. This does not set a scientific eligibility threshold.

Any formula in a mapped field is rejected, including a formula with a cached value. Export/review a values-only source explicitly before importing calculated fields. Unmapped formulas remain source evidence. Workbook macros, relationships to external sheets and XML DTDs are never executed or followed. Legacy XLS, encrypted workbooks, strict-namespace workbooks and non-worksheet workbook relationships require an explicit conversion to a supported source. There is no claim that formatting, comments, charts or workbook calculations are rendered by the importer; their complete originals are retained in the source bytes.

Each source has its own ID, original filename/path, UTC capture time, SHA-256, bytes and parsed tables. Each application independently records sheet, range, columns, units and numeric-ID/demonstration choices. Each imported animal/treatment links to its application and original source row. Animal record/source-row bindings are immutable across input revisions. Corrections to current values create an input revision with a reason and preserve the original observations and earlier values. Replacing a source pool imports new source records; it is not an implicit identity merge.

Import previews return either the complete accepted batch or issues and no batch. Adding/replacing animals, adding sources and creating their input revision commit together. Failed imports leave the project and completed result unchanged.

## Selection and input revisions

Animal import creates a pool with **no selected records** and a pending decision for each animal. `SelectExactPool` is an explicit researcher action available only when the complete pool is exactly the required size. `Select` supplies one include/exclude decision and reason for every pool record; every pending decision must be resolved and the included count must be exact. No automatic median screening, sampling, inferred exclusions or seed for selection exists.

Replacing/appending the pool resets selection to pending so the researcher reviews the complete new pool. Appended duplicate internal or scoped animal identities are rejected. Treatment/settings changes cannot cause allocation until the current explicit selection meets the new required count.

Each input revision has an independent immutable ID, sequential number, UTC time, reason and full-input SHA-256. This digest covers the complete pool, unselected weights and locations, ancestry/notes, treatment order/factors, import applications, selection ledger and setup settings. It differs from the engine's scientific allocation fingerprint. Source IDs point to independently validated, immutable original source evidence; the full project payload checksum also covers that evidence.

`IsStale` indicates that the active result does not match ready scientific inputs. `HasEarlierInputRevision` separately identifies a result linked to an earlier complete input revision, including descriptive/unselected-source changes that do not change the scientific fingerprint. Earlier results retain their original input snapshot. Edits require the active result to belong to the current complete input revision; historical review does not silently rebind an old result to new source data.

## Allocation history and consistent outputs

`ProjectSession` records every structured run outcome, including invalid, exhausted and cancelled attempts. Successful runs, swaps, reversals and physical mappings create immutable result revisions. Failed runs and rejected edits preserve the previous completed result. Rejected edits/imports do not insert a successful history entry. Unexpected callback/programming exceptions also leave the completed project intact. A progress callback can inspect but cannot reenter a project mutation.

Events have independent IDs, sequence numbers, UTC times, input revision IDs and reasons. Run events preserve seed, status, diagnostics and attempt traces. Edit events preserve their parent revision and ordered before/after edit log. Mapping events preserve the exact physical pool, locations, capacities, eligibility and independent mapping seed. Both original block/subblock and current assignments survive swaps and reversals.

`Choose(resultId)` returns one immutable `ChosenRevision` containing the allocation, its exact input revision, relevant source evidence and applicable physical pool. Use that object for every view/export in one output operation. `Activate` records which completed revision was selected without deleting later history. Changing the active choice does not modify an already chosen snapshot. Movement and balance calculations continue to use that chosen input and allocation together.

On open, successful runs are replayed with their saved seed and supported engine version. Every swap and mapping is then replayed from its recorded parent and compared against the entire saved allocation, including original provenance, metrics, attempts, locations and edits. Invalid/exhausted run diagnostics must also replay. Cancellation timing is external: cancelled outcomes preserve their recorded traces, which are checked structurally, but do not claim to reproduce the instant at which cancellation occurred. Unsupported engine versions are rejected; the project is preserved for its matching engine.

## Project files, atomic saves and recovery

The JSON envelope declares `PoultryDesigner.Project`, schema 1 and a SHA-256 over its typed project payload. Sources, all input revisions, all events, the current input and chosen active result are contained in the file. A schema-specific reader rejects unknown/missing fields, duplicate JSON properties, invalid enum codes, absent parents, inconsistent selection, malformed sources, corrupted metrics/edits and checksum mismatches. Inspection JSON from Section 3 is not a project file and is not silently migrated. There are no earlier project schemas to migrate yet; future schemas are explicitly rejected instead of being interpreted as version 1.

`ProjectStore.Save` validates the candidate project, writes a uniquely named sibling temporary file with write-through and a flushed handle, checks cancellation, then uses filesystem replacement. The previous target becomes `.poultryproj.bak`. First saves use a same-directory move. A failed replacement leaves the existing project and previous backup intact, and cleans up that operation's temporary file. There is no delete-before-write fallback. Only `.poultryproj` is an allowed save target, preventing project saves from overwriting an XLSM/XLSX source.

A sibling exclusive lock prevents overlapping cooperating writers. `OpenWithDigest` returns the session and disk digest from the same validated read handle. Pass that digest as `expectedFileDigest` to Save and retain the newly returned digest; an externally changed target is rejected. Saving to a target with another project's ID is rejected even without a supplied digest. Open/read and Save support cancellation. Network/cloud filesystem durability and power-loss behavior depend on the filesystem; no hardware power-loss test was performed.

`LoadBackup(path)` explicitly validates and replays `path + ".bak"`, even if the main file is truncated. Save the recovered document to a **new** `.poultryproj` path, preserving the damaged main file and its backup for inspection. Loading never writes files and does not silently fall back to a backup. A failed load creates no replacement session, so the interface can retain its already open project. Save refuses to overwrite a malformed existing target rather than replacing a recoverable backup with corruption.

This milestone recovers **saved** history and the preceding saved version. It does not yet autosave unsaved memory changes or maintain an incremental crash journal. The interface must make save/backup recovery and unsaved changes visible. There is also no project authentication/signature: hashes detect accidental corruption, not deliberate rewriting of a file and its hashes.

## Library usage

Use configured or setup-incomplete immutable study inputs to create a session. Import remains a separate action from selection:

```csharp
var study = new Study(Design.Rcbd, [], [],
    [new("01", "Control"), new("02", "Supplement")], 2, 1);
var session = ProjectSession.Create("Example study",
    ProjectInputs.FromStudy(study, "Initial setup"), "Create study");
var source = DataImport.Read("animals.csv");
var preview = DataImport.Animals(source, "Delimited", 1, 2, 5,
    new AnimalColumns(Identity: 1, Weight: 2), weightUnit: "g");
if (!preview.Success) { /* Present preview.Issues and keep the current project. */ }
else
{
    session.ImportAnimals(source, preview.Value!, append: false, reason: "Import reviewed source");
    session.SelectExactPool("Researcher explicitly includes all four records");
    var run = session.Run(202612, "Initial randomization");
    if (run.Status == RunStatus.Success)
    {
        var chosen = session.Choose(session.Document.ActiveResultId!);
        var movement = ResultViews.Movement(chosen.Input.Inputs.Study, chosen.Allocation);
        var balance = ResultViews.Balance(chosen.Input.Inputs.Study, chosen.Allocation);
    }
    var diskDigest = ProjectStore.Save(session.Document, "example.poultryproj");
}

var opened = ProjectStore.OpenWithDigest("example.poultryproj");
var recoveredSession = opened.Session;
// After a successful run/edit/input revision, update the digest after saving:
var nextDigest = ProjectStore.Save(recoveredSession.Document, "example.poultryproj",
    expectedFileDigest: opened.FileDigest);
```

For a larger pool, supply explicit `SelectionDecision` records to `Select` instead of calling `SelectExactPool`. Treat source paths and row mappings in this example as explicit example choices, not discovery defaults.

## Limits and verification

Source files are bounded at 32 MiB; expanded workbook packages at 128 MiB, 10,000 entries and 2,000,000 cells. Mapped ranges are at most 100,000 animals/records with 16,384 supported worksheet columns. Delimited sources are bounded to 110,000 records including headers/context. Project files are bounded at 256 MiB, 10,000 input revisions and 100,000 history events. History replay can take time for large studies or many RCBD retries; loading verifies rather than trusting cached assignments. These are safeguards, not promises that every combination of maximum sizes will be fast. No truncation or automatic history pruning occurs.

`./build-and-check.ps1` runs the engine and Section 4 checks together and records their output in `analysis/check-results.txt`. The Section 4 runner checks CSV quoting/multiline physical provenance, text identity/cohorts/demonstration duplicates, unit conversions and invalid BW, treatment factors, sparse XLSX/shared strings/formulas/numeric-ID policy, malformed packages, all-or-nothing imports, pending/exact/subset selection, full-input revisions, failed/cancelled/exhausted lifecycle, cross-block edits/reversals/mapping replay, chosen outputs, incomplete setup round trips, malformed/future schemas, corruption, missing parents, source-row immutability, callback reentry, save conflicts, cancellation, atomic replacement failure, backups and a complete 100,000-animal import.

The final combined run passed **52,100 assertions**, including **117 Section 4 assertions**. Read-only import of the preserved original workbook found all 900 demonstration records; the new project retains the 6 × 12 × 12 dimensions, grams and 0.5% allowance with selection pending. It does not choose the 864 animals. The original XLSM SHA-256 remains `3a8b964c0877643bb1b94ea952b1aa055a87f2e0dadb650d74337460a57b1686`.

`analysis/project-validation/check-results.txt` contains Section 4 evidence. `latest-fixture.txt` identifies the latest saved synthetic round-trip fixture with multiple input, run, edit and mapping revisions. The fixture is test data, not a research allocation or an approved workbook subset.

## Section completion

Section 4 is complete at the library level. Section 5 will build the Windows interface around these operations: setup, source preview/mapping, duplicate errors, explicit include/exclude decisions, run status, chosen historical results, save/open/recovery and unsaved-change handling. Printable maps, Excel exports, randomization reports and packaging remain subsequent work. Every future output must consume one chosen immutable revision and identify the pen/cage experimental unit.
