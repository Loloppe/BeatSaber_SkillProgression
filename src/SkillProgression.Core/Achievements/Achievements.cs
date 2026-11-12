using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;

namespace SkillProgression.Core.Achievements
{
    public enum AchievementKind
    {
        Milestone,
        /// <summary>Retired (D-035); kept so stored achievements still load.</summary>
        Step,
        /// <summary>Retired (D-035); kept so stored achievements still load.</summary>
        StyleEstablished,
        PersonalRecord,
        Consistency,
        /// <summary>Retired (D-035); kept so stored achievements still load.</summary>
        Comeback,
        Platform,
    }

    /// <summary>An unlocked achievement. <see cref="Id"/> is stable so nothing unlocks twice.</summary>
    public sealed class Achievement
    {
        public string Id { get; set; } = "";
        public AchievementKind Kind { get; set; }
        public string Title { get; set; } = "";
        public DateTime UnlockedUtc { get; set; }
        /// <summary>Already earned before tracking started (first import): recorded quietly, not celebrated.</summary>
        public bool Retroactive { get; set; }
    }

    /// <summary>Official rank on a platform (category empty for BL/SS).</summary>
    public sealed class PlatformRank
    {
        public string Platform { get; set; } = "";
        public string Category { get; set; } = "";
        public int Rank { get; set; }
        /// <summary>Official points (pp or AP) and their unit; 0 when unknown.</summary>
        public double Points { get; set; }
        public string Unit { get; set; } = "pp";
        public string Name => string.IsNullOrEmpty(Category) ? Platform : $"{Platform} {Category}";
    }

    /// <summary>Daily progression snapshot (history). Aggregates of the player's own data only.</summary>
    public sealed class ProgressSnapshot
    {
        public DateTime DateUtc { get; set; }
        public Dictionary<string, PathSnapshot> Paths { get; set; } = new Dictionary<string, PathSnapshot>();
        public Dictionary<string, double> StyleShares { get; set; } = new Dictionary<string, double>();
        public List<string> Established { get; set; } = new List<string>();
        public List<PlatformRank> Platforms { get; set; } = new List<PlatformRank>();
        /// <summary>Hardest cleared rating per <see cref="RecordAxes"/> key, style-free (D-035). Empty in old snapshots.</summary>
        public Dictionary<string, double> Records { get; set; } = new Dictionary<string, double>();
        /// <summary>Best accuracy per star level (key N = maps from N to N+1 stars), a records table, not achievements (D-035).</summary>
        public Dictionary<int, double> BestAccByStar { get; set; } = new Dictionary<int, double>();
    }

    /// <summary>Personal record axes (D-035): hardest map cleared by BeatLeader rating, whatever the style.</summary>
    public static class RecordAxes
    {
        public const string Stars = "stars";
        public const string Pass = "pass";
        public const string Tech = "tech";
        public const string Acc = "acc";

        /// <summary>Axis and highest tier (1-point tiers; caps from the ranked pool's top ratings, 2026-11-10).</summary>
        public static readonly (string Key, int Max)[] All = { (Stars, 15), (Pass, 18), (Tech, 17), (Acc, 16) };

        public static double Value(string axis, MapRatings r) => axis switch
        {
            Stars => r.Stars,
            Pass => r.Pass,
            Tech => r.Tech,
            _ => r.Acc,
        };
    }

    public sealed class PathSnapshot
    {
        /// <summary>1-based milestone reached.</summary>
        public int Milestone { get; set; }
        public double Progress { get; set; }
        public double Difficulty { get; set; }
        public double AccuracyResidual { get; set; }
        /// <summary>Short-term targets at that time, to detect completed steps later (D-029).</summary>
        public double? NextClearTarget { get; set; }
        public double? NextAccuracyTarget { get; set; }
        /// <summary>Different maps at or above your level on this style, cleared through the plugin in the last 7 days.</summary>
        public int WeeklyMapsAtLevel { get; set; }
    }

