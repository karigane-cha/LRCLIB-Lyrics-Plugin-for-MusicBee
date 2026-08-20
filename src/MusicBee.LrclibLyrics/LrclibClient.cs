using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MusicBeePlugin
{
    internal sealed class LrclibClient
    {
        private const string ApiBase = "https://lrclib.net/api";
        private readonly HttpClient client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public async Task<LyricsSearchResponse> SearchAsync(TrackMetadata track, bool syncedOnly, int timeoutSeconds)
        {
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                var direct = await GetObjectAsync("/get?track_name=" + Escape(track.Title) + "&artist_name=" + Escape(track.Artist) + "&album_name=" + Escape(track.Album) + "&duration=" + track.DurationSeconds, cancellation.Token).ConfigureAwait(false);
                var result = ToResult(direct, syncedOnly);
                if (result != null) return LyricsSearchResponse.Direct(result);

                var candidates = await GetArrayAsync("/search?q=" + Escape(track.Artist + " " + track.Title), cancellation.Token).ConfigureAwait(false);
                var list = candidates
                    .Select(x => ToResult(x, syncedOnly))
                    .Where(x => x != null)
                    .Select(x => { x.MatchScore = Score(x, track); return x; })
                    .Where(x => x.MatchScore >= 60)
                    .OrderByDescending(x => x.MatchScore)
                    .ToList();
                return LyricsSearchResponse.FromCandidates(list);
            }
        }

        private async Task<string> GetJsonAsync(string relativeUrl, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + relativeUrl))
            {
                request.Headers.UserAgent.ParseAdd("MusicBee-LrclibLyrics/0.1 (+https://github.com/your-account/MusicBee-Lyrics-Plugin)");
                using (var response = await client.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task<Dictionary<string, object>> GetObjectAsync(string relativeUrl, CancellationToken token)
        {
            var json = await GetJsonAsync(relativeUrl, token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? null : serializer.Deserialize<Dictionary<string, object>>(json);
        }

        private async Task<IEnumerable<Dictionary<string, object>>> GetArrayAsync(string relativeUrl, CancellationToken token)
        {
            var json = await GetJsonAsync(relativeUrl, token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? Enumerable.Empty<Dictionary<string, object>>() : serializer.Deserialize<List<Dictionary<string, object>>>(json);
        }

        private static string Escape(string value) { return Uri.EscapeDataString(value ?? string.Empty); }

        private LyricsResult ToResult(Dictionary<string, object> data, bool syncedOnly)
        {
            if (data == null) return null;
            var synced = Value(data, "syncedLyrics");
            var plain = Value(data, "plainLyrics");
            if (!string.IsNullOrWhiteSpace(synced)) return new LyricsResult(Value(data, "trackName"), Value(data, "artistName"), Value(data, "albumName"), synced, true, Number(data, "duration"));
            if (!syncedOnly && !string.IsNullOrWhiteSpace(plain)) return new LyricsResult(Value(data, "trackName"), Value(data, "artistName"), Value(data, "albumName"), plain, false, Number(data, "duration"));
            return null;
        }

        private static int Score(LyricsResult candidate, TrackMetadata track)
        {
            var score = Similar(candidate.Title, track.Title) * 45 + Similar(candidate.Artist, track.Artist) * 35;
            if (track.DurationSeconds > 0 && candidate.DurationSeconds > 0)
                score += Math.Max(0, 20 - Math.Min(20, Math.Abs(track.DurationSeconds - candidate.DurationSeconds)));
            return score;
        }
        private static int Similar(string left, string right)
        {
            var normalizedLeft = Normalize(left);
            var normalizedRight = Normalize(right);
            if (normalizedLeft.Length == 0 || normalizedRight.Length == 0) return 0;
            return normalizedLeft == normalizedRight || normalizedLeft.Contains(normalizedRight) || normalizedRight.Contains(normalizedLeft) ? 1 : 0;
        }
        private static string Normalize(string value) => new string((value ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        private static string Value(IDictionary<string, object> data, string key) => data.ContainsKey(key) && data[key] != null ? Convert.ToString(data[key], CultureInfo.InvariantCulture) : string.Empty;
        private static int Number(IDictionary<string, object> data, string key) { int value; return int.TryParse(Value(data, key), out value) ? value : 0; }
    }

    internal sealed class TrackMetadata
    {
        public string Path; public string Title; public string Artist; public string Album; public int DurationSeconds;
    }
    internal sealed class LyricsResult
    {
        public readonly string Title, Artist, Album, Lyrics; public readonly bool IsSynced; public readonly int DurationSeconds;
        public int MatchScore { get; set; }
        public LyricsResult(string title, string artist, string album, string lyrics, bool isSynced, int durationSeconds) { Title = title; Artist = artist; Album = album; Lyrics = lyrics; IsSynced = isSynced; DurationSeconds = durationSeconds; }
    }

    internal sealed class LyricsSearchResponse
    {
        public readonly bool IsDirectMatch;
        public readonly IList<LyricsResult> Candidates;

        private LyricsSearchResponse(bool isDirectMatch, IList<LyricsResult> candidates) { IsDirectMatch = isDirectMatch; Candidates = candidates; }
        public static LyricsSearchResponse Direct(LyricsResult result) { return new LyricsSearchResponse(true, new List<LyricsResult> { result }); }
        public static LyricsSearchResponse FromCandidates(IList<LyricsResult> results) { return new LyricsSearchResponse(false, results); }
    }
}
