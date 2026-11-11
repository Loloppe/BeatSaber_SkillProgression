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
        /// <summary>BeatSaver curated maps BeatLeader rates, ranked or not (D-045). Explore only, never Training.</summary>
        Curated,
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
            MapPool.AnyRanked => IsRanked(map),
            MapPool.Curated => map.Curated,
            _ => map.RankedBeatLeader,
        };

        /// <summary>Ranked on BeatLeader, ScoreSaber or AccSaber: the only maps progression is measured on (D-045).</summary>
        public static bool IsRanked(MapEntry map) => map.RankedBeatLeader || map.RankedScoreSaber || map.RankedAccSaber;
    }
}
