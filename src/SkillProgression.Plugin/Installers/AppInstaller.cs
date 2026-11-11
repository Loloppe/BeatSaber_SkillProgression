using System;
using System.IO;
using System.Net.Http;
using IPA.Utilities;
using SkillProgression.Data.BeatLeader;
using SkillProgression.Data.Platforms;
using SkillProgression.Data.Plays;
using SkillProgression.Plugin.Gameplay;
using SkillProgression.Plugin.Services;
using Zenject;

namespace SkillProgression.Plugin.Installers;

/// <summary>App-lifetime services: storage, web clients and the progression service (docs/ARCHITECTURE.md).</summary>
internal class AppInstaller : Installer
{
    public override void InstallBindings()
    {
        var dataDir = Path.Combine(UnityGame.UserDataPath, "SkillProgression");
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SkillProgression/{typeof(AppInstaller).Assembly.GetName().Version}");
        var playLog = new PlayLogStore(Path.Combine(dataDir, "playlog.jsonl"));

        Container.Bind<PlayLogStore>().FromInstance(playLog).AsSingle();
        Container.Bind<PlaySession>().AsSingle();
        Container.Bind<MapDownloader>().FromInstance(new MapDownloader(http)).AsSingle();
        Container.Bind<ProgressionService>().FromInstance(new ProgressionService(
            dataDir, playLog, new BeatLeaderClient(http), new PlatformStatsClient(http))).AsSingle();
    }
}
