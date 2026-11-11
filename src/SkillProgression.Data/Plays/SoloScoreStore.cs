using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;

namespace SkillProgression.Data.Plays
{
    /// <summary>
    /// Clears made from the game's Solo menu (D-049), kept as scores only: the best accuracy per map, used as evidence until
    /// BeatLeader syncs them. Unlike the play log they never count for step sizes, misses, calibration or achievements.
    /// </summary>
    public sealed class SoloScoreStore
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

        private readonly string _filePath;
        private readonly object _lock = new object();

        public SoloScoreStore(string filePath) => _filePath = filePath;

        /// <summary>Keeps the play when it beats the stored accuracy on that map (or the map is new).</summary>
        public void Add(PlayEvidence play)
        {
            lock (_lock)
            {
                var entries = Read();
                var key = play.Map;
                var existing = entries.FirstOrDefault(e => Key(e).Equals(key));
                if (existing != null && existing.Accuracy >= play.Accuracy) return;
                if (existing != null) entries.Remove(existing);
                entries.Add(new Entry
                {
                    Hash = key.Hash, Characteristic = key.Characteristic, Difficulty = key.Difficulty,
                    Accuracy = play.Accuracy, Modifiers = play.Modifiers.ToList(), TimeUtc = play.TimeUtc,
                });
                Write(entries);
            }
        }

        public IReadOnlyList<PlayEvidence> All()
        {
            lock (_lock)
                return Read().Select(e => new PlayEvidence(Key(e), e.Accuracy, e.Modifiers, e.TimeUtc, EvidenceSource.LocalSolo)).ToList();
        }

        private static MapKey Key(Entry e) => new MapKey(e.Hash, e.Characteristic, e.Difficulty);

        private List<Entry> Read()
        {
            if (!File.Exists(_filePath)) return new List<Entry>();
            try { return JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(_filePath, Encoding.UTF8)) ?? new List<Entry>(); }
            catch (JsonException) { return new List<Entry>(); }
        }

        private void Write(List<Entry> entries)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(entries), new UTF8Encoding(false));
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
        }
    }
}
