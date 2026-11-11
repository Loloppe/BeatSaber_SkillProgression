using System;
using System.Linq;
using HMUI;
using SiraUtil.Affinity;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;
using SkillProgression.Plugin.Gameplay;
using SkillProgression.Plugin.Services;
using Zenject;
using LevelCategory = SelectLevelCategoryViewController.LevelCategory;

namespace SkillProgression.Plugin.Solo;

/// <summary>
/// Game patches for the Solo "To improve" tab (D-049), verified against 1.40.8's decompiled code:
/// the tab list comes from the prefab's _allLevelCategoryInfos; the enabled categories from SetupBeatmapLevelPacks; content
/// from UpdateSecondChildControllerContent (whose switch throws on unknown categories); Solo results from
/// SoloFreePlayFlowCoordinator.ProcessLevelCompletionResultsAfterLevelDidFinish (our own launches don't go through it).
/// </summary>
internal sealed class ToImprovePatches : IAffinity, IInitializable, IDisposable
{
    [Inject] private readonly ToImproveTab _tab = null!;
    [Inject] private readonly ProgressionService _service = null!;

    private LevelFilteringNavigationController? _filtering;
    /// <summary>Category view widths from the prefab (4 vanilla tabs), per instance (the view is reused).</summary>
    private readonly System.Collections.Generic.Dictionary<SelectLevelCategoryViewController, float> _baseWidths = new();
    /// <summary>Solo shows its results screen after a clear or a fail: rebuilt content waits until it's closed.</summary>
    private bool _resultsOpen;

    public void Initialize() => _tab.PackChanged += HandlePackChanged;

    public void Dispose() => _tab.PackChanged -= HandlePackChanged;

    private void HandlePackChanged()
    {
        if (!_resultsOpen) Reshow();
    }

    /// <summary>Shows the current pack again if our tab is the one selected in the open level selection.</summary>
    private void Reshow()
    {
        var filtering = _filtering;
        if (filtering == null || !filtering.isInViewControllerHierarchy
            || filtering._selectLevelCategoryViewController.selectedLevelCategory != ToImproveTab.Category) return;
        filtering.UpdateSecondChildControllerContent(ToImproveTab.Category);
    }

    [AffinityPrefix]
    [AffinityPatch(typeof(LevelSelectionFlowCoordinator), "DidActivate")]
    private void TrackSolo(LevelSelectionFlowCoordinator __instance, bool addedToHierarchy)
    {
        if (addedToHierarchy) _tab.SoloActive = __instance is SoloFreePlayFlowCoordinator;
    }

    /// <summary>Adds our tab to the prefab's list once (after All, so the vanilla tabs keep their positions).</summary>
    [AffinityPrefix]
    [AffinityPatch(typeof(SelectLevelCategoryViewController), nameof(SelectLevelCategoryViewController.Setup))]
    private void AddCategoryInfo(SelectLevelCategoryViewController __instance)
    {
        if (__instance._allLevelCategoryInfos.Any(i => i.levelCategory == ToImproveTab.Category)) return;
        var info = new SelectLevelCategoryViewController.LevelCategoryInfo
        {
            levelCategory = ToImproveTab.Category, localizedKey = ToImproveTab.Hint, categoryIcon = _tab.Icon,
        };
        __instance._allLevelCategoryInfos = __instance._allLevelCategoryInfos.Append(info).ToArray();
    }

