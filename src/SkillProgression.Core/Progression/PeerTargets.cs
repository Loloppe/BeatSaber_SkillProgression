using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Progression
{
    /// <summary>
    /// D-058: "players at your level get this here". The player's accuracy level per cell (style, style difficulty band)
    /// = median of (no-modifier PB - the map's ease) over their maps in the cell; the target on a map = the map's peer
    /// curve at that level. A band where the player has fewer than <see cref="PeerTargetsModel.MinPlayerMaps"/> maps uses
    /// the style-wide cell; null (callers keep their previous rule) when the style has too few maps too, the map has no
    /// curve, or the model predates D-058. On a played map the level leaves that map out, so its own score doesn't pull
    /// its target. Only <see cref="Comparable"/> scores count, on both sides (the population fit uses the same rule).
    /// </summary>
    public sealed class PeerTargets
    {
        private readonly int _minMaps;
        /// <summary>cell id -> (map, accuracy - ease) of the player's no-modifier PBs.</summary>
        private readonly Dictionary<string, List<(MapKey map, double residual)>> _residuals =
            new Dictionary<string, List<(MapKey, double)>>();

        public static readonly PeerTargets None = new PeerTargets(int.MaxValue);

        /// <summary>
        /// Modifiers that neither change the song speed nor make the map easier (user, D-058): Old Dots (BeatLeader's tag
        /// for Old Dots mode / game versions before 1.20), Pro Mode, Disappearing Arrows, Ghost Notes, Battery Energy.
        /// Mirrors training/peer_targets.py NEUTRAL_MODIFIERS.
        /// </summary>
        public static readonly string[] NeutralModifiers = { "OD", "PM", "DA", "GN", "BE" };

        /// <summary>A score peers can be compared on: no modifier, or only <see cref="NeutralModifiers"/>.</summary>
        public static bool Comparable(PlayEvidence play) =>
            play.Modifiers.All(m => NeutralModifiers.Contains(m.Trim().ToUpperInvariant()));

        private PeerTargets(int minMaps) => _minMaps = minMaps;

        public static PeerTargets For(IEnumerable<RatedPlay> bestPlays, ModelFile model)
        {
            if (model.PeerTargets == null) return None;
            var targets = new PeerTargets(Math.Max(1, model.PeerTargets.MinPlayerMaps));
            foreach (var p in bestPlays)
            {
                if (!Comparable(p.Evidence)) continue;
                foreach (var cell in p.Map.PeerTargets)
                {
                    if (!targets._residuals.TryGetValue(cell.Key, out var list))
                        targets._residuals[cell.Key] = list = new List<(MapKey, double)>();
                    list.Add((p.Evidence.Map, p.Accuracy - cell.Value.Ease));
                }
            }
            return targets;
        }

        /// <summary>Peer target on <paramref name="map"/> for <paramref name="path"/>, or null (see class remarks).</summary>
        public double? Target(string path, MapEntry map)
        {
            path = ScoreSaberStyles.Style(path); // D-062: ScoreSaber styles use their BeatLeader style's peer cells
            var key = new MapKey(map.Hash, map.Characteristic, map.Difficulty);
            var band = map.PeerTargets.Keys.FirstOrDefault(k => k.StartsWith(path + "|", StringComparison.Ordinal));
            if (band != null && Level(band, key) is double bandLevel) return At(map.PeerTargets[band], bandLevel);
            if (map.PeerTargets.TryGetValue(path, out var style) && Level(path, key) is double styleLevel) return At(style, styleLevel);
            return null;
        }

        /// <summary>
        /// Whether the player has a peer level on the style: then lists of played maps only show maps with a peer target,
        /// so fallback targets (another rule, often much harder) never mix into the same ranking (D-058).
        /// </summary>
        public bool Covers(string path) => Level(ScoreSaberStyles.Style(path)) != null;

        /// <summary>The player's level in a cell over all their maps there; null under the minimum map count.</summary>
        public double? Level(string cell) => Level(cell, default);

        /// <summary>The player's level in a cell without <paramref name="exclude"/>; null under the minimum map count.</summary>
        public double? Level(string cell, MapKey exclude)
        {
            if (!_residuals.TryGetValue(cell, out var list)) return null;
            var values = list.Where(x => !x.map.Equals(exclude)).Select(x => x.residual).ToList();
            return values.Count >= _minMaps ? Median(values) : (double?)null;
        }

        /// <summary>
        /// The curve at a level: linear between stored levels; below the first one the value moves one for one with the
        /// level (as the adjusted bands do); above the last one it stays at the curve's top (user, 2026-11-22: extending
        /// it upward gave top players targets nobody reaches); ease + level when the map has no stored values.
        /// </summary>
        public static double At(PeerCurve curve, double level)
        {
            var n = Math.Min(curve.Levels.Count, curve.Values.Count);
            double v;
            if (n == 0) v = curve.Ease + level;
            else if (level <= curve.Levels[0]) v = curve.Values[0] + (level - curve.Levels[0]);
            else if (level >= curve.Levels[n - 1]) v = curve.Values[n - 1];
            else
            {
                var i = 1;
                while (curve.Levels[i] < level) i++;
                var x0 = curve.Levels[i - 1];
                var x1 = curve.Levels[i];
                v = x1 > x0 ? curve.Values[i - 1] + (curve.Values[i] - curve.Values[i - 1]) * (level - x0) / (x1 - x0) : curve.Values[i];
            }
            return Math.Max(0, Math.Min(PathAnalyzer.MaxTargetAccuracy, v));
        }

        private static double Median(List<double> values)
        {
            values.Sort();
            var n = values.Count;
            return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
        }
    }
}
