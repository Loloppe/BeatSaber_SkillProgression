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

        /// <summary>
        /// Whether a map counts for a path. AccSaber tracks (D-061): the map's AccSaber category; styles:
        /// <see cref="IsRelevant(string, MapRatings, DestinationThresholds)"/> on the ratings.
        /// </summary>
        public static bool IsRelevant(string path, MapEntry map, MapRatings r, DestinationThresholds t) =>
            AccSaberTracks.IsTrack(path)
                ? map.RankedAccSaber && map.AccSaberComplexity > 0 && map.AccSaberCategory == AccSaberTracks.Code(path)
                : ScoreSaberStyles.IsSsPath(path)
                ? map.RankedScoreSaber && map.ScoreSaberStars > 0 && IsRelevant(ScoreSaberStyles.Style(path), NoModifier(map, r), t)
                : IsRelevant(path, r, t);

        /// <summary>
        /// D-062: ScoreSaber gives speed modifiers no pp, so its styles rate every play at the map's no-modifier ratings
        /// (falling back to the given ones when the map has none).
        /// </summary>
        private static MapRatings NoModifier(MapEntry map, MapRatings r) => map.Ratings.TryGetValue("", out var nomod) ? nomod : r;

        public static bool IsRelevant(string path, RatedPlay play, DestinationThresholds t) => IsRelevant(path, play.Map, play.Ratings, t);

        /// <summary>
        /// Whether a map may be recommended on a path (Next up, starters, calibration, Explore): in the catalog's pool and
        /// of the path's style — except the AccSaber pool, where every AccSaber map is an Accuracy map (user, 2026-11-11;
        /// 4 of ~600 have a pass rating above the Accuracy limit). Progress is still measured with <see cref="IsRelevant"/>.
        /// </summary>
        public static bool IsRecommendable(MapCatalog catalog, MapEntry map, MapRatings r, string path, DestinationThresholds t)
        {
            var pool = MapPools.Effective(catalog.Pool, path);
            if (!MapPools.Contains(pool, map)) return false;
            if (AccSaberTracks.IsTrack(path) || ScoreSaberStyles.IsSsPath(path)) return IsRelevant(path, map, r, t);
            return pool == MapPool.AccSaber ? path == Destinations.Accuracy : IsRelevant(path, r, t);
        }

        /// <summary>Difficulty on the path axis: AccSaber complexity on AccSaber tracks (D-061), else <see cref="Axis(string, MapRatings)"/>.</summary>
        public static double Axis(string path, MapEntry map, MapRatings r) =>
            AccSaberTracks.IsTrack(path) ? map.AccSaberComplexity
            : ScoreSaberStyles.IsSsPath(path) ? Axis(ScoreSaberStyles.Style(path), NoModifier(map, r))
            : Axis(path, r);

        public static double Axis(string path, RatedPlay play) => Axis(path, play.Map, play.Ratings);

        /// <summary>
        /// D-061 (user, option 2): ranking value of a new map on an AccSaber track — the AP a score at
        /// <paramref name="targetAccuracy"/> would add to the player's category total; null on styles or without a model.
        /// </summary>
        public static Func<double, double, double>? TrackGain(string path, IEnumerable<RatedPlay> bestPlays, ModelFile model)
        {
            if (!AccSaberTracks.IsTrack(path) || model.AccSaber is not { } accSaber) return null;
            var aps = bestPlays.Where(p => IsRelevant(path, p, model.Thresholds) && p.Ap > 0).Select(p => p.Ap)
                .OrderByDescending(x => x).ToList();
            return (complexity, targetAccuracy) => accSaber.Gain(aps, accSaber.Ap(complexity, targetAccuracy));
        }

        /// <summary>What a play is worth on a path: AccSaber AP on AccSaber tracks (D-061), else its pp.</summary>
        public static double Value(string path, RatedPlay play) =>
            AccSaberTracks.IsTrack(path) ? play.Ap : ScoreSaberStyles.IsSsPath(path) ? play.SsPp : play.Pp;

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
        /// <param name="accSaber">AccSaber weights for AccSaber tracks (D-061: path pp = the category's AP as AccSaber sums
        /// it); null = the 0.965 decay.</param>
        /// <returns>Null when fewer than <see cref="MinPlays"/> path plays.</returns>
        /// <summary>
        /// Predicted accuracy a play is compared with: AccSaber tracks (D-061) and ScoreSaber styles (D-062) count speed
        /// modifiers at their raw accuracy, so the no-modifier prediction (as their waypoints use); styles the play's
        /// effective ratings.
        /// </summary>
        private static double PredictedAcc(bool noModifier, RatedPlay p) =>
            noModifier && p.Map.Ratings.TryGetValue("", out var nomod) ? nomod.PredictedAcc : p.Ratings.PredictedAcc;

        public static PathState? Compute(string path, IEnumerable<RatedPlay> bestPlays, DestinationThresholds t, AccSaberModel? accSaber = null)
        {
            var track = AccSaberTracks.IsTrack(path);
            // Tracks and ScoreSaber styles: only plays worth something there (AP / SS pp), at no-modifier predictions.
            var platform = track || ScoreSaberStyles.IsSsPath(path);
            var plays = bestPlays.Where(p => PathRules.IsRelevant(path, p, t) && (!platform || PathRules.Value(path, p) > 0))
                .OrderByDescending(p => PathRules.Value(path, p)).ToList();
            if (plays.Count < MinPlays) return null;
            var top = plays.Take(TopK).ToList();
            var w = top.Select(p => PathRules.Value(path, p)).ToArray();
            double pathPp = 0, decay = 1;
            for (var i = 0; i < plays.Count; i++)
            {
                var value = PathRules.Value(path, plays[i]);
                if (track && accSaber != null) pathPp += value * accSaber.Weight(i);
                else { pathPp += value * decay; decay *= 0.965; }
            }
            return new PathState
            {
                Path = path,
                PathPp = pathPp,
                Difficulty = Stats.WeightedMedian(top.Select(p => PathRules.Axis(path, p)).ToArray(), w),
                AccuracyResidual = Stats.WeightedAverage(top.Select(p => p.Accuracy - PredictedAcc(platform, p)).ToArray(), w),
                Accuracy = Stats.WeightedAverage(top.Select(p => p.Accuracy).ToArray(), w),
                PathPlayCount = plays.Count,
            };
        }
    }
}
