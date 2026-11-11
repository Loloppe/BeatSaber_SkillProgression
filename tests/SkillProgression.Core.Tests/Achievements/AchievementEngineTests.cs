using SkillProgression.Core.Achievements;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using static SkillProgression.Core.Tests.TestMaps;

namespace SkillProgression.Core.Tests.Achievements;

public class AchievementEngineTests
{
    private static readonly DestinationThresholds Rules = new();
    private static readonly DateTime Now = new(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

    private static ProgressSnapshot Snap(int milestone, double stars = 9.2, int weekly = 0, params PlatformRank[] platforms) => new()
    {
        DateUtc = Now.Date,
        Paths = { ["Speed"] = new PathSnapshot { Milestone = milestone, Progress = 0.4, Difficulty = 9, WeeklyMapsAtLevel = weekly } },
        Established = { "Speed" },
        Platforms = platforms.ToList(),
        Records = { [RecordAxes.Stars] = stars, [RecordAxes.Pass] = 7.5, [RecordAxes.Tech] = 3.1, [RecordAxes.Acc] = 11 },
    };

    private static List<Achievement> Detect(ProgressSnapshot? prev, ProgressSnapshot cur, ISet<string>? unlocked = null) =>
        AchievementEngine.Detect(prev, cur, unlocked ?? new HashSet<string>(), Now);

    [Fact]
    public void First_run_records_everything_already_earned_quietly()
    {
        var found = Detect(null, Snap(3));
        Assert.Contains(found, a => a.Id == "milestone|Speed|2");
        Assert.Contains(found, a => a.Id == "milestone|Speed|3");
        Assert.DoesNotContain(found, a => a.Id == "milestone|Speed|1");
        Assert.Contains(found, a => a.Id == "best|stars|9");
        Assert.All(found, a => Assert.True(a.Retroactive));
    }

    [Fact]
    public void Later_unlocks_are_new_and_nothing_unlocks_twice()
    {
        var unlocked = new HashSet<string>(Detect(null, Snap(3)).Select(a => a.Id));
        var found = Detect(Snap(3), Snap(4), unlocked);
        var milestone = Assert.Single(found, a => a.Kind == AchievementKind.Milestone);
        Assert.Equal("milestone|Speed|4", milestone.Id);
        Assert.False(milestone.Retroactive);
        Assert.DoesNotContain(found, a => unlocked.Contains(a.Id));
    }

    [Fact]
    public void Records_are_style_free_one_point_tiers_of_each_rating()
    {
        var found = Detect(null, Snap(1, stars: 9.7));
        Assert.Contains(found, a => a.Id == "best|stars|9");
        Assert.DoesNotContain(found, a => a.Id == "best|stars|10");
        Assert.Contains(found, a => a.Id == "best|pass|7");
        Assert.Contains(found, a => a.Id == "best|tech|3");
        Assert.Contains(found, a => a.Id == "best|acc|11");
    }

    [Fact]
    public void Records_first_computed_on_an_older_progress_file_are_quiet()
    {
        var old = Snap(1);
        old.Records.Clear();
        Assert.All(Detect(old, Snap(1), new HashSet<string>()).Where(a => a.Kind == AchievementKind.PersonalRecord),
            a => Assert.True(a.Retroactive));
        Assert.False(Assert.Single(Detect(Snap(1, stars: 9.2), Snap(1, stars: 10.1), new HashSet<string>(Detect(null, Snap(1)).Select(a => a.Id))),
            a => a.Kind == AchievementKind.PersonalRecord).Retroactive);
    }

    [Fact]
    public void Consistency_unlocks_every_tier_reached()
    {
        var found = Detect(Snap(3), Snap(3, weekly: 12));
        Assert.Contains(found, a => a.Id == "consistency|Speed|5");
        Assert.Contains(found, a => a.Id == "consistency|Speed|10");
        Assert.DoesNotContain(found, a => a.Id == "consistency|Speed|15");
    }

    [Fact]
    public void Consistency_needs_five_different_maps_not_five_plays_of_one()
    {
        var maps = Enumerable.Range(0, 5).Select(_ => Map(10, 9.5, 4, "speed")).ToList();
        var catalog = new MapCatalog(maps);
        RatedPlay Played(MapEntry m) => catalog.Rate(new PlayEvidence(Key(m), 0.93, null, Now.AddDays(-1), EvidenceSource.LocalPlayLog))!;

        var sameMap = Enumerable.Range(0, 5).Select(_ => Played(maps[0])).ToList();
        Assert.Equal(1, AchievementEngine.WeeklyMapsAtLevel("Speed", 9, sameMap, Rules, Now));

        var distinct = maps.Select(Played).ToList();
        Assert.Equal(5, AchievementEngine.WeeklyMapsAtLevel("Speed", 9, distinct, Rules, Now));
    }

    [Fact]
    public void Best_accuracy_is_kept_per_star_level()
    {
        var maps = new[] { Map(8.2, 7, 3), Map(8.9, 8, 3), Map(10.1, 9, 4) };
        var catalog = new MapCatalog(maps);
        var plays = new[] { (maps[0], 0.953), (maps[1], 0.961), (maps[2], 0.92) }
            .Select(x => catalog.Rate(new PlayEvidence(Key(x.Item1), x.Item2, null, Now, EvidenceSource.BeatLeader))!).ToList();
        var table = AchievementEngine.BestAccByStar(plays);
        Assert.Equal(new[] { 8, 10 }, table.Keys.OrderBy(k => k));
        Assert.Equal(0.961, table[8]);
        Assert.Equal(0.92, table[10]);
    }

    [Fact]
    public void Platform_thresholds_unlock_for_every_threshold_crossed()
    {
        var found = Detect(null, Snap(1, platforms: new PlatformRank { Platform = "BeatLeader", Rank = 152 }));
        Assert.Contains(found, a => a.Id == "platform|BeatLeader|250");
        Assert.Contains(found, a => a.Id == "platform|BeatLeader|10000");
        Assert.DoesNotContain(found, a => a.Id == "platform|BeatLeader|100");
    }

    [Fact]
    public void A_platform_seen_for_the_first_time_is_recorded_quietly_then_new_thresholds_are_celebrated()
    {
        var bl = (int rank) => new PlatformRank { Platform = "BeatLeader", Rank = rank };
        var first = Detect(Snap(1), Snap(1, platforms: bl(260)));
        Assert.All(first.Where(a => a.Kind == AchievementKind.Platform), a => Assert.True(a.Retroactive));

        var unlocked = new HashSet<string>(first.Select(a => a.Id));
        var crossed = Detect(Snap(1, platforms: bl(260)), Snap(1, platforms: bl(240)), unlocked);
        Assert.False(Assert.Single(crossed, a => a.Kind == AchievementKind.Platform).Retroactive);
    }

    [Fact]
    public void Pp_and_ap_thresholds_unlock()
    {
        var bl = new PlatformRank { Platform = "BeatLeader", Rank = 152, Points = 17577, Unit = "pp" };
        var acc = new PlatformRank { Platform = "AccSaber", Category = "Standard Acc", Rank = 281, Points = 9066, Unit = "AP" };
        var found = Detect(null, Snap(1, platforms: new[] { bl, acc }));
        Assert.Contains(found, a => a.Id == "points|BeatLeader|17500");
        Assert.DoesNotContain(found, a => a.Id == "points|BeatLeader|18000");
        Assert.DoesNotContain(found, a => a.Id == "points|BeatLeader|5000"); // tiers start at 8,000 pp
        Assert.Contains(found, a => a.Id == "points|AccSaber Standard Acc|9000"); // category tiers, not overall

    }
}
