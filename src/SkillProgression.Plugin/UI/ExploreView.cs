using System;
using System.Collections.Generic;
using Reactive;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using Reactive.Components.Basic;
using Reactive.Yoga;
using SkillProgression.Core.Model;
using SkillProgression.Core.Maps;
using SkillProgression.Core.Profiles;
using SkillProgression.Core.Progression;
using TMPro;
using UnityEngine;
using Label = Reactive.BeatSaber.Components.Label;

namespace SkillProgression.Plugin.UI;

/// <summary>
/// Explore side feature (user request 2026-11-10): fresh lists of maps to improve or to discover for a style, with an
/// easier/harder slider and a personal expected accuracy. Presentation only.
/// </summary>
internal class ExploreView : ReactiveComponent
{
    private static readonly Color Dim = new(0.72f, 0.72f, 0.72f);
    private static readonly Color Accent = new(0.6f, 0.85f, 1f);
    private readonly MapCardOverlay _card = new();

    private static readonly Dictionary<ExploreMode, BsSegmentedControlItem> ModeItems = new()
    {
        [ExploreMode.Improve] = new("Improve a score", null),
        [ExploreMode.NewMaps] = new("Never played", null),
    };

    private static readonly Dictionary<string, BsSegmentedControlItem> StyleItems = new()
    {
        [Destinations.Accuracy] = new("Accuracy", null),
        [Destinations.Midspeed] = new("Midspeed", null),
        [Destinations.HighTech] = new("Tech", null),
        [Destinations.Speed] = new("Speed", null),
        [Destinations.Extreme] = new("Extreme", null),
    };

    /// <summary>Training's pools plus Curated, which is Explore only (D-045).</summary>
    private static readonly Dictionary<MapPool, BsDropdownItem> PoolItems = new(TrainingView.PoolItems)
    {
        [MapPool.Curated] = new("Curated", null),
    };

    public State<ExploreMode> Mode { get; } = new(ExploreMode.Improve);
    /// <summary>The style or AccSaber track lists are built for; set it with <see cref="Select"/>.</summary>
    public State<string> Style { get; } = new(Destinations.Midspeed);
    /// <summary>Button states: styles, and AccSaber's tracks with the AccSaber pool (D-061).</summary>
    private State<string> StyleButton { get; } = new(Destinations.Midspeed);
    private State<string> TrackButton { get; } = new(AccSaberTracks.TrueAcc);
    public State<MapPool> Pool { get; } = new(MapPool.AnyRanked);
    public State<int> Offset { get; } = new(0);
    public State<IReadOnlyList<MapRow>> Rows { get; } = new(Array.Empty<MapRow>());
    public State<string> Status { get; } = new("");
    /// <summary>Candidates per slider position (D-032 B) and the range actually used (D-032 A).</summary>
    public State<string> Counts { get; } = new("");
    public State<string> Range { get; } = new("");
    public State<bool> Busy { get; } = new(false);
    public State<bool> Working { get; } = new(false);

    public Action? OnChanged { get; set; }
    public Action<MapPool>? OnPoolChanged { get; set; }
    public Action? OnNewList { get; set; }
    /// <summary>Fill the Solo menu's tab with this list (Improve or Never played) and open it (D-049).</summary>
    public Action? OnOpenInSolo { get; set; }
    public Action<MapRow>? OnPlay { get; set; }
    /// <summary>"To Solo" (D-057): play the row's map from the game's Solo menu.</summary>
    public Action<MapRow>? OnToSolo { get; set; }

    /// <summary>Selects a style or an AccSaber track (D-061) without raising <see cref="OnChanged"/>.</summary>
    public void Select(string path)
    {
        Style.Value = path;
        if (AccSaberTracks.IsTrack(path)) TrackButton.Value = path;
        else StyleButton.Value = ScoreSaberStyles.Style(path);
    }

