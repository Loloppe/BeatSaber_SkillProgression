using System;
using System.Linq;

namespace SkillProgression.Core.Model
{
    /// <summary>
    /// Explore requirement filter (user request 2026-11-11), "only that mod": None lists vanilla maps only, a mod lists
    /// only maps using it, All lists everything. Chroma is not tracked: Chroma maps count as vanilla (user decision).
    /// </summary>
    public enum RequirementFilter
    {
        None,
        NoodleExtensions,
        MappingExtensions,
        Vivify,
        All,
    }

    public static class MapRequirements
    {
        // Spelled as in map Info.dat requirements / SongCore capabilities.
        public const string NoodleExtensions = "Noodle Extensions";
        public const string MappingExtensions = "Mapping Extensions";
        public const string Vivify = "Vivify";

        public static bool Matches(RequirementFilter filter, MapEntry map) => filter switch
        {
            RequirementFilter.All => true,
            RequirementFilter.NoodleExtensions => Uses(map, NoodleExtensions),
            RequirementFilter.MappingExtensions => Uses(map, MappingExtensions),
            RequirementFilter.Vivify => Uses(map, Vivify),
            _ => map.Requirements == null || map.Requirements.Count == 0,
        };

        public static bool Uses(MapEntry map, string requirement) =>
            map.Requirements != null && map.Requirements.Any(r => string.Equals(r, requirement, StringComparison.OrdinalIgnoreCase));
    }
}
