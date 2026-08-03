using UnityEngine;
using UnityEngine.UI;

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
