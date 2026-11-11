using Reactive;
using Reactive.BeatSaber;
using Reactive.Yoga;
using UnityEngine;
using Background = Reactive.BeatSaber.Components.Background;
using Image = Reactive.BeatSaber.Components.Image;

namespace SkillProgression.Plugin.UI;

/// <summary>
/// Horizontal progress bar (Reactive has none): a dim track with a fill whose width follows <see cref="Progress"/>.
/// Notes from in-game review 2026-11-10:
/// - Reactive builds a component in its constructor, so init-only inputs (e.g. passing a State) arrive too late;
///   inputs are settable properties instead, which also gives the generated binding (sProgress = state).
/// - The fill is resized rather than using Unity's "Filled" mode, which HMUI's ImageView does not render.
/// </summary>
internal class ProgressBar : ReactiveComponent
{
    private Background _track = null!;
    private Image _fill = null!;
    private float _progress, _width = 60f, _height = 1.6f;

    /// <summary>0..1.</summary>
    public float Progress
    {
        get => _progress;
        set { _progress = Mathf.Clamp01(value); Apply(); }
    }

    public float Width
    {
        get => _width;
        set { _width = value; Apply(); }
    }

    public float Height
    {
        get => _height;
        set { _height = value; Apply(); }
    }

    protected override GameObject Construct()
    {
        var root = new Background
        {
            FlexController = { JustifyContent = Justify.FlexStart, AlignItems = Align.Stretch },
            Sprite = BeatSaberResources.Sprites.rectangle,
            Color = Color.white with { a = 0.12f },
            Children =
            {
                new Image
                {
                    Sprite = BeatSaberResources.Sprites.rectangle,
                    Color = new Color(0.6f, 0.85f, 1f),
                }.Bind(ref _fill),
            }
        }.Bind(ref _track).Use();
        Apply();
        return root;
    }

    private void Apply()
    {
        if (_track == null) return;
        _track.FlexItem.Size = new() { x = _width.pt, y = _height.pt };
        _fill.FlexItem.Size = new() { x = (_width * _progress).pt, y = _height.pt };
    }
}
