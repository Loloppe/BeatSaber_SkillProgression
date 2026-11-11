using System;
using System.Collections.Generic;

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
        public List<MapEntry> Maps { get; set; } = new List<MapEntry>();
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
        /// <summary>BL map type tag: acc / midspeed / speed / tech (may be empty for unranked maps).</summary>
        public string MapType { get; set; } = "";
        public double Duration { get; set; }
        public double PeakEbpm { get; set; }
        public bool RankedBeatLeader { get; set; }
        public bool RankedScoreSaber { get; set; }
        public bool RankedAccSaber { get; set; }
        /// <summary>Ratings by speed modifier: "" (none), "SS", "FS", "SF".</summary>
        public Dictionary<string, MapRatings> Ratings { get; set; } = new Dictionary<string, MapRatings>();
        /// <summary>acc -> pp lookup curves by speed modifier (D-026).</summary>
        public Dictionary<string, PpCurve> PpCurves { get; set; } = new Dictionary<string, PpCurve>();
        /// <summary>How many players at "Destination|waypoint" newly cleared this map (recommendations, D-025).</summary>
        public Dictionary<string, int> PeerPopularity { get; set; } = new Dictionary<string, int>();
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
