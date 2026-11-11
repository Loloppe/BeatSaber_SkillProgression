using System;
using System.Collections.Generic;
using System.Linq;
using SiraUtil.Submissions;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Plays;
using SkillProgression.Data.Plays;
using Zenject;

namespace SkillProgression.Plugin.Gameplay;

/// <summary>
/// Starts a level directly from the plugin (D-006) and records its result in the local play log (D-011).
/// Mirrors what SinglePlayerLevelSelectionFlowCoordinator / SoloFreePlayFlowCoordinator do on 1.40.8, minus their UI:
/// gameMode "Solo" (required by BeatLeader/ScoreSaber), player settings, and the local stats update afterwards.
/// </summary>
internal class LevelLauncher
{
    [Inject] private readonly MenuTransitionsHelper _menuTransitionsHelper = null!;
    [Inject] private readonly PlayerDataModel _playerDataModel = null!;
    [Inject] private readonly EnvironmentsListModel _environmentsListModel = null!;
    [Inject] private readonly BeatmapLevelsModel _beatmapLevelsModel = null!;
    [Inject] private readonly PlatformLeaderboardsModel _platformLeaderboardsModel = null!;
    [Inject] private readonly GameplaySetupViewController _gameplaySetup = null!;
    [Inject] private readonly PlayLogStore _playLog = null!;
    [Inject] private readonly PlaySession _session = null!;

    private Action<PlayRecord?>? _onFinished;
    private MapKey _current;
    private double? _target;

    public string Describe(MapKey map)
    {
        var level = SongCore.Loader.GetLevelByHash(map.Hash);
        return level == null
            ? $"Test map not downloaded ({map.Hash.Substring(0, 8)}…)"
            : $"{level.songName} — {level.songAuthorName} [{map.Difficulty}]";
    }

    /// <returns>An error message for the player, or null when the level is starting.</returns>
    /// <param name="targetAccuracy">Target shown for the play, kept in the play log (D-042).</param>
    public string? Start(MapKey map, Action<PlayRecord?> onFinished, double? targetAccuracy = null)
    {
        if (!SongCore.Loader.AreSongsLoaded) return "Songs are still loading, try again in a moment.";
        var level = SongCore.Loader.GetLevelByHash(map.Hash);
        if (level == null || _beatmapLevelsModel.GetBeatmapLevel(level.levelID) == null)
            return "This map is not downloaded.";

        var key = level.GetBeatmapKeys().FirstOrDefault(k =>
            string.Equals(k.beatmapCharacteristic.serializedName, map.Characteristic, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(k.difficulty.ToString(), map.Difficulty, StringComparison.OrdinalIgnoreCase));
        if (key == default) return $"Difficulty {map.Characteristic} {map.Difficulty} not found in this map.";

        _onFinished = onFinished;
        _current = map;
        _target = targetAccuracy;
        // Current choices from the gameplay setup panel shown in our flow (as the game's solo flow does).
        var colorSchemes = _gameplaySetup.colorSchemesSettings;

        if (!BeatLeaderInterop.PrepareRecording())
            Plugin.Log.Info("Starting without BeatLeader recording.");

        _session.Begin(_gameplaySetup.gameplayModifiers.noFailOn0Energy);
        _menuTransitionsHelper.StartStandardLevel(
            gameMode: "Solo",
            beatmapKey: key,
            beatmapLevel: level,
            overrideEnvironmentSettings: _gameplaySetup.environmentOverrideSettings,
            playerOverrideColorScheme: colorSchemes.GetOverrideColorScheme(),
            playerOverrideLightshowColors: colorSchemes.ShouldOverrideLightshowColors(),
            beatmapOverrideColorScheme: level.GetColorScheme(key.beatmapCharacteristic, key.difficulty),
            gameplayModifiers: _gameplaySetup.gameplayModifiers,
            playerSpecificSettings: _gameplaySetup.playerSettings,
            practiceSettings: null,
            environmentsListModel: _environmentsListModel,
            backButtonText: "Skill Progression",
            useTestNoteCutSoundEffects: false,
            startPaused: false,
            beforeSceneSwitchToGameplayCallback: null,
            afterSceneSwitchToGameplayCallback: null,
            levelFinishedCallback: HandleLevelFinished,
            levelRestartedCallback: null);
        return null;
    }

    private void HandleLevelFinished(StandardLevelScenesTransitionSetupDataSO setup, LevelCompletionResults results)
    {
        PlayRecord? record = null;
        try
        {
            record = ToRecord(setup, results);
            // A restart plays the level again in the same session: track it afresh.
            if (record.Outcome == PlayOutcome.Restarted) _session.Begin(_session.NoFail);
            else _session.End();
            _playLog.Append(record);
            UpdateLocalStats(setup, results);
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"Failed to record play: {e}");
        }

        var callback = _onFinished;
        _onFinished = null;
        callback?.Invoke(record);
    }

