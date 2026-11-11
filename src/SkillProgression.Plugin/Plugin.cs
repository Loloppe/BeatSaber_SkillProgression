using IPA;
using IPA.Logging;
using SiraUtil.Zenject;
using SkillProgression.Plugin.Installers;

namespace SkillProgression.Plugin;

[Plugin(RuntimeOptions.SingleStartInit)]
[NoEnableDisable]
// ReSharper disable once UnusedType.Global
public class Plugin
{
    internal static Logger Log { get; private set; } = null!;

    [Init]
    public Plugin(Logger logger, Zenjector zenjector)
    {
        Log = logger;
        zenjector.UseLogger(logger);
        zenjector.Install<AppInstaller>(Location.App);
        zenjector.Install<MenuInstaller>(Location.Menu);
        zenjector.Install<GameplayInstaller>(Location.StandardPlayer);
    }
}
