using System;
using System.Collections.Generic;
using SkillProgression.Core.Model;

namespace SkillProgression.Core.Scoring
{
    /// <summary>Platform whose own value (pp / AP) a map row shows (D-060).</summary>
    public enum ScorePlatform
    {
        BeatLeader,
        ScoreSaber,
        AccSaber,
    }

    /// <summary>
    /// Live per-map facts fetched from the platforms for display (D-060): BeatLeader ratings / curve / jump data, ScoreSaber
    /// stars, AccSaber complexity. Null / 0 = unknown or not ranked there. Not the model's pp space (D-026).
    /// </summary>
    public sealed class LiveMapInfo
    {
        public double Bpm { get; set; }
        public double Njs { get; set; }
        public double NjsOffset { get; set; }
        public double Stars { get; set; }
        public double Pass { get; set; }
        public double Acc { get; set; }
        public double Tech { get; set; }
        public string CoverUrl { get; set; } = "";
        /// <summary>BeatLeader's accuracy -> pp curve without modifiers (`/pp/curve`); null when the map has no BL pp.</summary>
        public PpCurve? BeatLeaderCurve { get; set; }
        /// <summary>ScoreSaber stars when ranked there.</summary>
        public double? ScoreSaberStars { get; set; }
        /// <summary>AccSaber complexity when ranked there.</summary>
        public double? AccSaberComplexity { get; set; }
    }

    /// <summary>Platform-wide curves (ScoreSaber pp curve, AccSaber score curve), fetched once.</summary>
    public sealed class LiveCurves
    {
        public PpCurve? ScoreSaber { get; set; }
        public PpCurve? AccSaber { get; set; }
        /// <summary>AccSaber curve parameters (`/v1/curves`: scale 61, shift -18 on 2026-11-22).</summary>
        public double AccSaberScale { get; set; } = 61;
        public double AccSaberShift { get; set; } = -18;
    }

    /// <summary>One platform's value at an accuracy, e.g. 530 pp or 1,085 AP.</summary>
    public readonly struct ScoreValue
    {
        public ScorePlatform Platform { get; }
        public double Value { get; }

        public ScoreValue(ScorePlatform platform, double value)
        {
            Platform = platform;
            Value = value;
        }
    }

    /// <summary>
    /// Each platform's own value of an accuracy on a map (D-060), no modifiers. Formulas verified against live scores
    /// 2026-11-22 (docs/API_INTEGRATIONS.md): BeatLeader = its `/pp/curve` points interpolated (as PPCounter does);
    /// ScoreSaber = stars × 42.117 × curve(acc); AccSaber AP = curve(acc) × (complexity − shift) × scale.
    /// </summary>
    public static class LiveScoring
    {
        /// <summary>ScoreSaber pp per star at 95% (curve value 1).</summary>
        public const double ScoreSaberPpPerStar = 42.117208413;

        /// <summary>Values of every platform the map is ranked on, BeatLeader first.</summary>
        public static IReadOnlyList<ScoreValue> All(LiveMapInfo? info, LiveCurves? curves, double accuracy)
        {
            var result = new List<ScoreValue>();
            foreach (ScorePlatform p in Enum.GetValues(typeof(ScorePlatform)))
                if (Value(p, info, curves, accuracy) is double v) result.Add(new ScoreValue(p, v));
            return result;
        }

        /// <summary>One platform's value; null when the map isn't ranked there or the data isn't loaded.</summary>
        public static double? Value(ScorePlatform platform, LiveMapInfo? info, LiveCurves? curves, double accuracy)
        {
            if (info == null) return null;
            accuracy = Math.Max(0, Math.Min(1, accuracy));
            switch (platform)
            {
                case ScorePlatform.BeatLeader:
                    return info.BeatLeaderCurve is { } bl ? bl.Evaluate(accuracy) : (double?)null;
                case ScorePlatform.ScoreSaber:
                    return info.ScoreSaberStars is double s && s > 0 && curves?.ScoreSaber is { } ss
                        ? s * ScoreSaberPpPerStar * ss.Evaluate(accuracy) : (double?)null;
                default:
                    return info.AccSaberComplexity is double c && curves?.AccSaber is { } ac
                        ? ac.Evaluate(accuracy) * (c - curves.AccSaberShift) * curves.AccSaberScale : (double?)null;
            }
        }

        /// <summary>
        /// The platform a row shows (user, 2026-11-22: follow the pool): BeatLeader / ScoreSaber / AccSaber pools show their
        /// own value; Any ranked and Curated show BeatLeader pp when BL-ranked, else ScoreSaber pp, else AccSaber AP.
        /// Null when the map is ranked nowhere.
        /// </summary>
        public static ScorePlatform? ForPool(MapPool pool, MapEntry map)
        {
            switch (pool)
            {
                case MapPool.BeatLeader when map.RankedBeatLeader: return ScorePlatform.BeatLeader;
                case MapPool.ScoreSaber when map.RankedScoreSaber: return ScorePlatform.ScoreSaber;
                case MapPool.AccSaber when map.RankedAccSaber: return ScorePlatform.AccSaber;
            }
            if (map.RankedBeatLeader) return ScorePlatform.BeatLeader;
            if (map.RankedScoreSaber) return ScorePlatform.ScoreSaber;
            if (map.RankedAccSaber) return ScorePlatform.AccSaber;
            return null;
        }

        /// <summary>
        /// Base jump distance in metres, as the game computes it without JD mods: half jump of 4 beats, halved while the
        /// half jump distance exceeds 18 m, plus the map's offset, at least 0.25 beats. 0 when BPM or NJS is unknown.
        /// </summary>
        public static double BaseJumpDistance(double bpm, double njs, double offset)
        {
            if (bpm <= 0 || njs <= 0) return 0;
            var beat = 60.0 / bpm;
            var halfJump = 4.0;
            while (njs * beat * halfJump > 17.999) halfJump /= 2;
            halfJump = Math.Max(0.25, halfJump + offset);
            return njs * beat * halfJump * 2;
        }
    }
}
