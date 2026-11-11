using System.Collections.Generic;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;

namespace SkillProgression.Core.Model
{
    /// <summary>A play rated with the effective ratings of the speed modifier used, and its pp from the lookup curve.</summary>
    public sealed class RatedPlay
    {
        public PlayEvidence Evidence { get; }
        public MapEntry Map { get; }
        public MapRatings Ratings { get; }
        public double Pp { get; }

        public RatedPlay(PlayEvidence evidence, MapEntry map, MapRatings ratings, double pp)
        {
            Evidence = evidence;
            Map = map;
            Ratings = ratings;
            Pp = pp;
        }

        public double Accuracy => Evidence.Accuracy;
        public bool UsesSpeedModifier => Evidence.SpeedModifier.Length > 0;
    }

    /// <summary>Lookup of model maps by canonical key; rates plays (effective ratings + pp).</summary>
    public sealed class MapCatalog
    {
        private readonly Dictionary<MapKey, MapEntry> _maps = new Dictionary<MapKey, MapEntry>();

        public MapCatalog(IEnumerable<MapEntry> maps)
        {
            foreach (var m in maps)
                _maps[new MapKey(m.Hash, m.Characteristic, m.Difficulty)] = m;
        }

        public int Count => _maps.Count;

        public bool TryGet(MapKey key, out MapEntry map) => _maps.TryGetValue(key, out map!);

        public IEnumerable<MapEntry> All => _maps.Values;

        /// <summary>Pool recommendations come from (D-041). Rating plays always uses every map.</summary>
        public MapPool Pool { get; set; } = MapPool.BeatLeader;

        /// <summary>Maps that may be recommended on a path: those of the selected pool (AccSaber: Accuracy only).</summary>
        public IEnumerable<MapEntry> Recommendable(string path)
        {
            var pool = MapPools.Effective(Pool, path);
            foreach (var m in _maps.Values)
                if (MapPools.Contains(pool, m)) yield return m;
        }

        public bool IsRecommendable(MapEntry map, string path) => MapPools.Contains(MapPools.Effective(Pool, path), map);

        /// <summary>
        /// Rates a play: effective ratings of its speed modifier (falling back to nomod when the modifier variant is
        /// missing, like the prototype) and pp from the matching curve. Null when the map is not in the model.
        /// </summary>
        public RatedPlay? Rate(PlayEvidence play)
        {
            if (!_maps.TryGetValue(play.Map, out var map)) return null;
            var mod = play.SpeedModifier;
            if (!map.Ratings.TryGetValue(mod, out var ratings))
            {
                if (!map.Ratings.TryGetValue("", out ratings)) return null;
                mod = "";
            }

            var pp = map.PpCurves.TryGetValue(mod, out var curve) ? curve.Evaluate(play.Accuracy) : 0;
            return new RatedPlay(play, map, ratings, pp);
        }
    }
}
