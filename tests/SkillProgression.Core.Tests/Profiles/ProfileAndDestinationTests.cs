using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Profiles;

public class ProfileAndDestinationTests
{
    private static readonly DestinationThresholds Rules = new();

    private static PlayerProfile Profile(IEnumerable<(MapEntry map, double acc, string[] mods)> plays)
    {
        var list = plays.ToList();
        var catalog = new MapCatalog(list.Select(p => p.map).Distinct());
        var rated = list.Select(p => catalog.Rate(Play(p.map, p.acc, p.mods))!);
        return ProfileBuilder.Build(rated)!;
    }

    private static IEnumerable<(MapEntry, double, string[])> Many(int n, Func<int, MapEntry> map, double acc, params string[] mods) =>
        Enumerable.Range(0, n).Select(i => (map(i), acc, mods));

    [Fact]
    public void Best_play_per_map_wins_and_top_n_is_capped()
    {
        var map = Map(5, 3, 3);
        var catalog = new MapCatalog(new[] { map });
        var rated = new[] { 0.80, 0.95, 0.90 }.Select(a => catalog.Rate(Play(map, a))!);
        var best = ProfileBuilder.BestPerMap(rated);
        Assert.Single(best);
        Assert.Equal(0.95, best[0].Accuracy);

        var profile = Profile(Many(40, i => Map(5, 3, 3), 0.9));
        Assert.Equal(40, profile.PlayCount);
        Assert.Equal(ProfileBuilder.DefaultTopN, profile.TopPlayCount);
    }

    [Fact]
    public void Style_features_are_pp_weighted()
    {
        // Two maps, the faster one is worth twice the pp at the same acc.
        var slow = Map(4, 2, 3, type: "acc", predictedAcc: 0.97, ppAt100: 100);
        var fast = Map(10, 9, 5, type: "speed", predictedAcc: 0.90, duration: 300, ppAt100: 200);
        var p = Profile(new[] { (slow, 0.98, Array.Empty<string>()), (fast, 0.88, new[] { "FS" }) });

        var wSlow = 98.0; var wFast = 176.0; var total = wSlow + wFast;
        Assert.Equal((2 * wSlow + 9 * wFast) / total, p.Pass, 9);
        Assert.Equal(wSlow / total, p.Style[StyleFeatures.TypeAcc], 9);
        Assert.Equal(wFast / total, p.Style[StyleFeatures.TypeSpeed], 9);
        Assert.Equal(wFast / total, p.Style[StyleFeatures.SpeedMods], 9);
        Assert.Equal(wFast / total, p.Style[StyleFeatures.LongMaps], 9);
        Assert.Equal(wFast / total, p.Style[StyleFeatures.Sub95], 9);
        Assert.Equal((0.01 * wSlow - 0.02 * wFast) / total, p.Style[StyleFeatures.AccResidual], 9);
        Assert.Equal(wSlow / total, p.ShareUnder5, 9);
        Assert.Equal(wFast / total, p.Share9To13, 9);
    }

    [Fact]
    public void Accuracy_player_on_slow_maps() =>
        Assert.Equal(Destinations.Accuracy,
            DestinationRules.Classify(Profile(Many(30, i => Map(4, 1.5, 2.5, "acc", predictedAcc: 0.985), 0.99)), Rules));

    [Fact]
    public void Low_accuracy_on_slow_maps_is_unspecialised() =>
        Assert.Equal(Destinations.Unspecialised,
            DestinationRules.Classify(Profile(Many(30, i => Map(4, 1.5, 2.5, "acc", predictedAcc: 0.985), 0.90)), Rules));

    [Fact]
    public void Midspeed_needs_choice_a_pass_ceiling_above_the_farmed_maps()
    {
        var farmed = Many(30, i => Map(8, 6, 4.5, predictedAcc: 0.97), 0.975).ToList();
        Assert.Equal(Destinations.Unspecialised, DestinationRules.Classify(Profile(farmed), Rules));

        // Same farm plus a few low-pp clears of faster maps -> pass ceiling well above 6
        var withFaster = farmed.Concat(Many(5, i => Map(9, 9.5, 4.5, predictedAcc: 0.9, ppAt100: 10), 0.85)).ToList();
        Assert.Equal(Destinations.Midspeed, DestinationRules.Classify(Profile(withFaster), Rules));
    }

    [Fact]
    public void Slow_tech_acc_is_the_high_tech_path() =>
        Assert.Equal(Destinations.HighTech,
            DestinationRules.Classify(Profile(Many(30, i => Map(9, 5, 8, "tech", predictedAcc: 0.95), 0.96)), Rules));

    [Fact]
    public void Tech_led_at_higher_speed_is_high_tech_like_rank_1()
    {
        // User ruling D-021: pass 9.5 / tech 7.75 is High Tech
        var p = Profile(Many(30, i => Map(12.4, 9.5, 7.75, "tech", predictedAcc: 0.97), 0.95));
        Assert.Equal(Destinations.HighTech, DestinationRules.Classify(p, Rules));
    }

    [Fact]
    public void Fast_non_tech_maps_are_speed() =>
        Assert.Equal(Destinations.Speed,
            DestinationRules.Classify(Profile(Many(30, i => Map(11, 10, 4.5, "speed", predictedAcc: 0.96), 0.93)), Rules));

    [Fact]
    public void Very_high_effective_stars_are_extreme_even_with_good_acc()
    {
        var sf = new MapRatings { Stars = 14.5, Pass = 13.5, Tech = 6, Acc = 14.5, PredictedAcc = 0.95 };
        var p = Profile(Many(30, i => Map(11, 10, 5, "speed", sf: sf), 0.955, "SF"));
        Assert.Equal(Destinations.Extreme, DestinationRules.Classify(p, Rules));
    }
}
