using System.Data;
using PoultryDesigner.Engine;
using PoultryDesigner.AppCore;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    internal async Task ExerciseBalanceNoticeForSmoke(string fixture, Action<bool, string> check, Action<Form, string> render)
    {
        await Workspace.OpenAsync(fixture, discardChanges: true);
        var original = Workspace.Inputs;
        Workspace.Revise(original with { Study = original.Study with { Design = Design.Crd, FilterBalance = false }, PreparedLayout = null }, "ANOVA screen regression");
        setupDirty = selectionDirty = false; Reload(); SetStage(2); firstHousing.Value = 1;
        lastHousing.Value = Workspace.Inputs.Study.Treatments.Length * Workspace.Inputs.Study.Replicates;
        var study = Workspace.Inputs.Study;
        ulong lowSeed = Enumerable.Range(1, 50).Select(i => (ulong)i).First(candidate =>
        {
            var result = AllocationEngine.Run(study, candidate).Allocation!;
            return BodyWeightAnova.Calculate(study, result.Pens).PValue is <= .6;
        });
        seed.Text = lowSeed.ToString(); await RunAsync();
        var actual = BodyWeightAnova.Calculate(chosen!.Input.Inputs.Study, chosen.Allocation.Pens);
        check(actual.PValue is <= .6 && balanceNoticePanel.Visible && balanceNotice.Text.Contains("Repeat with a different seed") && repeatAllocation.Visible && repeatAllocation.Enabled,
            "Completed low-p allocation displays an actionable repeat warning before export");
        render(this, "allocation-balance-repeat");
        check(repeatAllocation.Right <= balanceNoticePanel.ClientSize.Width && balanceNoticePanel.Right <= balanceNoticePanel.Parent!.ClientSize.Width,
            "Repeat action stays entirely inside the window's responsive balance notice");
        var tabs = allocationViews.Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 2;
        check(balanceNoticePanel.Visible && repeatAllocation.Visible, "ANOVA warning remains visible on the unit BW tab");
        render(this, "allocation-balance-unit-bw");
        var previousResult = chosen.ResultId; var housing = chosen.Allocation.Locations;
        await RepeatAllocationAsync();
        check(chosen!.ResultId != previousResult && chosen.Allocation.Seed == lowSeed + 1 && chosen.Allocation.Locations.SequenceEqual(housing)
            && Workspace.Document.Events.Any(e => e.Id == previousResult), "Repeat action records the next seed, retains the prior result and preserves prepared housing");
        var edge = actual with { PValue = .6 };
        ShowBalanceNotice(edge); check(repeatAllocation.Visible, "Exactly p=0.6 retains the accepted strictly-greater target");
        ShowBalanceNotice(edge with { PValue = .61 }); check(!repeatAllocation.Visible && balanceNotice.Text.Contains("Meets your balance target"), "p=0.61 displays a met target without a repeat action");
        ShowBalanceNotice(edge with { PValue = null }); check(!repeatAllocation.Visible && balanceNotice.Text.Contains("not estimable"), "Undefined p-value requests review rather than inventing a pass");
        await Workspace.OpenAsync(fixture, discardChanges: true); setupDirty = selectionDirty = false; Reload(); SetStage(0);
    }

    internal async Task ExerciseReviewForSmoke(string fixture, Action<bool, string> check, Action<Form, string> render)
    {
        check(PresentationState.SaveState(false, false, false, null) == "Not saved to file", "Fresh project is never presented as saved");
        check(PresentationState.SaveState(false, false, true, fixture) == "Unsaved changes", "Draft changes remain visibly unsaved");
        check(PresentationState.SaveState(true, false, true, fixture) == "Saving…", "Saving state is explicit");
        check(PresentationState.SaveState(false, true, false, fixture) == "Save failed", "Save failure cannot appear saved");
        check(PresentationState.SaveState(false, false, false, fixture) == "Saved", "Persisted clean project has saved status");
        foreach (int dpi in new[] { 96, 120, 144, 192 }) check(Theme.Pixels(32, dpi) == dpi / 3, "Minimum interaction height follows Windows DPI " + dpi);
        double boundary = 0.5; for (int i = 0; i < 8; i++) boundary = Math.BitIncrement(boundary);
        check(!PresentationState.CvExceeds(boundary, 0.5m) && PresentationState.CvExceeds(Math.BitIncrement(boundary), 0.5m), "CV warning respects the existing scientific boundary tolerance");
        var before = Workspace.Document;
        SetStage(0); perPen.Value = 0;
        check(fieldErrors.GetError(perPen).Length > 0 && pageAlert.Text.Contains("At least one animal"), "Invalid unit size has field and page validation");
        render(this, "study-invalid");
        setupDirty = false; Reload();
        treatments.ClearSelection(); treatments.Rows[0].Selected = true; MoveTreatment(1);
        check(Convert.ToString(treatmentTable.Rows[0]["Treatment ID"]) == "T02" && ReferenceEquals(before, Workspace.Document), "Explicit reorder is staged without changing stored treatment order");
        check(!generate.Enabled, "Unapplied treatment order blocks generation");
        var originalTreatments = Workspace.Inputs.Treatments;
        setupReason.Clear(); ApplySetup();
        check(Workspace.Document.Inputs.Last().Reason == "Apply study settings", "Routine settings retain a truthful action label without requesting a reason");
        check(Workspace.Inputs.Study.Treatments[0].Id == "T02" && Workspace.Inputs.Treatments.All(t => originalTreatments.Contains(t)), "Applied reorder retains treatment metadata by immutable ID");
        check(chosen!.Input.Inputs.Study.Treatments[0].Id == "T01", "Earlier result keeps its original treatment order");
        await Workspace.OpenAsync(fixture, discardChanges: true); Reload(); before = Workspace.Document;
        setupDirty = false; Reload();
        SetStage(1); search.Text = Workspace.Inputs.Study.Pool[0].RecordId;
        check(!animals.Columns["Record ID"]!.Visible && !animals.Columns["Source row"]!.Visible && !animals.Columns["Reason"]!.Visible, "Technical animal columns are hidden in the everyday view");
        animalDetails.Checked = true;
        check(animals.Columns["Record ID"]!.Visible && animals.Columns["Reason"]!.Visible, "Show details preserves access to full record and audit information");
        animalDetails.Checked = false;
        check(animals.Rows.Count == 1, "Record ID search retains one exact source binding");
        animals.ClearSelection(); animals.Rows[0].Cells["Mark"].Value = true; CaptureSelection();
        check(!selectionDirty && ReferenceEquals(before, Workspace.Document), "Highlight checkbox never changes scientific selection or dirty state");
        search.Text = "no matching synthetic observation";
        check(animalNoResults.Visible && filteredAnimals.Length == 0 && checkedAnimals.Count == 1, "No-results state keeps highlighted identity without changing decisions");
        render(this, "animals-no-results");
        ClearAnimalFilters(); checkedAnimals.Clear(); RenderAnimals();
        var decisions = selection.Values.ToArray();
        var excluded = decisions.Select((x, i) => i == 0 ? x with { Choice = SelectionChoice.Exclude } : x);
        check(PresentationState.SelectionMessage(Workspace.Inputs.Study, excluded).Contains("47 selected"), "Exact selection blocker reports current count");
        check(PresentationState.SelectionMessage(Workspace.Inputs.Study, decisions.Select(x => x with { Reason = "" })).Contains("48 decisions need an audit reason"), "Missing-reason blocker remains explicit at the exact count");
        SetStage(2);
        var tabs = allocationViews.Controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 2;
        string historical = Workspace.Document.Events.First(e => e.Result is not null).Id;
        revisions.SelectedItem = revisions.Items.Cast<RevisionOption>().Single(x => x.Id == historical);
        Reload();
        check(chosen?.ResultId == historical && tabs.SelectedIndex == 2, "Reload preserves the chosen historical revision and native tab");
        check(resultLabel.Text.Contains("Historical"), "Historical result is labeled explicitly");
        check(!previewSwap.Enabled && !mapUnits.Enabled && activateRevision.Enabled, "Historical review exposes activation and blocks current-input edits");
        string active = Workspace.Document.ActiveResultId!;
        var inputs = Workspace.Inputs;
        Workspace.Revise(inputs with { Study = inputs.Study with { FilterBalance = true, CvLimitPercent = 0, MaximumAttemptsPerBlock = 1 } }, "Smoke: deliberately unattainable CV");
        var failure = await Workspace.RunAsync(99, "Smoke: failed allocation retention");
        check(failure.Status == RunStatus.Exhausted && Workspace.Document.ActiveResultId == active, "Real exhausted run retains the previous completed result");
        lastRunOutcome = failure; lastRunProject = Workspace.Document.Id; Reload(); SetStage(2);
        check(pageAlert.Text.Contains("Exhausted") && chosen is not null && exportReport.Enabled, "Failed run remains explicit while prior output is reviewable/exportable");
        render(this, "allocation-failed");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = await Workspace.RunAsync(99, "Smoke: cancelled allocation", cancellation.Token);
        check(cancelled.Status == RunStatus.Cancelled && Workspace.Document.ActiveResultId == active, "Real cancelled run retains the previous completed result");
        lastRunOutcome = cancelled; RefreshPresentation();
        check(pageAlert.Text.Contains("Cancelled"), "Cancellation cannot be shown as successful allocation");
        render(this, "allocation-cancelled");
        Workspace.Revise(inputs, "Smoke: restore original inputs");
        var success = await Workspace.RunAsync(850, "Smoke: new active revision"); Reload();
        check(success.Status == RunStatus.Success && chosen?.ResultId == Workspace.Document.ActiveResultId && chosen?.ResultId != historical, "New completed allocation becomes chosen rather than retaining historical view");
        Workspace.Revise(inputs with { Study = inputs.Study with { Design = Design.Crd } }, "Smoke: CRD metric applicability");
        var crd = await Workspace.RunAsync(851, "Smoke: CRD one-pass output"); Reload();
        check(crd.Status == RunStatus.Success && crd.Allocation!.Blocks.IsEmpty && qualityStats.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).Count(c => c.Text == "Not applicable") == 1 && Components.Help.GetToolTip(qualityStats)?.Contains("Not applicable") == true,
            "CRD presents block CV and block attempt metrics as not applicable instead of inventing zeroes");
        firstHousing.Value = 101; lastHousing.Value = 113;
        check(!generate.Enabled && housingCount.Text.Contains("required"), "Oversized housing range blocks allocation");
        lastHousing.Value = 112; seed.Text = "25"; PrepareLayout();
        var prepared = Workspace.Inputs.PreparedLayout!;
        check(prepared.First == 101 && prepared.Last == 112 && prepared.Locations.Length == 12 && printLayout.Enabled,
            "Prepare layout saves treatment housing assignments and enables printing");
        seed.Text = "852"; await RunAsync();
        check(Workspace.Document.Events.Last(e => e.Action == ProjectAction.Run).Reason == "Generate allocation" && Workspace.Document.Events.Last().Result?.Seed == 852 && seed.Text == "852" && chosen!.Allocation.Locations.Length == chosen.Allocation.Pens.Length,
            "Generate records its actual action and explicit seed without a run-reason field");
        check(chosen!.Allocation.Locations.SequenceEqual(prepared.Locations) && chosen.Allocation.LocationSeed == 25,
            "Actual Generate button preserves prepared housing while using a different animal seed");
        using (var reports = new ReportDialog(PoultryDesigner.AppCore.LayoutReports.Create(Workspace.Document.Name, prepared), []))
        {
            check(reports.ReportForSmoke.Tables.Single(t => t.Name == "Allotment sheets").Rows.All(r => Equals(r[5], "") && Equals(r[6], "")), "Preparation report keeps animal IDs and BW blank");
            render(reports, "prepared-layout-report");
            var choice = reports.ChoiceForSmoke;
            choice.SelectedIndex = 3;
            check(reports.ReportForSmoke.Tables.Length == 1 && reports.ReportForSmoke.Tables[0].Name == "Housing labels", "Labels can be printed separately from blank sheets");
            using var printing = new ReportPrinting(reports.ReportForSmoke);
            check(printing.Pages.SelectMany(p => p.Rows).Count(r => !r.Header && !r.Continuation) == 12, "Native label printing includes every prepared housing unit");
            using var labelPage = printing.Render(0); labelPage.Save(Path.Combine(Path.GetDirectoryName(fixture)!, "prepared-labels-page.png"));
        }
        foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            using var reports = new ReportDialog(PoultryDesigner.AppCore.LayoutReports.Create(Workspace.Document.Name, prepared), []);
            reports.Scale(new SizeF(scale, scale));
            render(reports, $"prepared-report-scaled-{scale:0.##}");
            var heading = reports.HeadingForSmoke;
            check(heading.Height >= heading.GetPreferredSize(new Size(heading.Width, 0)).Height,
                "Prepared layout heading has its full measured height at scale " + scale);
        }
        checkedAnimals.Add("record absent from opened project"); lastRunOutcome = null; selectionDirty = setupDirty = false;
        bool discardGuard = false;
        try { await Workspace.OpenAsync(fixture); } catch (InvalidOperationException) { discardGuard = true; }
        check(discardGuard, "Open protects unsaved allocation history from implicit discard");
        await Workspace.OpenAsync(fixture, discardChanges: true); Reload();
        check(checkedAnimals.Count == 0, "Opening a project removes obsolete highlight IDs");
        using var paged = new PagedGrid(); using var host = new Form { ClientSize = new(800, 500), ShowInTaskbar = false, Location = new(-25000, -25000), StartPosition = FormStartPosition.Manual };
        host.Controls.Add(paged); host.Show();
        paged.SetRows(["Record ID", "BW"], 1201, i => [$"record-{i}", i]); Application.DoEvents();
        Button Page(string text) => paged.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(x => x.Text == text);
        Page("2").PerformClick(); paged.Grid.ClearSelection(); paged.Grid.Rows[0].Selected = true;
        check(paged.SelectedIndices.SequenceEqual(new[] { 500 }) && Convert.ToString(paged.Grid.Rows[0].Cells[0].Value) == "record-500", "Paged selected indices retain immutable source-row offsets");
        paged.SetRows(["Record ID", "BW"], 1201, i => [$"record-{i}", i]);
        check(Convert.ToString(paged.Grid.Rows[0].Cells[0].Value) == "record-500", "Read-only table refresh preserves its page");
        var size = paged.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<ComboBox>().Single(); size.SelectedItem = "250";
        Page("3").PerformClick(); paged.Grid.ClearSelection(); paged.Grid.Rows[0].Selected = true;
        check(paged.SelectedIndices.SequenceEqual(new[] { 500 }) && paged.Grid.Rows.Count == 250, "Rows-per-page changes preserve correct absolute record offsets");
        host.Close();
        Workspace.New("Pre-allocation preparation", discardChanges: true);
        var emptyStudy = inputs.Study with { Pool = [], SelectedRecordIds = [] };
        Workspace.Revise(ProjectInputs.FromStudy(emptyStudy, "Preparation"), "Prepare study settings");
        Reload(); SetStage(2); firstHousing.Value = 101; seed.Text = "25"; PrepareLayout();
        check(Workspace.Inputs.Study.Pool.IsEmpty && Workspace.Document.Events.IsEmpty && !generate.Enabled && printLayout.Enabled && preparedGrid.Grid.Rows.Count == 12,
            "Actual preparation UI works before animals are entered, without creating a completed allocation");
        render(this, "prepared-layout-no-animals");
        using (var entry = new AnimalEntryDialog())
        {
            var source = entry.ExerciseForSmoke(); var batch = DataImport.Animals(source, "Delimited", 1, 2, 3, new(1, 2, Cohort: 3, OldLocation: 4));
            check(batch.Value is not null && batch.Value.Animals[0].AnimalId == "000123" && batch.Value.Animals[1].AnimalId == "A,002" && batch.Value.Animals[0].WeightGrams == 125.5m,
                "Direct entry retains text IDs, quoted delimiters, grams and reviewable source provenance");
            var plan = Workspace.Inputs.PreparedLayout!; Workspace.ImportAnimals(source, batch.Value!, false, "Enter reviewed animals"); Reload();
            check(Workspace.Inputs.PreparedLayout == plan && !generate.Enabled && printLayout.Enabled, "Direct entry retains layout and does not bypass exact animal selection");
            render(entry, "animal-entry");
            using var review = new ImportDialog(source, true, false); await review.ExerciseEnteredAnimalsForSmoke(check); render(review, "animal-entry-reviewed");
        }
        await Workspace.OpenAsync(fixture, discardChanges: true); Reload(); tabs.SelectedIndex = 0; SetStage(0);
    }
}
