using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Screen-space presentation for the three facilities in a formal facility
/// assault mission. This component deliberately owns presentation only: it
/// never raycasts, changes an objective, or writes anything into the scene.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class FinitePlanetAssaultObjectiveHud : MonoBehaviour
{
    public const int MaximumFacilityCount = 3;
    public const string ObjectiveIconResourcePath =
        "UI/FacilityAssault/FacilityObjectiveIcon";

    const float CompletedSummarySeconds = 2.5f;
    const float HorizontalSafeMarginPixels = 88f;
    const float BottomSafeMarginPixels = 96f;
    const float TopSafeMarginPixels = 142f;
    const float MinimumSafeViewportSize = 0.08f;

    readonly FacilitySlot[] slots = new FacilitySlot[MaximumFacilityCount]
    {
        new FacilitySlot(),
        new FacilitySlot(),
        new FacilitySlot()
    };

    Camera worldCamera;
    Rigidbody playerBody;
    Transform playerTransform;
    Texture2D objectiveIcon;
    bool configured;
    bool presentationVisible;
    bool completionTimerStarted;
    float hideCompletedSummaryAt;

    GUISkin cachedSkin;
    int cachedStyleScaleKey = -1;
    GUIStyle panelStyle;
    GUIStyle titleStyle;
    GUIStyle activeStatusStyle;
    GUIStyle completedStatusStyle;
    GUIStyle missingStatusStyle;
    GUIStyle markerStyle;
    GUIStyle markerShadowStyle;
    GUIStyle arrowStyle;
    GUIStyle fallbackIconStyle;

    public bool IsConfigured => configured;
    public bool PresentationVisible => presentationVisible;
    public int FacilityCount { get; private set; }

    sealed class FacilitySlot
    {
        public FinitePlanetFacilityAssaultObjective objective;
        public FinitePlanetAssaultHudProjection projection;
        public float distance;
    }

    /// <summary>
    /// Result of the pure viewport projection policy. viewportAnchor uses
    /// Unity viewport coordinates: bottom-left is (0, 0), top-right is (1, 1).
    /// </summary>
    public readonly struct FinitePlanetAssaultHudProjection
    {
        public FinitePlanetAssaultHudProjection(
            bool onScreen,
            bool behindCamera,
            Vector2 viewportAnchor,
            Vector2 outwardDirection)
        {
            this.onScreen = onScreen;
            this.behindCamera = behindCamera;
            this.viewportAnchor = viewportAnchor;
            this.outwardDirection = outwardDirection;
        }

        public readonly bool onScreen;
        public readonly bool behindCamera;
        public readonly Vector2 viewportAnchor;
        public readonly Vector2 outwardDirection;
    }

    void Awake()
    {
        LoadObjectiveIcon();
    }

    /// <summary>
    /// Connects the HUD to its camera, player and objectives. Passing the
    /// player Rigidbody preserves an accurate centre-of-mass distance.
    /// </summary>
    public void Configure(
        Camera targetCamera,
        Rigidbody targetPlayerBody,
        IReadOnlyList<FinitePlanetFacilityAssaultObjective> objectives)
    {
        worldCamera = targetCamera;
        playerBody = targetPlayerBody;
        playerTransform = targetPlayerBody != null
            ? targetPlayerBody.transform
            : null;
        ConfigureObjectives(objectives);
    }

    /// <summary>
    /// Transform overload for lightweight previews and focused tests.
    /// </summary>
    public void Configure(
        Camera targetCamera,
        Transform targetPlayer,
        IReadOnlyList<FinitePlanetFacilityAssaultObjective> objectives)
    {
        worldCamera = targetCamera;
        playerBody = null;
        playerTransform = targetPlayer;
        ConfigureObjectives(objectives);
    }

    public void SetVisible(bool value)
    {
        presentationVisible = value && configured;
    }

    void ConfigureObjectives(
        IReadOnlyList<FinitePlanetFacilityAssaultObjective> objectives)
    {
        for (int index = 0; index < slots.Length; index++)
        {
            slots[index].objective = null;
            slots[index].projection = default;
            slots[index].distance = 0f;
        }

        FacilityCount = 0;
        if (objectives != null)
        {
            for (int sourceIndex = 0;
                 sourceIndex < objectives.Count &&
                 FacilityCount < MaximumFacilityCount;
                 sourceIndex++)
            {
                FinitePlanetFacilityAssaultObjective objective =
                    objectives[sourceIndex];
                if (objective == null || ContainsObjective(objective))
                    continue;

                int preferredSlot = objective.ObjectiveIndex;
                int destination = preferredSlot >= 0 &&
                                  preferredSlot < MaximumFacilityCount &&
                                  slots[preferredSlot].objective == null
                    ? preferredSlot
                    : FirstEmptySlot();
                if (destination < 0)
                    break;
                slots[destination].objective = objective;
                FacilityCount++;
            }
        }

        LoadObjectiveIcon();
        configured = worldCamera != null && FacilityCount > 0;
        presentationVisible = configured;
        completionTimerStarted = false;
        hideCompletedSummaryAt = 0f;
        enabled = configured;
    }

    bool ContainsObjective(FinitePlanetFacilityAssaultObjective objective)
    {
        for (int index = 0; index < slots.Length; index++)
        {
            if (slots[index].objective == objective)
                return true;
        }
        return false;
    }

    int FirstEmptySlot()
    {
        for (int index = 0; index < slots.Length; index++)
        {
            if (slots[index].objective == null)
                return index;
        }
        return -1;
    }

    void LoadObjectiveIcon()
    {
        if (objectiveIcon != null)
            return;
        objectiveIcon = Resources.Load<Texture2D>(ObjectiveIconResourcePath);
    }

    void LateUpdate()
    {
        if (!configured || !presentationVisible || worldCamera == null)
            return;

        Rect safeViewport = CalculateRuntimeSafeViewport();
        Vector3 distanceOrigin = playerBody != null
            ? playerBody.worldCenterOfMass
            : playerTransform != null
                ? playerTransform.position
                : worldCamera.transform.position;
        bool allDestroyed = FacilityCount > 0;
        for (int index = 0; index < slots.Length; index++)
        {
            FacilitySlot slot = slots[index];
            FinitePlanetFacilityAssaultObjective objective = slot.objective;
            if (objective == null)
                continue;

            if (!objective.IsDestroyed)
            {
                allDestroyed = false;
                Vector3 markerPosition = objective.MarkerWorldPosition;
                Vector3 viewport = worldCamera.WorldToViewportPoint(
                    markerPosition);
                slot.projection = ProjectViewportPoint(
                    viewport,
                    safeViewport);
                slot.distance = Vector3.Distance(
                    distanceOrigin,
                    markerPosition);
            }
        }

        if (!allDestroyed)
        {
            completionTimerStarted = false;
            hideCompletedSummaryAt = 0f;
            return;
        }

        if (!completionTimerStarted)
        {
            completionTimerStarted = true;
            hideCompletedSummaryAt =
                Time.unscaledTime + CompletedSummarySeconds;
        }
        else if (Time.unscaledTime >= hideCompletedSummaryAt)
        {
            presentationVisible = false;
        }
    }

    void OnGUI()
    {
        if (!configured || !presentationVisible || worldCamera == null)
            return;

        EnsureStyles();
        if (panelStyle == null)
            return;

        float scale = CalculateUiScale();
        DrawFacilitySummary(scale);
        for (int index = 0; index < slots.Length; index++)
        {
            FacilitySlot slot = slots[index];
            if (slot.objective == null || slot.objective.IsDestroyed)
                continue;
            DrawFacilityMarker(index, slot, scale);
        }
    }

    void DrawFacilitySummary(float scale)
    {
        float panelWidth = 660f * scale;
        float panelHeight = 86f * scale;
        Rect panel = new Rect(
            Screen.width - panelWidth - 18f * scale,
            18f * scale,
            panelWidth,
            panelHeight);
        GUI.Box(panel, GUIContent.none, panelStyle);

        Rect title = new Rect(
            panel.x + 12f * scale,
            panel.y + 5f * scale,
            panel.width - 24f * scale,
            25f * scale);
        GUI.Label(
            title,
            "设备突袭　摧毁模块外壳并击穿核心",
            titleStyle);

        float gap = 6f * scale;
        float innerWidth = panel.width - 24f * scale;
        float statusWidth =
            (innerWidth - gap * (MaximumFacilityCount - 1)) /
            MaximumFacilityCount;
        float statusY = panel.y + 34f * scale;
        for (int index = 0; index < MaximumFacilityCount; index++)
        {
            FinitePlanetFacilityAssaultObjective objective =
                slots[index].objective;
            string objectiveStatus = objective != null
                ? objective.StatusLabel
                : string.Empty;
            string state = objective == null
                ? $"设施 {index + 1}　未部署"
                : objective.IsDestroyed
                    ? $"设施 {index + 1}　已摧毁"
                    : string.IsNullOrWhiteSpace(objectiveStatus)
                        ? $"设施 {index + 1}　目标完整"
                        : objectiveStatus;
            GUIStyle style = objective == null
                ? missingStatusStyle
                : objective.IsDestroyed
                    ? completedStatusStyle
                    : activeStatusStyle;
            Rect statusRect = new Rect(
                panel.x + 12f * scale + index * (statusWidth + gap),
                statusY,
                statusWidth,
                42f * scale);
            GUI.Box(statusRect, GUIContent.none, panelStyle);
            GUI.Label(
                statusRect,
                state,
                style);
        }
    }

    void DrawFacilityMarker(
        int slotIndex,
        FacilitySlot slot,
        float scale)
    {
        Vector2 anchor = ViewportToGui(slot.projection.viewportAnchor);
        string distance = FormatDistance(slot.distance);
        string label = $"设施 {slotIndex + 1}　{distance}";
        if (slot.projection.behindCamera)
            label = $"设施 {slotIndex + 1}　后方　{distance}";

        if (slot.projection.onScreen)
        {
            float iconSize = 58f * scale;
            Rect iconRect = new Rect(
                anchor.x - iconSize * 0.5f,
                anchor.y - iconSize * 0.5f,
                iconSize,
                iconSize);
            DrawObjectiveIcon(iconRect);
            DrawOutlinedLabel(
                new Rect(
                    anchor.x - 105f * scale,
                    anchor.y + 32f * scale,
                    210f * scale,
                    30f * scale),
                label);
            return;
        }

        Vector2 guiDirection = new Vector2(
            slot.projection.outwardDirection.x,
            -slot.projection.outwardDirection.y);
        if (guiDirection.sqrMagnitude < 0.0001f)
            guiDirection = Vector2.up;
        guiDirection.Normalize();

        float arrowSize = 38f * scale;
        Rect arrowRect = new Rect(
            anchor.x - arrowSize * 0.5f,
            anchor.y - arrowSize * 0.5f,
            arrowSize,
            arrowSize);
        Matrix4x4 previousMatrix = GUI.matrix;
        float guiAngle = Mathf.Atan2(
                             guiDirection.y,
                             guiDirection.x) * Mathf.Rad2Deg + 90f;
        GUIUtility.RotateAroundPivot(guiAngle, anchor);
        GUI.Label(arrowRect, "▲", arrowStyle);
        GUI.matrix = previousMatrix;

        Vector2 inward = -guiDirection;
        Vector2 iconCenter = anchor + inward * (42f * scale);
        float iconSizeAtEdge = 32f * scale;
        DrawObjectiveIcon(new Rect(
            iconCenter.x - iconSizeAtEdge * 0.5f,
            iconCenter.y - iconSizeAtEdge * 0.5f,
            iconSizeAtEdge,
            iconSizeAtEdge));

        Vector2 labelCenter = anchor + inward * (110f * scale);
        DrawOutlinedLabel(
            new Rect(
                labelCenter.x - 110f * scale,
                labelCenter.y - 14f * scale,
                220f * scale,
                30f * scale),
            label);
    }

    void DrawObjectiveIcon(Rect rect)
    {
        if (objectiveIcon != null)
        {
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(
                rect,
                objectiveIcon,
                ScaleMode.ScaleToFit,
                true);
            GUI.color = previous;
            return;
        }
        GUI.Label(rect, "◆", fallbackIconStyle);
    }

    void DrawOutlinedLabel(Rect rect, string value)
    {
        Rect shadow = rect;
        shadow.x += 1f;
        shadow.y += 2f;
        GUI.Label(shadow, value, markerShadowStyle);
        GUI.Label(rect, value, markerStyle);
    }

    void EnsureStyles()
    {
        GUISkin skin = GUI.skin;
        int scaleKey = Mathf.RoundToInt(CalculateUiScale() * 100f);
        if (skin == cachedSkin && scaleKey == cachedStyleScaleKey &&
            panelStyle != null)
        {
            return;
        }

        cachedSkin = skin;
        cachedStyleScaleKey = scaleKey;
        float scale = CalculateUiScale();
        panelStyle = new GUIStyle(skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 5, 5)
        };
        titleStyle = new GUIStyle(skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(18f * scale),
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.82f, 0.97f, 1f, 1f) }
        };
        activeStatusStyle = CreateStatusStyle(
            skin,
            scale,
            new Color(1f, 0.58f, 0.12f, 1f));
        completedStatusStyle = CreateStatusStyle(
            skin,
            scale,
            new Color(0.3f, 1f, 0.62f, 1f));
        missingStatusStyle = CreateStatusStyle(
            skin,
            scale,
            new Color(0.55f, 0.62f, 0.66f, 1f));
        markerStyle = new GUIStyle(skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(17f * scale),
            fontStyle = FontStyle.Bold,
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(1f, 0.72f, 0.25f, 1f) }
        };
        markerShadowStyle = new GUIStyle(markerStyle)
        {
            normal = { textColor = new Color(0.03f, 0.015f, 0f, 0.95f) }
        };
        arrowStyle = new GUIStyle(skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(30f * scale),
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.36f, 0.08f, 1f) }
        };
        fallbackIconStyle = new GUIStyle(skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(30f * scale),
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.12f, 0.88f, 1f, 1f) }
        };
    }

    static GUIStyle CreateStatusStyle(
        GUISkin skin,
        float scale,
        Color color)
    {
        return new GUIStyle(skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(15f * scale),
            fontStyle = FontStyle.Bold,
            wordWrap = true,
            clipping = TextClipping.Clip,
            normal = { textColor = color }
        };
    }

    static float CalculateUiScale()
    {
        if (Screen.width <= 0 || Screen.height <= 0)
            return 1f;
        return Mathf.Clamp(
            Mathf.Min(Screen.width / 1920f, Screen.height / 1080f),
            0.72f,
            1.4f);
    }

    static Rect CalculateRuntimeSafeViewport()
    {
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);
        Rect safeArea = Screen.safeArea;
        if (safeArea.width <= 1f || safeArea.height <= 1f)
            safeArea = new Rect(0f, 0f, width, height);
        Rect normalized = new Rect(
            safeArea.xMin / width,
            safeArea.yMin / height,
            safeArea.width / width,
            safeArea.height / height);
        float left = normalized.xMin + HorizontalSafeMarginPixels / width;
        float right = normalized.xMax - HorizontalSafeMarginPixels / width;
        float bottom = normalized.yMin + BottomSafeMarginPixels / height;
        float top = normalized.yMax - TopSafeMarginPixels / height;
        return NormalizeSafeViewport(new Rect(
            left,
            bottom,
            right - left,
            top - bottom));
    }

    static Vector2 ViewportToGui(Vector2 viewport)
    {
        return new Vector2(
            viewport.x * Screen.width,
            (1f - viewport.y) * Screen.height);
    }

    static string FormatDistance(float distance)
    {
        float safeDistance = Mathf.Max(0f, distance);
        if (safeDistance >= 1000f)
            return (safeDistance / 1000f).ToString("0.0") + " 千米";
        int rounded = Mathf.RoundToInt(safeDistance / 5f) * 5;
        return rounded + " 米";
    }

    /// <summary>
    /// Pure projection/clamping policy used by the runtime and focused tests.
    /// No Camera, Physics, Screen or scene state is accessed here.
    /// </summary>
    public static FinitePlanetAssaultHudProjection ProjectViewportPoint(
        Vector3 viewportPoint,
        Rect safeViewport)
    {
        Rect safe = NormalizeSafeViewport(safeViewport);
        Vector2 point = new Vector2(viewportPoint.x, viewportPoint.y);
        bool behind = viewportPoint.z <= 0f;
        bool onScreen = !behind && safe.Contains(point);
        Vector2 center = safe.center;
        Vector2 direction = point - center;
        if (behind)
            direction = -direction;
        if (direction.sqrMagnitude < 0.000001f)
            direction = Vector2.down;
        direction.Normalize();

        if (onScreen)
        {
            return new FinitePlanetAssaultHudProjection(
                true,
                false,
                point,
                direction);
        }

        float horizontalScale = float.PositiveInfinity;
        if (direction.x > 0.000001f)
            horizontalScale = (safe.xMax - center.x) / direction.x;
        else if (direction.x < -0.000001f)
            horizontalScale = (safe.xMin - center.x) / direction.x;

        float verticalScale = float.PositiveInfinity;
        if (direction.y > 0.000001f)
            verticalScale = (safe.yMax - center.y) / direction.y;
        else if (direction.y < -0.000001f)
            verticalScale = (safe.yMin - center.y) / direction.y;

        float scale = Mathf.Min(horizontalScale, verticalScale);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0f)
            scale = 0f;
        Vector2 anchor = center + direction * scale;
        anchor.x = Mathf.Clamp(anchor.x, safe.xMin, safe.xMax);
        anchor.y = Mathf.Clamp(anchor.y, safe.yMin, safe.yMax);
        return new FinitePlanetAssaultHudProjection(
            false,
            behind,
            anchor,
            direction);
    }

    static Rect NormalizeSafeViewport(Rect value)
    {
        float xMin = Mathf.Clamp01(Mathf.Min(value.xMin, value.xMax));
        float xMax = Mathf.Clamp01(Mathf.Max(value.xMin, value.xMax));
        float yMin = Mathf.Clamp01(Mathf.Min(value.yMin, value.yMax));
        float yMax = Mathf.Clamp01(Mathf.Max(value.yMin, value.yMax));
        if (xMax - xMin < MinimumSafeViewportSize)
        {
            xMin = 0.06f;
            xMax = 0.94f;
        }
        if (yMax - yMin < MinimumSafeViewportSize)
        {
            yMin = 0.1f;
            yMax = 0.88f;
        }
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }
}
