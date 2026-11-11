using System;
using System.Collections.Generic;

namespace SkillProgression.Core.Plays
{
    /// <summary>How a play started through the plugin ended (D-011).</summary>
    public enum PlayOutcome
    {
        Cleared,
        Failed,
        Quit,
        Restarted,
    }

    /// <summary>
    /// One play started through the plugin, recorded in the local play log (D-011).
    /// Primary evidence for the player's current profile; independent of Unity and the game types.
    /// </summary>
    public sealed class PlayRecord
    {
        /// <summary>Format version of this record, for future migrations.</summary>
        public int Version { get; init; } = 2;

        public DateTime PlayedAtUtc { get; init; }

        /// <summary>Map identity (canonical key: upper-case hash + characteristic + difficulty).</summary>
        public string Hash { get; init; } = "";
        public string Characteristic { get; init; } = "Standard";
        public string Difficulty { get; init; } = "";

        public PlayOutcome Outcome { get; init; }
        public bool Practice { get; init; }

        /// <summary>Accuracy in [0, 1]: multiplied score / max multiplied score (modifiers excluded).</summary>
        public double Accuracy { get; init; }
        public int MultipliedScore { get; init; }
        public int ModifiedScore { get; init; }
        public int MaxMultipliedScore { get; init; }

        public int Misses { get; init; }
        public int BadCuts { get; init; }
        public int MaxCombo { get; init; }
        public bool FullCombo { get; init; }

        /// <summary>BeatLeader-style modifier codes, e.g. "FS", "NF".</summary>
        public IReadOnlyList<string> Modifiers { get; init; } = Array.Empty<string>();

        /// <summary>Seconds into the song when the play ended, and the song length.</summary>
        public double EndSongTime { get; init; }
        public double SongDuration { get; init; }

        /// <summary>
        /// No Fail play whose energy reached 0 (version 2+): the game reports it as cleared, but it is a fail for the
        /// plugin (no evidence, counts as a miss).
        /// </summary>
        public bool SoftFailed { get; init; }
        /// <summary>Seconds into the song when energy reached 0 (soft fails only).</summary>
        public double? SoftFailSongTime { get; init; }
        /// <summary>Times the player paused during the play (version 2+).</summary>
        public int Pauses { get; init; }
        /// <summary>Accuracy target shown for this play (Next up / calibration target, Explore expected acc); null in older
        /// records and for plays without one. Used to judge success afterwards (D-042).</summary>
        public double? TargetAccuracy { get; init; }

        /// <summary>Reached its target (or, without one, passed): a success for adaptive step sizes (D-042).</summary>
        public bool ReachedTarget() => IsPass() && (TargetAccuracy is not double t || Accuracy >= t);

        /// <summary>A real pass: cleared without a No Fail soft fail.</summary>
        public bool IsPass() => Outcome == PlayOutcome.Cleared && !SoftFailed;
    }
}
