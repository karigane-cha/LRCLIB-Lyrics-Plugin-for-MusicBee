using System;
using System.Collections.Generic;
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
        private readonly object operationsLock = new object();
        private readonly Dictionary<string, LyricsOperation> activeOperations = new Dictionary<string, LyricsOperation>(StringComparer.OrdinalIgnoreCase);
        private ToolStripItem manualSearchMenuItem;

        private enum LyricsRequestMode
        {
            Automatic,
            ManualResearch
        }

        private enum AutomaticRequestOrigin
        {
            TrackChanged,
            LyricsProvider
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

        private sealed class LyricsOperation
        {
            public readonly LyricsRequestMode RequestMode;
            public readonly AutomaticRequestOrigin Origin;
            public readonly TaskCompletionSource<LyricsOperationResult> Completion = new TaskCompletionSource<LyricsOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<LyricsOperationResult> Task { get { return Completion.Task; } }

            public LyricsOperation(LyricsRequestMode requestMode, AutomaticRequestOrigin origin)
            {
                RequestMode = requestMode;
                Origin = origin;
            }
        }

        private sealed class LyricsOperationResult
        {
            public readonly LyricsResult Lyrics;
            public readonly bool Saved;
            public readonly bool Canceled;
            public readonly Exception Error;

            public LyricsOperationResult(LyricsResult lyrics, bool saved, bool canceled, Exception error)
            {
                Lyrics = lyrics;
                Saved = saved;
                Canceled = canceled;
                Error = error;
            }

            public static LyricsOperationResult FromSearch(LyricsRequestOutcome outcome, bool saved, bool canceled)
            {
                return new LyricsOperationResult(outcome == null ? null : outcome.Result, saved, canceled, null);
            }

            public static LyricsOperationResult Failed(Exception error)
            {
                return new LyricsOperationResult(null, false, false, error);
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

                var operation = GetOrStartAutomaticOperation(file, track, false, AutomaticRequestOrigin.LyricsProvider);
                var result = operation.Task.GetAwaiter().GetResult();
                return result.Error == null && !result.Canceled && result.Lyrics != null &&
                    (operation.RequestMode != LyricsRequestMode.ManualResearch || result.Saved)
                    ? result.Lyrics.Lyrics
                    : null;
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
            if (!IsLocalFile(file)) return;
            try
            {
                var operation = GetOrStartAutomaticOperation(file, null, true, AutomaticRequestOrigin.TrackChanged);
                if (operation != null) await operation.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).LyricsFetchFailed(ex.Message));
            }
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

        private LyricsOperation GetOrStartAutomaticOperation(string file, TrackMetadata track, bool fromTrackChanged, AutomaticRequestOrigin origin)
        {
            var key = GetOperationKey(file);
            LyricsOperation operation;
            var start = false;
            var joined = false;

            lock (operationsLock)
            {
                if (key != null && activeOperations.TryGetValue(key, out operation))
                {
                    if (fromTrackChanged && operation.RequestMode == LyricsRequestMode.ManualResearch)
                        return null;
                    joined = true;
                }
                else
                {
                    if (fromTrackChanged && (!IsLocalFile(file) || !ShouldSearchLocalFile(file)))
                        return null;

                    operation = new LyricsOperation(LyricsRequestMode.Automatic, origin);
                    if (key != null) activeOperations.Add(key, operation);
                    start = true;
                }
            }

            if (joined)
            {
                Trace(operation.RequestMode == LyricsRequestMode.Automatic
                    ? "Automatic request joined an active operation."
                    : "Automatic request joined an active manual operation.");
                return operation;
            }

            if (start)
            {
                Trace("Automatic operation started.");
                StartOperation(key, operation, async () =>
                {
                    var operationTrack = track ?? ReadTrack(file);
                    var outcome = await GetLyricsForTrackAsync(operationTrack, LyricsRequestMode.Automatic).ConfigureAwait(false);
                    if (outcome.Result == null)
                    {
                        if (origin == AutomaticRequestOrigin.TrackChanged)
                            Trace(PluginLocalization.Get(settings.LanguageMode).LyricsNotFoundOrCanceled(operationTrack.Artist, operationTrack.Title));
                        return LyricsOperationResult.FromSearch(outcome, false, outcome.HasCandidates);
                    }

                    var saved = SaveLyricsFile(file, outcome.Result);
                    return LyricsOperationResult.FromSearch(outcome, saved, false);
                });
            }

            return operation;
        }

        private LyricsOperation TryStartManualOperation(string file)
        {
            var key = GetOperationKey(file);
            if (key == null) return null;

            LyricsOperation operation;
            lock (operationsLock)
            {
                if (activeOperations.ContainsKey(key)) return null;
                operation = new LyricsOperation(LyricsRequestMode.ManualResearch, AutomaticRequestOrigin.LyricsProvider);
                activeOperations.Add(key, operation);
            }

            Trace("Manual research operation started.");
            StartOperation(key, operation, () => RunManualOperationAsync(file));
            return operation;
        }

        private void StartOperation(string key, LyricsOperation operation, Func<Task<LyricsOperationResult>> work)
        {
            _ = CompleteOperationAsync(key, operation, work);
        }

        private async Task CompleteOperationAsync(string key, LyricsOperation operation, Func<Task<LyricsOperationResult>> work)
        {
            var result = new LyricsOperationResult(null, false, false, null);
            try
            {
                result = await work().ConfigureAwait(false);
                if (result == null) result = new LyricsOperationResult(null, false, false, null);
            }
            catch (Exception ex)
            {
                result = LyricsOperationResult.Failed(ex);
                var strings = PluginLocalization.Get(settings.LanguageMode);
                var errorMessage = operation.RequestMode == LyricsRequestMode.ManualResearch
                    ? strings.ManualSearchFailed(ex.Message)
                    : operation.Origin == AutomaticRequestOrigin.TrackChanged
                        ? strings.LyricsFetchFailed(ex.Message)
                        : strings.RetrieveRequestFailed(ex.Message);
                Trace(errorMessage);
            }
            finally
            {
                if (key != null)
                {
                    lock (operationsLock)
                    {
                        LyricsOperation current;
                        if (activeOperations.TryGetValue(key, out current) && ReferenceEquals(current, operation))
                            activeOperations.Remove(key);
                    }
                }

                operation.Completion.TrySetResult(result);
                Trace(operation.RequestMode == LyricsRequestMode.ManualResearch
                    ? "Manual research operation completed."
                    : "Automatic operation completed.");
            }
        }

        private string GetOperationKey(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            try
            {
                Uri uri;
                var isWindowsPath = (file.Length >= 2 && char.IsLetter(file[0]) && file[1] == ':') ||
                    file.StartsWith("\\\\", StringComparison.Ordinal) || file.StartsWith("//", StringComparison.Ordinal);
                if (!isWindowsPath && Uri.TryCreate(file, UriKind.Absolute, out uri)) return null;
                return Path.GetFullPath(file);
            }
            catch { return null; }
        }

        private async Task<LyricsOperationResult> RunManualOperationAsync(string file)
        {
            var strings = PluginLocalization.Get(settings.LanguageMode);
            var track = ReadTrack(file);
            if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            {
                Trace(strings.SearchTagsMissing);
                await PluginMessageDialog.ShowInformationAsync(strings.SearchTagsMissing, strings.MessageTitle, strings.Ok, settings.PopupTheme).ConfigureAwait(false);
                return new LyricsOperationResult(null, false, false, null);
            }

            var outcome = await GetLyricsForTrackAsync(track, LyricsRequestMode.ManualResearch).ConfigureAwait(false);
            if (!outcome.HasCandidates)
            {
                if (!outcome.SearchSucceeded)
                {
                    Trace(strings.LrclibSearchUnavailable);
                    return LyricsOperationResult.FromSearch(outcome, false, false);
                }
                Trace(strings.NoLyricsFound);
                await PluginMessageDialog.ShowInformationAsync(strings.NoLyricsFound, strings.MessageTitle, strings.Ok, settings.PopupTheme).ConfigureAwait(false);
                return LyricsOperationResult.FromSearch(outcome, false, false);
            }
            if (outcome.Result == null) return LyricsOperationResult.FromSearch(outcome, false, true);

            var saved = await SaveManualLyricsFileAsync(file, outcome.Result).ConfigureAwait(false);
            return LyricsOperationResult.FromSearch(outcome, saved, false);
        }

        private bool SaveLyricsFile(string file, LyricsResult result)
        {
            if (!IsLocalFile(file) || result == null) return false;

            var outputPath = result.IsSynced ? Path.ChangeExtension(file, ".lrc") : Path.ChangeExtension(file, ".txt");
            if (File.Exists(outputPath) && !settings.OverwriteExistingLrcFile)
            {
                Trace(PluginLocalization.Get(settings.LanguageMode).ExistingFileSkipped(outputPath));
                return false;
            }

            File.WriteAllText(outputPath, result.Lyrics, new UTF8Encoding(false));
            if (settings.EmbeddedLyricsHandling == EmbeddedLyricsHandling.Overwrite)
                OverwriteEmbeddedLyrics(file, result.Lyrics);
            Trace(PluginLocalization.Get(settings.LanguageMode).LyricsSaved(outputPath));
            NotifyLyricsDownloaded();
            return true;
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

            var operation = TryStartManualOperation(file);
            if (operation == null)
            {
                Trace("Manual research operation rejected because another operation is active.");
                await PluginMessageDialog.ShowInformationAsync(strings.ManualSearchAlreadyRunning, strings.MessageTitle, strings.Ok, settings.PopupTheme).ConfigureAwait(false);
                return;
            }

            await operation.Task.ConfigureAwait(false);
        }

        private async Task<bool> SaveManualLyricsFileAsync(string file, LyricsResult result)
        {
            if (!IsLocalFile(file) || result == null) return false;

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
                if (!replace) return false;
            }

            WriteLyricsFileSafely(outputPath, result.Lyrics, outputAlreadyExists);
            if (settings.EmbeddedLyricsHandling == EmbeddedLyricsHandling.Delete)
                RemoveEmbeddedLyrics(file);
            else if (settings.EmbeddedLyricsHandling == EmbeddedLyricsHandling.Overwrite)
                OverwriteEmbeddedLyrics(file, result.Lyrics);

            Trace(strings.LyricsSaved(outputPath));
            NotifyLyricsDownloaded();
            return true;
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
