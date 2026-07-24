using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum SurfaceRadialIconKind
{
    None = 0,
    Spacecraft = 1,
    Scan = 2
}

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SurfaceRadialMenuGraphic : MaskableGraphic
{
    [SerializeField] float innerRadius = 110f;
    [SerializeField] float outerRadius = 198f;
    [SerializeField] int segments = 96;

    int actionCount = 2;
    int selectedIndex = -1;
    bool[] availability = { true, true };
    SurfaceRadialIconKind[] icons =
    {
        SurfaceRadialIconKind.Spacecraft,
        SurfaceRadialIconKind.Scan
    };

    public void SetActions(
        int count,
        int selected,
        IReadOnlyList<bool> available,
        IReadOnlyList<SurfaceRadialIconKind> actionIcons)
    {
        count = Mathf.Max(0, count);
        bool changed = actionCount != count || selectedIndex != selected;
        actionCount = count;
        selectedIndex = selected;

        if (availability.Length != count)
        {
            availability = new bool[count];
            changed = true;
        }
        if (icons.Length != count)
        {
            icons = new SurfaceRadialIconKind[count];
            changed = true;
        }

        for (int i = 0; i < count; i++)
        {
            bool isAvailable = available == null
                || i >= available.Count
                || available[i];
            SurfaceRadialIconKind icon = actionIcons != null
                && i < actionIcons.Count
                ? actionIcons[i]
                : SurfaceRadialIconKind.None;
            changed |= availability[i] != isAvailable || icons[i] != icon;
            availability[i] = isAvailable;
            icons[i] = icon;
        }

        if (changed)
            SetVerticesDirty();
    }

    // Compatibility for older callers and editor tests.
    public void SetState(
        int actionIndex,
        bool isCallAvailable,
        bool isScanAvailable)
    {
        SetActions(
            2,
            actionIndex,
            new[] { isCallAvailable, isScanAvailable },
            new[]
            {
                SurfaceRadialIconKind.Spacecraft,
                SurfaceRadialIconKind.Scan
            });
    }

    public void SetState(bool isSelected, bool isAvailable)
    {
        SetState(isSelected ? 0 : -1, isAvailable, true);
    }

    public static float GetActionCenterAngle(int index, int count)
    {
        int slotCount = Mathf.Max(4, count);
        return 90f - index * (360f / slotCount);
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        if (actionCount <= 0)
            return;

        int slotCount = Mathf.Max(4, actionCount);
        float slotAngle = 360f / slotCount;
        float halfAngle = Mathf.Min(38f, slotAngle * 0.44f);

        for (int i = 0; i < actionCount; i++)
        {
            float centerAngle = GetActionCenterAngle(i, actionCount);
            bool isAvailable = availability[i];
            bool isSelected = selectedIndex == i && isAvailable;
            AddActionSector(
                helper,
                centerAngle,
                halfAngle,
                isAvailable,
                isSelected);

            Vector2 iconCenter = Direction(centerAngle) * 166f;
            Color32 iconColor = isAvailable
                ? isSelected
                    ? new Color(0.92f, 1f, 1f, 1f)
                    : new Color(0.55f, 0.9f, 0.96f, 0.88f)
                : new Color(0.32f, 0.39f, 0.41f, 0.75f);
            AddIcon(helper, icons[i], iconCenter, iconColor);
        }
    }

    void AddActionSector(
        VertexHelper helper,
        float centerDegrees,
        float halfAngle,
        bool available,
        bool selected)
    {
        Color32 fill = !available
            ? new Color(0.05f, 0.08f, 0.09f, 0.34f)
            : selected
                ? new Color(0f, 0.78f, 1f, 0.52f)
                : new Color(0f, 0.2f, 0.28f, 0.2f);
        Color32 edge = !available
            ? new Color(0.18f, 0.22f, 0.23f, 0.5f)
            : selected
                ? new Color(0.35f, 0.96f, 1f, 1f)
                : new Color(0.04f, 0.58f, 0.72f, 0.72f);
        int stepCount = Mathf.Max(
            6,
            Mathf.RoundToInt(segments * halfAngle / 180f));

        AddRing(
            helper,
            Vector2.zero,
            centerDegrees - halfAngle,
            centerDegrees + halfAngle,
            innerRadius,
            outerRadius,
            fill,
            stepCount);
        AddRing(
            helper,
            Vector2.zero,
            centerDegrees - halfAngle,
            centerDegrees + halfAngle,
            outerRadius - (selected ? 6f : 3f),
            outerRadius,
            edge,
            stepCount);
        AddRing(
            helper,
            Vector2.zero,
            centerDegrees - halfAngle,
            centerDegrees + halfAngle,
            innerRadius,
            innerRadius + (selected ? 4f : 2f),
            edge,
            stepCount);
    }

    static void AddIcon(
        VertexHelper helper,
        SurfaceRadialIconKind icon,
        Vector2 center,
        Color32 color)
    {
        switch (icon)
        {
            case SurfaceRadialIconKind.Spacecraft:
                AddShipIcon(helper, center, color);
                break;
            case SurfaceRadialIconKind.Scan:
                AddScanIcon(helper, center, color);
                break;
            case SurfaceRadialIconKind.None:
                AddRing(helper, center, 0f, 360f, 2.5f, 5f, color, 16);
                break;
        }
    }

    static Vector2 Direction(float angleDegrees)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    static void AddRing(
        VertexHelper helper,
        Vector2 center,
        float startDegrees,
        float endDegrees,
        float inner,
        float outer,
        Color32 color,
        int stepCount)
    {
        int start = helper.currentVertCount;
        for (int i = 0; i <= stepCount; i++)
        {
            float ratio = i / (float)stepCount;
            Vector2 direction = Direction(
                Mathf.Lerp(startDegrees, endDegrees, ratio));
            helper.AddVert(center + direction * inner, color, Vector2.zero);
            helper.AddVert(center + direction * outer, color, Vector2.one);
        }
        for (int i = 0; i < stepCount; i++)
        {
            int index = start + i * 2;
            helper.AddTriangle(index, index + 2, index + 1);
            helper.AddTriangle(index + 2, index + 3, index + 1);
        }
    }

    static void AddShipIcon(VertexHelper helper, Vector2 center, Color32 color)
    {
        int start = helper.currentVertCount;
        helper.AddVert(center + new Vector2(0f, 15f), color, Vector2.zero);
        helper.AddVert(center + new Vector2(-10f, -12f), color, Vector2.zero);
        helper.AddVert(center + new Vector2(0f, -7f), color, Vector2.zero);
        helper.AddVert(center + new Vector2(10f, -12f), color, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
        helper.AddTriangle(start, start + 2, start + 3);
    }

    static void AddScanIcon(VertexHelper helper, Vector2 center, Color32 color)
    {
        AddRing(helper, center, 18f, 342f, 4f, 6f, color, 16);
        AddRing(helper, center, 18f, 342f, 10f, 12f, color, 20);
        AddRing(helper, center, 22f, 118f, 16f, 18f, color, 7);

        int start = helper.currentVertCount;
        helper.AddVert(center + new Vector2(0f, -2f), color, Vector2.zero);
        helper.AddVert(center + new Vector2(19f, 6f), color, Vector2.zero);
        helper.AddVert(center + new Vector2(4f, 3f), color, Vector2.zero);
        helper.AddTriangle(start, start + 1, start + 2);
    }
}
