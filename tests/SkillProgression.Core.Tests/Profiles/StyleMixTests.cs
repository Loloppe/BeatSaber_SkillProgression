using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Profiles;

public class StyleMixTests
{
    private static readonly DestinationThresholds Rules = new();

    private static StyleMix Mix(params (int count, Func<MapEntry> map, double acc)[] groups)
    {
        var maps = new List<(MapEntry map, double acc)>();
        foreach (var (count, map, acc) in groups)
            for (var i = 0; i < count; i++) maps.Add((map(), acc));
        var catalog = new MapCatalog(maps.Select(m => m.map));
        return StyleMixCalculator.Compute(maps.Select(m => catalog.Rate(Play(m.map, m.acc))!), Rules);
    }

    [Theory]
    [InlineData(4, 2.0, 3.0, "Accuracy")]
    [InlineData(8, 6.0, 4.5, "Midspeed")]
    [InlineData(11, 10.0, 4.5, "Speed")]
    [InlineData(10, 6.0, 8.0, "High Tech")]
    [InlineData(13.5, 12.0, 6.0, "Extreme")]
    public void Each_play_belongs_to_exactly_one_style(double stars, double pass, double tech, string style) =>
        Assert.Equal(style, StyleMixCalculator.StyleOf(new MapRatings { Stars = stars, Pass = pass, Tech = tech }, Rules));

    [Fact]
    public void A_fast_map_tagged_tech_by_BeatLeader_is_not_High_Tech()
    {
        // D-053: the tag mostly marks fast speed/extreme maps; only the ratings decide (pass 12, tech 9 < 0.8 x pass)
        var mix = Mix((30, () => Map(12.5, 12, 9, "tech", predictedAcc: 0.95), 0.94));
        Assert.Equal(1.0, mix.Shares["Speed"], 9);
        Assert.Equal(0.0, mix.Shares["High Tech"], 9);
    }

    [Fact]
    public void Shares_partition_the_top_play_pp()
    {
        var mix = Mix((15, () => Map(4, 2, 3, "acc", predictedAcc: 0.98), 0.985),
                      (15, () => Map(11, 9.5, 4.5, "speed", predictedAcc: 0.95), 0.94));
        Assert.Equal(1.0, mix.Shares.Values.Sum(), 9);
        var acc = 15 * 0.985 * 300; var speed = 15 * 0.94 * 300; // pp = acc x 300 on these test maps
        Assert.Equal(acc / (acc + speed), mix.Shares["Accuracy"], 9);
        Assert.Equal(speed / (acc + speed), mix.Shares["Speed"], 9);
    }

    [Fact]
    public void A_mixed_player_can_have_several_established_styles()
    {
        // "accuracy + speed" player (review round 3)
        var mix = Mix((15, () => Map(4, 2, 3, "acc", predictedAcc: 0.98), 0.985),
                      (15, () => Map(11, 9.5, 4.5, "speed", predictedAcc: 0.95), 0.94));
        Assert.Equal(new[] { "Accuracy", "Speed" }, mix.Established.OrderBy(x => x));
    }

    [Fact]
    public void A_small_share_or_failing_the_criteria_is_not_established()
    {
        var mix = Mix((27, () => Map(11, 9.5, 4.5, "speed", predictedAcc: 0.95), 0.94),
                      (3, () => Map(4, 2, 3, "acc", predictedAcc: 0.98), 0.985),    // 10%: too small
                      (0, () => Map(4, 2, 3), 0));
        Assert.Equal(new[] { "Speed" }, mix.Established);
        Assert.Equal("Speed", mix.Main);

        var lowAcc = Mix((30, () => Map(4, 2, 3, "acc", predictedAcc: 0.98), 0.95)); // 3% under predicted
        Assert.Empty(lowAcc.Established);
        Assert.Equal(Destinations.Unspecialised, lowAcc.Main);
    }

    [Fact]
    public void Established_styles_are_ordered_by_share()
    {
        var mix = Mix((10, () => Map(4, 2, 3, "acc", predictedAcc: 0.98), 0.985),
                      (20, () => Map(11, 9.5, 4.5, "speed", predictedAcc: 0.95), 0.94));
        Assert.Equal(new[] { "Speed", "Accuracy" }, mix.Established);
    }
}
