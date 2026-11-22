using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using SkillProgression.Core.Scoring;

namespace SkillProgression.Plugin.UI;

/// <summary>
/// Player-facing wording for Core results (kept out of the views so they stay presentation-only).
/// No ':' anywhere — Beat Saber's font renders it as a dot (user feedback 2026-11-10).
/// </summary>
/// <summary>What a row needs besides the Core item (D-060): the model map, live platform data (null until loaded) and the pool.</summary>
internal sealed record MapContext(MapEntry? Map, LiveMapInfo? Live, LiveCurves? Curves, MapPool Pool);

internal static class Format
{
    private const string Sep = "   |   ";

    public static string Axis(string path) => path switch
    {
        Destinations.Speed or Destinations.Midspeed => "pass",
        Destinations.HighTech => "tech",
        _ => "stars",
    };

    private static string AxisLong(string path) => path switch
    {
        Destinations.Speed or Destinations.Midspeed => "pass rating",
        Destinations.HighTech => "tech rating",
        _ => "stars",
    };

    /// <summary>Style mix for the platforms panel: one style per line.</summary>
    public static string StyleMixLines(PlayerReport report)
    {
        var lines = report.StyleMix.Shares.Where(kv => kv.Value >= 0.01).OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key} {kv.Value:P0}{(report.StyleMix.Established.Contains(kv.Key) ? " ✓" : "")}");
        var text = string.Join("\n", lines);
        return text.Length == 0 ? "No style yet" : text;
    }

    /// <summary>"Milestone" instead of waypoint, average global rank instead of "players here" (user wording).</summary>
    public static string Milestone(PathProgress? p, bool starter)
    {
        if (p == null) return starter ? "Getting started - easier maps for this style" : "";
        return $"Milestone {p.Current!.Index + 1}/{p.WaypointCount}{Sep}Average global rank {p.Current.EquivalentRank:N0}";
    }

    /// <summary>Fresh-start calibration header (D-040).</summary>
    public static string CalibrationTitle(CalibrationStatus c) => $"Calibration {c.Step}/{c.Total}{Sep}finding your level after the fresh start";

    /// <summary>What the last calibration map said, and where the next one goes.</summary>
    public static string CalibrationLast(CalibrationStatus c) =>
        c.Last == null ? "Starting with the easiest maps of the pool"
        : c.Last.Success ? $"Last map cleared at {c.Last.Accuracy:P1} - the next one is a bit harder"
        : c.Last.Failed ? "Last map failed - the next one is a bit easier"
        : $"Last map {c.Last.Accuracy:P1}, under the target - the next one is a bit easier";

    /// <summary>
    /// Polish steps are in acc vs predicted; shown as the average accuracy to reach on the style's top plays (same
    /// weights, so a residual gain is the same accuracy gain) plus the gain, e.g. "95.5% (+0.6%)" (user, 2026-11-11).
    /// </summary>
    public static string Steps(string path, PathState? state, IReadOnlyList<ShortTermStep> steps) =>
        string.Join("        ", steps.Select(s => s.Kind == ObjectiveKind.Push
            ? s.AtLevel ? $"Next clear target - new maps around {s.Target:F2} {AxisLong(path)}"
            : $"Next clear target - {s.Target:F2} {AxisLong(path)}"
            : state == null ? $"Next accuracy target - {s.Target - s.Current:+0.00%} average accuracy"
            : $"Next accuracy target - {Math.Min(1, state.Accuracy + s.Target - s.Current):P2} ({s.Target - s.Current:+0.00%})"));

    /// <summary>Explore row (D-060): "Reach this acc" on Improve, "Clear this map" on Never played, toward the expected accuracy.</summary>
    public static MapRow ExploreRow(string path, ExploreMode mode, ExploreItem i, bool downloaded, MapContext m)
    {
        var line = Goal(mode == ExploreMode.Improve ? "Reach this acc" : "Clear this map", i.CurrentAccuracy ?? 0, i.ExpectedAccuracy, m);
        // The style's rating is already in the card's rating pills (user, 2026-11-22: no duplicates).
        var notes = $"Expected {i.ExpectedAccuracy:P2}" + (i.CurrentAccuracy is double c ? $"   best {c:P2}" : "");
        return new MapRow(i.Map, i.Name, RowTitle(i.Mapper, i.Name), line, m.Live?.CoverUrl ?? "", downloaded ? "Play" : "Get & Play",
            Card(i.Map, i.Mapper, i.Name, i.Stars, i.Nps, i.Njs, i.CurrentAccuracy ?? 0, i.ExpectedAccuracy, notes, m));
    }

    /// <summary>
    /// "Reach this acc | 98.70% (500pp) → 99.20% (530pp)" (user format, 2026-11-22). The value is the pool's own (BeatLeader
    /// pp, ScoreSaber pp or AccSaber AP), read from the same curve at both accuracies, no modifiers; left out until loaded
    /// and on maps ranked nowhere.
    /// </summary>
    private static string Goal(string label, double current, double target, MapContext m)
    {
        var platform = m.Map == null ? null : LiveScoring.ForPool(m.Pool, m.Map);
        string Part(double acc) => platform is ScorePlatform p && LiveScoring.Value(p, m.Live, m.Curves, acc) is double v
            ? $"{acc:P2} ({Points(p, v)})" : $"{acc:P2}";
        return $"{label}   |   {Part(current)}  →  <color=#99D9FF>{Part(target)}</color>";
    }

    private static string Points(ScorePlatform p, double value) => p == ScorePlatform.AccSaber ? $"{value:N0} AP" : $"{value:N0}pp";

    private static string PlatformName(ScorePlatform p) => p switch
    {
        ScorePlatform.BeatLeader => "BeatLeader", ScorePlatform.ScoreSaber => "ScoreSaber", _ => "AccSaber",
    };

    /// <summary>Hover card (D-060): every row detail plus base JD, all ratings and every platform's value.</summary>
    private static MapCard Card(MapKey key, string mapper, string name, double modelStars, double nps, double modelNjs,
        double current, double target, string notes, MapContext m)
    {
        var live = m.Live;
        var model = m.Map != null && m.Map.Ratings.TryGetValue("", out var r) ? r : null;
        // Live BeatLeader ratings when loaded (what BeatLeader shows), else the model's.
        var stars = live?.Stars > 0 ? live.Stars : model?.Stars ?? modelStars;
        var pass = live?.Pass > 0 ? live.Pass : model?.Pass ?? 0;
        var acc = live?.Acc > 0 ? live.Acc : model?.Acc ?? 0;
        var tech = live?.Tech > 0 ? live.Tech : model?.Tech ?? 0;
        var njs = live?.Njs > 0 ? live.Njs : modelNjs;
        var jd = live == null ? 0 : LiveScoring.BaseJumpDistance(live.Bpm, njs, live.NjsOffset);

        var stats = string.Join("     ", new[]
        {
            nps > 0 ? $"{nps:0.0} NPS" : "",
            njs > 0 ? $"{njs:0.#} NJS" : "",
            jd > 0 ? $"{jd:0.0} JD" : "",
            live?.Bpm > 0 ? $"{live.Bpm:0.#} BPM" : "",
        }.Where(x => x.Length > 0));

        var values = live == null ? "Loading pp" : string.Join("\n", Enum.GetValues(typeof(ScorePlatform)).Cast<ScorePlatform>()
            .Select(p => (p, now: LiveScoring.Value(p, live, m.Curves, current), goal: LiveScoring.Value(p, live, m.Curves, target)))
            .Where(x => x.now != null && x.goal != null)
            .Select(x => $"{PlatformName(x.p)}<pos=32%>{Points(x.p, x.now!.Value)}  →  <color=#99D9FF>{Points(x.p, x.goal!.Value)}</color>"));
        if (live != null && values.Length == 0) values = "No pp - not ranked on BeatLeader, ScoreSaber or AccSaber";

        return new MapCard(Clip(name, 60), (string.IsNullOrWhiteSpace(mapper) ? "" : Clip(mapper, 40) + "   ·   ") + Long(key.Difficulty),
            stars > 0 ? $"{stars:F2}★" : "", pass > 0 ? $"Pass {pass:F2}" : "", acc > 0 ? $"Acc {acc:F2}" : "", tech > 0 ? $"Tech {tech:F2}" : "",
            stats, values, notes);
    }

    /// <summary>Longest mapper / song name in a row title; longer ones end in "…".</summary>
    public const int MaxMapperLength = 22, MaxSongLength = 34;

    /// <summary>Song name with the mapper dimmed after it, both clipped so the row keeps one line.</summary>
    private static string RowTitle(string mapper, string name) =>
        Clip(name, MaxSongLength) + (string.IsNullOrWhiteSpace(mapper) ? "" : $"   <color=#A6A6A6><size=85%>{Clip(mapper, MaxMapperLength)}</size></color>");

    private static string Clip(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text.Substring(0, max - 1).TrimEnd() + "…";
    }

    /// <summary>D-032 B: how many maps each slider position offers; the current one highlighted.</summary>
    public static string ExploreCounts(int[] counts, int offset, ExploreMode mode)
    {
        var cells = counts.Select((n, i) => i - 3 == offset ? $"<b><color=#99D9FF>{n}</color></b>" : n.ToString());
        var what = mode == ExploreMode.Improve ? "Scores to improve" : "New maps";
        return $"{what} per position, easier to harder   {string.Join("   ", cells)}";
    }

    /// <summary>D-032 A: the range used, and a note when it had to widen.</summary>
    public static string ExploreRange(string path, ExploreResult r)
    {
        var range = $"{AxisLong(path)} {r.RangeLow:F2} - {r.RangeHigh:F2}";
        return r.Widened ? $"Few maps here - range widened to {range}" : $"Range {range}";
    }

    public static string ExploreResult(ExploreItem item, PlayRecord? record)
    {
        if (record == null) return "Back from the level.";
        if (record.Outcome == PlayOutcome.Failed || record.SoftFailed) return "Failed - maybe try an easier list.";
        if (record.Outcome != PlayOutcome.Cleared) return "Back from the level.";
        var vsBest = item.CurrentAccuracy is double c && record.Accuracy > c ? $" - new best (+{record.Accuracy - c:P1})" : "";
        return $"{record.Accuracy:P2} - expected {item.ExpectedAccuracy:P1}{vsBest}";
    }

    /// <summary>Next up row (D-060): "Clear this map" for push objectives, "Reach this acc" for accuracy ones.</summary>
    public static MapRow Row(string path, NextUpItem n, bool downloaded, MapContext m)
    {
        var r = n.Recommendation;
        var line = Goal(n.Kind == ObjectiveKind.Push ? "Clear this map" : "Reach this acc", r.CurrentAccuracy ?? 0, n.TargetAccuracy, m);
        var misses = n.Demoted ? "   moved down" : n.RecentMisses > 0 ? $"   {n.RecentMisses} miss" + (n.RecentMisses > 1 ? "es" : "") : "";
        // The style's rating is already in the card's rating pills (user, 2026-11-22: no duplicates).
        var notes = $"{(n.Kind == ObjectiveKind.Push ? "Clear" : "Accuracy")} objective{misses}";
        return new MapRow(r.Map, r.Name, RowTitle(r.Mapper, r.Name), line, m.Live?.CoverUrl ?? "", downloaded ? "Play" : "Get & Play",
            Card(r.Map, r.Mapper, r.Name, r.Stars, r.Nps, r.Njs, r.CurrentAccuracy ?? 0, n.TargetAccuracy, notes, m));
    }

    /// <summary>Progress of something not finished: rounded down, never "100%".</summary>
    public static string Percent(double progress) => $"{Math.Min(99, Math.Floor(Math.Max(0, progress) * 100)):0}%";

    /// <summary>Newly unlocked achievements appended to the result line.</summary>
    public static string Unlocks(IReadOnlyList<SkillProgression.Core.Achievements.Achievement>? unlocked) =>
        unlocked == null || unlocked.Count == 0 ? ""
            : unlocked.Count == 1 ? $"   |   Unlocked - {unlocked[0].Title} ✓" : $"   |   {unlocked.Count} achievements unlocked ✓";

    /// <summary>D-030: missing a target costs nothing; the line is factual and encouraging.</summary>
    public static string Result(NextUpItem item, PlayRecord? record)
    {
        if (record == null) return "Back from the level.";
        if (record.Outcome == PlayOutcome.Quit) return "Quit - no worries, it is still in your list.";
        if (record.Outcome == PlayOutcome.Failed || record.SoftFailed) return "Failed - it stays in your list (3 misses move it down).";
        var delta = record.Accuracy - item.TargetAccuracy;
        return delta >= 0
            ? $"{record.Accuracy:P2} - target {item.TargetAccuracy:P2} reached ✓"
            : $"{record.Accuracy:P2} - target {item.TargetAccuracy:P2} ({delta:+0.00%;-0.00%}). Try again or skip.";
    }

    /// <summary>Result of a calibration map (D-040): the first clear or fail counts and the map is replaced, so no retry.</summary>
    public static string CalibrationResult(NextUpItem item, PlayRecord? record)
    {
        if (record == null) return "Back from the level.";
        if (record.Outcome == PlayOutcome.Quit) return "Quit - not counted, the calibration map is still waiting.";
        if (record.Outcome == PlayOutcome.Failed || record.SoftFailed) return "Failed - counted, calibration steps down to easier maps.";
        var delta = record.Accuracy - item.TargetAccuracy;
        return delta >= 0
            ? $"{record.Accuracy:P2} - target {item.TargetAccuracy:P2} reached ✓ - calibration steps up."
            : $"{record.Accuracy:P2} - target {item.TargetAccuracy:P2} ({delta:+0.00%;-0.00%}) - counted, calibration steps down.";
    }

    /// <summary>User format: [E] Easy, [N] Normal, [H] Hard, [Ex] Expert, [Ex+] Expert+.</summary>
    public static string Short(string difficulty) => difficulty switch
    {
        "ExpertPlus" => "Ex+", "Expert" => "Ex", "Hard" => "H", "Normal" => "N", "Easy" => "E", _ => difficulty,
    };

    private static string Long(string difficulty) => difficulty == "ExpertPlus" ? "Expert+" : difficulty;
}
