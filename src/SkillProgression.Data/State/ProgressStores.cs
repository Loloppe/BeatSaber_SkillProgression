using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SkillProgression.Core.Achievements;

namespace SkillProgression.Data.State
{
    /// <summary>One point of a platform's rank history (API back-fill + the plugin's daily snapshots).</summary>
    public sealed class RankPoint
    {
        public DateTime DateUtc { get; set; }
        public int Rank { get; set; }
        public double Pp { get; set; }
    }

    /// <summary>
    /// Achievements, daily progression snapshots and BL rank history in one versioned JSON file under UserData
    /// (survives plugin updates; atomic replace). Snapshots keep one entry per day (the latest of the day).
    /// </summary>
    public sealed class ProgressStore
    {
        private sealed class FileData
        {
            public int Version { get; set; } = 1;
            public List<Achievement> Achievements { get; set; } = new List<Achievement>();
            public List<ProgressSnapshot> Snapshots { get; set; } = new List<ProgressSnapshot>();
            /// <summary>Rank history per platform: "BeatLeader", "ScoreSaber", "AccSaber" (overall).</summary>
            public Dictionary<string, List<RankPoint>> RankHistories { get; set; } = new Dictionary<string, List<RankPoint>>();
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        };

        private readonly string _filePath;
        private FileData _data;

        public ProgressStore(string filePath)
        {
            _filePath = filePath;
            _data = Read();
        }

        public IReadOnlyList<Achievement> Achievements => _data.Achievements;
        public IReadOnlyList<ProgressSnapshot> Snapshots => _data.Snapshots;
        public IReadOnlyList<RankPoint> RankHistory(string platform) =>
            _data.RankHistories.TryGetValue(platform, out var list) ? list : (IReadOnlyList<RankPoint>)new List<RankPoint>();
        public ProgressSnapshot? LastSnapshot => _data.Snapshots.LastOrDefault();
        public ISet<string> UnlockedIds() => new HashSet<string>(_data.Achievements.Select(a => a.Id));

        public void Add(IEnumerable<Achievement> achievements)
        {
            var ids = UnlockedIds();
            _data.Achievements.AddRange(achievements.Where(a => ids.Add(a.Id)));
        }

        public void PutSnapshot(ProgressSnapshot snapshot)
        {
            _data.Snapshots.RemoveAll(s => s.DateUtc.Date == snapshot.DateUtc.Date);
            _data.Snapshots.Add(snapshot);
            _data.Snapshots.Sort((a, b) => a.DateUtc.CompareTo(b.DateUtc));
        }

        /// <summary>Merges rank points of a platform by day (newer data wins).</summary>
        public void MergeRankHistory(string platform, IEnumerable<RankPoint> points)
        {
            var byDay = RankHistory(platform).ToDictionary(p => p.DateUtc.Date);
            foreach (var p in points) byDay[p.DateUtc.Date] = new RankPoint { DateUtc = p.DateUtc.Date, Rank = p.Rank, Pp = p.Pp };
            _data.RankHistories[platform] = byDay.Values.OrderBy(p => p.DateUtc).ToList();
        }

        /// <summary>Clears everything (start-mode change resets progression tracking, D-029).</summary>
        public void Reset() => _data = new FileData();

        public void Save()
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_data, Settings), new UTF8Encoding(false));
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
        }

        private FileData Read()
        {
            if (!File.Exists(_filePath)) return new FileData();
            try { return JsonConvert.DeserializeObject<FileData>(File.ReadAllText(_filePath, Encoding.UTF8), Settings) ?? new FileData(); }
            catch (JsonException)
            {
                File.Copy(_filePath, _filePath + ".corrupt", overwrite: true);
                return new FileData();
            }
        }
    }
}
