using SkillProgression.Core.Model;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Model;

public class MapCatalogTests
{
    [Fact]
    public void Pp_curve_interpolates_and_clamps_like_numpy_interp()
    {
        var curve = new PpCurve { Acc = { 0.9, 0.95, 1.0 }, Pp = { 100, 200, 400 } };
        Assert.Equal(100, curve.Evaluate(0.5));
        Assert.Equal(150, curve.Evaluate(0.925), 9);
        Assert.Equal(300, curve.Evaluate(0.975), 9);
        Assert.Equal(400, curve.Evaluate(1.2));
    }

    [Fact]
    public void Unknown_map_is_not_rated()
    {
        var catalog = new MapCatalog(new[] { Map(5, 3, 3) });
        Assert.Null(catalog.Rate(Play(Map(6, 4, 4), 0.9)));
    }

    [Fact]
    public void Speed_modifier_play_uses_that_modifiers_ratings_and_curve()
    {
        var sf = new MapRatings { Stars = 14, Pass = 13, Tech = 6, Acc = 14, PredictedAcc = 0.93 };
        var map = Map(10, 9, 5, sf: sf);
        var rated = new MapCatalog(new[] { map }).Rate(Play(map, 0.9, "SF", "NF"))!;

        Assert.Same(sf, rated.Ratings);
        Assert.Equal(0.9 * 450, rated.Pp, 9);
        Assert.True(rated.UsesSpeedModifier);
    }

    [Fact]
    public void Missing_modifier_variant_falls_back_to_nomod()
    {
        var map = Map(10, 9, 5);
        var rated = new MapCatalog(new[] { map }).Rate(Play(map, 0.9, "FS"))!;
        Assert.Equal(10, rated.Ratings.Stars);
        Assert.Equal(0.9 * 300, rated.Pp, 9);
    }

    [Fact]
    public void Superfast_wins_over_other_speed_modifiers() =>
        Assert.Equal("SF", Play(Map(1, 1, 1), 0.9, "SS", "SF", "FS").SpeedModifier);
}
