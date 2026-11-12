using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Progression;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Progression;

public class CalibrationTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    // Old level 10, bottom of the pool 2: range 8, first step 1.
    private const double Old = 10, Bottom = 2;

    private static CalibrationPick Pick(string hash, int minute, double axis = 9, double target = 0.95) =>
        new() { Map = new MapKey(hash, "Standard", "ExpertPlus"), Axis = axis, TargetAccuracy = target, PickedUtc = T0.AddMinutes(minute) };

    private static PlayRecord Played(string hash, int minute, double acc, PlayOutcome outcome = PlayOutcome.Cleared, bool soft = false) => new()
    {
        Hash = hash.ToUpperInvariant(), Difficulty = "ExpertPlus", Outcome = outcome, Accuracy = acc, SoftFailed = soft,
        PlayedAtUtc = T0.AddMinutes(minute),
    };

    /// <summary>One pick per result (first at the bottom), each played 5 minutes after being picked.</summary>
    private static CalibrationStatus Run(params double[] accs)
    {
        var picks = accs.Select((_, i) => Pick($"m{i}", i * 10, axis: i == 0 ? Bottom : 9)).ToList();
        var log = accs.Select((a, i) => Played($"m{i}", i * 10 + 5, a, a < 0.5 ? PlayOutcome.Failed : PlayOutcome.Cleared)).ToList();
        return Calibrator.Status(Old, Bottom, picks, log);
    }

    [Fact]
    public void Starts_at_the_bottom_of_the_pool_and_waits_for_the_first_result()
    {
        var status = Calibrator.Status(Old, Bottom, new[] { Pick("a", 0, axis: Bottom) }, Array.Empty<PlayRecord>());
        Assert.False(status.Done);
        Assert.Equal(1, status.Step);
        Assert.Equal(Bottom, status.Level, 6);
        Assert.Equal("A", status.Current!.Map.Hash);
        Assert.Null(status.Last);
    }

    [Fact]
    public void The_first_pick_fixes_the_bottom_so_a_pool_change_does_not_move_the_range()
    {
        var status = Calibrator.Status(Old, 4, new[] { Pick("a", 0, axis: Bottom) }, Array.Empty<PlayRecord>());
        Assert.Equal(Bottom, status.Level, 6);
    }

    [Fact]
    public void Steps_grow_while_every_map_succeeds_then_halve_at_each_turn()
    {
        Assert.Equal(3, Run(0.96).Level, 6);                    // 2 -> 3 (1/8 of the range)
        Assert.Equal(5, Run(0.96, 0.96).Level, 6);              // -> 5 (step doubles)
        Assert.Equal(9, Run(0.96, 0.96, 0.96).Level, 6);        // -> 9
        var turn = Run(0.96, 0.96, 0.96, 0.10);                 // fail at 9: step 4 halves to 2 -> 7
        Assert.Equal(7, turn.Level, 6);
        Assert.False(turn.Done);
        Assert.True(turn.Last!.Failed);
        var under = Run(0.96, 0.96, 0.96, 0.10, 0.94);          // under target at 7, same direction: -> 5
        Assert.Equal(5, under.Level, 6);
        Assert.False(under.Last!.Success);
        Assert.False(under.Last.Failed);
        Assert.Null(under.Current); // a new pick is needed
        Assert.Equal(6, under.Step);
    }

    [Fact]
    public void Stops_once_the_step_is_small_after_a_miss()
    {
        var status = Run(0.96, 0.10); // 2 -> 3, fail at 3: step 0.5 (< 10% of the range) -> 2.5
        Assert.True(status.Done);
        Assert.Equal(2.5, status.Estimate, 6);
    }

    [Fact]
    public void A_miss_at_the_bottom_ends_it_at_the_bottom()
    {
        var status = Run(0.10);
        Assert.True(status.Done);
        Assert.Equal(Bottom, status.Estimate, 6);
    }

    [Fact]
    public void A_success_at_the_old_level_ends_it_there_and_never_estimates_above()
    {
        var climbing = Run(0.99, 0.99, 0.99, 0.99); // 3, 5, 9, then capped at 10
        Assert.False(climbing.Done);
        Assert.Equal(Old, climbing.Level, 6);
        var top = Run(0.99, 0.99, 0.99, 0.99, 0.99);
        Assert.True(top.Done);
        Assert.Equal(Old, top.Estimate); // recovery ceiling
    }

    [Fact]
    public void Finishes_after_the_maximum_number_of_maps()
    {
        // 3, 5, 9 then misses all the way down (step stays 2): 7, 5, 3, still large enough to go on.
        Assert.False(Run(0.99, 0.99, 0.99, 0.10, 0.10, 0.10).Done);
        var status = Run(0.99, 0.99, 0.99, 0.10, 0.10, 0.10, 0.10);
        Assert.True(status.Done);
        Assert.Equal(Calibrator.MaxMaps, status.Step);
        Assert.Equal(Bottom, status.Estimate, 6);
    }

    [Fact]
    public void Nothing_to_calibrate_when_the_old_level_is_at_the_bottom()
    {
        var status = Calibrator.Status(2, 3, new List<CalibrationPick>(), Array.Empty<PlayRecord>());
        Assert.True(status.Done);
        Assert.Equal(3, status.Estimate, 6);
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
    public void Bottom_is_the_easiest_recommendable_map_of_the_path()
    {
        var maps = new[] { Map(10, 8.0, 4, "speed"), Map(10, 6.5, 4, "speed"), Map(10, 9.5, 4, "speed") };
        var model = new ModelFile { Maps = maps.ToList() };
        var catalog = new MapCatalog(model.Maps);
        Assert.Equal(PathRules.Axis("Speed", maps[1].Ratings[""]), Calibrator.Bottom("Speed", model, catalog));
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
