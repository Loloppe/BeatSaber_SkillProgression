using System;
using System.Collections.Generic;
using Reactive;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using Reactive.Components.Basic;
using Reactive.Yoga;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Model;
using SkillProgression.Core.Profiles;
using TMPro;
using UnityEngine;
using Label = Reactive.BeatSaber.Components.Label;

namespace SkillProgression.Plugin.UI;

internal enum TrainingScreen
{
    Loading,
    ChooseStart,
    Training,
    ConfirmStartChange,
    Error,
}

/// <summary>One "Next up" row as displayed.</summary>
internal sealed record NextUpRow(MapKey Map, string Title, string Detail, string PlayText);

/// <summary>
/// Training screen (D-029). Presentation only: state is pushed in by the view controller, actions go out through
/// callbacks. Text avoids ':' — Beat Saber's font renders it as a dot (user feedback 2026-11-10).
/// </summary>
internal class TrainingView : ReactiveComponent
{
    private static readonly Color Dim = new(0.72f, 0.72f, 0.72f);
    private static readonly Color Accent = new(0.6f, 0.85f, 1f);
    private const float TitleWidth = 72f;

    private static readonly Dictionary<string, BsSegmentedControlItem> FocusItems = new()
    {
        [Destinations.Accuracy] = new("Accuracy", null),
        [Destinations.Midspeed] = new("Midspeed", null),
        [Destinations.HighTech] = new("Tech", null),
        [Destinations.Speed] = new("Speed", null),
        [Destinations.Extreme] = new("Extreme", null),
    };

    public State<TrainingScreen> Screen { get; } = new(TrainingScreen.Loading);
    public State<string> Message { get; } = new("Loading");
    public State<bool> Importing { get; } = new(false);
    public State<float> ImportProgress { get; } = new(0f);
    public State<string> Focus { get; } = new(Destinations.Midspeed);
    public State<MapPool> Pool { get; } = new(MapPool.AnyRanked);

    /// <summary>Map pool choice (D-041), "Any ranked" first (the default). AccSaber applies to the Accuracy style only.</summary>
    internal static readonly Dictionary<MapPool, BsDropdownItem> PoolItems = new()
    {
        [MapPool.AnyRanked] = new("Any ranked", null),
        [MapPool.BeatLeader] = new("BeatLeader", null),
        [MapPool.ScoreSaber] = new("ScoreSaber", null),
        [MapPool.AccSaber] = new("AccSaber", null),
    };
    public State<string> Milestone { get; } = new("");
    public State<bool> HasProgress { get; } = new(false);
    public State<float> Progress { get; } = new(0f);
    public State<string> Steps { get; } = new("");
    public State<IReadOnlyList<NextUpRow>> NextUp { get; } = new(Array.Empty<NextUpRow>());
    public State<string> Status { get; } = new("");
    public State<bool> Busy { get; } = new(false);
    /// <summary>Spinner next to the status line (map download, sync).</summary>
    public State<bool> Working { get; } = new(false);
    public State<string> ConfirmText { get; } = new("");
    /// <summary>Fresh-start calibration running (D-040): shows "Skip calibration".</summary>
    public State<bool> Calibrating { get; } = new(false);

    public Action<bool>? OnChooseStart { get; set; }           // true = import from BeatLeader, false = fresh
    public Action<string>? OnFocusChanged { get; set; }
    public Action<MapPool>? OnPoolChanged { get; set; }
    public Action<NextUpRow>? OnPlay { get; set; }
    public Action<NextUpRow>? OnSkip { get; set; }
    public Action? OnRequestStartChange { get; set; }
    public Action<bool>? OnConfirmStartChange { get; set; }     // true = confirmed
    public Action? OnOpenExplore { get; set; }
    public Action? OnOpenAchievements { get; set; }
    public Action? OnSkipCalibration { get; set; }

