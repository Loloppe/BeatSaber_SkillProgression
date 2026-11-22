using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Scoring;

namespace SkillProgression.Data.Platforms
{
    /// <summary>
    /// Live per-map facts for map rows and cards (D-060), fetched only for the maps on screen and cached on disk for a day:
    /// BeatLeader `/leaderboards/hash/{hash}` (ratings, BPM, NJS, offset, cover, leaderboard id) and `/pp/curve` (ranked
    /// maps), ScoreSaber `/api/leaderboard/by-hash/{HASH}/info` (stars), AccSaber `/v1/maps/difficulties/all` (complexity)
    /// and both platforms' curves. Endpoints verified live 2026-11-22. Failures leave the part unknown, never throw.
    /// </summary>
    public sealed class LiveMapClient
    {
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

        private sealed class Entry
        {
            public DateTime FetchedUtc { get; set; }
            public LiveMapInfo Info { get; set; } = new LiveMapInfo();
        }

        private sealed class FileData
        {
            public int Version { get; set; } = 1;
            public DateTime CurvesFetchedUtc { get; set; }
            public LiveCurves? Curves { get; set; }
            /// <summary>AccSaber complexity by "hash|Difficulty" (Standard only, the pool has no other mode).</summary>
            public Dictionary<string, double> AccSaberComplexity { get; set; } = new Dictionary<string, double>();
            public Dictionary<string, Entry> Maps { get; set; } = new Dictionary<string, Entry>();
        }

        private readonly HttpClient _http;
        private readonly string _filePath;
        private readonly TimeSpan _delay;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private FileData? _data;

        public LiveMapClient(HttpClient http, string filePath, TimeSpan? delayBetweenRequests = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _filePath = filePath;
            // ~3 req/s, far below BeatLeader's 50 / 10 s and ScoreSaber's / AccSaber's 400 / min.
            _delay = delayBetweenRequests ?? TimeSpan.FromMilliseconds(350);
        }

        /// <summary>ScoreSaber and AccSaber curves (and AccSaber complexities), refreshed daily.</summary>
        public async Task<LiveCurves> GetCurvesAsync(DateTime nowUtc, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var data = Load();
                if (data.Curves != null && nowUtc - data.CurvesFetchedUtc < MaxAge) return data.Curves;
                var curves = new LiveCurves();
                var ok = true;
                try { curves.ScoreSaber = ParseScoreSaberCurve(await GetTokenAsync("https://scoresaber.com/api/v2/realms/1/pp-curve", ct).ConfigureAwait(false)); }
                catch (Exception e) when (!(e is OperationCanceledException)) { ok = false; }
                try { ReadAccSaberCurve(await GetTokenAsync("https://api.accsaber.com/v1/curves", ct).ConfigureAwait(false), curves); }
                catch (Exception e) when (!(e is OperationCanceledException)) { ok = false; }
                try { data.AccSaberComplexity = ParseAccSaberComplexity(await GetTokenAsync("https://api.accsaber.com/v1/maps/difficulties/all", ct).ConfigureAwait(false)); }
                catch (Exception e) when (!(e is OperationCanceledException)) { ok = false; }
                if (!ok && data.Curves != null) return data.Curves; // keep the old copy over a partial one
                data.Curves = curves;
                data.CurvesFetchedUtc = ok ? nowUtc : DateTime.MinValue; // retry next time after a failure
                Save(data);
                return curves;
            }
            finally { _gate.Release(); }
        }

