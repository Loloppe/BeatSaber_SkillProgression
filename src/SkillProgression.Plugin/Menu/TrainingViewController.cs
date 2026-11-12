using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HMUI;
using Reactive;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Progression;
using SkillProgression.Data.State;
using SkillProgression.Plugin.Gameplay;
using SkillProgression.Plugin.Services;
using SkillProgression.Plugin.UI;
using Zenject;

namespace SkillProgression.Plugin.Menu;

/// <summary>
/// Hosts the Training view and wires it to the services. Runs on the Unity main thread; awaits come back here.
/// </summary>
internal class TrainingViewController : ViewController
{
    [Inject] private readonly ProgressionService _service = null!;
    [Inject] private readonly LevelLauncher _launcher = null!;
    [Inject] private readonly MapDownloader _downloader = null!;
    [InjectOptional] private readonly IPlatformUserModel? _platformUserModel = null;
    [Inject] private readonly PlatformsViewController _platforms = null!;

    internal Action? ExploreRequested { get; set; }
    internal Action? AchievementsRequested { get; set; }

    private TrainingView? _view;
    private TrainingSnapshot? _snapshot;
    private string? _playerId;
    private StartMode _pendingStartMode;
    private bool _synced;

    /// <summary>
    /// The next load syncs with BeatLeader (incremental: scores since the last sync). Called each time the plugin is
    /// opened from the main menu; coming back from Explore / Achievements inside the plugin doesn't resync.
    /// </summary>
    public void SyncOnNextLoad()
    {
        _synced = false;
        _service.ResyncUnrankedOnNextUse();
    }

