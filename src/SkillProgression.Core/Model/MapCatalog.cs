using System.Collections.Generic;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Model
{
    /// <summary>A play rated with the effective ratings of the speed modifier used, and its pp from the lookup curve.</summary>
    public sealed class RatedPlay
    {
        public PlayEvidence Evidence { get; }
        public MapEntry Map { get; }
        public MapRatings Ratings { get; }
        public double Pp { get; }

        /// <summary>
        /// AccSaber AP at the play's raw accuracy (D-061 tracks); 0 when the map isn't AccSaber-ranked, the model has no
        /// AccSaber curve, or the play uses a modifier AccSaber bans (<see cref="AccSaberTracks.Counts"/>).
        /// </summary>
        public double Ap { get; }

        /// <summary>ScoreSaber pp (D-062 styles); 0 when the map isn't ScoreSaber-ranked or the model has no SS formula.</summary>
        public double SsPp { get; }

        public RatedPlay(PlayEvidence evidence, MapEntry map, MapRatings ratings, double pp, double ap = 0, double ssPp = 0)
        {
            Evidence = evidence;
            Map = map;
            Ratings = ratings;
            Pp = pp;
            Ap = ap;
            SsPp = ssPp;
        }

        public double Accuracy => Evidence.Accuracy;
        public bool UsesSpeedModifier => Evidence.SpeedModifier.Length > 0;
    }

    /// <summary>Lookup of model maps by canonical key; rates plays (effective ratings + pp).</summary>
    public sealed class MapCatalog
    {
        private readonly Dictionary<MapKey, MapEntry> _maps = new Dictionary<MapKey, MapEntry>();
        private readonly AccSaberModel? _accSaber;
        private readonly ScoreSaberModel? _scoreSaber;

        public MapCatalog(IEnumerable<MapEntry> maps, AccSaberModel? accSaber = null, ScoreSaberModel? scoreSaber = null)
        {
            _accSaber = accSaber;
            _scoreSaber = scoreSaber;
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
            var ap = _accSaber != null && map.RankedAccSaber && map.AccSaberComplexity > 0 && AccSaberTracks.Counts(play)
                ? _accSaber.Ap(map.AccSaberComplexity, play.Accuracy) : 0;
            var ssPp = _scoreSaber != null && map.RankedScoreSaber && map.ScoreSaberStars > 0
                ? _scoreSaber.Pp(map.ScoreSaberStars, play.Accuracy, play.Modifiers) : 0;
            return new RatedPlay(play, map, ratings, pp, ap, ssPp);
        }
    }
}
