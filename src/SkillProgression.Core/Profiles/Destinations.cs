using System.Linq;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;

namespace SkillProgression.Core.Profiles
{
    /// <summary>Destination names (D-021).</summary>
    public static class Destinations
    {
        public const string Accuracy = "Accuracy";
        public const string Midspeed = "Midspeed";
        public const string HighTech = "High Tech";
        public const string Speed = "Speed";
        public const string Extreme = "Extreme";
        public const string Unspecialised = "Unspecialised";

        public static readonly string[] All = { Accuracy, Midspeed, HighTech, Speed, Extreme };
    }

    /// <summary>
    /// The five styles measured on ScoreSaber (D-062): SS pp from the player's ScoreSaber scores, milestones at SS global
    /// ranks. Own path ids ("SS Speed") so their progress and achievements never mix with BeatLeader's.
    /// </summary>
    public static class ScoreSaberStyles
    {
        public const string Prefix = "SS ";

        public static readonly string[] All = Destinations.All.Select(Id).ToArray();

        public static string Id(string style) => Prefix + style;

        public static bool IsSsPath(string path) => path.StartsWith(Prefix, System.StringComparison.Ordinal);

        /// <summary>The BeatLeader style an SS path measures (membership, axis, peer targets); the path itself otherwise.</summary>
        public static string Style(string path) => IsSsPath(path) ? path.Substring(Prefix.Length) : path;
    }

    /// <summary>How paths are named to the player.</summary>
    public static class PathNames
    {
        public static string Display(string path) =>
            ScoreSaberStyles.IsSsPath(path) ? $"{ScoreSaberStyles.Style(path)} (ScoreSaber)" : path;

        /// <summary>Styles, AccSaber tracks or ScoreSaber styles: achievements new to a family are recorded quietly.</summary>
        public static string Family(string path) =>
            AccSaberTracks.IsTrack(path) ? "AccSaber" : ScoreSaberStyles.IsSsPath(path) ? "ScoreSaber" : "BeatLeader";
    }

    /// <summary>
    /// AccSaber category tracks (D-061): with the AccSaber pool they replace the five styles. Each is a path whose maps are
    /// the category's AccSaber maps, measured on AccSaber complexity and weighted by AccSaber AP.
    /// </summary>
    public static class AccSaberTracks
    {
        public const string TrueAcc = "True Acc";
        public const string StandardAcc = "Standard Acc";
        public const string TechAcc = "Tech Acc";

        public static readonly string[] All = { TrueAcc, StandardAcc, TechAcc };

        public static bool IsTrack(string path) => path == TrueAcc || path == StandardAcc || path == TechAcc;

        /// <summary>
        /// Modifiers AccSaber bans (user, 2026-11-22): No Obstacles, No Bombs, Slower Song, Small Notes (BeatLeader "SC",
        /// AccSaber "SN"). No Fail too (Claude assumption: AccSaber lists it at x0.5, how it applies is not verified).
        /// Everything else counts at its raw accuracy, speed modifiers included (live scores, D-061).
        /// Mirrors training/accsaber_tracks.py BANNED_MODIFIERS.
        /// </summary>
        public static readonly string[] BannedModifiers = { "NO", "NB", "SS", "SC", "SN", "NF" };

        /// <summary>Whether a play earns AccSaber AP: none of <see cref="BannedModifiers"/>.</summary>
        public static bool Counts(PlayEvidence play) =>
            !play.Modifiers.Any(m => BannedModifiers.Contains(m.Trim().ToUpperInvariant()));

        /// <summary>AccSaber's category code (`/v1/categories`), as stored on <see cref="MapEntry.AccSaberCategory"/>.</summary>
        public static string Code(string track) => track switch
        {
            TrueAcc => "true_acc",
            StandardAcc => "standard_acc",
            TechAcc => "tech_acc",
            _ => "",
        };
    }

    /// <summary>
    /// Rule-based arrival at a destination (rules v6; D-018, D-020, D-021). Port of profiles.py `destination`.
    /// Uses only pass rating, tech rating and accuracy vs predicted acc, plus effective stars for Extreme.
    /// </summary>
    public static class DestinationRules
    {
        public static string Classify(PlayerProfile p, DestinationThresholds t)
        {
            var pass = p.PassMedian;
            var tech = p.TechMedian;
            var acc = p.Style.TryGetValue(StyleFeatures.AccResidual, out var r) ? r : double.NegativeInfinity;
            var techLed = tech >= t.TechRatio * pass && tech >= t.TechMin;

            if (p.StarsMedian >= t.ExtremeStars) return Destinations.Extreme;
            if (acc >= t.AccResid)
            {
                if (pass <= t.LowPass) return Destinations.Accuracy;
                if (pass <= t.MidPass)
                {
                    if (techLed) return Destinations.HighTech; // slow tech acc = early High Tech phase (D-020)
                    if (p.PassCeiling >= pass + t.MidGap) return Destinations.Midspeed;
                }
            }

            if (techLed && tech >= t.TechArrive) return Destinations.HighTech;
            if (!techLed && pass >= t.SpeedArrive) return Destinations.Speed;
            return Destinations.Unspecialised;
        }
    }
}
