using System;

namespace SkillProgression.Core.Maps
{
    /// <summary>
    /// Canonical identity of a beatmap difficulty across BeatLeader, ScoreSaber and AccSaber:
    /// upper-case song hash + characteristic + difficulty name (docs/API_INTEGRATIONS.md).
    /// </summary>
    public readonly struct MapKey : IEquatable<MapKey>
    {
        public string Hash { get; }
        public string Characteristic { get; }
        public string Difficulty { get; }

        public MapKey(string hash, string characteristic, string difficulty)
        {
            if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("Hash is required.", nameof(hash));
            if (string.IsNullOrWhiteSpace(difficulty)) throw new ArgumentException("Difficulty is required.", nameof(difficulty));
            Hash = hash.Trim().ToUpperInvariant();
            Characteristic = string.IsNullOrWhiteSpace(characteristic) ? "Standard" : characteristic.Trim();
            Difficulty = difficulty.Trim();
        }

        public bool Equals(MapKey other) =>
            Hash == other.Hash &&
            string.Equals(Characteristic, other.Characteristic, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Difficulty, other.Difficulty, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is MapKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Hash?.GetHashCode() ?? 0;
                h = h * 397 ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Characteristic ?? "");
                return h * 397 ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Difficulty ?? "");
            }
        }

        public override string ToString() => $"{Hash}/{Characteristic}/{Difficulty}";
    }
}
