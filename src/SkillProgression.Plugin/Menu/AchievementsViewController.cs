using System;
using System.Collections.Generic;
using System.Linq;
using HMUI;
using Reactive;
using Reactive.BeatSaber.Components;
using SkillProgression.Core.Achievements;
using SkillProgression.Core.Profiles;
using SkillProgression.Data.State;
using SkillProgression.Plugin.Services;
using SkillProgression.Plugin.UI;
using UnityEngine;
using Zenject;

namespace SkillProgression.Plugin.Menu;

/// <summary>Achievements (by type, unlocked and locked) and history charts (by platform or style).</summary>
internal class AchievementsViewController : ViewController
{
    private static readonly string[] Platforms = { "BeatLeader", "ScoreSaber", "AccSaber" };
    private const string AccByStar = "accbystar";

    [Inject] private readonly ProgressionService _service = null!;

    private AchievementsView? _view;
    private ProgressOverview? _overview;

    public override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            _view = new AchievementsView { OnCategoryChanged = ShowCategory, OnHistorySourceChanged = ShowHistory };
            _view.Use(transform);
            _view.WithRectExpand();
            var sources = new Dictionary<string, BsDropdownItem>();
            foreach (var p in Platforms) sources[p] = new BsDropdownItem($"{p} rank", null);
            foreach (var d in Destinations.All) sources["style|" + d] = new BsDropdownItem($"{(d == Destinations.HighTech ? "Tech" : d)} milestones", null);
            sources[AccByStar] = new BsDropdownItem("Best accuracy per star", null);
            _view.HistorySources.Value = sources;
        }
        if (addedToHierarchy)
        {
            _overview = _service.Overview();
            // Style buttons open on the focus style, like Training.
            if (!string.IsNullOrEmpty(_service.State.FocusPath)) _view!.Style.Value = _service.State.FocusPath;
            ShowCategory();
            ShowHistory();
        }
    }

    private void ShowCategory()
    {
        if (_view == null || _overview == null) return;
        var category = _view.Category.Value;
        var all = AchievementBoard.Build(category, _view.Style.Value, _overview.Last, _overview.Unlocked, _overview.MilestoneCounts);
        var entries = _view.HideAchieved.Value ? all.Where(e => !e.Unlocked).ToList() : all;
        _view.Rows.Value = entries.Select(e => new AchievementRow(
            e.Title,
            e.Unlocked ? (e.Retroactive ? "earned before" : e.UnlockedUtc?.ToLocalTime().ToString("MMM d, yyyy") ?? "")
                       : e.Progress is double p ? Format.Percent(p) : "locked",
            (float)(e.Progress ?? 0),
            !e.Unlocked && e.Progress.HasValue,
            e.Unlocked)).ToList();
        _view.Empty.Value = entries.Count > 0 ? "" : all.Count > 0 ? "Everything here is achieved." : category switch
        {
            AchievementCategory.AlmostThere => "Nothing in progress yet - play a few maps from Next up.",
            AchievementCategory.Milestones => "No milestones for this style.",
            _ => "Nothing here yet.",
        };
    }

    private void ShowHistory()
    {
        if (_view == null || _overview == null) return;
        var source = _view.HistorySource.Value;
        _view.ShowTable.Value = source == AccByStar;
        if (source == AccByStar) ShowAccByStar();
        else if (source.StartsWith("style|", StringComparison.Ordinal)) ShowMilestones(source.Substring(6));
        else ShowRank(source);
    }

    /// <summary>Best accuracy on N to N+1 star maps: a records table, nothing to unlock (D-035, moved to History).</summary>
    private void ShowAccByStar()
    {
        if (_view == null || _overview == null) return;
        var table = _overview.Last?.BestAccByStar.OrderBy(kv => kv.Key).ToList() ?? new List<KeyValuePair<int, double>>();
        // Two columns so up to 15 star levels fit the chart's space.
        var half = (table.Count + 1) / 2;
        string Levels(IEnumerable<KeyValuePair<int, double>> rows) => string.Join("\n", rows.Select(kv => $"{kv.Key} - {kv.Key + 1} stars"));
        string Values(IEnumerable<KeyValuePair<int, double>> rows) => string.Join("\n", rows.Select(kv => $"{kv.Value * 100:0.00}%"));
        _view.TableLevels.Value = Levels(table.Take(half));
        _view.TableValues.Value = Values(table.Take(half));
        _view.TableLevels2.Value = Levels(table.Skip(half));
        _view.TableValues2.Value = Values(table.Skip(half));
        _view.ChartCaption.Value = table.Count > 0 ? "" : "No cleared ranked maps yet.";
        _view.ChartHelp.Value = "Your best accuracy on ranked maps of each star level (speed modifiers count at their effective stars).";
    }

    private void ShowRank(string platform)
    {
        if (_view == null || _overview == null) return;
        var history = _overview.RankHistories.TryGetValue(platform, out var h) ? h : new List<RankPoint>();
        _view.ChartHelp.Value = "Global rank over time, better ranks higher (log scale)." +
                                (platform == "AccSaber" ? " AccSaber history is short: it started in autumn 2026." : "");
        if (history.Count < 2)
        {
            _view.ChartPoints = Array.Empty<Vector2>();
            _view.ChartCaption.Value = history.Count == 1 ? $"Today #{history[0].Rank:N0} - more points appear day by day." : "No history yet.";
            return;
        }
        var start = history[0].DateUtc;
        var last = history[history.Count - 1];
        var days = Math.Max(1, (last.DateUtc - start).TotalDays);
        var logs = history.Select(p => Math.Log10(Math.Max(1, p.Rank))).ToList();
        double lo = logs.Min(), hi = logs.Max(), span = Math.Max(1e-6, hi - lo);
        _view.ChartPoints = history.Select((p, i) => new Vector2((float)((p.DateUtc - start).TotalDays / days), (float)(1 - (logs[i] - lo) / span))).ToList();
        _view.ChartCaption.Value = $"{start.ToLocalTime():MMM yyyy}  #{history[0].Rank:N0}   to   today  #{last.Rank:N0}   |   best  #{history.Min(p => p.Rank):N0}";
    }

    private void ShowMilestones(string style)
    {
        if (_view == null || _overview == null) return;
        var points = _overview.Snapshots.Where(s => s.Paths.ContainsKey(style))
            .Select(s => (date: s.DateUtc, milestone: s.Paths[style].Milestone, progress: s.Paths[style].Progress)).ToList();
        _view.ChartHelp.Value = "Milestone reached on this style each day, plus progress toward the next one.\nRecorded from the day you started using Skill Progression.";
        if (points.Count == 0)
        {
            _view.ChartPoints = Array.Empty<Vector2>();
            _view.ChartCaption.Value = "No milestone history for this style yet.";
            return;
        }
        var now = points[points.Count - 1];
        var caption = $"Today  milestone {now.milestone}, {Format.Percent(now.progress)} toward milestone {now.milestone + 1}";
        if (points.Count < 2)
        {
            _view.ChartPoints = Array.Empty<Vector2>();
            _view.ChartCaption.Value = caption;
            return;
        }
        var start = points[0].date;
        var days = Math.Max(1, (now.date - start).TotalDays);
        var values = points.Select(p => p.milestone + p.progress).ToList();
        double lo = Math.Floor(values.Min()), hi = Math.Ceiling(values.Max()), span = Math.Max(1, hi - lo);
        _view.ChartPoints = points.Select((p, i) => new Vector2((float)((p.date - start).TotalDays / days), (float)((values[i] - lo) / span))).ToList();
        _view.ChartCaption.Value = $"{start.ToLocalTime():MMM d}  milestone {points[0].milestone}   |   {caption}";
    }
}
