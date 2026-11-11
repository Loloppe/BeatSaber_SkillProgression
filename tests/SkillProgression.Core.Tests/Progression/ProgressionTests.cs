using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class ProgressionTests
{
    private static readonly DestinationThresholds Rules = new();

    // ---- Leaning ------------------------------------------------------------------------------------------------

    [Fact]
    public void Leaning_is_a_softmax_over_peer_relative_standardised_features()
    {
        var model = new LeaningParameters
        {
            Features = { StyleFeatures.TypeTech },
            PeerLevelCenters = { 0, 10 },
            PeerMedians = { [StyleFeatures.TypeTech] = new List<double> { 0.2, 0.2 } },
            Mean = { 0 }, Scale = { 0.1 },
            Classes = { "High Tech", "Speed" },
            Coefficients = { new List<double> { 1 }, new List<double> { -1 } },
            Intercepts = { 0, 0 },
        };
        var profile = new PlayerProfile { TopPpSum = 1000, Style = { [StyleFeatures.TypeTech] = 0.3 } };

        var p = Leaning.Predict(profile, model);

        // x = (0.3 - 0.2 - 0) / 0.1 = 1 -> logits (1, -1)
        Assert.Equal(Math.Exp(1) / (Math.Exp(1) + Math.Exp(-1)), p["High Tech"], 9);
        Assert.Equal(1.0, p.Values.Sum(), 9);
    }

    [Fact]
    public void A_player_exactly_at_the_peer_median_gets_the_intercept_only()
    {
        var model = new LeaningParameters
        {
            Features = { StyleFeatures.TypeTech }, PeerLevelCenters = { 0, 10 },
            PeerMedians = { [StyleFeatures.TypeTech] = new List<double> { 0.1, 0.5 } },
            Mean = { 0 }, Scale = { 1 }, Classes = { "A", "B" },
            Coefficients = { new List<double> { 5 }, new List<double> { -5 } }, Intercepts = { 0, 0 },
        };
        // level = ln(e^5) = 5 -> peer median interpolated = 0.3
        var profile = new PlayerProfile { TopPpSum = Math.Exp(5), Style = { [StyleFeatures.TypeTech] = 0.3 } };
        Assert.Equal(0.5, Leaning.Predict(profile, model)["A"], 9);
    }

    [Fact]
    public void No_leaning_model_gives_no_leaning() =>
        Assert.Empty(Leaning.Predict(new PlayerProfile(), null));

    // ---- Paths --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 4.5, "Speed", true)]
    [InlineData(10, 4.5, "High Tech", false)]
    [InlineData(6, 8, "High Tech", true)]
    [InlineData(6, 8, "Midspeed", false)]
    [InlineData(6, 4, "Midspeed", true)]
    [InlineData(2, 2.5, "Accuracy", true)]
    [InlineData(2, 2.5, "Speed", false)]
    public void Path_relevance_follows_pass_and_tech(double pass, double tech, string path, bool relevant) =>
        Assert.Equal(relevant, PathRules.IsRelevant(path, new MapRatings { Pass = pass, Tech = tech }, Rules));

    [Fact]
    public void Path_state_needs_five_path_plays_and_uses_top_fifteen()
    {
        var catalog = new MapCatalog(Enumerable.Empty<MapEntry>());
        Assert.Null(PathState.Compute("Speed", Rated(4, i => Map(10, 9, 4), 0.9), Rules));

        var plays = Rated(20, i => Map(10, 8 + i * 0.1, 4, predictedAcc: 0.9, ppAt100: 100 + i), 0.92);
        var state = PathState.Compute("Speed", plays, Rules)!;
        Assert.Equal(20, state.PathPlayCount);
        Assert.Equal(0.02, state.AccuracyResidual, 9);
        Assert.True(state.Difficulty >= 9.2, "top-15 by pp are the faster maps here");
        var pp = plays.Select(p => p.Pp).OrderByDescending(x => x).Select((x, i) => x * Math.Pow(0.965, i)).Sum();
        Assert.Equal(pp, state.PathPp, 6);
    }

    // ---- Waypoints, gaps, recommendations ---------------------------------------------------------------------

    private static List<RatedPlay> Rated(int n, Func<int, MapEntry> map, double acc)
    {
        var maps = Enumerable.Range(0, n).Select(map).ToList();
        var catalog = new MapCatalog(maps);
        return maps.Select(m => catalog.Rate(Play(m, acc))!).ToList();
    }

    private static ModelFile SpeedModel(IEnumerable<MapEntry> maps, double pushPrior = 0.5) => new()
    {
        Maps = maps.ToList(),
        Paths =
        {
            new PathModel
            {
                Destination = "Speed", PushPrior = pushPrior, DifficultySpread = 1, AccuracySpread = 0.01,
                Waypoints =
                {
                    new WaypointModel { Index = 0, PathPpLow = 0, Difficulty = 8, AccuracyResidual = -0.08 },
                    new WaypointModel { Index = 1, PathPpLow = 3000, Difficulty = 9, AccuracyResidual = -0.06 },
                    new WaypointModel { Index = 2, PathPpLow = 6000, Difficulty = 10, AccuracyResidual = -0.04 },
                },
            },
        },
    };

    [Fact]
    public void Analyzer_finds_waypoints_gaps_and_recommends_unplayed_maps_in_the_window()
    {
        var played = Enumerable.Range(0, 10).Select(i => Map(10, 8.5, 4, "speed", predictedAcc: 0.95)).ToList();
        var tooEasy = Map(8, 6.5, 4, "speed");
        var inWindow = Map(10, 9.2, 4, "speed");
        var popular = Map(10, 9.0, 4, "speed");
        popular.PeerPopularity["Speed|1"] = 50;
        var model = SpeedModel(played.Concat(new[] { tooEasy, inWindow, popular }));
        var catalog = new MapCatalog(model.Maps);
        var best = played.Select(m => catalog.Rate(Play(m, 0.88))!).ToList();   // resid -0.07, pp 264 each

        var progress = PathAnalyzer.Analyze("Speed", best, model, catalog)!;

        Assert.Equal(0, progress.Current!.Index);
        Assert.Equal(1, progress.Next!.Index);
        var push = progress.Objectives.Single(o => o.Kind == ObjectiveKind.Push);
        var polish = progress.Objectives.Single(o => o.Kind == ObjectiveKind.Polish);
        Assert.Equal(0.5, push.Gap, 9);   // 9 - 8.5
        Assert.Equal(1.0, polish.Gap, 9); // (-0.06 - -0.07) / 0.01
        Assert.Equal(polish, progress.Objectives[0]);
        Assert.Equal(popular.Hash, push.Maps[0].Map.Hash);
        Assert.Contains(push.Maps, r => r.Map.Hash == inWindow.Hash);
        Assert.DoesNotContain(push.Maps, r => r.Map.Hash == tooEasy.Hash);
        Assert.DoesNotContain(push.Maps, r => played.Any(p => p.Hash == r.Map.Hash));
        Assert.All(polish.Maps, r => Assert.True(r.CurrentAccuracy < r.TargetAccuracy));
    }

    [Fact]
    public void Path_prior_can_put_push_first()
    {
        var played = Enumerable.Range(0, 10).Select(i => Map(10, 8.5, 4, "speed", predictedAcc: 0.95)).ToList();
        var model = SpeedModel(played, pushPrior: 0.9);
        var catalog = new MapCatalog(model.Maps);
        var best = played.Select(m => catalog.Rate(Play(m, 0.885))!).ToList(); // resid -0.065: polish gap 0.5, push gap 0.5
        Assert.Equal(ObjectiveKind.Push, PathAnalyzer.Analyze("Speed", best, model, catalog)!.Objectives[0].Kind);
    }

    [Fact]
    public void High_path_pp_from_easier_maps_does_not_place_a_player_past_unreached_difficulty()
    {
        // Lots of pp on pass-6.5 maps (a Midspeed acc player) but Speed waypoints start at difficulty 8.
        var played = Enumerable.Range(0, 40).Select(i => Map(9, 6.5, 4, "midspeed", predictedAcc: 0.97, ppAt100: 600)).ToList();
        var model = SpeedModel(played);
        var catalog = new MapCatalog(model.Maps);
        var best = played.Select(m => catalog.Rate(Play(m, 0.98))!).ToList();

        var progress = PathAnalyzer.Analyze("Speed", best, model, catalog)!;

        Assert.True(progress.State.PathPp > 6000, "path pp alone would say top waypoint");
        Assert.Equal(0, progress.Current!.Index);
        Assert.Equal(0, progress.ProgressToNext); // difficulty 6.5 has not started toward 9 from waypoint 0's 8
        Assert.Equal(ObjectiveKind.Push, progress.Objectives[0].Kind);
    }

    [Fact]
    public void Ungated_paths_place_by_path_pp_only()
    {
        var played = Enumerable.Range(0, 40).Select(i => Map(9, 6.5, 4, "midspeed", predictedAcc: 0.97, ppAt100: 600)).ToList();
        var model = SpeedModel(played);
        model.Paths[0].DifficultyGated = false;
        var catalog = new MapCatalog(model.Maps);
        var best = played.Select(m => catalog.Rate(Play(m, 0.98))!).ToList();
        Assert.Equal(2, PathAnalyzer.Analyze("Speed", best, model, catalog)!.Current!.Index);
    }

    [Fact]
    public void Dimensions_already_at_the_next_waypoint_are_not_objectives()
    {
        // Difficulty 9.2 is already past the next waypoint's 9.0: only polish remains.
        var played = Enumerable.Range(0, 10).Select(i => Map(10, 9.2, 4, "speed", predictedAcc: 0.95)).ToList();
        var model = SpeedModel(played);
        var catalog = new MapCatalog(model.Maps);
        var progress = PathAnalyzer.Analyze("Speed", played.Select(m => catalog.Rate(Play(m, 0.88))!).ToList(), model, catalog)!;
        Assert.Equal(ObjectiveKind.Polish, Assert.Single(progress.Objectives).Kind);
    }

    [Fact]
    public void Above_the_next_waypoint_difficulty_polish_picks_maps_around_it()
    {
        // D-038: difficulty ~10 is past the next waypoint's 9.0: polish on the ~9.0 maps, not the player's harder ones.
        var hard = Enumerable.Range(0, 10).Select(i => Map(11, 10.0, 4, "speed", predictedAcc: 0.95)).ToList();
        var easier = Enumerable.Range(0, 3).Select(i => Map(10, 9.1, 4, "speed", predictedAcc: 0.95)).ToList();
        var model = SpeedModel(hard.Concat(easier));
        var catalog = new MapCatalog(model.Maps);
        var best = hard.Concat(easier).Select(m => catalog.Rate(Play(m, 0.88))!).ToList();
        var progress = PathAnalyzer.Analyze("Speed", best, model, catalog)!;
        Assert.True(progress.State.Difficulty > progress.Next!.Difficulty);

        var polish = progress.Objectives.Single(o => o.Kind == ObjectiveKind.Polish);
        Assert.NotEmpty(polish.Maps);
        Assert.All(polish.Maps, r => Assert.Contains(easier, m => m.Hash == r.Map.Hash));
    }

    [Fact]
    public void Progress_blends_capped_dimensions_and_a_surplus_cannot_hide_a_gap()
    {
        // D-039: difficulty 10 is far past the next waypoint's 9 (capped at 100%), accuracy is behind.
        var played = Enumerable.Range(0, 10).Select(i => Map(11, 10.0, 4, "speed", predictedAcc: 0.95)).ToList();
        var model = SpeedModel(played);
        model.Paths[0].DifficultyGated = false;
        var catalog = new MapCatalog(model.Maps);
        var progress = PathAnalyzer.Analyze("Speed", played.Select(m => catalog.Rate(Play(m, 0.80))!).ToList(), model, catalog)!;

        Assert.Equal(1, progress.DimensionProgress["difficulty"]);
        Assert.Equal(0, progress.DimensionProgress["accuracy"]);
        var dims = progress.DimensionProgress.Values;
        Assert.InRange(progress.ProgressToNext, dims.Min(), dims.Max());
        Assert.True(progress.ProgressToNext < 1, "the accuracy gap keeps the bar below full");
    }

    [Fact]
    public void Diversity_rule_caps_one_map_type()
    {
        var played = Enumerable.Range(0, 10).Select(i => Map(10, 8.5, 4, "speed")).ToList();
        var speedMaps = Enumerable.Range(0, 12).Select(i => Map(10, 9.0, 4, "speed")).ToList();
        var midMaps = Enumerable.Range(0, 3).Select(i => Map(10, 9.0, 4, "midspeed")).ToList();
        foreach (var m in speedMaps) m.PeerPopularity["Speed|1"] = 100; // more popular than the midspeed ones
        var model = SpeedModel(played.Concat(speedMaps).Concat(midMaps));
        var catalog = new MapCatalog(model.Maps);
        var best = played.Select(m => catalog.Rate(Play(m, 0.88))!).ToList();

        var push = PathAnalyzer.Analyze("Speed", best, model, catalog)!.Objectives.Single(o => o.Kind == ObjectiveKind.Push);

        Assert.Equal(PathAnalyzer.MapsPerObjective, push.Maps.Count);
        Assert.Equal(3, push.Maps.Count(r => midMaps.Any(m => m.Hash == r.Map.Hash)));
    }

    [Fact]
    public void Past_the_last_waypoint_there_is_still_a_next_step_with_objectives()
    {
        // D-027: even the best player gets something to work on.
        var played = Enumerable.Range(0, 30).Select(i => Map(12, 10.5, 4, "speed", ppAt100: 500)).ToList();
        var harder = Enumerable.Range(0, 5).Select(i => Map(13, 11.5, 4, "speed")).ToList();
        var model = SpeedModel(played.Concat(harder));
        var catalog = new MapCatalog(model.Maps);
        var progress = PathAnalyzer.Analyze("Speed", played.Select(m => catalog.Rate(Play(m, 0.95))!).ToList(), model, catalog)!;

        Assert.True(progress.BeyondLastWaypoint);
        Assert.Equal(2, progress.Current!.Index);
        Assert.True(progress.Next!.Difficulty > 10.5, "next step is beyond the player's own level");
        Assert.True(progress.Next.AccuracyResidual > -0.04);
        var push = progress.Objectives.Single(o => o.Kind == ObjectiveKind.Push);
        Assert.Contains(push.Maps, r => harder.Any(h => h.Hash == r.Map.Hash));
        Assert.Contains(progress.Objectives, o => o.Kind == ObjectiveKind.Polish);
    }

    // ---- Engine -------------------------------------------------------------------------------------------------

    [Fact]
    public void Fresh_start_still_recommends_easier_maps_for_each_style()
    {
        var easy = Map(9, 7.8, 4, "speed");
        var veryHard = Map(13, 11.5, 4, "speed");
        var tooEasy = Map(3, 4.0, 2, "midspeed"); // more than 2 spreads below the first waypoint
        easy.PeerPopularity["Speed|0"] = 5;
        var engine = new ProgressionEngine(SpeedModel(new[] { easy, veryHard, tooEasy }));

        var report = engine.Analyze(Array.Empty<PlayEvidence>(), new[] { "Speed" });

        Assert.Null(report.Profile);
        var starter = report.Starters["Speed"];
        Assert.Equal(easy.Hash, starter.Maps[0].Map.Hash);
        Assert.DoesNotContain(starter.Maps, m => m.Map.Hash == veryHard.Hash || m.Map.Hash == tooEasy.Hash);
        Assert.NotEmpty(TrainingPlanner.NextUpFromObjectives(new[] { starter }, Array.Empty<PlayRecord>(), new HashSet<SkillProgression.Core.Maps.MapKey>()));
    }

    [Fact]
    public void Fresh_start_ignores_older_evidence()
    {
        var maps = Enumerable.Range(0, 30).Select(i => Map(11, 10, 4.5, "speed", predictedAcc: 0.96)).ToList();
        var engine = new ProgressionEngine(SpeedModel(maps));
        var old = maps.Select(m => new PlayEvidence(Key(m), 0.93, null, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), EvidenceSource.BeatLeader));

        Assert.Equal(Destinations.Speed, engine.Analyze(old).ArrivedDestination);
        var fresh = engine.Analyze(old, since: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Null(fresh.Profile);
        Assert.Equal(Destinations.Unspecialised, fresh.ArrivedDestination);
    }
}