    protected override GameObject Construct()
    {
        return new Layout
        {
            FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.Center, Gap = 1.4f.pt },
            Children =
            {
                new BsSegmentedControl<ExploreMode>
                {
                    FlexItem = { Size = new() { x = 70.pt, y = 5.5f.pt } },
                    Items = ModeItems,
                    sKey = Mode,
                    OnKeyChanged = m => { Mode.Value = m; OnChanged?.Invoke(); },
                },
                new Layout
                {
                    FlexController = { AlignItems = Align.Center, Gap = 2.pt },
                    Children =
                    {
                        new BsSegmentedControl<string>
                        {
                            sEnabled = Pool.Map(p => p != MapPool.AccSaber),
                            FlexItem = { Size = new() { x = 100.pt, y = 5.5f.pt } },
                            Items = StyleItems,
                            sKey = StyleButton,
                            // ScoreSaber pool: the same style measured on ScoreSaber (D-062).
                            OnKeyChanged = s => { Select(Pool.Value == MapPool.ScoreSaber ? ScoreSaberStyles.Id(s) : s); OnChanged?.Invoke(); },
                        },
                        new BsSegmentedControl<string>
                        {
                            sEnabled = Pool.Map(p => p == MapPool.AccSaber),
                            FlexItem = { Size = new() { x = 100.pt, y = 5.5f.pt } },
                            Items = TrainingView.TrackItems,
                            sKey = TrackButton,
                            OnKeyChanged = s => { Select(s); OnChanged?.Invoke(); },
                        },
                        // Ranked pools are shared with Training (D-041); Curated is Explore only (D-045).
                        new BsDropdown<MapPool>
                        {
                            FlexItem = { Size = new() { x = 28.pt, y = 5.5f.pt } },
                            ModalParams = new() { MaxVisibleRows = 5 },
                            Items = PoolItems,
                            sKey = Pool,
                            OnKeyChanged = p => { if (p != Pool.Value) { Pool.Value = p; OnPoolChanged?.Invoke(p); } },
                        },
                    }
                },
                new Layout
                {
                    FlexController = { AlignItems = Align.Center, Gap = 2.pt },
                    Children =
                    {
                        new BsSlider
                        {
                            FlexItem = { Size = new() { x = 60.pt, y = 5.pt } },
                            MinValue = -3f, MaxValue = 3f, ValueStep = 1f, Value = 0f,
                            ValueFormatter = v => v == 0 ? "Training level" : v < 0 ? $"Easier {-v:0}" : $"Harder {v:0}",
                            OnValueChanged = v => { Offset.Value = Mathf.RoundToInt(v); OnChanged?.Invoke(); },
                        },
                        new BsPrimaryTextButton { Text = "New list", FlexItem = { Size = new() { x = 20.pt, y = 5.5f.pt } },
                                                  sInteractable = Busy.Map(b => !b), OnClick = () => OnNewList?.Invoke() },
                        new BsPrimaryTextButton { Text = "Open in Solo", FlexItem = { Size = new() { x = 24.pt, y = 5.5f.pt } },
                                                  sInteractable = Busy.Map(b => !b), OnClick = () => OnOpenInSolo?.Invoke() },
                    }
                },
                new Label { sText = Counts, FontSize = 2.6f, Color = Dim, RichText = true },
                new Label { sText = Range, FontSize = 2.8f, Color = Accent },
                new Repeater<MapRow, Layout>
                {
                    FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.Center, Gap = 0.8f.pt },
                    SelectionMode = SelectionMode.None,
                    sItems = Rows,
                    ConstructCell = cell => new Layout
                    {
                        FlexController = { AlignItems = Align.Center, JustifyContent = Justify.Center, Gap = 2.pt },
                        FlexItem = { Size = new() { y = MapRowCell.Height.pt } },
                        Children =
                        {
                            MapRowCell.Build(cell, 74f, _card),
                            new BsPrimaryTextButton { sText = cell.Map(c => c.Item.PlayText), FlexItem = { Size = new() { x = 20.pt, y = 5.5f.pt } },
                                                      sInteractable = Busy.Map(b => !b), OnClick = () => OnPlay?.Invoke(cell.Item) },
                            new BsTextButton { Text = "To Solo", FlexItem = { Size = new() { x = 16.pt, y = 5.5f.pt } },
                                               sInteractable = Busy.Map(b => !b), OnClick = () => OnToSolo?.Invoke(cell.Item) },
                        }
                    }
                },
                _card,
                new Layout
                {
                    FlexController = { AlignItems = Align.Center, Gap = 1.pt },
                    Children =
                    {
                        new Spinner { FlexItem = { Size = 4.pt }, sEnabled = Working },
                        new Label { sText = Status, FontSize = 3.1f, Color = Accent },
                    }
                },
            }
        }.Use();
    }
}
