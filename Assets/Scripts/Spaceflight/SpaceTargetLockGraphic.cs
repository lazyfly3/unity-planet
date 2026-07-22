using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SpaceTargetLockGraphic : MaskableGraphic
{
    [SerializeField, Min(1f)] float lineThickness = 4f;
    [SerializeField, Min(4f)] float cornerLength = 22f;
    [SerializeField, Min(0f)] float inset = 7f;
    [SerializeField, Range(0f, 1f)] float glowAlpha = 0.2f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        Rect rect = GetPixelAdjustedRect();
        float halfWidth = Mathf.Max(1f, rect.width * 0.5f - inset);
        float halfHeight = Mathf.Max(1f, rect.height * 0.5f - inset);
        Vector2 center = rect.center;
        Vector2 top = center + Vector2.up * halfHeight;
        Vector2 right = center + Vector2.right * halfWidth;
        Vector2 bottom = center + Vector2.down * halfHeight;
        Vector2 left = center + Vector2.left * halfWidth;
        float edgeLength = Vector2.Distance(top, right);
        float edgeRatio = Mathf.Clamp01(cornerLength / Mathf.Max(0.001f, edgeLength));

        Color glow = color;
        glow.a *= glowAlpha;
        AddCorners(vertexHelper, top, right, bottom, left, edgeRatio, lineThickness * 2.5f, glow);
        AddCorners(vertexHelper, top, right, bottom, left, edgeRatio, lineThickness, color);
    }

    static void AddCorners(
        VertexHelper vertexHelper,
        Vector2 top,
        Vector2 right,
        Vector2 bottom,
        Vector2 left,
        float edgeRatio,
        float thickness,
        Color tint)
    {
        AddCorner(vertexHelper, top, right, left, edgeRatio, thickness, tint);
        AddCorner(vertexHelper, right, bottom, top, edgeRatio, thickness, tint);
        AddCorner(vertexHelper, bottom, left, right, edgeRatio, thickness, tint);
        AddCorner(vertexHelper, left, top, bottom, edgeRatio, thickness, tint);
    }

    static void AddCorner(
        VertexHelper vertexHelper,
        Vector2 corner,
        Vector2 clockwise,
        Vector2 counterClockwise,
        float edgeRatio,
        float thickness,
        Color tint)
    {
        AddLine(vertexHelper, corner, Vector2.Lerp(corner, clockwise, edgeRatio), thickness, tint);
        AddLine(vertexHelper, corner, Vector2.Lerp(corner, counterClockwise, edgeRatio), thickness, tint);
    }

    static void AddLine(
        VertexHelper vertexHelper,
        Vector2 start,
        Vector2 end,
        float thickness,
        Color tint)
    {
        Vector2 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (thickness * 0.5f);
        int index = vertexHelper.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = tint;

        vertex.position = start - normal;
        vertexHelper.AddVert(vertex);
        vertex.position = start + normal;
        vertexHelper.AddVert(vertex);
        vertex.position = end + normal;
        vertexHelper.AddVert(vertex);
        vertex.position = end - normal;
        vertexHelper.AddVert(vertex);

        vertexHelper.AddTriangle(index, index + 1, index + 2);
        vertexHelper.AddTriangle(index, index + 2, index + 3);
    }
}