    protected override GameObject Construct()
    {

        return new Layout
        {
            FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.Stretch },
            Children =
            {
                // Loading / import / error
                new Layout
                {
                    sEnabled = Screen.Map(s => s == TrainingScreen.Loading || s == TrainingScreen.Error),
                    FlexItem = { FlexGrow = 1 },
                    FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.Center, Gap = 2.pt },
                    Children =
                    {
                        new Spinner { FlexItem = { Size = 8.pt }, sEnabled = Screen.Map(s => s == TrainingScreen.Loading) },
                        new Label { sText = Message, FontSize = 4f, Color = Dim },
                        new ProgressBar { sProgress = ImportProgress, Width = 60f, Height = 1.8f, sEnabled = Importing },
                    }
                },

                // First run: start fresh or import (D-029)
                new Layout
                {
                    sEnabled = Screen.Map(s => s == TrainingScreen.ChooseStart),
                    FlexItem = { FlexGrow = 1 },
                    FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.Center, Gap = 2.5f.pt },
                    Children =
                    {
                        new Label { Text = "How do you want to start?", FontSize = 5.5f, FontStyle = FontStyles.Bold },
                        new Label { Text = "Import your BeatLeader scores, or start fresh (for example after a long break).", FontSize = 3.4f, Color = Dim },
                        new Layout
                        {
                            FlexController = { Gap = 3.pt },
                            Children =
                            {
                                new BsPrimaryTextButton { Text = "Import from BeatLeader", FlexItem = { Size = new() { x = 42.pt, y = 7.pt } }, OnClick = () => OnChooseStart?.Invoke(true) },
                                new BsTextButton { Text = "Start fresh", FlexItem = { Size = new() { x = 30.pt, y = 7.pt } }, OnClick = () => OnChooseStart?.Invoke(false) },
                            }
                        },
                        new Label { Text = "You can change this later.", FontSize = 2.8f, Color = Dim },
                    }
                },