    /// <summary>
    /// Detects achievements (D-033, revised D-035): milestone reached, consistency tiers (distinct maps at your level on a
    /// style in a week), style-free personal records (hardest star / pass / tech / acc rating cleared), platform rank and
    /// points thresholds. Never rewards repeating one map.
    /// </summary>
    public static class AchievementEngine
    {
        public static readonly int[] RankThresholds = { 10000, 5000, 2000, 1000, 500, 250, 100, 50, 25, 10 };
        // Official points thresholds (user request 2026-11-10), tuned on live leaderboards the same day: points get
        // harder to gain the higher you climb, so tiers start wide and tighten near the top. Starts: 8,000 pp, 4,000 AP.
        // BL pp at rank 10k/1k/100/10/1 = 8,551/14,869/18,031/20,281/21,862 (SS similar: 3,753/12,158/16,355/19,723/22,008).
        public static readonly int[] PpThresholds =
            { 8000, 10000, 12000, 13500, 15000, 16000, 17000, 17500, 18000, 18500, 19000, 19500, 20000, 20500, 21000, 21500, 22000, 22500 };
        // AccSaber Overall AP at rank 4k/1k/100/10/1 = 5,436/20,799/28,471/31,435/33,006.
        public static readonly int[] ApOverallThresholds =
            { 4000, 8000, 12000, 16000, 20000, 22500, 24000, 25000, 26000, 27000, 28000, 29000, 30000, 31000, 32000, 33000 };
        // AccSaber categories (True/Standard/Tech) top out near 11k AP: rank 1k/100/10/1 ≈ 6.7-7.7k/9.1-9.8k/10.1-10.8k/10.8-11.5k.
        public static readonly int[] ApCategoryThresholds =
            { 4000, 5500, 7000, 8000, 8500, 9000, 9500, 9750, 10000, 10250, 10500, 10750, 11000, 11250, 11500 };

        public static int[] PointThresholds(PlatformRank r) =>
            r.Unit != "AP" ? PpThresholds
            : string.IsNullOrEmpty(r.Category) || r.Category == "Overall" ? ApOverallThresholds : ApCategoryThresholds;
        public static readonly int[] ConsistencyTiers = { 5, 10, 15, 20, 25 };
        public static readonly TimeSpan ConsistencyWindow = TimeSpan.FromDays(7);

        /// <param name="recentLocalPlays">Plays made through the plugin (rated), for consistency.</param>
        public static ProgressSnapshot Snapshot(PlayerReport report, IReadOnlyList<RatedPlay> bestPlays, IReadOnlyList<RatedPlay> recentLocalPlays,
            DestinationThresholds t, IEnumerable<PlatformRank> platforms, DateTime nowUtc)
        {
            var s = new ProgressSnapshot
            {
                DateUtc = nowUtc.Date,
                StyleShares = new Dictionary<string, double>(report.StyleMix.Shares),
                Established = report.StyleMix.Established.ToList(),
                Platforms = platforms.ToList(),
            };
            foreach (var axis in RecordAxes.All)
                s.Records[axis.Key] = bestPlays.Select(p => RecordAxes.Value(axis.Key, p.Ratings)).DefaultIfEmpty(0).Max();
            s.BestAccByStar = BestAccByStar(bestPlays);
            foreach (var kv in report.Paths)
            {
                var p = kv.Value;
                var push = p.Objectives.FirstOrDefault(o => o.Kind == ObjectiveKind.Push && !o.AtLevel);
                var polish = p.Objectives.FirstOrDefault(o => o.Kind == ObjectiveKind.Polish);
                s.Paths[kv.Key] = new PathSnapshot
                {
                    Milestone = p.Current!.Index + 1,
                    Progress = p.ProgressToNext,
                    Difficulty = p.State.Difficulty,
                    AccuracyResidual = p.State.AccuracyResidual,
                    NextClearTarget = push != null ? TrainingPlanner.Step(push).Target : (double?)null,
                    NextAccuracyTarget = polish != null ? TrainingPlanner.Step(polish).Target : (double?)null,
                    WeeklyMapsAtLevel = WeeklyMapsAtLevel(kv.Key, p.State.Difficulty, recentLocalPlays, t, nowUtc),
                };
            }
            return s;
        }

