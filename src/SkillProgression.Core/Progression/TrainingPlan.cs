using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;

namespace SkillProgression.Core.Progression
{
    /// <summary>A short-term step toward the next waypoint (D-029: smaller steps that feel achievable).</summary>
    public sealed class ShortTermStep
    {
        public ObjectiveKind Kind { get; set; }
        /// <summary>New maps around the current level (D-051): <see cref="Target"/> is that level, not a harder one.</summary>
        public bool AtLevel { get; set; }
        public double Current { get; set; }
        /// <summary>The step's target (axis rating for push, acc vs predicted for polish).</summary>
        public double Target { get; set; }
        /// <summary>The waypoint target this step leads to.</summary>
        public double WaypointTarget { get; set; }
        /// <summary>1-based step number within the waypoint gap, and how many steps the gap is cut into.</summary>
        public int Number { get; set; }
        public int Count { get; set; }
    }

    public sealed class NextUpItem
    {
        public MapRecommendation Recommendation { get; set; } = new MapRecommendation();
        public ObjectiveKind Kind { get; set; }
        /// <summary>Short-term accuracy target for this map (smaller than the waypoint target, D-029).</summary>
        public double TargetAccuracy { get; set; }
        /// <summary>Unsuccessful attempts since the last success (D-030).</summary>
        public int RecentMisses { get; set; }
        public bool Demoted => RecentMisses >= TrainingPlanner.MissesBeforeDemotion;
    }

    /// <summary>
    /// Turns a path analysis into what the player sees: short-term steps and the "Next up" list for the focus style
    /// (D-025, D-029, D-030). Pure function of the report, the local play log and the skipped maps.
    /// </summary>
    /// <summary>
    /// Adaptive step sizes (D-042): the number of short-term steps to the next waypoint follows recent results on the
    /// style. Reaching the target +1, a fail or No Fail soft fail -1, a clear under the target 0; the mean over the last
    /// <see cref="RecentPlays"/> sets 3 steps (always succeeding) to 6 (always failing), 4 when neutral or too few plays.
    /// </summary>
    public static class StepSizer
    {
        public const int MinSteps = 3;
        public const int MaxSteps = 6;
        public const int RecentPlays = 20;
        public const int MinPlays = 5;

        /// <param name="plays">Plugin plays on the style's maps (any order); practice, quits and restarts are ignored.</param>
        public static double Score(IEnumerable<PlayRecord> plays)
        {
            var recent = plays.Where(p => !p.Practice && (p.Outcome == PlayOutcome.Cleared || p.Outcome == PlayOutcome.Failed))
                .OrderByDescending(p => p.PlayedAtUtc).Take(RecentPlays).ToList();
            if (recent.Count < MinPlays) return 0;
            return recent.Average(p => p.ReachedTarget() ? 1.0 : p.IsPass() ? 0.0 : -1.0);
        }

        public static int StepsPerGap(IEnumerable<PlayRecord> plays)
        {
            var score = Score(plays);
            var steps = score >= 0
                ? TrainingPlanner.StepsPerWaypointGap - score * (TrainingPlanner.StepsPerWaypointGap - MinSteps)
                : TrainingPlanner.StepsPerWaypointGap - score * (MaxSteps - TrainingPlanner.StepsPerWaypointGap);
            return Math.Max(MinSteps, Math.Min(MaxSteps, (int)Math.Round(steps, MidpointRounding.AwayFromZero)));
        }
    }

    public static class TrainingPlanner
    {
        public const int StepsPerWaypointGap = 4;
        public const double MinPushStep = 0.1;
        public const double MinPolishStep = 0.002;
        /// <summary>A polish target never asks for more than this accuracy gain on one map in one step.</summary>
        public const double MaxPolishGainPerStep = 0.005;
        public const int MissesBeforeDemotion = 3;
        public const int DefaultNextUpCount = 5;

        public static ShortTermStep Step(Objective objective) => Step(objective, StepsPerWaypointGap);

