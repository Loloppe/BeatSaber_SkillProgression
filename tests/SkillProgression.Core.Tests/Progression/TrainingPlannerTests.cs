using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Progression;

namespace SkillProgression.Core.Tests.Progression;

public class TrainingPlannerTests
{
    private static MapRecommendation Rec(string hash, double target, double? current = null) =>
        new() { Map = new MapKey(hash, "Standard", "ExpertPlus"), Name = hash, TargetAccuracy = target, CurrentAccuracy = current };

    private static PathProgress Progress(params Objective[] objectives) => new() { Objectives = objectives.ToList() };

    private static PlayRecord Played(string hash, PlayOutcome outcome, double acc, int minute) => new()
    {
        Hash = hash.ToUpperInvariant(), Difficulty = "ExpertPlus", Outcome = outcome, Accuracy = acc,
        PlayedAtUtc = new DateTime(2026, 11, 10, 12, minute, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Steps_cut_the_waypoint_gap_into_smaller_steps()
    {
        var step = TrainingPlanner.Step(new Objective { Kind = ObjectiveKind.Push, CurrentValue = 8.0, TargetValue = 9.0 });
        Assert.Equal(8.25, step.Target, 9);
        Assert.Equal(4, step.Count);
        Assert.Equal(9.0, step.WaypointTarget);
    }

    [Fact]
    public void Small_gaps_use_a_minimum_step_and_never_overshoot()
    {
        var step = TrainingPlanner.Step(new Objective { Kind = ObjectiveKind.Polish, CurrentValue = -0.010, TargetValue = -0.008 });
        Assert.Equal(-0.008, step.Target, 9);
        Assert.Equal(1, step.Count);
    }

    [Fact]
    public void Next_up_interleaves_objectives_in_priority_order_and_drops_skipped_maps()
    {
        var polish = new Objective { Kind = ObjectiveKind.Polish, Maps = { Rec("p1", 0.98, 0.96), Rec("p2", 0.98, 0.97) } };
        var push = new Objective { Kind = ObjectiveKind.Push, Maps = { Rec("s1", 0.95), Rec("s2", 0.95) } };
        var skipped = new HashSet<MapKey> { new("p2", "Standard", "ExpertPlus") };

        var next = TrainingPlanner.NextUp(Progress(polish, push), Array.Empty<PlayRecord>(), skipped);

        Assert.Equal(new[] { "p1", "s1", "s2" }, next.Select(n => n.Recommendation.Name));
        Assert.Equal(ObjectiveKind.Polish, next[0].Kind);
    }

    [Fact]
    public void Polish_targets_ask_for_a_small_gain_not_the_full_waypoint_target()
    {
        var polish = new Objective { Kind = ObjectiveKind.Polish, Maps = { Rec("p1", 0.98, 0.96) } };
        var item = TrainingPlanner.NextUp(Progress(polish), Array.Empty<PlayRecord>(), new HashSet<MapKey>()).Single();
        Assert.Equal(0.965, item.TargetAccuracy, 9);
    }

    [Fact]
    public void Three_misses_move_a_map_to_the_end_and_a_success_resets_the_count()
    {
        var push = new Objective { Kind = ObjectiveKind.Push, Maps = { Rec("hard", 0.95), Rec("ok", 0.95) } };
        var log = new List<PlayRecord>
        {
            Played("hard", PlayOutcome.Failed, 0.70, 1),
            Played("hard", PlayOutcome.Quit, 0.10, 2),      // quits don't count
            Played("hard", PlayOutcome.Cleared, 0.93, 3),   // below target
            Played("hard", PlayOutcome.Failed, 0.80, 4),
        };

        var next = TrainingPlanner.NextUp(Progress(push), log, new HashSet<MapKey>());
        Assert.Equal(new[] { "ok", "hard" }, next.Select(n => n.Recommendation.Name));
        Assert.True(next[1].Demoted);

        log.Add(Played("hard", PlayOutcome.Cleared, 0.951, 5));
        Assert.Equal(0, TrainingPlanner.RecentMisses(log, new MapKey("hard", "Standard", "ExpertPlus"), 0.95));
    }

    [Fact]
    public void A_no_fail_soft_fail_counts_as_a_miss_even_above_the_target()
    {
        var map = new MapKey("hard", "Standard", "ExpertPlus");
        var soft = new PlayRecord
        {
            Hash = "HARD", Difficulty = "ExpertPlus", Outcome = PlayOutcome.Cleared, Accuracy = 0.96, SoftFailed = true,
            PlayedAtUtc = new DateTime(2026, 11, 10, 12, 1, 0, DateTimeKind.Utc),
        };
        Assert.False(soft.IsPass());
        Assert.Equal(1, TrainingPlanner.RecentMisses(new[] { soft }, map, 0.95));
        Assert.True(Played("hard", PlayOutcome.Cleared, 0.96, 2).IsPass());
    }

    private static PlayRecord Result(int minute, string kind) => new()
    {
        Hash = "M" + minute, Difficulty = "ExpertPlus", PlayedAtUtc = new DateTime(2026, 11, 10, 12, minute, 0, DateTimeKind.Utc),
        Outcome = kind == "fail" ? PlayOutcome.Failed : PlayOutcome.Cleared, SoftFailed = kind == "soft",
        Accuracy = kind == "under" ? 0.90 : 0.96, TargetAccuracy = 0.95,
    };

    [Theory]
    [InlineData("hit", 3)]     // always reaching the target: bigger steps
    [InlineData("under", 4)]   // clears under the target are neutral
    [InlineData("fail", 6)]    // always failing: smaller steps
    [InlineData("soft", 6)]    // No Fail soft fails count as fails
    public void Steps_per_gap_follow_recent_results(string kind, int expected) =>
        Assert.Equal(expected, StepSizer.StepsPerGap(Enumerable.Range(0, 10).Select(i => Result(i, kind))));

    [Fact]
    public void Steps_stay_at_four_with_few_plays_and_only_the_last_twenty_count()
    {
        Assert.Equal(4, StepSizer.StepsPerGap(Enumerable.Range(0, 4).Select(i => Result(i, "fail"))));
        var old = Enumerable.Range(0, 30).Select(i => Result(i, "fail"));
        var recent = Enumerable.Range(30, 20).Select(i => Result(i, "hit"));
        Assert.Equal(3, StepSizer.StepsPerGap(old.Concat(recent)));
    }

    [Fact]
    public void Smaller_steps_lower_the_per_map_accuracy_gain()
    {
        var polish = new Objective { Kind = ObjectiveKind.Polish, CurrentValue = -0.02, TargetValue = 0.0 };
        var rec = new MapRecommendation { TargetAccuracy = 0.99, CurrentAccuracy = 0.95 };
        Assert.Equal(0.955, TrainingPlanner.ShortTermAccuracy(polish, rec), 6);
        Assert.True(TrainingPlanner.ShortTermAccuracy(polish, rec, 6) < 0.955);
        Assert.Equal(0.02 / 6, TrainingPlanner.Step(polish, 6).Target + 0.02, 6);
    }

    [Fact]
    public void No_progress_or_objectives_gives_an_empty_list() =>
        Assert.Empty(TrainingPlanner.NextUp(null, Array.Empty<PlayRecord>(), new HashSet<MapKey>()));
}
