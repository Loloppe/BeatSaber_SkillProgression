using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Progression
{
    /// <summary>A map chosen for one calibration step (persisted by the caller).</summary>
    public sealed class CalibrationPick
    {
        public MapKey Map { get; set; }
        public double Axis { get; set; }
        public double TargetAccuracy { get; set; }
        public DateTime PickedUtc { get; set; }
    }

    /// <summary>The first real result (clear or fail, not practice) on a pick after it was chosen.</summary>
    public sealed class CalibrationResult
    {
        public CalibrationPick Pick { get; set; } = new CalibrationPick();
        public bool Success { get; set; }
        public double Accuracy { get; set; }
        public bool Failed { get; set; }
    }

    public sealed class CalibrationStatus
    {
        public bool Done { get; set; }
        /// <summary>1-based number of the step being played (or the last one when done).</summary>
        public int Step { get; set; }
        public int Total { get; set; } = Calibrator.MaxMaps;
        /// <summary>Axis level the next pick aims at.</summary>
        public double Level { get; set; }
        /// <summary>Estimated current level on the path (never above the old level: recovery ceiling).</summary>
        public double Estimate { get; set; }
        /// <summary>Pick waiting for a result; null when a new one must be chosen (or when done).</summary>
        public CalibrationPick? Current { get; set; }
        public CalibrationResult? Last { get; set; }
    }

    /// <summary>
    /// Fresh-start calibration (D-010, D-040): an adaptive staircase on the path axis, one map at a time. Starts below the
    /// old level, steps up after a success (clear at the target accuracy) and down after a miss, halving the step at each
    /// change of direction. Stops after <see cref="MaxMaps"/> results or once the step is small.
    /// </summary>
    public static class Calibrator
    {
        public const int MaxMaps = 5;
        /// <summary>First pick at this share of the old level (slightly below, D-010).</summary>
        public const double StartShare = 0.85;
        /// <summary>First step as a share of the old level.</summary>
        public const double StepShare = 0.10;
        /// <summary>Stop early once the step is below this share of the old level.</summary>
        public const double MinStepShare = 0.025;

        /// <summary>Level to start from when calibration is skipped: the cautious first-pick level.</summary>
        public static double CautiousLevel(double oldLevel) => oldLevel * StartShare;

        public static List<CalibrationResult> Results(IEnumerable<CalibrationPick> picks, IReadOnlyCollection<PlayRecord> log)
        {
            var results = new List<CalibrationResult>();
            foreach (var pick in picks)
            {
                var play = log
                    .Where(p => !p.Practice && p.PlayedAtUtc >= pick.PickedUtc
                                && (p.Outcome == PlayOutcome.Cleared || p.Outcome == PlayOutcome.Failed)
                                && p.Hash.Length > 0 && p.Difficulty.Length > 0 && new MapKey(p.Hash, p.Characteristic, p.Difficulty).Equals(pick.Map))
                    .OrderBy(p => p.PlayedAtUtc).FirstOrDefault();
                if (play == null) break; // later picks can't have results before this one
                results.Add(new CalibrationResult
                {
                    Pick = pick,
                    Success = play.IsPass() && play.Accuracy >= pick.TargetAccuracy,
                    Accuracy = play.Accuracy,
                    Failed = !play.IsPass(),
                });
            }
            return results;
        }

        public static CalibrationStatus Status(double oldLevel, IReadOnlyList<CalibrationPick> picks, IReadOnlyCollection<PlayRecord> log)
        {
            var results = Results(picks, log);
            var level = oldLevel * StartShare;
            var step = oldLevel * StepShare;
            var lastDirection = 0;
            foreach (var r in results)
            {
                var direction = r.Success ? 1 : -1;
                if (lastDirection != 0 && direction != lastDirection) step /= 2;
                level += direction * step;
                lastDirection = direction;
            }
            var done = results.Count >= MaxMaps || (results.Count >= 2 && step < oldLevel * MinStepShare);
            return new CalibrationStatus
            {
                Done = done,
                Step = Math.Min(MaxMaps, done ? results.Count : results.Count + 1),
                Level = level,
                Estimate = Math.Max(0, Math.Min(oldLevel, level)),
                Current = done || picks.Count <= results.Count ? null : picks[results.Count],
                Last = results.LastOrDefault(),
            };
        }

        /// <summary>Map closest to <paramref name="level"/> on the path, popular with players near that level.</summary>
        public static CalibrationPick? Pick(string path, double level, ModelFile model, MapCatalog catalog, ISet<MapKey> exclude, DateTime nowUtc)
        {
            var pathModel = model.Paths.FirstOrDefault(p => p.Destination == path);
            if (pathModel == null || pathModel.Waypoints.Count == 0) return null;
            var waypoint = pathModel.Waypoints.OrderBy(w => Math.Abs(w.Difficulty - level)).First();
            var popularityKey = $"{path}|{waypoint.Index}";
            var best = catalog.Recommendable(path)
                .Where(m => m.Ratings.TryGetValue("", out var r) && PathRules.IsRelevant(path, r, model.Thresholds))
                .Select(m => (map: m, key: new MapKey(m.Hash, m.Characteristic, m.Difficulty), r: m.Ratings[""]))
                .Where(x => !exclude.Contains(x.key))
                .Select(x => (x.map, x.key, x.r, axis: PathRules.Axis(path, x.r)))
                // Closest in steps of 0.1, then the most popular there.
                .OrderBy(x => Math.Round(Math.Abs(x.axis - level) * 10))
                .ThenByDescending(x => x.map.PeerPopularity.TryGetValue(popularityKey, out var n) ? n : 0)
                .FirstOrDefault();
            if (best.map == null) return null;
            return new CalibrationPick
            {
                Map = best.key, Axis = best.axis, PickedUtc = nowUtc,
                TargetAccuracy = best.r.PredictedAcc + waypoint.AccuracyResidual,
            };
        }
    }
}
