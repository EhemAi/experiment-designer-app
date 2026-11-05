using System.Globalization;
using PoultryDesigner.Engine;

namespace PoultryDesigner.Desktop;

internal sealed partial class MainForm
{
    private const string LegacySexCounts = "Automatic (previous preview)";
    private TableLayoutPanel studyStack = null!, sexSettingsPanel = null!;
    private readonly ComboBox sexPattern = Ui.Combo(["Same in every pen", "Two groups"]);
    private readonly ComboBox maleReplicateCount = CountDropdown(), firstGroupEnd = CountDropdown(), firstMales = CountDropdown(), secondMales = CountDropdown();
    private readonly Label femaleReplicateCount = Ui.Label(""), firstFemales = Ui.Label(""), secondFemales = Ui.Label(""), sexRequirements = Ui.Label("");
    private readonly Label[] sexSettingLabels = Enumerable.Range(0, 5).Select(_ => Ui.Label("")).ToArray();
    private bool refreshingSexSettings;
    private static ComboBox CountDropdown() => new DesktopComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 76, MaxDropDownItems = 10, MaxLength = 6 };

    private void BuildSexSettings()
    {
        sexSettingsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new(12, 0, 12, 0), Margin = Padding.Empty };
        sexSettingsPanel.ColumnStyles.Add(new(SizeType.Percent, 55)); sexSettingsPanel.ColumnStyles.Add(new(SizeType.Percent, 45));
        Control Pair(ComboBox males, Label females)
        {
            males.Width = 76; females.AutoSize = true;
            var bar = Ui.Bar(males, females); bar.WrapContents = false; bar.Padding = Padding.Empty; bar.Dock = DockStyle.Fill; return bar;
        }
        Control[] inputs = [sexPattern, Pair(maleReplicateCount, femaleReplicateCount), firstGroupEnd, Pair(firstMales, firstFemales), Pair(secondMales, secondFemales)];
        for (int row = 0; row < inputs.Length; row++)
        {
            sexSettingsPanel.RowStyles.Add(new(SizeType.Absolute, 46));
            inputs[row].Dock = DockStyle.Fill; inputs[row].Margin = new(4, 2, 4, 2);
            sexSettingLabels[row].Dock = DockStyle.Fill; sexSettingLabels[row].AutoSize = false; sexSettingLabels[row].TextAlign = ContentAlignment.MiddleLeft;
            sexSettingsPanel.Controls.Add(sexSettingLabels[row], 0, row); sexSettingsPanel.Controls.Add(inputs[row], 1, row);
        }
        sexSettingsPanel.RowStyles.Add(new(SizeType.Absolute, 46));
        sexRequirements.Dock = DockStyle.Fill; sexRequirements.AutoSize = false; sexRequirements.Padding = new(0, 6, 0, 4);
        sexSettingsPanel.Controls.Add(sexRequirements, 0, 5); sexSettingsPanel.SetColumnSpan(sexRequirements, 2);
        sexPattern.AccessibleName = "Sex composition pattern"; maleReplicateCount.AccessibleName = "Male replicates";
        firstGroupEnd.AccessibleName = "Last replicate in group 1"; firstMales.AccessibleName = "Group 1 males per pen"; secondMales.AccessibleName = "Group 2 males per pen";
        foreach (var combo in new[] { sexPattern, maleReplicateCount, firstGroupEnd, firstMales, secondMales })
            combo.TextChanged += (_, _) => { if (!loading && !refreshingSexSettings) { DraftSetup(); RefreshSexSettings(); } };
        RefreshSexSettings();
    }

    private void SexModeChanged()
    {
        if (!loading && !refreshingSexSettings)
        {
            refreshingSexSettings = true;
            try
            {
                if (sexPattern.SelectedIndex < 0 || sexPattern.SelectedIndex == 2) sexPattern.SelectedIndex = 0;
                if (maleReplicateCount.Text == LegacySexCounts) maleReplicateCount.Text = ((int)replicates.Value / 2).ToString(CultureInfo.InvariantCulture);
            }
            finally { refreshingSexSettings = false; }
        }
        DraftSetup(); RefreshSexSettings(); Capabilities();
    }

    private void LoadSexSettings(Study study)
    {
        refreshingSexSettings = true;
        try
        {
            var options = study.SexSettings;
            bool legacyBalance = study.SexMode == SexAllocationMode.Balance && options is null;
            if (legacyBalance && sexPattern.Items.Count == 2) sexPattern.Items.Add("Automatic mix (previous preview)");
            if (!legacyBalance && sexPattern.Items.Count > 2) sexPattern.Items.RemoveAt(2);
            sexPattern.SelectedIndex = study.SexMode == SexAllocationMode.Balance && options is null ? 2 : (int)(options?.Pattern ?? SexBalancePattern.SameInEveryPen);
            maleReplicateCount.Text = study.SexMode == SexAllocationMode.Separate && options is null ? LegacySexCounts
                : (study.SexMode == SexAllocationMode.Separate && options is not null ? options.MaleReplicates : study.Replicates / 2).ToString(CultureInfo.InvariantCulture);
            firstGroupEnd.Text = (options is { Pattern: SexBalancePattern.TwoReplicateGroups } ? options.FirstGroupReplicates : Math.Max(1, study.Replicates / 2)).ToString(CultureInfo.InvariantCulture);
            firstMales.Text = (options is not null && study.SexMode == SexAllocationMode.Balance ? options.MalesPerPen : study.AnimalsPerPen / 2).ToString(CultureInfo.InvariantCulture);
            secondMales.Text = (options is { Pattern: SexBalancePattern.TwoReplicateGroups } ? options.SecondGroupMalesPerPen : study.AnimalsPerPen / 2).ToString(CultureInfo.InvariantCulture);
        }
        finally { refreshingSexSettings = false; }
        RefreshSexSettings();
    }

    private static int CountValue(ComboBox combo, string name, int maximum, int minimum = 0)
    {
        if (!int.TryParse(combo.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < minimum || count > maximum)
            throw new ArgumentException($"{name}: enter a whole number from {minimum:N0} to {Math.Max(minimum, maximum):N0}.");
        return count;
    }

    private SexAllocationSettings? ReadSexSettings()
    {
        if (sexMode.SelectedIndex <= 0) return null;
        int r = (int)replicates.Value, p = (int)perPen.Value;
        if (sexMode.SelectedIndex == (int)SexAllocationMode.Separate)
        {
            if (maleReplicateCount.Text == LegacySexCounts) return null;
            return new(SexBalancePattern.SameInEveryPen, CountValue(maleReplicateCount, "Male replicates", r), 0, 0, 0);
        }
        if (sexPattern.SelectedIndex == 2) return null;
        if (sexPattern.SelectedIndex is < 0 or > 1) throw new ArgumentException("Choose a sex composition pattern.");
        int males = CountValue(firstMales, "Males per pen", p);
        return sexPattern.SelectedIndex == 0 ? new(SexBalancePattern.SameInEveryPen, 0, males, 0, 0)
            : new(SexBalancePattern.TwoReplicateGroups, 0, males, CountValue(firstGroupEnd, "Group 1 last replicate", r - 1, 1), CountValue(secondMales, "Group 2 males per pen", p));
    }

    private void RefreshSexSettings()
    {
        if (sexSettingsPanel is null || refreshingSexSettings) return;
        refreshingSexSettings = true;
        try
        {
            int r = (int)replicates.Value, p = (int)perPen.Value;
            void Choices(ComboBox combo, int max, int fallback, bool legacy = false)
            {
                string previous = combo.Text;
                var identity = (max, legacy);
                if (Equals(combo.Tag, identity)) return;
                combo.BeginUpdate();
                try
                {
                    combo.Items.Clear(); if (legacy) combo.Items.Add(LegacySexCounts);
                    // Suggest common counts; larger studies can type an exact count.
                    foreach (int n in Enumerable.Range(0, Math.Min(max, 30) + 1)) combo.Items.Add(n.ToString(CultureInfo.InvariantCulture));
                    if (max > 30) combo.Items.Add(max.ToString(CultureInfo.InvariantCulture));
                    combo.Text = previous.Length > 0 ? previous : fallback.ToString(CultureInfo.InvariantCulture); combo.Tag = identity;
                }
                finally { combo.EndUpdate(); }
            }
            Choices(maleReplicateCount, r, r / 2, maleReplicateCount.Text == LegacySexCounts);
            Choices(firstGroupEnd, Math.Max(1, r - 1), Math.Max(1, r / 2)); Choices(firstMales, p, p / 2); Choices(secondMales, p, p / 2);
            bool separate = sexMode.SelectedIndex == 2, balanceMode = sexMode.SelectedIndex == 1;
            bool grouped = balanceMode && sexPattern.SelectedIndex == 1, legacyBalance = balanceMode && sexPattern.SelectedIndex == 2;
            bool[] shown = [balanceMode, separate, grouped, balanceMode && !legacyBalance, grouped];
            sexSettingLabels[0].Text = "Pen composition"; sexSettingLabels[1].Text = "Male replicates";
            sexSettingLabels[2].Text = "Group 1 last replicate";
            int.TryParse(firstGroupEnd.Text, out int end);
            sexSettingLabels[3].Text = grouped ? $"Replicates 1–{end}: males/pen" : "Males per pen";
            sexSettingLabels[4].Text = $"Replicates {end + 1}–{r}: males/pen";
            for (int row = 0; row < shown.Length; row++)
            {
                sexSettingsPanel.RowStyles[row].Height = shown[row] ? 46 : 0;
                for (int col = 0; col < 2; col++) sexSettingsPanel.GetControlFromPosition(col, row)!.Visible = shown[row];
            }
            string Remainder(ComboBox combo, int total) => int.TryParse(combo.Text, out int n) && n >= 0 && n <= total ? $"Female: {total - n:N0}" : "Female: —";
            femaleReplicateCount.Text = Remainder(maleReplicateCount, r); firstFemales.Text = Remainder(firstMales, p); secondFemales.Text = Remainder(secondMales, p);
            bool active = separate || balanceMode; sexSettingsPanel.Visible = active;
            int summaryHeight = legacyBalance || (separate && maleReplicateCount.Text == LegacySexCounts) ? 64 : 46;
            sexSettingsPanel.RowStyles[5].Height = summaryHeight;
            int height = active ? shown.Count(x => x) * 46 + summaryHeight : 0;
            studyStack.RowStyles[1].Height = height;
            int designHeight = (int)studyStack.RowStyles[0].Height, helpHeight = (int)studyStack.RowStyles[2].Height;
            studyStack.Height = designHeight + height + helpHeight;
            if (active)
            {
                try
                {
                    var settings = ReadSexSettings();
                    if (settings is null) sexRequirements.Text = "This saved study uses the previous preview's automatic composition. Choose explicit counts to change it.";
                    else
                    {
                        int treatmentCount = treatmentTable.Rows.Cast<System.Data.DataRow>().Count(x => x.RowState != System.Data.DataRowState.Deleted);
                        var needs = SexAllocation.RequiredCounts(treatmentCount, r, p, (SexAllocationMode)sexMode.SelectedIndex, settings);
                        sexRequirements.Text = $"Required: {needs.Males:N0} males · {needs.Females:N0} females · {p:N0} per pen";
                    }
                    sexRequirements.ForeColor = Theme.Muted;
                }
                catch (ArgumentException ex) { sexRequirements.Text = ex.Message; sexRequirements.ForeColor = Theme.Warning; }
            }
        }
        finally { refreshingSexSettings = false; }
    }
}
