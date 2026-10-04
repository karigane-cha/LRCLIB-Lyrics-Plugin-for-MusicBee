using System;
using System.Globalization;

namespace MusicBeePlugin
{
    public enum PluginLanguageMode
    {
        Automatic = 0,
        English = 1,
        Japanese = 2
    }

    internal sealed class LocalizedStrings
    {
        private readonly bool japanese;

        public LocalizedStrings(PluginLanguageMode language)
        {
            japanese = language == PluginLanguageMode.Japanese
                || (language == PluginLanguageMode.Automatic && IsJapaneseCulture());
        }

        public string PluginDescription => japanese
            ? "再生中の曲の歌詞をLRCLIBから取得し、.lrcまたは.txtで保存します。"
            : "Fetches lyrics from LRCLIB and saves them as .lrc or .txt.";
        public string PluginInfoButton => japanese ? "プラグイン情報" : "Plugin info";
        public string PluginSettingsButton => japanese ? "プラグイン設定" : "Plugin settings";
        public string AboutTitle => japanese ? "情報" : "Information";
        public string AboutDescription
        {
            get
            {
                var version = PluginVersionInfo.UserAgentVersion;
                return japanese
                    ? "LRCLIB Lyrics (" + version + ")\r\n"
                        + "karigane-cha\r\n"
                        + "LRCLIB から歌詞を取得し、曲と同じフォルダーに\r\n"
                        + ".lrc または .txt として保存する MusicBee プラグインです。"
                    : "LRCLIB Lyrics (" + version + ")\r\n"
                        + "karigane-cha\r\n"
                        + "This is a MusicBee plugin that retrieves lyrics from LRCLIB\r\n"
                        + "and saves them in the same folder as the song.";
            }
        }
        public string Ok => "OK";
        public string SettingsTitle => japanese ? "LRCLIB Lyrics - プラグイン設定" : "LRCLIB Lyrics - Plugin settings";
        public string Description => japanese
            ? "LRCLIB から現在再生中の曲の歌詞を取得します。同期歌詞は .lrc、非同期歌詞は .txt として曲と同じフォルダーに保存します。"
            : "Fetches lyrics for the current track from LRCLIB. Synced lyrics are saved as .lrc and unsynced lyrics as .txt in the track folder.";
        public string SyncedOnly => japanese ? "同期歌詞のみを取得する" : "Fetch synced lyrics only";
        public string LyricsCondition => japanese ? "歌詞を取得する条件" : "When to fetch lyrics";
        public string[] LyricsConditions => japanese
            ? new[]
            {
                "同名の .lrc ファイルがない場合",
                "同名の .lrc または .txt ファイルがない場合",
                "同名の .lrc／.txt と埋め込み歌詞がない場合"
            }
            : new[]
            {
                "When no same-name .lrc file exists",
                "When no same-name .lrc or .txt file exists",
                "When no same-name .lrc/.txt or embedded lyrics exist"
            };
        public string EmbeddedLyricsHandling => japanese ? "埋め込み歌詞の処理" : "Embedded lyrics handling";
        public string[] EmbeddedLyricsOptions => japanese ? new[] { "無視", "削除", "上書き" } : new[] { "Ignore", "Delete", "Overwrite" };
        public string CandidatePicker => japanese ? "候補が複数ある場合は選択画面を表示する" : "Show a selection dialog when multiple candidates are found";
        public string Overwrite => japanese ? "既存の歌詞ファイル（.lrc / .txt）を上書きする" : "Overwrite existing lyric files (.lrc / .txt)";
        public string PopupTheme => japanese ? "プラグイン設定ポップアップのテーマ" : "Plugin settings popup theme";
        public string[] PopupThemes => japanese
            ? new[] { "Windows の設定に同期", "ライトモードを使用", "ダークモードを使用" }
            : new[] { "Follow Windows settings", "Use light mode", "Use dark mode" };
        public string Language => japanese ? "プラグインの言語" : "Plugin language";
        public string[] Languages => japanese ? new[] { "自動", "英語", "日本語" } : new[] { "Automatic", "English", "Japanese" };
        public string Timeout => japanese ? "通信タイムアウト（秒）" : "Request timeout (seconds)";
        public string Save => japanese ? "保存" : "Save";
        public string ResetDefaults => japanese ? "デフォルトに戻す" : "Reset to defaults";
        public string ResetConfirmation => japanese ? "すべての設定をデフォルトに戻しますか？" : "Reset all settings to their defaults?";
        public string Saved => japanese ? "保存しました" : "Saved";
        public string CandidateDialogTitle => japanese ? "LRCLIB Lyrics - 歌詞候補の選択" : "LRCLIB Lyrics - Select lyrics";
        public string CandidateTitle => japanese ? "曲名" : "Title";
        public string CandidateArtist => japanese ? "アーティスト" : "Artist";
        public string CandidateAlbum => japanese ? "アルバム" : "Album";
        public string CandidateDuration => japanese ? "長さ" : "Duration";
        public string CandidateType => japanese ? "種類" : "Type";
        public string Synced => japanese ? "同期歌詞" : "Synced";
        public string Unsynced => japanese ? "非同期歌詞" : "Unsynced";
        public string LyricsPreview => japanese ? "歌詞プレビュー" : "Lyrics preview";
        public string CandidatePreviewHint => japanese ? "候補を選択すると歌詞を表示します。" : "Select a candidate to preview its lyrics.";
        public string SelectLyrics => japanese ? "この歌詞を保存" : "Save this lyric";
        public string Cancel => japanese ? "キャンセル" : "Cancel";
        public string SelectCandidate(string artist, string title) => japanese
            ? "「" + artist + " - " + title + "」の候補を選択してください。"
            : "Select a lyric for \"" + artist + " - " + title + "\".";

