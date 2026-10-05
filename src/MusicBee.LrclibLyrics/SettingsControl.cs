using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class SettingsControl : UserControl
    {
        private readonly CheckBox syncedOnly = new CheckBox { AutoSize = true };
        private readonly Label existingLyricsSkipModeLabel = new Label { AutoSize = true };
        private readonly ComboBox existingLyricsSkipMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        private readonly Label embeddedLyricsHandlingLabel = new Label { AutoSize = true };
        private readonly ComboBox embeddedLyricsHandling = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        private readonly Label candidateDialogModeLabel = new Label { AutoSize = true };
        private readonly ComboBox candidateDialogMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        private readonly CheckBox overwrite = new CheckBox { AutoSize = true };
        private readonly Label popupThemeLabel = new Label { AutoSize = true };
        private readonly ComboBox popupTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        private readonly Label languageLabel = new Label { AutoSize = true };
        private readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
        private readonly NumericUpDown timeout = new NumericUpDown { Minimum = 3, Maximum = 60, Width = 55 };
        private readonly Button save = new Button { AutoSize = true };
        private readonly Button reset = new Button { AutoSize = true };
        private readonly PluginSettings settings;
        private readonly Action<PluginSettings> saveAction;
        private readonly LocalizedStrings strings;

        public SettingsControl(PluginSettings settings, Action<PluginSettings> saveAction)
        {
            this.settings = settings; this.saveAction = saveAction;
            strings = PluginLocalization.Get(settings.LanguageMode);
            Dock = DockStyle.Fill; Padding = new Padding(12); AutoScroll = true;

            syncedOnly.Text = strings.SyncedOnly;
            syncedOnly.Checked = settings.SyncedLyricsOnly;
            existingLyricsSkipModeLabel.Text = strings.LyricsCondition;
            existingLyricsSkipMode.Items.AddRange(strings.LyricsConditions);
            existingLyricsSkipMode.SelectedIndex = (int)settings.ExistingLyricsSkipMode;
            embeddedLyricsHandlingLabel.Text = strings.EmbeddedLyricsHandling;
            embeddedLyricsHandling.Items.AddRange(strings.EmbeddedLyricsOptions);
            embeddedLyricsHandling.SelectedIndex = (int)settings.EmbeddedLyricsHandling;
            candidateDialogModeLabel.Text = strings.CandidateDialogMode;
            candidateDialogMode.Items.AddRange(strings.CandidateDialogModes);
            candidateDialogMode.SelectedIndex = (int)settings.CandidateDialogMode;
            overwrite.Text = strings.Overwrite;
            overwrite.Checked = settings.OverwriteExistingLrcFile;
            popupThemeLabel.Text = strings.PopupTheme;
            popupTheme.Items.AddRange(strings.PopupThemes);
            popupTheme.SelectedIndex = (int)settings.PopupTheme;
            languageLabel.Text = strings.Language;
            language.Items.AddRange(strings.Languages);
            language.SelectedIndex = (int)settings.LanguageMode;
            save.Text = strings.Save;
            reset.Text = strings.ResetDefaults;
            timeout.Value = settings.RequestTimeoutSeconds;

            var description = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text = strings.Description
            };
            var timeoutLabel = new Label { Text = strings.Timeout, AutoSize = true };
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            panel.Controls.Add(description); panel.Controls.Add(Spacer()); panel.Controls.Add(syncedOnly);
            panel.Controls.Add(existingLyricsSkipModeLabel); panel.Controls.Add(existingLyricsSkipMode);
            panel.Controls.Add(embeddedLyricsHandlingLabel); panel.Controls.Add(embeddedLyricsHandling);
            panel.Controls.Add(candidateDialogModeLabel); panel.Controls.Add(candidateDialogMode); panel.Controls.Add(overwrite);
            panel.Controls.Add(popupThemeLabel); panel.Controls.Add(popupTheme);
            panel.Controls.Add(languageLabel); panel.Controls.Add(language); panel.Controls.Add(Spacer());
            var timeoutRow = new FlowLayoutPanel { AutoSize = true }; timeoutRow.Controls.Add(timeoutLabel); timeoutRow.Controls.Add(timeout); panel.Controls.Add(timeoutRow);
            panel.Controls.Add(Spacer());
            var buttonRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            buttonRow.Controls.Add(save); buttonRow.Controls.Add(reset);
            panel.Controls.Add(buttonRow); Controls.Add(panel);
            save.Click += Save;
            reset.Click += Reset;
        }

        private void Save(object sender, EventArgs e)
        {
            settings.SyncedLyricsOnly = syncedOnly.Checked;
            settings.ExistingLyricsSkipMode = (ExistingLyricsSkipMode)existingLyricsSkipMode.SelectedIndex;
            settings.EmbeddedLyricsHandling = (EmbeddedLyricsHandling)embeddedLyricsHandling.SelectedIndex;
            settings.CandidateDialogMode = (CandidateDialogMode)candidateDialogMode.SelectedIndex;
            settings.OverwriteExistingLrcFile = overwrite.Checked;
            settings.PopupTheme = (PopupTheme)popupTheme.SelectedIndex;
            settings.LanguageMode = (PluginLanguageMode)language.SelectedIndex;
            settings.RequestTimeoutSeconds = Decimal.ToInt32(timeout.Value);
            saveAction(settings); save.Text = PluginLocalization.Get(settings.LanguageMode).Saved;
        }
        private void Reset(object sender, EventArgs e)
        {
            var result = MessageBox.Show(this, strings.ResetConfirmation, strings.ResetDefaults, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            var defaults = new PluginSettings();
            syncedOnly.Checked = defaults.SyncedLyricsOnly;
            existingLyricsSkipMode.SelectedIndex = (int)defaults.ExistingLyricsSkipMode;
            embeddedLyricsHandling.SelectedIndex = (int)defaults.EmbeddedLyricsHandling;
            candidateDialogMode.SelectedIndex = (int)defaults.CandidateDialogMode;
            overwrite.Checked = defaults.OverwriteExistingLrcFile;
            popupTheme.SelectedIndex = (int)defaults.PopupTheme;
            language.SelectedIndex = (int)defaults.LanguageMode;
            timeout.Value = defaults.RequestTimeoutSeconds;
            save.Text = strings.Save;
        }

        private static Control Spacer() { return new Label { Height = 5, AutoSize = false }; }
    }
}
