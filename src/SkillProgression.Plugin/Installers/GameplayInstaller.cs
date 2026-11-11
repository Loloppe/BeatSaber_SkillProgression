using SkillProgression.Plugin.Gameplay;
using Zenject;

namespace SkillProgression.Plugin.Installers;

/// <summary>Standard (solo) game scene: soft-fail and pause tracking for plugin-started plays.</summary>
internal class GameplayInstaller : Installer
{
    public override void InstallBindings() => Container.BindInterfacesTo<GameplayTracker>().AsSingle();
}
