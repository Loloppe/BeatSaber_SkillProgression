using System;
using System.Linq;
using HMUI;
using SiraUtil.Affinity;
using SiraUtil.Services;
using SkillProgression.Core.Maps;
using SkillProgression.Data.Plays;
using SkillProgression.Plugin.Gameplay;
using SkillProgression.Plugin.Menu;
using Zenject;

namespace SkillProgression.Plugin.Solo;

/// <summary>
/// "To Solo" (D-057, user request after player feedback): hands one recommended map to the game's Solo menu, so mods that
/// work there (e.g. JDFixer's per-map preferences) apply. While the handoff lasts (until the plugin is opened again):
/// Solo opens on that map's difficulty, its plays there are recorded in the play log as plugin plays with the target shown
/// (option A), and Solo's back arrow returns to the plugin instead of the main menu (BetterSongSearch's route on 1.40.8).
/// </summary>
internal sealed class ToSoloHandoff : IAffinity, IInitializable, IDisposable
{
    [Inject] private readonly PlaySession _session = null!;
    [Inject] private readonly PlayLogStore _playLog = null!;
    [Inject] private readonly ILevelFinisher _levelFinisher = null!;
    [Inject] private readonly LevelLauncher _launcher = null!;
    [Inject] private readonly ToImproveTab _tab = null!;
    [Inject] private readonly MainFlowCoordinator _mainFlowCoordinator = null!;
    // Lazy: the flow coordinator's view controllers inject this class.
    [Inject] private readonly LazyInject<SkillProgressionFlowCoordinator> _flowCoordinator = null!;

    public void Initialize() => _levelFinisher.StandardLevelDidFinish += HandleLevelFinished;

    public void Dispose() => _levelFinisher.StandardLevelDidFinish -= HandleLevelFinished;

    /// <summary>Starts a handoff of a downloaded map; an error message for the player when it can't be opened in Solo.</summary>
    public string? Prepare(MapKey map, double? targetAccuracy, out BeatmapLevel level, out BeatmapKey key)
    {
        var error = _launcher.Resolve(map, out level, out key);
        if (error == null) _session.Handoff = new SoloHandoff(map, targetAccuracy);
        return error;
    }

    /// <summary>A play of the handed-off map started from Solo (tracked since its game scene began): into the play log.</summary>
    private void HandleLevelFinished(StandardLevelScenesTransitionSetupDataSO setup, LevelCompletionResults results)
    {
        if (!_session.Active || !_session.FromSolo || _session.Handoff is not { } handoff) return;
        try
        {
            if (setup.gameMode != "Solo") return;
            _playLog.Append(LevelLauncher.ToRecord(setup, results, handoff.Map, handoff.TargetAccuracy, _session));
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"Could not record the To Solo play: {e}");
        }
        finally
        {
            // A restart begins again in the next game scene (GameplayTracker).
            _session.End();
        }
    }

    /// <summary>Solo shows the handed-off map on its difficulty (the game ignores the difficulty of the Solo state).</summary>
    [AffinityPrefix]
    // Overloaded method: name the public overload's parameters (see ToImprovePatches.SelectRecommendedDifficulty).
    [AffinityPatch(typeof(StandardLevelDetailView), nameof(StandardLevelDetailView.SetContent), AffinityMethodType.Normal, null,
        typeof(BeatmapLevel), typeof(BeatmapDifficultyMask), typeof(System.Collections.Generic.HashSet<BeatmapCharacteristicSO>),
        typeof(BeatmapDifficulty), typeof(BeatmapCharacteristicSO), typeof(PlayerData))]
    private void SelectHandoffDifficulty(BeatmapLevel level, ref BeatmapDifficulty defaultDifficulty,
        ref BeatmapCharacteristicSO defaultBeatmapCharacteristic)
    {
        try
        {
            if (!_tab.SoloActive || _session.Handoff is not { } handoff) return;
            if (!string.Equals(SongCore.Collections.GetCustomLevelHash(level.levelID), handoff.Map.Hash, StringComparison.OrdinalIgnoreCase)) return;
            var key = level.GetBeatmapKeys().FirstOrDefault(k =>
                string.Equals(k.beatmapCharacteristic.serializedName, handoff.Map.Characteristic, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.difficulty.ToString(), handoff.Map.Difficulty, StringComparison.OrdinalIgnoreCase));
            if (key == default) return;
            defaultDifficulty = key.difficulty;
            defaultBeatmapCharacteristic = key.beatmapCharacteristic;
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"Could not preselect the To Solo difficulty: {e.Message}");
        }
    }

    /// <summary>Leaving Solo during a handoff: dismiss it at once, then show the plugin again (postfix below).</summary>
    [AffinityPrefix]
    [AffinityPatch(typeof(FlowCoordinator), "DismissFlowCoordinator")]
    private void DismissSoloImmediately(FlowCoordinator flowCoordinator, ref bool immediately)
    {
        if (_session.Handoff != null && flowCoordinator is SoloFreePlayFlowCoordinator) immediately = true;
    }

    [AffinityPostfix]
    [AffinityPatch(typeof(FlowCoordinator), "DismissFlowCoordinator")]
    private void ReturnToPlugin(FlowCoordinator flowCoordinator)
    {
        if (_session.Handoff == null || flowCoordinator is not SoloFreePlayFlowCoordinator) return;
        try { _mainFlowCoordinator.PresentFlowCoordinator(_flowCoordinator.Value, immediately: true); }
        catch (Exception e) { Plugin.Log.Error($"Could not return to Skill Progression: {e}"); }
    }
}
