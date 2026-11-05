using System.Collections.Immutable;
using System.Text;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    internal async Task ExerciseSexForSmoke(string fixture, Action<bool, string> check, Action<Form, string> render)
    {
        Workspace.New("Animal sex feature check", true);
        Workspace.Revise(ProjectInputs.FromStudy(new Study(Design.Rcbd, [], [],
            [new("T1", "Control"), new("T2", "Diet A"), new("T3", "Diet B")], 4, 4), "Sex study"), "Sex study");
        var source = DataImport.Read(Encoding.UTF8.GetBytes("ID,BW,Sex\n" + string.Join("\n",
            Enumerable.Range(1, 50).Select(i => $"{1000 + i},{100 + i},{(i % 2 == 0 ? "M" : "F")}"))), "sex-smoke.csv");
        var imported = DataImport.Animals(source, "Delimited", 1, 2, 51, new(1, 2, Sex: 3)).Value!;
        Workspace.ImportAutomaticAnimalPool(source, imported, false, "Import sex observations");
        selectionDirty = setupDirty = false; Reload(); ClearAnimalFilters(); SetStage(0);
        check(sexMode.SelectedIndex == 0, "New studies default to Ignore sex");
        sexMode.SelectedIndex = 1; ApplySetup();
        check(Workspace.Inputs.Study.SexMode == SexAllocationMode.Balance && Workspace.IsReady, "Study control applies Balance sexes");
        render(this, "sex-balance-study"); SetStage(1);
        check(animals.Columns["Sex"]!.Visible && sexCounts.Text.Contains("25 M / 25 F"), "Animal Sex column and pool counts are visible");
        sexFilter.SelectedItem = "Male";
        check(filteredAnimals.Length == 25 && animals.Rows.Cast<DataGridViewRow>().All(r => r.Cells["Sex"].Value is "Male"), "Sex filter displays males without changing selection");
        render(this, "sex-filtered-animals"); ClearAnimalFilters();
        var layout = HousingLayouts.Create(Workspace.Inputs, 101, 112, 25);
        Workspace.Revise(Workspace.Inputs with { PreparedLayout = layout }, "Prepare sex-study housing");
        var first = await Workspace.RunAsync(55, "Balanced sexes");
        check(first.Status == RunStatus.Success, "Native workspace balances heaviest selected animals");
        Workspace.Map(layout.Pens, layout.Seed, "Use prepared sex housing"); Reload(); SetStage(2);
        render(this, "sex-balanced-result");
        var chosenResult = Workspace.Choose(Workspace.Document.ActiveResultId!);
        check(chosenResult.Allocation.Positions.Length == 48 && chosenResult.Input.Inputs.Study.Pool.Length == 50, "Sex-aware native result retains 48 selected and two EX");
        SetStage(0); sexMode.SelectedIndex = 2; ApplySetup();
        check(Workspace.IsReady && ReferenceEquals(Workspace.Inputs.PreparedLayout, layout), "Separate setting keeps the prepared layout");
        check((await Workspace.RunAsync(56, "Separate sexes")).Status == RunStatus.Success, "Separate mode allocates through the workspace");
        Workspace.Map(layout.Pens, layout.Seed, "Keep prepared sex housing"); Reload(); SetStage(2);
        render(this, "sex-separate-result");
        var animal = Workspace.Inputs.Study.Pool.First(a => Workspace.Inputs.Study.SelectedRecordIds.Contains(a.RecordId));
        var missingSex = Workspace.Inputs.Study.Pool.Where(a => a.Sex == animal.Sex).Take(2).Select(a => a.RecordId).ToHashSet();
        Workspace.Revise(Workspace.Inputs with
        {
            Study = Workspace.Inputs.Study with
            {
                Pool = Workspace.Inputs.Study.Pool.Select(a => missingSex.Contains(a.RecordId) ? a with { Sex = AnimalSex.Unknown } : a).ToImmutableArray()
            }
        }, "Insufficient known-sex animals");
        Reload();
        check(!generate.Enabled && pageAlert.Text.Contains("specified composition requires"), "Missing known-sex animals disable allocation with an actionable quota message");
        render(this, "sex-unknown-guidance");
        await Workspace.OpenAsync(fixture, discardChanges: true); selectionDirty = setupDirty = false; Reload(); ClearAnimalFilters(); SetStage(0);
    }
}
