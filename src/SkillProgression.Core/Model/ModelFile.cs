using System;
using System.Collections.Generic;
using System.Linq;

namespace SkillProgression.Core.Model
{
    /// <summary>
    /// The published progression model (model.json, docs/ARCHITECTURE.md). Aggregates only (D-007).
    /// Plain settable DTOs so the Data layer can deserialize them; Core never does I/O.
    /// </summary>
    public sealed class ModelFile
    {
        public int Version { get; set; } = 1;
        public DateTime GeneratedAt { get; set; }
        public string SourceDb { get; set; } = "";
        public DestinationThresholds Thresholds { get; set; } = new DestinationThresholds();
        public LeaningParameters? Leaning { get; set; }
        public List<PathModel> Paths { get; set; } = new List<PathModel>();
        /// <summary>Peer-based accuracy targets (D-058); null in models built before it.</summary>
        public PeerTargetsModel? PeerTargets { get; set; }
        /// <summary>AccSaber score curve and weights (D-061 tracks); null in models built before it.</summary>
        public AccSaberModel? AccSaber { get; set; }
        /// <summary>ScoreSaber pp curve and modifier values (D-062 styles); null in models built before it.</summary>
        public ScoreSaberModel? ScoreSaber { get; set; }
        public List<MapEntry> Maps { get; set; } = new List<MapEntry>();
    }

    /// <summary>
    /// ScoreSaber's pp formula at model build time (verified on live scores, D-060 / D-062): pp = stars × Factor ×
    /// curve(unmodified accuracy) × (1 + Σ negative modifier values); positive modifiers add nothing (the ranked realm's
    /// `positiveModifiers` is false). A player's total = Σ pp_i × Decay^i.
    /// </summary>
    public sealed class ScoreSaberModel
    {
        /// <summary>pp curve points: Acc = accuracy, Pp = multiplier (1.0 at 95%).</summary>
        public PpCurve Curve { get; set; } = new PpCurve();
        public double Factor { get; set; } = 42.117;
        public double Decay { get; set; } = 0.965;
        /// <summary>Ranked realm modifier values ("NF" → -0.5 …); only negative ones apply.</summary>
        public Dictionary<string, double> ModifierValues { get; set; } = new Dictionary<string, double>();

        public double Pp(double stars, double accuracy, IEnumerable<string> modifiers)
        {
            var multiplier = 1.0;
            foreach (var m in modifiers)
                if (ModifierValues.TryGetValue(m.Trim().ToUpperInvariant(), out var v) && v < 0) multiplier += v;
            return stars * Factor * Curve.Evaluate(accuracy) * Math.Max(0, multiplier);
        }
    }

    /// <summary>
    /// AccSaber's AP formulas at model build time (`/v1/curves`, `/v1/categories`; verified on live scores, D-060 / D-061):
    /// AP = curve(acc) × (complexity − shift) × scale; a category's AP = Σ AP_i × w(i) over scores by AP descending.
    /// </summary>
    public sealed class AccSaberModel
    {
        /// <summary>AccSaber Score Curve points: x = accuracy, y = multiplier.</summary>
        public PpCurve Curve { get; set; } = new PpCurve();
        public double Scale { get; set; } = 61;
        public double Shift { get; set; } = -18;
        /// <summary>Weight curve LOGISTIC_SIGMOID parameters: w(0) = 1, w(X1) = Y1, steepness K.</summary>
        public double WeightK { get; set; } = 0.4;
        public double WeightY1 { get; set; } = 0.1;
        public double WeightX1 { get; set; } = 15;

        public double Ap(double complexity, double accuracy) => Curve.Evaluate(accuracy) * (complexity - Shift) * Scale;

        /// <summary>Weight of the score at <paramref name="index"/> (0 = best): S(i) / S(0), S(i) = 1 / (1 + a·e^(K(i − X1))).</summary>
        public double Weight(int index)
        {
            var a = (1 - WeightY1) / (WeightY1 - Math.Exp(-WeightK * WeightX1));
            double S(double i) => 1 / (1 + a * Math.Exp(WeightK * (i - WeightX1)));
            return S(index) / S(0);
        }

        /// <summary>Category AP of scores' AP values (any order): Σ AP_i × w(i), best first.</summary>
        public double Total(IEnumerable<double> aps)
        {
            double total = 0;
            var i = 0;
            foreach (var ap in aps.OrderByDescending(x => x)) total += ap * Weight(i++);
            return total;
        }

