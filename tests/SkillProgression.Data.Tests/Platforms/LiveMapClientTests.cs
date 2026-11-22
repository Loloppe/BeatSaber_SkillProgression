using System.Net;
using Newtonsoft.Json.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Scoring;
using SkillProgression.Data.Platforms;

namespace SkillProgression.Data.Tests.Platforms;

public class LiveMapClientTests
{
    // Shapes verified against the live APIs on 2026-11-22 (docs/API_INTEGRATIONS.md).
    private const string Leaderboards = """
        {"song":{"bpm":171,"coverImage":"https://cdn/cover.png"},"leaderboards":[
          {"id":"44c1axxxx91","difficulty":{"difficultyName":"ExpertPlus","modeName":"Standard","status":3,"stars":7.69,
           "passRating":5.31,"accRating":8.85,"techRating":5.08,"njs":18,"noteJumpStartBeatOffset":-0.25}},
          {"id":"44c1axxxx96","difficulty":{"difficultyName":"ExpertPlus","modeName":"Lightshow","status":0}}]}
        """;

    private sealed class StubHandler(Func<string, string?> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            var body = respond(url);
            return Task.FromResult(body == null ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public void Reads_ratings_jump_data_and_leaderboard_of_the_right_mode()
    {
        var info = new LiveMapInfo();
        var id = LiveMapClient.ReadBeatLeader(JToken.Parse(Leaderboards), new MapKey("abc", "Standard", "ExpertPlus"), info);
        Assert.Equal("44c1axxxx91", id);
        Assert.Equal((171, 18, -0.25), (info.Bpm, info.Njs, info.NjsOffset));
        Assert.Equal((7.69, 5.31, 8.85, 5.08), (info.Stars, info.Pass, info.Acc, info.Tech));
        Assert.Equal("https://cdn/cover.png", info.CoverUrl);
    }

    [Fact]
    public void BeatLeader_curve_needs_hasPp()
    {
        var curve = LiveMapClient.ParseBeatLeaderCurve(JToken.Parse("""{"hasPp":true,"points":[[0,0],[0.95,374.53],[1,1064.3]]}"""))!;
        Assert.Equal(374.53, curve.Evaluate(0.95), 6);
        Assert.Null(LiveMapClient.ParseBeatLeaderCurve(JToken.Parse("""{"hasPp":false,"points":[]}""")));
    }

    [Fact]
    public void ScoreSaber_curve_is_sorted_ascending_and_stars_need_ranked()
    {
        var curve = LiveMapClient.ParseScoreSaberCurve(JToken.Parse("""{"curve":[[1,5.37],[0.95,1],[0,0]]}"""))!;
        Assert.Equal(new[] { 0, 0.95, 1 }, curve.Acc);
        Assert.Equal(3.66, LiveMapClient.ParseScoreSaberStars(JToken.Parse("""{"ranked":true,"stars":3.66}""")));
        Assert.Null(LiveMapClient.ParseScoreSaberStars(JToken.Parse("""{"ranked":false,"stars":0}""")));
    }

    [Fact]
    public void AccSaber_curve_and_complexity()
    {
        var curves = new LiveCurves();
        LiveMapClient.ReadAccSaberCurve(JToken.Parse("""
            [{"name":"AccSaber Weight Curve","type":"FORMULA"},
             {"name":"AccSaber Score Curve","scale":61,"shift":-18,"points":[{"x":0.995,"y":0.7},{"x":0.99,"y":0.6}]}]
            """), curves);
        Assert.Equal(new[] { 0.99, 0.995 }, curves.AccSaber!.Acc);
        Assert.Equal((61, -18), (curves.AccSaberScale, curves.AccSaberShift));

        var complexity = LiveMapClient.ParseAccSaberComplexity(JToken.Parse(
            """[{"songHash":"00512e48","difficulty":"EXPERT_PLUS","complexity":8.6}]"""));
        Assert.Equal(8.6, complexity["00512E48|ExpertPlus"]);
    }

    [Fact]
    public async Task Fetches_only_platforms_the_map_is_ranked_on_and_caches()
    {
        var handler = new StubHandler(url =>
            url.Contains("/leaderboards/hash/") ? Leaderboards
            : url.Contains("/pp/curve") ? """{"hasPp":true,"points":[[0,0],[1,1000]]}"""
            : url.Contains("pp-curve") ? """{"curve":[[1,5],[0,0]]}"""
            : url.Contains("/v1/curves") ? """[{"name":"AccSaber Score Curve","scale":61,"shift":-18,"points":[{"x":0,"y":0},{"x":1,"y":1}]}]"""
            : url.Contains("/difficulties/all") ? "[]"
            : null);
        var file = Path.Combine(Path.GetTempPath(), $"live-{Guid.NewGuid():N}.json");
        try
        {
            var client = new LiveMapClient(new HttpClient(handler), file, TimeSpan.Zero);
            var key = new MapKey("abc", "Standard", "ExpertPlus");
            var now = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
            var info = (await client.GetAsync(key, new MapEntry { RankedBeatLeader = true }, now))!;
            Assert.Equal(500, info.BeatLeaderCurve!.Evaluate(0.5), 6);
            Assert.DoesNotContain(handler.Urls, u => u.Contains("scoresaber.com/api/leaderboard"));
            Assert.Contains(handler.Urls, u => u.EndsWith("/pp/curve?leaderboardId=44c1axxxx91&modifiers="));

            var calls = handler.Urls.Count;
            var reloaded = new LiveMapClient(new HttpClient(handler), file, TimeSpan.Zero);
            Assert.NotNull((await reloaded.GetAsync(key, new MapEntry { RankedBeatLeader = true }, now.AddHours(1)))?.BeatLeaderCurve);
            Assert.Equal(calls, handler.Urls.Count); // served from the disk cache
        }
        finally { File.Delete(file); }
    }
}