                // Confirmation for changing the start mode later (D-029)
                new Layout
                {
                    sEnabled = Screen.Map(s => s == TrainingScreen.ConfirmStartChange),
                    FlexItem = { FlexGrow = 1 },
                    FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.Center, Gap = 2.5f.pt },
                    Children =
                    {
                        new Label { sText = ConfirmText, FontSize = 4f },
                        new Layout
                        {
                            FlexController = { Gap = 3.pt },
                            Children =
                            {
                                new BsPrimaryTextButton { Text = "Confirm", FlexItem = { Size = new() { x = 26.pt, y = 7.pt } }, OnClick = () => OnConfirmStartChange?.Invoke(true) },
                                new BsTextButton { Text = "Cancel", FlexItem = { Size = new() { x = 26.pt, y = 7.pt } }, OnClick = () => OnConfirmStartChange?.Invoke(false) },
                            }
                        },
                    }
                },

                // Training
                new Layout
                {
                    sEnabled = Screen.Map(s => s == TrainingScreen.Training),
                    FlexItem = { FlexGrow = 1 },
                    FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.SpaceBetween, AlignItems = Align.Center,
                                       Padding = new() { top = 1.5f.pt, bottom = 1.5f.pt } },
                    Children =
                    {
                        new Layout
                        {
                            FlexController = { AlignItems = Align.Center, Gap = 2.pt },
                            Children =
                            {
                                new BsSegmentedControl<string>
                                {
                                    FlexItem = { Size = new() { x = 100.pt, y = 5.5f.pt } },
                                    Items = FocusItems,
                                    sKey = Focus,
                                    OnKeyChanged = k => OnFocusChanged?.Invoke(k),
                                },
                                new BsDropdown<MapPool>
                                {
                                    FlexItem = { Size = new() { x = 28.pt, y = 5.5f.pt } },
                                    ModalParams = new() { MaxVisibleRows = 4 },
                                    Items = PoolItems,
                                    sKey = Pool,
                                    OnKeyChanged = p => { if (p != Pool.Value) { Pool.Value = p; OnPoolChanged?.Invoke(p); } },
                                },
                            }
                        },
                        new Label { sText = Milestone, FontSize = 3.4f },
                        new Layout
                        {
                            sEnabled = HasProgress,
                            FlexController = { AlignItems = Align.Center, Gap = 1.5f.pt },
                            Children =
                            {
                                new ProgressBar { sProgress = Progress, Width = 70f, Height = 1.4f },
                                new Label { sText = Progress.Map(p => $"{p:P0}"), FontSize = 3f, Color = Dim },
                            }
                        },
                        new Label { sText = Steps, FontSize = 3.3f, Color = Accent, Alignment = TextAlignmentOptions.Center },
                        new Repeater<NextUpRow, Layout>
                        {
                            FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.Center, Gap = 0.6f.pt },
                            SelectionMode = SelectionMode.None,
                            sItems = NextUp,
                            ConstructCell = cell => new Layout
                            {
                                FlexController = { AlignItems = Align.Center, JustifyContent = Justify.Center, Gap = 2.pt },
                                FlexItem = { Size = new() { y = 6.4f.pt } },
                                Children =
                                {
                                    new Layout
                                    {
                                        FlexItem = { Size = new() { x = TitleWidth.pt, y = 6.4f.pt } },
                                        FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.FlexStart, Gap = 0.3f.pt },
                                        Children =
                                        {
                                            new Label { sText = cell.Map(c => c.Item.Title), FontSize = 3f, Alignment = TextAlignmentOptions.Left,
                                                        FlexItem = { Size = new() { x = TitleWidth.pt, y = 3.2f.pt } } },
                                            new Label { sText = cell.Map(c => c.Item.Detail), FontSize = 2.4f, Color = Dim, Alignment = TextAlignmentOptions.Left, RichText = true,
                                                        FlexItem = { Size = new() { x = TitleWidth.pt, y = 2.6f.pt } } },
                                        }
                                    },
                                    new BsPrimaryTextButton { sText = cell.Map(c => c.Item.PlayText), FlexItem = { Size = new() { x = 20.pt, y = 5.5f.pt } },
                                                              sInteractable = Busy.Map(b => !b), OnClick = () => OnPlay?.Invoke(cell.Item) },
                                    new BsTextButton { Text = "Skip", FlexItem = { Size = new() { x = 12.pt, y = 5.5f.pt } },
                                                       sInteractable = Busy.Map(b => !b), OnClick = () => OnSkip?.Invoke(cell.Item) },
                                }
                            }
                        },
                        new Layout
                        {
                            FlexController = { AlignItems = Align.Center, Gap = 1.pt },
                            Children =
                            {
                                new Spinner { FlexItem = { Size = 4.pt }, sEnabled = Working },
                                new Label { sText = Status, FontSize = 3.2f, Color = Accent },
                            }
                        },
                        new Layout
                        {
                            FlexController = { Gap = 3.pt },
                            Children =
                            {
                                new BsTextButton { Text = "Skip calibration", FlexItem = { Size = new() { x = 30.pt, y = 5.pt } }, sEnabled = Calibrating,
                                                   sInteractable = Busy.Map(b => !b), OnClick = () => OnSkipCalibration?.Invoke() },
                                new BsTextButton { Text = "Start mode", FlexItem = { Size = new() { x = 22.pt, y = 5.pt } },
                                                   sInteractable = Busy.Map(b => !b), OnClick = () => OnRequestStartChange?.Invoke() },
                                new BsTextButton { Text = "Explore", FlexItem = { Size = new() { x = 22.pt, y = 5.pt } },
                                                   sInteractable = Busy.Map(b => !b), OnClick = () => OnOpenExplore?.Invoke() },
                                new BsTextButton { Text = "Achievements", FlexItem = { Size = new() { x = 26.pt, y = 5.pt } },
                                                   sInteractable = Busy.Map(b => !b), OnClick = () => OnOpenAchievements?.Invoke() },
                            }
                        },
                    }
                },
            }
        }.Use();
    }
}
