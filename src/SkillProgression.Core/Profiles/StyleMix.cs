using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Model;

namespace SkillProgression.Core.Profiles
{
    /// <summary>
    /// A player's style mix (review round 3, replaces the single "arrived" label): the share of top-play pp from each
    /// style's maps, plus which styles are established. Players are often mixes (e.g. "accuracy + speed").
    /// </summary>
    public sealed class StyleMix
    {
        /// <summary>Share of top-play pp per destination; the partition sums to 1.</summary>
        public Dictionary<string, double> Shares { get; } = new Dictionary<string, double>();
        /// <summary>Styles that meet that destination's criteria on their own maps, by descending share.</summary>
        public List<string> Established { get; } = new List<string>();
        /// <summary>Largest established style, or Unspecialised.</summary>
        public string Main => Established.Count > 0 ? Established[0] : Destinations.Unspecialised;
    }

    public static class StyleMixCalculator
    {
        /// <summary>A style needs at least this share of top-play pp to be considered established.</summary>
        public const double MinShare = 0.25;
        /// <summary>Extreme is a focus on very hard maps: it needs a larger share (user definition "mostly").</summary>
        public const double MinExtremeShare = 0.4;

        /// <summary>
        /// Assigns each top play to exactly one style (Extreme, Accuracy, High Tech, Midspeed, Speed — in that order)
        /// using pass/tech ratings (D-020/D-021) and effective stars for Extreme — the same tech rule as the paths.
        /// BeatLeader's "tech" tag is not used: the maps it adds are mostly fast speed/extreme maps (D-053).
        /// </summary>
        public static string StyleOf(MapRatings r, DestinationThresholds t)
        {
            if (r.Stars >= t.ExtremeStars) return Destinations.Extreme;
            if (r.Pass <= t.LowPass) return Destinations.Accuracy;
            if (r.Tech >= t.TechRatio * r.Pass && r.Tech >= t.TechMin) return Destinations.HighTech;
            return r.Pass <= t.MidPass ? Destinations.Midspeed : Destinations.Speed;
        }

        public static StyleMix Compute(IEnumerable<RatedPlay> plays, DestinationThresholds t, int topN = ProfileBuilder.DefaultTopN)
        {
            var best = ProfileBuilder.BestPerMap(plays);
            var top = best.Where(p => p.Pp > 0).Take(topN).ToList();
            var mix = new StyleMix();
            foreach (var d in Destinations.All) mix.Shares[d] = 0;
            var total = top.Sum(p => p.Pp);
            if (total <= 0) return mix;

            var groups = top.GroupBy(p => StyleOf(p.Ratings, t)).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var g in groups) mix.Shares[g.Key] = g.Value.Sum(p => p.Pp) / total;

            var ceiling = Stats.Quantile(best.Where(p => p.Pp > 0).Select(p => p.Ratings.Pass).ToArray(), 0.95);
            foreach (var d in Destinations.All)
            {
                if (!groups.TryGetValue(d, out var g)) continue;
                var share = mix.Shares[d];
                if (share < (d == Destinations.Extreme ? MinExtremeShare : MinShare)) continue;
                var w = g.Select(p => p.Pp).ToArray();
                var accResid = Stats.WeightedAverage(g.Select(p => p.Accuracy - p.Ratings.PredictedAcc).ToArray(), w);
                var pass = Stats.WeightedMedian(g.Select(p => p.Ratings.Pass).ToArray(), w);
                var tech = Stats.WeightedMedian(g.Select(p => p.Ratings.Tech).ToArray(), w);
                var ok = d switch
                {
                    Destinations.Accuracy => accResid >= t.AccResid,
                    Destinations.Midspeed => accResid >= t.AccResid && ceiling >= pass + t.MidGap,
                    Destinations.HighTech => tech >= t.TechArrive,
                    Destinations.Speed => pass >= t.SpeedArrive,
                    Destinations.Extreme => true,
                    _ => false,
                };
                if (ok) mix.Established.Add(d);
            }
            mix.Established.Sort((a, b) => mix.Shares[b].CompareTo(mix.Shares[a]));
            return mix;
        }
    }
}