        /// <param name="stepsPerGap">Steps between the current level and the next waypoint (adaptive, D-042).</param>
        public static ShortTermStep Step(Objective objective, int stepsPerGap)
        {
            var min = objective.Kind == ObjectiveKind.Push ? MinPushStep : MinPolishStep;
            var gap = Math.Max(0, objective.TargetValue - objective.CurrentValue);
            var size = Math.Max(min, gap / Math.Max(1, stepsPerGap));
            var count = Math.Max(1, (int)Math.Ceiling(gap / size - 1e-9));
            return new ShortTermStep
            {
                Kind = objective.Kind,
                AtLevel = objective.AtLevel,
                Current = objective.CurrentValue,
                Target = Math.Min(objective.TargetValue, objective.CurrentValue + size),
                WaypointTarget = objective.TargetValue,
                Number = 1,
                Count = count,
            };
        }

        /// <summary>
        /// Next up for the focus path: maps from its objectives in priority order, interleaved, without skipped maps;
        /// maps with 3+ recent misses move to the end (D-030). Short-term targets are used (D-029).
        /// </summary>
        public static List<NextUpItem> NextUp(PathProgress? progress, IReadOnlyCollection<PlayRecord> playLog,
            ISet<MapKey> skipped, int count = DefaultNextUpCount, int stepsPerGap = StepsPerWaypointGap) =>
            NextUpFromObjectives(progress?.Objectives ?? new List<Objective>(), playLog, skipped, count, stepsPerGap);

        /// <summary>Same as above for any objectives (e.g. a starter objective when there is no path progress yet).</summary>
        public static List<NextUpItem> NextUpFromObjectives(IReadOnlyList<Objective> objectives, IReadOnlyCollection<PlayRecord> playLog,
            ISet<MapKey> skipped, int count = DefaultNextUpCount, int stepsPerGap = StepsPerWaypointGap)
        {
            var result = new List<NextUpItem>();
            if (objectives.Count == 0) return result;

            var queues = objectives.Select(o => new Queue<MapRecommendation>(o.Maps)).ToList();
            var seen = new HashSet<MapKey>();
            var candidates = new List<NextUpItem>();
            while (queues.Any(q => q.Count > 0))
            {
                for (var i = 0; i < queues.Count; i++)
                {
                    if (queues[i].Count == 0) continue;
                    var rec = queues[i].Dequeue();
                    if (skipped.Contains(rec.Map) || !seen.Add(rec.Map)) continue;
                    var objective = objectives[i];
                    var target = ShortTermAccuracy(objective, rec, stepsPerGap);
                    candidates.Add(new NextUpItem
                    {
                        Recommendation = rec, Kind = objective.Kind, TargetAccuracy = target,
                        RecentMisses = RecentMisses(playLog, rec.Map, target),
                    });
                }
            }

            // Stable: keep priority order, demoted maps after the others.
            result.AddRange(candidates.Where(c => !c.Demoted));
            result.AddRange(candidates.Where(c => c.Demoted));
            return result.Take(count).ToList();
        }

        /// <summary>Push: map's predicted acc + the next short-term step's residual. Polish: a small gain on the PB.</summary>
        /// <param name="stepsPerGap">Adaptive steps (D-042): the per-map gain cap scales with the step size.</param>
        public static double ShortTermAccuracy(Objective objective, MapRecommendation rec, int stepsPerGap = StepsPerWaypointGap)
        {
            if (objective.Kind == ObjectiveKind.Polish && rec.CurrentAccuracy is double current)
                return Math.Min(rec.TargetAccuracy, current + MaxPolishGainPerStep * StepsPerWaypointGap / Math.Max(1, stepsPerGap));
            return rec.TargetAccuracy;
        }

        /// <summary>Unsuccessful attempts (fail or clear below target) on a map since its last successful attempt.</summary>
        public static int RecentMisses(IEnumerable<PlayRecord> playLog, MapKey map, double targetAccuracy)
        {
            var misses = 0;
            foreach (var play in playLog.Where(p => !p.Practice && Matches(p, map)).OrderBy(p => p.PlayedAtUtc))
            {
                // A No Fail soft fail is reported as cleared: it counts as a miss, never as a success.
                var success = play.IsPass() && play.Accuracy >= targetAccuracy;
                if (success) misses = 0;
                else if (play.Outcome == PlayOutcome.Cleared || play.Outcome == PlayOutcome.Failed) misses++;
                // Quit / restart: not counted either way.
            }
            return misses;
        }

        private static bool Matches(PlayRecord p, MapKey map) =>
            string.Equals(p.Hash, map.Hash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Difficulty, map.Difficulty, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Characteristic, map.Characteristic, StringComparison.OrdinalIgnoreCase);
    }
}
