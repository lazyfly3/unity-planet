using System.Collections.Generic;
using SpacecraftEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[DefaultExecutionOrder(850)]
[DisallowMultipleComponent]
public sealed class SpaceflightCockpitController : MonoBehaviour
{
    const string CockpitResourcePath = "Spaceflight/UniversalCockpit";
    const int MaximumRadarContacts = 24;

    static readonly Color Cyan = new Color(0.08f, 0.86f, 1f, 1f);
    static readonly Color Pale = new Color(0.79f, 0.96f, 1f, 1f);
    static readonly Color Muted = new Color(0.42f, 0.75f, 0.84f, 1f);
    static readonly Color Amber = new Color(1f, 0.58f, 0.12f, 1f);
    static readonly Color Danger = new Color(1f, 0.18f, 0.08f, 1f);

    [SerializeField] InterstellarShipController ship;
    [SerializeField] InterstellarCameraRig cameraRig;
    [SerializeField] InterstellarFlightHud hud;
    [SerializeField] Camera cockpitCamera;
    [SerializeField] GameObject cockpitRoot;
    [SerializeField] Transform stickPivot;
    [SerializeField] Transform throttlePivot;

    readonly SpaceflightRadarContact[] radarContacts =
        new SpaceflightRadarContact[MaximumRadarContacts];
    readonly List<RendererState> exteriorRendererStates = new List<RendererState>(32);

    Text leftReadout;
    Text leftMode;
    Text centerTarget;
    Text rightReadout;
    Text rightStatus;
    SpaceflightSegmentedBarGraphic hullBar;
    SpaceflightSegmentedBarGraphic boostBar;
    SpaceflightSegmentedBarGraphic ammunitionBar;
    SpaceflightSegmentedBarGraphic capacitorBar;
    SpaceflightSegmentedBarGraphic heatBar;
    SpaceflightCockpitRadarGraphic radarGraphic;
    Quaternion stickBaseRotation = Quaternion.identity;
    Quaternion throttleBaseRotation = Quaternion.identity;
    Vector3 cockpitBaseScale = Vector3.one;
    Renderer[] warningLights;
    MaterialPropertyBlock warningLightProperties;
    float nextInstrumentRefreshTime;
    bool visible;
    bool initialized;

    struct RendererState
    {
        public Renderer renderer;
        public bool enabled;
        public ShadowCastingMode shadowCastingMode;
    }

    public bool IsVisible => visible;
    public Camera CockpitCamera => cockpitCamera;

    public void Configure(
        InterstellarShipController configuredShip,
        InterstellarCameraRig configuredCameraRig,
        InterstellarFlightHud configuredHud)
    {
        if (configuredShip != null)
            ship = configuredShip;
        if (configuredCameraRig != null)
            cameraRig = configuredCameraRig;
        if (configuredHud != null)
            hud = configuredHud;
        EnsureInitialized();
    }

    public void SetVisible(bool shouldBeVisible)
    {
        EnsureInitialized();
        if (visible == shouldBeVisible)
        {
            SyncVisibility();
            return;
        }
        visible = shouldBeVisible;
        SyncVisibility();
    }

    public void RefreshShipFit()
    {
        if (!initialized || cockpitRoot == null || ship == null || cameraRig == null)
            return;

        ShipHullController hull = ship.GetComponent<ShipHullController>();
        Vector3 dimensions = hull != null && hull.CurrentHull != null
            ? hull.CurrentHull.Dimensions
            : hull != null
                ? hull.LocalBounds.size
                : ship.VisualBounds.size;
        float widthScale = Mathf.Clamp(dimensions.x / 3.4f, 0.86f, 1.22f);
        float heightScale = Mathf.Clamp(dimensions.y / 2.2f, 0.9f, 1.16f);
        cockpitRoot.transform.localPosition = cameraRig.CockpitAnchorLocal;
        cockpitRoot.transform.localRotation = Quaternion.identity;
        cockpitRoot.transform.localScale = Vector3.Scale(
            cockpitBaseScale,
            new Vector3(widthScale, heightScale, 1f));
        RefreshExteriorRendererCache();
        SyncVisibility();
    }

    void Awake()
    {
        EnsureInitialized();
    }

