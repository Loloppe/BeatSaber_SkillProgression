using SkillProgression.Plugin.Gameplay;
using SkillProgression.Plugin.Menu;
using SkillProgression.Plugin.Solo;
using Zenject;

namespace SkillProgression.Plugin.Installers;

/// <summary>Menu-lifetime bindings: menu button, our flow (D-006, D-024, D-029) and level launching.</summary>
internal class MenuInstaller : Installer
{
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<LevelLauncher>().AsSingle();
        // Solo menu "To improve" tab (D-049): menu-scoped, so the patches go away with the menu.
        Container.BindInterfacesAndSelfTo<ToImproveTab>().AsSingle();
        Container.BindInterfacesTo<ToImprovePatches>().AsSingle();
        Container.Bind<PlatformsViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<TrainingViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<ExploreViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<AchievementsViewController>().FromNewComponentAsViewController().AsSingle();
        Container.Bind<SkillProgressionFlowCoordinator>().FromNewComponentOnNewGameObject().AsSingle();
        Container.Bind<MenuButtonManager>().FromNewComponentOnNewGameObject().AsSingle().NonLazy();
    }
}
