using SkillProgression.Core.Model;
using SkillProgression.Core.Scoring;

namespace SkillProgression.Core.Tests.Scoring;

public class LiveScoringTests
{
    private static readonly LiveCurves Curves = new()
    {
        // Excerpts of the live curves (2026-11-22): ScoreSaber 1.0 at 95%, AccSaber score curve.
        ScoreSaber = new PpCurve { Acc = { 0.95, 0.975, 0.98 }, Pp = { 1, 1.3807102743105126, 1.5702410055532239 } },
        AccSaber = new PpCurve { Acc = { 0.99, 0.995 }, Pp = { 0.6, 0.7 } },
    };

    [Fact]
    public void BeatLeader_value_interpolates_its_pp_curve()
    {
        var info = new LiveMapInfo { BeatLeaderCurve = new PpCurve { Acc = { 0.95, 0.96 }, Pp = { 374.53, 408.87 } } };
        Assert.Equal(391.70, LiveScoring.Value(ScorePlatform.BeatLeader, info, Curves, 0.955)!.Value, 2);
    }

    [Fact]
    public void ScoreSaber_value_matches_a_live_score()
    {
        // Leaderboard 46809 (5.26★): a 97.39% score was worth 298.917pp on ScoreSaber.
        var curve = new PpCurve { Acc = { 0.9725, 0.975 }, Pp = { 1.3090333065057616, 1.3807102743105126 } };
        var info = new LiveMapInfo { ScoreSaberStars = 5.26 };
        var pp = LiveScoring.Value(ScorePlatform.ScoreSaber, info, new LiveCurves { ScoreSaber = curve }, 487529 / 500595.0)!.Value;
        Assert.InRange(pp, 298.917 * 0.999, 298.917 * 1.001);
    }

    [Fact]
    public void AccSaber_ap_is_curve_times_complexity_plus_18_times_61()
    {
        var info = new LiveMapInfo { AccSaberComplexity = 8.6 };
        Assert.Equal(0.65 * 26.6 * 61, LiveScoring.Value(ScorePlatform.AccSaber, info, Curves, 0.9925)!.Value, 6);
    }

    [Fact]
    public void Unranked_or_unloaded_platforms_have_no_value()
    {
        Assert.Null(LiveScoring.Value(ScorePlatform.BeatLeader, null, Curves, 0.9));
        Assert.Null(LiveScoring.Value(ScorePlatform.ScoreSaber, new LiveMapInfo(), Curves, 0.9));
        Assert.Null(LiveScoring.Value(ScorePlatform.AccSaber, new LiveMapInfo { AccSaberComplexity = 5 }, null, 0.9));
        Assert.Empty(LiveScoring.All(new LiveMapInfo(), Curves, 0.9));
    }

    [Theory]
    [InlineData(MapPool.BeatLeader, true, true, true, ScorePlatform.BeatLeader)]
    [InlineData(MapPool.ScoreSaber, true, true, true, ScorePlatform.ScoreSaber)]
    [InlineData(MapPool.AccSaber, true, true, true, ScorePlatform.AccSaber)]
    [InlineData(MapPool.AnyRanked, true, true, true, ScorePlatform.BeatLeader)]
    [InlineData(MapPool.AnyRanked, false, true, true, ScorePlatform.ScoreSaber)]
    [InlineData(MapPool.AnyRanked, false, false, true, ScorePlatform.AccSaber)]
    [InlineData(MapPool.Curated, false, true, false, ScorePlatform.ScoreSaber)]
    // A pool the map isn't ranked in falls back like Any ranked (AccSaber pool on a non-Accuracy style).
    [InlineData(MapPool.ScoreSaber, true, false, false, ScorePlatform.BeatLeader)]
    public void Row_value_follows_the_pool(MapPool pool, bool bl, bool ss, bool acc, ScorePlatform expected)
    {
        var map = new MapEntry { RankedBeatLeader = bl, RankedScoreSaber = ss, RankedAccSaber = acc };
        Assert.Equal(expected, LiveScoring.ForPool(pool, map));
    }

    [Fact]
    public void Map_ranked_nowhere_has_no_row_value() =>
        Assert.Null(LiveScoring.ForPool(MapPool.Curated, new MapEntry { Curated = true }));

    [Theory]
    [InlineData(171, 18, 0, 25.263)]  // 4-beat half jump is 25.3 m > 18 -> 2 beats
    [InlineData(120, 10, 0, 20.0)]    // 20 m > 18 -> 2 beats
    [InlineData(60, 4, 0, 32.0)]      // 16 m fits: 4 beats
    [InlineData(128, 16, -0.5, 22.5)] // 2 beats + offset -0.5
    [InlineData(128, 16, -5, 3.75)]   // never under 0.25 beats
    public void Base_jump_distance_follows_the_game(double bpm, double njs, double offset, double expected) =>
        Assert.Equal(expected, LiveScoring.BaseJumpDistance(bpm, njs, offset), 3);

    [Fact]
    public void Jump_distance_unknown_without_bpm_or_njs()
    {
        Assert.Equal(0, LiveScoring.BaseJumpDistance(0, 18, 0));
        Assert.Equal(0, LiveScoring.BaseJumpDistance(171, 0, 0));
    }
}