    void LateUpdate()
    {
        if (!initialized)
            EnsureInitialized();
        if (!visible)
            return;

        SyncCockpitCamera();
        if (Time.unscaledTime >= nextInstrumentRefreshTime)
        {
            nextInstrumentRefreshTime = Time.unscaledTime + 0.1f;
            UpdateInstruments();
        }
        AnimateControls();
    }

    void EnsureInitialized()
    {
        if (initialized)
            return;
        if (warningLightProperties == null)
            warningLightProperties = new MaterialPropertyBlock();
        if (cameraRig == null)
            cameraRig = GetComponent<InterstellarCameraRig>();
        if (ship == null)
            ship = FindObjectOfType<InterstellarShipController>();
        if (hud == null)
            hud = FindObjectOfType<InterstellarFlightHud>();
        Camera worldCamera = cameraRig == null ? Camera.main : cameraRig.TargetCamera;
        if (ship == null || worldCamera == null)
            return;

        int cockpitLayer = LayerMask.NameToLayer("CockpitView");
        if (cockpitLayer < 0)
            cockpitLayer = 9;

        GameObject prefab = Resources.Load<GameObject>(CockpitResourcePath);
        if (cockpitRoot == null && prefab != null)
        {
            cockpitRoot = Instantiate(prefab, ship.transform);
            cockpitRoot.name = "UniversalCockpitView";
        }
        if (cockpitRoot == null)
        {
            cockpitRoot = new GameObject("UniversalCockpitView");
            cockpitRoot.transform.SetParent(ship.transform, false);
            BuildRuntimeFallbackModel(cockpitRoot.transform);
            Debug.LogWarning(
                "SpaceflightCockpitController: cockpit prefab was unavailable; using the runtime fallback. "
                + "Run Tools/Spacecraft/Rebuild Flight Cockpit Assets to rebuild the Blender prefab.",
                this);
        }

        cockpitBaseScale = cockpitRoot.transform.localScale;
        SetLayerRecursively(cockpitRoot.transform, cockpitLayer);
        DisableModelScreenSurfaces();
        ResolveControlPivots();
        ResolveWarningLights();
        BuildInstrumentCanvases(cockpitLayer);
        BuildCockpitCamera(worldCamera, cockpitLayer);
        RefreshExteriorRendererCache();
        initialized = true;
        RefreshShipFit();
        SyncVisibility();
    }

    void BuildCockpitCamera(Camera worldCamera, int cockpitLayer)
    {
        if (cockpitCamera == null)
        {
            Transform existing = worldCamera.transform.Find("CockpitOverlayCamera");
            if (existing != null)
                cockpitCamera = existing.GetComponent<Camera>();
        }
        if (cockpitCamera == null)
        {
            GameObject cameraObject = new GameObject("CockpitOverlayCamera");
            cameraObject.transform.SetParent(worldCamera.transform, false);
            cockpitCamera = cameraObject.AddComponent<Camera>();
        }

        cockpitCamera.CopyFrom(worldCamera);
        cockpitCamera.transform.localPosition = Vector3.zero;
        cockpitCamera.transform.localRotation = Quaternion.identity;
        cockpitCamera.clearFlags = CameraClearFlags.Depth;
        cockpitCamera.depth = worldCamera.depth + 1f;
        cockpitCamera.cullingMask = 1 << cockpitLayer;
        cockpitCamera.nearClipPlane = 0.01f;
        cockpitCamera.farClipPlane = 8f;
        cockpitCamera.useOcclusionCulling = false;
        cockpitCamera.enabled = false;
        AudioListener listener = cockpitCamera.GetComponent<AudioListener>();
        if (listener != null)
            DestroySafe(listener);
    }

    void BuildInstrumentCanvases(int cockpitLayer)
    {
        Font font = hud == null ? null : hud.HudFont;
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Transform oldInstruments = cockpitRoot.transform.Find("RuntimeInstruments");
        if (oldInstruments != null)
            DestroySafe(oldInstruments.gameObject);
        Transform instruments = new GameObject("RuntimeInstruments").transform;
        instruments.SetParent(cockpitRoot.transform, false);
        instruments.gameObject.layer = cockpitLayer;

        BuildLeftDisplay(
            CreateInstrumentAnchor(
                instruments,
                "LeftMFD_RuntimeAnchor",
                "RightMFD_Anchor",
                new Vector3(0.86f, -0.2f, 0.905f)),
            font,
            cockpitLayer);
        BuildCenterDisplay(
            CreateInstrumentAnchor(
                instruments,
                "CenterRadar_RuntimeAnchor",
                "CenterRadar_Anchor",
                new Vector3(0f, -0.14f, 1.005f)),
            font,
            cockpitLayer);
        BuildRightDisplay(
            CreateInstrumentAnchor(
                instruments,
                "RightMFD_RuntimeAnchor",
                "LeftMFD_Anchor",
                new Vector3(-0.86f, -0.2f, 0.905f)),
            font,
            cockpitLayer);
    }

