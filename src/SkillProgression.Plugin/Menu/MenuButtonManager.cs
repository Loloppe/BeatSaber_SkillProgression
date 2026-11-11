using System;
using BeatSaberMarkupLanguage.MenuButtons;
using UnityEngine;
using Zenject;

namespace SkillProgression.Plugin.Menu;

/// <summary>Main-menu entry point (BSML mods panel) that opens the self-contained plugin flow.</summary>
internal class MenuButtonManager : MonoBehaviour
{
    [Inject] private readonly MainFlowCoordinator _mainFlowCoordinator = null!;
    [Inject] private readonly SkillProgressionFlowCoordinator _flowCoordinator = null!;

    private static event Action? MenuButtonClicked;

    private static readonly MenuButton menuButton = new(
        "Skill Progression", "Track and train your Beat Saber skills",
        () => MenuButtonClicked?.Invoke());

    // On game exit the menu is torn down: unregistering then makes BSML rebuild its panel from destroyed templates
    // (BSMLParserException "Instantiate failed" in the log, 2026-11-10). Nothing to clean up when quitting anyway.
    private static bool _quitting;

    static MenuButtonManager() => Application.quitting += () => _quitting = true;

    private void OnEnable()
    {
        MenuButtons.Instance.RegisterButton(menuButton);
        MenuButtonClicked += HandleMenuButtonClicked;
    }

    private void OnDisable()
    {
        MenuButtonClicked -= HandleMenuButtonClicked;
        if (!_quitting) MenuButtons.Instance.UnregisterButton(menuButton);
    }

    private void HandleMenuButtonClicked() => _mainFlowCoordinator.PresentFlowCoordinator(_flowCoordinator);
}
