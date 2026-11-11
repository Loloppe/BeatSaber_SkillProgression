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
    public void Curated_pool_ranks_never_played_maps_by_beatsaver_score()
    {
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 9.0, 4, "speed"), 0.94)).ToList();
        var curated = Enumerable.Range(0, 60).Select(i => CuratedOnly(Map(10, 9.0, 4, "speed"), score: i / 60.0)).ToList();
        var (model, catalog, best) = Setup(plays, curated);
        catalog.Pool = MapPool.Curated;

        var items = Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 1).Items;

        var top = curated.OrderByDescending(m => m.BeatSaverScore).Take(Explorer.DefaultCount * 4).Select(m => m.Hash).ToHashSet();
        Assert.Equal(Explorer.DefaultCount, items.Count);
        Assert.All(items, i => Assert.Contains(i.Map.Hash, top));
    }

    [Fact]
    public void Curated_plays_do_not_move_the_explore_level()
    {
        // No ranked Speed plays, many easy curated ones (no pp): the level is the style's first waypoint (8), as for a
        // player without ranked evidence, not the curated plays' difficulty.
        var easyCurated = Enumerable.Range(0, 30).Select(_ => (CuratedOnly(Map(6, 5.0, 4, "speed"), 0.5), 0.99));
        var candidates = Enumerable.Range(0, 70).Select(i => CuratedOnly(Map(8, 5.0 + 0.1 * i, 4, "speed"), 0.5)).ToList();
        var (model, catalog, best) = Setup(easyCurated, candidates);
        catalog.Pool = MapPool.Curated;

        var result = Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 1);

        Assert.InRange((result.RangeLow + result.RangeHigh) / 2, 7.9, 8.1);
    }

    [Fact]
    public void A_given_level_centres_the_window_instead_of_the_players_own()
    {
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 9.0, 4, "speed"), 0.94)).ToList();
        var pool = Enumerable.Range(0, 80).Select(i => Map(8, 6.0 + 0.05 * i, 4, "speed")).ToList(); // pass 6.0 - 9.95
        var (model, catalog, best) = Setup(plays, pool);

        var own = Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 1);
        var training = Explorer.Suggest("Speed", ExploreMode.NewMaps, 0, best, model, catalog, 1, level: 7.0);

        Assert.InRange((own.RangeLow + own.RangeHigh) / 2, 8.9, 9.1);
        Assert.InRange((training.RangeLow + training.RangeHigh) / 2, 6.9, 7.1);
        Assert.All(training.Items, i => Assert.InRange(i.AxisRating, 6.6, 7.4));
    }

    [Fact]
    public void Training_level_is_the_top_objectives_map_centre_else_the_starters()
    {
        var report = new PlayerReport();
        report.Paths["Speed"] = new PathProgress { Objectives = { new Objective { Kind = ObjectiveKind.Polish, MapCentre = 7.5 },
                                                                new Objective { Kind = ObjectiveKind.Push, MapCentre = 9.5 } } };
        report.Starters["Accuracy"] = new Objective { MapCentre = 4.2 };

        Assert.Equal(7.5, Explorer.TrainingLevel(report, "Speed"));
        Assert.Equal(4.2, Explorer.TrainingLevel(report, "Accuracy"));
        Assert.Null(Explorer.TrainingLevel(report, "Midspeed"));
    }

    [Fact]
    public void Easier_positions_are_shares_of_the_level()
    {
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 9.0, 4, "speed"), 0.94)).ToList();
        var pool = Enumerable.Range(0, 200).Select(i => Map(8, 3.0 + 0.04 * i, 4, "speed")).ToList(); // pass 3.0 - 10.96
        var (model, catalog, best) = Setup(plays, pool);

        double Centre(int offset)
        {
            var r = Explorer.Suggest("Speed", ExploreMode.NewMaps, offset, best, model, catalog, 1, level: 8.0);
            return (r.RangeLow + r.RangeHigh) / 2;
        }

        Assert.InRange(Centre(-1), 6.75, 6.85); // 85%
        Assert.InRange(Centre(-2), 5.55, 5.65); // 70%
        Assert.InRange(Centre(-3), 3.95, 4.05); // 50%
        Assert.InRange(Centre(1), 8.3, 8.4);    // + one step (0.35)
    }

    [Fact]
    public void Without_random_pick_the_list_is_the_top_candidates_in_rank_order()
    {
        var weakest = Enumerable.Range(0, 12).Select(i => (Map(10, 9.0, 4, "speed", predictedAcc: 0.95), 0.90 + 0.003 * i)).ToList();
        var plays = Enumerable.Range(0, 10).Select(_ => (Map(10, 9.0, 4, "speed", predictedAcc: 0.95), 0.94)).Concat(weakest).ToList();
        var (model, catalog, best) = Setup(plays, Array.Empty<MapEntry>());

        var a = Explorer.Suggest("Speed", ExploreMode.Improve, 0, best, model, catalog, seed: 1, count: 4, randomPick: false).Items;
        var b = Explorer.Suggest("Speed", ExploreMode.Improve, 0, best, model, catalog, seed: 2, count: 4, randomPick: false).Items;

        Assert.Equal(a.Select(i => i.Map), b.Select(i => i.Map));       // seed doesn't matter
        Assert.Equal(weakest.Take(4).Select(w => w.Item1.Hash), a.Select(i => i.Map.Hash)); // biggest expected gain first
    }

    private static MapEntry CuratedOnly(MapEntry map, double score)
    {
        map.RankedBeatLeader = false;
        map.Curated = true;
        map.BeatSaverScore = score;
        map.PpCurves.Clear();
        return map;
    }
}
