using System.Collections.Immutable;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    internal async Task ExerciseSexSettingsForSmoke(string root, Action<bool, string> check, Action<Form, string> render)
    {
        async Task Restore(string path)
        {
            await Workspace.OpenAsync(path, discardChanges: true); setupDirty = selectionDirty = false; Reload(); SetStage(0);
        }
        Workspace.New("Specified sex composition", true);
        var pool = Enumerable.Range(1, 40).Select(i => new Animal("M" + i, "M" + i, 200 + i) { Sex = AnimalSex.Male })
            .Concat(Enumerable.Range(1, 40).Select(i => new Animal("F" + i, "F" + i, 100 + i) { Sex = AnimalSex.Female })).ToImmutableArray();
        Workspace.Revise(ProjectInputs.FromStudy(new Study(Design.Rcbd, pool, [], [new("T1", "Control"), new("T2", "Diet A"), new("T3", "Diet B")], 4, 4), "Setup")
            with
        { AutomaticPoolSelection = true }, "Setup");
        var layout = HousingLayouts.Create(Workspace.Inputs, 101, 112, 25);
        Workspace.Revise(Workspace.Inputs with { PreparedLayout = layout }, "Prepare fixed housing");
        selectionDirty = setupDirty = false; Reload(); ClearAnimalFilters(); SetStage(0);
        check(!sexSettingsPanel.Visible, "Ignore sex hides optional composition controls");
        sexMode.SelectedIndex = 1;
        check(sexPattern.SelectedIndex == 0 && sexSettingsPanel.Visible && sexPattern.Items.Count == 2, "Balance offers the two explicit patterns for a new study");
        firstMales.Text = "3";
        check(firstFemales.Text == "Female: 1", "Female count automatically completes fixed pen size");
        ApplySetup();
        check(Workspace.Inputs.Study.SexSettings is { Pattern: SexBalancePattern.SameInEveryPen, MalesPerPen: 3 }
            && Workspace.Inputs.Study.SelectedRecordIds.Count(id => id.StartsWith("M", StringComparison.Ordinal)) == 36, "Same-in-every-pen settings select the required heaviest males/females");
        render(this, "sex-counts-uniform");
        sexPattern.SelectedIndex = 1; firstGroupEnd.Text = "2"; firstMales.Text = "1"; secondMales.Text = "3";
        check(firstFemales.Text == "Female: 3" && secondFemales.Text == "Female: 1"
            && sexSettingLabels[3].Text.Contains("1–2") && sexSettingLabels[4].Text.Contains("3–4"), "Two-group controls show exact replicate ranges and derived females");
        ApplySetup();
        check(Workspace.IsReady && ReferenceEquals(Workspace.Inputs.PreparedLayout, layout), "Group composition keeps the prepared housing layout");
        check((await Workspace.RunAsync(101, "Two sex compositions")).Status == RunStatus.Success, "Two-group controls generate an allocation");
        Workspace.Map(layout.Pens, layout.Seed, "Use fixed housing"); Reload(); SetStage(0);
        string output = Path.Combine(root, "analysis", "desktop-validation");
        string saved = Path.Combine(output, "sex-counts-" + Guid.NewGuid().ToString("N") + ".experimentproj"); await Workspace.SaveAsync(saved);
        foreach (var size in new[] { new Size(1440, 900), new Size(1200, 780) })
        {
            ClientSize = size; render(this, $"sex-counts-groups-{size.Width}");
        }
        bool wasDark = Theme.IsDark; ToggleAppearance(false); render(this, "sex-counts-groups-other-theme");
        if (Theme.IsDark != wasDark) ToggleAppearance(false);
        ClientSize = new(1440, 900);
        foreach (float factor in new[] { 1.25f, 1.5f, 2f })
        {
            using var scaled = new MainForm(); scaled.StartPosition = FormStartPosition.Manual; scaled.Location = new(-25000, -25000); scaled.Show(); scaled.LoadForSmoke(saved);
            scaled.Scale(new SizeF(factor, factor)); scaled.ClientSize = new((int)(1200 * factor), (int)(780 * factor));
            render(scaled, $"sex-counts-scaled-{factor:0.##}"); scaled.CloseForSmoke();
        }
        sexMode.SelectedIndex = 2;
        check(maleReplicateCount.Text == "2" && femaleReplicateCount.Text == "Female: 2", "Switching from Balance to Separate uses a valid default replicate split");
        maleReplicateCount.Text = "1";
        check(femaleReplicateCount.Text == "Female: 3", "Female replicate count completes the total automatically");
        ApplySetup(); check(Workspace.Inputs.Study.SexSettings?.MaleReplicates == 1 && Workspace.IsReady, "Separate uses the chosen male replicate count");
        render(this, "sex-counts-separate");
        check((await Workspace.RunAsync(102, "One male and three female replicates")).Status == RunStatus.Success, "Separate controls allocate exact sex replicate counts");
        Reload(); SetStage(0);
        sexMode.SelectedIndex = 1; sexPattern.SelectedIndex = 0; firstMales.Text = "5";
        var before = Workspace.Document; bool rejected = false;
        try { ApplySetup(); } catch (ArgumentException) { rejected = true; }
        check(rejected && ReferenceEquals(before, Workspace.Document) && sexRequirements.Text.Contains("0 to 4"), "Invalid male counts are rejected without committing or losing prior work");
        firstMales.Text = "2"; perPen.Value = 100000;
        check(firstMales.Items.Count <= 33 && secondMales.Items.Count <= 33, "Large counts use bounded dropdown suggestions rather than thousands of UI items");
        await Restore(saved);
        Workspace.Revise(Workspace.Inputs with { AutomaticPoolSelection = false, Study = Workspace.Inputs.Study with { SexMode = SexAllocationMode.Ignore, SexSettings = null } }, "Legacy manual selection");
        Reload(); SetStage(0); sexMode.SelectedIndex = 1; firstMales.Text = "2"; ApplySetup();
        check(Workspace.Inputs.AutomaticPoolSelection && Workspace.Inputs.Study.SexSettings is not null, "Choosing explicit sex counts enables heaviest-within-sex selection for a legacy manual study");
        await Restore(saved);
        check(firstMales.Text == "1" && secondMales.Text == "3" && firstGroupEnd.Text == "2", "Saved group controls restore exact values");
        string legacyDirectory = Path.Combine(root, "analysis", "sex-settings-validation", "previous-preview");
        if (Directory.Exists(legacyDirectory)) foreach (string previous in Directory.EnumerateFiles(legacyDirectory, "*.experimentproj"))
        {
            await Restore(previous);
            check(Workspace.Inputs.Study.SexSettings is null && sexPattern.SelectedIndex == 2 && ReadSexSettings() is null, "Previous preview's automatic mix is labeled and retained when opened");
            ApplySetup(); check(Workspace.Inputs.Study.SexSettings is null, "Applying unrelated settings does not silently change previous preview selection policy");
        }
        await Restore(saved);
    }
}