    private PlayRecord ToRecord(StandardLevelScenesTransitionSetupDataSO setup, LevelCompletionResults results)
    {
        var max = ScoreModel.ComputeMaxMultipliedScoreForBeatmap(setup.transformedBeatmapData);
        return new PlayRecord
        {
            PlayedAtUtc = DateTime.UtcNow,
            Hash = _current.Hash,
            Characteristic = _current.Characteristic,
            Difficulty = _current.Difficulty,
            Outcome = results.levelEndAction switch
            {
                LevelCompletionResults.LevelEndAction.Quit => PlayOutcome.Quit,
                LevelCompletionResults.LevelEndAction.Restart => PlayOutcome.Restarted,
                _ => results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared
                    ? PlayOutcome.Cleared
                    : results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed ? PlayOutcome.Failed : PlayOutcome.Quit
            },
            Practice = setup.practiceSettings != null,
            Accuracy = max > 0 ? (double)results.multipliedScore / max : 0,
            MultipliedScore = results.multipliedScore,
            ModifiedScore = results.modifiedScore,
            MaxMultipliedScore = max,
            Misses = results.missedCount,
            BadCuts = results.badCutsCount,
            MaxCombo = results.maxCombo,
            FullCombo = results.fullCombo,
            Modifiers = ModifierCodes(results.gameplayModifiers),
            EndSongTime = results.endSongTime,
            SongDuration = setup.beatmapLevel.songDuration,
            SoftFailed = _session.SoftFailSongTime != null && results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared,
            SoftFailSongTime = _session.SoftFailSongTime,
            Pauses = _session.Pauses,
            TargetAccuracy = _target,
        };
    }

    /// <summary>Same local stats update as SoloFreePlayFlowCoordinator (1.40.8), which we bypass.</summary>
    private void UpdateLocalStats(StandardLevelScenesTransitionSetupDataSO setup, LevelCompletionResults results)
    {
        if (setup.practiceSettings != null) return;
        if (results is SiraLevelCompletionResults sira && !sira.ShouldSubmitScores) return;

        var playerData = _playerDataModel.playerData;
        var key = setup.beatmapKey;
        var cleared = results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared;
        var failed = results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed;
        if (cleared || failed)
        {
            var stats = playerData.GetOrCreatePlayerLevelStatsData(in key);
            LevelCompletionResultsHelper.ProcessScore(in key, playerData, stats, results, setup.transformedBeatmapData, _platformLeaderboardsModel);
        }

        playerData.playerAllOverallStatsData.UpdateSoloFreePlayOverallStatsData(results, key.difficulty);
        UnityAsyncHelper.InvokeSafe(_playerDataModel.SaveAsync);
    }

    private static IReadOnlyList<string> ModifierCodes(GameplayModifiers m)
    {
        var codes = new List<string>();
        if (m.songSpeed == GameplayModifiers.SongSpeed.Faster) codes.Add("FS");
        if (m.songSpeed == GameplayModifiers.SongSpeed.SuperFast) codes.Add("SF");
        if (m.songSpeed == GameplayModifiers.SongSpeed.Slower) codes.Add("SS");
        if (m.noFailOn0Energy) codes.Add("NF");
        if (m.instaFail) codes.Add("IF");
        if (m.energyType == GameplayModifiers.EnergyType.Battery) codes.Add("BE");
        if (m.disappearingArrows) codes.Add("DA");
        if (m.ghostNotes) codes.Add("GN");
        if (m.noBombs) codes.Add("NB");
        if (m.noArrows) codes.Add("NA");
        if (m.enabledObstacleType == GameplayModifiers.EnabledObstacleType.NoObstacles) codes.Add("NO");
        if (m.proMode) codes.Add("PM");
        if (m.strictAngles) codes.Add("SA");
        if (m.smallCubes) codes.Add("SC");
        if (m.zenMode) codes.Add("ZM");
        return codes;
    }
}