    void BuildLeftDisplay(Transform parent, Font font, int layer)
    {
        RectTransform root = CreateDisplay(
            "LeftMFD",
            parent,
            Vector3.zero,
            new Vector2(680f, 420f),
            layer);
        leftReadout = CreateText(
            "Readout",
            root,
            font,
            "速度      0 m/s\n船体完整度    100%\n推进储量      100%",
            34,
            TextAnchor.UpperLeft,
            Pale,
            new Vector2(34f, 160f),
            new Vector2(612f, 208f));
        leftMode = CreateText(
            "FlightMode",
            root,
            font,
            "辅助模式",
            31,
            TextAnchor.MiddleLeft,
            Cyan,
            new Vector2(34f, 34f),
            new Vector2(612f, 48f));
        hullBar = CreateBar("HullBar", root, new Vector2(34f, 116f), new Vector2(612f, 20f), 20);
        boostBar = CreateBar("BoostBar", root, new Vector2(34f, 84f), new Vector2(612f, 16f), 16);
    }

    void BuildCenterDisplay(Transform parent, Font font, int layer)
    {
        RectTransform root = CreateDisplay(
            "CenterRadar",
            parent,
            Vector3.zero,
            new Vector2(560f, 420f),
            layer);
        RectTransform radarRect = CreateRect("RadarGraphic", root);
        SetRect(radarRect, new Vector2(0f, 72f), new Vector2(560f, 330f));
        radarGraphic = radarRect.gameObject.AddComponent<SpaceflightCockpitRadarGraphic>();
        radarGraphic.color = Color.white;
        centerTarget = CreateText(
            "Target",
            root,
            font,
            "雷达待机",
            30,
            TextAnchor.MiddleCenter,
            Cyan,
            new Vector2(20f, 18f),
            new Vector2(520f, 50f));
    }

    void BuildRightDisplay(Transform parent, Font font, int layer)
    {
        RectTransform root = CreateDisplay(
            "RightMFD",
            parent,
            Vector3.zero,
            new Vector2(680f, 420f),
            layer);
        rightReadout = CreateText(
            "Readout",
            root,
            font,
            "武器组 1\n弹药 --\n电容 --\n热量 --",
            34,
            TextAnchor.UpperLeft,
            Pale,
            new Vector2(34f, 160f),
            new Vector2(612f, 208f));
        rightStatus = CreateText(
            "Status",
            root,
            font,
            "武器离线",
            29,
            TextAnchor.MiddleLeft,
            Cyan,
            new Vector2(34f, 26f),
            new Vector2(612f, 48f));
        ammunitionBar = CreateBar("AmmunitionBar", root, new Vector2(290f, 226f), new Vector2(356f, 16f), 16);
        capacitorBar = CreateBar("CapacitorBar", root, new Vector2(290f, 178f), new Vector2(356f, 16f), 16);
        heatBar = CreateBar("HeatBar", root, new Vector2(290f, 130f), new Vector2(356f, 16f), 16);
    }

