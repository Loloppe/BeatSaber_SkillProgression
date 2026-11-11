using System.Collections.Generic;
using Reactive;
using Reactive.BeatSaber;
using UnityEngine;
using UnityEngine.UI;

namespace SkillProgression.Plugin.UI;

/// <summary>
/// Minimal line chart (Reactive has no charts): a custom UI Graphic drawing a polyline as quads, like BeatLeader's
/// accuracy graph. Points are normalised 0..1 on both axes by the caller. Inputs are settable properties because
/// Reactive builds components in their constructor.
/// </summary>
internal class LineChart : ReactiveComponent
{
    private LineGraphic _graphic = null!;

    public IReadOnlyList<Vector2> Points
    {
        get => _graphic.Points;
        set => _graphic.SetPoints(value);
    }

    public Color LineColor
    {
        get => _graphic.color;
        set => _graphic.color = value;
    }

    protected override GameObject Construct()
    {
        var go = new GameObject("LineChart", typeof(RectTransform));
        _graphic = go.AddComponent<LineGraphic>();
        _graphic.material = GameResources.UINoGlowMaterial;
        _graphic.color = new Color(0.6f, 0.85f, 1f);
        _graphic.raycastTarget = false;
        return go;
    }

    private sealed class LineGraphic : MaskableGraphic
    {
        private readonly List<Vector2> _points = new();
        public IReadOnlyList<Vector2> Points => _points;
        public float Thickness = 0.35f;

        public void SetPoints(IReadOnlyList<Vector2> points)
        {
            _points.Clear();
            if (points != null) _points.AddRange(points);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_points.Count < 2) return;
            var r = rectTransform.rect;
            Vector2 ToLocal(Vector2 p) => new(r.xMin + p.x * r.width, r.yMin + p.y * r.height);

            for (var i = 0; i < _points.Count - 1; i++)
            {
                var a = ToLocal(_points[i]);
                var b = ToLocal(_points[i + 1]);
                var dir = (b - a).normalized;
                var normal = new Vector2(-dir.y, dir.x) * Thickness;
                var start = vh.currentVertCount;
                vh.AddVert(a - normal, color, Vector2.zero);
                vh.AddVert(a + normal, color, Vector2.zero);
                vh.AddVert(b + normal, color, Vector2.zero);
                vh.AddVert(b - normal, color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
