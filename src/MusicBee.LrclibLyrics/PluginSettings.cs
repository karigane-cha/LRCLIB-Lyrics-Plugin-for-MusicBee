using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace MusicBeePlugin
{
    public enum ExistingLyricsSkipMode
    {
        LrcFile = 0,
        LrcOrTextFile = 1,
        LrcOrTextOrEmbeddedLyrics = 2
    }

    public enum EmbeddedLyricsHandling
    {
        Ignore = 0,
        Delete = 1,
        Overwrite = 2
    }

    public enum PopupTheme
    {
        Windows = 0,
        Light = 1,
        Dark = 2
    }

    public sealed class PluginSettings
    {
        private const int CurrentLanguageModeVersion = 2;

        public bool SyncedLyricsOnly { get; set; } = true;
        public ExistingLyricsSkipMode ExistingLyricsSkipMode { get; set; } = ExistingLyricsSkipMode.LrcOrTextOrEmbeddedLyrics;
        public EmbeddedLyricsHandling EmbeddedLyricsHandling { get; set; } = EmbeddedLyricsHandling.Ignore;
        public bool ShowCandidatePickerWhenMultiple { get; set; } = true;
        public bool OverwriteExistingLrcFile { get; set; }
        public PopupTheme PopupTheme { get; set; } = PopupTheme.Windows;
        public PluginLanguageMode LanguageMode { get; set; } = PluginLanguageMode.Automatic;
        public int LanguageModeVersion { get; set; } = CurrentLanguageModeVersion;
        public int RequestTimeoutSeconds { get; set; } = 12;

        public static PluginSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new PluginSettings();
                var json = File.ReadAllText(path);
                var serializer = new JavaScriptSerializer();
                var value = serializer.Deserialize<PluginSettings>(json);
                if (value == null) return new PluginSettings();
                var savedValues = serializer.Deserialize<Dictionary<string, object>>(json);
                if (!savedValues.ContainsKey("EmbeddedLyricsHandling"))
                {
                    value.EmbeddedLyricsHandling = LegacyBoolean(savedValues, "RemoveEmbeddedLyricsBeforeSearch")
                        ? EmbeddedLyricsHandling.Delete
                        : LegacyBoolean(savedValues, "EmbedLyricsInAudioFile") ? EmbeddedLyricsHandling.Overwrite : EmbeddedLyricsHandling.Ignore;
                }
                if (!Enum.IsDefined(typeof(ExistingLyricsSkipMode), value.ExistingLyricsSkipMode))
                    value.ExistingLyricsSkipMode = ExistingLyricsSkipMode.LrcOrTextOrEmbeddedLyrics;
                if (!Enum.IsDefined(typeof(EmbeddedLyricsHandling), value.EmbeddedLyricsHandling))
                    value.EmbeddedLyricsHandling = EmbeddedLyricsHandling.Ignore;
                if (!Enum.IsDefined(typeof(PopupTheme), value.PopupTheme))
                    value.PopupTheme = PopupTheme.Windows;

                if (!savedValues.ContainsKey("LanguageMode"))
                {
                    value.LanguageMode = PluginLanguageMode.Automatic;
                }
                else if (!savedValues.ContainsKey("LanguageModeVersion"))
                {
                    // The previous release used English=0, Japanese=1, Automatic=2.
                    value.LanguageMode = MigrateLegacyLanguageMode(savedValues["LanguageMode"]);
                }
                if (!Enum.IsDefined(typeof(PluginLanguageMode), value.LanguageMode))
                    value.LanguageMode = PluginLanguageMode.Automatic;
                value.LanguageModeVersion = CurrentLanguageModeVersion;
                value.RequestTimeoutSeconds = Math.Max(3, Math.Min(60, value.RequestTimeoutSeconds));
                return value;
            }
            catch
            {
                return new PluginSettings();
            }
        }

        public void Save(string path)
        {
            LanguageModeVersion = CurrentLanguageModeVersion;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(this));
        }

        private static bool LegacyBoolean(IDictionary<string, object> values, string key)
        {
            return values.ContainsKey(key) && values[key] != null && Convert.ToBoolean(values[key]);
        }

        private static PluginLanguageMode MigrateLegacyLanguageMode(object value)
        {
            try
            {
                switch (Convert.ToInt32(value))
                {
                    case 0: return PluginLanguageMode.English;
                    case 1: return PluginLanguageMode.Japanese;
                    case 2: return PluginLanguageMode.Automatic;
                    default: return PluginLanguageMode.Automatic;
                }
            }
            catch
            {
                return PluginLanguageMode.Automatic;
            }
        }
    }
}