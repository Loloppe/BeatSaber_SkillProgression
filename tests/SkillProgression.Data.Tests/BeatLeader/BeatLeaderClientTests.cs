using System.Net;
using Newtonsoft.Json.Linq;
using SkillProgression.Data.BeatLeader;

namespace SkillProgression.Data.Tests.BeatLeader;

public class BeatLeaderClientTests
{
    // Shape verified against the live API on 2026-11-10 (docs/API_INTEGRATIONS.md).
    private static string Score(string hash, double acc, string mods = "", long timepost = 1748482550) => new JObject
    {
        ["accuracy"] = acc, ["modifiers"] = mods, ["timeset"] = timepost.ToString(), ["timepost"] = timepost, ["pp"] = 293.8,
        ["leaderboard"] = new JObject
        {
            ["song"] = new JObject { ["hash"] = hash },
            ["difficulty"] = new JObject { ["difficultyName"] = "ExpertPlus", ["modeName"] = "Standard", ["status"] = 3 },
        },
    }.ToString();

    private static string Page(int total, params string[] scores) =>
        $$"""{"metadata":{"itemsPerPage":100,"page":1,"total":{{total}}},"data":[{{string.Join(",", scores)}}]}""";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Synchronous IProgress (Progress&lt;T&gt; posts asynchronously).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public void Parses_a_score_into_evidence()
    {
        var e = BeatLeaderClient.ParseScore(JToken.Parse(Score("abc123", 0.9023, "FS,NF")))!;
        Assert.Equal("ABC123", e.Map.Hash);
        Assert.Equal("ExpertPlus", e.Map.Difficulty);
        Assert.Equal(0.9023, e.Accuracy, 6);
        Assert.Equal(new[] { "FS", "NF" }, e.Modifiers);
        Assert.Equal("FS", e.SpeedModifier);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1748482550).UtcDateTime, e.TimeUtc);
    }

    [Fact]
    public void Score_without_map_identity_is_skipped() =>
        Assert.Null(BeatLeaderClient.ParseScore(JToken.Parse("""{"accuracy":0.9,"leaderboard":{}}""")));

    [Fact]
    public async Task Pages_until_total_and_requests_ranked_scores()
    {
        var first = Enumerable.Range(0, 100).Select(i => Score($"h{i}", 0.9)).ToArray();
        var handler = new StubHandler(r => Json(r.RequestUri!.Query.Contains("page=1") ? Page(130, first) : Page(130, Score("last", 0.8))));
        var client = new BeatLeaderClient(new HttpClient(handler), delayBetweenPages: TimeSpan.Zero);

        var reported = new List<double>();
        var scores = await client.GetRankedScoresAsync("12345", progress: new SyncProgress(reported.Add));

        Assert.Equal(101, scores.Count);
        Assert.Equal(new[] { 100.0 / 130, 1.0 }, reported);
        Assert.Equal(2, handler.Urls.Count);
        Assert.All(handler.Urls, u => Assert.Contains("type=ranked", u));
    }

    [Fact]
    public async Task Incremental_sync_passes_time_from()
    {
        var handler = new StubHandler(_ => Json(Page(0)));
        var client = new BeatLeaderClient(new HttpClient(handler), delayBetweenPages: TimeSpan.Zero);

        await client.GetRankedScoresAsync("1", since: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Contains("time_from=1767225600", handler.Urls.Single());
    }

    [Fact]
    public async Task Unranked_scores_query_unranked_maps()
    {
        var handler = new StubHandler(_ => Json(Page(0)));
        var client = new BeatLeaderClient(new HttpClient(handler), delayBetweenPages: TimeSpan.Zero);

        await client.GetScoresAsync("1", ScoreType.Unranked);

        Assert.Contains("type=unranked", handler.Urls.Single());
    }

    [Fact]
    public async Task Unknown_player_raises_a_clear_error()
    {
        var client = new BeatLeaderClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        await Assert.ThrowsAsync<BeatLeaderException>(() => client.GetRankedScoresAsync("nope"));
    }
}
