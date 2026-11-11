using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HMUI;
using Reactive;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using SkillProgression.Plugin.Gameplay;
using SkillProgression.Plugin.Services;
using SkillProgression.Plugin.UI;
using Zenject;

namespace SkillProgression.Plugin.Menu;

/// <summary>
/// Explore side feature (user request 2026-11-10). Independent of the Training objectives, but plays go through the
/// same launcher and play log, so they can still complete a milestone or step.
/// </summary>
internal class ExploreViewController : ViewController
{
    [Inject] private readonly ProgressionService _service = null!;
    [Inject] private readonly LevelLauncher _launcher = null!;
    [Inject] private readonly MapDownloader _downloader = null!;

    private ExploreView? _view;
    private List<ExploreItem> _items = new();
    private int _seed = Environment.TickCount;
    private int _requestId;
    /// <summary>Curated picked in Explore (session only; Training keeps its ranked pool, D-045).</summary>
    private bool _curated;

    public override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            _view = new ExploreView
            {
                OnChanged = () => _ = LoadAsync(), OnNewList = NewList, OnPlay = Play,
                OnPoolChanged = pool =>
                {
                    _curated = pool == MapPool.Curated;
                    if (!_curated) _service.SetMapPool(pool);
                    if (pool == MapPool.AccSaber) _view!.Style.Value = Destinations.Accuracy;
                    _ = LoadAsync();
                },
            };
            _view.Use(transform);
            _view.WithRectExpand();
        }

        if (addedToHierarchy && _view != null)
        {
            var focus = _service.State.FocusPath;
            _view.Pool.Value = _curated ? MapPool.Curated : _service.State.MapPool;
            _view.Style.Value = _view.Pool.Value == MapPool.AccSaber ? Destinations.Accuracy
                : string.IsNullOrEmpty(focus) ? Destinations.Midspeed : focus;
            _ = LoadAsync();
        }
    }

    private void NewList()
    {
        _seed++;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_view == null) return;
        var request = ++_requestId;
        _view.Working.Value = true;
        _view.Status.Value = "Finding maps";
        try
        {
            var (result, counts) = await _service.ExploreAsync(_view.Style.Value, _view.Mode.Value, _view.Offset.Value, _seed, _view.Pool.Value);
            if (request != _requestId) return; // a newer request (slider moved) superseded this one
            var items = result.Items;
            _items = items;
            _view.Counts.Value = Format.ExploreCounts(counts, _view.Offset.Value, _view.Mode.Value);
            _view.Range.Value = Format.ExploreRange(_view.Style.Value, result);
            _view.Rows.Value = items.Select(i => Format.ExploreRow(_view.Style.Value, i, MapDownloader.IsDownloaded(i.Map.Hash))).ToList();
            _view.Status.Value = items.Count == 0
                ? (_view.Mode.Value == ExploreMode.Improve ? "No scores to improve at this difficulty - try another one." : "No new maps at this difficulty - try another one.")
                : "";
        }
        catch (Exception e)
        {
            Plugin.Log.Error(e);
            if (request == _requestId) _view.Status.Value = "Could not build a list. See the log for details.";
        }
        finally
        {
            if (request == _requestId) _view.Working.Value = false;
        }
    }

    private async void Play(ExploreRow row)
    {
        if (_view == null) return;
        var item = _items.FirstOrDefault(i => i.Map.Equals(row.Map));
        if (item == null) return;
        try
        {
            _view.Busy.Value = true;
            if (!MapDownloader.IsDownloaded(row.Map.Hash))
            {
                _view.Status.Value = $"Downloading {row.Title}";
                _view.Working.Value = true;
                _view.Rows.Value = _view.Rows.Value.Select(r => r.Map.Equals(row.Map) ? r with { PlayText = "Downloading" } : r).ToList();
                try { await _downloader.DownloadAsync(row.Map.Hash); }
                finally { _view.Working.Value = false; }
            }

            var error = _launcher.Start(row.Map, record => OnLevelFinished(item, record), item.ExpectedAccuracy);
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

    private void OnLevelFinished(ExploreItem item, PlayRecord? record)
    {
        if (_view == null) return;
        _view.Busy.Value = false;
        RefreshRows();
        _view.Status.Value = Format.ExploreResult(item, record);
    }

    /// <summary>Rebuilds the rows, resetting a "Downloading" button and showing newly downloaded maps.</summary>
    private void RefreshRows()
    {
        if (_view == null) return;
        _view.Rows.Value = _items.Select(i => Format.ExploreRow(_view.Style.Value, i, MapDownloader.IsDownloaded(i.Map.Hash))).ToList();
    }
}
