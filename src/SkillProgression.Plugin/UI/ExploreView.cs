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

internal sealed record ExploreRow(MapKey Map, string Title, string Detail, string PlayText);

/// <summary>
/// Explore side feature (user request 2026-11-10): fresh lists of maps to improve or to discover for a style, with an
/// easier/harder slider and a personal expected accuracy. Presentation only.
/// </summary>
internal class ExploreView : ReactiveComponent
{
    private static readonly Color Dim = new(0.72f, 0.72f, 0.72f);
    private static readonly Color Accent = new(0.6f, 0.85f, 1f);

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
    public State<string> Style { get; } = new(Destinations.Midspeed);
    public State<MapPool> Pool { get; } = new(MapPool.AnyRanked);
    public State<int> Offset { get; } = new(0);
    public State<IReadOnlyList<ExploreRow>> Rows { get; } = new(Array.Empty<ExploreRow>());
    public State<string> Status { get; } = new("");
    /// <summary>Candidates per slider position (D-032 B) and the range actually used (D-032 A).</summary>
    public State<string> Counts { get; } = new("");
    public State<string> Range { get; } = new("");
    public State<bool> Busy { get; } = new(false);
    public State<bool> Working { get; } = new(false);

    public Action? OnChanged { get; set; }
    public Action<MapPool>? OnPoolChanged { get; set; }
    public Action? OnNewList { get; set; }
    public Action<ExploreRow>? OnPlay { get; set; }

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
                            FlexItem = { Size = new() { x = 100.pt, y = 5.5f.pt } },
                            Items = StyleItems,
                            sKey = Style,
                            // AccSaber ranks accuracy maps only: the style is locked to Accuracy (user, 2026-11-11).
                            sInteractable = Pool.Map(p => p != MapPool.AccSaber),
                            OnKeyChanged = s => { Style.Value = s; OnChanged?.Invoke(); },
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
                    }
                },
                new Label { sText = Counts, FontSize = 2.6f, Color = Dim, RichText = true },
                new Label { sText = Range, FontSize = 2.8f, Color = Accent },
                new Repeater<ExploreRow, Layout>
                {
                    FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.Center, Gap = 0.8f.pt },
                    SelectionMode = SelectionMode.None,
                    sItems = Rows,
                    ConstructCell = cell => new Layout
                    {
                        FlexController = { AlignItems = Align.Center, JustifyContent = Justify.Center, Gap = 2.pt },
                        FlexItem = { Size = new() { y = 6.5f.pt } },
                        Children =
                        {
                            new Layout
                            {
                                FlexItem = { Size = new() { x = 72.pt, y = 6.5f.pt } },
                                FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.FlexStart, Gap = 0.3f.pt },
                                Children =
                                {
                                    new Label { sText = cell.Map(c => c.Item.Title), FontSize = 3f, Alignment = TextAlignmentOptions.Left,
                                                FlexItem = { Size = new() { x = 72.pt, y = 3.2f.pt } } },
                                    new Label { sText = cell.Map(c => c.Item.Detail), FontSize = 2.4f, Color = Dim, Alignment = TextAlignmentOptions.Left, RichText = true,
                                                FlexItem = { Size = new() { x = 72.pt, y = 2.6f.pt } } },
                                }
                            },
                            new BsPrimaryTextButton { sText = cell.Map(c => c.Item.PlayText), FlexItem = { Size = new() { x = 20.pt, y = 5.5f.pt } },
                                                      sInteractable = Busy.Map(b => !b), OnClick = () => OnPlay?.Invoke(cell.Item) },
                        }
                    }
                },
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
