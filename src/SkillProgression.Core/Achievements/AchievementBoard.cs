using System;
using System.Collections.Generic;
using System.Linq;

namespace SkillProgression.Core.Achievements
{
    /// <summary>Achievement screen categories (dropdown, D-034 / D-035).</summary>
    public enum AchievementCategory
    {
        AlmostThere,
        Milestones,
        Consistency,
        PersonalRecords,
        PlatformRanks,
        PlatformPoints,
    }

    /// <summary>One line of an achievement list: unlocked (with date) or locked (optionally with progress 0..1).</summary>
    public sealed class BoardEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Unlocked { get; set; }
        public DateTime? UnlockedUtc { get; set; }
        public bool Retroactive { get; set; }
        /// <summary>Progress toward this locked achievement; null when it is not the next one in its series.</summary>
        public double? Progress { get; set; }
    }

    /// <summary>
    /// Every achievement of a category, unlocked and not, in order, with progress on the next locked one of each series.
    /// Milestones and consistency belong to the selected style; records and platforms are style-free (D-035).
    /// "Almost there" = closest locked ones of the selected style and the style-free categories.
    /// </summary>
    public static class AchievementBoard
    {
        public static bool IsPerStyle(AchievementCategory c) => c == AchievementCategory.Milestones || c == AchievementCategory.Consistency;

        /// <summary>
        /// Unlocked entries plus only the next locked one of each series (user, 2026-11-10): top 250 shows top 100,
        /// and top 50 appears once top 100 is unlocked.
        /// </summary>
        public static List<BoardEntry> Build(AchievementCategory category, string style, ProgressSnapshot? current,
            IReadOnlyList<Achievement> unlocked, IReadOnlyDictionary<string, int> milestoneCounts)
        {
            var all = BuildAll(category, style, current, unlocked, milestoneCounts);
            // The next one is the locked entry carrying progress; otherwise the first locked one of the series.
            var next = all.Where(e => !e.Unlocked).GroupBy(e => Series(e.Id))
                .Select(g => g.FirstOrDefault(e => e.Progress.HasValue) ?? g.First()).ToList();
            return all.Where(e => e.Unlocked || next.Contains(e)).ToList();
        }

        /// <summary>Series of an id: everything before the tier ("platform|BeatLeader|250" → "platform|BeatLeader").</summary>
        private static string Series(string id) => id.Substring(0, Math.Max(0, id.LastIndexOf('|')));

        /// <summary>Every achievement of a category, unlocked and locked, in order.</summary>
        public static List<BoardEntry> BuildAll(AchievementCategory category, string style, ProgressSnapshot? current,
            IReadOnlyList<Achievement> unlocked, IReadOnlyDictionary<string, int> milestoneCounts)
        {
            var byId = unlocked.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
            BoardEntry Entry(string id, string title, double? progress = null)
            {
                var e = new BoardEntry { Id = id, Title = title };
                if (byId.TryGetValue(id, out var a))
                {
                    e.Unlocked = true;
                    e.UnlockedUtc = a.UnlockedUtc;
                    e.Retroactive = a.Retroactive;
                }
                else e.Progress = progress;
                return e;
            }
            PathSnapshot? path = null;
            current?.Paths.TryGetValue(style, out path);

            var list = new List<BoardEntry>();
            switch (category)
            {
                case AchievementCategory.AlmostThere:
                    return Enum.GetValues(typeof(AchievementCategory)).Cast<AchievementCategory>()
                        .Where(c => c != AchievementCategory.AlmostThere)
                        .SelectMany(c => Build(c, style, current, unlocked, milestoneCounts))
                        .Where(e => !e.Unlocked && e.Progress.HasValue)
                        .OrderByDescending(e => e.Progress).Take(6).ToList();

                case AchievementCategory.Milestones:
                    if (milestoneCounts.TryGetValue(style, out var count))
                        for (var m = 2; m <= count; m++)
                            list.Add(Entry($"milestone|{style}|{m}", $"{style} milestone {m}",
                                path != null && m == path.Milestone + 1 ? path.Progress : (double?)null));
                    return list;

                case AchievementCategory.Consistency:
                {
                    var week = path?.WeeklyMapsAtLevel ?? 0;
                    var next = true;
                    var below = 0;
                    foreach (var tier in AchievementEngine.ConsistencyTiers)
                    {
                        var id = $"consistency|{style}|{tier}";
                        double? progress = null;
                        if (!byId.ContainsKey(id) && next)
                        {
                            progress = Clamp((double)(week - below) / (tier - below));
                            next = false;
                        }
                        list.Add(Entry(id, AchievementEngine.ConsistencyTitle(style, tier), progress));
                        below = tier;
                    }
                    return list;
                }

                case AchievementCategory.PersonalRecords:
                    // A clear is done or not: no progress bar (user, 2026-11-10). Locked tiers start above the hardest
                    // clear, so the next one shown is the first tier not cleared yet.
                    foreach (var axis in RecordAxes.All)
                    {
                        var hardest = current != null && current.Records.TryGetValue(axis.Key, out var h) ? h : 0;
                        for (var tier = 1; tier <= axis.Max; tier++)
                        {
                            var id = $"best|{axis.Key}|{tier}";
                            if (byId.ContainsKey(id) || tier > hardest)
                                list.Add(Entry(id, AchievementEngine.RecordTitle(axis.Key, tier)));
                        }
                    }
                    return list;

                case AchievementCategory.PlatformRanks:
                    foreach (var r in current?.Platforms ?? new List<PlatformRank>())
                    {
                        var next = AchievementEngine.RankThresholds.Where(th => th < r.Rank).DefaultIfEmpty(0).Max();
                        var above = AchievementEngine.RankThresholds.Where(th => th >= r.Rank).DefaultIfEmpty(r.Rank * 2).Min();
                        foreach (var th in AchievementEngine.RankThresholds)
                            list.Add(Entry($"platform|{r.Name}|{th}", $"{r.Name} top {th:N0}",
                                th == next && r.Rank > 0 ? Clamp((double)(above - r.Rank) / Math.Max(1, above - next)) : (double?)null));
                    }
                    return list;

                case AchievementCategory.PlatformPoints:
                    foreach (var r in current?.Platforms ?? new List<PlatformRank>())
                    {
                        var thresholds = AchievementEngine.PointThresholds(r);
                        var next = thresholds.Where(th => th > r.Points).DefaultIfEmpty(0).Min();
                        var below = thresholds.Where(th => th <= r.Points).DefaultIfEmpty(0).Max();
                        foreach (var th in thresholds)
                            list.Add(Entry($"points|{r.Name}|{th}", $"{r.Name} {th:N0} {r.Unit}",
                                th == next ? Clamp((r.Points - below) / Math.Max(1, next - below)) : (double?)null));
                    }
                    return list;
            }
            return list;
        }

        /// <summary>Locked progress never reaches 1: a full bar would read as completed.</summary>
        private static double Clamp(double v) => v < 0 ? 0 : v > 0.99 ? 0.99 : v;
    }
}
