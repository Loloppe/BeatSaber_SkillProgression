using System;
using System.Linq;
using Reactive;
using Reactive.BeatSaber;
using Reactive.BeatSaber.Components;
using Reactive.Components;
using Reactive.Compiler;
using Reactive.Yoga;
using SkillProgression.Core.Maps;
using TMPro;
using UnityEngine;
using Background = Reactive.BeatSaber.Components.Background;
using Label = Reactive.BeatSaber.Components.Label;

namespace SkillProgression.Plugin.UI;

/// <summary>
/// One map row (D-060): cover, song + mapper, and the goal line "Reach this acc | 98.70% (500pp) → 99.20% (530pp)".
/// Everything else is on the hover card. Name is the plain song name (status lines); Title is rich text.
/// </summary>
internal sealed record MapRow(MapKey Map, string Name, string Title, string Line, string CoverUrl, string PlayText, MapCard Card);

/// <summary>Hover card content (D-060). Empty strings hide nothing but render as blank lines, so Format fills each one.</summary>
internal sealed record MapCard(string Title, string Subtitle, string Stars, string Pass, string Acc, string Tech, string Stats,
    string Values, string Notes);

/// <summary>The map part of a row (cover + text), shared by Training's Next up and Explore. Hovering it opens the card.</summary>
internal static class MapRowCell
{
    public const float Height = 7f;
    private static readonly Color Dim = new(0.72f, 0.72f, 0.72f);
    private static readonly Color Hover = new(1f, 1f, 1f, 0.08f);
    private static Material? _roundEdge;

    /// <summary>The game's rounded-corner UI material (as BeatLeader uses for covers); null falls back to square covers.</summary>
    private static Material? RoundEdge => _roundEdge ??= Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "UINoGlowRoundEdge");

    [StateGen]
    public static Background Build(CellContext<MapRow> cell, float width, MapCardOverlay card)
    {
        Background area = null!;
        return new Background
        {
            FlexItem = { Size = new() { x = width.pt, y = Height.pt } },
            FlexController = { AlignItems = Align.Center, Gap = 1.6f.pt, Padding = new() { left = 0.6f.pt, right = 0.6f.pt } },
            Sprite = BeatSaberResources.Sprites.background,
            PixelsPerUnit = 10f,
            Color = Color.clear,
            Children =
            {
                new WebImage
                {
                    FlexItem = { Size = new() { x = 6.pt, y = 6.pt } },
                    Material = RoundEdge,
                    PreserveAspect = true,
                    RaycastTarget = false,
                    sSrc = cell.Map(string? (c) => string.IsNullOrEmpty(c.Item.CoverUrl) ? null : c.Item.CoverUrl),
                },
                new Layout
                {
                    FlexItem = { Size = new() { x = (width - 9f).pt, y = Height.pt } },
                    FlexController = { FlexDirection = FlexDirection.Column, JustifyContent = Justify.Center, AlignItems = Align.FlexStart, Gap = 0.3f.pt },
                    Children =
                    {
                        new Label { sText = cell.Map(c => c.Item.Title), FontSize = 3f, Alignment = TextAlignmentOptions.Left, RichText = true,
                                    RaycastTarget = false, FlexItem = { Size = new() { x = (width - 9f).pt, y = 3.2f.pt } } },
                        new Label { sText = cell.Map(c => c.Item.Line), FontSize = 2.5f, Color = Dim, Alignment = TextAlignmentOptions.Left, RichText = true,
                                    RaycastTarget = false, FlexItem = { Size = new() { x = (width - 9f).pt, y = 2.7f.pt } } },
                    }
                },
            }
        }.Bind(ref area).WithPointerEvents(
            onEnter: h =>
            {
                area.Color = Hover;
                card.Show(cell.Item.Card, (RectTransform)h.transform);
            },
            onLeave: _ =>
            {
                area.Color = Color.clear;
                card.Hide();
            });
    }
}

/// <summary>
/// Floating map card shown while a row is hovered (D-060). An <see cref="Overlay"/> rather than a modal: modals add a
/// full-screen raycast blocker that would steal the pointer from the row and close the card at once. The card itself
/// never takes the pointer.
/// </summary>
internal class MapCardOverlay : ReactiveComponent
{
    private const float Width = 62f;
    private static readonly Color Dim = new(0.72f, 0.72f, 0.72f);

