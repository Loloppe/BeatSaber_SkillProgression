using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class CalibrationTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static CalibrationPick Pick(string hash, int minute, double target = 0.95) =>
        new() { Map = new MapKey(hash, "Standard", "ExpertPlus"), Axis = 9, TargetAccuracy = target, PickedUtc = T0.AddMinutes(minute) };

    private static PlayRecord Played(string hash, int minute, double acc, PlayOutcome outcome = PlayOutcome.Cleared, bool soft = false) => new()
    {
        Hash = hash.ToUpperInvariant(), Difficulty = "ExpertPlus", Outcome = outcome, Accuracy = acc, SoftFailed = soft,
        PlayedAtUtc = T0.AddMinutes(minute),
    };

    [Fact]
    public void Starts_below_the_old_level_and_waits_for_the_first_result()
    {
        var status = Calibrator.Status(10, new[] { Pick("a", 0) }, Array.Empty<PlayRecord>());
        Assert.False(status.Done);
        Assert.Equal(1, status.Step);
        Assert.Equal(8.5, status.Level, 6);
        Assert.Equal("A", status.Current!.Map.Hash);
        Assert.Null(status.Last);
    }

    [Fact]
    public void Steps_up_on_success_down_on_a_miss_and_halves_at_each_turn()
    {
        var picks = new[] { Pick("a", 0), Pick("b", 10), Pick("c", 20) };
        var log = new[]
        {
            Played("a", 5, 0.96),                          // success: 8.5 -> 9.5
            Played("b", 15, 0.80, PlayOutcome.Failed),     // miss: step halves to 0.5 -> 9.0
            Played("c", 25, 0.94),                         // under target, same direction: 9.0 -> 8.5
        };
        var status = Calibrator.Status(10, picks, log);
        Assert.Equal(8.5, status.Level, 6);
        Assert.Equal(4, status.Step);
        Assert.Null(status.Current); // a new pick is needed
        Assert.False(status.Last!.Success);
        Assert.False(status.Last.Failed);
    }

    [Fact]
    public void A_no_fail_soft_fail_or_a_play_before_the_pick_is_not_a_success()
    {
        var log = new[] { Played("a", -5, 0.99), Played("a", 5, 0.99, soft: true) };
        var result = Assert.Single(Calibrator.Results(new[] { Pick("a", 0) }, log));
        Assert.False(result.Success);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Finishes_after_five_maps_and_never_estimates_above_the_old_level()
    {
        var picks = Enumerable.Range(0, 5).Select(i => Pick($"m{i}", i * 10)).ToList();
        var log = picks.Select((p, i) => Played($"m{i}", i * 10 + 5, 0.99)).ToList();
        var status = Calibrator.Status(10, picks, log);
        Assert.True(status.Done);
        Assert.Equal(5, status.Step);
        Assert.True(status.Level > 10);
        Assert.Equal(10, status.Estimate); // recovery ceiling
    }

    [Fact]
    public void Picks_the_closest_map_to_the_level_and_skips_excluded_ones()
    {
        var maps = new[] { Map(10, 8.0, 4, "speed"), Map(10, 8.6, 4, "speed"), Map(10, 9.5, 4, "speed") };
        var model = new ModelFile
        {
            Maps = maps.ToList(),
            Paths = { new PathModel { Destination = "Speed", Waypoints = { new WaypointModel { Index = 0, Difficulty = 8.5, AccuracyResidual = -0.02 } } } },
        };
        var catalog = new MapCatalog(model.Maps);

        var pick = Calibrator.Pick("Speed", 8.5, model, catalog, new HashSet<MapKey>(), T0)!;
        Assert.Equal(Key(maps[1]), pick.Map);
        Assert.Equal(maps[1].Ratings[""].PredictedAcc - 0.02, pick.TargetAccuracy, 6);

        var next = Calibrator.Pick("Speed", 8.5, model, catalog, new HashSet<MapKey> { Key(maps[1]) }, T0)!;
        Assert.Equal(Key(maps[0]), next.Map);
    }
}
