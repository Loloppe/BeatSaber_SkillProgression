using Reactive;
using Reactive.BeatSaber;
using Reactive.Yoga;
using TMPro;
using UnityEngine;
using Background = Reactive.BeatSaber.Components.Background;
using Label = Reactive.BeatSaber.Components.Label;

namespace SkillProgression.Plugin.UI;

/// <summary>
/// Section title on a skewed, fading teal/blue bar, in the style of the game's title bar (user request 2026-11-10).
/// Text is a settable property (Reactive builds in the constructor, see ProgressBar).
/// </summary>
internal class SectionHeader : ReactiveComponent
{
    private static readonly Color Teal = new(0.05f, 0.62f, 0.95f, 1f);

    private Label _label = null!;
    private Background _bar = null!;
    private float _width = 60f;

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public float Width
    {
        get => _width;
        set { _width = value; _bar.FlexItem.Size = new() { x = _width.pt, y = 6.pt }; }
    }

    protected override GameObject Construct()
    {
        return new Background
        {
            FlexController = { JustifyContent = Justify.Center, AlignItems = Align.Center },
            FlexItem = { Size = new() { x = _width.pt, y = 6.pt } },
            Sprite = BeatSaberResources.Sprites.background,
            PixelsPerUnit = 10f,
            Skew = BeatSaberStyle.Skew,
            Color = Color.white,
            UseGradient = true,
            GradientDirection = HMUI.ImageView.GradientDirection.Horizontal,
            GradientColor0 = Teal,
            GradientColor1 = Teal with { a = 0.1f },
            Children =
            {
                new Label { FontSize = 4.2f, FontStyle = FontStyles.Bold | FontStyles.Italic, Alignment = TextAlignmentOptions.Center }
                    .Bind(ref _label),
            }
        }.Bind(ref _bar).Use();
    }
}