    public override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            _view = new TrainingView
            {
                OnChooseStart = importBeatLeader => ChooseStart(importBeatLeader ? StartMode.ImportBeatLeader : StartMode.Fresh),
                OnFocusChanged = ChangeFocus,
                OnPoolChanged = ChangePool,
                OnPlay = Play,
                OnSkip = Skip,
                OnRequestStartChange = RequestStartChange,
                OnConfirmStartChange = ConfirmStartChange,
                OnOpenExplore = () => ExploreRequested?.Invoke(),
                OnOpenAchievements = () => AchievementsRequested?.Invoke(),
                OnSkipCalibration = SkipCalibration,
            };
            _view.Use(transform);
            _view.WithRectExpand();
        }

        if (addedToHierarchy) _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_view == null) return;
        try
        {
            _view.Screen.Value = TrainingScreen.Loading;
            _view.Message.Value = "Loading your profile";
            if (_playerId == null && _platformUserModel != null)
                _playerId = (await _platformUserModel.GetUserInfo(CancellationToken.None))?.platformUserId;
            if (string.IsNullOrEmpty(_playerId))
            {
                ShowError("Could not identify your account.");
                return;
            }

            _ = LoadPlatformsAsync(_playerId!);
            if (_service.State.StartMode == StartMode.Unset)
            {
                _view.Screen.Value = TrainingScreen.ChooseStart;
                return;
            }

            await RefreshAsync(sync: !_synced, showImport: _service.LastSyncUtc == null && _service.State.StartMode == StartMode.ImportBeatLeader);
            _synced = true;
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e);
            ShowError(e is System.IO.FileNotFoundException
                ? $"Model file missing: {_service.ModelPath}"
                : "Something went wrong while loading. See the log for details.");
        }
    }

    private async Task LoadPlatformsAsync(string playerId)
    {
        try { _platforms.Show(await _service.GetPlatformStatsAsync(playerId)); }
        catch (Exception e) { Plugin.Log.Warn($"Platform stats failed: {e.Message}"); }
    }

    private async Task RefreshAsync(bool sync, bool showImport = false)
    {
        if (_view == null || _playerId == null) return;
        _view.Busy.Value = true;
        if (showImport)
        {
            // First import can take a while: full-screen progress (user request).
            _view.Screen.Value = TrainingScreen.Loading;
            _view.Message.Value = "Importing your BeatLeader scores";
            _view.ImportProgress.Value = 0f;
            _view.Importing.Value = true;
        }
        else if (sync)
        {
            _view.Status.Value = "Syncing with BeatLeader";
            _view.Working.Value = true;
        }
        // Progress<T> captures the Unity main-thread context: reports arrive on the main thread.
        var progress = new Progress<double>(p => _view.ImportProgress.Value = (float)p);
        try
        {
            _snapshot = await _service.RefreshAsync(_playerId, sync, progress);
        }
        finally
        {
            _view.Importing.Value = false;
            _view.Working.Value = false;
            _view.Busy.Value = false;
        }
        Show(_snapshot);
        if (sync) _view.Status.Value = _snapshot.Offline ? "Offline — using cached scores." : "";
    }

    private void Show(TrainingSnapshot s)
    {
        if (_view == null) return;
        _view.Screen.Value = TrainingScreen.Training;
        _platforms.ShowStyleMix(Format.StyleMixLines(s.Report));
        _view.Focus.Value = s.FocusPath;
        _view.Pool.Value = _service.State.MapPool;
        _view.Calibrating.Value = s.Calibration != null;
        if (s.Calibration is { } c)
        {
            _view.Milestone.Value = Format.CalibrationTitle(c);
            _view.HasProgress.Value = true;
            _view.Progress.Value = (float)(c.Step - 1) / c.Total;
            _view.Steps.Value = Format.CalibrationLast(c);
        }
        else
        {
            _view.Milestone.Value = Format.Milestone(s.Focus, s.Starter);
            _view.HasProgress.Value = s.Focus != null;
            _view.Progress.Value = (float)(s.Focus?.ProgressToNext ?? 0);
            _view.Steps.Value = Format.Steps(s.FocusPath, s.Steps);
        }
        _view.NextUp.Value = s.NextUp.Select(n => Format.Row(s.FocusPath, n, MapDownloader.IsDownloaded(n.Recommendation.Map.Hash))).ToList();
        if (s.NextUp.Count == 0)
            _view.Status.Value = "No maps to recommend for this style right now.";
    }

    private void ShowError(string message)
    {
        if (_view == null) return;
        _view.Screen.Value = TrainingScreen.Error;
        _view.Message.Value = message;
    }

    private async void ChooseStart(StartMode mode)
    {
        _service.SetStartMode(mode, DateTime.UtcNow);
        try { await RefreshAsync(sync: true, showImport: mode == StartMode.ImportBeatLeader && _service.LastSyncUtc == null); }
        catch (Exception e) { Plugin.Log.Error(e); ShowError("Loading failed. See the log for details."); }
    }

    private void RequestStartChange()
    {
        if (_view == null) return;
        _pendingStartMode = _service.State.StartMode == StartMode.Fresh ? StartMode.ImportBeatLeader : StartMode.Fresh;
        _view.ConfirmText.Value = _pendingStartMode == StartMode.Fresh
            ? "Start fresh from now? Your BeatLeader history will be ignored."
            : "Import your BeatLeader scores? Your full history will be used again.";
        _view.Screen.Value = TrainingScreen.ConfirmStartChange;
    }

    private void ConfirmStartChange(bool confirmed)
    {
        if (confirmed) ChooseStart(_pendingStartMode);
        else if (_snapshot != null) Show(_snapshot);
    }

    private async void ChangeFocus(string path)
    {
        if (_snapshot != null && path == _snapshot.FocusPath) return;
        _service.SetFocus(path);
        try { await RefreshAsync(sync: false); }
        catch (Exception e) { Plugin.Log.Error(e); }
    }

    private async void ChangePool(Core.Model.MapPool pool)
    {
        _service.SetMapPool(pool);
        try { await RefreshAsync(sync: false); }
        catch (Exception e) { Plugin.Log.Error(e); }
    }

    private async void SkipCalibration()
    {
        if (_snapshot == null) return;
        _service.SkipCalibration(_snapshot.FocusPath);
        try { await RefreshAsync(sync: false); }
        catch (Exception e) { Plugin.Log.Error(e); }
    }

    private async void Skip(NextUpRow row)
    {
        _service.Skip(row.Map);
        try { await RefreshAsync(sync: false); }
        catch (Exception e) { Plugin.Log.Error(e); }
    }

    private async void Play(NextUpRow row)
    {
        if (_view == null || _snapshot == null) return;
        var item = _snapshot.NextUp.FirstOrDefault(n => n.Recommendation.Map.Equals(row.Map));
        if (item == null) return;
        try
        {
            _view.Busy.Value = true;
            if (!MapDownloader.IsDownloaded(row.Map.Hash))
            {
                _view.Status.Value = $"Downloading {row.Title}";
                _view.Working.Value = true;
                _view.NextUp.Value = _view.NextUp.Value.Select(r => r.Map.Equals(row.Map) ? r with { PlayText = "Downloading" } : r).ToList();
                try { await _downloader.DownloadAsync(row.Map.Hash); }
                finally { _view.Working.Value = false; }
                _view.Status.Value = "Starting";
            }

            var calibrating = _snapshot.Calibration != null;
            var error = _launcher.Start(row.Map, record => OnLevelFinished(item, record, calibrating), item.TargetAccuracy);
            if (error != null)
            {
                _view.Status.Value = error;
                _view.Busy.Value = false;
                RefreshRows();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e);
            _view.Status.Value = "Could not start this map. See the log for details.";
            _view.Busy.Value = false;
            RefreshRows();
        }
    }

    /// <summary>Rebuilds the Next up rows, resetting a "Downloading" button and showing newly downloaded maps.</summary>
    private void RefreshRows()
    {
        if (_view == null || _snapshot is not { } s) return;
        _view.NextUp.Value = s.NextUp.Select(n => Format.Row(s.FocusPath, n, MapDownloader.IsDownloaded(n.Recommendation.Map.Hash))).ToList();
    }

    private async void OnLevelFinished(NextUpItem item, PlayRecord? record, bool calibrating)
    {
        if (_view == null) return;
        _view.Busy.Value = false;
        var result = calibrating ? Format.CalibrationResult(item, record) : Format.Result(item, record);
        _view.Status.Value = result;
        try { await RefreshAsync(sync: false); }
        catch (Exception e) { Plugin.Log.Error(e); }
        _view.Status.Value = result + Format.Unlocks(_snapshot?.NewAchievements); // refresh may overwrite the line
    }
}