        /// <summary>Best accuracy on maps of each star level (N to N+1 stars, effective ratings with speed modifiers).</summary>
        public static Dictionary<int, double> BestAccByStar(IEnumerable<RatedPlay> bestPlays) =>
            bestPlays.Where(p => p.Ratings.Stars >= 1)
                .GroupBy(p => (int)Math.Floor(p.Ratings.Stars))
                .ToDictionary(g => g.Key, g => g.Max(p => p.Accuracy));

        /// <summary>Different maps (not plays) at or above <paramref name="difficulty"/> on the path, cleared in the last 7 days.</summary>
        public static int WeeklyMapsAtLevel(string path, double difficulty, IEnumerable<RatedPlay> recentLocalPlays, DestinationThresholds t, DateTime nowUtc) =>
            recentLocalPlays
                .Where(r => nowUtc - r.Evidence.TimeUtc <= ConsistencyWindow && PathRules.IsRelevant(path, r.Ratings, t)
                            && PathRules.Axis(path, r.Ratings) >= difficulty)
                .Select(r => r.Evidence.Map).Distinct().Count();

        /// <param name="previous">Last saved snapshot (null on first run: everything found is retroactive).</param>
        public static List<Achievement> Detect(ProgressSnapshot? previous, ProgressSnapshot current, ISet<string> unlocked, DateTime nowUtc)
        {
            var found = new List<Achievement>();
            var retroactive = previous == null;
            void Add(string id, AchievementKind kind, string title, bool quiet = false)
            {
                if (unlocked.Contains(id) || found.Any(a => a.Id == id)) return;
                found.Add(new Achievement { Id = id, Kind = kind, Title = title, UnlockedUtc = nowUtc, Retroactive = retroactive || quiet });
            }

            foreach (var kv in current.Paths)
            {
                var path = kv.Key;
                var p = kv.Value;
                // Milestone 1 is the entry point of a path, not an achievement.
                for (var m = 2; m <= p.Milestone; m++)
                    Add($"milestone|{path}|{m}", AchievementKind.Milestone, $"{path} milestone {m} reached");

                foreach (var tier in ConsistencyTiers.Where(n => p.WeeklyMapsAtLevel >= n))
                    Add($"consistency|{path}|{tier}", AchievementKind.Consistency, ConsistencyTitle(path, tier));
            }

            // Records are new in older progress files: the first time they are computed, everything found is quiet.
            var recordsQuiet = previous != null && previous.Records.Count == 0;
            foreach (var axis in RecordAxes.All)
                if (current.Records.TryGetValue(axis.Key, out var hardest))
                    for (var tier = 1; tier <= Math.Min(axis.Max, (int)Math.Floor(hardest + 1e-9)); tier++)
                        Add($"best|{axis.Key}|{tier}", AchievementKind.PersonalRecord, RecordTitle(axis.Key, tier), quiet: recordsQuiet);

            foreach (var r in current.Platforms)
            {
                // Platform ranks load separately: the first time a platform is seen, its thresholds are not "new".
                var firstSeen = previous == null || previous.Platforms.All(p => p.Name != r.Name);
                foreach (var threshold in RankThresholds.Where(th => r.Rank > 0 && r.Rank <= th))
                    Add($"platform|{r.Name}|{threshold}", AchievementKind.Platform, $"{r.Name} top {threshold:N0}", quiet: firstSeen);
                foreach (var threshold in PointThresholds(r).Where(th => r.Points >= th))
                    Add($"points|{r.Name}|{threshold}", AchievementKind.Platform, $"{r.Name} {threshold:N0} {r.Unit}", quiet: firstSeen);
            }

            return found;
        }

        public static string ConsistencyTitle(string path, int tier) => $"{tier} different {path} maps at your level in a week";

        public static string RecordTitle(string axis, int tier) => axis switch
        {
            RecordAxes.Stars => $"Clear a {tier}+ star map",
            RecordAxes.Pass => $"Clear a {tier}+ pass rating map",
            RecordAxes.Tech => $"Clear a {tier}+ tech rating map",
            _ => $"Clear a {tier}+ acc rating map",
        };
    }
}
