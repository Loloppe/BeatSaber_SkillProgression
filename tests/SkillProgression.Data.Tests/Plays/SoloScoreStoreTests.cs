using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;
using SkillProgression.Data.Plays;

namespace SkillProgression.Data.Tests.Plays;

public sealed class SoloScoreStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "nested", "solo-scores.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static readonly MapKey MapA = new("33F7950E5D92996727D514C78D5D30242D371761", "Standard", "ExpertPlus");
    private static readonly MapKey MapB = new("A1B2C3D4E5F60718293A4B5C6D7E8F9012345678", "Standard", "Expert");

    private static PlayEvidence Play(MapKey map, double acc, int day) =>
        new(map, acc, new[] { "FS" }, new DateTime(2026, 11, day, 12, 0, 0, DateTimeKind.Utc), EvidenceSource.LocalSolo);

    [Fact]
    public void Keeps_the_best_accuracy_per_map_and_survives_a_reload()
    {
        var store = new SoloScoreStore(FilePath);
        store.Add(Play(MapA, 0.95, 1));
        store.Add(Play(MapA, 0.93, 2)); // worse: ignored
        store.Add(Play(MapB, 0.90, 3));
        store.Add(Play(MapA, 0.97, 4)); // better: replaces

        var all = new SoloScoreStore(FilePath).All();

        Assert.Equal(2, all.Count);
        var a = Assert.Single(all, p => p.Map.Equals(MapA));
        Assert.Equal(0.97, a.Accuracy);
        Assert.Equal(new[] { "FS" }, a.Modifiers);
        Assert.Equal(EvidenceSource.LocalSolo, a.Source);
    }

    [Fact]
    public void Missing_or_unreadable_file_means_no_scores()
    {
        Assert.Empty(new SoloScoreStore(FilePath).All());
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");
        Assert.Empty(new SoloScoreStore(FilePath).All());
    }
}