        /// <summary>
        /// Live info of one map; <paramref name="map"/> says where it is ranked so only useful calls are made.
        /// Null only when nothing could be fetched and nothing is cached.
        /// </summary>
        public async Task<LiveMapInfo?> GetAsync(MapKey key, MapEntry map, DateTime nowUtc, CancellationToken ct = default)
        {
            await GetCurvesAsync(nowUtc, ct).ConfigureAwait(false); // AccSaber complexities come with the curves
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var data = Load();
                var id = key.ToString();
                if (data.Maps.TryGetValue(id, out var cached) && nowUtc - cached.FetchedUtc < MaxAge) return cached.Info;

                var info = new LiveMapInfo();
                var ok = false;
                try
                {
                    var json = await GetTokenAsync($"https://api.beatleader.com/leaderboards/hash/{key.Hash.ToLowerInvariant()}", ct).ConfigureAwait(false);
                    var leaderboardId = ReadBeatLeader(json, key, info);
                    ok = true;
                    if (leaderboardId != null && map.RankedBeatLeader)
                    {
                        await Task.Delay(_delay, ct).ConfigureAwait(false);
                        info.BeatLeaderCurve = ParseBeatLeaderCurve(await GetTokenAsync(
                            $"https://api.beatleader.com/pp/curve?leaderboardId={Uri.EscapeDataString(leaderboardId)}&modifiers=", ct).ConfigureAwait(false));
                    }
                }
                catch (Exception e) when (!(e is OperationCanceledException)) { }

                if (map.RankedScoreSaber && ScoreSaberDifficulty(key.Difficulty) is int ssDiff)
                {
                    try
                    {
                        await Task.Delay(_delay, ct).ConfigureAwait(false);
                        var json = await GetTokenAsync($"https://scoresaber.com/api/leaderboard/by-hash/{key.Hash}/info" +
                                                       $"?difficulty={ssDiff}&gameMode=Solo{key.Characteristic}", ct).ConfigureAwait(false);
                        info.ScoreSaberStars = ParseScoreSaberStars(json);
                        ok = true;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException)) { }
                }

                if (map.RankedAccSaber && data.AccSaberComplexity.TryGetValue(AccSaberKey(key.Hash, key.Difficulty), out var complexity))
                {
                    info.AccSaberComplexity = complexity;
                    ok = true;
                }

