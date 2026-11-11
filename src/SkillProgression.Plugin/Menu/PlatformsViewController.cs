using System.Collections.Generic;
using System.Linq;
using HMUI;
using Reactive;
using SkillProgression.Data.Platforms;
using SkillProgression.Plugin.UI;

namespace SkillProgression.Plugin.Menu;

/// <summary>Right screen: live official progress (D-029: BeatLeader, ScoreSaber, AccSaber). No country rank (user).</summary>
internal class PlatformsViewController : ViewController
{
    private PlatformsView? _view;
    private IReadOnlyList<PlatformStats>? _pending;
    private string _styleMix = "";

    public override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (!firstActivation) return;
        _view = new PlatformsView();
        _view.Use(transform);
        _view.WithRectExpand();
        if (_pending != null) Show(_pending);
        _view.StyleMix.Value = _styleMix;
    }

    public void Show(IReadOnlyList<PlatformStats> stats)
    {
        _pending = stats;
        if (_view == null) return;
        _view.Message.Value = stats.Count == 0 ? "No platform data available." : "";
        _view.Rows.Value = Rows(stats).ToList();
    }

    /// <summary>Style mix shown above the platforms (moved from Training, user request).</summary>
    public void ShowStyleMix(string text)
    {
        _styleMix = text;
        if (_view != null) _view.StyleMix.Value = text;
    }

    private static IEnumerable<PlatformRow> Rows(IReadOnlyList<PlatformStats> stats)
    {
        foreach (var group in stats.GroupBy(s => s.Platform))
        {
            var categorised = group.Any(s => !string.IsNullOrEmpty(s.Category));
            if (categorised) yield return new PlatformRow(group.Key, "", "", true);
            foreach (var s in group)
            {
                var name = categorised ? "  " + s.Category : s.Platform;
                var rank = s.Rank is int r ? $"#{r:N0}" : "-";
                var points = s.Points is double p ? $"{p:N0} {s.Unit}" : "";
                yield return new PlatformRow(name, rank, points, !categorised);
            }
        }
    }
}
