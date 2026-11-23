using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Progression
{
    public enum ObjectiveKind
    {
        /// <summary>Clear harder maps on the path axis.</summary>
        Push,
        /// <summary>Raise accuracy on ratings already played.</summary>
        Polish,
    }

    public sealed class MapRecommendation
    {
        public MapKey Map { get; set; }
        public string Name { get; set; } = "";
        public string Mapper { get; set; } = "";
        public double AxisRating { get; set; }
        /// <summary>BeatLeader star rating (same ratings as <see cref="AxisRating"/>).</summary>
        public double Stars { get; set; }
        /// <summary>Note jump speed, 0 = unknown (D-056).</summary>
        public double Njs { get; set; }
        /// <summary>Notes per second, 0 = unknown (D-059).</summary>
        public double Nps { get; set; }
        /// <summary>
        /// Accuracy to aim for: what players at the player's level get on the map (D-058, <see cref="PeerTargets"/>).
        /// Without peer data: played maps (polish) predicted acc + next waypoint's typical residual; new maps (push, D-055)
        /// the player's expected acc on the map + one short-term accuracy step.
        /// </summary>
        public double TargetAccuracy { get; set; }
        /// <summary>Player's best accuracy on it so far (polish), null when never cleared (push).</summary>
        public double? CurrentAccuracy { get; set; }
        public int PeerPopularity { get; set; }
    }

    public sealed class Objective
    {
        public ObjectiveKind Kind { get; set; }
        /// <summary>
        /// Push only (D-051): new maps around the player's own level, because path pp (breadth) is what is missing;
        /// <see cref="CurrentValue"/> = <see cref="TargetValue"/> = the player's difficulty.
        /// </summary>
        public bool AtLevel { get; set; }
        /// <summary>Normalised gap to the next waypoint (in path spread units) before the path prior.</summary>
        public double Gap { get; set; }
        /// <summary>Gap weighted by the path's push/polish prior; objectives are ordered by this (D-025).</summary>
        public double Priority { get; set; }
        public double CurrentValue { get; set; }
        public double TargetValue { get; set; }
        /// <summary>Path-axis difficulty the objective's maps are centred on (Explore's "Training level" follows it, D-047).</summary>
        public double MapCentre { get; set; }
        public List<MapRecommendation> Maps { get; set; } = new List<MapRecommendation>();
    }

    public sealed class PathProgress
    {
        /// <summary>True when the player is past the last waypoint and <see cref="Next"/> is an open-ended step (D-027).</summary>
        public bool BeyondLastWaypoint { get; set; }
        public PathState State { get; set; } = new PathState();
        public WaypointModel? Current { get; set; }
        public WaypointModel? Next { get; set; }
        public int WaypointCount { get; set; }
        /// <summary>0..1 position between the current and next waypoint by profile distance (D-009, D-039).</summary>
        public double ProgressToNext { get; set; }
        /// <summary>Per-dimension progress toward the next waypoint, each capped at 1 (keys: pp, difficulty, accuracy).</summary>
        public Dictionary<string, double> DimensionProgress { get; set; } = new Dictionary<string, double>();
        public List<Objective> Objectives { get; set; } = new List<Objective>();
    }

    /// <summary>Locates waypoints, computes push/polish gaps and recommends maps (D-009, D-022, D-025).</summary>
    public static class PathAnalyzer
    {
        public const int MapsPerObjective = 10;
        /// <summary>Diversity rule (D-025): at most this share of a list from one map type.</summary>
        public const double MaxShareOfOneType = 0.6;
        /// <summary>Difficulty tolerance for waypoint gating, in path difficulty-spread units.</summary>
        public const double GateTolerance = 0.25;
        /// <summary>Half-width (axis units) of the polish window around the next waypoint's difficulty (D-038).</summary>
        public const double PolishWindow = 0.75;
        /// <summary>Half-width (axis units) of the new-maps-at-your-level window (D-051).</summary>
        public const double AtLevelWindow = 0.5;
        /// <summary>Highest accuracy a new-map target asks for (same cap as Explore's expected accuracy).</summary>
        public const double MaxTargetAccuracy = 0.9995;

        /// <param name="skipped">Maps the player skipped: replaced by the next best ones, kept only as a last resort.</param>
        public static PathProgress? Analyze(string path, IReadOnlyList<RatedPlay> bestPlays, ModelFile model, MapCatalog catalog,
            ISet<MapKey>? skipped = null)
        {
            var pathModel = model.Paths.FirstOrDefault(p => p.Destination == path);
            var state = PathState.Compute(path, bestPlays, model.Thresholds, model.AccSaber);
            if (pathModel == null || state == null || pathModel.Waypoints.Count == 0) return null;

            var wps = pathModel.Waypoints.OrderBy(w => w.Index).ToList();
            // Typical difficulty per waypoint, made non-decreasing on gated paths (raw values can dip between steps).
            var targetD = new double[wps.Count];
            for (var i = 0; i < wps.Count; i++)
                targetD[i] = pathModel.DifficultyGated && i > 0 ? Math.Max(targetD[i - 1], wps[i].Difficulty) : wps[i].Difficulty;

            var idx = 0;
            for (var i = 0; i < wps.Count; i++) if (state.PathPp >= wps[i].PathPpLow) idx = i;
            if (pathModel.DifficultyGated)
            {
                var tolerance = GateTolerance * Positive(pathModel.DifficultySpread);
                var reached = 0;
                for (var i = 0; i < wps.Count; i++) if (state.Difficulty >= targetD[i] - tolerance) reached = i;
                idx = Math.Min(idx, reached);
            }
            if (pathModel.AccuracyGated)
            {
                // Same rule on accuracy vs predicted, with the waypoints' accuracy made non-decreasing. The tolerance is a
                // share of the step from the previous waypoint, not of the path's spread: the cross-sectional SS sample
                // spans 78% to 99% accuracy, so a spread-based tolerance (~2%) let a -1.2% player pass a +0.1% waypoint.
                var targetA = wps[0].AccuracyResidual;
                var reached = 0;
                for (var i = 1; i < wps.Count; i++)
                {
                    var previous = targetA;
                    targetA = Math.Max(targetA, wps[i].AccuracyResidual);
                    if (state.AccuracyResidual >= targetA - GateTolerance * (targetA - previous)) reached = i;
                    else break;
                }
                idx = Math.Min(idx, reached);
            }
            var current = wps[idx];
            var progress = new PathProgress { State = state, Current = current, WaypointCount = wps.Count };
            WaypointModel next;
            double nextD;
            if (idx + 1 < wps.Count)
            {
                next = wps[idx + 1];
                nextD = targetD[idx + 1];
            }
            else
            {
                // D-027: no "top reached" dead end. Open-ended next step one typical waypoint step beyond the better of
                // the last waypoint and the player's own level, on both difficulty and accuracy.
                next = OpenEndedStep(wps, targetD, state);
                nextD = next.Difficulty;
                progress.BeyondLastWaypoint = true;
            }
            progress.Next = next;

            // AccSaber tracks (D-061, user): clearing harder maps isn't a goal there, accuracy is.
            var track = AccSaberTracks.IsTrack(path);
            ProfileProgress(progress, state, current, targetD[idx], next, nextD, pathModel, countDifficulty: !track);

            var pushGap = Math.Max(0, nextD - state.Difficulty) / Positive(pathModel.DifficultySpread);
            var polishGap = Math.Max(0, next.AccuracyResidual - state.AccuracyResidual) / Positive(pathModel.AccuracySpread);
            var step = Math.Max(nextD - state.Difficulty, 0.25);
            var played = new Dictionary<MapKey, RatedPlay>();
            foreach (var p in bestPlays) played[p.Evidence.Map] = p;
            var popularityKey = $"{path}|{next.Index}";
            // D-055: new maps aim at what the player usually gets on maps like it (Explore's estimate over all their style
            // plays) plus one short-term step of the accuracy gap, not the top plays' residual: the top plays are the most
            // replayed ones, so first attempts on new maps fell ~0.3% short (player feedback 2026-11-14).
            var estimator = AccuracyEstimator.Fit(path, bestPlays, model.Thresholds);
            var accuracyStep = Math.Max(0, next.AccuracyResidual - state.AccuracyResidual) / TrainingPlanner.StepsPerWaypointGap;
            var peers = PeerTargets.For(bestPlays, model);
            // Tracks (D-061, user): new maps by the AP they'd add at their target, popularity only breaks ties.
            var gain = PathRules.TrackGain(path, bestPlays, model);

            List<MapRecommendation> NewMaps(double low, double high, double centre) => Diversify(OrderNew(catalog.Recommendable(path)
                .Where(m => !played.ContainsKey(Key(m)) && m.Ratings.TryGetValue("", out var r) && PathRules.IsRecommendable(catalog, m, r, path, model.Thresholds))
                .Select(m => (map: m, axis: PathRules.Axis(path, m, m.Ratings[""])))
                .Where(x => x.axis >= low && x.axis <= high)
                .Select(x => new MapRecommendation
                {
                    Map = Key(x.map), Name = x.map.Name, Mapper = x.map.Mapper, AxisRating = x.axis, Stars = x.map.Ratings[""].Stars, Njs = x.map.Njs, Nps = x.map.Nps,
                    TargetAccuracy = peers.Target(path, x.map)
                                     ?? Math.Min(MaxTargetAccuracy, estimator.Expected(x.map, x.map.Ratings[""], path) + accuracyStep),
                    PeerPopularity = x.map.PeerPopularity.TryGetValue(popularityKey, out var n) ? n : 0,
                }), centre), catalog, skipped);

            IEnumerable<MapRecommendation> OrderNew(IEnumerable<MapRecommendation> maps, double centre) => gain != null
                ? maps.OrderByDescending(r => gain(r.AxisRating, r.TargetAccuracy)).ThenByDescending(r => r.PeerPopularity)
                : maps.OrderByDescending(r => r.PeerPopularity).ThenBy(r => Math.Abs(r.AxisRating - centre));

            // Tracks: new maps only around the player's own complexity, for the AP they add (no "clear harder maps").
            var push = track ? AtLevel(state, current, next, pathModel, NewMaps)
                : pushGap > 0
                ? new Objective
                {
                    Kind = ObjectiveKind.Push, Gap = pushGap, Priority = pushGap * 2 * pathModel.PushPrior,
                    CurrentValue = state.Difficulty, TargetValue = nextD, MapCentre = nextD,
                    Maps = NewMaps(state.Difficulty - step, nextD + step, nextD),
                }
                : AtLevel(state, current, next, pathModel, NewMaps);

            // D-038: the next waypoint's accuracy is what its players reach on maps of its typical difficulty. A player
            // already above that difficulty polishes around it (easier maps), not at their own harder level, where
            // beating the predicted accuracy is harder. Falls back to their own level when nothing was played there.
            var polishCandidates = played.Values
                .Where(p => PathRules.IsRecommendable(catalog, p.Map, p.Ratings, path, model.Thresholds))
                // D-058: with a peer level on the style, only maps with a peer target (no mixing with the residual rule).
                .Where(p => !peers.Covers(path) || PeerTarget(peers, path, p) != null)
                .Select(p => (play: p, axis: PathRules.Axis(path, p)))
                .ToList();
            var polishCentre = state.Difficulty;
            if (state.Difficulty > nextD && polishCandidates.Any(x => Math.Abs(x.axis - nextD) <= PolishWindow
                                                                     && x.play.Accuracy < x.play.Ratings.PredictedAcc + next.AccuracyResidual))
                polishCentre = nextD;
            var polishWindow = polishCentre == state.Difficulty ? step : PolishWindow;
            var polish = new Objective
            {
                Kind = ObjectiveKind.Polish, Gap = polishGap, Priority = polishGap * 2 * (1 - pathModel.PushPrior),
                CurrentValue = state.AccuracyResidual, TargetValue = next.AccuracyResidual, MapCentre = polishCentre,
                Maps = Diversify(polishCandidates
                    .Where(x => Math.Abs(x.axis - polishCentre) <= polishWindow)
                    .Select(x => new MapRecommendation
                    {
                        Map = x.play.Evidence.Map, Name = x.play.Map.Name, Mapper = x.play.Map.Mapper, AxisRating = x.axis, Stars = x.play.Ratings.Stars, Njs = x.play.Map.Njs, Nps = x.play.Map.Nps,
                        TargetAccuracy = PeerTarget(peers, path, x.play) ?? x.play.Ratings.PredictedAcc + next.AccuracyResidual,
                        CurrentAccuracy = x.play.Accuracy,
                        PeerPopularity = x.play.Map.PeerPopularity.TryGetValue(popularityKey, out var n) ? n : 0,
                    })
                    // D-058: furthest below players at their level first.
                    .Where(r => r.CurrentAccuracy < r.TargetAccuracy)
                    .OrderBy(r => r.CurrentAccuracy - r.TargetAccuracy), catalog, skipped),
            };

            // A dimension where the player is already at/above the next waypoint is not an objective.
            progress.Objectives = new[] { push, polish }.Where(o => o != null && o.Gap > 0).Select(o => o!)
                .OrderByDescending(o => o.Priority).ToList();
            return progress;
        }

        /// <summary>
        /// D-051: difficulty already at the next waypoint but path pp behind it (typically after a fresh start, or on an
        /// ungated path): the gap is breadth, so the push objective becomes new maps around the player's own level.
        /// Gap = share of the pp step still missing (capped at 1). Null when pp is not behind either.
        /// </summary>
        private static Objective? AtLevel(PathState state, WaypointModel current, WaypointModel next, PathModel pathModel,
            Func<double, double, double, List<MapRecommendation>> newMaps)
        {
            if (state.PathPp >= next.PathPpLow) return null;
            var gap = Math.Min(1, (next.PathPpLow - state.PathPp) / Positive(next.PathPpLow - current.PathPpLow));
            return new Objective
            {
                Kind = ObjectiveKind.Push, AtLevel = true, Gap = gap, Priority = gap * 2 * pathModel.PushPrior,
                CurrentValue = state.Difficulty, TargetValue = state.Difficulty, MapCentre = state.Difficulty,
                Maps = newMaps(state.Difficulty - AtLevelWindow, state.Difficulty + AtLevelWindow, state.Difficulty),
            };
        }

        /// <summary>
        /// Starter maps for a path the player has (almost) no evidence on — fresh start or an unplayed style: maps on
        /// the easier side of the first waypoint, ranked by what players at that stage played (user request: fresh
        /// start must still recommend maps, D-027 "always a next recommendation").
        /// </summary>
        /// <param name="startLevel">Fresh start (D-040): calibrated or cautious level from the old profile, instead of the
        /// first waypoint's difficulty.</param>
        public static Objective? Starter(string path, ModelFile model, MapCatalog catalog, IReadOnlyCollection<MapKey> played,
            double? startLevel = null, ISet<MapKey>? skipped = null)
        {
            var pathModel = model.Paths.FirstOrDefault(p => p.Destination == path);
            if (pathModel == null || pathModel.Waypoints.Count == 0) return null;
            var first = startLevel is double level
                ? pathModel.Waypoints.OrderBy(w => Math.Abs(w.Difficulty - level)).First()
                : pathModel.Waypoints.OrderBy(w => w.Index).First();
            var easier = startLevel ?? first.Difficulty;
            var floor = easier - 2 * Positive(pathModel.DifficultySpread);
            var popularity = new[] { $"{path}|{first.Index}", $"{path}|{first.Index + 1}" };
            var playedSet = new HashSet<MapKey>(played);
            var maps = catalog.Recommendable(path)
                .Where(m => m.Ratings.TryGetValue("", out var r) && PathRules.IsRecommendable(catalog, m, r, path, model.Thresholds))
                .Select(m => (map: m, key: Key(m), axis: PathRules.Axis(path, m, m.Ratings[""])))
                .Where(x => !playedSet.Contains(x.key) && x.axis <= easier && x.axis >= floor)
                .Select(x => new MapRecommendation
                {
                    Map = x.key, Name = x.map.Name, Mapper = x.map.Mapper, AxisRating = x.axis, Stars = x.map.Ratings[""].Stars, Njs = x.map.Njs, Nps = x.map.Nps,
                    TargetAccuracy = x.map.Ratings[""].PredictedAcc + first.AccuracyResidual,
                    PeerPopularity = popularity.Sum(k => x.map.PeerPopularity.TryGetValue(k, out var n) ? n : 0),
                })
                .OrderByDescending(r => r.PeerPopularity).ThenBy(r => r.AxisRating);
            return new Objective
            {
                Kind = ObjectiveKind.Push, Gap = 0, Priority = 0, CurrentValue = floor, TargetValue = easier, MapCentre = easier,
                Maps = Diversify(maps, catalog, skipped),
            };
        }

        /// <summary>
        /// D-039: where the profile sits between the current and next waypoint's typical players. Each dimension (path pp,
        /// difficulty, accuracy vs predicted) is a 0..1 share of its step, capped so a surplus can't hide a gap, and
        /// weighted by its step size in units of how much players vary on it (squared, as a projection in profile space;
        /// pp's own step is the unit). On gated paths the bar can't run ahead of difficulty: pp from easier maps alone
        /// doesn't count as progress there.
        /// </summary>
        /// <param name="countDifficulty">False on AccSaber tracks (D-061): only AP and accuracy make progress.</param>
        private static void ProfileProgress(PathProgress progress, PathState state, WaypointModel current, double currentD,
            WaypointModel next, double nextD, PathModel pathModel, bool countDifficulty = true)
        {
            double Share(double value, double from, double to) =>
                to > from ? Clamp01((value - from) / (to - from)) : value >= to ? 1 : 0;

            var dims = new[]
            {
                (key: "pp", share: Share(state.PathPp, current.PathPpLow, next.PathPpLow), step: 1.0),
                (key: "difficulty", share: Share(state.Difficulty, currentD, nextD),
                 step: countDifficulty ? (nextD - currentD) / Positive(pathModel.DifficultySpread) : 0),
                (key: "accuracy", share: Share(state.AccuracyResidual, current.AccuracyResidual, next.AccuracyResidual),
                 step: (next.AccuracyResidual - current.AccuracyResidual) / Positive(pathModel.AccuracySpread)),
            };
            foreach (var d in dims) progress.DimensionProgress[d.key] = d.share;
            var weight = dims.Sum(d => d.step * d.step);
            var bar = weight <= 0 ? 0 : dims.Sum(d => d.step * d.step * d.share) / weight;
            if (pathModel.DifficultyGated) bar = Math.Min(bar, progress.DimensionProgress["difficulty"]);
            if (pathModel.AccuracyGated) bar = Math.Min(bar, progress.DimensionProgress["accuracy"]);
            progress.ProgressToNext = Clamp01(bar);
        }

        private static WaypointModel OpenEndedStep(List<WaypointModel> wps, double[] targetD, PathState state)
        {
            var n = wps.Count;
            var last = wps[n - 1];
            double stepD = 0.25, stepA = 0.002, stepPp = Math.Max(last.PathPpHigh - last.PathPpLow, 1);
            if (n >= 2)
            {
                stepD = Math.Max(stepD, (targetD[n - 1] - targetD[0]) / (n - 1));
                stepA = Math.Max(stepA, (last.AccuracyResidual - wps[0].AccuracyResidual) / (n - 1));
                stepPp = Math.Max(1, (last.PathPpLow - wps[0].PathPpLow) / (n - 1));
            }
            return new WaypointModel
            {
                Index = n,
                PathPpLow = Math.Max(last.PathPpLow, state.PathPp) + stepPp,
                Difficulty = Math.Max(targetD[n - 1], state.Difficulty) + stepD,
                AccuracyResidual = Math.Max(last.AccuracyResidual, state.AccuracyResidual) + stepA,
                Accuracy = Math.Min(1, Math.Max(last.Accuracy, state.Accuracy) + stepA),
                EquivalentRank = 1,
            };
        }

        /// <summary>
        /// Skipped maps (D-029, user 2026-11-22: skipping every map emptied Next up) give their place to the next best
        /// ones; they only fill the list when there aren't enough others, and Next up shows them last.
        /// </summary>
        private static List<MapRecommendation> Diversify(IEnumerable<MapRecommendation> ordered, MapCatalog catalog,
            ISet<MapKey>? skipped)
        {
            if (skipped == null || skipped.Count == 0) return Diversify(ordered, catalog);
            var list = ordered.ToList();
            var result = Diversify(list.Where(r => !skipped.Contains(r.Map)), catalog);
            if (result.Count < MapsPerObjective)
                result.AddRange(Diversify(list.Where(r => skipped.Contains(r.Map)), catalog).Take(MapsPerObjective - result.Count));
            return result;
        }

        private static List<MapRecommendation> Diversify(IEnumerable<MapRecommendation> ordered, MapCatalog catalog)
        {
            var cap = (int)Math.Ceiling(MapsPerObjective * MaxShareOfOneType);
            var perType = new Dictionary<string, int>();
            var result = new List<MapRecommendation>();
            var skipped = new List<MapRecommendation>();
            foreach (var r in ordered)
            {
                var type = catalog.TryGet(r.Map, out var m) ? m.MapType : "";
                perType.TryGetValue(type, out var n);
                if (n >= cap) { skipped.Add(r); continue; }
                perType[type] = n + 1;
                result.Add(r);
                if (result.Count == MapsPerObjective) return result;
            }
            // Not enough variety available: fill with the best skipped ones rather than return a short list.
            result.AddRange(skipped.Take(MapsPerObjective - result.Count));
            return result;
        }

        /// <summary>Peer target on a played map; null when its PB isn't comparable (speed / easier modifiers) or no data.</summary>
        internal static double? PeerTarget(PeerTargets peers, string path, RatedPlay play) =>
            PeerTargets.Comparable(play.Evidence) ? peers.Target(path, play.Map) : null;

        private static MapKey Key(MapEntry m) => new MapKey(m.Hash, m.Characteristic, m.Difficulty);
        private static double Positive(double x) => x > 0 ? x : 1;
        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }
}
