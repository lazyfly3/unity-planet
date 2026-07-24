using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SurfaceShipIndicatorGraphic : MaskableGraphic
{
    bool offscreen;

    public void SetOffscreen(bool value)
    {
        if (offscreen == value)
            return;
        offscreen = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        Color32 glow = new Color(0f, 0.9f, 1f, 0.98f);
        Color32 soft = new Color(0.35f, 0.96f, 1f, 0.55f);
        if (offscreen)
        {
            AddTriangle(
                helper,
                new Vector2(0f, 29f),
                new Vector2(-17f, -7f),
                new Vector2(17f, -7f),
                glow);
            AddLine(
                helper,
                new Vector2(-13f, -15f),
                new Vector2(0f, -5f),
                3f,
                soft);
            AddLine(
                helper,
                new Vector2(0f, -5f),
                new Vector2(13f, -15f),
                3f,
                soft);
            return;
        }

        Vector2 top = new Vector2(0f, 27f);
        Vector2 right = new Vector2(27f, 0f);
        Vector2 bottom = new Vector2(0f, -27f);
        Vector2 left = new Vector2(-27f, 0f);
        AddLine(helper, top, right, 3f, glow);
        AddLine(helper, right, bottom, 3f, glow);
        AddLine(helper, bottom, left, 3f, glow);
        AddLine(helper, left, top, 3f, glow);
        AddLine(helper, new Vector2(-8f, 0f), new Vector2(8f, 0f), 2f, soft);
        AddLine(helper, new Vector2(0f, -8f), new Vector2(0f, 8f), 2f, soft);
    }

    static void AddTriangle(
        VertexHelper helper,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Color32 color)
    {
        int start = helper.currentVertCount;
        helper.AddVert(a, color, Vector2.zero);
        helper.AddVert(b, color, Vector2.zero);
        helper.AddVert(c, color, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
    }

    static void AddLine(
        VertexHelper helper,
        Vector2 startPoint,
        Vector2 endPoint,
        float width,
        Color32 color)
    {
        Vector2 direction = (endPoint - startPoint).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
        int start = helper.currentVertCount;
        helper.AddVert(startPoint - normal, color, Vector2.zero);
        helper.AddVert(startPoint + normal, color, Vector2.zero);
        helper.AddVert(endPoint + normal, color, Vector2.zero);
        helper.AddVert(endPoint - normal, color, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
        helper.AddTriangle(start, start + 2, start + 3);
    }
}
