using SkillProgression.Core.Model;
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
}
