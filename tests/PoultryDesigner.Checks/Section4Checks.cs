using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PoultryDesigner.Engine;

internal static class Section4Checks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception("Section 4: " + message); }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or InvalidOperationException or JsonException)
            { rejected = true; }
            Check(rejected, message);
        }
        string Json<T>(T value) => JsonSerializer.Serialize(value);
        string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
        ImportSource Csv(string value, string name = "animals.csv") => DataImport.Read(Encoding.UTF8.GetBytes(value), name);
        var treatmentSource = Csv("ID,Label,Feed,Dose\n01,Control,Basal,0\n02,Supplement,Basal,1\n", "treatments.csv");
        var trt = DataImport.Treatments(treatmentSource, "Delimited", 1, 2, 3, new(1, 2, [3, 4]));
        Check(trt.Success && trt.Value!.Treatments[0].Id == "01" && trt.Value.Details[0].FactorColumns.Length == 2, "Treatment identities/factors and source row provenance.");
        var source = Csv("ID,BW,Cohort,Old,Maternal,Paternal,Notes,Legacy\r\n 001 ,0.1,A,Room 1/01,M1,P1,\"first, note\",F\r\n002,0.11,A,Room 1/02,M2,P2,\"two\nlines\",M\r\n003,0.12,A,Room 1/03,,,\"quote \"\"here\"\"\",F\r\n004,0.13,A,Room 1/04,,,,M\r\n005,0.14,A,Room 1/05,,,,F\r\n");
        var preview = DataImport.Animals(source, "Delimited", 1, 2, 7, new(1, 2, 3, 4, 5, 6, 7), "kg");
        Check(preview.Success && preview.Value!.Animals.Length == 5, "Quoted CSV including multiline record is not dropped.");
        var imported = preview.Value!;
        Check(imported.Animals[0].AnimalId == "001" && imported.Animals[0].WeightGrams == 100m, "Text identity trimming, leading zeros and kilogram conversion.");
        Check(imported.Details[1].Notes == "two\nlines" && imported.Details[2].Origin!.Row == 5, "Multiline notes and physical source start lines.");
        Check(source.Tables[0].Rows[1].Cells[7].Value == "F" && Convert.FromBase64String(source.ContentBase64).SequenceEqual(Encoding.UTF8.GetBytes("ID,BW,Cohort,Old,Maternal,Paternal,Notes,Legacy\r\n 001 ,0.1,A,Room 1/01,M1,P1,\"first, note\",F\r\n002,0.11,A,Room 1/02,M2,P2,\"two\nlines\",M\r\n003,0.12,A,Room 1/03,,,\"quote \"\"here\"\"\",F\r\n004,0.13,A,Room 1/04,,,,M\r\n005,0.14,A,Room 1/05,,,,F\r\n")), "Unmapped legacy metadata and original bytes retained.");
        foreach (string weight in new[] { "", "0", "-1", "NaN", "Infinity", "1,000", "1000001", "79228162514264337593543950335" })
        {
            var bad = Csv($"ID,BW\n1,\"{weight}\"\n2,100\n");
            var result = DataImport.Animals(bad, "Delimited", 1, 2, 3, new(1, 2));
            Check(!result.Success && result.Value is null && result.Issues.Single().Row == 2, "Invalid BW is row-specific and all-or-nothing: " + weight);
        }
        var duplicate = Csv("ID,BW,Cohort\n01,100,A\n01,101,A\n");
        Check(!DataImport.Animals(duplicate, "Delimited", 1, 2, 3, new(1, 2, 3)).Success, "Duplicate scoped identity rejected.");
        Check(DataImport.Animals(duplicate, "Delimited", 1, 2, 3, new(1, 2, 3), demonstration: true).Success, "Explicit demonstration import permits repeated displayed IDs.");
        Check(DataImport.Animals(Csv("ID,BW,Cohort\n01,100,A\n01,101,B\n"), "Delimited", 1, 2, 3, new(1, 2, 3)).Success, "Explicit cohort makes identities distinct.");
        Check(DataImport.Animals(Csv("ID,BW\na,100\nA,101\n"), "Delimited", 1, 2, 3, new(1, 2)).Success, "Identity matching preserves case.");
        Check(!DataImport.Animals(Csv("ID,BW\n1,100\n,101\n"), "Delimited", 1, 2, 3, new(1, 2)).Success, "Missing ID rejected.");
        Reject(() => DataImport.Animals(source, "missing", 1, 2, 7, new(1, 2)), "No inferred worksheet.");
        Reject(() => DataImport.Animals(source, "Delimited", 1, 2, 7, new(1, 1)), "Duplicate mapped columns rejected.");
        Reject(() => DataImport.Animals(source, "Delimited", 1, 2, 7, new(1, 2), "lb"), "Unsupported units rejected.");
        Reject(() => Csv("ID,BW\n\"unfinished"), "Unclosed CSV quote.");
        Reject(() => Csv("ID,BW\n\"id\"junk,100"), "Characters after closing quote rejected.");
        Reject(() => Csv("ID,BW\nx\"y,100"), "Bare quote rejected.");
        Reject(() => DataImport.Read([0xff], "bad.csv"), "Invalid UTF-8 rejected.");
        Check(DataImport.Animals(Csv("ID\tBW\n001\t100", "a.tsv"), "Delimited", 1, 2, 2, new(1, 2)).Value!.Animals[0].AnimalId == "001", "TSV import preserves identity.");
        Check(!DataImport.Treatments(Csv("ID,Label\n01,A\n02,A\n"), "Delimited", 1, 2, 3, new(1, 2)).Success, "Duplicate treatment label rejected.");

        byte[] Workbook(string rows, string shared = "", string relationship = "worksheets/sheet1.xml", bool duplicateEntry = false)
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                void Part(string path, string text) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text); }
                Part("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Pool\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Part("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"{relationship}\"/></Relationships>");
                Part("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>{rows}</sheetData></worksheet>");
                if (shared.Length > 0) Part("xl/sharedStrings.xml", shared);
                if (duplicateEntry) Part("xl/workbook.xml", "duplicate");
            }
            return memory.ToArray();
        }
        string header = "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>ID</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>BW</t></is></c></row>";
        var workbook = DataImport.Read(Workbook(header + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>0</v></c><c r=\"B2\" s=\"3\"><v>100.25</v></c><c r=\"D2\"><f>1+1</f><v>2</v></c></row>",
            "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><r><t>00</t></r><r><t>1</t></r></si></sst>"), "a.xlsx");
        var wbImport = DataImport.Animals(workbook, "Pool", 1, 2, 2, new(1, 2));
        Check(wbImport.Success && wbImport.Value!.Animals[0].AnimalId == "001" && wbImport.Value.Animals[0].WeightGrams == 100.25m, "XLSX shared/rich strings and numeric grams.");
        Check(workbook.Tables[0].Rows[1].Cells.Single(x => x.Column == 4).Formula == "1+1", "Unmapped formula retained without execution.");
        Check(!DataImport.Animals(workbook, "Pool", 1, 2, 2, new(1, 4)).Success, "Cached formula cannot become BW.");
        var numericWorkbook = DataImport.Read(Workbook(header + "<row r=\"2\"><c r=\"A2\"><v>12</v></c><c r=\"B2\"><v>100</v></c></row>"), "numeric.xlsm");
        Check(!DataImport.Animals(numericWorkbook, "Pool", 1, 2, 2, new(1, 2)).Success, "Numeric worksheet IDs require explicit policy.");
        Check(DataImport.Animals(numericWorkbook, "Pool", 1, 2, 2, new(1, 2), allowIntegerIds: true).Value!.Animals[0].AnimalId == "12", "Explicit integer numeric identity import.");
        var missing = DataImport.Read(Workbook(header + "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>1</t></is></c><c r=\"B3\"><v>100</v></c></row>"), "gap.xlsx");
        Check(DataImport.Animals(missing, "Pool", 1, 2, 3, new(1, 2)).Issues.Single().Row == 2, "Missing worksheet rows do not vanish from chosen range.");
        Reject(() => DataImport.Read(Workbook(header, relationship: "https://evil.invalid/sheet.xml"), "bad.xlsx"), "External worksheet target rejected.");
        Reject(() => DataImport.Read(Workbook(header, duplicateEntry: true), "bad.xlsx"), "Duplicate ZIP entry rejected.");
        Reject(() => DataImport.Read(Workbook("<row r=\"2\"><c r=\"A3\"><v>1</v></c></row>"), "bad.xlsx"), "Mismatched cell row rejected.");
        Reject(() => DataImport.Read(Workbook("<row r=\"2\"><c r=\"A2\"><v>1</v></c><c r=\"A2\"><v>2</v></c></row>"), "bad.xlsx"), "Duplicate worksheet cell rejected.");
        Reject(() => DataImport.Read([1, 2, 3], "bad.xls"), "Legacy XLS requires conversion.");

        var animals = Enumerable.Range(1, 4).Select(x => new Animal($"record-{x}", $"{x:D3}", 100m)).ToImmutableArray();
        var study = new Study(Design.Rcbd, animals, animals.Select(x => x.RecordId).ToImmutableArray(), trt.Value!.Treatments, 2, 1);
        var session = ProjectSession.Create("Persistence fixture", ProjectInputs.FromStudy(study, "Researcher supplied exact pool"), "Initial study");
        Check(session.IsReady && session.Document.ActiveResultId is null, "Configured initial project.");
        session.Run(13, "Initial allocation"); string initialId = session.Document.ActiveResultId!;
        var chosen = session.Choose(initialId);
        var first = chosen.Allocation.Positions[0]; var second = chosen.Allocation.Positions.First(x => x.Block != first.Block);
        session.Swap(first.AnimalRecordId, second.AnimalRecordId, "Cross-block researcher correction");
        session.MapPens(Enumerable.Range(1, 4).Select(x => new PhysicalPen($"P{x}", $"Room A/Pen {x:D3}", 1, (x - 1) / 2 + 1)).ToImmutableArray(), 99, "Map declared block pools");
        session.Swap(first.AnimalRecordId, second.AnimalRecordId, "Reverse previous swap");
        string editedId = session.Document.ActiveResultId!; var edited = session.Choose(editedId);
        Check(edited.Allocation.Edits.Length == 2 && edited.Allocation.Revision == 4 && edited.PhysicalPens.Length == 4, "Ordered undo and physical pool remain in revision history.");
        Check(chosen.Allocation.Edits.IsEmpty && chosen.Allocation.Locations.IsEmpty, "Previously chosen immutable result stays unchanged.");
        session.Activate(initialId, "Review original revision");
        Check(session.Document.ActiveResultId == initialId && session.Choose(editedId).Allocation.Revision == 4, "Chosen revision change retains later edits.");
        session.Activate(editedId, "Return to mapped revision");
        using (var cancel = new CancellationTokenSource())
        { cancel.Cancel(); Check(session.Run(12, "Cancelled retry", cancel.Token).Status == RunStatus.Cancelled, "Cancelled allocation recorded."); }
        Check(session.Document.ActiveResultId == editedId, "Cancellation retains completed result.");
        string before = Json(session.Document);
        Reject(() => session.Swap(first.AnimalRecordId, first.AnimalRecordId, "bad swap"), "Rejected swap.");
        Reject(() => session.MapPens([], 0, "bad pool"), "Rejected physical pool.");
        Check(Json(session.Document) == before, "Rejected edit/map leaves history and active result unchanged.");
        Reject(() => session.Run(22, "Callback mutation attempt", progress: (_, _) => session.Activate(initialId, "Reentrant change")),
            "Reentrant progress callback cannot mutate the project.");
        Check(Json(session.Document) == before, "Callback exception preserves completed project and resets run guard.");

        session.ImportAnimals(source, imported, false, "Replace pool with traced source");
        Check(!session.IsReady && session.IsStale && session.Document.ActiveResultId == editedId, "Import never selects a subset and keeps prior result.");
        before = Json(session.Document);
        Reject(() => session.SelectExactPool("Use all"), "Oversized exact-pool selection rejected.");
        Reject(() => session.ImportAnimals(source, imported, true, "Duplicate append"), "Duplicate appended record and actual identities rejected.");
        Reject(() => session.ImportAnimals(source, imported with { Animals = imported.Animals.SetItem(0, imported.Animals[0] with { WeightGrams = 999 }) }, false, "Forged batch"), "Batch must agree with original source.");
        Check(Json(session.Document) == before, "Failed import/selection atomic.");
        Check(session.Run(55, "Pending selection attempt").Status == RunStatus.Invalid && session.Document.ActiveResultId == editedId, "Pending selection recorded as invalid without replacing result.");
        var decisions = imported.Animals.Select((a, i) => new SelectionDecision(a.RecordId, i < 4 ? SelectionChoice.Include : SelectionChoice.Exclude,
            i < 4 ? "Researcher included" : "Researcher excluded for study capacity")).ToImmutableArray();
        session.Select(decisions, "Explicit chosen four-record subset");
        Check(session.IsReady && session.CurrentInput.Inputs.Study.SelectedRecordIds.Length == 4, "Researcher-selected subset is exact.");
        Reject(() => session.Swap(first.AnimalRecordId, second.AnimalRecordId, "stale edit"), "Historical result cannot be edited against different full-input revision.");
        session.ImportTreatments(treatmentSource, trt.Value!, "Preserve factor source columns");
        Check(session.CurrentInput.Inputs.Treatments[1].FactorColumns.Length == 2, "Imported factor metadata retained.");
        before = Json(session.Document);
        var sourceDetails = session.CurrentInput.Inputs.Animals;
        Reject(() => session.Revise(session.CurrentInput.Inputs with
        {
            Animals = sourceDetails.SetItem(0, sourceDetails[0] with
            {
                Origin = sourceDetails[0].Origin! with { Row = 3 }
            })
        }, "Change source-row binding"), "Existing animal source-row binding cannot change.");
        Check(Json(session.Document) == before, "Rejected provenance change leaves history unchanged.");
        session.Run(56, "Allocate explicit subset");
        string importedResultId = session.Document.ActiveResultId!;
        var importedChosen = session.Choose(importedResultId);
        Check(importedChosen.Allocation.Positions.All(x => x.AnimalRecordId != imported.Animals[4].RecordId), "Excluded source record stays unallocated.");
        var current = session.CurrentInput.Inputs;
        var amended = current with
        {
            Study = current.Study with
            {
                Pool = current.Study.Pool.SetItem(4,
            current.Study.Pool[4] with { WeightGrams = 200, OldLocation = "Changed location" })
            },
            Notes = "Full source revision changed"
        };
        session.Revise(amended, "Update unselected weight and descriptive location");
        Check(AllocationEngine.Fingerprint(amended.Study) == importedChosen.Allocation.InputFingerprint && session.CurrentInput.FullInputDigest != importedChosen.Input.FullInputDigest,
            "Full project digest distinguishes source changes outside allocation fingerprint.");
        Check(!session.IsStale && session.HasEarlierInputRevision, "Distinguish scientific staleness from earlier complete source provenance.");
        Check(session.Choose(importedResultId).Input.Inputs.Study.Pool[4].WeightGrams == 140m, "Historical source pool is immutable.");

        string output = Path.Combine(root, "analysis", "project-validation"); Directory.CreateDirectory(output);
        // Unique temporary path keeps repeated test runs independent and preserves demonstration artifacts.
        string work = Path.Combine(output, "test-" + Guid.NewGuid().ToString("N") + ".poultryproj");
        string hash = ProjectStore.Save(session.Document, work);
        string modernPath = Path.ChangeExtension(work, ProjectStore.ProjectExtension);
        string modernHash = ProjectStore.Save(session.Document, modernPath);
        var modern = ProjectStore.Open(modernPath);
        Check(modernHash == hash && modern.Document.Id == session.Document.Id && modern.Document.ActiveResultId == session.Document.ActiveResultId,
            "Species-neutral project extension retains the exact legacy payload, project identity and chosen allocation.");
        ProjectStore.Save(modern.Document, modernPath, expectedFileDigest: modernHash);
        Check(ProjectStore.LoadBackup(modernPath).Id == modern.Document.Id, "New project extension retains validated backup recovery.");
        var loaded = ProjectStore.Open(work);
        Check(Json(loaded.Document) == Json(session.Document), "Complete source/input/failed-attempt/edit history round trip.");
        Check(Json(loaded.Choose(editedId)) == Json(edited), "Historical mapped edit revision joins original input and physical pool.");
        Check(Json(ResultViews.Movement(loaded.Choose(importedResultId).Input.Inputs.Study, loaded.Choose(importedResultId).Allocation))
            == Json(ResultViews.Movement(importedChosen.Input.Inputs.Study, importedChosen.Allocation)), "Loaded chosen-revision output agrees with original snapshot.");
        loaded.Run(57, "New seeded revision after recovery");
        string newerHash = ProjectStore.Save(loaded.Document, work, expectedFileDigest: hash);
        var opened = ProjectStore.OpenWithDigest(work);
        Check(opened.FileDigest == newerHash && Json(opened.Session.Document) == Json(loaded.Document), "Open returns concurrency digest of the same validated file snapshot.");
        Check(Json(ProjectStore.LoadBackup(work)) == Json(session.Document), "Backup is the preceding fully recoverable project.");
        string backupHash = ProjectStore.FileDigest(work + ".bak");
        Reject(() => ProjectStore.Save(session.Document, work, expectedFileDigest: hash), "Outdated save digest detects changed disk state.");
        Check(ProjectStore.FileDigest(work) == newerHash && ProjectStore.FileDigest(work + ".bak") == backupHash, "Conflicting save leaves target and backup unchanged.");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); bool cancelled = false;
            try { ProjectStore.Save(loaded.Document, work, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && ProjectStore.FileDigest(work) == newerHash, "Cancelled save preserves target.");
        }
        using (var locked = new FileStream(work, FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => ProjectStore.Save(loaded.Document, work, expectedFileDigest: newerHash), "Replacement failure retains old file.");
        Check(ProjectStore.FileDigest(work) == newerHash && ProjectStore.FileDigest(work + ".bak") == backupHash
            && !Directory.EnumerateFiles(output, Path.GetFileName(work) + ".*.tmp").Any(), "Atomic replace failure preserves backup and cleans its temporary file.");
        Reject(() => ProjectStore.Save(loaded.Document, Path.Combine(output, "source.xlsm")), "Project save cannot overwrite workbook.");
        var other = ProjectSession.Create("Other study", ProjectInputs.FromStudy(study, "Selected"), "Initial");
        Reject(() => ProjectStore.Save(other.Document, work), "Cannot overwrite different project identity.");

        string corrupt = Path.Combine(output, "malformed-test.poultryproj");
        void CorruptDocument(ProjectDocument doc, string message)
        {
            File.WriteAllText(corrupt, Json(new { Format = "PoultryDesigner.Project", SchemaVersion = 1, PayloadSha256 = Digest(doc), Project = doc }));
            Reject(() => ProjectStore.Load(corrupt), message);
        }
        var document = loaded.Document;
        CorruptDocument(document with { SchemaVersion = 2 }, "Future payload schema rejected.");
        CorruptDocument(document with { ActiveResultId = initialId }, "Active selection cannot contradict history.");
        CorruptDocument(document with { CurrentInputId = document.Inputs[0].Id }, "Missing current input lineage rejected.");
        CorruptDocument(document with { Sources = document.Sources.Add(document.Sources[0]) }, "Duplicate source ID rejected.");
        CorruptDocument(document with { Sources = document.Sources.SetItem(0, document.Sources[0] with { ContentBase64 = "AAAA" }) }, "Source bytes/hash corruption rejected independently of payload hash.");
        CorruptDocument(document with
        {
            Sources = document.Sources.SetItem(0, document.Sources[0] with
            {
                Tables = document.Sources[0].Tables.SetItem(0, document.Sources[0].Tables[0] with { Name = "Changed" })
            })
        }, "Parsed source table cannot diverge from original bytes.");
        CorruptDocument(document with { Inputs = document.Inputs.SetItem(0, document.Inputs[0] with { FullInputDigest = "bad" }) }, "Full input digest corruption rejected.");
        int eventIndex = Array.FindIndex(document.Events.ToArray(), x => x.Result is not null);
        var ev = document.Events[eventIndex];
        void BadResult(Allocation result, string message) => CorruptDocument(document with { Events = document.Events.SetItem(eventIndex, ev with { Result = result }) }, message);
        BadResult(ev.Result! with { EngineVersion = "future-version" }, "Unsupported engine cannot silently replay with current engine.");
        BadResult(ev.Result! with { Seed = 999 }, "Allocation seed must agree with event seed.");
        BadResult(ev.Result! with { Positions = ev.Result!.Positions.SetItem(0, ev.Result.Positions[0] with { OriginalBlock = 99 }) }, "Corrupt original block provenance rejected by replay.");
        BadResult(ev.Result! with { Pens = ev.Result!.Pens.SetItem(0, ev.Result.Pens[0] with { MeanWeightGrams = 999 }) }, "Corrupt cached statistics rejected.");
        int swapIndex = Array.FindIndex(document.Events.ToArray(), x => x.Action == ProjectAction.Swap);
        var swap = document.Events[swapIndex];
        CorruptDocument(document with { Events = document.Events.SetItem(swapIndex, swap with { ParentResultId = "missing" }) }, "Missing edit parent rejected.");
        CorruptDocument(document with { Events = document.Events.SetItem(swapIndex, swap with { Result = swap.Result! with { Edits = [] } }) }, "Ordered edit log required for replay.");
        int mapIndex = Array.FindIndex(document.Events.ToArray(), x => x.Action == ProjectAction.MapPens);
        var map = document.Events[mapIndex];
        CorruptDocument(document with { Events = document.Events.SetItem(mapIndex, map with { PhysicalPens = [] }) }, "Physical pool provenance required to replay mapping.");
        int lastInput = document.Inputs.Length - 1;
        var inputRev = document.Inputs[lastInput];
        var badInputs = inputRev.Inputs with { Selection = inputRev.Inputs.Selection.SetItem(0, inputRev.Inputs.Selection[0] with { Reason = "" }) };
        CorruptDocument(document with { Inputs = document.Inputs.SetItem(lastInput, inputRev with { Inputs = badInputs, FullInputDigest = Digest(badInputs) }) }, "Invalid selection ledger rejected even with recomputed digests.");
        File.WriteAllText(corrupt, "{\"Format\":\"PoultryDesigner.Project\",\"Format\":\"PoultryDesigner.Project\"}");
        Reject(() => ProjectStore.Load(corrupt), "Duplicate JSON property rejected.");
        File.WriteAllText(corrupt, "{ incomplete"); Reject(() => ProjectStore.Load(corrupt), "Truncated JSON rejected.");
        var tree = JsonNode.Parse(File.ReadAllText(work))!;
        tree["SchemaVersion"] = 99; File.WriteAllText(corrupt, tree.ToJsonString()); Reject(() => ProjectStore.Load(corrupt), "Future envelope schema rejected.");
        tree = JsonNode.Parse(File.ReadAllText(work))!; tree["unexpected"] = 1;
        File.WriteAllText(corrupt, tree.ToJsonString()); Reject(() => ProjectStore.Load(corrupt), "Unknown JSON field rejected.");
        tree = JsonNode.Parse(File.ReadAllText(work))!; tree["Project"]!["Name"] = "Changed without hash";
        File.WriteAllText(corrupt, tree.ToJsonString()); Reject(() => ProjectStore.Load(corrupt), "Payload checksum detects accidental edit.");
        tree = JsonNode.Parse(File.ReadAllText(work))!; tree["Project"]!.AsObject().Remove("Events");
        File.WriteAllText(corrupt, tree.ToJsonString()); Reject(() => ProjectStore.Load(corrupt), "Missing required JSON member rejected.");
        tree = JsonNode.Parse(File.ReadAllText(work))!; tree["Project"]!["Inputs"]![0]!["Inputs"]!["Study"]!.AsObject().Remove("CvLimitPercent");
        File.WriteAllText(corrupt, tree.ToJsonString()); Reject(() => ProjectStore.Load(corrupt), "Missing persisted optional engine setting cannot silently take a constructor default.");
        before = Json(loaded.Document);
        Reject(() => ProjectStore.Open(corrupt), "Failed open rejects candidate.");
        Check(Json(loaded.Document) == before, "Failed load never mutates existing open project.");
        // Ready, incomplete and exhausted projects all preserve the same lifecycle after reopen.
        var incompleteStudy = new Study(Design.Rcbd, [], [], [], 0, 0);
        var incomplete = ProjectSession.Create("Setup incomplete", ProjectInputs.FromStudy(incompleteStudy, "Initial"), "Initial setup");
        Check(!incomplete.IsReady && incomplete.Run(0, "Attempt before setup").Status == RunStatus.Invalid, "Saveable incomplete setup remains unready.");
        string incompletePath = Path.Combine(output, "incomplete-test.poultryproj");
        ProjectStore.Save(incomplete.Document, incompletePath);
        Check(Json(ProjectStore.Open(incompletePath).Document) == Json(incomplete.Document), "Incomplete setup and invalid attempt round trip.");
        Reject(() => incomplete.Revise(incomplete.CurrentInput.Inputs with { Study = incompleteStudy with { Replicates = -1 } }, "bad count"), "Negative draft counts rejected.");
        Reject(() => incomplete.Revise(incomplete.CurrentInput.Inputs with { Study = incompleteStudy with { Pool = default } }, "bad pool"), "Uninitialized input arrays rejected.");
        var filtered = ProjectSession.Create("Exhaustion fixture", ProjectInputs.FromStudy(study, "Chosen"), "Initial");
        filtered.Run(4, "Previous completed result"); string retained = filtered.Document.ActiveResultId!;
        filtered.Revise(filtered.CurrentInput.Inputs with
        {
            Study = study with
            {
                FilterBalance = true,
                CvLimitPercent = 0,
                MaximumAttemptsPerBlock = 1,
                Pool = study.Pool.SetItem(0, study.Pool[0] with { WeightGrams = 101 })
            }
        }, "Strict zero allowance");
        Check(filtered.Run(5, "Exhaust one attempt").Status == RunStatus.Exhausted && filtered.Document.ActiveResultId == retained,
            "Exhausted run retains active completed revision and attempt diagnostics.");
        string filteredPath = Path.Combine(output, "exhausted-test.poultryproj");
        ProjectStore.Save(filtered.Document, filteredPath);
        var filteredLoaded = ProjectStore.Open(filteredPath);
        Check(Json(filteredLoaded.Document) == Json(filtered.Document) && filteredLoaded.IsStale, "Exhausted run diagnostics replay and old scientific result stays stale after reopening.");
        using (var during = new CancellationTokenSource())
            Check(filteredLoaded.Run(6, "Cancel after attempted block", during.Token, (_, _) => during.Cancel()).Status == RunStatus.Cancelled,
                "In-progress cancellation is recorded and retains active result.");
        ProjectStore.Save(filteredLoaded.Document, filteredPath);
        Check(ProjectStore.Open(filteredPath).Document.ActiveResultId == retained, "Cancelled during-attempt state round trip.");
        // Load bounds and several independent import resources are enforced before allocating.
        Reject(() => DataImport.Read(new byte[DataImport.MaximumSourceBytes + 1], "oversized.csv"), "Source byte limit enforced.");
        Reject(() => DataImport.Animals(source, "Delimited", 1, 2, 100_002, new(1, 2)), "Import row range limit enforced.");
        var bigCsv = new StringBuilder("ID,BW\n");
        for (int i = 1; i <= 100_000; i++) bigCsv.Append(i.ToString("D6", CultureInfo.InvariantCulture)).Append(",100\n");
        var bigSource = Csv(bigCsv.ToString());
        var bigImport = DataImport.Animals(bigSource, "Delimited", 1, 2, 100_001, new(1, 2));
        Check(bigImport.Success && bigImport.Value!.Animals.Length == 100_000 && bigImport.Value.Animals[^1].AnimalId == "100000",
            "100,000-animal import does not truncate or merge records.");
        File.WriteAllText(work, "crash/truncated target");
        Check(Json(ProjectStore.LoadBackup(work)) == Json(session.Document), "Explicit backup recovery works with unreadable main file.");

        // Read the preserved legacy source only; no researcher selection is inferred.
        string? legacy = Directory.EnumerateFiles(root, "*.xlsm").SingleOrDefault();
        string originalHash = "not supplied; optional private-workbook checks skipped";
        if (legacy is not null)
        {
            originalHash = ProjectStore.FileDigest(legacy);
            var legacySource = DataImport.Read(legacy);
            var originalPreview = DataImport.Animals(legacySource, "Animal Pool", 1, 2, 901, new(2, 3, OldLocation: 1, MaternalId: 5, PaternalId: 6),
                demonstration: true, allowIntegerIds: true);
            Check(originalPreview.Success && originalPreview.Value!.Animals.Length == 900, "Legacy demonstration pool imports all 900 with explicit numeric-ID policy.");
            Check(!DataImport.Animals(legacySource, "Animal Pool", 1, 2, 901, new(2, 3), allowIntegerIds: true).Success, "Real-study duplicate rules reject demonstration repeated tags.");
            var legacyStudy = new Study(Design.Rcbd, [], [], Enumerable.Range(1, 6).Select(x => new Treatment($"T{x}", $"Treatment {x}")).ToImmutableArray(),
                12, 12, true, 0.5m, 30, true);
            var demo = ProjectSession.Create("Legacy demonstration import — selection pending", ProjectInputs.FromStudy(legacyStudy, "Initial draft"), "Set declared dimensions");
            demo.ImportAnimals(legacySource, originalPreview.Value!, false, "Import all demonstration source records");
            Check(demo.CurrentInput.Inputs.Study.Pool.Length == 900 && demo.CurrentInput.Inputs.Study.SelectedRecordIds.IsEmpty && !demo.IsReady,
                "900-row import remains pending explicit 864-animal researcher selection.");
            Check(demo.CurrentInput.Inputs.Study.CvLimitPercent == 0.5m && demo.CurrentInput.Inputs.Study.Demonstration,
                "Legacy study dimensions/grams/allowance and demonstration marking preserved.");
            Check(ProjectStore.FileDigest(legacy) == originalHash && originalHash == "3A8B964C0877643BB1B94EA952B1AA055A87F2E0DADB650D74337460A57B1686", "Original workbook unchanged.");
        }
        else Console.WriteLine("SKIP: optional private-workbook integration checks; synthetic import checks still run.");
        // Save a durable review fixture to a fresh name because test project IDs change between runs.
        string fixture = Path.Combine(output, "section4-fixture-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".poultryproj");
        ProjectStore.Save(session.Document, fixture);
        File.WriteAllText(Path.Combine(output, "latest-fixture.txt"), fixture + Environment.NewLine);
        File.WriteAllText(Path.Combine(output, "check-results.txt"), $"Section 4: {checks} assertions passed. Original workbook SHA-256: {originalHash}." + Environment.NewLine);
        // Only remove files generated by this test invocation, after all recovery checks finish.
        foreach (string file in new[] { work, work + ".bak", corrupt, incompletePath, filteredPath, filteredPath + ".bak" }) File.Delete(file);
        Console.WriteLine($"PASS: Section 4 imports, full provenance, project replay, schemas, corruption, atomic saves/recovery ({checks} assertions).");
        return checks;
    }
}
