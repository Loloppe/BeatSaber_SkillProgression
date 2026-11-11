using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;

namespace SkillProgression.Data.BeatLeader
{
    /// <summary>
    /// Anonymous BeatLeader API reads (docs/API_INTEGRATIONS.md). Personal bests only; incremental via time_from.
    /// The HttpClient is injected so tests can stub it and the plugin can share one instance.
    /// Uses ConfigureAwait(false): callers on the Unity main thread must marshal results back themselves.
    /// </summary>
    public sealed class BeatLeaderClient
    {
        public const string DefaultBaseUrl = "https://api.beatleader.com";
        private const int PageSize = 100;

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly TimeSpan _delayBetweenPages;

        public BeatLeaderClient(HttpClient http, string baseUrl = DefaultBaseUrl, TimeSpan? delayBetweenPages = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _baseUrl = baseUrl.TrimEnd('/');
            // ~3 req/s keeps far below the 50 req / 10 s limit.
            _delayBetweenPages = delayBetweenPages ?? TimeSpan.FromMilliseconds(350);
        }

        /// <summary>All ranked personal bests of a player, newest first; only those set at or after <paramref name="since"/>.</summary>
        /// <param name="progress">Receives 0..1 as pages arrive (UI progress bar for the first import).</param>
        public Task<IReadOnlyList<PlayEvidence>> GetRankedScoresAsync(string playerId, DateTime? since = null,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
            GetScoresAsync(playerId, ScoreType.Ranked, since, cancellationToken, progress);

        /// <summary>
        /// Personal bests of a player on maps of one status (type=ranked / unranked, BL DifficultyStatus; unranked
        /// verified 2026-11-11), newest first; only those set at or after <paramref name="since"/>.
        /// </summary>
        public async Task<IReadOnlyList<PlayEvidence>> GetScoresAsync(string playerId, ScoreType type, DateTime? since = null,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player id is required.", nameof(playerId));
            var status = type == ScoreType.Unranked ? "unranked" : "ranked";
            var result = new List<PlayEvidence>();
            for (var page = 1; ; page++)
            {
                var url = $"{_baseUrl}/player/{Uri.EscapeDataString(playerId)}/scores?type={status}&sortBy=date&order=desc" +
                          $"&count={PageSize}&page={page}";
                if (since.HasValue)
                    url += "&time_from=" + new DateTimeOffset(DateTime.SpecifyKind(since.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();

                var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
                var data = json["data"] as JArray;
                if (data == null || data.Count == 0) break;
                foreach (var score in data)
                {
                    var evidence = ParseScore(score);
                    if (evidence != null) result.Add(evidence);
                }

                var total = json["metadata"]?["total"]?.Value<int>() ?? 0;
                progress?.Report(total <= 0 ? 1 : Math.Min(1.0, (double)page * PageSize / total));
                if (page * PageSize >= total) break;
                await Task.Delay(_delayBetweenPages, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        /// <summary>Daily rank/pp history (verified 2026-11-10: array of {timestamp, rank, pp, …}), oldest first.</summary>
        public async Task<IReadOnlyList<SkillProgression.Data.State.RankPoint>> GetRankHistoryAsync(string playerId, int days = 2000,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/player/{Uri.EscapeDataString(playerId)}/history?count={days}";
            using (var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var array = JArray.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                return array
                    .Select(x => (time: ParseUnix(x["timestamp"]), rank: x["rank"]?.Value<int?>() ?? 0, pp: x["pp"]?.Value<double?>() ?? 0))
                    .Where(x => x.time.HasValue && x.rank > 0)
                    .Select(x => new SkillProgression.Data.State.RankPoint { DateUtc = x.time!.Value.Date, Rank = x.rank, Pp = x.pp })
                    .OrderBy(p => p.DateUtc)
                    .ToList();
            }
        }

        /// <summary>Parses one score of /player/{id}/scores; null when essential fields are missing.</summary>
        public static PlayEvidence? ParseScore(JToken score)
        {
            var lb = score["leaderboard"];
            var hash = lb?["song"]?["hash"]?.Value<string>();
            var difficulty = lb?["difficulty"]?["difficultyName"]?.Value<string>();
            var mode = lb?["difficulty"]?["modeName"]?.Value<string>() ?? "Standard";
            var accuracy = score["accuracy"]?.Value<double?>();
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(difficulty) || accuracy == null) return null;

            var modifiers = (score["modifiers"]?.Value<string>() ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var time = ParseUnix(score["timepost"]) ?? ParseUnix(score["timeset"]) ?? DateTime.MinValue;
            return new PlayEvidence(new MapKey(hash!, mode, difficulty!), accuracy.Value, modifiers, time, EvidenceSource.BeatLeader);
        }

        private static DateTime? ParseUnix(JToken? token)
        {
            if (token == null) return null;
            var text = token.Type == JTokenType.Integer ? token.Value<long>().ToString(CultureInfo.InvariantCulture) : token.Value<string>();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s > 0
                ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime
                : (DateTime?)null;
        }

        private async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                using (var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 429 && attempt < 3)
                    {
                        var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new BeatLeaderException($"BeatLeader player or resource not found ({url}).");
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JObject.Parse(body);
                }
            }
        }
    }

    /// <summary>Map status of the scores to fetch.</summary>
    public enum ScoreType
    {
        Ranked,
        /// <summary>Unranked maps (Explore's Curated pool, D-045); never used for progression.</summary>
        Unranked,
    }

    public sealed class BeatLeaderException : Exception
    {
        public BeatLeaderException(string message) : base(message) { }
    }
}
