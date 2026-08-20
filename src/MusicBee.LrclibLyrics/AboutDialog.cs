using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class AboutDialog : Form
    {
        public AboutDialog(PluginLanguageMode language, PopupTheme theme)
        {
            var strings = PluginLocalization.Get(language);
            Text = strings.AboutTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(390, 180);

            var icon = new PictureBox
            {
                Image = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(24, 28)
            };
            var information = new Label
            {
                AutoSize = true,
                Location = new Point(88, 25),
                MaximumSize = new Size(275, 90),
                Text = strings.AboutDescription
            };
            var ok = new Button
            {
                Text = strings.Ok,
                DialogResult = DialogResult.OK,
                Width = 96,
                Height = 24,
                Location = new Point(147, 135)
            };

            Controls.Add(icon);
            Controls.Add(information);
            Controls.Add(ok);
            AcceptButton = ok;
            ThemeHelper.Apply(this, theme);
        }
    }
}