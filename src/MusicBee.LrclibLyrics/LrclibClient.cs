using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MusicBeePlugin
{
    internal sealed class LrclibClient
    {
        private const string ApiBase = "https://lrclib.net/api";
        private const string ProjectUrl = "https://github.com/karigane-cha/LRCLIB-Lyrics-Plugin-for-MusicBee";
        private const int TooManyRequestsStatusCode = 429;
        private const int MaxRateLimitRetries = 2;
        private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(250);
        private readonly HttpClient client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly SemaphoreSlim requestSemaphore = new SemaphoreSlim(1, 1);
        private DateTimeOffset nextRequestAllowedAt = DateTimeOffset.MinValue;

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
            await requestSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                for (var retry = 0; ; retry++)
                {
                    var wait = nextRequestAllowedAt - DateTimeOffset.UtcNow;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, token).ConfigureAwait(false);

                    using (var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + relativeUrl))
                    {
                        request.Headers.UserAgent.ParseAdd("MusicBee-LrclibLyrics/" + PluginVersionInfo.UserAgentVersion + " (+" + ProjectUrl + ")");
                        using (var response = await client.SendAsync(request, token).ConfigureAwait(false))
                        {
                            nextRequestAllowedAt = DateTimeOffset.UtcNow.Add(MinimumRequestInterval);
                            if ((int)response.StatusCode == TooManyRequestsStatusCode)
                            {
                                var retryAfter = GetRetryAfter(response);
                                if (retryAfter > MinimumRequestInterval)
                                    nextRequestAllowedAt = DateTimeOffset.UtcNow.Add(retryAfter);

                                if (retry >= MaxRateLimitRetries) return null;
                                continue;
                            }

                            if (!response.IsSuccessStatusCode) return null;
                            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
            finally
            {
                requestSemaphore.Release();
            }
        }

        private static TimeSpan GetRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null) return DefaultRetryAfter;
            if (retryAfter.Delta.HasValue) return retryAfter.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : retryAfter.Delta.Value;
            if (retryAfter.Date.HasValue)
            {
                var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            }
            return DefaultRetryAfter;
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
            {
                var durationScore = 20.0 - Math.Min(20.0, Math.Abs(track.DurationSeconds - candidate.DurationSeconds));
                score += (int)Math.Round(durationScore, MidpointRounding.AwayFromZero);
            }
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
        private static double Number(IDictionary<string, object> data, string key)
        {
            if (data == null || !data.ContainsKey(key) || data[key] == null) return 0;

            var raw = data[key];
            double value;
            var text = raw as string;
            if (text != null)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return 0;
            }
            else
            {
                if (raw is bool || raw is char) return 0;
                try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
                catch { return 0; }
            }

            return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
        }
    }

    internal sealed class TrackMetadata
    {
        public string Path; public string Title; public string Artist; public string Album; public int DurationSeconds;
    }
    internal sealed class LyricsResult
    {
        public readonly string Title, Artist, Album, Lyrics; public readonly bool IsSynced; public readonly double DurationSeconds;
        public int MatchScore { get; set; }
        public LyricsResult(string title, string artist, string album, string lyrics, bool isSynced, double durationSeconds) { Title = title; Artist = artist; Album = album; Lyrics = lyrics; IsSynced = isSynced; DurationSeconds = durationSeconds; }
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
