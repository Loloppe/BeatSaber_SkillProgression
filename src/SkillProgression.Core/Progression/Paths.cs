using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Progression
{
    /// <summary>
    /// Which maps count for a path and the rating axis that measures difficulty on it (D-022).
    /// Port of profiles.py path_plays / map_tech_led.
    /// </summary>
    public static class PathRules
    {
        public static bool TechLed(MapRatings r, DestinationThresholds t) => r.Tech >= t.TechRatio * r.Pass && r.Tech >= t.TechMin;

        public static bool IsRelevant(string path, MapRatings r, DestinationThresholds t)
        {
            switch (path)
            {
                case Destinations.Speed: return !TechLed(r, t) && r.Pass > t.LowPass;
                case Destinations.HighTech: return TechLed(r, t);
                case Destinations.Midspeed: return !TechLed(r, t) && r.Pass > t.LowPass && r.Pass <= t.MidPass + 1;
                case Destinations.Accuracy: return r.Pass <= t.LowPass;
                case Destinations.Extreme: return r.Stars >= t.ExtremePathMinStars;
                default: throw new ArgumentException($"Unknown path '{path}'.", nameof(path));
            }
        }

        /// <summary>Difficulty on the path axis: pass for Speed/Midspeed, tech for High Tech, stars otherwise.</summary>
        public static double Axis(string path, MapRatings r)
        {
            switch (path)
            {
                case Destinations.Speed:
                case Destinations.Midspeed: return r.Pass;
                case Destinations.HighTech: return r.Tech;
                case Destinations.Accuracy:
                case Destinations.Extreme: return r.Stars;
                default: throw new ArgumentException($"Unknown path '{path}'.", nameof(path));
            }
        }
    }

    /// <summary>Player's state on a path: path pp (orders waypoints), difficulty reached D and accuracy at it A (D-022).</summary>
    public sealed class PathState
    {
        public string Path { get; set; } = "";
        public double PathPp { get; set; }
        /// <summary>Typical difficulty on the path axis of the top path plays ("push" component).</summary>
        public double Difficulty { get; set; }
        /// <summary>Typical accuracy vs BL predicted acc on those plays ("polish" component).</summary>
        public double AccuracyResidual { get; set; }
        public double Accuracy { get; set; }
        public int PathPlayCount { get; set; }

        public const int TopK = 15;
        public const int MinPlays = 5;

        /// <param name="bestPlays">Best play per map (any order).</param>
        /// <returns>Null when fewer than <see cref="MinPlays"/> path plays.</returns>
        public static PathState? Compute(string path, IEnumerable<RatedPlay> bestPlays, DestinationThresholds t)
        {
            var plays = bestPlays.Where(p => PathRules.IsRelevant(path, p.Ratings, t)).OrderByDescending(p => p.Pp).ToList();
            if (plays.Count < MinPlays) return null;
            var top = plays.Take(TopK).ToList();
            var w = top.Select(p => p.Pp).ToArray();
            double pathPp = 0, decay = 1;
            foreach (var p in plays) { pathPp += p.Pp * decay; decay *= 0.965; }
            return new PathState
            {
                Path = path,
                PathPp = pathPp,
                Difficulty = Stats.WeightedMedian(top.Select(p => PathRules.Axis(path, p.Ratings)).ToArray(), w),
                AccuracyResidual = Stats.WeightedAverage(top.Select(p => p.Accuracy - p.Ratings.PredictedAcc).ToArray(), w),
                Accuracy = Stats.WeightedAverage(top.Select(p => p.Accuracy).ToArray(), w),
                PathPlayCount = plays.Count,
            };
        }
    }
}
