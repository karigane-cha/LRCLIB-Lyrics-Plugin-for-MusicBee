using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class SettingsDialog : Form
    {
        public SettingsDialog(PluginSettings settings, System.Action<PluginSettings> saveAction)
        {
            var strings = PluginLocalization.Get(settings.LanguageMode);
            Text = strings.SettingsTitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 500);
            MinimumSize = new Size(520, 450);

            Controls.Add(new SettingsControl(settings, value =>
            {
                saveAction(value);
                Text = PluginLocalization.Get(value.LanguageMode).SettingsTitle;
                ThemeHelper.Apply(this, value.PopupTheme);
            }));
            ThemeHelper.Apply(this, settings.PopupTheme);
        }
    }
}