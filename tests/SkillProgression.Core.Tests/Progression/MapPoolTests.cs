using SkillProgression.Core.Model;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class MapPoolTests
{
    [Fact]
    public void Recommendations_come_from_the_selected_pool_and_accsaber_only_on_accuracy()
    {
        var bl = Map(5, 2, 3, "acc");
        var ss = Map(5, 2, 3, "acc");
        ss.RankedBeatLeader = false;
        ss.RankedScoreSaber = true;
        var acc = Map(5, 2, 3, "acc");
        acc.RankedBeatLeader = false;
        acc.RankedAccSaber = true;
        var catalog = new MapCatalog(new[] { bl, ss, acc });

        Assert.Equal(new[] { bl }, catalog.Recommendable("Accuracy"));
        catalog.Pool = MapPool.ScoreSaber;
        Assert.Equal(new[] { ss }, catalog.Recommendable("Accuracy"));
        catalog.Pool = MapPool.AccSaber;
        Assert.Equal(new[] { acc }, catalog.Recommendable("Accuracy"));
        Assert.Equal(new[] { bl }, catalog.Recommendable("Speed")); // AccSaber ranks accuracy maps only (D-023)
        catalog.Pool = MapPool.AnyRanked;
        Assert.Equal(3, catalog.Recommendable("Speed").Count());
    }

    [Fact]
    public void Curated_pool_lists_curated_maps_ranked_or_not()
    {
        var ranked = Map(5, 2, 3, "acc");
        var rankedCurated = Map(5, 2, 3, "acc");
        rankedCurated.Curated = true;
        var curatedOnly = Curated(Map(5, 2, 3, "acc"));
        var catalog = new MapCatalog(new[] { ranked, rankedCurated, curatedOnly }) { Pool = MapPool.Curated };

        Assert.Equal(new[] { rankedCurated, curatedOnly }, catalog.Recommendable("Accuracy"));
        catalog.Pool = MapPool.AnyRanked;
        Assert.Equal(new[] { ranked, rankedCurated }, catalog.Recommendable("Accuracy"));
    }

    [Fact]
    public void Progression_ignores_curated_maps_ranked_nowhere()
    {
        var ranked = Map(5, 2, 3, "acc");
        var curatedOnly = Curated(Map(5, 2, 3, "acc"));
        var engine = new ProgressionEngine(new ModelFile { Maps = { ranked, curatedOnly } });

        Assert.NotNull(engine.Catalog.Rate(Play(ranked, 0.95)));
        Assert.Null(engine.Catalog.Rate(Play(curatedOnly, 0.95)));
        Assert.NotNull(engine.ExploreCatalog.Rate(Play(curatedOnly, 0.95)));
    }

    [Fact]
    public void Every_accsaber_map_is_an_accuracy_map_in_the_accsaber_pool()
    {
        var t = new DestinationThresholds();
        var lowPass = Map(5, t.LowPass - 0.5, 2, "acc");
        var highPass = Map(6, t.LowPass + 1.5, 2, "midspeed"); // pass above the Accuracy limit
        foreach (var m in new[] { lowPass, highPass }) { m.RankedBeatLeader = false; m.RankedAccSaber = true; }
        var catalog = new MapCatalog(new[] { lowPass, highPass }) { Pool = MapPool.AccSaber };

        Assert.True(PathRules.IsRecommendable(catalog, highPass, highPass.Ratings[""], "Accuracy", t));
        Assert.True(PathRules.IsRecommendable(catalog, lowPass, lowPass.Ratings[""], "Accuracy", t));
        // Progress is still measured by style rules, and other pools keep them for recommendations too.
        Assert.False(PathRules.IsRelevant("Accuracy", highPass.Ratings[""], t));
        catalog.Pool = MapPool.AnyRanked;
        Assert.False(PathRules.IsRecommendable(catalog, highPass, highPass.Ratings[""], "Accuracy", t));
    }

    private static MapEntry Curated(MapEntry map)
    {
        map.RankedBeatLeader = false;
        map.Curated = true;
        map.PpCurves.Clear();
        return map;
    }
}
