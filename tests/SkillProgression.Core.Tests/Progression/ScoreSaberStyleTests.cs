using SkillProgression.Core.Achievements;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class ScoreSaberStyleTests
{
    private static readonly DestinationThresholds Rules = new();
    private static readonly string SsSpeed = ScoreSaberStyles.Id(Destinations.Speed);

    private static ScoreSaberModel Model() => new()
    {
        Curve = Line(1), // multiplier = accuracy
        ModifierValues = { ["NF"] = -0.5, ["NO"] = -0.05, ["FS"] = 0.08 },
    };

    private static MapEntry SsMap(double ssStars, double pass = 10, double tech = 4, bool rankedScoreSaber = true)
    {
        var m = Map(9, pass, tech, "speed", sf: new MapRatings { Stars = 11, Pass = pass + 3, Tech = tech, PredictedAcc = 0.93 });
        m.RankedScoreSaber = rankedScoreSaber;
        m.ScoreSaberStars = rankedScoreSaber ? ssStars : 0;
        return m;
    }

    [Fact]
    public void Pp_ignores_positive_modifiers_and_applies_negative_ones()
    {
        var m = Model();
        Assert.Equal(10 * 42.117 * 0.95, m.Pp(10, 0.95, Array.Empty<string>()), 9);
        Assert.Equal(10 * 42.117 * 0.95, m.Pp(10, 0.95, new[] { "FS" }), 9);
        Assert.Equal(10 * 42.117 * 0.95 * 0.5, m.Pp(10, 0.95, new[] { "NF" }), 9);
        Assert.Equal(10 * 42.117 * 0.95 * 0.45, m.Pp(10, 0.95, new[] { "NF", "NO", "FS" }), 9);
    }

    [Fact]
    public void Ss_styles_use_scoresaber_maps_no_modifier_ratings_and_ss_pp()
    {
        var map = SsMap(8);
        var blOnly = SsMap(8, rankedScoreSaber: false);
        var catalog = new MapCatalog(new[] { map, blOnly }, scoreSaber: Model()) { Pool = MapPool.BeatLeader };
        var sf = catalog.Rate(Play(map, 0.95, "SF"))!;

        Assert.Equal(13, sf.Ratings.Pass);                         // BeatLeader styles keep the effective ratings
        Assert.Equal(10, PathRules.Axis(SsSpeed, sf));             // ScoreSaber styles rate at no modifier
        Assert.Equal(8 * 42.117 * 0.95, PathRules.Value(SsSpeed, sf), 9);
        Assert.True(PathRules.IsRelevant(SsSpeed, sf, Rules));
        Assert.False(PathRules.IsRelevant(SsSpeed, blOnly, blOnly.Ratings[""], Rules));
        Assert.Equal(new[] { map }, catalog.Recommendable(SsSpeed)); // the ScoreSaber pool, whatever the catalog's
    }

    [Fact]
    public void Ss_style_state_sums_ss_pp_with_the_ss_decay()
    {
        var maps = Enumerable.Range(0, 6).Select(i => SsMap(6 + i)).ToList();
        var catalog = new MapCatalog(maps, scoreSaber: Model());
        var plays = maps.Select(m => catalog.Rate(Play(m, 0.95))!).ToList();

        var state = PathState.Compute(SsSpeed, plays, Rules)!;

        var expected = plays.Select(p => p.SsPp).OrderByDescending(x => x).Select((pp, i) => pp * Math.Pow(0.965, i)).Sum();
        Assert.Equal(expected, state.PathPp, 6);
        Assert.Equal(10, state.Difficulty);
    }

    [Fact]
    public void Ss_styles_read_their_beatleader_styles_peer_targets()
    {
        var maps = Enumerable.Range(0, 12).Select(_ => SsMap(8)).ToList();
        foreach (var m in maps) m.PeerTargets["Speed"] = new PeerCurve { Ease = 0.95, Levels = { -0.01, 0.01 }, Values = { 0.94, 0.96 } };
        var model = new ModelFile { PeerTargets = new PeerTargetsModel { MinPlayerMaps = 10 }, Maps = maps };
        var catalog = new MapCatalog(maps, scoreSaber: Model());
        var best = maps.Take(11).Select(m => catalog.Rate(Play(m, 0.955))!).ToList();

        var peers = PeerTargets.For(best, model);

        Assert.True(peers.Covers(SsSpeed));
        Assert.Equal(peers.Target(Destinations.Speed, maps[11]), peers.Target(SsSpeed, maps[11]));
    }

    [Fact]
    public void Accuracy_gated_paths_stop_at_the_first_waypoint_whose_accuracy_is_not_reached()
    {
        var ssAcc = ScoreSaberStyles.Id(Destinations.Accuracy);
        var maps = Enumerable.Range(0, 6).Select(_ => SsMap(3, pass: 2, tech: 2)).ToList(); // predicted acc 0.95
        var catalog = new MapCatalog(maps, scoreSaber: Model());
        var best = maps.Select(m => catalog.Rate(Play(m, 0.955))!).ToList();             // +0.5% vs predicted
        var path = new PathModel
        {
            Destination = ssAcc, DifficultyGated = false, AccuracyGated = true, DifficultySpread = 1, AccuracySpread = 0.01,
            Waypoints =
            {
                new WaypointModel { Index = 0, PathPpLow = 0, AccuracyResidual = -0.01 },
                new WaypointModel { Index = 1, PathPpLow = 1, AccuracyResidual = 0.0 },
                new WaypointModel { Index = 2, PathPpLow = 2, AccuracyResidual = 0.02 },  // path pp reached, accuracy not
                new WaypointModel { Index = 3, PathPpLow = 3, AccuracyResidual = 0.015 }, // smoothed up to 0.02
            },
        };
        var model = new ModelFile { ScoreSaber = Model(), Maps = maps, Paths = { path } };

        var progress = PathAnalyzer.Analyze(ssAcc, best, model, catalog)!;
        Assert.Equal(1, progress.Current!.Index);
        Assert.True(progress.ProgressToNext <= progress.DimensionProgress["accuracy"] + 1e-9);

        path.AccuracyGated = false;
        Assert.Equal(3, PathAnalyzer.Analyze(ssAcc, best, model, catalog)!.Current!.Index);
    }

    [Fact]
    public void Ss_styles_have_their_own_names_and_quiet_first_achievements()
    {
        Assert.Equal("Speed (ScoreSaber)", PathNames.Display(SsSpeed));
        var now = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
        ProgressSnapshot Snap(bool ss) => new()
        {
            DateUtc = now.Date,
            Paths = ss
                ? new Dictionary<string, PathSnapshot> { ["Speed"] = new() { Milestone = 3 }, [SsSpeed] = new() { Milestone = 3 } }
                : new Dictionary<string, PathSnapshot> { ["Speed"] = new() { Milestone = 3 } },
        };
        var unlocked = new HashSet<string> { "milestone|Speed|2", "milestone|Speed|3" };

        var found = AchievementEngine.Detect(Snap(false), Snap(true), unlocked, now);

        Assert.Equal(new[] { $"milestone|{SsSpeed}|2", $"milestone|{SsSpeed}|3" }, found.Select(a => a.Id));
        Assert.All(found, a => Assert.True(a.Retroactive));
        Assert.Equal("Speed (ScoreSaber) milestone 2 reached", found[0].Title);
    }
}
