using SkillProgression.Core.Model;

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
