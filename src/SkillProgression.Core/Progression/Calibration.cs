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
    /// Fresh-start calibration (D-010, D-040, D-050): an adaptive staircase on the path axis, one map at a time, between the
    /// easiest map of the pool and the old level (the "range"). Starts at the bottom; steps up after a success (clear at the
    /// target accuracy), doubling the step while nothing was missed, and down after a miss, halving the step at each change
    /// of direction. Never goes outside the range. Stops after <see cref="MaxMaps"/> results, once the step is small, on a
    /// miss at the bottom or on a success at the old level.
    /// </summary>
    public static class Calibrator
    {
        public const int MaxMaps = 7;
        /// <summary>First step up as a share of the range (then 1/4, 1/2... while every map succeeds).</summary>
        public const double FirstStepShare = 0.125;
        /// <summary>Stop early once the step is below this share of the range (after a miss).</summary>
        public const double MinStepShare = 0.1;
        /// <summary>Level used when calibration is skipped (D-040): slightly below the old level.</summary>
        public const double CautiousShare = 0.85;

        /// <summary>Level to start from when calibration is skipped: a cautious share of the old level.</summary>
        public static double CautiousLevel(double oldLevel) => oldLevel * CautiousShare;

        /// <summary>Axis level of the easiest map calibration can pick on the path (current pool); null without maps.</summary>
        public static double? Bottom(string path, ModelFile model, MapCatalog catalog)
        {
            var axes = catalog.Recommendable(path)
                .Where(m => m.Ratings.TryGetValue("", out var r) && PathRules.IsRecommendable(catalog, m, r, path, model.Thresholds))
                .Select(m => PathRules.Axis(path, m.Ratings[""])).ToList();
            return axes.Count == 0 ? (double?)null : axes.Min();
        }

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

        /// <param name="bottom">Easiest map's level (<see cref="Bottom"/>); the first pick's level once there is one, so a
        /// pool change mid-calibration doesn't move the range.</param>
        public static CalibrationStatus Status(double oldLevel, double bottom, IReadOnlyList<CalibrationPick> picks, IReadOnlyCollection<PlayRecord> log)
        {
            var results = Results(picks, log);
            var start = picks.Count > 0 ? picks[0].Axis : bottom;
            var range = oldLevel - start;
            if (range <= 0) // the old level is at the bottom of the pool: nothing to calibrate
                return new CalibrationStatus { Done = true, Level = start, Estimate = Math.Max(0, start), Last = results.LastOrDefault() };

            var level = start;
            var step = range * FirstStepShare;
            var lastDirection = 0;
            var missed = false;
            var edge = false; // missed at the bottom or succeeded at the old level: no point going on
            foreach (var r in results)
            {
                var direction = r.Success ? 1 : -1;
                if (lastDirection != 0 && direction != lastDirection) step /= 2;
                else if (direction == 1 && lastDirection == 1 && !missed) step *= 2;
                edge = direction == 1 ? level >= oldLevel : level <= start;
                level = Math.Max(start, Math.Min(oldLevel, level + direction * step));
                missed |= direction == -1;
                lastDirection = direction;
            }
            var done = results.Count >= MaxMaps || edge || (missed && step < range * MinStepShare);
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
                .Where(m => m.Ratings.TryGetValue("", out var r) && PathRules.IsRecommendable(catalog, m, r, path, model.Thresholds))
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
