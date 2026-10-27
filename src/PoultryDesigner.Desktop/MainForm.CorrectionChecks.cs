namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    internal void ExerciseCorrectionsForSmoke(string fixture, string output, Action<bool, string> check, Action<Form, string> render)
    {
        void Restore() { Workspace.OpenAsync(fixture, discardChanges: true).GetAwaiter().GetResult(); setupDirty = selectionDirty = false; Reload(); }
        Restore(); SetStage(0);
        IEnumerable<Control> Descend(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descend(c)));
        var studyControls = Descend(stages[0]).ToArray();
        check(!studyControls.Any(c => c.Text is "Allocation constraints" or "Notes / metadata" or "Enforce CV limit" or "Study notes"),
            "Study screen hides optimization controls and metadata without hidden editable fields");
        var inputs = Workspace.Inputs;
        Workspace.Revise(inputs with { Notes = "Retained historical notes", Study = inputs.Study with { FilterBalance = true, CvLimitPercent = 90, MaximumAttemptsPerBlock = 7 } }, "Preservation fixture");
        Reload(); ApplySetup();
        check(Workspace.Inputs.Notes == "Retained historical notes" && Workspace.Inputs.Study.FilterBalance
            && Workspace.Inputs.Study.CvLimitPercent == 90 && Workspace.Inputs.Study.MaximumAttemptsPerBlock == 7
            && Workspace.Inputs.Study.Demonstration == inputs.Study.Demonstration, "Simplified setup preserves saved balancing settings, notes and demonstration policy");
        Restore(); SetStage(1); search.Text = "B0";
        var document = Workspace.Document; var binding = animals.DataSource; var before = Theme.IsDark;
        perPen.Value++; int draft = (int)perPen.Value; var selectionBefore = selection.ToArray();
        ToggleAppearance(false);
        check(Theme.IsDark != before && animals.BackgroundColor == Theme.Canvas && animals.DefaultCellStyle.ForeColor == Theme.Text,
            $"Theme switch updates the existing animal grid palette: dark {before}->{Theme.IsDark}; background {animals.BackgroundColor.ToArgb():X8}/{Theme.Canvas.ToArgb():X8}; text {animals.DefaultCellStyle.ForeColor.ToArgb():X8}/{Theme.Text.ToArgb():X8}");
        check(ReferenceEquals(document, Workspace.Document) && ReferenceEquals(binding, animals.DataSource)
            && perPen.Value == draft && setupDirty && search.Text == "B0" && selection.SequenceEqual(selectionBefore),
            "Theme switch retains project, animal binding, search, selection and pending setup values");
        SetStage(0); render(this, "theme-switched-study"); SetStage(1); render(this, "theme-switched-animals");
        ToggleAppearance(false);
        check(Theme.IsDark == before && appearance.Text == (before ? "Light mode" : "Dark mode"), "Second theme toggle restores palette and button label");
        RedesignChecks.CheckTheme(check);
        string settings = Path.Combine(output, "appearance-test.json");
        AppearancePreferences.Save(false, settings); check(AppearancePreferences.Load(settings) == false, "Light preference persists independently of project files");
        AppearancePreferences.Save(true, settings); check(AppearancePreferences.Load(settings) == true, "Dark preference persists independently of project files");
        File.WriteAllText(settings, "invalid preferences"); check(AppearancePreferences.Load(settings) is null, "Invalid preferences fall back without blocking startup");
        File.Delete(settings); Restore(); search.Clear(); SetStage(0);
    }
}
