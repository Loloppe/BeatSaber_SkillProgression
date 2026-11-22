using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Scoring;
using SkillProgression.Plugin.Services;
using SkillProgression.Plugin.UI;

namespace SkillProgression.Plugin.Menu;

/// <summary>
/// Live platform data behind the rows of one screen (D-060): rows render at once from the model, then each map's pp / AP,
/// ratings, JD and cover fill in as its data arrives. Main thread only.
/// </summary>
internal sealed class LiveRowData
{
    private readonly ProgressionService _service;
    private readonly Dictionary<MapKey, LiveMapInfo> _maps = new();
    private readonly HashSet<MapKey> _loading = new();
    private LiveCurves? _curves;
    private Task? _curvesTask;

    public LiveRowData(ProgressionService service) => _service = service;

    public MapContext Context(MapKey key, MapPool pool) =>
        new(_service.FindMap(key), _maps.TryGetValue(key, out var live) ? live : null, _curves, pool);

    /// <summary>Fetches what is missing for these maps, in order, calling <paramref name="changed"/> after each arrival.</summary>
    public async void Load(IEnumerable<MapKey> keys, Action changed)
    {
        try
        {
            if (_curves == null)
            {
                _curvesTask ??= LoadCurvesAsync();
                await _curvesTask;
                changed();
            }
            foreach (var key in keys.Where(k => !_maps.ContainsKey(k) && _loading.Add(k)).ToList())
            {
                try
                {
                    if (await _service.LiveMapAsync(key) is { } info)
                    {
                        _maps[key] = info;
                        changed();
                    }
                }
                finally { _loading.Remove(key); }
            }
        }
        catch (Exception e) { Plugin.Log.Error(e); }
    }

    private async Task LoadCurvesAsync()
    {
        _curves = await _service.LiveCurvesAsync();
        if (_curves == null) _curvesTask = null; // retry on the next load
    }
}
