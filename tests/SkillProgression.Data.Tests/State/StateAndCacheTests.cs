using System.Net;
using Newtonsoft.Json.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Data.BeatLeader;
using SkillProgression.Data.Platforms;
using SkillProgression.Data.State;

namespace SkillProgression.Data.Tests.State;

public sealed class StateAndCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sp-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void User_state_round_trips_and_starts_unset()
    {
        var store = new UserStateStore(Path.Combine(_dir, "state.json"));
        Assert.Equal(StartMode.Unset, store.Load().StartMode);

        var state = new UserState { PlayerId = "1", StartMode = StartMode.Fresh, FreshStartUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                                    Paths = { "Speed", "Accuracy" }, FocusPath = "Speed" };
        state.Skip(new MapKey("abc", "Standard", "ExpertPlus"));
        state.Skip(new MapKey("ABC", "Standard", "ExpertPlus"));
        store.Save(state);
        store.Save(state); // replace path

        var loaded = store.Load();
        Assert.Equal(StartMode.Fresh, loaded.StartMode);
        Assert.Equal("Speed", loaded.FocusPath);
        Assert.Single(loaded.Skipped);
        Assert.Contains(new MapKey("abc", "Standard", "ExpertPlus"), loaded.SkippedKeys());
    }

    [Fact]
    public void Map_pool_and_calibration_picks_round_trip()
    {
        var store = new UserStateStore(Path.Combine(_dir, "state.json"));
        var state = new UserState { MapPool = SkillProgression.Core.Model.MapPool.ScoreSaber };
        var pick = new SkillProgression.Core.Progression.CalibrationPick
        {
            Map = new MapKey("abc", "Standard", "Expert"), Axis = 6.2, TargetAccuracy = 0.96, PickedUtc = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
        };
        state.AddCalibrationPick("Midspeed", pick);
        store.Save(state);

        var loaded = store.Load();
        Assert.Equal(SkillProgression.Core.Model.MapPool.ScoreSaber, loaded.MapPool);
        var back = Assert.Single(loaded.CalibrationPicks("Midspeed"));
        Assert.Equal(pick.Map, back.Map);
        Assert.Equal(0.96, back.TargetAccuracy);
        Assert.Empty(loaded.CalibrationPicks("Speed"));
    }

    [Fact]
    public void Version_1_state_moves_to_the_any_ranked_pool()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, """{"Version":1,"MapPool":"BeatLeader","FocusPath":"Speed"}""");
        var loaded = new UserStateStore(path).Load();
        Assert.Equal(SkillProgression.Core.Model.MapPool.AnyRanked, loaded.MapPool);
        Assert.Equal(UserState.CurrentVersion, loaded.Version);
        Assert.Equal("Speed", loaded.FocusPath);
        Assert.Equal(SkillProgression.Core.Model.MapPool.AnyRanked, new UserState().MapPool);
    }

    [Fact]
    public void Version_2_state_restarts_calibration_but_keeps_it_skipped()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, """
            {"Version":2,"Calibrations":{
              "Speed":{"Picks":[{"Map":"ABC/Standard/Expert","Axis":8.5,"TargetAccuracy":0.95,"PickedUtc":"2026-11-10T00:00:00Z"}]},
              "Tech":{"Skipped":true}}}
            """);
        var loaded = new UserStateStore(path).Load();
        Assert.Equal(3, loaded.Version);
        Assert.Empty(loaded.CalibrationPicks("Speed"));
        Assert.True(loaded.Calibrations["Tech"].Skipped);
    }

    [Fact]
    public void Corrupt_state_starts_clean_and_keeps_the_file_aside()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(StartMode.Unset, new UserStateStore(path).Load().StartMode);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    private sealed class Stub(Func<string, string> body) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Urls.Add(r.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body(r.RequestUri!.ToString())) });
        }
    }

    private static string Page(params (string hash, double acc, long time)[] scores) => new JObject
    {
        ["metadata"] = new JObject { ["total"] = scores.Length },
        ["data"] = new JArray(scores.Select(s => new JObject
        {
            ["accuracy"] = s.acc, ["modifiers"] = "", ["timepost"] = s.time,
            ["leaderboard"] = new JObject { ["song"] = new JObject { ["hash"] = s.hash },
                                            ["difficulty"] = new JObject { ["difficultyName"] = "ExpertPlus", ["modeName"] = "Standard" } },
        })),
    }.ToString();

    [Fact]
    public async Task Score_cache_syncs_incrementally_and_newer_pbs_replace_older_ones()
    {
        var call = 0;
        var stub = new Stub(_ => ++call == 1 ? Page(("a", 0.90, 1000), ("b", 0.80, 1000)) : Page(("a", 0.95, 2000)));
        var client = new BeatLeaderClient(new HttpClient(stub), delayBetweenPages: TimeSpan.Zero);
        var cache = new ScoreCache(Path.Combine(_dir, "scores.json"));
        var now = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(2, (await cache.SyncAsync(client, "p1", now)).Count);
        var second = await cache.SyncAsync(client, "p1", now.AddDays(1));

        Assert.DoesNotContain("time_from", stub.Urls[0]);
        Assert.Contains("time_from", stub.Urls[1]);
        Assert.Equal(2, second.Count);
        Assert.Equal(0.95, second.Single(e => e.Map.Hash == "A").Accuracy);
        Assert.Equal(2, cache.Cached().Count);
    }

    [Fact]
    public async Task Another_player_resets_the_cache()
    {
        var stub = new Stub(_ => Page(("a", 0.9, 1000)));
        var client = new BeatLeaderClient(new HttpClient(stub), delayBetweenPages: TimeSpan.Zero);
        var cache = new ScoreCache(Path.Combine(_dir, "scores.json"));
        await cache.SyncAsync(client, "p1", DateTime.UtcNow);
        await cache.SyncAsync(client, "p2", DateTime.UtcNow);
        Assert.DoesNotContain("time_from", stub.Urls[1]);
    }

    [Fact]
    public void Parses_platform_stats_including_accsaber_categories()
    {
        var bl = PlatformStatsClient.ParseRankPp("BeatLeader", JObject.Parse("""{"rank":152,"countryRank":13,"pp":17577.48}"""));
        Assert.Equal(152, bl.Rank);
        Assert.Equal(17577.48, bl.Points);

        var acc = PlatformStatsClient.ParseAccSaber(JObject.Parse("""
            {"categories":[
              {"ap":9066.1,"categoryId":"b0000000-0000-0000-0000-000000000002","countryRanking":22,"ranking":281},
              {"ap":30000,"categoryId":"b0000000-0000-0000-0000-000000000005","countryRanking":5,"ranking":120}]}
            """));
        Assert.Equal(new[] { "Overall", "Standard Acc" }, acc.Select(a => a.Category));
        Assert.All(acc, a => Assert.Equal("AP", a.Unit));
    }

    [Fact]
    public void Parses_scoresaber_and_accsaber_histories()
    {
        var ss = PlatformStatsClient.ParseHistory(JArray.Parse("""[{"rank":351,"totalPP":14031.5,"createdAt":"2026-04-07T09:05:04.189Z"},{"rank":400,"totalPP":14000,"createdAt":"2026-11-11T00:00:00Z"}]"""),
            "createdAt", "rank", "totalPP");
        Assert.Equal(new[] { 351, 400 }, ss.Select(p => p.Rank));
        Assert.Equal(new DateTime(2026, 4, 7), ss[0].DateUtc);
        var acc = PlatformStatsClient.ParseHistory(JArray.Parse("""[{"ranking":244,"recordedAt":"2026-11-02T00:05:00Z"},{"ranking":0,"recordedAt":"2026-11-03T00:05:00Z"}]"""),
            "recordedAt", "ranking", null);
        Assert.Equal(244, Assert.Single(acc).Rank);
    }

    [Fact]
    public async Task A_failing_platform_does_not_hide_the_others()
    {
        var stub = new Stub(url => url.Contains("scoresaber") ? throw new HttpRequestException("down")
            : url.Contains("accsaber") ? """{"categories":[]}""" : """{"rank":1,"pp":2}""");
        var stats = await new PlatformStatsClient(new HttpClient(stub)).GetAllAsync("p");
        Assert.Equal("BeatLeader", Assert.Single(stats).Platform);
    }
}
