using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Progression;

namespace SkillProgression.Data.State
{
    public enum StartMode
    {
        /// <summary>Not chosen yet: the first-run question is shown (D-029).</summary>
        Unset,
        /// <summary>Use BeatLeader personal bests as evidence (D-005 snapshot).</summary>
        ImportBeatLeader,
        /// <summary>Ignore everything before <see cref="UserState.FreshStartUtc"/> (D-005/D-010).</summary>
        Fresh,
    }

    /// <summary>Fresh-start calibration of one path (D-040): the maps picked so far, or skipped.</summary>
    public sealed class CalibrationState
    {
        public List<CalibrationPickState> Picks { get; set; } = new List<CalibrationPickState>();
        public bool Skipped { get; set; }
    }

    public sealed class CalibrationPickState
    {
        /// <summary>"HASH/Characteristic/Difficulty".</summary>
        public string Map { get; set; } = "";
        public double Axis { get; set; }
        public double TargetAccuracy { get; set; }
        public DateTime PickedUtc { get; set; }
    }

    /// <summary>The player's choices, persisted between sessions. Versioned for migrations.</summary>
    public sealed class UserState
    {
        public int Version { get; set; } = CurrentVersion;
        public const int CurrentVersion = 2;
        public string PlayerId { get; set; } = "";
        public StartMode StartMode { get; set; } = StartMode.Unset;
        public DateTime? FreshStartUtc { get; set; }
        /// <summary>Paths the player follows (D-008), and the one they focus on now (D-029: switchable any time).</summary>
        public List<string> Paths { get; set; } = new List<string>();
        public string FocusPath { get; set; } = "";
        /// <summary>Pool recommendations come from (D-041); "Any ranked" by default (user, 2026-11-10).</summary>
        public MapPool MapPool { get; set; } = MapPool.AnyRanked;
        /// <summary>Skipped recommendations as "HASH/Characteristic/Difficulty" (D-029).</summary>
        public List<string> Skipped { get; set; } = new List<string>();
        /// <summary>Fresh-start calibration per path (D-040); cleared when the start mode changes.</summary>
        public Dictionary<string, CalibrationState> Calibrations { get; set; } = new Dictionary<string, CalibrationState>();

        public CalibrationState Calibration(string path)
        {
            if (!Calibrations.TryGetValue(path, out var c)) Calibrations[path] = c = new CalibrationState();
            return c;
        }

        public List<CalibrationPick> CalibrationPicks(string path) =>
            Calibrations.TryGetValue(path, out var c)
                ? c.Picks.Select(p => (p, key: Parse(p.Map))).Where(x => x.key.HasValue)
                    .Select(x => new CalibrationPick { Map = x.key!.Value, Axis = x.p.Axis, TargetAccuracy = x.p.TargetAccuracy, PickedUtc = x.p.PickedUtc })
                    .ToList()
                : new List<CalibrationPick>();

        public void AddCalibrationPick(string path, CalibrationPick pick) => Calibration(path).Picks.Add(new CalibrationPickState
        {
            Map = pick.Map.ToString(), Axis = pick.Axis, TargetAccuracy = pick.TargetAccuracy, PickedUtc = pick.PickedUtc,
        });

        public ISet<MapKey> SkippedKeys() => new HashSet<MapKey>(Skipped.Select(Parse).Where(k => k.HasValue).Select(k => k!.Value));

        public void Skip(MapKey map)
        {
            var s = map.ToString();
            if (!Skipped.Contains(s)) Skipped.Add(s);
        }

        private static MapKey? Parse(string s)
        {
            var parts = s.Split('/');
            return parts.Length == 3 && parts[0].Length > 0 && parts[2].Length > 0 ? new MapKey(parts[0], parts[1], parts[2]) : (MapKey?)null;
        }
    }

    /// <summary>JSON file store with atomic replace, so a crash mid-save never corrupts the player's choices.</summary>
    public sealed class UserStateStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            Formatting = Formatting.Indented,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        };

        public string FilePath { get; }

        public UserStateStore(string filePath) => FilePath = filePath;

        public UserState Load()
        {
            if (!File.Exists(FilePath)) return new UserState();
            try
            {
                var state = JsonConvert.DeserializeObject<UserState>(File.ReadAllText(FilePath, Encoding.UTF8), Settings) ?? new UserState();
                if (state.Version < 2)
                {
                    // v2: the pool default became "Any ranked"; v1 files only ever held the old BeatLeader default.
                    state.MapPool = MapPool.AnyRanked;
                    state.Version = 2;
                }
                return state;
            }
            catch (JsonException)
            {
                // Keep the unreadable file for inspection, start clean rather than crash the menu.
                File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
                return new UserState();
            }
        }

        public void Save(UserState state)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(state, Settings), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }
}
