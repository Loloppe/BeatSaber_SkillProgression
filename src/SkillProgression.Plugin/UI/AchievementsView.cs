using System;
using System.Collections.Generic;
using Reactive;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using Reactive.Components.Basic;
using Reactive.Yoga;
using SkillProgression.Core.Achievements;
using SkillProgression.Core.Profiles;
using TMPro;
using UnityEngine;
using Label = Reactive.BeatSaber.Components.Label;
using ScrollArea = Reactive.BeatSaber.Components.ScrollArea;

namespace SkillProgression.Plugin.UI;

/// <summary>One achievement line: unlocked (status = date / "earned before") or locked (progress bar or "locked").</summary>
internal sealed record AchievementRow(string Title, string Status, float Progress, bool ShowBar, bool Unlocked);

internal enum ProgressTab
{
    Achievements,
    History,
}

/// <summary>
/// Achievements + History screen. Achievements: style buttons (as on Training) and a type dropdown; the list shows every
/// achievement of that type, unlocked and not, in order, optionally hiding achieved ones (D-034, D-035). History: a
/// dropdown picks the chart. Presentation only.
/// </summary>
internal class AchievementsView : ReactiveComponent
{
    private static readonly Color Dim = new(0.6f, 0.6f, 0.6f);
    private static readonly Color Bright = new(0.95f, 0.95f, 0.95f);

    private static readonly Dictionary<ProgressTab, BsSegmentedControlItem> Tabs = new()
    {
        [ProgressTab.Achievements] = new("Achievements", null),
        [ProgressTab.History] = new("History", null),
    };

    private static readonly Dictionary<string, BsSegmentedControlItem> StyleItems = new()
    {
        [Destinations.Accuracy] = new("Accuracy", null),
        [Destinations.Midspeed] = new("Midspeed", null),
        [Destinations.HighTech] = new("Tech", null),
        [Destinations.Speed] = new("Speed", null),
        [Destinations.Extreme] = new("Extreme", null),
    };

    public static readonly Dictionary<AchievementCategory, BsDropdownItem> Categories = new()
    {
        [AchievementCategory.AlmostThere] = new("Almost there", null),
        [AchievementCategory.Milestones] = new("Milestones", null),
        [AchievementCategory.Consistency] = new("Consistency", null),
        [AchievementCategory.PersonalRecords] = new("Personal records", null),
        [AchievementCategory.PlatformRanks] = new("Platform ranks", null),
        [AchievementCategory.PlatformPoints] = new("Platform pp / AP", null),
    };

    public State<ProgressTab> Tab { get; } = new(ProgressTab.Achievements);
    public State<string> Style { get; } = new(Destinations.Midspeed);
    public State<AchievementCategory> Category { get; } = new(AchievementCategory.AlmostThere);
    public State<bool> HideAchieved { get; } = new(false);
    public State<IReadOnlyList<AchievementRow>> Rows { get; } = new(Array.Empty<AchievementRow>());
    public State<string> Empty { get; } = new("");
    public State<string> HistorySource { get; } = new("BeatLeader");
    public State<IReadOnlyDictionary<string, BsDropdownItem>> HistorySources { get; } = new(new Dictionary<string, BsDropdownItem>());
    public State<string> ChartCaption { get; } = new("");
    public State<string> ChartHelp { get; } = new("");
    /// <summary>History source shown as a table instead of a chart (best accuracy per star level).</summary>
    public State<bool> ShowTable { get; } = new(false);
    public State<string> TableLevels { get; } = new("");
    public State<string> TableValues { get; } = new("");
    public State<string> TableLevels2 { get; } = new("");
    public State<string> TableValues2 { get; } = new("");

    public Action? OnCategoryChanged { get; set; }
    public Action? OnHistorySourceChanged { get; set; }

    private LineChart _chart = null!;

    public IReadOnlyList<Vector2> ChartPoints { set => _chart.Points = value; }