        /// <summary>
        /// D-061: what one new score of <paramref name="ap"/> adds to the category AP of <paramref name="sortedDesc"/> (the
        /// player's AP values, best first): its own weighted AP plus the weight every lower score loses by moving down.
        /// </summary>
        public double Gain(IReadOnlyList<double> sortedDesc, double ap)
        {
            var at = 0;
            while (at < sortedDesc.Count && sortedDesc[at] >= ap) at++;
            var gain = ap * Weight(at);
            for (var i = at; i < sortedDesc.Count; i++) gain += sortedDesc[i] * (Weight(i + 1) - Weight(i));
            return gain;
        }
    }

    /// <summary>
    /// D-058: per style ("Accuracy") and per style difficulty band ("Accuracy|0".."Accuracy|2"), a population fit
    /// acc ≈ ease(map) + level(player) over no-modifier PBs. Maps carry the curves (<see cref="MapEntry.PeerTargets"/>).
    /// </summary>
    public sealed class PeerTargetsModel
    {
        /// <summary>The player's no-modifier maps needed in a band before its own band level is used (else the style's).</summary>
        public int MinPlayerMaps { get; set; } = 10;
        public List<PeerCellModel> Cells { get; set; } = new List<PeerCellModel>();
    }

    public sealed class PeerCellModel
    {
        public string Id { get; set; } = "";
        public string Destination { get; set; } = "";
        /// <summary>Difficulty band 0..2 along the style's axis; -1 = the whole style.</summary>
        public int Band { get; set; } = -1;
        public int Players { get; set; }
        public int Maps { get; set; }
    }

    /// <summary>
    /// One map in one cell: <see cref="Ease"/> = the map's term of the fit; <see cref="Values"/>[i] = median accuracy of
    /// population players at level <see cref="Levels"/>[i] (ascending; each a median of ≥ 8 players).
    /// </summary>
    public sealed class PeerCurve
    {
        public double Ease { get; set; }
        public List<double> Levels { get; set; } = new List<double>();
        public List<double> Values { get; set; } = new List<double>();
    }

    /// <summary>Destination rules thresholds (rules v6, D-018..D-021). Mirrors training/explore/profiles.py THRESHOLDS.</summary>
    public sealed class DestinationThresholds
    {
        /// <summary>Accuracy vs BL predicted acc needed for Accuracy / Midspeed.</summary>
        public double AccResid { get; set; } = -0.01;
        /// <summary>Accuracy: typical top-play pass at most this.</summary>
        public double LowPass { get; set; } = 3.0;
        /// <summary>Midspeed: typical top-play pass at most this (high end of midspeed, D-021).</summary>
        public double MidPass { get; set; } = 8.0;
        /// <summary>Midspeed "choice": pass ceiling at least typical pass + this.</summary>
        public double MidGap { get; set; } = 1.0;
        /// <summary>Tech-led: typical tech at least TechRatio x typical pass ...</summary>
        public double TechRatio { get; set; } = 0.8;
        /// <summary>... and at least TechMin (slow maps have tech above pass naturally).</summary>
        public double TechMin { get; set; } = 5.0;
        /// <summary>High Tech arrival: typical tech at least this.</summary>
        public double TechArrive { get; set; } = 6.0;
        /// <summary>Speed arrival: typical pass at least this.</summary>
        public double SpeedArrive { get; set; } = 8.0;
        /// <summary>Extreme: typical top-play effective stars at least this.</summary>
        public double ExtremeStars { get; set; } = 13.0;
        /// <summary>The Extreme path only counts maps at least this hard (effective stars).</summary>
        public double ExtremePathMinStars { get; set; } = 10.0;
    }

    /// <summary>Multinomial logistic regression over peer-relative style features (exploration 06).</summary>
    public sealed class LeaningParameters
    {
        public List<string> Features { get; set; } = new List<string>();
        /// <summary>Level (log of top-N pp sum) bin centers, and per-feature population medians at those centers.</summary>
        public List<double> PeerLevelCenters { get; set; } = new List<double>();
        public Dictionary<string, List<double>> PeerMedians { get; set; } = new Dictionary<string, List<double>>();
        public List<double> Mean { get; set; } = new List<double>();
        public List<double> Scale { get; set; } = new List<double>();
        public List<string> Classes { get; set; } = new List<string>();
        public List<List<double>> Coefficients { get; set; } = new List<List<double>>();
        public List<double> Intercepts { get; set; } = new List<double>();
    }

