using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SurfaceScanOverlayGraphic : MaskableGraphic
{
    float pulseProgress;
    float pulseStrength;

    public void SetPulse(float progress, float strength)
    {
        progress = Mathf.Clamp01(progress);
        strength = Mathf.Clamp01(strength);
        if (Mathf.Approximately(pulseProgress, progress)
            && Mathf.Approximately(pulseStrength, strength))
        {
            return;
        }

        pulseProgress = progress;
        pulseStrength = strength;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        if (pulseStrength <= 0.001f)
            return;

        Rect rect = rectTransform.rect;
        Color32 veil = new Color(0f, 0.08f, 0.11f, pulseStrength * 0.16f);
        AddQuad(
            helper,
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.xMax, rect.yMax),
            veil);

        float maximumRadius = Mathf.Sqrt(
            rect.width * rect.width + rect.height * rect.height) * 0.55f;
        float radius = Mathf.Lerp(24f, maximumRadius, pulseProgress);
        Color32 cyan = new Color(0f, 0.9f, 1f, pulseStrength);
        AddRing(helper, Vector2.zero, radius, radius + 4f, cyan, 96);
        AddRing(
            helper,
            Vector2.zero,
            Mathf.Max(8f, radius - 18f),
            Mathf.Max(10f, radius - 15f),
            new Color(0f, 0.65f, 0.78f, pulseStrength * 0.34f),
            96);

        float tickLength = 26f + pulseProgress * 18f;
        float tickOffset = 34f;
        AddLine(
            helper,
            new Vector2(-tickOffset - tickLength, 0f),
            new Vector2(-tickOffset, 0f),
            2f,
            cyan);
        AddLine(
            helper,
            new Vector2(tickOffset, 0f),
            new Vector2(tickOffset + tickLength, 0f),
            2f,
            cyan);
        AddLine(
            helper,
            new Vector2(0f, -tickOffset - tickLength),
            new Vector2(0f, -tickOffset),
            2f,
            cyan);
        AddLine(
            helper,
            new Vector2(0f, tickOffset),
            new Vector2(0f, tickOffset + tickLength),
            2f,
            cyan);
    }

    static void AddQuad(
        VertexHelper helper,
        Vector2 minimum,
        Vector2 maximum,
        Color32 color)
    {
        int start = helper.currentVertCount;
        helper.AddVert(new Vector2(minimum.x, minimum.y), color, Vector2.zero);
        helper.AddVert(new Vector2(minimum.x, maximum.y), color, Vector2.zero);
        helper.AddVert(new Vector2(maximum.x, maximum.y), color, Vector2.zero);
        helper.AddVert(new Vector2(maximum.x, minimum.y), color, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
        helper.AddTriangle(start, start + 2, start + 3);
    }

    static void AddRing(
        VertexHelper helper,
        Vector2 center,
        float inner,
        float outer,
        Color32 color,
        int steps)
    {
        int start = helper.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float angle = i / (float)steps * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            helper.AddVert(center + direction * inner, color, Vector2.zero);
            helper.AddVert(center + direction * outer, color, Vector2.one);
        }
        for (int i = 0; i < steps; i++)
        {
            int index = start + i * 2;
            helper.AddTriangle(index, index + 2, index + 1);
            helper.AddTriangle(index + 2, index + 3, index + 1);
        }
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
