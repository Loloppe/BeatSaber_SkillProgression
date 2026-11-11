using SkillProgression.Core.Achievements;

namespace SkillProgression.Core.Tests.Achievements;

public class AchievementBoardTests
{
    private static readonly DateTime Day = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static ProgressSnapshot Snap() => new()
    {
        DateUtc = Day,
        Paths =
        {
            ["Speed"] = new PathSnapshot { Milestone = 3, Progress = 0.4, WeeklyMapsAtLevel = 7 },
            ["Accuracy"] = new PathSnapshot { Milestone = 2, Progress = 0.9 },
        },
        Platforms = { new PlatformRank { Platform = "BeatLeader", Rank = 152, Points = 17577, Unit = "pp" } },
        Records = { [RecordAxes.Stars] = 9.47, [RecordAxes.Pass] = 7.2, [RecordAxes.Tech] = 3.1, [RecordAxes.Acc] = 11 },
    };

    private static readonly Dictionary<string, int> Counts = new() { ["Speed"] = 5, ["Accuracy"] = 4 };

    private static List<Achievement> Unlocked(params string[] ids) =>
        ids.Select(id => new Achievement { Id = id, Title = id, UnlockedUtc = Day }).ToList();

    private static List<BoardEntry> Build(AchievementCategory c, string style, params string[] unlocked) =>
        AchievementBoard.Build(c, style, Snap(), Unlocked(unlocked), Counts);

    [Fact]
    public void Milestones_list_only_the_selected_style_unlocked_ones_then_the_next_one()
    {
        var list = Build(AchievementCategory.Milestones, "Speed", "milestone|Speed|2", "milestone|Speed|3");
        Assert.Equal(new[] { "milestone|Speed|2", "milestone|Speed|3", "milestone|Speed|4" }, list.Select(e => e.Id));
        Assert.True(list.Single(e => e.Id == "milestone|Speed|3").Unlocked);
        Assert.Equal(0.4, list.Single(e => e.Id == "milestone|Speed|4").Progress);
    }

    [Fact]
    public void Consistency_shows_the_next_tier_with_progress_from_the_previous_tier()
    {
        var list = Build(AchievementCategory.Consistency, "Speed", "consistency|Speed|5");
        Assert.Equal(new[] { 5, 10 }, list.Select(e => int.Parse(e.Id.Split('|')[2])));
        Assert.Equal(0.4, list.Single(e => e.Id == "consistency|Speed|10").Progress!.Value, 6); // 7 maps: 2 of the 5 from 5 to 10
    }

    [Fact]
    public void Records_are_style_free_show_the_next_tier_and_have_no_progress()
    {
        var stars = Enumerable.Range(1, 9).Select(n => $"best|stars|{n}").ToArray();
        var speed = Build(AchievementCategory.PersonalRecords, "Speed", stars);
        Assert.Equal(speed.Select(e => e.Id), Build(AchievementCategory.PersonalRecords, "Accuracy", stars).Select(e => e.Id));
        Assert.Equal(9 + 1 + 3, speed.Count); // 9 unlocked + next star tier, + next pass / tech / acc tier
        Assert.DoesNotContain(speed, e => e.Id == "best|stars|11");
        Assert.Contains(speed, e => e.Id == "best|stars|10");
        Assert.Contains(speed, e => e.Id == "best|pass|8"); // pass 7.2: next tier not cleared yet
        Assert.Contains(speed, e => e.Id == "best|acc|12");
        Assert.All(speed, e => Assert.Null(e.Progress)); // a clear is done or not: no progress bar
    }

    [Fact]
    public void Platform_lists_contain_unlocked_and_locked_thresholds_and_progress_is_capped()
    {
        var points = Build(AchievementCategory.PlatformPoints, "Speed", "points|BeatLeader|17500");
        Assert.True(points.Single(e => e.Id == "points|BeatLeader|17500").Unlocked);
        Assert.InRange(points.Single(e => e.Id == "points|BeatLeader|18000").Progress!.Value, 0.15, 0.16);
        var ranks = Build(AchievementCategory.PlatformRanks, "Speed");
        Assert.Equal("platform|BeatLeader|100", Assert.Single(ranks).Id); // rank 152: top 100 next, top 50 hidden
        Assert.All(ranks.Where(e => e.Progress.HasValue), e => Assert.True(e.Progress < 1));
    }

    [Fact]
    public void Almost_there_takes_the_closest_locked_ones_of_the_selected_style_and_style_free_categories()
    {
        var list = Build(AchievementCategory.AlmostThere, "Speed");
        Assert.NotEmpty(list);
        Assert.All(list, e => Assert.False(e.Unlocked));
        Assert.DoesNotContain(list, e => e.Id.Contains("Accuracy"));
        Assert.Equal(list.OrderByDescending(e => e.Progress).Select(e => e.Id), list.Select(e => e.Id));
        Assert.All(list, e => Assert.True(e.Progress < 1));
    }
}
