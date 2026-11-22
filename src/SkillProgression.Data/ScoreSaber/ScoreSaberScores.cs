using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;

namespace SkillProgression.Data.ScoreSaber
{
    /// <summary>
    /// A player's ScoreSaber scores (D-062), from `/api/player/{id}/scores?sort=recent&amp;limit=100` (verified 2026-11-22,
    /// API_INTEGRATIONS.md). Ranked maps only: accuracy = base score / max score, the unmodified accuracy SS pp uses.
    /// </summary>
    public sealed class ScoreSaberClient
    {
        public const int PageSize = 100;
        private static readonly string[] Difficulties = { "", "Easy", "", "Normal", "", "Hard", "", "Expert", "", "ExpertPlus" };

        private readonly HttpClient _http;
        private readonly TimeSpan _delayBetweenPages;

        /// <param name="delayBetweenPages">ScoreSaber allows 400 calls a minute; the default stays well under it.</param>
        public ScoreSaberClient(HttpClient http, TimeSpan? delayBetweenPages = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _delayBetweenPages = delayBetweenPages ?? TimeSpan.FromMilliseconds(250);
        }

        /// <summary>
        /// Ranked scores newest first, only those set at or after <paramref name="since"/>. A player without a ScoreSaber
        /// profile throws <see cref="ScoreSaberException"/>.
        /// </summary>
        public async Task<IReadOnlyList<PlayEvidence>> GetScoresAsync(string playerId, DateTime? since = null,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(playerId)) throw new ArgumentException("Player id is required.", nameof(playerId));
            var result = new List<PlayEvidence>();
            for (var page = 1; ; page++)
            {
                var json = await GetJsonAsync($"https://scoresaber.com/api/player/{Uri.EscapeDataString(playerId)}/scores" +
                                              $"?sort=recent&limit={PageSize}&page={page}", cancellationToken).ConfigureAwait(false);
                if (!(json["playerScores"] is JArray scores) || scores.Count == 0) break;
                var older = false;
                foreach (var s in scores)
                {
                    var time = ParseTime(s["score"]?["timeSet"]);
                    if (since.HasValue && time < since.Value) { older = true; break; }
                    if (ParseScore(s) is { } evidence) result.Add(evidence);
                }

                var total = json["metadata"]?["total"]?.Value<int>() ?? 0;
                progress?.Report(total <= 0 ? 1 : Math.Min(1.0, (double)page * PageSize / total));
                if (older || page * PageSize >= total) break;
                await Task.Delay(_delayBetweenPages, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        /// <summary>One item of /player/{id}/scores; null for unranked maps, unknown difficulties or missing fields.</summary>
        public static PlayEvidence? ParseScore(JToken item)
        {
            var score = item["score"];
            var lb = item["leaderboard"];
            if (score == null || lb == null || lb["ranked"]?.Value<bool?>() != true) return null;
            var hash = lb["songHash"]?.Value<string>();
            var difficulty = lb["difficulty"]?["difficulty"]?.Value<int?>() ?? 0;
            var mode = lb["difficulty"]?["gameMode"]?.Value<string>() ?? "";
            var baseScore = score["baseScore"]?.Value<double?>();
            var maxScore = lb["maxScore"]?.Value<double?>() ?? 0;
            if (string.IsNullOrEmpty(hash) || difficulty <= 0 || difficulty >= Difficulties.Length || Difficulties[difficulty].Length == 0
                || !mode.StartsWith("Solo", StringComparison.Ordinal) || baseScore == null || maxScore <= 0) return null;

            var modifiers = (score["modifiers"]?.Value<string>() ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return new PlayEvidence(new MapKey(hash!, mode.Substring(4), Difficulties[difficulty]), Math.Min(1, baseScore.Value / maxScore),
                modifiers, ParseTime(score["timeSet"]), EvidenceSource.ScoreSaber);
        }

        private static DateTime ParseTime(JToken? token) =>
            token?.Type == JTokenType.Date ? DateTime.SpecifyKind(token.Value<DateTime>().ToUniversalTime(), DateTimeKind.Utc)
            : DateTime.TryParse(token?.Value<string>(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

        private async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                using (var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 429 && attempt < 3)
                    {
                        await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        throw new ScoreSaberException($"ScoreSaber player not found ({url}).");
                    response.EnsureSuccessStatusCode();
                    return JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }
        }
    }

    public sealed class ScoreSaberException : Exception
    {
        public ScoreSaberException(string message) : base(message) { }
    }

    /// <summary>
    /// Local cache of a player's ranked ScoreSaber scores (D-062), like the BeatLeader <c>ScoreCache</c>: the first sync
    /// downloads everything, later ones only scores set since the last sync. ScoreSaber keeps one score per map, so a
    /// newer score on the same map replaces the older one.
    /// </summary>
    public sealed class ScoreSaberScoreCache
    {
        private sealed class Entry
        {
            public string Hash { get; set; } = "";
            public string Characteristic { get; set; } = "";
            public string Difficulty { get; set; } = "";
            public double Accuracy { get; set; }
            public List<string> Modifiers { get; set; } = new List<string>();
            public DateTime TimeUtc { get; set; }
        }

        private sealed class FileData
        {
            public int Version { get; set; } = 1;
            public string PlayerId { get; set; } = "";
            public DateTime? LastSyncUtc { get; set; }
            public List<Entry> Scores { get; set; } = new List<Entry>();
        }

        public static readonly TimeSpan SyncOverlap = TimeSpan.FromHours(1);

        private readonly string _filePath;

        public ScoreSaberScoreCache(string filePath) => _filePath = filePath;

        public DateTime? LastSyncUtc => Read()?.LastSyncUtc;

        /// <summary>Syncs with ScoreSaber and returns all cached scores. A different player id resets the cache.</summary>
        public async Task<IReadOnlyList<PlayEvidence>> SyncAsync(ScoreSaberClient client, string playerId, DateTime nowUtc,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            var data = Read();
            if (data == null || data.PlayerId != playerId) data = new FileData { PlayerId = playerId };

            var fresh = await client.GetScoresAsync(playerId, data.LastSyncUtc - SyncOverlap, cancellationToken, progress).ConfigureAwait(false);
            var byMap = data.Scores.ToDictionary(Key);
            foreach (var e in fresh)
                byMap[e.Map] = new Entry
                {
                    Hash = e.Map.Hash, Characteristic = e.Map.Characteristic, Difficulty = e.Map.Difficulty,
                    Accuracy = e.Accuracy, Modifiers = e.Modifiers.ToList(), TimeUtc = e.TimeUtc,
                };
            data.Scores = byMap.Values.ToList();
            data.LastSyncUtc = nowUtc;
            Write(data);
            return ToEvidence(data);
        }

        /// <summary>Cached scores without contacting ScoreSaber (offline / outage / no profile).</summary>
        public IReadOnlyList<PlayEvidence> Cached() => Read() is { } d ? ToEvidence(d) : Array.Empty<PlayEvidence>();

        private static IReadOnlyList<PlayEvidence> ToEvidence(FileData d) =>
            d.Scores.Select(e => new PlayEvidence(Key(e), e.Accuracy, e.Modifiers, e.TimeUtc, EvidenceSource.ScoreSaber)).ToList();

        private static MapKey Key(Entry e) => new MapKey(e.Hash, e.Characteristic, e.Difficulty);

        private FileData? Read()
        {
            if (!File.Exists(_filePath)) return null;
            try { return JsonConvert.DeserializeObject<FileData>(File.ReadAllText(_filePath, Encoding.UTF8)); }
            catch (JsonException) { return null; }
        }

        private void Write(FileData data)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(data), new UTF8Encoding(false));
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
        }
    }
}
