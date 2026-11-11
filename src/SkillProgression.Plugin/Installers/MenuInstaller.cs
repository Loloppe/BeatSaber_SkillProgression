using SkillProgression.Plugin.Gameplay;
using SkillProgression.Plugin.Menu;
using Zenject;

namespace SkillProgression.Plugin.Installers;

/// <summary>Menu-lifetime bindings: menu button, our flow (D-006, D-024, D-029) and level launching.</summary>
internal class MenuInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<LevelLauncher>().AsSingle();
        Container.Bind<PlatformsViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<TrainingViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<ExploreViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<AchievementsViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<SkillProgressionFlowCoordinator>().FromNewComponentOnNewGameObject().AsSingle();
        Container.Bind<MenuButtonManager>().FromNewComponentOnNewGameObject().AsSingle().NonLazy();
    }
}
