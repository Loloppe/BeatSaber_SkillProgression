using SkillProgression.Core.Achievements;
using SkillProgression.Data.State;

namespace SkillProgression.Data.Tests.State;

public sealed class ProgressStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-progress-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "progress.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Achievements_snapshots_and_rank_history_survive_a_reload()
    {
        var store = new ProgressStore(FilePath);
        var day = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        store.Add(new[] { new Achievement { Id = "milestone|Speed|2", Kind = AchievementKind.Milestone, Title = "x", UnlockedUtc = day } });
        store.Add(new[] { new Achievement { Id = "milestone|Speed|2", Kind = AchievementKind.Milestone, Title = "dup" } });
        store.PutSnapshot(new ProgressSnapshot { DateUtc = day, Paths = { ["Speed"] = new PathSnapshot { Milestone = 2 } } });
        store.PutSnapshot(new ProgressSnapshot { DateUtc = day.AddHours(5), Paths = { ["Speed"] = new PathSnapshot { Milestone = 3 } } });
        store.MergeRankHistory("BeatLeader", new[] { new RankPoint { DateUtc = day, Rank = 160 }, new RankPoint { DateUtc = day.AddDays(-1), Rank = 170 } });
        store.MergeRankHistory("BeatLeader", new[] { new RankPoint { DateUtc = day, Rank = 152 } });
        store.MergeRankHistory("ScoreSaber", new[] { new RankPoint { DateUtc = day, Rank = 400 } });
        store.Save();

        var loaded = new ProgressStore(FilePath);
        Assert.Single(loaded.Achievements);
        Assert.Equal(AchievementKind.Milestone, loaded.Achievements[0].Kind);
        Assert.Equal(3, Assert.Single(loaded.Snapshots).Paths["Speed"].Milestone); // one snapshot per day, latest wins
        Assert.Equal(new[] { 170, 152 }, loaded.RankHistory("BeatLeader").Select(p => p.Rank));
        Assert.Equal(400, Assert.Single(loaded.RankHistory("ScoreSaber")).Rank);
        Assert.Empty(loaded.RankHistory("AccSaber"));
        Assert.Contains("milestone|Speed|2", loaded.UnlockedIds());
    }

    [Fact]
    public void Reset_clears_everything()
    {
        var store = new ProgressStore(FilePath);
        store.Add(new[] { new Achievement { Id = "a" } });
        store.Reset();
        store.Save();
        Assert.Empty(new ProgressStore(FilePath).Achievements);
    }
}
