using HMUI;
using Zenject;

namespace SkillProgression.Plugin.Menu;

/// <summary>
/// Self-contained flow (D-006): presented over the main menu, so the main menu, the BSML mods panel and BeatLeader's
/// noticeboard disappear. The left screen shows the game's own gameplay setup panel (modifiers, player options,
/// environments, colors — user request), set up exactly like solo mode; the right screen shows live platform stats. The game's back arrow next to the title
/// is the only way out (user preference, like other game menus).
/// Levels started from here return to this flow, because the menu scene stays loaded under gameplay.
/// </summary>
internal class SkillProgressionFlowCoordinator : FlowCoordinator
{
    [Inject] private readonly TrainingViewController _trainingViewController = null!;
    [Inject] private readonly PlatformsViewController _platformsViewController = null!;
    [Inject] private readonly ExploreViewController _exploreViewController = null!;
    [Inject] private readonly AchievementsViewController _achievementsViewController = null!;

    private const string Title = "Skill Progression";
    [Inject] private readonly GameplaySetupViewController _gameplaySetupViewController = null!;

    public override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            SetTitle(Title);
            showBackButton = true;
            _trainingViewController.ExploreRequested = OpenExplore;
            _trainingViewController.AchievementsRequested = () => Open(_achievementsViewController, "Achievements");
        }

        if (addedToHierarchy)
        {
            // Same setup as SoloFreePlayFlowCoordinator on 1.40.8; the panel saves changes to player data itself.
            _gameplaySetupViewController.Setup(
                showModifiers: true, showEnvironmentOverrideSettings: true, showColorSchemesSettings: true,
                showMultiplayer: false, PlayerSettingsPanelController.PlayerSettingsPanelLayout.Singleplayer);
            ProvideInitialViewControllers(_trainingViewController, leftScreenViewController: _gameplaySetupViewController,
                rightScreenViewController: _platformsViewController);
        }
    }

    private void OpenExplore() => Open(_exploreViewController, "Explore");

    private void Open(ViewController viewController, string title)
    {
        ReplaceTopViewController(viewController, animationType: ViewController.AnimationType.In);
        SetTitle(title);
    }

    public override void BackButtonWasPressed(ViewController topViewController)
    {
        // Back from Explore / Achievements returns to Training; back from Training leaves the plugin.
        if (topViewController != _trainingViewController)
        {
            ReplaceTopViewController(_trainingViewController, animationType: ViewController.AnimationType.Out);
            SetTitle(Title);
            return;
        }
        _parentFlowCoordinator.DismissFlowCoordinator(this);
    }
}
