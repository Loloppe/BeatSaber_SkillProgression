using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Progression
{
    public enum ExploreMode
    {
        /// <summary>Maps the player has a score on and is expected to beat.</summary>
        Improve,
        /// <summary>Maps the player has never played.</summary>
        NewMaps,
    }

    public sealed class ExploreItem
    {
        public MapKey Map { get; set; }
        public string Name { get; set; } = "";
        public double AxisRating { get; set; }
        public double Stars { get; set; }
        public double ExpectedAccuracy { get; set; }
        public double? CurrentAccuracy { get; set; }
    }

    /// <summary>
    /// Personal expected accuracy on a style (Explore side feature, 2026-11-10): the map's BL predicted acc adjusted by
    /// how the player performs relative to predictions on that style, as a function of difficulty (pp-weighted linear
    /// fit over their best plays on the style; harder maps usually pull the residual down). Falls back to the player's
    /// overall residual, then to the prediction itself.
    /// </summary>
    public sealed class AccuracyEstimator
    {
        public const int MinPlaysForSlope = 8;
        private readonly double _intercept;
        private readonly double _slope;

        public AccuracyEstimator(double intercept, double slope)
        {
            _intercept = intercept;
            _slope = slope;
        }

        public double Residual(double axis) => _intercept + _slope * axis;

        public double Expected(MapRatings ratings, string path) =>
            Clamp(ratings.PredictedAcc + Residual(PathRules.Axis(path, ratings)), 0.5, 0.9995);

        public static AccuracyEstimator Fit(string path, IReadOnlyList<RatedPlay> bestPlays, DestinationThresholds t)
        {
            var plays = bestPlays.Where(p => p.Pp > 0 && PathRules.IsRelevant(path, p.Ratings, t)).ToList();
            if (plays.Count == 0)
            {
                var all = bestPlays.Where(p => p.Pp > 0).ToList();
                var overall = all.Count == 0 ? 0 : Stats.WeightedAverage(all.Select(p => p.Accuracy - p.Ratings.PredictedAcc).ToArray(), all.Select(p => p.Pp).ToArray());
                return new AccuracyEstimator(overall, 0);
            }

            var x = plays.Select(p => PathRules.Axis(path, p.Ratings)).ToArray();
            var y = plays.Select(p => p.Accuracy - p.Ratings.PredictedAcc).ToArray();
            var w = plays.Select(p => p.Pp).ToArray();
            var mx = Stats.WeightedAverage(x, w);
            var my = Stats.WeightedAverage(y, w);
            if (plays.Count < MinPlaysForSlope) return new AccuracyEstimator(my, 0);

            double sxy = 0, sxx = 0;
            for (var i = 0; i < x.Length; i++)
            {
                sxy += w[i] * (x[i] - mx) * (y[i] - my);
                sxx += w[i] * (x[i] - mx) * (x[i] - mx);
            }
            // Harder maps should never be expected to raise accuracy vs prediction: clamp the slope at 0.
            var slope = sxx > 1e-9 ? Math.Min(0, sxy / sxx) : 0;
            return new AccuracyEstimator(my - slope * mx, slope);
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    public sealed class ExploreResult
    {
        public List<ExploreItem> Items { get; set; } = new List<ExploreItem>();
        /// <summary>Difficulty range actually used (axis units of the style).</summary>
        public double RangeLow { get; set; }
        public double RangeHigh { get; set; }
        /// <summary>True when the range had to grow to find enough maps (D-032 A).</summary>
        public bool Widened { get; set; }
    }

    /// <summary>
    /// Explore side feature (D-031/D-032): fresh lists of maps to improve or to discover, at a chosen difficulty offset.
    /// The window stays inside the range where the style has maps, and widens when too few candidates remain.
    /// </summary>
    public static class Explorer
    {
        public const int DefaultCount = 6;
        public const int MinOffset = -3, MaxOffset = 3;
        /// <summary>Minimum expected gain over the PB for an "Improve" suggestion.</summary>
        public const double MinGain = 0.002;
        /// <summary>Below this many candidates the window widens, one step at a time, up to MaxWiden steps.</summary>
        public const int MinCandidates = 10;
        public const int MaxWiden = 4;

        private sealed class Context
        {
            public string Path = "";
            public ExploreMode Mode;
            public double Level, Step, AxisMin, AxisMax;
            public AccuracyEstimator Estimator = new AccuracyEstimator(0, 0);
            public List<(ExploreItem item, double rank)> Pool = new List<(ExploreItem, double)>();
        }

        /// <param name="offset">Difficulty steps relative to the player's level on the style (negative = easier).</param>
        /// <param name="seed">Different seeds give different lists ("New list").</param>
        public static ExploreResult Suggest(string path, ExploreMode mode, int offset, IReadOnlyList<RatedPlay> bestPlays,
            ModelFile model, MapCatalog catalog, int seed, int count = DefaultCount)
        {
            var ctx = Prepare(path, mode, bestPlays, model, catalog);
            var center = Center(ctx, offset);
            var half = ctx.Step;
            var widened = false;
            var candidates = InWindow(ctx, center, half);
            for (var i = 0; i < MaxWiden && candidates.Count < MinCandidates; i++)
            {
                half += ctx.Step;
                widened = true;
                candidates = InWindow(ctx, center, half);
            }

            var ranked = candidates.OrderByDescending(c => c.rank).Select(c => c.item).ToList();
            return new ExploreResult
            {
                Items = Pick(ranked, count, new Random(seed)),
                RangeLow = Math.Max(ctx.AxisMin, center - half),
                RangeHigh = Math.Min(ctx.AxisMax, center + half),
                Widened = widened,
            };
        }

        /// <summary>Candidates per slider position (MinOffset..MaxOffset) without widening (D-032 B).</summary>
        public static int[] Counts(string path, ExploreMode mode, IReadOnlyList<RatedPlay> bestPlays, ModelFile model, MapCatalog catalog)
        {
            var ctx = Prepare(path, mode, bestPlays, model, catalog);
            return Enumerable.Range(MinOffset, MaxOffset - MinOffset + 1)
                .Select(o => InWindow(ctx, Center(ctx, o), ctx.Step).Count).ToArray();
        }

        private static Context Prepare(string path, ExploreMode mode, IReadOnlyList<RatedPlay> bestPlays, ModelFile model, MapCatalog catalog)
        {
            var t = model.Thresholds;
            var pathModel = model.Paths.FirstOrDefault(p => p.Destination == path);
            var spread = pathModel?.DifficultySpread > 0 ? pathModel.DifficultySpread : 1.0;
            var ctx = new Context
            {
                Path = path, Mode = mode, Step = Math.Max(0.25, spread * 0.35),
                Estimator = AccuracyEstimator.Fit(path, bestPlays, t),
                Level = PathState.Compute(path, bestPlays, t)?.Difficulty
                        ?? pathModel?.Waypoints.OrderBy(w => w.Index).FirstOrDefault()?.Difficulty ?? 0,
            };

            if (mode == ExploreMode.Improve)
            {
                ctx.Pool = bestPlays
                    .Where(p => PathRules.IsRelevant(path, p.Ratings, t) && catalog.IsRecommendable(p.Map, path))
                    .Select(p => new ExploreItem
                    {
                        Map = p.Evidence.Map, Name = p.Map.Name, AxisRating = PathRules.Axis(path, p.Ratings), Stars = p.Ratings.Stars,
                        ExpectedAccuracy = ctx.Estimator.Expected(p.Ratings, path), CurrentAccuracy = p.Accuracy,
                    })
                    .Where(i => i.ExpectedAccuracy - i.CurrentAccuracy >= MinGain)
                    .Select(i => (i, i.ExpectedAccuracy - i.CurrentAccuracy!.Value))
                    .ToList();
                return WithRange(ctx);
            }

            var played = new HashSet<MapKey>(bestPlays.Select(p => p.Evidence.Map));
            var popularityKeys = pathModel?.Waypoints.Select(w => $"{path}|{w.Index}").ToArray() ?? Array.Empty<string>();
            ctx.Pool = catalog.Recommendable(path)
                .Where(m => m.Ratings.TryGetValue("", out var r) && PathRules.IsRelevant(path, r, t))
                .Select(m => (map: m, key: new MapKey(m.Hash, m.Characteristic, m.Difficulty), r: m.Ratings[""]))
                .Where(x => !played.Contains(x.key))
                .Select(x => (new ExploreItem
                {
                    Map = x.key, Name = x.map.Name, AxisRating = PathRules.Axis(path, x.r), Stars = x.r.Stars, ExpectedAccuracy = ctx.Estimator.Expected(x.r, path),
                }, (double)popularityKeys.Sum(k => x.map.PeerPopularity.TryGetValue(k, out var n) ? n : 0)))
                .ToList();
            return WithRange(ctx);
        }

        /// <summary>The slider is clamped to where candidates of the current mode exist (not all maps of the style).</summary>
        private static Context WithRange(Context ctx)
        {
            ctx.AxisMin = ctx.Pool.Count > 0 ? ctx.Pool.Min(p => p.item.AxisRating) : ctx.Level;
            ctx.AxisMax = ctx.Pool.Count > 0 ? ctx.Pool.Max(p => p.item.AxisRating) : ctx.Level;
            return ctx;
        }

        /// <summary>Window centre for a slider offset, kept inside the range where the style has maps.</summary>
        private static double Center(Context ctx, int offset)
        {
            var c = ctx.Level + offset * ctx.Step;
            var lo = Math.Min(ctx.AxisMin + ctx.Step, ctx.AxisMax);
            var hi = Math.Max(ctx.AxisMax - ctx.Step, lo);
            return c < lo ? lo : c > hi ? hi : c;
        }

        private static List<(ExploreItem item, double rank)> InWindow(Context ctx, double center, double half) =>
            ctx.Pool.Where(p => p.item.AxisRating >= center - half && p.item.AxisRating <= center + half).ToList();

        /// <summary>Random pick among the best few times <paramref name="count"/> candidates, shown in ranked order.</summary>
        private static List<ExploreItem> Pick(List<ExploreItem> ranked, int count, Random rng)
        {
            var pool = ranked.Take(count * 4).Select((item, rank) => (item, rank)).ToList();
            return pool.OrderBy(_ => rng.Next()).Take(count).OrderBy(x => x.rank).Select(x => x.item).ToList();
        }
    }
}