    /// <summary>One destination's path: the rating axis that measures difficulty on it, and its waypoints (D-022).</summary>
    public sealed class PathModel
    {
        public string Destination { get; set; } = "";
        /// <summary>Share of historical waypoint moves that were mainly "push" (harder maps) rather than "polish".</summary>
        public double PushPrior { get; set; } = 0.5;
        /// <summary>Spread of D and A on this path, used to normalise gaps.</summary>
        public double DifficultySpread { get; set; } = 1.0;
        public double AccuracySpread { get; set; } = 0.01;
        /// <summary>
        /// When true a player cannot be placed past a waypoint whose (smoothed) difficulty they have not reached:
        /// path pp alone lets breadth on easier maps inflate the position (e.g. a Midspeed acc player at "top of Speed").
        /// False for polish-dominated paths (Accuracy), where difficulty stays flat.
        /// </summary>
        public bool DifficultyGated { get; set; } = true;
        /// <summary>
        /// D-062 (user): a player cannot be placed past a waypoint whose (smoothed) accuracy vs predicted they have not
        /// (nearly) reached: within a quarter of the step from the previous waypoint. Set on ScoreSaber's ungated styles
        /// (Accuracy, Midspeed), whose cross-sectional waypoints mix in top players who only dabble in the style, so path
        /// pp alone placed players far too high.
        /// </summary>
        public bool AccuracyGated { get; set; }
        public List<WaypointModel> Waypoints { get; set; } = new List<WaypointModel>();
    }

    public sealed class WaypointModel
    {
        public int Index { get; set; }
        public double PathPpLow { get; set; }
        public double PathPpHigh { get; set; }
        /// <summary>Typical difficulty reached on the path axis.</summary>
        public double Difficulty { get; set; }
        /// <summary>Typical accuracy vs BL predicted acc at that difficulty.</summary>
        public double AccuracyResidual { get; set; }
        public double Accuracy { get; set; }
        public int EquivalentRank { get; set; }
    }

    /// <summary>A ranked map difficulty with everything Core needs to rate a play on it.</summary>
    public sealed class MapEntry
    {
        public string Hash { get; set; } = "";
        public string Characteristic { get; set; } = "Standard";
        public string Difficulty { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Mapper name(s) as BeatLeader shows them (may be empty in older models).</summary>
        public string Mapper { get; set; } = "";
        /// <summary>BL map type tag: acc / midspeed / speed / tech (may be empty for unranked maps).</summary>
        public string MapType { get; set; } = "";
        public double Duration { get; set; }
        public double PeakEbpm { get; set; }
        /// <summary>Note jump speed from the map file (BeatLeader data); 0 = unknown (models built before D-056).</summary>
        public double Njs { get; set; }
        /// <summary>Notes per second as BeatLeader shows it (notes / note span); 0 = unknown (models built before D-059).</summary>
        public double Nps { get; set; }
        public bool RankedBeatLeader { get; set; }
        public bool RankedScoreSaber { get; set; }
        public bool RankedAccSaber { get; set; }
        /// <summary>ScoreSaber star rating at model build time (D-062 SS pp); 0 when not ScoreSaber-ranked or before D-062.</summary>
        public double ScoreSaberStars { get; set; }
        /// <summary>AccSaber category code (true_acc / standard_acc / tech_acc) when AccSaber-ranked; empty before D-061.</summary>
        public string AccSaberCategory { get; set; } = "";
        /// <summary>AccSaber complexity at model build time (D-061 tracks' difficulty axis); 0 when not AccSaber-ranked.</summary>
        public double AccSaberComplexity { get; set; }
        /// <summary>Curated on BeatSaver (Explore's Curated pool, D-045). Curated maps ranked nowhere have no pp curves.</summary>
        public bool Curated { get; set; }
        /// <summary>BeatSaver vote score (0..1), set for curated maps: orders Explore's Curated "never played" list.</summary>
        public double BeatSaverScore { get; set; }
        /// <summary>Ratings by speed modifier: "" (none), "SS", "FS", "SF".</summary>
        public Dictionary<string, MapRatings> Ratings { get; set; } = new Dictionary<string, MapRatings>();
        /// <summary>acc -> pp lookup curves by speed modifier (D-026).</summary>
        public Dictionary<string, PpCurve> PpCurves { get; set; } = new Dictionary<string, PpCurve>();
        /// <summary>How many players at "Destination|waypoint" newly cleared this map (recommendations, D-025).</summary>
        public Dictionary<string, int> PeerPopularity { get; set; } = new Dictionary<string, int>();
        /// <summary>Peer target curves by cell id (D-058): "Destination" and "Destination|band"; empty when no population scores.</summary>
        public Dictionary<string, PeerCurve> PeerTargets { get; set; } = new Dictionary<string, PeerCurve>();
    }

    public sealed class MapRatings
    {
        public double Stars { get; set; }
        public double Pass { get; set; }
        public double Acc { get; set; }
        public double Tech { get; set; }
        public double PredictedAcc { get; set; }
    }
}
