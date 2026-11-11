using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;

namespace SkillProgression.Core.Profiles
{
    /// <summary>
    /// Builds a <see cref="PlayerProfile"/> from rated plays. Port of training/explore/profiles.py `profile`:
    /// best play per map (highest pp), top N by pp, pp-weighted features. Keep both in sync (D-017).
    /// </summary>
    public static class ProfileBuilder
    {
        public const int DefaultTopN = 30;
        public const double LongMapSeconds = 240;

        /// <summary>Best play per map: the one with the highest pp.</summary>
        public static List<RatedPlay> BestPerMap(IEnumerable<RatedPlay> plays)
        {
            var best = new Dictionary<MapKey, RatedPlay>();
            foreach (var p in plays)
                if (!best.TryGetValue(p.Evidence.Map, out var cur) || p.Pp > cur.Pp)
                    best[p.Evidence.Map] = p;
            return best.Values.OrderByDescending(p => p.Pp).ToList();
        }

        /// <returns>Null when there are no plays with pp.</returns>
        public static PlayerProfile? Build(IEnumerable<RatedPlay> plays, int topN = DefaultTopN)
        {
            var best = BestPerMap(plays);
            var top = best.Where(p => p.Pp > 0).Take(topN).ToList();
            if (top.Count == 0) return null;

            var w = top.Select(p => p.Pp).ToArray();
            double Avg(Func<RatedPlay, double> f) => Stats.WeightedAverage(top.Select(f).ToArray(), w);
            double Share(Func<RatedPlay, bool> f) => Stats.Share(w, top.Select(f).ToArray());
            double ShareIn(Func<RatedPlay, bool> flag, Func<RatedPlay, bool> band) =>
                Stats.ShareWithin(w, top.Select(flag).ToArray(), top.Select(band).ToArray());

            var stars = top.Select(p => p.Ratings.Stars).ToArray();
            var ceiling = Stats.Quantile(best.Where(p => p.Pp > 0).Select(p => p.Ratings.Pass).ToArray(), 0.95);
            var pass = Avg(p => p.Ratings.Pass);
            bool IsTech(RatedPlay p) => p.Map.MapType == "tech";

            var profile = new PlayerProfile
            {
                PlayCount = best.Count,
                TopPlayCount = top.Count,
                Stars = Stats.WeightedAverage(stars, w),
                Pass = pass,
                AccRating = Avg(p => p.Ratings.Acc),
                Tech = Avg(p => p.Ratings.Tech),
                Accuracy = Avg(p => p.Accuracy),
                PassCeiling = ceiling,
                Duration = Avg(p => p.Map.Duration),
                Ebpm = Avg(p => p.Map.PeakEbpm),
                TopPpSum = w.Sum(),
                PassMedian = Stats.WeightedMedian(top.Select(p => p.Ratings.Pass).ToArray(), w),
                TechMedian = Stats.WeightedMedian(top.Select(p => p.Ratings.Tech).ToArray(), w),
                StarsMedian = Stats.WeightedMedian(stars, w),
                ShareUnder5 = Share(p => p.Ratings.Stars < 5),
                Share5To9 = Share(p => p.Ratings.Stars >= 5 && p.Ratings.Stars < 9),
                Share9To13 = Share(p => p.Ratings.Stars >= 9 && p.Ratings.Stars < 13),
                Share13Plus = Share(p => p.Ratings.Stars >= 13),
                TechShare5To9 = ShareIn(IsTech, p => p.Ratings.Stars >= 5 && p.Ratings.Stars < 9),
                TechShare9Plus = ShareIn(IsTech, p => p.Ratings.Stars >= 9),
                TechShare5Plus = ShareIn(IsTech, p => p.Ratings.Stars >= 5),
            };

            var meanStars = stars.Average();
            profile.Style[StyleFeatures.BelowCeiling] = 1.0 - pass / Math.Max(ceiling, 1e-6);
            profile.Style[StyleFeatures.TechVsPass] = profile.Tech / Math.Max(pass, 1e-6);
            profile.Style[StyleFeatures.AccResidual] = Avg(p => p.Accuracy - p.Ratings.PredictedAcc);
            profile.Style[StyleFeatures.TypeAcc] = Share(p => p.Map.MapType == "acc");
            profile.Style[StyleFeatures.TypeTech] = Share(IsTech);
            profile.Style[StyleFeatures.TypeSpeed] = Share(p => p.Map.MapType == "speed");
            profile.Style[StyleFeatures.StarSpread] = Stats.StdDev(stars) / Math.Max(meanStars, 1e-6);
            profile.Style[StyleFeatures.SpeedMods] = Share(p => p.UsesSpeedModifier);
            profile.Style[StyleFeatures.LongMaps] = Share(p => p.Map.Duration > LongMapSeconds);
            profile.Style[StyleFeatures.Sub95] = Share(p => p.Accuracy < 0.95);
            return profile;
        }
    }
}
