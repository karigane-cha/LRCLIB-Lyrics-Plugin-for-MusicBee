using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class CandidatePickerDialog : Form
    {
        private readonly ListView candidates = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
        private readonly Button select = new Button { Enabled = false, AutoSize = true };
        private readonly RichTextBox preview = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle };
        private readonly LocalizedStrings strings;
        public LyricsResult SelectedCandidate { get; private set; }

        private CandidatePickerDialog(TrackMetadata track, IEnumerable<LyricsResult> results, PluginLanguageMode language, PopupTheme theme)
        {
            strings = PluginLocalization.Get(language);
            Text = strings.CandidateDialogTitle;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 480);
            Size = new Size(900, 600);
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
                Text = strings.ReviewCandidates(track.Artist, track.Title)
            };
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Size = new Size(880, 450),
                Panel1MinSize = 170,
                Panel2MinSize = 150,
                SplitterDistance = 270
            };
            split.Panel1.Controls.Add(candidates);
            var previewHeading = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 4, 2, 6),
                Text = strings.LyricsPreview
            };
            preview.Text = strings.CandidatePreviewHint;
            split.Panel2.Controls.Add(preview);
            split.Panel2.Controls.Add(previewHeading);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
            var cancel = new Button { Text = strings.Cancel, DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(cancel); buttons.Controls.Add(select);
            Controls.Add(split); Controls.Add(buttons); Controls.Add(heading);
            AcceptButton = select; CancelButton = cancel;
            candidates.SelectedIndexChanged += (sender, args) => UpdateSelection();
            select.Click += (sender, args) => SelectCandidate();
            if (candidates.Items.Count == 1)
            {
                candidates.Items[0].Selected = true;
                candidates.Items[0].Focused = true;
                UpdateSelection();
            }
            ThemeHelper.Apply(this, theme);
        }

        public static Task<LyricsResult> ShowAsync(TrackMetadata track, IList<LyricsResult> results, PluginLanguageMode language, PopupTheme theme)
        {
            var completion = new TaskCompletionSource<LyricsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            SelectedCandidate = GetSelectedCandidate();
            if (SelectedCandidate == null) return;
            DialogResult = DialogResult.OK;
        }

        private void UpdateSelection()
        {
            var selected = GetSelectedCandidate();
            if (selected == null)
            {
                select.Enabled = false;
                preview.Text = strings.CandidatePreviewHint;
                return;
            }

            select.Enabled = true;
            preview.Text = selected.Lyrics ?? string.Empty;
        }

        private LyricsResult GetSelectedCandidate()
        {
            if (candidates.SelectedItems.Count == 1)
                return candidates.SelectedItems[0].Tag as LyricsResult;
            return candidates.Items.Count == 1 ? candidates.Items[0].Tag as LyricsResult : null;
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return string.Empty;

            var roundedSeconds = Math.Round(seconds, MidpointRounding.AwayFromZero);
            var totalSeconds = roundedSeconds >= long.MaxValue ? long.MaxValue : (long)roundedSeconds;
            var totalMinutes = totalSeconds / 60;
            var remainingSeconds = totalSeconds % 60;
            return totalMinutes.ToString(CultureInfo.InvariantCulture) + ":" + remainingSeconds.ToString("D2", CultureInfo.InvariantCulture);
        }
    }
}
