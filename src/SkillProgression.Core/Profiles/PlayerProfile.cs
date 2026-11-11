using System.Collections.Generic;

namespace SkillProgression.Core.Profiles
{
    /// <summary>
    /// A player's demonstrated profile from their top plays (C# port of training/explore/profiles.py `profile`).
    /// "Typical" values are pp-weighted over the top plays; ratings are modifier-adjusted.
    /// </summary>
    public sealed class PlayerProfile
    {
        public int PlayCount { get; set; }
        public int TopPlayCount { get; set; }

        // Level descriptors
        public double Stars { get; set; }
        public double Pass { get; set; }
        public double AccRating { get; set; }
        public double Tech { get; set; }
        public double Accuracy { get; set; }
        /// <summary>95th percentile pass rating over all the player's best plays: speed demonstrated at all.</summary>
        public double PassCeiling { get; set; }
        public double Duration { get; set; }
        public double Ebpm { get; set; }
        public double TopPpSum { get; set; }
        public double PassMedian { get; set; }
        public double TechMedian { get; set; }
        public double StarsMedian { get; set; }

        // Shares of top-play pp by effective star band
        public double ShareUnder5 { get; set; }
        public double Share5To9 { get; set; }
        public double Share9To13 { get; set; }
        public double Share13Plus { get; set; }
        public double TechShare5To9 { get; set; }
        public double TechShare9Plus { get; set; }
        public double TechShare5Plus { get; set; }

        /// <summary>Level-independent style features (names match the prototype's s_* columns).</summary>
        public Dictionary<string, double> Style { get; set; } = new Dictionary<string, double>();
    }

    public static class StyleFeatures
    {
        public const string BelowCeiling = "s_below_ceiling";
        public const string TechVsPass = "s_tech_vs_pass";
        public const string AccResidual = "s_acc_resid";
        public const string TypeAcc = "s_type_acc";
        public const string TypeTech = "s_type_tech";
        public const string TypeSpeed = "s_type_speed";
        public const string StarSpread = "s_star_spread";
        public const string SpeedMods = "s_speed_mods";
        public const string LongMaps = "s_long_maps";
        public const string Sub95 = "s_sub95";
    }
}
