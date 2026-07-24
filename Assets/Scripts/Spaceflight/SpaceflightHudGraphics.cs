using UnityEngine;
using UnityEngine.UI;

public enum SpaceflightHudIconKind
{
    Speed,
    Boost,
    FlightMode,
    Capacitor,
    Heat,
    Mount,
    Target
}

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpaceflightHudPanelGraphic : MaskableGraphic
{
    [SerializeField] Color panelColor = new Color(0.015f, 0.075f, 0.12f, 0.9f);
    [SerializeField] Color edgeColor = new Color(0.06f, 0.78f, 1f, 0.9f);
    [SerializeField] Color accentColor = new Color(1f, 0.58f, 0.12f, 0.95f);
    [SerializeField, Min(0f)] float cornerCut = 18f;
    [SerializeField, Min(0.5f)] float edgeThickness = 1.5f;

    public void Configure(Color panel, Color edge, Color accent, float cut = 18f)
    {
        panelColor = panel;
        edgeColor = edge;
        accentColor = accent;
        cornerCut = Mathf.Max(0f, cut);
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
        float cut = Mathf.Min(cornerCut, Mathf.Min(rect.width, rect.height) * 0.22f);
        Vector2[] points =
        {
            new Vector2(rect.xMin + cut, rect.yMin),
            new Vector2(rect.xMax - cut, rect.yMin),
            new Vector2(rect.xMax, rect.yMin + cut),
            new Vector2(rect.xMax, rect.yMax - cut),
            new Vector2(rect.xMax - cut, rect.yMax),
            new Vector2(rect.xMin + cut, rect.yMax),
            new Vector2(rect.xMin, rect.yMax - cut),
            new Vector2(rect.xMin, rect.yMin + cut)
        };

        Color fill = panelColor * color;
        Vector2 center = rect.center;
        int centerIndex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, center, fill);
        for (int index = 0; index < points.Length; index++)
            AddVertex(vertexHelper, points[index], fill);
        for (int index = 0; index < points.Length; index++)
            vertexHelper.AddTriangle(centerIndex, centerIndex + 1 + index, centerIndex + 1 + (index + 1) % points.Length);

        Color glow = edgeColor;
        glow.a *= 0.18f;
        for (int index = 0; index < points.Length; index++)
        {
            Vector2 start = points[index];
            Vector2 end = points[(index + 1) % points.Length];
            AddLine(vertexHelper, start, end, edgeThickness * 4f, glow);
            AddLine(vertexHelper, start, end, edgeThickness, edgeColor);
        }

        float accentStart = Mathf.Lerp(rect.xMin, rect.xMax, 0.72f);
        float accentEnd = Mathf.Lerp(rect.xMin, rect.xMax, 0.86f);
        AddLine(
            vertexHelper,
            new Vector2(accentStart, rect.yMax - edgeThickness),
            new Vector2(accentEnd, rect.yMax - edgeThickness),
            edgeThickness * 2.2f,
            accentColor);
    }

    static void AddVertex(VertexHelper helper, Vector2 position, Color tint)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = tint;
        helper.AddVert(vertex);
    }

    internal static void AddLine(
        VertexHelper helper,
        Vector2 start,
        Vector2 end,
        float thickness,
        Color tint)
    {
        Vector2 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f)
            return;
        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
        int first = helper.currentVertCount;
        AddVertex(helper, start - normal, tint);
        AddVertex(helper, start + normal, tint);
        AddVertex(helper, end + normal, tint);
        AddVertex(helper, end - normal, tint);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }
}

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

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpaceflightHudIconGraphic : MaskableGraphic
{
    [SerializeField] SpaceflightHudIconKind iconKind;
    [SerializeField, Min(0.5f)] float lineThickness = 2f;

    public void Configure(SpaceflightHudIconKind kind)
    {
        iconKind = kind;
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
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.38f;
        switch (iconKind)
        {
            case SpaceflightHudIconKind.Speed:
                AddRing(vertexHelper, center, radius, 16);
                AddLine(vertexHelper, center - Vector2.one * radius * 0.35f, center + Vector2.one * radius * 0.5f);
                AddLine(vertexHelper, center + Vector2.one * radius * 0.5f, center + new Vector2(-0.05f, 0.55f) * radius);
                break;
            case SpaceflightHudIconKind.Boost:
                for (int index = -1; index <= 1; index++)
                {
                    float x = center.x + index * radius * 0.52f;
                    AddLine(vertexHelper, new Vector2(x - radius * 0.35f, center.y - radius * 0.6f), new Vector2(x + radius * 0.2f, center.y));
                    AddLine(vertexHelper, new Vector2(x + radius * 0.2f, center.y), new Vector2(x - radius * 0.35f, center.y + radius * 0.6f));
                }
                break;
            case SpaceflightHudIconKind.FlightMode:
                AddRing(vertexHelper, center, radius, 14);
                for (int index = 0; index < 3; index++)
                {
                    float angle = index / 3f * Mathf.PI * 2f + Mathf.PI * 0.5f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    AddLine(vertexHelper, center + direction * radius * 0.35f, center + direction * radius * 1.15f);
                }
                break;
            case SpaceflightHudIconKind.Capacitor:
                AddLightning(vertexHelper, center, radius);
                break;
            case SpaceflightHudIconKind.Heat:
                AddLine(vertexHelper, center + Vector2.down * radius * 0.55f, center + Vector2.up * radius * 0.55f);
                AddRing(vertexHelper, center + Vector2.down * radius * 0.7f, radius * 0.35f, 12);
                AddRing(vertexHelper, center + Vector2.up * radius * 0.62f, radius * 0.22f, 10);
                break;
            case SpaceflightHudIconKind.Mount:
            case SpaceflightHudIconKind.Target:
                AddRing(vertexHelper, center, radius * 0.62f, 16);
                AddLine(vertexHelper, center + Vector2.left * radius, center + Vector2.left * radius * 0.45f);
                AddLine(vertexHelper, center + Vector2.right * radius, center + Vector2.right * radius * 0.45f);
                AddLine(vertexHelper, center + Vector2.up * radius, center + Vector2.up * radius * 0.45f);
                AddLine(vertexHelper, center + Vector2.down * radius, center + Vector2.down * radius * 0.45f);
                if (iconKind == SpaceflightHudIconKind.Target)
                    AddRing(vertexHelper, center, radius * 0.12f, 8);
                break;
        }
    }

    void AddRing(VertexHelper helper, Vector2 center, float radius, int segments)
    {
        for (int index = 0; index < segments; index++)
        {
            float firstAngle = index / (float)segments * Mathf.PI * 2f;
            float secondAngle = (index + 1) / (float)segments * Mathf.PI * 2f;
            AddLine(
                helper,
                center + new Vector2(Mathf.Cos(firstAngle), Mathf.Sin(firstAngle)) * radius,
                center + new Vector2(Mathf.Cos(secondAngle), Mathf.Sin(secondAngle)) * radius);
        }
    }

    void AddLine(VertexHelper helper, Vector2 start, Vector2 end)
        => SpaceflightHudPanelGraphic.AddLine(helper, start, end, lineThickness, color);

    void AddLightning(VertexHelper helper, Vector2 center, float radius)
    {
        Vector2[] points =
        {
            center + new Vector2(0.12f, 1f) * radius,
            center + new Vector2(-0.52f, 0.08f) * radius,
            center + new Vector2(-0.08f, 0.08f) * radius,
            center + new Vector2(-0.22f, -1f) * radius,
            center + new Vector2(0.58f, -0.05f) * radius,
            center + new Vector2(0.12f, -0.05f) * radius
        };
        int first = helper.currentVertCount;
        for (int index = 0; index < points.Length; index++)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = points[index];
            vertex.color = color;
            helper.AddVert(vertex);
        }
        for (int index = 1; index < points.Length - 1; index++)
            helper.AddTriangle(first, first + index, first + index + 1);
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpaceflightAimReticleGraphic : MaskableGraphic
{
    [SerializeField, Min(0.5f)] float lineThickness = 1.8f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.16f;
        for (int index = 0; index < 16; index++)
        {
            float a = index / 16f * Mathf.PI * 2f;
            float b = (index + 1) / 16f * Mathf.PI * 2f;
            AddLine(
                vertexHelper,
                center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius);
        }
        AddLine(vertexHelper, center + Vector2.left * radius * 2.8f, center + Vector2.left * radius * 1.35f);
        AddLine(vertexHelper, center + Vector2.right * radius * 1.35f, center + Vector2.right * radius * 2.8f);
        AddLine(vertexHelper, center + Vector2.up * radius * 1.35f, center + Vector2.up * radius * 2.8f);
        AddLine(vertexHelper, center + Vector2.down * radius * 2.8f, center + Vector2.down * radius * 1.35f);
    }

    void AddLine(VertexHelper helper, Vector2 start, Vector2 end)
        => SpaceflightHudPanelGraphic.AddLine(helper, start, end, lineThickness, color);
}