                if (!ok) return cached?.Info;
                data.Maps[id] = new Entry { FetchedUtc = nowUtc, Info = info };
                foreach (var old in data.Maps.Where(kv => nowUtc - kv.Value.FetchedUtc > MaxAge + MaxAge).Select(kv => kv.Key).ToList())
                    data.Maps.Remove(old);
                Save(data);
                await Task.Delay(_delay, ct).ConfigureAwait(false);
                return info;
            }
            finally { _gate.Release(); }
        }

        /// <summary>Fills ratings / jump data / cover from `/leaderboards/hash`; returns the difficulty's leaderboard id.</summary>
        public static string? ReadBeatLeader(JToken json, MapKey key, LiveMapInfo info)
        {
            var song = json["song"];
            info.Bpm = song?["bpm"]?.Value<double?>() ?? 0;
            info.CoverUrl = song?["coverImage"]?.Value<string>() ?? "";
            var lb = (json["leaderboards"] as JArray)?.FirstOrDefault(l =>
                string.Equals(l["difficulty"]?["difficultyName"]?.Value<string>(), key.Difficulty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l["difficulty"]?["modeName"]?.Value<string>(), key.Characteristic, StringComparison.OrdinalIgnoreCase));
            if (lb == null) return null;
            var d = lb["difficulty"]!;
            info.Njs = d["njs"]?.Value<double?>() ?? 0;
            info.NjsOffset = d["noteJumpStartBeatOffset"]?.Value<double?>() ?? 0;
            info.Stars = d["stars"]?.Value<double?>() ?? 0;
            info.Pass = d["passRating"]?.Value<double?>() ?? 0;
            info.Acc = d["accRating"]?.Value<double?>() ?? 0;
            info.Tech = d["techRating"]?.Value<double?>() ?? 0;
            return lb["id"]?.Value<string>();
        }

        /// <summary>`/pp/curve` → curve; null unless `hasPp` (unranked maps return an empty one).</summary>
        public static PpCurve? ParseBeatLeaderCurve(JToken json)
        {
            if (json["hasPp"]?.Value<bool?>() != true || !(json["points"] is JArray points)) return null;
            return ToCurve(points.Select(p => (p[0]!.Value<double>(), p[1]!.Value<double>())));
        }

        /// <summary>`/api/v2/realms/1/pp-curve` → {"curve": [[acc, multiplier], …]} (descending accuracy).</summary>
        public static PpCurve? ParseScoreSaberCurve(JToken json) =>
            json["curve"] is JArray points ? ToCurve(points.Select(p => (p[0]!.Value<double>(), p[1]!.Value<double>()))) : null;

        /// <summary>`/v1/curves` → the "AccSaber Score Curve" (points {x, y}, scale, shift).</summary>
        public static void ReadAccSaberCurve(JToken json, LiveCurves curves)
        {
            var curve = (json as JArray)?.FirstOrDefault(c => c["name"]?.Value<string>() == "AccSaber Score Curve");
            if (curve == null || !(curve["points"] is JArray points)) return;
            curves.AccSaber = ToCurve(points.Select(p => (p["x"]!.Value<double>(), p["y"]!.Value<double>())));
            curves.AccSaberScale = curve["scale"]?.Value<double?>() ?? curves.AccSaberScale;
            curves.AccSaberShift = curve["shift"]?.Value<double?>() ?? curves.AccSaberShift;
        }

        public static Dictionary<string, double> ParseAccSaberComplexity(JToken json)
        {
            var result = new Dictionary<string, double>();
            foreach (var d in (json as JArray) ?? new JArray())
            {
                var hash = d["songHash"]?.Value<string>();
                var difficulty = AccSaberDifficulty(d["difficulty"]?.Value<string>());
                var complexity = d["complexity"]?.Value<double?>();
                if (!string.IsNullOrEmpty(hash) && difficulty != null && complexity != null)
                    result[AccSaberKey(hash!, difficulty)] = complexity.Value;
            }
            return result;
        }

        /// <summary>Stars when the ScoreSaber leaderboard is ranked, else null.</summary>
        public static double? ParseScoreSaberStars(JToken json) =>
            json["ranked"]?.Value<bool?>() == true && json["stars"]?.Value<double?>() is double s && s > 0 ? s : (double?)null;

        private static PpCurve ToCurve(IEnumerable<(double acc, double value)> points)
        {
            var sorted = points.OrderBy(p => p.acc).ToList();
            return new PpCurve { Acc = sorted.Select(p => p.acc).ToList(), Pp = sorted.Select(p => p.value).ToList() };
        }

        private static string AccSaberKey(string hash, string difficulty) => hash.ToUpperInvariant() + "|" + difficulty;

        private static string? AccSaberDifficulty(string? code) => code switch
        {
            "EASY" => "Easy", "NORMAL" => "Normal", "HARD" => "Hard", "EXPERT" => "Expert", "EXPERT_PLUS" => "ExpertPlus", _ => null,
        };

        private static int? ScoreSaberDifficulty(string difficulty) => difficulty switch
        {
            "Easy" => 1, "Normal" => 3, "Hard" => 5, "Expert" => 7, "ExpertPlus" => 9, _ => (int?)null,
        };

        private async Task<JToken> GetTokenAsync(string url, CancellationToken ct)
        {
            using (var response = await _http.GetAsync(url, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return JToken.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        private FileData Load()
        {
            if (_data != null) return _data;
            try
            {
                _data = File.Exists(_filePath)
                    ? JsonConvert.DeserializeObject<FileData>(File.ReadAllText(_filePath, Encoding.UTF8)) ?? new FileData()
                    : new FileData();
            }
            catch (Exception e) when (e is IOException || e is JsonException) { _data = new FileData(); }
            return _data;
        }

        private void Save(FileData data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
                var tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(data), Encoding.UTF8);
                if (File.Exists(_filePath)) File.Delete(_filePath);
                File.Move(tmp, _filePath);
            }
            catch (IOException) { } // a cache: losing a write only costs a refetch
        }
    }
}
