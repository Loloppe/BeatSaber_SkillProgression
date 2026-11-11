using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SkillProgression.Data.Platforms
{
    /// <summary>One line of the platforms panel (D-003, D-029): official progress on a ranking platform.</summary>
    public sealed class PlatformStats
    {
        public string Platform { get; set; } = "";
        /// <summary>Category name for platforms that rank per category (AccSaber); empty otherwise.</summary>
        public string Category { get; set; } = "";
        public int? Rank { get; set; }
        public int? CountryRank { get; set; }
        public double? Points { get; set; }
        /// <summary>"pp" or "AP".</summary>
        public string Unit { get; set; } = "pp";
    }

    /// <summary>
    /// Live official stats from BeatLeader, ScoreSaber and AccSaber (endpoints verified 2026-11-10, anonymous).
    /// Each platform fails independently: a missing profile or an outage just yields no line for it.
    /// </summary>
    public sealed class PlatformStatsClient
    {
        private static readonly Dictionary<string, string> AccSaberCategories = new Dictionary<string, string>
        {
            ["b0000000-0000-0000-0000-000000000001"] = "True Acc",
            ["b0000000-0000-0000-0000-000000000002"] = "Standard Acc",
            ["b0000000-0000-0000-0000-000000000003"] = "Tech Acc",
            ["b0000000-0000-0000-0000-000000000005"] = "Overall",
        };

        private readonly HttpClient _http;

        public PlatformStatsClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

        public async Task<IReadOnlyList<PlatformStats>> GetAllAsync(string playerId, CancellationToken cancellationToken = default)
        {
            var tasks = new[]
            {
                Try(() => GetBeatLeaderAsync(playerId, cancellationToken)),
                Try(() => GetScoreSaberAsync(playerId, cancellationToken)),
                Try(() => GetAccSaberAsync(playerId, cancellationToken)),
            };
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.SelectMany(r => r).ToList();
        }

        public async Task<IReadOnlyList<PlatformStats>> GetBeatLeaderAsync(string playerId, CancellationToken ct = default)
        {
            var json = await GetAsync($"https://api.beatleader.com/player/{Uri.EscapeDataString(playerId)}?stats=false", ct).ConfigureAwait(false);
            return new[] { ParseRankPp("BeatLeader", json) };
        }

        public async Task<IReadOnlyList<PlatformStats>> GetScoreSaberAsync(string playerId, CancellationToken ct = default)
        {
            var json = await GetAsync($"https://scoresaber.com/api/player/{Uri.EscapeDataString(playerId)}/basic", ct).ConfigureAwait(false);
            return new[] { ParseRankPp("ScoreSaber", json) };
        }

        public async Task<IReadOnlyList<PlatformStats>> GetAccSaberAsync(string playerId, CancellationToken ct = default)
        {
            var json = await GetAsync($"https://api.accsaber.com/v1/users/{Uri.EscapeDataString(playerId)}/statistics/all", ct).ConfigureAwait(false);
            return ParseAccSaber(json);
        }

        /// <summary>ScoreSaber daily history (v2: [{rank, totalPP, createdAt}], ~7 months for an active player).</summary>
        public async Task<IReadOnlyList<SkillProgression.Data.State.RankPoint>> GetScoreSaberHistoryAsync(string playerId, CancellationToken ct = default)
        {
            var array = await GetArrayAsync($"https://scoresaber.com/api/v2/players/{Uri.EscapeDataString(playerId)}/history", ct).ConfigureAwait(false);
            return ParseHistory(array, "createdAt", "rank", "totalPP");
        }

        /// <summary>AccSaber overall ranking history ([{ranking, recordedAt}]; AccSaber Reloaded started autumn 2026, so short).</summary>
        public async Task<IReadOnlyList<SkillProgression.Data.State.RankPoint>> GetAccSaberHistoryAsync(string playerId, CancellationToken ct = default)
        {
            var array = await GetArrayAsync($"https://api.accsaber.com/v1/users/{Uri.EscapeDataString(playerId)}/ranking-history", ct).ConfigureAwait(false);
            return ParseHistory(array, "recordedAt", "ranking", null);
        }

        public static IReadOnlyList<SkillProgression.Data.State.RankPoint> ParseHistory(JArray array, string dateField, string rankField, string? pointsField) =>
            array.Select(x => (date: x[dateField]?.Value<DateTime?>(), rank: x[rankField]?.Value<int?>() ?? 0,
                               points: pointsField == null ? 0 : x[pointsField]?.Value<double?>() ?? 0))
                .Where(x => x.date.HasValue && x.rank > 0)
                .Select(x => new SkillProgression.Data.State.RankPoint { DateUtc = x.date!.Value.ToUniversalTime().Date, Rank = x.rank, Pp = x.points })
                .OrderBy(p => p.DateUtc).ToList();

        private async Task<JArray> GetArrayAsync(string url, CancellationToken ct)
        {
            using (var response = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return JArray.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        public static PlatformStats ParseRankPp(string platform, JObject json) => new PlatformStats
        {
            Platform = platform,
            Rank = json["rank"]?.Value<int?>(),
            CountryRank = json["countryRank"]?.Value<int?>(),
            Points = json["pp"]?.Value<double?>(),
            Unit = "pp",
        };

        public static IReadOnlyList<PlatformStats> ParseAccSaber(JObject json)
        {
            var result = new List<PlatformStats>();
            if (!(json["categories"] is JArray categories)) return result;
            foreach (var c in categories)
            {
                var id = c["categoryId"]?.Value<string>() ?? "";
                result.Add(new PlatformStats
                {
                    Platform = "AccSaber",
                    Category = AccSaberCategories.TryGetValue(id, out var name) ? name : id,
                    Rank = c["ranking"]?.Value<int?>(),
                    CountryRank = c["countryRanking"]?.Value<int?>(),
                    Points = c["ap"]?.Value<double?>(),
                    Unit = "AP",
                });
            }
            // Overall first, then the categories in a fixed order.
            var order = AccSaberCategories.Values.ToList();
            return result.OrderBy(r => r.Category == "Overall" ? -1 : order.IndexOf(r.Category)).ToList();
        }

        private async Task<JObject> GetAsync(string url, CancellationToken ct)
        {
            using (var response = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        private static async Task<IReadOnlyList<PlatformStats>> Try(Func<Task<IReadOnlyList<PlatformStats>>> call)
        {
            try { return await call().ConfigureAwait(false); }
            catch (Exception) { return Array.Empty<PlatformStats>(); }
        }
    }
}