    void UpdateInstruments()
    {
        SpaceflightTelemetrySnapshot telemetry = hud == null
            ? default
            : hud.CurrentTelemetry;
        if (!telemetry.valid)
            return;

        if (leftReadout != null)
        {
            leftReadout.text = telemetry.cinematicTransit
                ? "航行状态      跃迁航行\n"
                    + $"船体完整度    {telemetry.hullRatio * 100f:0}%\n"
                    + $"质量          {SpaceflightUnitFormatter.FormatMass(telemetry.shipMass)}\n"
                    + "IFCS 设定      --"
                : $"实际速度      {SpaceflightUnitFormatter.FormatSpeed(telemetry.speed)}\n"
                + $"当前加速度    {SpaceflightUnitFormatter.FormatAcceleration(telemetry.acceleration.magnitude)}\n"
                + $"船体完整度    {telemetry.hullRatio * 100f:0}%\n"
                + $"质量/推力     {SpaceflightUnitFormatter.FormatMass(telemetry.shipMass)} / " +
                    $"{SpaceflightUnitFormatter.FormatForce(telemetry.appliedLocalForce.magnitude)}\n"
                + $"IFCS 设定     {SpaceflightUnitFormatter.FormatSpeed(telemetry.targetSpeed)}";
        }
        if (leftMode != null)
        {
            string assist = telemetry.assistMode == SpacecraftAssistMode.Decoupled
                ? "惯性模式"
                : telemetry.assistMode == SpacecraftAssistMode.Direct
                    ? "直接控制"
                    : "辅助模式";
            leftMode.text = assist + " · "
                + SpaceflightUnitFormatter.FormatReference(telemetry.velocityReference)
                + (telemetry.interactionMode == SpaceflightInteractionMode.HighSpeedTravel
                    ? " · 高速航行层"
                    : string.Empty);
            leftMode.color = telemetry.controlAuthority < 0.65f ? Amber : Cyan;
        }
        hullBar?.SetValue(
            telemetry.hullRatio,
            telemetry.hullRatio < 0.25f ? Danger : telemetry.hullRatio < 0.55f ? Amber : Cyan);
        boostBar?.SetValue(telemetry.boostRatio, Cyan);

        if (rightReadout != null)
        {
            string ammo = telemetry.ammunitionCapacity <= 0
                ? "∞"
                : $"{telemetry.ammunition}/{telemetry.ammunitionCapacity}";
            rightReadout.text =
                $"武器组        {telemetry.weaponGroup}\n"
                + $"弹药          {ammo}\n"
                + $"电容          {telemetry.capacitorRatio * 100f:0}%\n"
                + $"热量          {telemetry.heatRatio * 100f:0}%";
        }
        if (rightStatus != null)
        {
            rightStatus.text = telemetry.interactionMode == SpaceflightInteractionMode.HighSpeedTravel
                ? "高速航行   武器离线"
                : $"{telemetry.mountLabel}   {telemetry.lockLabel}";
            rightStatus.color = telemetry.heatRatio > 0.82f ? Danger : Cyan;
        }
        float ammunitionRatio = telemetry.ammunitionCapacity <= 0
            ? 1f
            : telemetry.ammunition / (float)Mathf.Max(1, telemetry.ammunitionCapacity);
        ammunitionBar?.SetValue(ammunitionRatio, ammunitionRatio <= 0.05f ? Danger : Cyan);
        capacitorBar?.SetValue(telemetry.capacitorRatio, Cyan);
        heatBar?.SetValue(
            telemetry.heatRatio,
            telemetry.heatRatio > 0.82f ? Danger : telemetry.heatRatio > 0.58f ? Amber : Cyan);

        int contactCount = hud == null ? 0 : hud.CopyRadarContacts(radarContacts);
        radarGraphic?.SetContacts(radarContacts, contactCount);
        if (centerTarget != null)
        {
            centerTarget.text = string.IsNullOrEmpty(telemetry.targetName)
                ? "雷达扫描"
                : $"{telemetry.targetName}   {FormatDistance(telemetry.targetDistance)}";
            centerTarget.color = telemetry.targetKind == SpaceWeaponTargetKind.Combatant
                ? Danger
                : Cyan;
        }
        UpdateWarningLights(telemetry);
    }

    void AnimateControls()
    {
        if (ship == null)
            return;
        SpacecraftFlightCommand command = ship.FlightCommand;
        float blend = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
        if (stickPivot != null)
        {
            Quaternion targetRotation = stickBaseRotation
                * Quaternion.Euler(command.vjoy.x * 13f, command.vjoy.y * 8f, -command.roll * 18f);
            stickPivot.localRotation = Quaternion.Slerp(stickPivot.localRotation, targetRotation, blend);
        }
        if (throttlePivot != null)
        {
            float throttle = Mathf.Clamp(command.translation.z + (command.boost ? 0.35f : 0f), -1f, 1.35f);
            Quaternion targetRotation = throttleBaseRotation * Quaternion.Euler(-throttle * 24f, 0f, 0f);
            throttlePivot.localRotation = Quaternion.Slerp(throttlePivot.localRotation, targetRotation, blend);
        }
    }

