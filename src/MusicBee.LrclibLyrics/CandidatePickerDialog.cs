using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class CandidatePickerDialog : Form
    {
        private readonly ListView candidates = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        private readonly Button select = new Button { Enabled = false, AutoSize = true };
        public LyricsResult SelectedCandidate { get; private set; }

        private CandidatePickerDialog(TrackMetadata track, IEnumerable<LyricsResult> results, PluginLanguageMode language, PopupTheme theme)
        {
            var strings = PluginLocalization.Get(language);
            Text = strings.CandidateDialogTitle;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(680, 350);
            Size = new Size(820, 440);
            select.Text = strings.SelectLyrics;
            candidates.Columns.Add(strings.CandidateTitle, 210); candidates.Columns.Add(strings.CandidateArtist, 160); candidates.Columns.Add(strings.CandidateAlbum, 160);
            candidates.Columns.Add(strings.CandidateDuration, 65); candidates.Columns.Add(strings.CandidateType, 75);
            foreach (var result in results)
            {
                var item = new ListViewItem(result.Title ?? string.Empty);
                item.SubItems.Add(result.Artist ?? string.Empty); item.SubItems.Add(result.Album ?? string.Empty);
                item.SubItems.Add(FormatDuration(result.DurationSeconds)); item.SubItems.Add(result.IsSynced ? strings.Synced : strings.Unsynced);
                item.Tag = result; candidates.Items.Add(item);
            }
            var heading = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 8),
                Text = strings.SelectCandidate(track.Artist, track.Title)
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
            var cancel = new Button { Text = strings.Cancel, DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel); buttons.Controls.Add(select);
            Controls.Add(candidates); Controls.Add(buttons); Controls.Add(heading);
            AcceptButton = select; CancelButton = cancel;
            candidates.SelectedIndexChanged += (sender, args) => select.Enabled = candidates.SelectedItems.Count == 1;
            candidates.DoubleClick += (sender, args) => SelectCandidate();
            select.Click += (sender, args) => SelectCandidate();
            ThemeHelper.Apply(this, theme);
        }

        public static Task<LyricsResult> ShowAsync(TrackMetadata track, IList<LyricsResult> results, PluginLanguageMode language, PopupTheme theme)
        {
            var completion = new TaskCompletionSource<LyricsResult>();
            var thread = new Thread(() =>
            {
                try
                {
                    using (var dialog = new CandidatePickerDialog(track, results, language, theme))
                        completion.SetResult(dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedCandidate : null);
                }
                catch (Exception exception) { completion.SetException(exception); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }

        private void SelectCandidate()
        {
            if (candidates.SelectedItems.Count != 1) return;
            SelectedCandidate = (LyricsResult)candidates.SelectedItems[0].Tag;
            DialogResult = DialogResult.OK;
        }

        private static string FormatDuration(int seconds) { return seconds <= 0 ? "" : TimeSpan.FromSeconds(seconds).ToString(@"m\:ss"); }
    }
}