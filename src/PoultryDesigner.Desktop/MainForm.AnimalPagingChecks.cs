using System.Collections.Immutable;
using PoultryDesigner.Engine;
using PoultryDesigner.AppCore;
namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    internal async Task ExerciseAnimalPagingForSmoke(string fixture, Action<bool, string> check)
    {
        Console.WriteLine("Checking 289 imported pending animals...");
        Workspace.New("Pending import regression", discardChanges: true);
        Workspace.Revise(ProjectInputs.FromStudy(new Study(Design.Rcbd, [], [],
            [new("T1", "Control"), new("T2", "Diet A"), new("T3", "Diet B")], 8, 12), "Import fixture"), "Import fixture");
        string csv = "ID,Weight\n" + string.Join("\n", Enumerable.Range(1, 289).Select(i => $"{1119 + i},{47 + i / 10}"));
        string? regressionFile = Environment.GetEnvironmentVariable("EXPERIMENT_DESIGNER_TEST_ANIMALS");
        var source = string.IsNullOrEmpty(regressionFile)
            ? DataImport.Read(System.Text.Encoding.UTF8.GetBytes(csv), "pending-289.csv") : DataImport.Read(regressionFile);
        int lastSourceRow = source.Tables.Single().Rows.Max(row => row.Number), importedCount = lastSourceRow - 1;
        var preview = DataImport.Animals(source, source.Tables.Single().Name, 1, 2, lastSourceRow, new(1, 2), allowIntegerIds: true);
        check(preview.Success, "Source validates with explicit integer-ID policy: " + string.Join("; ", preview.Issues.Select(i => i.ToString())));
        var batch = preview.Value!;
        using (var reviewed = new ImportDialog(source, true, false))
        {
            Exception? reviewError = null;
            reviewed.StartPosition = FormStartPosition.Manual; reviewed.Location = Location;
            reviewed.Shown += async (_, _) =>
            {
                try { await reviewed.ReviewPendingPoolForSmoke(check, lastSourceRow); }
                catch (Exception ex) { reviewError = ex; reviewed.DialogResult = DialogResult.Cancel; }
            };
            var outcome = reviewed.ShowDialog(this);
            if (reviewError is not null) throw reviewError;
            check(outcome == DialogResult.OK, "Actual modal import returns a reviewed batch to the main window");
            Workspace.ImportAnimals(reviewed.Source, reviewed.AnimalBatch!, reviewed.Append, reviewed.Reason);
        }
        selectionDirty = setupDirty = false; search.Clear(); Reload(); SetStage(1);
        Application.DoEvents();
        check(animals.Rows.Count == importedCount && selection.Values.All(x => x.Choice == SelectionChoice.Pending), "Real import displays all pending records for a 288-animal study");
        int gridHeight = animals.Height;
        var statCells = animalStats.Controls.Cast<Control>().ToArray();
        animals.Rows[0].Selected = true; Application.DoEvents();
        Console.WriteLine("Pending import row highlighted.");
        animals.ClearSelection(); Application.DoEvents();
        check(animals.Height == gridHeight && !decisionActions.Controls.OfType<Button>().Any(b => b.Enabled), "Highlight changes preserve animal grid geometry and disable unavailable actions");
        UpdateSelectionCount();
        check(animalStats.Controls.Cast<Control>().SequenceEqual(statCells), "Selection refresh preserves existing statistic controls and native handles");
        check(animalStats.GetControlFromPosition(0, 0)!.Controls["stat-label"]!.Text == "Source animals"
            && animalStats.GetControlFromPosition(0, 0)!.Controls["stat-value"]!.Text == importedCount.ToString("N0"),
            "Statistic refresh preserves captions while updating the displayed count");
        Console.WriteLine("Pending import highlight cleared.");
        var originalSize = ClientSize;
        Scale(new SizeF(1.25f, 1.25f)); ClientSize = new(1675, 975);
        Reload(); SetStage(1); animals.CurrentCell = animals.Rows[0].Cells["Animal ID"];
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(150);
            animals.FirstDisplayedScrollingRowIndex = i * 50;
            animals.Rows[i * 50].Cells["Mark"].Value = true;
            animals.Invalidate();
        }
        check(HighlightedIds().Length >= 5 && animals.Rows.Count == importedCount, "Pending imported animal grid processes timed UI callbacks, scrolling and marks at 125% scale");
        for (int i = 0; i < 12; i++)
        {
            ClientSize = new(1675 - i % 2 * 100, 975 - i % 2 * 40);
            animals.ClearSelection(); animals.Rows[i].Selected = true;
            await Task.Delay(20);
        }
        check(animals.DisplayedRowCount(false) > 0 && animals.Rows.Count == importedCount, "Repeated live resizing and pending-row selection retain a usable animal table");
        Scale(new SizeF(.8f, .8f)); ClientSize = originalSize; checkedAnimals.Clear();
        Workspace.Revise(Workspace.Inputs with { AutomaticPoolSelection = true }, "Enable heaviest-first automatic pool");
        selectionDirty = setupDirty = false; Reload(); SetStage(1); Application.DoEvents();
        check(Workspace.IsReady && selection.Values.Count(d => d.Choice == SelectionChoice.Include) == 288 && !manualSelectionActions.Visible,
            "Automatic import is ready for 288 places without manual Include/Exclude controls");
        check(animals.Rows.Cast<DataGridViewRow>().Count(row => Convert.ToString(row.Cells["Decision"].Value) == "Exclude") == importedCount - 288,
            "Animal table retains the unused extra record and its automatic status");
        await ExerciseAnimalIdleForSmoke(check);
        await RunAsync(); Application.DoEvents();
        check(chosen is not null && chosen.Allocation.Positions.Length == 288 && AllocationReports.Create(chosen, false, false).Tables.Single(t => t.Name == "Movement").Rows.Count(row => row[4] is "EX") == importedCount - 288,
            "Main-window automatic allocation and mapped export produce the correct EX count without a selection commit");
        Workspace.New("Animal paging regression", discardChanges: true);
        var pool = Enumerable.Range(1, 1201).Select(i => new Animal($"paging-{i:D4}", $"A{i:D4}", 100m + i / 100m)).ToImmutableArray();
        var study = new Study(Design.Rcbd, pool, pool.Take(1200).Select(a => a.RecordId).ToImmutableArray(),
            [new("T1", "Control"), new("T2", "Diet A"), new("T3", "Diet B")], 4, 100);
        Workspace.Revise(ProjectInputs.FromStudy(study, "Paging fixture"), "Paging fixture");
        selectionDirty = setupDirty = false; search.Clear(); Reload(); SetStage(1); Application.DoEvents();
        check(animals.Rows.Count == 500, "Animal paging starts with the first 500 records");
        Button Page(string text) => animalPagination.Controls.OfType<Button>().Single(b => b.Text == text);
        var nextButton = Page("Next"); var sizeSelector = animalPagination.Controls.OfType<ComboBox>().Single();
        var decisionColumn = animals.Columns["Decision"];
        animals.CurrentCell = animals.Rows[499].Cells["Decision"]; ShowAnimalDecisionMenu(499);
        animalDecisionMenu.Items.OfType<ToolStripMenuItem>().Single(i => Equals(i.Tag, SelectionChoice.Exclude)).PerformClick(); animalDecisionMenu.Close();
        animalDetails.Checked = true; animals.CurrentCell = animals.Rows[499].Cells["Reason"]; animals.BeginEdit(true);
        if (animals.EditingControl is TextBox reasonEditor) reasonEditor.Text = "Reviewed at page boundary";
        Console.WriteLine("Checking actual animal table page 2...");
        Page("2").PerformClick(); Application.DoEvents();
        check(animals.Rows.Count == 500 && Convert.ToString(animals.Rows[0].Cells["Animal ID"].Value) == "A0501", "Actual animal page 2 displays record 501 without a crash or hang");
        check(selection["paging-0500"].Choice == SelectionChoice.Exclude && selection["paging-0500"].Reason == "Reviewed at page boundary" && ReferenceEquals(decisionColumn, animals.Columns["Decision"]), "Leaving an actively edited reason commits it without replacing live grid columns");
        check(ReferenceEquals(nextButton, Page("Next")) && ReferenceEquals(sizeSelector, animalPagination.Controls.OfType<ComboBox>().Single()) && !nextButton.IsDisposed,
            "Native paging button and row selector survive navigation without disposal or focus reentrancy");
        animals.Rows[0].Cells["Decision"].Value = "Exclude"; animals.Rows[0].Cells["Mark"].Value = true;
        Page("3").PerformClick(); Application.DoEvents();
        check(animals.Rows.Count == 201 && Convert.ToString(animals.Rows[0].Cells["Animal ID"].Value) == "A1001", "Last animal page contains the correct partial page");
        Page("Previous").PerformClick(); Application.DoEvents();
        check(selection["paging-0501"].Choice == SelectionChoice.Exclude && Equals(animals.Rows[0].Cells["Mark"].Value, true), "Page navigation retains decisions and marks on immutable animal records");
        var size = animalPagination.Controls.OfType<ComboBox>().Single(); size.SelectedItem = "250"; Application.DoEvents();
        check(animals.Rows.Count == 250 && animalRowsPerPage == 250 && animalPage == 0, "Changing animal page size resets to a valid first page");
        Page("3").PerformClick(); Application.DoEvents();
        check(Convert.ToString(animals.Rows[0].Cells["Animal ID"].Value) == "A0501" && Convert.ToString(animals.Rows[0].Cells["Decision"].Value) == "Exclude", "Animal decisions retain identity after page size changes");
        for (int i = 0; i < 8; i++) { Page("Next").PerformClick(); Application.DoEvents(); Page("Previous").PerformClick(); Application.DoEvents(); }
        check(selection["paging-0501"].Choice == SelectionChoice.Exclude, "Repeated Next and Previous keep edits without reentrant pagination");
        animalDetails.Checked = true; Page("Next").PerformClick(); Application.DoEvents();
        check(animals.Columns["Reason"]!.Visible && animals.Rows.Count > 0, "Details remain stable while paging with editable audit fields");
        animalDetails.Checked = false;
        search.Text = "A1201"; Application.DoEvents();
        check(animals.Rows.Count == 1 && animalPage == 0 && Convert.ToString(animals.Rows[0].Cells["Animal ID"].Value) == "A1201", "Filtering from a later animal page resets to the correct record");
        search.Clear(); selectionDirty = setupDirty = false; await Workspace.OpenAsync(fixture, discardChanges: true); Reload(); SetStage(0);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private async Task ExerciseAnimalIdleForSmoke(Action<bool, string> check)
    {
        var log = Path.Combine(Environment.CurrentDirectory, "analysis", "desktop-validation", "animal-idle-check.txt");
        void Step(string message) => File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + "\n");
        File.WriteAllText(log, "Imported animal idle/scroll regression\n");
        check(!animals.ShowCellToolTips && string.IsNullOrEmpty(Components.Help.GetToolTip(animals)),
            "Scrolling animal table has no native cell or whole-grid tracking tooltips");
        int beats = 0; using var heartbeat = new System.Windows.Forms.Timer { Interval = 100 };
        heartbeat.Tick += (_, _) => beats++; heartbeat.Start();
        Notify("Animal pool imported"); animals.Focus();
        var bounds = animals.GetCellDisplayRectangle(animals.Columns["Decision"]!.Index, 0, true);
        var position = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        IntPtr packed = new((position.Y << 16) | (position.X & 0xffff));
        SendMessage(animals.Handle, 0x200, IntPtr.Zero, packed); // WM_MOUSEMOVE on our own table
        SendMessage(animals.Handle, 0x2A1, IntPtr.Zero, packed); // WM_MOUSEHOVER
        Step("Hover entered; waiting beyond toast and tooltip timers");
        string help = Components.Help.GetToolTip(animals) ?? "";
        if (help.Length > 0) Components.Help.Show(help, animals, position, 16000);
        await Task.Delay(18000);
        Step("Idle completed; sending native wheel messages");
        check(beats >= 80 && !toast.Visible, "Animal page remains responsive beyond idle tooltip and notification delays");
        Components.Help.Hide(animals);
        for (int i = 0; i < 12; i++)
        {
            SendMessage(animals.Handle, 0x20A, new IntPtr(unchecked((int)(0xff880000u))), IntPtr.Zero); // WM_MOUSEWHEEL -120
            await Task.Delay(30);
        }
        check(animals.FirstDisplayedScrollingRowIndex > 0 && animals.Rows.Count == filteredAnimals.Length,
            "Native mouse wheel scrolls the imported animal table after an extended idle period");
        int wheelPosition = animals.FirstDisplayedScrollingRowIndex;
        var verticalScroll = animals.Controls.OfType<VScrollBar>().Single();
        SendMessage(animals.Handle, 0x115, new IntPtr(3), verticalScroll.Handle); // WM_VSCROLL / SB_PAGEDOWN
        check(animals.FirstDisplayedScrollingRowIndex > wheelPosition, "Native scrollbar page-down remains responsive after idle");
        SendMessage(animals.Handle, 0x100, new IntPtr((int)Keys.PageDown), IntPtr.Zero); // WM_KEYDOWN
        await Task.Delay(100);
        check(animals.CurrentCell is not null && beats >= 80, "Keyboard scrolling and the UI heartbeat remain active after idle");
        Step("Native idle/scroll passed");
    }

    internal void ExerciseAllTabsForSmoke(Action<bool, string> check, Action<Form, string> render)
    {
        static IEnumerable<TabControl> Tabs(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => (c is TabControl t ? new[] { t } : Enumerable.Empty<TabControl>()).Concat(Tabs(c)));
        for (int stageIndex = 0; stageIndex < stages.Length; stageIndex++)
        {
            SetStage(stageIndex); Application.DoEvents();
            check(stages[stageIndex].Visible && stages.Where((_, i) => i != stageIndex).All(p => !p.Visible), "Exactly one main desktop stage is visible: " + title.Text);
            foreach (var tabs in Tabs(stages[stageIndex]).ToArray())
            {
                int original = tabs.SelectedIndex;
                for (int page = 0; page < tabs.TabPages.Count; page++)
                {
                    tabs.SelectedIndex = page; Application.DoEvents(); var selected = tabs.SelectedTab!;
                    check(selected.Visible && selected.Controls.Count > 0 && selected.ClientSize.Width > 0 && selected.ClientSize.Height > 0,
                        "Native desktop tab opens with its complete view: " + selected.Text);
                    render(this, $"final-stage-{stageIndex}-tab-{page}");
                }
                tabs.SelectedIndex = original;
            }
        }
        SetStage(0);
    }
}

