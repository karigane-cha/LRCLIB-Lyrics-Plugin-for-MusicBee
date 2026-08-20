using Microsoft.Win32;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal static class ThemeHelper
    {
        private static readonly Color DarkBackground = Color.FromArgb(43, 43, 43);
        private static readonly Color DarkControl = Color.FromArgb(58, 58, 58);
        private static readonly Color DarkButton = Color.FromArgb(72, 72, 72);
        private static readonly Color DarkBorder = Color.FromArgb(105, 105, 105);
        private static readonly Color DarkForeground = Color.FromArgb(241, 241, 241);

        public static void Apply(Form form, PopupTheme theme)
        {
            var dark = IsDark(theme);
            ApplyControl(form, dark);
            ApplyTitleBar(form, dark);
        }

        private static void ApplyControl(Control control, bool dark)
        {
            control.ForeColor = dark ? DarkForeground : SystemColors.ControlText;
            control.BackColor = dark ? DarkBackground : SystemColors.Control;

            var button = control as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = dark ? DarkButton : SystemColors.Control;
                button.ForeColor = dark ? DarkForeground : SystemColors.ControlText;
                button.FlatAppearance.BorderColor = dark ? DarkBorder : SystemColors.ControlDark;
            }
            else if (control is ComboBox || control is TextBoxBase || control is NumericUpDown || control is ListView)
            {
                control.BackColor = dark ? DarkControl : SystemColors.Window;
                control.ForeColor = dark ? DarkForeground : SystemColors.WindowText;
            }

            foreach (Control child in control.Controls)
                ApplyControl(child, dark);
        }

        private static bool IsDark(PopupTheme theme)
        {
            if (theme == PopupTheme.Dark) return true;
            if (theme == PopupTheme.Light) return false;

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
                {
                    var value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value != null && Convert.ToInt32(value) == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyTitleBar(Form form, bool dark)
        {
            try
            {
                var value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
            }
            catch
            {
                // Older Windows versions may not support this attribute.
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
    }
}