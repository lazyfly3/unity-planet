using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpaceflightCockpitRadarGraphic : MaskableGraphic
{
    const int MaximumContacts = 24;
    readonly SpaceflightRadarContact[] contacts =
        new SpaceflightRadarContact[MaximumContacts];
    int contactCount;

    static readonly Color GridColor = new Color(0.08f, 0.78f, 1f, 0.28f);
    static readonly Color ForwardColor = new Color(0.2f, 0.95f, 1f, 0.85f);

    public void SetContacts(SpaceflightRadarContact[] source, int count)
    {
        int nextCount = Mathf.Clamp(count, 0, MaximumContacts);
        for (int index = 0; index < nextCount; index++)
            contacts[index] = source[index];
        contactCount = nextCount;
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.42f;

        AddRing(helper, center, radius, 48, GridColor, 2f);
        AddRing(helper, center, radius * 0.66f, 40, GridColor, 1.3f);
        AddRing(helper, center, radius * 0.33f, 28, GridColor, 1.1f);
        AddLine(helper, center + Vector2.left * radius, center + Vector2.right * radius, 1.1f, GridColor);
        AddLine(helper, center + Vector2.down * radius, center + Vector2.up * radius, 1.1f, GridColor);
        AddTriangle(
            helper,
            center + Vector2.up * (radius + 10f),
            8f,
            ForwardColor);

        for (int index = 0; index < contactCount; index++)
        {
            SpaceflightRadarContact contact = contacts[index];
            Vector3 direction = contact.localDirection.sqrMagnitude < 0.0001f
                ? Vector3.forward
                : contact.localDirection.normalized;
            float normalizedDistance = NormalizeDistance(contact);
            Vector2 projected = new Vector2(direction.x, direction.z);
            if (projected.sqrMagnitude > 1f)
                projected.Normalize();
            Vector2 position = center + projected * radius * normalizedDistance;
            Color tint = contact.selected ? Color.white : contact.color;
            float size = contact.selected ? 8f : 5.5f;
            switch (contact.kind)
            {
                case SpaceflightRadarContactKind.Combatant:
                    AddDiamond(helper, position, size, tint);
                    break;
                case SpaceflightRadarContactKind.Asteroid:
                    AddQuad(helper, position, size * 0.65f, tint);
                    break;
                default:
                    AddRing(helper, position, size, 12, tint, contact.selected ? 2.2f : 1.4f);
                    break;
            }
            if (Mathf.Abs(direction.y) > 0.16f)
            {
                Vector2 tip = position + Vector2.up * Mathf.Sign(direction.y) * 13f;
                AddLine(helper, position, tip, 1.2f, tint);
            }
        }
    }

    static float NormalizeDistance(SpaceflightRadarContact contact)
    {
        float range = contact.kind == SpaceflightRadarContactKind.Planet
            ? 260000f
            : 6000f;
        return Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0f, contact.distance) / range));
    }

    static void AddRing(
        VertexHelper helper,
        Vector2 center,
        float radius,
        int segments,
        Color tint,
        float thickness)
    {
        for (int index = 0; index < segments; index++)
        {
            float first = index / (float)segments * Mathf.PI * 2f;
            float second = (index + 1) / (float)segments * Mathf.PI * 2f;
            AddLine(
                helper,
                center + new Vector2(Mathf.Cos(first), Mathf.Sin(first)) * radius,
                center + new Vector2(Mathf.Cos(second), Mathf.Sin(second)) * radius,
                thickness,
                tint);
        }
    }

    static void AddLine(
        VertexHelper helper,
        Vector2 start,
        Vector2 end,
        float thickness,
        Color tint)
        => SpaceflightHudPanelGraphic.AddLine(helper, start, end, thickness, tint);

    static void AddQuad(VertexHelper helper, Vector2 center, float radius, Color tint)
    {
        int first = helper.currentVertCount;
        AddVertex(helper, center + new Vector2(-radius, -radius), tint);
        AddVertex(helper, center + new Vector2(-radius, radius), tint);
        AddVertex(helper, center + new Vector2(radius, radius), tint);
        AddVertex(helper, center + new Vector2(radius, -radius), tint);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }

    static void AddDiamond(VertexHelper helper, Vector2 center, float radius, Color tint)
    {
        int first = helper.currentVertCount;
        AddVertex(helper, center + Vector2.up * radius, tint);
        AddVertex(helper, center + Vector2.right * radius, tint);
        AddVertex(helper, center + Vector2.down * radius, tint);
        AddVertex(helper, center + Vector2.left * radius, tint);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }

    static void AddTriangle(VertexHelper helper, Vector2 center, float radius, Color tint)
    {
        int first = helper.currentVertCount;
        AddVertex(helper, center + Vector2.up * radius, tint);
        AddVertex(helper, center + new Vector2(radius, -radius), tint);
        AddVertex(helper, center + new Vector2(-radius, -radius), tint);
        helper.AddTriangle(first, first + 1, first + 2);
    }

    static void AddVertex(VertexHelper helper, Vector2 position, Color tint)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = tint;
        helper.AddVert(vertex);
    }
}
