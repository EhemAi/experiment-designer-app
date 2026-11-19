using PoultryDesigner.AppCore;

namespace PoultryDesigner.Desktop;

internal sealed class SettingsDialog : Form
{
    internal readonly ComboBox LanguageSelector = Ui.Combo(["English", "Español (España)", "Español (México)"]);
    internal readonly ComboBox AppearanceSelector = Ui.Combo(["Light", "Dark"]);
    internal readonly Button SaveButton, CancelChanges;
    private readonly Label error = Ui.Label("");

    internal SettingsDialog(AppLanguage language, bool dark, Action<AppLanguage, bool> apply)
    {
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Text = "Settings"; Icon = Brand.Icon; Font = Theme.Body();
        ClientSize = new(660, 440); MinimumSize = new(600, 440);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        ((DesktopComboBox)LanguageSelector).TranslateItems = false;
        LanguageSelector.SelectedIndex = (int)language;
        AppearanceSelector.SelectedIndex = dark ? 1 : 0;
        var heading = Ui.Label("Settings"); heading.Font = Theme.Heading(18);
        var description = Ui.Label("Personalize language and appearance.");
        var fields = Components.Fields(("Language", LanguageSelector), ("Appearance", AppearanceSelector));
        var general = Components.Card("General", fields); general.Height = 142; general.Dock = DockStyle.Top;
        var note = Ui.Label("These preferences apply to this computer. Your study data are unaffected.");
        error.ForeColor = Theme.Red; error.Visible = false;
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new(24, 12, 24, 12) };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.Controls.Add(heading); content.Controls.Add(description); content.Controls.Add(general); content.Controls.Add(note); content.Controls.Add(error);
        foreach (var label in new[] { heading, description, note, error }) { label.AutoSize = true; label.Dock = DockStyle.Top; }
        content.SizeChanged += (_, _) =>
        {
            int width = Math.Max(1, content.ClientSize.Width - content.Padding.Horizontal - 8);
            foreach (var label in new[] { heading, description, note, error }) label.MaximumSize = new(width, 0);
        };
        SaveButton = Ui.Button("Save", () =>
        {
            try
            {
                apply((AppLanguage)LanguageSelector.SelectedIndex, AppearanceSelector.SelectedIndex == 1);
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { error.Text = "Could not save application settings. " + ex.Message; error.Visible = true; }
        });
        ((StyledButton)SaveButton).Primary = true;
        CancelChanges = Ui.Button("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        var footer = Ui.Bar(SaveButton, CancelChanges); footer.Dock = DockStyle.Bottom;
        footer.FlowDirection = FlowDirection.RightToLeft; footer.Padding = new(24, 8, 24, 16);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(content);
        Controls.Add(scroll); Controls.Add(footer); AcceptButton = SaveButton; CancelButton = CancelChanges;
        Theme.Apply(this);
    }
}

internal sealed partial class MainForm
{
    private SettingsDialog CreateSettingsDialog() => new(AppText.Language, Theme.IsDark, ApplyApplicationSettings);
    private void OpenSettings()
    {
        if (inOperation) return;
        using var dialog = CreateSettingsDialog(); dialog.ShowDialog(this);
    }
    private void ApplyApplicationSettings(AppLanguage language, bool dark)
    {
        AppLanguage previousPreference = LanguagePreference.Load(languagePreferencePath);
        bool languageSaved = false;
        try
        {
            LanguagePreference.Save(language, languagePreferencePath); languageSaved = true;
            AppearancePreferences.Save(dark, appearancePreferencePath);
        }
        catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (languageSaved)
            {
                try { LanguagePreference.Save(previousPreference, languagePreferencePath); }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                { throw new IOException("Could not restore the previous language preference. " + rollbackError.Message, saveError); }
            }
            throw;
        }
        AppText.Select(language, remember: false);
        if (dark != Theme.IsDark) ToggleAppearance(persist: false);
    }
}