    private readonly State<MapCard> _card = new(new MapCard("", "", "", "", "", "", "", "", ""));
    private Overlay _overlay = null!;
    private Layout _content = null!;
    private RectTransform? _anchor;

    public void Show(MapCard card, RectTransform anchor)
    {
        _card.Value = card;
        _anchor = anchor;
        _overlay.IsPushed = true;
        Place();
    }

    public void Hide()
    {
        _anchor = null;
        _overlay.IsPushed = false;
    }

    private void Place()
    {
        if (_overlay.IsPushed && _anchor != null)
            PlacementTool.Place(_content.ContentTransform, _anchor, new() { Placement = RelativePlacement.BottomLeft, Offset = new(0f, -0.6f) });
    }

    protected override GameObject Construct()
    {
        return new Overlay
        {
            Enabled = false,
            CompositionAnimation = new() { DimCanvas = false },
            Animated = false,
            Children =
            {
                new Layout
                {
                    FlexController = { ConstrainHorizontal = false, ConstrainVertical = false },
                    Children =
                    {
                        new Background
                        {
                            FlexItem = { Size = new() { x = Width.pt } },
                            FlexController = { FlexDirection = FlexDirection.Column, AlignItems = Align.FlexStart, Gap = 0.9f.pt,
                                               Padding = new() { left = 2.pt, right = 2.pt, top = 1.6f.pt, bottom = 1.8f.pt } },
                            RaycastTarget = false,
                            Children =
                            {
                                Text(c => c.Title, 3.4f, Color.white, FontStyles.Bold),
                                Text(c => c.Subtitle, 2.6f, Dim),
                                new Layout
                                {
                                    FlexController = { Gap = 1.pt },
                                    Children =
                                    {
                                        Chip(c => c.Stars, new Color(0.95f, 0.75f, 0.25f)),
                                        Chip(c => c.Pass, new Color(0.35f, 0.8f, 0.45f)),
                                        Chip(c => c.Acc, new Color(0.35f, 0.65f, 1f)),
                                        Chip(c => c.Tech, new Color(0.85f, 0.45f, 0.95f)),
                                    }
                                },
                                Text(c => c.Stats, 2.6f, Color.white),
                                Text(c => c.Values, 2.6f, Color.white),
                                Text(c => c.Notes, 2.4f, Dim),
                            }
                        // Solid, not blurred: the rows behind made the blurred card hard to read (user, 2026-11-22).
                        }.AsBackground(color: new Color(0.08f, 0.08f, 0.1f, 1f)),
                    }
                }.Bind(ref _content).With(x => x.LayoutAppliedEvent += _ => Place()),
            }
        }.Bind(ref _overlay).Use();
    }

    [StateGen]
    private Label Text(Func<MapCard, string> text, float size, Color color, FontStyles style = FontStyles.Normal) => new()
    {
        sText = _card.Map(text),
        sEnabled = _card.Map(c => text(c).Length > 0),
        FontSize = size,
        FontStyle = style,
        Color = color,
        RichText = true,
        EnableWrapping = true,
        RaycastTarget = false,
        Alignment = TextAlignmentOptions.Left,
        // Full inner width, not content-sized: the values' <pos=%> column is relative to the label's width.
        FlexItem = { Size = new() { x = (Width - 4f).pt } },
    };

    /// <summary>A rating pill, e.g. "Pass 5.31"; hidden when the rating is unknown.</summary>
    [StateGen]
    private Background Chip(Func<MapCard, string> text, Color color) => new Background
    {
        sEnabled = _card.Map(c => text(c).Length > 0),
        FlexController = { Padding = new() { left = 1.2f.pt, right = 1.2f.pt, top = 0.4f.pt, bottom = 0.4f.pt } },
        RaycastTarget = false,
        Children =
        {
            new Label { sText = _card.Map(text), FontSize = 2.5f, FontStyle = FontStyles.Bold, Color = Color.white, RaycastTarget = false },
        }
    }.AsBackground(color: color with { a = 0.35f });
}