    /// <summary>
    /// After Setup: our tab's hint text (it isn't a localization key), and the category view widened by one tab when ours
    /// is shown. The prefab is sized for 4 tabs: a 5th sticks out under the next panel, which takes its clicks and
    /// overlaps the search bar. The navigation controller lays its views out side by side from their widths, so the
    /// row re-centres (user report, 2026-11-11; layout from 1.40.8's decompiled NavigationController).
    /// </summary>
    [AffinityPostfix]
    [AffinityPatch(typeof(SelectLevelCategoryViewController), nameof(SelectLevelCategoryViewController.Setup))]
    private void AdjustTabs(SelectLevelCategoryViewController __instance)
    {
        try
        {
            var infos = __instance._levelCategoryInfos;
            var control = __instance._levelFilterCategoryIconSegmentedControl;
            var rect = __instance.rectTransform;
            if (!_baseWidths.TryGetValue(__instance, out var baseWidth)) _baseWidths[__instance] = baseWidth = rect.rect.width;
            var index = infos == null ? -1 : Array.FindIndex(infos, i => i.levelCategory == ToImproveTab.Category);
            var extra = 0f;
            if (index >= 0 && index < control.cells.Count)
            {
                if (control.cells[index] is IconSegmentedControlCell cell) cell.hintText = _tab.Title;
                extra = ((UnityEngine.RectTransform)control.cells[index].transform).rect.width;
            }
            rect.SetSizeWithCurrentAnchors(UnityEngine.RectTransform.Axis.Horizontal, baseWidth + extra);
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"Could not adjust the level category tabs: {e.Message}");
        }
    }

    /// <summary>Enables our tab in Solo only, once there is something to show.</summary>
    [AffinityPostfix]
    [AffinityPatch(typeof(LevelFilteringNavigationController), "SetupBeatmapLevelPacks")]
    private void EnableCategory(LevelFilteringNavigationController __instance)
    {
        if (!_tab.SoloActive || _tab.Pack == null || __instance._enabledLevelCategories.Contains(ToImproveTab.Category)) return;
        __instance._enabledLevelCategories = __instance._enabledLevelCategories.Append(ToImproveTab.Category).ToArray();
    }

    /// <summary>
    /// Shows our list for our tab (the game's switch would throw on it). Delivered like Favorites: a filtered list, so no
    /// pack grid (big cover), no pack header and no "Buy" panel, and our order is kept (no alphabetical sort). Same steps
    /// as ShowPacksInSecondChildController, with the grid hidden and the filtered-collection handler.
    /// </summary>
    [AffinityPrefix]
    [AffinityPatch(typeof(LevelFilteringNavigationController), "UpdateSecondChildControllerContent")]
    private bool ShowPack(LevelFilteringNavigationController __instance, LevelCategory levelCategory)
    {
        if (levelCategory != ToImproveTab.Category) return true;
        _filtering = __instance;
        __instance._currentNoDataInfoPrefab = null;
        var pack = _tab.Pack;
        if (pack == null) return false;
        var collections = __instance._annotatedBeatmapLevelCollectionsViewController;
        collections.SetData(new[] { pack }, 0, true);
        __instance.ReplaceSecondViewController(collections);
        __instance.HandleLevelSearchViewControllerDidFilterBeatmapLevelCollection(pack,
            new LevelSelectionOptions { sortAlphabetically = false, isFiltered = true });
        __instance._levelPackIdToBeSelectedAfterPresent = null;
        return false;
    }

    /// <summary>
    /// Songs from our tab open on the difficulty the list recommends (user, 2026-11-11): the detail view is filled with the
    /// player's last-used difficulty / characteristic as defaults (StandardLevelDetailViewController → SetContent, 1.40.8);
    /// swap them for the recommended ones. The player's saved last-used difficulty is untouched.
    /// </summary>
    [AffinityPrefix]
    // SetContent is overloaded (a private parameterless one too): name the public overload's parameters, or the patch
    // fails as ambiguous and SiraUtil's Affinity kernel can't start (breaks the whole menu; seen in-game 2026-11-11).
    [AffinityPatch(typeof(StandardLevelDetailView), nameof(StandardLevelDetailView.SetContent), AffinityMethodType.Normal, null,
        typeof(BeatmapLevel), typeof(BeatmapDifficultyMask), typeof(System.Collections.Generic.HashSet<BeatmapCharacteristicSO>),
        typeof(BeatmapDifficulty), typeof(BeatmapCharacteristicSO), typeof(PlayerData))]
    private void SelectRecommendedDifficulty(BeatmapLevel level, ref BeatmapDifficulty defaultDifficulty,
        ref BeatmapCharacteristicSO defaultBeatmapCharacteristic)
    {
        try
        {
            var filtering = _filtering;
            if (!_tab.SoloActive || filtering == null || !filtering.isInViewControllerHierarchy
                || filtering._selectLevelCategoryViewController.selectedLevelCategory != ToImproveTab.Category) return;
            if (_tab.RecommendedKey(level) is not { } key) return;
            defaultDifficulty = key.difficulty;
            defaultBeatmapCharacteristic = key.beatmapCharacteristic;
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"Could not preselect the recommended difficulty: {e.Message}");
        }
    }

    /// <summary>
    /// BetterSongList saves the last tab and restores it on the next Solo visit; a value the game doesn't know would crash
    /// Solo if our mod were removed. Store "All" instead whenever our tab is selected.
    /// </summary>
    [AffinityPostfix]
    [AffinityPatch(typeof(SelectLevelCategoryViewController), "LevelFilterCategoryIconSegmentedControlDidSelectCell")]
    private void GuardBetterSongList(SelectLevelCategoryViewController __instance)
    {
        if (__instance.selectedLevelCategory != ToImproveTab.Category) return;
        try
        {
            var config = Type.GetType("BetterSongList.Config, BetterSongList")?.GetProperty("Instance")?.GetValue(null);
            config?.GetType().GetProperty("LastCategory")?.SetValue(config, nameof(LevelCategory.All));
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"Could not reset BetterSongList's last category: {e.Message}");
        }
    }

    /// <summary>A Solo clear is kept as a score only (D-049), then the tab is rebuilt with it.</summary>
    [AffinityPostfix]
    [AffinityPatch(typeof(SoloFreePlayFlowCoordinator), "ProcessLevelCompletionResultsAfterLevelDidFinish")]
    private void RecordSoloPlay(LevelCompletionResults levelCompletionResults, IReadonlyBeatmapData transformedBeatmapData,
        BeatmapKey beatmapKey, BeatmapLevel beatmapLevel, GameplayModifiers modifiers, bool practice)
    {
        _resultsOpen = levelCompletionResults.levelEndStateType != LevelCompletionResults.LevelEndStateType.Incomplete;
        try
        {

            if (practice || levelCompletionResults.levelEndStateType != LevelCompletionResults.LevelEndStateType.Cleared
                || modifiers.noFailOn0Energy) return;
            var hash = SongCore.Collections.GetCustomLevelHash(beatmapLevel.levelID);
            var max = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(transformedBeatmapData);
            if (string.IsNullOrEmpty(hash) || max <= 0) return;
            var map = new MapKey(hash, beatmapKey.beatmapCharacteristic.serializedName, beatmapKey.difficulty.ToString());
            _service.AddSoloScore(new PlayEvidence(map, (double)levelCompletionResults.multipliedScore / max,
                LevelLauncher.ModifierCodes(modifiers), DateTime.UtcNow, EvidenceSource.LocalSolo));
            _ = _tab.RebuildAsync(downloadMissing: true);
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"Could not record the Solo play: {e}");
        }
    }

    /// <summary>After the results screen, show the pack (rebuilt meanwhile, or when its downloads finish).</summary>
    [AffinityPostfix]
    [AffinityPatch(typeof(SoloFreePlayFlowCoordinator), "HandleResultsViewControllerContinueButtonPressed")]
    private void RefreshAfterResults()
    {
        _resultsOpen = false;
        try { Reshow(); }
        catch (Exception e) { Plugin.Log.Error($"Could not refresh the To improve tab: {e}"); }
    }
}
