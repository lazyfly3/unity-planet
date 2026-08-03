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
