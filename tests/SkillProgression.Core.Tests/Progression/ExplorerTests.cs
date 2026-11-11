using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class ExplorerTests
{
    private static readonly DestinationThresholds Rules = new();

    private static (ModelFile model, MapCatalog catalog, List<RatedPlay> best) Setup(IEnumerable<(MapEntry map, double acc)> plays, IEnumerable<MapEntry> unplayed)
    {
        var playList = plays.ToList();
        var model = new ModelFile
        {
            Maps = playList.Select(p => p.map).Concat(unplayed).ToList(),
            Paths = { new PathModel { Destination = "Speed", DifficultySpread = 1.0, Waypoints = { new WaypointModel { Index = 0, Difficulty = 8 } } } },
        };
        var catalog = new MapCatalog(model.Maps);
        return (model, catalog, playList.Select(p => catalog.Rate(Play(p.map, p.acc))!).ToList());
    }

    [Fact]
    public void Estimator_learns_that_harder_maps_lower_accuracy_vs_prediction()
    {
        // residual -0.01 at pass 8, -0.03 at pass 10
        var plays = Enumerable.Range(0, 10).Select(i => (Map(10, 8 + 0.2 * i, 4, "speed", predictedAcc: 0.95), 0.95 - 0.01 - 0.002 * i * 1.0)).ToList();
        var (_, _, best) = Setup(plays, Array.Empty<MapEntry>());
        var est = AccuracyEstimator.Fit("Speed", best, Rules);
        Assert.Equal(-0.01, est.Residual(8), 3);
        Assert.Equal(-0.03, est.Residual(10), 3);
        Assert.Equal(0.95 - 0.03, est.Expected(new MapRatings { Pass = 10, Tech = 4, PredictedAcc = 0.95 }, "Speed"), 3);
    }

    [Fact]
    public void Estimator_never_expects_harder_maps_to_raise_accuracy()
    {
        var plays = Enumerable.Range(0, 10).Select(i => (Map(10, 8 + 0.2 * i, 4, "speed", predictedAcc: 0.95), 0.93 + 0.003 * i)).ToList();
        var (_, _, best) = Setup(plays, Array.Empty<MapEntry>());
        var est = AccuracyEstimator.Fit("Speed", best, Rules);
        Assert.Equal(est.Residual(8), est.Residual(12), 9);
    }

    [Fact]
    public void Improve_mode_lists_played_maps_where_a_better_score_is_expected()
    {
        var weak = Map(10, 9.0, 4, "speed", predictedAcc: 0.95);
        var plays = Enumerable.Range(0, 9).Select(_ => (Map(10, 9.0, 4, "speed", predictedAcc: 0.95), 0.94)).Append((weak, 0.90)).ToList();
        var (model, catalog, best) = Setup(plays, Array.Empty<MapEntry>());

        var items = Explorer.Suggest("Speed", ExploreMode.Improve, 0, best, model, catalog, seed: 1).Items;

        var item = Assert.Single(items);
        Assert.Equal(weak.Hash, item.Map.Hash);
        Assert.Equal(0.90, item.CurrentAccuracy);
        Assert.True(item.ExpectedAccuracy > 0.93);
    }

    [Fact]
    public void New_maps_mode_lists_unplayed_maps_and_the_slider_moves_the_window()
    {
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 9.0, 4, "speed"), 0.94)).ToList();
        var easier = Map(9, 8.0, 4, "speed");
        var same = Map(10, 9.1, 4, "speed");
        var harder = Map(12, 10.2, 4, "speed");
        var (model, catalog, best) = Setup(plays, new[] { easier, same, harder });

        // Few maps in this tiny catalog: the window widens (D-032) — the right map still comes first.
        var atLevel = Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 1);
        Assert.True(atLevel.Widened);
        Assert.Contains(atLevel.Items, i => i.Map.Hash == same.Hash);
        var counts = Explorer.Counts("Speed", ExploreMode.NewMaps, best, model, catalog);
        Assert.Equal(7, counts.Length);
        Assert.Equal(1, counts[3]);  // only 'same' within ±1 step of the level (9.0 ± 0.35)
        Assert.Contains(Explorer.Suggest("Speed", ExploreMode.NewMaps, -2, best, model, catalog, 1).Items, i => i.Map.Hash == easier.Hash);
        Assert.Contains(Explorer.Suggest("Speed", ExploreMode.NewMaps, 3, best, model, catalog, 1).Items, i => i.Map.Hash == harder.Hash);
    }

    [Fact]
    public void The_window_never_points_where_the_style_has_no_maps()
    {
        // Extreme-like: player level far below the style's maps; 'Easier' must still land on existing maps.
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 6.0, 4, "speed"), 0.94)).ToList();
        var hardOnly = Enumerable.Range(0, 12).Select(i => Map(12, 10.0 + 0.05 * i, 4, "speed")).ToList();
        var (model, catalog, best) = Setup(plays, hardOnly);
        var counts = Explorer.Counts("Speed", ExploreMode.NewMaps, best, model, catalog);
        Assert.All(counts.Take(4), c => Assert.True(c > 0));
    }

    [Fact]
    public void Different_seeds_give_different_lists_from_a_large_pool()
    {
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 9.0, 4, "speed"), 0.94)).ToList();
        var pool = Enumerable.Range(0, 60).Select(_ => Map(10, 9.0, 4, "speed")).ToList();
        var (model, catalog, best) = Setup(plays, pool);

        var a = Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 1).Items.Select(i => i.Map.Hash);
        var b = Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 2).Items.Select(i => i.Map.Hash);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Requirement_filter_lists_only_maps_using_the_selected_mod()
    {
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 9.0, 4, "speed"), 0.94)).ToList();
        var vanilla = Map(10, 9.0, 4, "speed");
        var noodle = Map(10, 9.0, 4, "speed");
        noodle.Requirements.Add(MapRequirements.NoodleExtensions);
        var both = Map(10, 9.0, 4, "speed");
        both.Requirements.AddRange(new[] { MapRequirements.NoodleExtensions, MapRequirements.Vivify });
        var (model, catalog, best) = Setup(plays, new[] { vanilla, noodle, both });

        string[] List(RequirementFilter f) => Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 1, requirements: f)
            .Items.Select(i => i.Map.Hash).OrderBy(h => h).ToArray();
        string[] Hashes(params MapEntry[] maps) => maps.Select(m => m.Hash).OrderBy(h => h).ToArray();

        Assert.Equal(Hashes(vanilla), List(RequirementFilter.None));
        Assert.Equal(Hashes(noodle, both), List(RequirementFilter.NoodleExtensions));
        Assert.Equal(Hashes(both), List(RequirementFilter.Vivify));
        Assert.Empty(List(RequirementFilter.MappingExtensions));
        Assert.Equal(Hashes(vanilla, noodle, both), List(RequirementFilter.All));
    }

    [Fact]
    public void Requirement_filter_also_applies_to_improve_mode()
    {
        var noodle = Map(10, 9.0, 4, "speed", predictedAcc: 0.95);
        noodle.Requirements.Add(MapRequirements.NoodleExtensions);
        var plays = Enumerable.Range(0, 9).Select(_ => (Map(10, 9.0, 4, "speed", predictedAcc: 0.95), 0.94)).Append((noodle, 0.90)).ToList();
        var (model, catalog, best) = Setup(plays, Array.Empty<MapEntry>());

        Assert.Empty(Explorer.Suggest("Speed", ExploreMode.Improve, 0, best, model, catalog, 1).Items);
        Assert.Single(Explorer.Suggest("Speed", ExploreMode.Improve, 0, best, model, catalog, 1, requirements: RequirementFilter.NoodleExtensions).Items);
    }
}
