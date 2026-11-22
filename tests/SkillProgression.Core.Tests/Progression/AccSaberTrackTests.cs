using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class AccSaberTrackTests
{
    private static readonly DestinationThresholds Rules = new();

    private static MapEntry AccMap(string category, double complexity, bool rankedBeatLeader = false)
    {
        var m = Map(4, 2.5, 3, "acc");
        m.RankedBeatLeader = rankedBeatLeader;
        m.RankedAccSaber = true;
        m.AccSaberCategory = category;
        m.AccSaberComplexity = complexity;
        return m;
    }

    private static AccSaberModel Linear() => new() { Curve = Line(1) }; // curve(acc) = acc

    [Fact]
    public void Weights_match_accsabers_live_weighted_ap()
    {
        // weightedAp / ap of a live true_acc top player (2026-11-22, API_INTEGRATIONS.md).
        var m = new AccSaberModel();
        Assert.Equal(1.0, m.Weight(0), 9);
        Assert.Equal(0.9891204, m.Weight(1), 6);
        Assert.Equal(0.2706272, m.Weight(12), 6);
        Assert.Equal(0.1, m.Weight(15), 9);
        Assert.Equal(0.0030195, m.Weight(24), 6);
    }

    [Fact]
    public void Ap_is_curve_times_shifted_complexity_times_scale() =>
        Assert.Equal(0.99 * (8 + 18) * 61, Linear().Ap(8, 0.99), 9);

    [Fact]
    public void A_track_counts_its_category_only_and_measures_complexity()
    {
        var trueAcc = AccMap("true_acc", 3.5);
        var tech = AccMap("tech_acc", 10.5);
        var blOnly = Map(4, 2.5, 3, "acc");

        Assert.True(PathRules.IsRelevant(AccSaberTracks.TrueAcc, trueAcc, trueAcc.Ratings[""], Rules));
        Assert.False(PathRules.IsRelevant(AccSaberTracks.TrueAcc, tech, tech.Ratings[""], Rules));
        Assert.False(PathRules.IsRelevant(AccSaberTracks.TrueAcc, blOnly, blOnly.Ratings[""], Rules));
        Assert.Equal(10.5, PathRules.Axis(AccSaberTracks.TechAcc, tech, tech.Ratings[""]));
        // Styles keep their rules on the same maps.
        Assert.Equal(4, PathRules.Axis(Destinations.Accuracy, tech, tech.Ratings[""]));
    }

    [Fact]
    public void Tracks_recommend_accsaber_maps_whatever_the_pool()
    {
        var trueAcc = AccMap("true_acc", 3.5);
        var standard = AccMap("standard_acc", 7, rankedBeatLeader: true);
        var catalog = new MapCatalog(new[] { trueAcc, standard }) { Pool = MapPool.BeatLeader };

        Assert.Equal(new[] { trueAcc, standard }, catalog.Recommendable(AccSaberTracks.TrueAcc));
        Assert.True(PathRules.IsRecommendable(catalog, trueAcc, trueAcc.Ratings[""], AccSaberTracks.TrueAcc, Rules));
        Assert.False(PathRules.IsRecommendable(catalog, standard, standard.Ratings[""], AccSaberTracks.TrueAcc, Rules));
    }

    [Fact]
    public void Track_state_sums_ap_with_accsaber_weights_over_plays_without_banned_modifiers()
    {
        var accSaber = Linear();
        var maps = Enumerable.Range(0, 6).Select(i => AccMap("true_acc", 2 + i)).ToList();
        var slowerMap = AccMap("true_acc", 9);
        var catalog = new MapCatalog(maps.Append(slowerMap), accSaber);
        var plays = maps.Select(m => catalog.Rate(Play(m, 0.99))!).ToList();
        var slower = catalog.Rate(Play(slowerMap, 0.99, "SS"))!; // banned by AccSaber: no AP

        var state = PathState.Compute(AccSaberTracks.TrueAcc, plays.Append(slower).ToList(), Rules, accSaber)!;

        Assert.Equal(0, slower.Ap);
        var expected = plays.Select(p => p.Ap).OrderByDescending(x => x).Select((ap, i) => ap * accSaber.Weight(i)).Sum();
        Assert.Equal(expected, state.PathPp, 6);
        Assert.Equal(6, state.PathPlayCount);
        Assert.InRange(state.Difficulty, 2, 7);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(true, "SF")]
    [InlineData(true, "FS", "PM")]
    [InlineData(true, "BE", "IF")]
    [InlineData(false, "NO")]
    [InlineData(false, "NB")]
    [InlineData(false, "SC")]
    [InlineData(false, "FS", "SS")]
    public void Accsaber_bans_no_obstacles_no_bombs_slower_song_and_small_notes(bool counts, params string[] mods)
    {
        var map = AccMap("true_acc", 3);
        var rated = new MapCatalog(new[] { map }, Linear()).Rate(Play(map, 0.99, mods.Where(m => m.Length > 0).ToArray()))!;
        Assert.Equal(counts, AccSaberTracks.Counts(rated.Evidence));
        Assert.Equal(counts, rated.Ap > 0);
    }

    [Fact]
    public void Tracks_aim_at_accuracy_and_new_maps_at_level_never_at_clearing_harder_maps()
    {
        var accSaber = Linear();
        var played = Enumerable.Range(0, 6).Select(i => AccMap("true_acc", 3 + i * 0.1)).ToList();
        var fresh = Enumerable.Range(0, 6).Select(i => AccMap("true_acc", 3 + i * 0.1)).ToList();
        var harder = Enumerable.Range(0, 6).Select(i => AccMap("true_acc", 6)).ToList();
        var model = new ModelFile
        {
            AccSaber = accSaber,
            Maps = played.Concat(fresh).Concat(harder).ToList(),
            Paths =
            {
                new PathModel
                {
                    Destination = AccSaberTracks.TrueAcc, DifficultyGated = false, DifficultySpread = 0.5, AccuracySpread = 0.01,
                    Waypoints =
                    {
                        new WaypointModel { Index = 0, PathPpLow = 0, Difficulty = 3, AccuracyResidual = 0.0 },
                        // Much harder and more accurate: a style would push toward complexity 6.
                        new WaypointModel { Index = 1, PathPpLow = 1e6, Difficulty = 6, AccuracyResidual = 0.03 },
                    },
                },
            },
        };
        var catalog = new MapCatalog(model.Maps, accSaber) { Pool = MapPool.AccSaber };
        var best = played.Select(m => catalog.Rate(Play(m, 0.96))!).ToList();

        var progress = PathAnalyzer.Analyze(AccSaberTracks.TrueAcc, best, model, catalog)!;

        Assert.DoesNotContain(progress.Objectives, o => o.Kind == ObjectiveKind.Push && !o.AtLevel);
        var newMaps = Assert.Single(progress.Objectives, o => o.Kind == ObjectiveKind.Push);
        Assert.All(newMaps.Maps, m => Assert.InRange(m.AxisRating, 2.5, 3.6)); // around the player's own complexity
        Assert.Contains(progress.Objectives, o => o.Kind == ObjectiveKind.Polish);
        // The bar weighs AP (step 1) and accuracy (step 0.03 / 0.01 = 3, squared) only; complexity is reported, not counted.
        var d = progress.DimensionProgress;
        Assert.True(d["difficulty"] > 0);
        Assert.Equal((d["pp"] + 9 * d["accuracy"]) / 10, progress.ProgressToNext, 9);
    }

    [Theory]
    [InlineData(1000)]  // new best
    [InlineData(450)]   // in the middle
    [InlineData(10)]    // at the bottom
    public void Gain_is_the_change_in_category_ap(double ap)
    {
        var m = new AccSaberModel();
        var scores = Enumerable.Range(0, 30).Select(i => 900.0 - i * 25).ToList();
        Assert.Equal(m.Total(scores.Append(ap)) - m.Total(scores), m.Gain(scores, ap), 6);
    }

    [Fact]
    public void Track_new_maps_come_by_ap_gain_not_popularity()
    {
        var accSaber = Linear();
        var played = Enumerable.Range(0, 6).Select(i => AccMap("true_acc", 3)).ToList();
        var popular = AccMap("true_acc", 2.8);
        popular.PeerPopularity["True Acc|1"] = 500;
        var valuable = AccMap("true_acc", 3.4); // more complexity at the same target: more AP
        var model = new ModelFile
        {
            AccSaber = accSaber,
            Maps = played.Append(popular).Append(valuable).ToList(),
            Paths =
            {
                new PathModel
                {
                    Destination = AccSaberTracks.TrueAcc, DifficultyGated = false, DifficultySpread = 0.5, AccuracySpread = 0.01,
                    Waypoints =
                    {
                        new WaypointModel { Index = 0, PathPpLow = 0, Difficulty = 3 },
                        new WaypointModel { Index = 1, PathPpLow = 1e6, Difficulty = 3, AccuracyResidual = 0.02 },
                    },
                },
            },
        };
        var catalog = new MapCatalog(model.Maps, accSaber) { Pool = MapPool.AccSaber };
        var best = played.Select(m => catalog.Rate(Play(m, 0.96))!).ToList();

        var newMaps = PathAnalyzer.Analyze(AccSaberTracks.TrueAcc, best, model, catalog)!.Objectives.Single(o => o.Kind == ObjectiveKind.Push);
        Assert.Equal(new[] { Key(valuable), Key(popular) }, newMaps.Maps.Select(r => r.Map));

        var explored = Explorer.Suggest(AccSaberTracks.TrueAcc, ExploreMode.NewMaps, 0, best, model, catalog, seed: 1, randomPick: false);
        Assert.Equal(new[] { Key(valuable), Key(popular) }, explored.Items.Select(i => i.Map));
    }

    [Fact]
    public void Without_an_accsaber_model_tracks_have_no_state()
    {
        var maps = Enumerable.Range(0, 6).Select(i => AccMap("true_acc", 2 + i)).ToList();
        var catalog = new MapCatalog(maps);
        var plays = maps.Select(m => catalog.Rate(Play(m, 0.99))!).ToList();
        Assert.Null(PathState.Compute(AccSaberTracks.TrueAcc, plays, Rules));
    }
}
