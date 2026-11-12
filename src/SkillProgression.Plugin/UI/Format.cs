using System;
using System.Collections.Generic;
using System.Linq;
using SkillProgression.Core.Plays;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;

namespace SkillProgression.Plugin.UI;

/// <summary>
/// Player-facing wording for Core results (kept out of the views so they stay presentation-only).
/// No ':' anywhere — Beat Saber's font renders it as a dot (user feedback 2026-11-10).
/// </summary>
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

    public static string Steps(string path, IReadOnlyList<ShortTermStep> steps) =>
        string.Join("        ", steps.Select(s => s.Kind == ObjectiveKind.Push
            ? $"Next clear target - {s.Target:F2} {AxisLong(path)}"
            : $"Next accuracy target - {s.Target:+0.0%;-0.0%}"));

    public static ExploreRow ExploreRow(string path, ExploreItem i, bool downloaded)
    {
        var best = i.CurrentAccuracy is double c ? $"<pos=46%>best {c:P1}" : "";
        return new ExploreRow(i.Map, Title(i.Mapper, i.Name, i.Map.Difficulty, i.Stars),
            $"{Axis(path)} {i.AxisRating:F2}<pos=20%>expected {i.ExpectedAccuracy:P1}{best}", downloaded ? "Play" : "Get & Play");
    }

    /// <summary>Longest mapper / song name in a map title; longer ones end in "…" so difficulty and stars stay visible.</summary>
    public const int MaxMapperLength = 16, MaxSongLength = 26;

    /// <summary>Map title with mapper, difficulty and star rating, e.g. "Mapper - Song [Ex+]  9.47★" (no mapper: "Song …").</summary>
    private static string Title(string mapper, string name, string difficulty, double stars) =>
        $"{(string.IsNullOrWhiteSpace(mapper) ? "" : Clip(mapper, MaxMapperLength) + " - ")}{Clip(name, MaxSongLength)} [{Short(difficulty)}]  {stars:F2}★";

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

    public static NextUpRow Row(string path, NextUpItem n, bool downloaded)
    {
        var r = n.Recommendation;
        // Columns at fixed positions (TMP <pos>) so ratings and targets line up from row to row.
        var kind = n.Kind == ObjectiveKind.Push ? "Clear" : "Accuracy";
        var now = r.CurrentAccuracy is double c ? $"<pos=54%>now {c:P1}" : "<pos=54%>now 0%";
        var misses = n.Demoted ? "<pos=74%>moved down" : n.RecentMisses > 0 ? $"<pos=74%>{n.RecentMisses} miss" + (n.RecentMisses > 1 ? "es" : "") : "";
        return new NextUpRow(r.Map, Title(r.Mapper, r.Name, r.Map.Difficulty, r.Stars),
            $"{kind}<pos=14%>{Axis(path)} {r.AxisRating:F2}<pos=32%>target {n.TargetAccuracy:P1}{now}{misses}", downloaded ? "Play" : "Get & Play");
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
            ? $"{record.Accuracy:P2} - target {item.TargetAccuracy:P1} reached ✓"
            : $"{record.Accuracy:P2} - target {item.TargetAccuracy:P1} ({delta:+0.0%;-0.0%}). Try again or skip.";
    }

    /// <summary>Result of a calibration map (D-040): the first clear or fail counts and the map is replaced, so no retry.</summary>
    public static string CalibrationResult(NextUpItem item, PlayRecord? record)
    {
        if (record == null) return "Back from the level.";
        if (record.Outcome == PlayOutcome.Quit) return "Quit - not counted, the calibration map is still waiting.";
        if (record.Outcome == PlayOutcome.Failed || record.SoftFailed) return "Failed - counted, calibration steps down to easier maps.";
        var delta = record.Accuracy - item.TargetAccuracy;
        return delta >= 0
            ? $"{record.Accuracy:P2} - target {item.TargetAccuracy:P1} reached ✓ - calibration steps up."
            : $"{record.Accuracy:P2} - target {item.TargetAccuracy:P1} ({delta:+0.0%;-0.0%}) - counted, calibration steps down.";
    }

    /// <summary>User format: [E] Easy, [N] Normal, [H] Hard, [Ex] Expert, [Ex+] Expert+.</summary>
    public static string Short(string difficulty) => difficulty switch
    {
        "ExpertPlus" => "Ex+", "Expert" => "Ex", "Hard" => "H", "Normal" => "N", "Easy" => "E", _ => difficulty,
    };
}
