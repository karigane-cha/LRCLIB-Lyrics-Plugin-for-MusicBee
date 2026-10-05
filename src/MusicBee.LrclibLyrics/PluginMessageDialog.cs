using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class PluginMessageDialog : Form
    {
        private PluginMessageDialog(string message, string title, string confirmText, string cancelText, PopupTheme theme)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(360, 0);
            Padding = new Padding(14);

            var messageLabel = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text = message,
                Padding = new Padding(0, 0, 0, 12)
            };
            var confirm = new Button { Text = confirmText, DialogResult = DialogResult.Yes, AutoSize = true, MinimumSize = new Size(80, 0) };
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 8, 0, 0)
            };
            buttons.Controls.Add(confirm);
            if (cancelText != null)
            {
                var cancel = new Button { Text = cancelText, DialogResult = DialogResult.No, AutoSize = true, MinimumSize = new Size(80, 0) };
                buttons.Controls.Add(cancel);
                CancelButton = cancel;
            }
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(messageLabel, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);
            AcceptButton = confirm;
            ThemeHelper.Apply(this, theme);
        }

        public static Task<bool> ShowConfirmationAsync(string message, string title, string confirmText, string cancelText, PopupTheme theme)
        {
            return ShowAsync(message, title, confirmText, cancelText, theme);
        }

        public static async Task ShowInformationAsync(string message, string title, string okText, PopupTheme theme)
        {
            await ShowAsync(message, title, okText, null, theme).ConfigureAwait(false);
        }

        private static Task<bool> ShowAsync(string message, string title, string confirmText, string cancelText, PopupTheme theme)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    using (var dialog = new PluginMessageDialog(message, title, confirmText, cancelText, theme))
                        completion.SetResult(dialog.ShowDialog() == DialogResult.Yes);
                }
                catch (Exception exception) { completion.SetException(exception); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }
    }
}
