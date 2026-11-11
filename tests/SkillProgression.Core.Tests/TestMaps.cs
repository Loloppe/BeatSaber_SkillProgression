using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;

namespace SkillProgression.Core.Tests;

/// <summary>Builders for synthetic maps and plays used across Core tests.</summary>
internal static class TestMaps
{
    private static int _next;

    /// <summary>A map whose pp is linear in accuracy: pp = ppAt100 * acc (simple, predictable curve).</summary>
    public static MapEntry Map(double stars, double pass, double tech, string type = "midspeed", double predictedAcc = 0.95,
        double duration = 120, double ppAt100 = 300, MapRatings? sf = null)
    {
        var hash = $"{++_next:X40}";
        var map = new MapEntry
        {
            Hash = hash, Difficulty = "ExpertPlus", MapType = type, Duration = duration, RankedBeatLeader = true,
            Ratings = { [""] = new MapRatings { Stars = stars, Pass = pass, Tech = tech, Acc = stars, PredictedAcc = predictedAcc } },
            PpCurves = { [""] = Line(ppAt100) },
        };
        if (sf != null)
        {
            map.Ratings["SF"] = sf;
            map.PpCurves["SF"] = Line(ppAt100 * 1.5);
        }
        return map;
    }

    public static PpCurve Line(double ppAt100) => new() { Acc = { 0.0, 1.0 }, Pp = { 0.0, ppAt100 } };

    public static MapKey Key(MapEntry m) => new(m.Hash, m.Characteristic, m.Difficulty);

    public static PlayEvidence Play(MapEntry m, double acc, params string[] mods) =>
        new(Key(m), acc, mods, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), EvidenceSource.BeatLeader);
}