        public string SettingsSaved => japanese ? "LRCLIB Lyrics の設定を保存しました。" : "LRCLIB Lyrics settings saved.";
        public string RetrieveRequestFailed(string message) => japanese
            ? "MusicBee からの歌詞取得要求に失敗しました: " + message
            : "MusicBee lyric request failed: " + message;
        public string LyricsNotFoundOrCanceled(string artist, string title) => japanese
            ? "LRCLIB に歌詞が見つからなかったか、候補の選択をキャンセルしました: " + artist + " - " + title
            : "No lyrics were found in LRCLIB, or candidate selection was canceled: " + artist + " - " + title;
        public string SearchTagsMissing => japanese
            ? "歌詞を検索できません: タイトルまたはアーティストタグが空です。"
            : "Cannot search for lyrics: the title or artist tag is empty.";
        public string ExistingFileSkipped(string path) => japanese
            ? "既存の歌詞ファイルを上書きしない設定のため保存をスキップしました: " + path
            : "Skipped saving because overwriting existing lyric files is disabled: " + path;
        public string LyricsSaved(string path) => japanese ? "歌詞を保存しました: " + path : "Lyrics saved: " + path;
        public string LyricsFetchFailed(string path) => japanese
            ? "LRCLIB の歌詞取得に失敗しました: " + path
            : "Failed to retrieve lyrics from LRCLIB: " + path;
        public string EmbeddedDeleteFailed(string path) => japanese
            ? "埋め込み歌詞を削除できなかったため、歌詞の取得を中止しました: " + path
            : "Lyrics retrieval canceled because embedded lyrics could not be removed: " + path;
        public string EmbeddedDeleted(string path) => japanese ? "埋め込み歌詞を削除しました: " + path : "Embedded lyrics deleted: " + path;
        public string EmbeddedSaveFailed(string path) => japanese
            ? "取得した歌詞を埋め込み歌詞として保存できませんでした: " + path
            : "Could not save the retrieved lyrics as embedded lyrics: " + path;
        private static bool IsJapaneseCulture()
        {
            try
            {
                return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(CultureInfo.CurrentCulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }

    internal static class PluginLocalization
    {
        public static LocalizedStrings Get(PluginLanguageMode language)
        {
            return new LocalizedStrings(language);
        }
    }
}
