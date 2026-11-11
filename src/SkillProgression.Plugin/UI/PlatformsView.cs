using System;
using System.Collections.Generic;
using Reactive;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using Reactive.Components.Basic;
using Reactive.Yoga;
using TMPro;
using UnityEngine;
using Label = Reactive.BeatSaber.Components.Label;

namespace SkillProgression.Plugin.UI;

/// <summary>One platforms-panel row: name left, rank centred, pp/AP right (user layout, 2026-11-10).</summary>
internal sealed record PlatformRow(string Name, string Rank, string Points, bool Header);

/// <summary>Right panel: official progress on BeatLeader, ScoreSaber and AccSaber (D-003, D-029). Presentation only.</summary>
internal class PlatformsView : ReactiveComponent
{
    private const float NameWidth = 30f, RankWidth = 16f, PointsWidth = 22f;
    private static readonly Color Dim = new(0.72f, 0.72f, 0.72f);

    public State<string> StyleMix { get; } = new("");
    public State<string> Message { get; } = new("Loading platforms");
    public State<IReadOnlyList<PlatformRow>> Rows { get; } = new(Array.Empty<PlatformRow>());

    protected override GameObject Construct()
    {
        return new Layout
        {
            FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.FlexStart, AlignItems = Align.Center, Gap = 2f.pt,
                               Padding = new() { top = 2.pt } },
            Children =
            {
                new SectionHeader { Text = "YOUR STYLE", Width = 62f, sEnabled = StyleMix.Map(s => s.Length > 0) },
                new Label { sText = StyleMix, FontSize = 3.2f, Alignment = TextAlignmentOptions.Center, sEnabled = StyleMix.Map(s => s.Length > 0) },
                new SectionHeader { Text = "PLATFORMS", Width = 62f },
                new Label { sText = Message, FontSize = 3f, Color = Dim, sEnabled = Message.Map(m => m.Length > 0) },
                new Repeater<PlatformRow, Layout>
                {
                    FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.Center, Gap = 0.8f.pt },
                    SelectionMode = SelectionMode.None,
                    sItems = Rows,
                    ConstructCell = cell => new Layout
                    {
                        FlexItem = { Size = new() { x = (NameWidth + RankWidth + PointsWidth).pt, y = 4.pt } },
                        FlexController = { AlignItems = Align.Center },
                        Children =
                        {
                            new Label { sText = cell.Map(c => c.Item.Name), FontSize = 3.1f, Alignment = TextAlignmentOptions.Left,
                                        sFontStyle = cell.Map(c => c.Item.Header ? FontStyles.Bold : FontStyles.Normal),
                                        FlexItem = { Size = new() { x = NameWidth.pt, y = 4.pt } } },
                            new Label { sText = cell.Map(c => c.Item.Rank), FontSize = 3.1f, Alignment = TextAlignmentOptions.Center,
                                        sFontStyle = cell.Map(c => c.Item.Header ? FontStyles.Bold : FontStyles.Normal),
                                        FlexItem = { Size = new() { x = RankWidth.pt, y = 4.pt } } },
                            new Label { sText = cell.Map(c => c.Item.Points), FontSize = 3.1f, Alignment = TextAlignmentOptions.Right,
                                        sFontStyle = cell.Map(c => c.Item.Header ? FontStyles.Bold : FontStyles.Normal),
                                        FlexItem = { Size = new() { x = PointsWidth.pt, y = 4.pt } } },
                        }
                    }
                },
            }
        }.Use();
    }
}
