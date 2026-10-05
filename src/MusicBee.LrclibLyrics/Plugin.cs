using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private const string ProviderName = "LRCLIB";
        private MusicBeeApiInterface api;
        private PluginSettings settings;
        private string settingsPath;
        private readonly LrclibClient lrclib = new LrclibClient();
        private readonly ConcurrentDictionary<string, byte> activeDownloads = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private ToolStripItem manualSearchMenuItem;

        private enum LyricsRequestMode
        {
            Automatic,
            ManualResearch
        }

        private sealed class LyricsRequestOutcome
        {
            public readonly LyricsResult Result;
            public readonly bool HasCandidates;
            public readonly bool SearchSucceeded;

            public LyricsRequestOutcome(LyricsResult result, bool hasCandidates, bool searchSucceeded)
            {
                Result = result;
                HasCandidates = hasCandidates;
                SearchSucceeded = searchSucceeded;
            }
        }

        // MusicBee looks for this exact entry point name (British spelling).
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            api = new MusicBeeApiInterface();
            api.Initialise(apiInterfacePtr);
            var storage = api.Setting_GetPersistentStoragePath();
            settingsPath = Path.Combine(string.IsNullOrWhiteSpace(storage) ? AppDomain.CurrentDomain.BaseDirectory : storage, "LrclibLyrics.settings.json");
            settings = PluginSettings.Load(settingsPath);
            RegisterManualSearchMenuItem();
            var strings = PluginLocalization.Get(settings.LanguageMode);
            var version = PluginVersionInfo.AssemblyVersion;
            return new PluginInfo
            {
                PluginInfoVersion = PluginInfoVersion,
                Type = PluginType.LyricsRetrieval,
                Name = "LRCLIB Lyrics",
                Description = strings.PluginDescription,
                Author = "karigane-cha",
                TargetApplication = "",
                VersionMajor = checked((short)version.Major),
                VersionMinor = checked((short)version.Minor),
                Revision = checked((short)version.Build),
                MinInterfaceVersion = MinInterfaceVersion,
                MinApiRevision = MinApiRevision,
                ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents,
                ConfigurationPanelHeight = 48
            };
        }

        public bool Configure(IntPtr panelHandle)
        {
            var panel = Control.FromHandle(panelHandle) as Panel;
            if (panel == null) return false;
            panel.Controls.Clear();
            panel.Controls.Add(new PluginConfigurationControl(settings, SaveSettings, panel.FindForm()));
            return true;
        }

        public string[] GetProviders()
        {
            return new[] { ProviderName };
        }

        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred, string provider)
        {
            if (!string.Equals(provider, ProviderName, StringComparison.OrdinalIgnoreCase)) return null;

            try
            {
                var file = ResolveFile(sourceFileUrl);
                var track = new TrackMetadata
                {
                    Path = file,
                    Title = string.IsNullOrWhiteSpace(trackTitle) && IsLocalFile(file) ? api.Library_GetFileTag(file, MetaDataType.TrackTitle) : trackTitle,
                    Artist = string.IsNullOrWhiteSpace(artist) && IsLocalFile(file) ? api.Library_GetFileTag(file, MetaDataType.Artist) : artist,
                    Album = string.IsNullOrWhiteSpace(album) && IsLocalFile(file) ? api.Library_GetFileTag(file, MetaDataType.Album) : album,
                    DurationSeconds = Math.Max(0, api.NowPlaying_GetDuration() / 1000)
                };

                var result = GetLyricsForTrackAsync(track, LyricsRequestMode.Automatic).GetAwaiter().GetResult().Result;
                if (result == null) return null;
                SaveLyricsFile(file, result);
                return result.Lyrics;
            }
            catch (Exception ex)
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).RetrieveRequestFailed(ex.Message));
                return null;
            }
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (type == NotificationType.TrackChanged)
            {
                var file = ResolveFile(sourceFileUrl);
                _ = DownloadForTrackAsync(file);
            }
        }

        private async Task DownloadForTrackAsync(string file)
        {
            if (!IsLocalFile(file) || !activeDownloads.TryAdd(file, 0)) return;
            try
            {
                if (!ShouldSearchLocalFile(file)) return;
                var track = ReadTrack(file);
                var result = (await GetLyricsForTrackAsync(track, LyricsRequestMode.Automatic).ConfigureAwait(false)).Result;
                if (result == null)
                {
                    Trace(PluginLocalization.Get(settings.LanguageMode).LyricsNotFoundOrCanceled(track.Artist, track.Title));
                    return;
                }

                SaveLyricsFile(file, result);
            }
            catch (Exception ex)
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).LyricsFetchFailed(ex.Message));
            }
            finally { byte ignored; activeDownloads.TryRemove(file, out ignored); }
        }

        private bool ShouldSearchLocalFile(string file)
        {
            var lrcPath = Path.ChangeExtension(file, ".lrc");
            var textPath = Path.ChangeExtension(file, ".txt");
            if (ShouldSkipTrackWithLocalLyrics(lrcPath, textPath)) return false;
            if (settings.EmbeddedLyricsHandling == EmbeddedLyricsHandling.Delete && !RemoveEmbeddedLyrics(file)) return false;
            return settings.ExistingLyricsSkipMode != ExistingLyricsSkipMode.LrcOrTextOrEmbeddedLyrics ||
                settings.EmbeddedLyricsHandling != EmbeddedLyricsHandling.Ignore ||
                !HasEmbeddedLyrics(file);
        }

        private async Task<LyricsRequestOutcome> GetLyricsForTrackAsync(TrackMetadata track, LyricsRequestMode requestMode)
        {
            if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).SearchTagsMissing);
                return new LyricsRequestOutcome(null, false, false);
            }

            var searchResponse = await lrclib.SearchAsync(track, settings.SyncedLyricsOnly, settings.RequestTimeoutSeconds).ConfigureAwait(false);
            if (searchResponse == null || searchResponse.Candidates.Count == 0)
                return new LyricsRequestOutcome(null, false, searchResponse != null && searchResponse.WasSearchSuccessful);

            var result = await ChooseLyricsAsync(track, searchResponse, requestMode).ConfigureAwait(false);
            return new LyricsRequestOutcome(result, true, true);
        }

        private void SaveLyricsFile(string file, LyricsResult result)
        {
            if (!IsLocalFile(file) || result == null) return;

            var outputPath = result.IsSynced ? Path.ChangeExtension(file, ".lrc") : Path.ChangeExtension(file, ".txt");
            if (File.Exists(outputPath) && !settings.OverwriteExistingLrcFile)
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).ExistingFileSkipped(outputPath));
                return;
            }

            File.WriteAllText(outputPath, result.Lyrics, new UTF8Encoding(false));
            if (settings.EmbeddedLyricsHandling == EmbeddedLyricsHandling.Overwrite)
                OverwriteEmbeddedLyrics(file, result.Lyrics);
            Trace(PluginLocalization.Get(settings.LanguageMode).LyricsSaved(outputPath));
            NotifyLyricsDownloaded();
        }

        private bool ShouldSkipTrackWithLocalLyrics(string lrcPath, string textPath)
        {
            if (File.Exists(lrcPath)) return true;
            if (settings.ExistingLyricsSkipMode == ExistingLyricsSkipMode.LrcFile) return false;
            if (File.Exists(textPath)) return true;
            if (settings.ExistingLyricsSkipMode == ExistingLyricsSkipMode.LrcOrTextFile) return false;
            return false;
        }

        private async Task<LyricsResult> ChooseLyricsAsync(TrackMetadata track, LyricsSearchResponse searchResponse, LyricsRequestMode requestMode)
        {
            if (searchResponse == null || searchResponse.Candidates.Count == 0) return null;
            if (!ShouldShowCandidateDialog(settings.CandidateDialogMode, requestMode, searchResponse.IsDirectMatch, searchResponse.Candidates.Count))
                return searchResponse.Candidates[0];

            return await CandidatePickerDialog.ShowAsync(track, searchResponse.Candidates, settings.LanguageMode, settings.PopupTheme).ConfigureAwait(false);
        }

        private static bool ShouldShowCandidateDialog(CandidateDialogMode candidateDialogMode, LyricsRequestMode requestMode, bool isDirectMatch, int candidateCount)
        {
            if (candidateCount <= 0) return false;
            if (requestMode == LyricsRequestMode.ManualResearch) return true;
            if (candidateDialogMode == CandidateDialogMode.Never) return false;
            if (candidateDialogMode == CandidateDialogMode.Always) return true;
            return !isDirectMatch && candidateCount > 1;
        }

        private void RegisterManualSearchMenuItem()
        {
            try
            {
                if (api.MB_AddMenuItem == null) return;
                var strings = PluginLocalization.Get(settings.LanguageMode);
                manualSearchMenuItem = api.MB_AddMenuItem("mnuTools/" + strings.ManualSearch, strings.ManualSearch, SearchCurrentTrackAgain);
            }
            catch (Exception ex)
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).ManualSearchFailed(ex.Message));
            }
        }

        private async void SearchCurrentTrackAgain(object sender, EventArgs e)
        {
            try { await SearchCurrentTrackAgainAsync().ConfigureAwait(false); }
            catch (Exception ex) { Trace(PluginLocalization.Get(settings.LanguageMode).ManualSearchFailed(ex.Message)); }
        }

        private async Task SearchCurrentTrackAgainAsync()
        {
            var strings = PluginLocalization.Get(settings.LanguageMode);
            var file = ResolveFile(null);
            if (!IsLocalFile(file))
            {
                await PluginMessageDialog.ShowInformationAsync(strings.NoCurrentLocalTrack, strings.MessageTitle, strings.Ok, settings.PopupTheme).ConfigureAwait(false);
                return;
            }

            if (!activeDownloads.TryAdd(file, 0))
            {
                await PluginMessageDialog.ShowInformationAsync(strings.ManualSearchAlreadyRunning, strings.MessageTitle, strings.Ok, settings.PopupTheme).ConfigureAwait(false);
                return;
            }

            try
            {
                var track = ReadTrack(file);
                if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
                {
                    Trace(strings.SearchTagsMissing);
                    await PluginMessageDialog.ShowInformationAsync(strings.SearchTagsMissing, strings.MessageTitle, strings.Ok, settings.PopupTheme).ConfigureAwait(false);
                    return;
                }

                var outcome = await GetLyricsForTrackAsync(track, LyricsRequestMode.ManualResearch).ConfigureAwait(false);
                if (!outcome.HasCandidates)
                {
                    if (!outcome.SearchSucceeded)
                    {
                        Trace(strings.LrclibSearchUnavailable);
                        return;
                    }
                    Trace(strings.NoLyricsFound);
                    await PluginMessageDialog.ShowInformationAsync(strings.NoLyricsFound, strings.MessageTitle, strings.Ok, settings.PopupTheme).ConfigureAwait(false);
                    return;
                }
                if (outcome.Result == null) return;

                await SaveManualLyricsFileAsync(file, outcome.Result).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace(strings.ManualSearchFailed(ex.Message));
            }
            finally
            {
                byte ignored;
                activeDownloads.TryRemove(file, out ignored);
            }
        }

        private async Task SaveManualLyricsFileAsync(string file, LyricsResult result)
        {
            if (!IsLocalFile(file) || result == null) return;

            var strings = PluginLocalization.Get(settings.LanguageMode);
            var outputPath = result.IsSynced ? Path.ChangeExtension(file, ".lrc") : Path.ChangeExtension(file, ".txt");
            var outputAlreadyExists = File.Exists(outputPath);
            if (outputAlreadyExists)
            {
                var replace = await PluginMessageDialog.ShowConfirmationAsync(
                    strings.ExistingFileReplaceConfirmation,
                    strings.MessageTitle,
                    strings.ReplaceExistingFile,
                    strings.Cancel,
                    settings.PopupTheme).ConfigureAwait(false);
                if (!replace) return;
            }

            WriteLyricsFileSafely(outputPath, result.Lyrics, outputAlreadyExists);
            if (settings.EmbeddedLyricsHandling == EmbeddedLyricsHandling.Delete)
                RemoveEmbeddedLyrics(file);
            else if (settings.EmbeddedLyricsHandling == EmbeddedLyricsHandling.Overwrite)
                OverwriteEmbeddedLyrics(file, result.Lyrics);

            Trace(strings.LyricsSaved(outputPath));
            NotifyLyricsDownloaded();
        }

        private static void WriteLyricsFileSafely(string outputPath, string lyrics, bool replaceExisting)
        {
            var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, lyrics, new UTF8Encoding(false));
                if (File.Exists(outputPath))
                {
                    if (!replaceExisting)
                        throw new IOException("The destination lyric file appeared after the save confirmation was skipped.");
                    File.Replace(temporaryPath, outputPath, null);
                }
                else
                    File.Move(temporaryPath, outputPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private bool HasEmbeddedLyrics(string file)
        {
            return !string.IsNullOrWhiteSpace(api.Library_GetFileTag(file, MetaDataType.Lyrics));
        }

        private bool RemoveEmbeddedLyrics(string file)
        {
            var embeddedLyrics = api.Library_GetFileTag(file, MetaDataType.Lyrics);
            if (string.IsNullOrWhiteSpace(embeddedLyrics)) return true;

            if (!api.Library_SetFileTag(file, MetaDataType.Lyrics, string.Empty) || !api.Library_CommitTagsToFile(file))
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).EmbeddedDeleteFailed(file));
                return false;
            }
            Trace(PluginLocalization.Get(settings.LanguageMode).EmbeddedDeleted(file));
            return true;
        }

        private void OverwriteEmbeddedLyrics(string file, string lyrics)
        {
            if (!api.Library_SetFileTag(file, MetaDataType.Lyrics, lyrics) || !api.Library_CommitTagsToFile(file))
                Trace(PluginLocalization.Get(settings.LanguageMode).EmbeddedSaveFailed(file));
        }

        private TrackMetadata ReadTrack(string file)
        {
            return new TrackMetadata
            {
                Path = file,
                Title = api.Library_GetFileTag(file, MetaDataType.TrackTitle),
                Artist = api.Library_GetFileTag(file, MetaDataType.Artist),
                Album = api.Library_GetFileTag(file, MetaDataType.Album),
                DurationSeconds = Math.Max(0, api.NowPlaying_GetDuration() / 1000)
            };
        }

        private string ResolveFile(string sourceFileUrl)
        {
            return string.IsNullOrWhiteSpace(sourceFileUrl) ? api.NowPlaying_GetFileUrl() : sourceFileUrl;
        }

        private static bool IsLocalFile(string file)
        {
            return !string.IsNullOrWhiteSpace(file) && File.Exists(file);
        }

        private void NotifyLyricsDownloaded()
        {
            try { api.MB_SendNotification(CallbackType.LyricsDownloaded); } catch { }
            try { api.MB_RefreshPanels(); } catch { }
        }

        private void SaveSettings(PluginSettings value)
        {
            value.Save(settingsPath);
            if (manualSearchMenuItem != null)
            {
                var strings = PluginLocalization.Get(value.LanguageMode);
                manualSearchMenuItem.Text = strings.ManualSearch;
            }
            Trace(PluginLocalization.Get(value.LanguageMode).SettingsSaved);
        }
        private void Trace(string message) { try { api.MB_Trace("[LRCLIB Lyrics] " + message); } catch { } }
    }
}