    void SyncCockpitCamera()
    {
        if (cockpitCamera == null || cameraRig == null || cameraRig.TargetCamera == null)
            return;
        Camera worldCamera = cameraRig.TargetCamera;
        cockpitCamera.fieldOfView = worldCamera.fieldOfView;
        cockpitCamera.aspect = worldCamera.aspect;
        cockpitCamera.rect = worldCamera.rect;
        cockpitCamera.targetTexture = worldCamera.targetTexture;
        cockpitCamera.depth = worldCamera.depth + 1f;
    }

    void SyncVisibility()
    {
        if (cockpitRoot != null)
            cockpitRoot.SetActive(visible);
        if (cockpitCamera != null)
            cockpitCamera.enabled = visible;
        ApplyExteriorVisibility(visible);
    }

    void RefreshExteriorRendererCache()
    {
        RestoreExteriorRenderers();
        exteriorRendererStates.Clear();
        if (ship == null)
            return;
        Renderer[] renderers = ship.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null
                || (cockpitRoot != null && renderer.transform.IsChildOf(cockpitRoot.transform)))
            {
                continue;
            }
            exteriorRendererStates.Add(new RendererState
            {
                renderer = renderer,
                enabled = renderer.enabled,
                shadowCastingMode = renderer.shadowCastingMode
            });
        }
    }

    void ApplyExteriorVisibility(bool cockpitVisible)
    {
        for (int index = 0; index < exteriorRendererStates.Count; index++)
        {
            RendererState state = exteriorRendererStates[index];
            if (state.renderer == null)
                continue;
            if (!cockpitVisible)
            {
                state.renderer.enabled = state.enabled;
                state.renderer.shadowCastingMode = state.shadowCastingMode;
                continue;
            }
            if (state.renderer is ParticleSystemRenderer || state.renderer is TrailRenderer)
            {
                state.renderer.enabled = false;
            }
            else
            {
                state.renderer.enabled = state.enabled;
                state.renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }
    }

    void RestoreExteriorRenderers()
    {
        for (int index = 0; index < exteriorRendererStates.Count; index++)
        {
            RendererState state = exteriorRendererStates[index];
            if (state.renderer == null)
                continue;
            state.renderer.enabled = state.enabled;
            state.renderer.shadowCastingMode = state.shadowCastingMode;
        }
    }

    void ResolveControlPivots()
    {
        if (cockpitRoot == null)
            return;
        stickPivot = FindDeep(cockpitRoot.transform, "StickPivot");
        throttlePivot = FindDeep(cockpitRoot.transform, "ThrottlePivot");
        if (stickPivot != null)
            stickBaseRotation = stickPivot.localRotation;
        if (throttlePivot != null)
            throttleBaseRotation = throttlePivot.localRotation;
    }

    void ResolveWarningLights()
    {
        if (cockpitRoot == null)
            return;
        Renderer[] renderers = cockpitRoot.GetComponentsInChildren<Renderer>(true);
        var matches = new List<Renderer>(4);
        for (int index = 0; index < renderers.Length; index++)
            if (renderers[index] != null && renderers[index].name.StartsWith("WarningLight_"))
                matches.Add(renderers[index]);
        warningLights = matches.ToArray();
    }

    void UpdateWarningLights(SpaceflightTelemetrySnapshot telemetry)
    {
        if (warningLights == null)
            return;
        Color emission = telemetry.hullRatio < 0.25f || telemetry.heatRatio > 0.9f
            ? Danger * (2.1f + Mathf.PingPong(Time.unscaledTime * 5f, 1.8f))
            : telemetry.hullRatio < 0.55f || telemetry.heatRatio > 0.68f
                ? Amber * 2.4f
                : Cyan * 1.35f;
        warningLightProperties.Clear();
        warningLightProperties.SetColor("_EmissionColor", emission);
        for (int index = 0; index < warningLights.Length; index++)
            if (warningLights[index] != null)
                warningLights[index].SetPropertyBlock(warningLightProperties);
    }

    void DisableModelScreenSurfaces()
    {
        if (cockpitRoot == null)
            return;
        Renderer[] renderers = cockpitRoot.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            string objectName = renderers[index].name;
            if (objectName.EndsWith("_Surface"))
                renderers[index].enabled = false;
        }
    }

    static RectTransform CreateDisplay(
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector2 size,
        int layer)
    {
        GameObject display = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        display.layer = layer;
        RectTransform rect = display.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localPosition = localPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * 0.001f;
        rect.sizeDelta = size;
        Canvas canvas = display.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        SpaceflightHudPanelGraphic panel = display.AddComponent<SpaceflightHudPanelGraphic>();
        panel.Configure(new Color(0.005f, 0.035f, 0.06f, 0.96f), Cyan, Amber, 18f);
        panel.raycastTarget = false;
        return rect;
    }

    static SpaceflightSegmentedBarGraphic CreateBar(
        string name,
        Transform parent,
        Vector2 position,
        Vector2 size,
        int segments)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, position, size);
        SpaceflightSegmentedBarGraphic bar =
            rect.gameObject.AddComponent<SpaceflightSegmentedBarGraphic>();
        bar.Configure(segments);
        return bar;
    }

    static Text CreateText(
        string name,
        Transform parent,
        Font font,
        string value,
        int size,
        TextAnchor alignment,
        Color tint,
        Vector2 position,
        Vector2 dimensions)
    {
        RectTransform rect = CreateRect(name, parent);
        SetRect(rect, position, dimensions);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = tint;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0.15f, 0.22f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject item = new GameObject(name, typeof(RectTransform));
        item.layer = parent == null ? 0 : parent.gameObject.layer;
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        return rect;
    }

    static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static Transform FindDeep(Transform root, string targetName)
    {
        if (root == null)
            return null;
        if (root.name == targetName)
            return root;
        for (int index = 0; index < root.childCount; index++)
        {
            Transform result = FindDeep(root.GetChild(index), targetName);
            if (result != null)
                return result;
        }
        return null;
    }

    Transform CreateInstrumentAnchor(
        Transform parent,
        string name,
        string sourceAnchorName,
        Vector3 fallbackPosition)
    {
        Transform wrapper = new GameObject(name).transform;
        wrapper.SetParent(parent, false);
        wrapper.gameObject.layer = parent.gameObject.layer;
        Transform source = FindDeep(cockpitRoot.transform, sourceAnchorName);
        if (source != null)
        {
            wrapper.position = source.position;
            wrapper.rotation = source.rotation;
        }
        else
        {
            wrapper.localPosition = fallbackPosition;
            wrapper.localRotation = Quaternion.identity;
        }
        return wrapper;
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;
        root.gameObject.layer = layer;
        for (int index = 0; index < root.childCount; index++)
            SetLayerRecursively(root.GetChild(index), layer);
    }

    static string FormatDistance(double distance)
    {
        return SpaceflightUnitFormatter.FormatDistance(distance);
    }

    void BuildRuntimeFallbackModel(Transform root)
    {
        CreateFallbackCube(root, "Dashboard", new Vector3(0f, -0.38f, 0.95f), new Vector3(2.4f, 0.18f, 0.64f));
        CreateFallbackCube(root, "CanopyFrame_Left", new Vector3(-1.04f, 0.17f, 1.32f), new Vector3(0.09f, 1.3f, 0.1f));
        CreateFallbackCube(root, "CanopyFrame_Right", new Vector3(1.04f, 0.17f, 1.32f), new Vector3(0.09f, 1.3f, 0.1f));
        stickPivot = CreateFallbackCube(root, "StickPivot", new Vector3(0.38f, -0.42f, 0.52f), new Vector3(0.11f, 0.36f, 0.11f)).transform;
        throttlePivot = CreateFallbackCube(root, "ThrottlePivot", new Vector3(-0.68f, -0.39f, 0.55f), new Vector3(0.1f, 0.3f, 0.1f)).transform;
    }

    static GameObject CreateFallbackCube(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = scale;
        Collider collider = cube.GetComponent<Collider>();
        if (collider != null)
            DestroySafe(collider);
        return cube;
    }

    static void DestroySafe(Object target)
    {
        if (target == null)
            return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    void OnDestroy()
    {
        RestoreExteriorRenderers();
    }
}
