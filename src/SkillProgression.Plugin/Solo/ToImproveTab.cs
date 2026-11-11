using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage;
using SkillProgression.Core.Progression;
using SkillProgression.Plugin.Services;
using UnityEngine;
using Zenject;

namespace SkillProgression.Plugin.Solo;

/// <summary>
/// The Solo menu's "To improve" tab (D-049): a level category after Favorites / All whose pack is Explore's Improve list
/// for the saved settings, rebuilt after every Solo play. Holds the pack; <see cref="ToImprovePatches"/> shows it.
/// </summary>
internal sealed class ToImproveTab : IInitializable
{
    public const SelectLevelCategoryViewController.LevelCategory Category = (SelectLevelCategoryViewController.LevelCategory)5;
    public const string PackId = "SkillProgression_ToImprove";
    public const string Hint = "To improve";

    [Inject] private readonly ProgressionService _service = null!;
    [Inject] private readonly MapDownloader _downloader = null!;

    /// <summary>Tab hint and pack name: follows the Explore mode the tab was filled from.</summary>
    public string Title => _service.State.ToImprove?.Mode == ExploreMode.NewMaps ? "Never played" : Hint;

    private Sprite? _icon;
    private Task? _rebuild;

    public Sprite Icon => _icon ??= LoadIcon();

    private IReadOnlyList<ExploreItem> _items = Array.Empty<ExploreItem>();

    /// <summary>
    /// The tab's pack, built from SongCore's current levels each time (a SongCore refresh reloads them, so a pack kept
    /// from before would hold stale levels). Null without settings or downloaded maps.
    /// </summary>
    public BeatmapLevelPack? Pack => BuildPack(_items);

    /// <summary>The Solo flow is the one using the level selection screens (they're shared with Multiplayer / Party).</summary>
    public bool SoloActive { get; set; }

    /// <summary>Raised on the main thread after the pack was rebuilt.</summary>
    public event Action? PackChanged;

    public void Initialize() => _ = RebuildAsync();

    /// <summary>
    /// The difficulty the tab recommends for a level (the list's entry for that song, first one when it lists several
    /// difficulties); null when the level isn't in the tab.
    /// </summary>
    public BeatmapKey? RecommendedKey(BeatmapLevel level)
    {
        var hash = SongCore.Collections.GetCustomLevelHash(level.levelID);
        if (string.IsNullOrEmpty(hash)) return null;
        var item = _items.FirstOrDefault(i => string.Equals(i.Map.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (item == null) return null;
        var key = level.GetBeatmapKeys().FirstOrDefault(k =>
            string.Equals(k.beatmapCharacteristic.serializedName, item.Map.Characteristic, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(k.difficulty.ToString(), item.Map.Difficulty, StringComparison.OrdinalIgnoreCase));
        return key.beatmapCharacteristic == null ? null : key;
    }

    /// <summary>The latest rebuild (completed when none is running).</summary>
    public Task Current => _rebuild ?? Task.CompletedTask;

    /// <summary>Rebuilds the pack from the saved Explore settings (downloaded maps only, Explore's order).</summary>
    /// <param name="downloadMissing">After a Solo play (user, 2026-11-11): first download the list's missing maps, so
    /// the next map joins the tab instead of the list shrinking.</param>
    public Task RebuildAsync(bool downloadMissing = false) => _rebuild = RebuildCoreAsync(_rebuild, downloadMissing);

    private async Task RebuildCoreAsync(Task? previous, bool downloadMissing)
    {
        if (previous != null) { try { await previous; } catch { /* logged by its own run */ } }
        try
        {
            var items = await _service.ToImproveAsync();
            if (downloadMissing)
                foreach (var item in items.Where(i => !MapDownloader.IsDownloaded(i.Map.Hash)))
                {
                    try { await _downloader.DownloadAsync(item.Map.Hash); }
                    catch (Exception e) { Plugin.Log.Warn($"Could not download {item.Name} for the Solo tab: {e.Message}"); }
                }
            _items = items;
            PackChanged?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"Could not build the To improve tab: {e}");
        }
    }

    private BeatmapLevelPack? BuildPack(IReadOnlyList<ExploreItem> items)
    {
        var levels = items
            .Select(i => SongCore.Loader.GetLevelByHash(i.Map.Hash))
            .Where(l => l != null)
            .Select(l => l!)
            .Distinct()
            .ToArray();
        if (levels.Length == 0) return null;
        // Shown as a filtered list (no pack header), but never offer to buy it if a header ever appears.
        return new BeatmapLevelPack(PackId, Title, Title, Icon, Icon, PackBuyOption.DisableBuyOption, levels, PlayerSensitivityFlag.Safe);
    }

    private static Sprite LoadIcon()
    {
        using var stream = typeof(ToImproveTab).Assembly.GetManifestResourceStream("SkillProgression.Plugin.Resources.to-improve.png");
        if (stream == null) throw new InvalidOperationException("Embedded icon to-improve.png is missing.");
        var bytes = new byte[stream.Length];
        _ = stream.Read(bytes, 0, bytes.Length);
        return Utilities.LoadSpriteRaw(bytes, 100f);
    }
}
