using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;

namespace SkillProgression.Data.BeatLeader
{
    /// <summary>
    /// Local cache of a player's BeatLeader personal bests so opening the plugin does not refetch everything:
    /// the first sync downloads all ranked PBs, later syncs only those set since the last sync (time_from).
    /// A newer PB on the same map replaces the older one.
    /// </summary>
    public sealed class ScoreCache
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

        /// <summary>Overlap kept when syncing incrementally, to tolerate clock skew and late uploads.</summary>
        public static readonly TimeSpan SyncOverlap = TimeSpan.FromHours(1);

        private readonly string _filePath;

        public ScoreCache(string filePath) => _filePath = filePath;

        public DateTime? LastSyncUtc => Read()?.LastSyncUtc;

        /// <summary>Syncs with BeatLeader and returns all cached PBs. A different player id resets the cache.</summary>
        public async Task<IReadOnlyList<PlayEvidence>> SyncAsync(BeatLeaderClient client, string playerId, DateTime nowUtc,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            var data = Read();
            if (data == null || data.PlayerId != playerId) data = new FileData { PlayerId = playerId };

            var since = data.LastSyncUtc - SyncOverlap;
            var fresh = await client.GetRankedScoresAsync(playerId, since, cancellationToken, progress).ConfigureAwait(false);
            var byMap = data.Scores.ToDictionary(e => Key(e));
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

        /// <summary>Cached PBs without contacting BeatLeader (offline / API outage).</summary>
        public IReadOnlyList<PlayEvidence> Cached() => Read() is { } d ? ToEvidence(d) : Array.Empty<PlayEvidence>();

        private static IReadOnlyList<PlayEvidence> ToEvidence(FileData d) =>
            d.Scores.Select(e => new PlayEvidence(Key(e), e.Accuracy, e.Modifiers, e.TimeUtc, EvidenceSource.BeatLeader)).ToList();

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
