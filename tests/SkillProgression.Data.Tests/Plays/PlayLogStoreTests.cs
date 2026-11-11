using SkillProgression.Core.Plays;
using SkillProgression.Data.Plays;

namespace SkillProgression.Data.Tests.Plays;

public sealed class PlayLogStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-tests-" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(_dir, "nested", "playlog.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static PlayRecord Sample(PlayOutcome outcome = PlayOutcome.Cleared, double acc = 0.9512) => new()
    {
        PlayedAtUtc = new DateTime(2026, 11, 10, 18, 30, 0, DateTimeKind.Utc),
        Hash = "33F7950E5D92996727D514C78D5D30242D371761",
        Difficulty = "ExpertPlus",
        Outcome = outcome,
        Accuracy = acc,
        MultipliedScore = 95120,
        ModifiedScore = 95120,
        MaxMultipliedScore = 100000,
        Misses = 2,
        BadCuts = 1,
        MaxCombo = 120,
        Modifiers = new[] { "FS", "NF" },
        EndSongTime = 22.1,
        SongDuration = 22.26,
    };

    [Fact]
    public void Reading_a_missing_log_returns_nothing()
    {
        var store = new PlayLogStore(LogPath);
        Assert.Empty(store.ReadAll(out var skipped));
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void Appended_records_round_trip_in_order_and_directory_is_created()
    {
        var store = new PlayLogStore(LogPath);
        store.Append(Sample(PlayOutcome.Failed, 0.81));
        store.Append(Sample(PlayOutcome.Cleared, 0.9512));

        var records = store.ReadAll(out var skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(2, records.Count);
        Assert.Equal(PlayOutcome.Failed, records[0].Outcome);
        var last = records[1];
        Assert.Equal(0.9512, last.Accuracy, 6);
        Assert.Equal(new[] { "FS", "NF" }, last.Modifiers);
        Assert.Equal(DateTimeKind.Utc, last.PlayedAtUtc.Kind);
        Assert.Equal(new DateTime(2026, 11, 10, 18, 30, 0, DateTimeKind.Utc), last.PlayedAtUtc);
    }

    [Fact]
    public void Soft_fail_and_pauses_round_trip_and_version_1_records_still_load()
    {
        var store = new PlayLogStore(LogPath);
        store.Append(new PlayRecord { Hash = "A", Outcome = PlayOutcome.Cleared, SoftFailed = true, SoftFailSongTime = 61.5, Pauses = 2 });
        File.AppendAllText(LogPath, "{\"Version\":1,\"Hash\":\"B\",\"Outcome\":\"Cleared\",\"Accuracy\":0.9}\n");

        var all = store.ReadAll(out var skipped);
        Assert.Equal(0, skipped);
        Assert.True(all[0].SoftFailed);
        Assert.Equal(61.5, all[0].SoftFailSongTime);
        Assert.Equal(2, all[0].Pauses);
        Assert.Equal(2, all[0].Version);
        Assert.False(all[1].SoftFailed);
        Assert.Equal(0, all[1].Pauses);
        Assert.True(all[1].IsPass());
    }

    [Fact]
    public void Outcomes_are_stored_as_readable_names()
    {
        var store = new PlayLogStore(LogPath);
        store.Append(Sample(PlayOutcome.Quit));
        Assert.Contains("\"Outcome\":\"Quit\"", File.ReadAllText(LogPath));
    }

    [Fact]
    public void Corrupt_lines_are_skipped_without_losing_the_others()
    {
        var store = new PlayLogStore(LogPath);
        store.Append(Sample());
        File.AppendAllText(LogPath, "{\"Hash\":\"trunc\n");  // e.g. game crashed mid-write
        store.Append(Sample(PlayOutcome.Failed));

        var records = store.ReadAll(out var skipped);

        Assert.Equal(1, skipped);
        Assert.Equal(2, records.Count);
    }
}
