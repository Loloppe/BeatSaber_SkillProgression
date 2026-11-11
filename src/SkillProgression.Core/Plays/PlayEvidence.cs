using System;
using System.Collections.Generic;
using SkillProgression.Core.Maps;

namespace SkillProgression.Core.Plays
{
    public enum EvidenceSource
    {
        /// <summary>A personal best fetched from BeatLeader.</summary>
        BeatLeader,
        /// <summary>A play recorded by the plugin itself (D-011).</summary>
        LocalPlayLog,
    }

    /// <summary>One cleared play used as evidence of skill, whatever its source.</summary>
    public sealed class PlayEvidence
    {
        public MapKey Map { get; }
        /// <summary>Accuracy in [0, 1].</summary>
        public double Accuracy { get; }
        public IReadOnlyList<string> Modifiers { get; }
        public DateTime TimeUtc { get; }
        public EvidenceSource Source { get; }

        public PlayEvidence(MapKey map, double accuracy, IReadOnlyList<string>? modifiers, DateTime timeUtc, EvidenceSource source)
        {
            Map = map;
            Accuracy = accuracy;
            Modifiers = modifiers ?? Array.Empty<string>();
            TimeUtc = timeUtc;
            Source = source;
        }

        /// <summary>
        /// Speed modifier that determines the effective ratings: SF over FS over SS, none otherwise
        /// (same priority as training/explore/profiles.py speed_mod).
        /// </summary>
        public string SpeedModifier
        {
            get
            {
                var found = "";
                foreach (var code in SpeedModifiers.Priority)
                    foreach (var m in Modifiers)
                        if (string.Equals(m, code, StringComparison.OrdinalIgnoreCase)) found = code;
                return found;
            }
        }
    }

    public static class SpeedModifiers
    {
        /// <summary>Ascending priority: a later code wins.</summary>
        public static readonly string[] Priority = { "SS", "FS", "SF" };
    }
}
