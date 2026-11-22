using Newtonsoft.Json.Linq;
using SkillProgression.Core.Plays;
using SkillProgression.Data.ScoreSaber;

namespace SkillProgression.Data.Tests.Platforms;

public class ScoreSaberScoresTests
{
    // Shape of /api/player/{id}/scores items (verified 2026-11-22), trimmed.
    private static JObject Item(bool ranked = true, int difficulty = 9, string mode = "SoloStandard", string mods = "BE,SF") => JObject.Parse($$"""
        {
          "score": { "baseScore": 2083546, "modifiedScore": 2083546, "pp": 820.55, "modifiers": "{{mods}}", "timeSet": "2026-07-29T03:36:09.886Z" },
          "leaderboard": { "songHash": "3f0d9251db8d9b38114a161d7b4015eb7bde633c", "maxScore": 2131755, "ranked": {{(ranked ? "true" : "false")}},
                           "difficulty": { "difficulty": {{difficulty}}, "gameMode": "{{mode}}" } }
        }
        """);

    [Fact]
    public void Parses_the_unmodified_accuracy_map_and_modifiers()
    {
        var e = ScoreSaberClient.ParseScore(Item())!;
        Assert.Equal("3F0D9251DB8D9B38114A161D7B4015EB7BDE633C", e.Map.Hash.ToUpperInvariant());
        Assert.Equal("Standard", e.Map.Characteristic);
        Assert.Equal("ExpertPlus", e.Map.Difficulty);
        Assert.Equal(2083546.0 / 2131755, e.Accuracy, 9);
        Assert.Equal(new[] { "BE", "SF" }, e.Modifiers);
        Assert.Equal(new DateTime(2026, 7, 29, 3, 36, 9, 886, DateTimeKind.Utc), e.TimeUtc);
        Assert.Equal(EvidenceSource.ScoreSaber, e.Source);
    }

    [Theory]
    [InlineData(false, 9, "SoloStandard")] // unranked
    [InlineData(true, 4, "SoloStandard")]  // unknown difficulty
    [InlineData(true, 9, "Party")]         // not a solo mode
    public void Skips_scores_that_cannot_count(bool ranked, int difficulty, string mode) =>
        Assert.Null(ScoreSaberClient.ParseScore(Item(ranked, difficulty, mode)));

    [Fact]
    public void Other_characteristics_keep_their_name() =>
        Assert.Equal("OneSaber", ScoreSaberClient.ParseScore(Item(mode: "SoloOneSaber"))!.Map.Characteristic);
}
