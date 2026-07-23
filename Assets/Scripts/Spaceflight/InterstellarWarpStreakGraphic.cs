using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InterstellarWarpStreakGraphic : Graphic
{
    const int StreakCount = 42;
    float intensity;

    public void SetIntensity(float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Abs(value - intensity) < 0.002f)
            return;
        intensity = value;
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
        if (intensity <= 0.001f)
            return;

        Rect rect = rectTransform.rect;
        Vector2 center = rect.center;
        float outerRadius = Mathf.Sqrt(rect.width * rect.width + rect.height * rect.height) * 0.56f;
        float innerRadius = Mathf.Lerp(outerRadius * 0.68f, outerRadius * 0.12f, intensity);
        Color baseColor = new Color(0.28f, 0.82f, 1f, 0.55f * intensity);
        for (int index = 0; index < StreakCount; index++)
        {
            float hash = Hash01((uint)(index + 1) * 0x9E3779B9u);
            float angle = (index + hash * 0.72f) / StreakCount * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float radialOffset = Mathf.Lerp(-0.08f, 0.08f, Hash01((uint)index * 0xA511E9B3u));
            float startRadius = innerRadius + outerRadius * radialOffset;
            float length = Mathf.Lerp(0.08f, 0.34f, hash) * outerRadius * intensity;
            float width = Mathf.Lerp(0.7f, 2.6f, Hash01((uint)index * 0x7FEB352Du)) * intensity;
            Vector2 start = center + direction * startRadius;
            Vector2 end = center + direction * Mathf.Min(outerRadius, startRadius + length);
            int first = vertexHelper.currentVertCount;
            Color transparent = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            vertexHelper.AddVert(start - normal * width, transparent, Vector2.zero);
            vertexHelper.AddVert(start + normal * width, transparent, Vector2.zero);
            vertexHelper.AddVert(end + normal * width * 0.35f, baseColor, Vector2.zero);
            vertexHelper.AddVert(end - normal * width * 0.35f, baseColor, Vector2.zero);
            vertexHelper.AddTriangle(first, first + 1, first + 2);
            vertexHelper.AddTriangle(first, first + 2, first + 3);
        }
    }

    static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 16777216f;
    }
}
