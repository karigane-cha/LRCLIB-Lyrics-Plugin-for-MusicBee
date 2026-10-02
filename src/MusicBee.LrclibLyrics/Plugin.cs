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

        // MusicBee looks for this exact entry point name (British spelling).
        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            api = new MusicBeeApiInterface();
            api.Initialise(apiInterfacePtr);
            var storage = api.Setting_GetPersistentStoragePath();
            settingsPath = Path.Combine(string.IsNullOrWhiteSpace(storage) ? AppDomain.CurrentDomain.BaseDirectory : storage, "LrclibLyrics.settings.json");
            settings = PluginSettings.Load(settingsPath);
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

                var result = GetLyricsForTrackAsync(track, true).GetAwaiter().GetResult();
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
                var result = await GetLyricsForTrackAsync(track, true).ConfigureAwait(false);
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

        private async Task<LyricsResult> GetLyricsForTrackAsync(TrackMetadata track, bool allowCandidatePicker)
        {
            if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).SearchTagsMissing);
                return null;
            }

            var searchResponse = await lrclib.SearchAsync(track, settings.SyncedLyricsOnly, settings.RequestTimeoutSeconds).ConfigureAwait(false);
            return await ChooseLyricsAsync(track, searchResponse, allowCandidatePicker).ConfigureAwait(false);
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

        private async Task<LyricsResult> ChooseLyricsAsync(TrackMetadata track, LyricsSearchResponse searchResponse, bool allowCandidatePicker)
        {
            if (searchResponse == null || searchResponse.Candidates.Count == 0) return null;
            if (searchResponse.IsDirectMatch || searchResponse.Candidates.Count == 1 || !allowCandidatePicker || !settings.ShowCandidatePickerWhenMultiple)
                return searchResponse.Candidates[0];
            return await CandidatePickerDialog.ShowAsync(track, searchResponse.Candidates, settings.LanguageMode, settings.PopupTheme).ConfigureAwait(false);
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

        private void SaveSettings(PluginSettings value) { value.Save(settingsPath); Trace(PluginLocalization.Get(value.LanguageMode).SettingsSaved); }
        private void Trace(string message) { try { api.MB_Trace("[LRCLIB Lyrics] " + message); } catch { } }
    }
}
