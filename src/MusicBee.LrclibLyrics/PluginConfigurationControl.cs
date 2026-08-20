using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class PluginConfigurationControl : UserControl
    {
        private readonly PluginSettings settings;
        private readonly Action<PluginSettings> saveAction;
        private readonly IWin32Window owner;

        public PluginConfigurationControl(PluginSettings settings, Action<PluginSettings> saveAction, IWin32Window owner)
        {
            this.settings = settings;
            this.saveAction = saveAction;
            this.owner = owner;
            var strings = PluginLocalization.Get(settings.LanguageMode);

            Dock = DockStyle.Fill;
            Padding = new Padding(0);
            BackColor = Color.Transparent;

            var about = new Button { Text = strings.PluginInfoButton, Width = 96, Height = 21, Margin = new Padding(0), Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f), TextAlign = ContentAlignment.MiddleCenter };
            var settingsButton = new Button { Text = strings.PluginSettingsButton, Width = 96, Height = 21, Margin = new Padding(5, 0, 0, 0), Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f), TextAlign = ContentAlignment.MiddleCenter };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            buttons.Controls.Add(about);
            buttons.Controls.Add(settingsButton);
            Controls.Add(buttons);

            about.Click += ShowAbout;
            settingsButton.Click += ShowSettings;
            ParentChanged += (sender, args) => ApplyMusicBeeButtonStyle(about, settingsButton);
            ApplyMusicBeeButtonStyle(about, settingsButton);
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            using (var dialog = new AboutDialog(settings.LanguageMode, settings.PopupTheme))
            {
                if (owner == null) dialog.ShowDialog();
                else dialog.ShowDialog(owner);
            }
        }

        private void ShowSettings(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog(settings, saveAction))
            {
                if (owner == null) dialog.ShowDialog();
                else dialog.ShowDialog(owner);
            }
        }

        private void ApplyMusicBeeButtonStyle(params Button[] buttons)
        {
            var parentColor = Parent == null ? SystemColors.Control : Parent.BackColor;
            var dark = parentColor.GetBrightness() < 0.5f;
            foreach (var button in buttons)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = dark ? Color.FromArgb(72, 72, 72) : SystemColors.Control;
                button.ForeColor = dark ? Color.FromArgb(241, 241, 241) : SystemColors.ControlText;
                button.FlatAppearance.BorderColor = dark ? Color.FromArgb(105, 105, 105) : SystemColors.ControlDark;
            }
        }
    }
}