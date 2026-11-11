using SkillProgression.Core.Profiles;

namespace SkillProgression.Core.Model
{
    /// <summary>Which ranked maps the plugin recommends (D-041). Progress is measured the same way whatever the pool.</summary>
    public enum MapPool
    {
        BeatLeader,
        ScoreSaber,
        AccSaber,
        /// <summary>Ranked on any of the three.</summary>
        AnyRanked,
    }

    public static class MapPools
    {
        /// <summary>AccSaber ranks accuracy maps only (D-023): on other styles its pool falls back to BeatLeader's.</summary>
        public static MapPool Effective(MapPool pool, string path) =>
            pool == MapPool.AccSaber && path != Destinations.Accuracy ? MapPool.BeatLeader : pool;

        public static bool Contains(MapPool pool, MapEntry map) => pool switch
        {
            MapPool.ScoreSaber => map.RankedScoreSaber,
            MapPool.AccSaber => map.RankedAccSaber,
            MapPool.AnyRanked => map.RankedBeatLeader || map.RankedScoreSaber || map.RankedAccSaber,
            _ => map.RankedBeatLeader,
        };
    }
}