    protected override GameObject Construct()
    {
        var scroll = new ScrollContext();
        return new Layout
        {
            FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.FlexStart, AlignItems = Align.Center, Gap = 1.5f.pt,
                               Padding = new() { top = 1.5f.pt } },
            Children =
            {
                new BsSegmentedControl<ProgressTab>
                {
                    FlexItem = { Size = new() { x = 60.pt, y = 5.5f.pt } },
                    Items = Tabs,
                    sKey = Tab,
                    OnKeyChanged = t => Tab.Value = t,
                },
                new BsSegmentedControl<string>
                {
                    sEnabled = Tab.Map(t => t == ProgressTab.Achievements),
                    FlexItem = { Size = new() { x = 100.pt, y = 5.5f.pt } },
                    Items = StyleItems,
                    sKey = Style,
                    OnKeyChanged = k => { Style.Value = k; OnCategoryChanged?.Invoke(); },
                },

                // Achievements tab
                new Layout
                {
                    sEnabled = Tab.Map(t => t == ProgressTab.Achievements),
                    FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.Center, Gap = 1.2f.pt },
                    Children =
                    {
                        new Layout
                        {
                            FlexController = { AlignItems = Align.Center, Gap = 2.pt },
                            Children =
                            {
                                new BsDropdown<AchievementCategory>
                                {
                                    FlexItem = { Size = new() { x = 46.pt, y = 5.5f.pt } },
                                    ModalParams = new() { MaxVisibleRows = 6 },
                                    Items = Categories,
                                    sKey = Category,
                                    OnKeyChanged = c => { Category.Value = c; OnCategoryChanged?.Invoke(); },
                                },
                                new BsTextButton
                                {
                                    FlexItem = { Size = new() { x = 28.pt, y = 5.5f.pt } },
                                    sText = HideAchieved.Map(h => h ? "Show achieved" : "Hide achieved"),
                                    OnClick = () => { HideAchieved.Value = !HideAchieved.Value; OnCategoryChanged?.Invoke(); },
                                },
                            }
                        },
                        new Label { sText = Empty, FontSize = 3f, Color = Dim, sEnabled = Empty.Map(s => s.Length > 0) },
                        new Layout
                        {
                            FlexController = { Gap = 1.pt },
                            Children =
                            {
                                new ScrollArea
                                {
                                    FlexItem = { Size = new() { x = 112.pt, y = 45.pt } },
                                    ScrollContext = scroll,
                                    ScrollContent = new Repeater<AchievementRow, Layout>
                                    {
                                        FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.Center, Gap = 0.6f.pt, ConstrainVertical = false },
                                        SelectionMode = SelectionMode.None,
                                        sItems = Rows,
                                        ConstructCell = cell => new Layout
                                        {
                                            FlexController = { AlignItems = Align.Center, Gap = 2.pt },
                                            FlexItem = { Size = new() { x = 108.pt, y = 4.2f.pt } },
                                            Children =
                                            {
                                                new Label
                                                {
                                                    sText = cell.Map(c => (c.Item.Unlocked ? "✓  " : "") + c.Item.Title),
                                                    sColor = cell.Map(c => c.Item.Unlocked ? Bright : Dim),
                                                    FontSize = 3f, Alignment = TextAlignmentOptions.Left,
                                                    FlexItem = { Size = new() { x = 66.pt, y = 4.pt } },
                                                },
                                                new ProgressBar { sProgress = cell.Map(c => c.Item.Progress), Width = 26f, Height = 1.4f,
                                                                  sEnabled = cell.Map(c => c.Item.ShowBar) },
                                                new Label
                                                {
                                                    sText = cell.Map(c => c.Item.Status),
                                                    sColor = cell.Map(c => c.Item.Unlocked ? Bright : Dim),
                                                    FontSize = 2.7f, Alignment = TextAlignmentOptions.Right,
                                                    // Grows into the bar's space when there is no bar.
                                                    FlexItem = { FlexGrow = 1, Size = new() { y = 4.pt } },
                                                },
                                            }
                                        }
                                    }
                                },
                                new Scrollbar { ScrollContext = scroll },
                            }
                        },
                    }
                },

                // History tab
                new Layout
                {
                    sEnabled = Tab.Map(t => t == ProgressTab.History),
                    FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.Center, Gap = 1.5f.pt },
                    Children =
                    {
                        new BsDropdown<string>
                        {
                            FlexItem = { Size = new() { x = 46.pt, y = 5.5f.pt } },
                            ModalParams = new() { MaxVisibleRows = 8 },
                            sItems = HistorySources,
                            sKey = HistorySource,
                            OnKeyChanged = s => { HistorySource.Value = s; OnHistorySourceChanged?.Invoke(); },
                        },
                        new LineChart { FlexItem = { Size = new() { x = 104.pt, y = 34.pt } }, sEnabled = ShowTable.Map(t => !t) }.Bind(ref _chart),
                        new Layout
                        {
                            sEnabled = ShowTable,
                            FlexController = { Gap = 4.pt },
                            Children =
                            {
                                new Label { sText = TableLevels, FontSize = 2.8f, Alignment = TextAlignmentOptions.Right },
                                new Label { sText = TableValues, FontSize = 2.8f, Alignment = TextAlignmentOptions.Left },
                                new Label { sText = TableLevels2, FontSize = 2.8f, Alignment = TextAlignmentOptions.Right },
                                new Label { sText = TableValues2, FontSize = 2.8f, Alignment = TextAlignmentOptions.Left },
                            }
                        },
                        new Label { sText = ChartCaption, FontSize = 3f },
                        new Label { sText = ChartHelp, FontSize = 2.6f, Color = Dim, Alignment = TextAlignmentOptions.Center },
                    }
                },
            }
        }.Use();
    }
}
