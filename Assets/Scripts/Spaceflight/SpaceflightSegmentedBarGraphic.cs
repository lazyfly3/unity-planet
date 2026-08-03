using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpaceflightSegmentedBarGraphic : MaskableGraphic
{
    [SerializeField, Range(0f, 1f)] float value = 1f;
    [SerializeField, Range(4, 32)] int segmentCount = 16;
    [SerializeField, Min(0f)] float segmentGap = 2f;
    [SerializeField] Color trackColor = new Color(0.005f, 0.035f, 0.055f, 0.95f);
    [SerializeField] Color emptySegmentColor = new Color(0.025f, 0.12f, 0.17f, 0.9f);
    [SerializeField] Color fillColor = new Color(0.06f, 0.86f, 1f, 1f);
    [SerializeField] Color borderColor = new Color(0.08f, 0.52f, 0.68f, 0.95f);

    public float Value => value;

    public void Configure(int segments, float gap = 2f)
    {
        segmentCount = Mathf.Clamp(segments, 4, 32);
        segmentGap = Mathf.Max(0f, gap);
        SetVerticesDirty();
    }

    public void SetValue(float ratio, Color tint)
    {
        ratio = Mathf.Clamp01(ratio);
        if (Mathf.Abs(value - ratio) < 0.001f && fillColor == tint)
            return;
        value = ratio;
        fillColor = tint;
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        AddRect(vertexHelper, rect, trackColor);
        AddBorder(vertexHelper, rect, 1.2f, borderColor);

        Rect inner = new Rect(rect.x + 3f, rect.y + 3f, Mathf.Max(0f, rect.width - 6f), Mathf.Max(0f, rect.height - 6f));
        float totalGap = segmentGap * (segmentCount - 1);
        float width = Mathf.Max(0.5f, (inner.width - totalGap) / segmentCount);
        float filledSegments = value * segmentCount;
        for (int index = 0; index < segmentCount; index++)
        {
            Rect segment = new Rect(
                inner.x + index * (width + segmentGap),
                inner.y,
                width,
                inner.height);
            AddRect(vertexHelper, segment, emptySegmentColor);
            float localFill = Mathf.Clamp01(filledSegments - index);
            if (localFill <= 0f)
                continue;
            Rect filled = segment;
            filled.width *= localFill;
            AddRect(vertexHelper, filled, fillColor);
            Color highlight = Color.Lerp(fillColor, Color.white, 0.45f);
            highlight.a *= 0.45f;
            Rect shine = new Rect(filled.x, filled.yMax - filled.height * 0.22f, filled.width, filled.height * 0.22f);
            AddRect(vertexHelper, shine, highlight);
        }
    }

    static void AddBorder(VertexHelper helper, Rect rect, float thickness, Color tint)
    {
        AddRect(helper, new Rect(rect.xMin, rect.yMin, rect.width, thickness), tint);
        AddRect(helper, new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), tint);
        AddRect(helper, new Rect(rect.xMin, rect.yMin, thickness, rect.height), tint);
        AddRect(helper, new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), tint);
    }

    static void AddRect(VertexHelper helper, Rect rect, Color tint)
    {
        int first = helper.currentVertCount;
        AddVertex(helper, new Vector2(rect.xMin, rect.yMin), tint);
        AddVertex(helper, new Vector2(rect.xMin, rect.yMax), tint);
        AddVertex(helper, new Vector2(rect.xMax, rect.yMax), tint);
        AddVertex(helper, new Vector2(rect.xMax, rect.yMin), tint);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }

    static void AddVertex(VertexHelper helper, Vector2 position, Color tint)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = tint;
        helper.AddVert(vertex);
    }
}
