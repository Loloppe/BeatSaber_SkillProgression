using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SkillProgression.Core.Plays;

namespace SkillProgression.Data.Plays
{
    /// <summary>
    /// Append-only local play log (D-011), one JSON object per line.
    /// Append-only keeps earlier history safe if the game crashes mid-write; unreadable lines are skipped on read.
    /// </summary>
    public sealed class PlayLogStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.None,
        };

        private readonly object _lock = new object();

        public string FilePath { get; }

        public PlayLogStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is required.", nameof(filePath));
            FilePath = filePath;
        }

        public void Append(PlayRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var line = JsonConvert.SerializeObject(record, Settings) + "\n";
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(FilePath, line, new UTF8Encoding(false));
            }
        }

        /// <summary>All readable records in file order. <paramref name="skipped"/> counts corrupt lines.</summary>
        public IReadOnlyList<PlayRecord> ReadAll(out int skipped)
        {
            skipped = 0;
            var result = new List<PlayRecord>();
            string[] lines;
            lock (_lock)
            {
                if (!File.Exists(FilePath)) return result;
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var record = JsonConvert.DeserializeObject<PlayRecord>(line, Settings);
                    if (record != null) result.Add(record);
                    else skipped++;
                }
                catch (JsonException)
                {
                    skipped++;
                }
            }

            return result;
        }
    }
}
