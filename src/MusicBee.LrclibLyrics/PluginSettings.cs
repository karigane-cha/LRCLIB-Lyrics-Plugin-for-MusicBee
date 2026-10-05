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

    public enum CandidateDialogMode
    {
        Never = 0,
        MultipleOnly = 1,
        Always = 2
    }

    public sealed class PluginSettings
    {
        private const int CurrentLanguageModeVersion = 2;

        public bool SyncedLyricsOnly { get; set; } = true;
        public ExistingLyricsSkipMode ExistingLyricsSkipMode { get; set; } = ExistingLyricsSkipMode.LrcOrTextOrEmbeddedLyrics;
        public EmbeddedLyricsHandling EmbeddedLyricsHandling { get; set; } = EmbeddedLyricsHandling.Ignore;
        public CandidateDialogMode CandidateDialogMode { get; set; } = CandidateDialogMode.MultipleOnly;
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
                var savedValues = serializer.Deserialize<Dictionary<string, object>>(json);
                if (savedValues == null) return new PluginSettings();

                var hasCandidateDialogMode = savedValues.ContainsKey("CandidateDialogMode");
                CandidateDialogMode candidateDialogMode = CandidateDialogMode.MultipleOnly;
                if (hasCandidateDialogMode && !TryReadCandidateDialogMode(savedValues["CandidateDialogMode"], out candidateDialogMode))
                    savedValues.Remove("CandidateDialogMode");

                // Deserialize a sanitized dictionary so one invalid enum value does not discard
                // otherwise valid settings.
                var value = serializer.Deserialize<PluginSettings>(serializer.Serialize(savedValues));
                if (value == null) return new PluginSettings();
                if (hasCandidateDialogMode)
                {
                    value.CandidateDialogMode = candidateDialogMode;
                }
                else if (savedValues.ContainsKey("ShowCandidatePickerWhenMultiple"))
                {
                    bool showWhenMultiple;
                    if (TryReadLegacyBoolean(savedValues, "ShowCandidatePickerWhenMultiple", out showWhenMultiple))
                        value.CandidateDialogMode = showWhenMultiple ? CandidateDialogMode.MultipleOnly : CandidateDialogMode.Never;
                }

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
                if (!Enum.IsDefined(typeof(CandidateDialogMode), value.CandidateDialogMode))
                    value.CandidateDialogMode = CandidateDialogMode.MultipleOnly;

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
            try { return values.ContainsKey(key) && values[key] != null && Convert.ToBoolean(values[key]); }
            catch { return false; }
        }

        private static bool TryReadLegacyBoolean(IDictionary<string, object> values, string key, out bool result)
        {
            result = false;
            if (!values.ContainsKey(key) || values[key] == null) return false;
            try
            {
                result = Convert.ToBoolean(values[key]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadCandidateDialogMode(object value, out CandidateDialogMode mode)
        {
            mode = CandidateDialogMode.MultipleOnly;
            if (value == null || value is bool || value is char) return false;

            try
            {
                var numericValue = Convert.ToInt32(value);
                if (!Enum.IsDefined(typeof(CandidateDialogMode), numericValue)) return false;
                mode = (CandidateDialogMode)numericValue;
                return true;
            }
            catch
            {
                return false;
            }
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
