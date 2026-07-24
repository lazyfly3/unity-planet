using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(VoxelPlanetPlayerController))]
public sealed class SurfaceScannerController : MonoBehaviour
{
    const float PulseDuration = 1.2f;
    const float PulseAfterglow = 0.35f;
    const float PulseMaximumRadius = 180f;
    const float PulseWidth = 6f;
    const float TrackingRevealDelay = 0.25f;
    const float TrackingFadeDuration = 0.35f;
    const float ScreenMargin = 82f;

    static readonly int ScanOriginId =
        Shader.PropertyToID("_SurfaceScanOrigin");
    static readonly int ScanRadiusId =
        Shader.PropertyToID("_SurfaceScanRadius");
    static readonly int ScanWidthId =
        Shader.PropertyToID("_SurfaceScanWidth");
    static readonly int ScanStrengthId =
        Shader.PropertyToID("_SurfaceScanStrength");
    static readonly int ScanColorId =
        Shader.PropertyToID("_SurfaceScanColor");

    readonly Color scanColor = new Color(0f, 0.9f, 1f, 1f);

    VoxelPlanetPlayerController player;
    VoxelQuadSphereWorld world;
    Camera playerCamera;
    Canvas scanCanvas;
    RectTransform canvasRect;
    SurfaceScanOverlayGraphic overlayGraphic;
    CanvasGroup overlayGroup;
    RectTransform indicatorRoot;
    RectTransform indicatorGraphicRect;
    SurfaceShipIndicatorGraphic indicatorGraphic;
    CanvasGroup indicatorGroup;
    Text indicatorLabel;
    Text scanResultLabel;
    bool inputBlocked;
    bool pulseActive;
    bool scanFoundTrackableTarget;
    float pulseAge;
    float trackingAge;
    float trackingRemaining;
    float scanResultRemaining;
    Vector3 scanOrigin;
    Vector2 lastOffscreenDirection = Vector2.up;

    public bool IsShipTrackingActive =>
        !inputBlocked
        && trackingRemaining > 0f
        && SurfaceSpacecraftController.Current != null;

    void Awake()
    {
        player = GetComponent<VoxelPlanetPlayerController>();
        world = FindObjectOfType<VoxelQuadSphereWorld>();
        playerCamera = Camera.main;
        BuildUI();
        ClearScanShader();
    }

    void Update()
    {
        if (inputBlocked)
            return;

        if (playerCamera == null)
            playerCamera = Camera.main;
        if (world == null)
            world = FindObjectOfType<VoxelQuadSphereWorld>();

        float deltaTime = Time.unscaledDeltaTime;
        UpdatePulse(deltaTime);
        UpdateTracking(deltaTime);
        UpdateScanResult(deltaTime);
    }

    public bool BeginScan(float trackingDuration = 15f)
    {
        SurfaceSpacecraftController ship = SurfaceSpacecraftController.Current;
        if (inputBlocked)
            return false;

        scanOrigin = transform.position;
        pulseAge = 0f;
        pulseActive = true;
        lastOffscreenDirection = Vector2.up;
        scanFoundTrackableTarget = ship != null && !ship.IsDeparting;
        if (scanFoundTrackableTarget)
        {
            trackingAge = 0f;
            trackingRemaining = Mathf.Max(0.1f, trackingDuration);
        }
        else
        {
            CancelTracking();
        }
        scanResultRemaining = 0f;
        scanResultLabel.gameObject.SetActive(false);
        overlayGroup.alpha = 1f;
        overlayGraphic.SetPulse(0f, 1f);
        SetScanShader(0f, 1f);
        return true;
    }

    public bool BeginShipScan(float trackingDuration = 15f)
    {
        return BeginScan(trackingDuration);
    }

    public void SetInputBlocked(bool blocked)
    {
        inputBlocked = blocked;
        if (blocked)
            CancelScan();
    }

    public static string FormatDistance(float distance)
    {
        distance = Mathf.Max(0f, distance);
        return distance < 1000f
            ? $"{Mathf.RoundToInt(distance)} m"
            : $"{distance / 1000f:0.0} km";
    }

    public static Vector2 ClampDirectionToSafeRect(
        Vector2 center,
        Vector2 direction,
        Rect safeRect,
        float margin)
    {
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.up;
        direction.Normalize();

        float halfWidth = Mathf.Max(
            1f,
            safeRect.width * 0.5f - Mathf.Max(0f, margin));
        float halfHeight = Mathf.Max(
            1f,
            safeRect.height * 0.5f - Mathf.Max(0f, margin));
        float xDistance = Mathf.Abs(direction.x) > 0.0001f
            ? halfWidth / Mathf.Abs(direction.x)
            : float.PositiveInfinity;
        float yDistance = Mathf.Abs(direction.y) > 0.0001f
            ? halfHeight / Mathf.Abs(direction.y)
            : float.PositiveInfinity;
        return center + direction * Mathf.Min(xDistance, yDistance);
    }

    public static Vector2 CalculateBehindCameraDirection(
        Vector3 cameraLocalTarget,
        float cameraAspect,
        float verticalFieldOfView,
        Vector2 fallbackDirection)
    {
        float verticalScale = 1f / Mathf.Max(
            0.001f,
            Mathf.Tan(
                Mathf.Clamp(verticalFieldOfView, 1f, 179f)
                * 0.5f
                * Mathf.Deg2Rad));
        float horizontalScale = verticalScale
            / Mathf.Max(0.01f, cameraAspect);
        Vector2 direction = new Vector2(
            cameraLocalTarget.x * horizontalScale,
            cameraLocalTarget.y * verticalScale);
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = fallbackDirection.sqrMagnitude > 0.0001f
                ? fallbackDirection
                : Vector2.up;
        }
        return direction.normalized;
    }

    void UpdatePulse(float deltaTime)
    {
        if (!pulseActive)
            return;

        pulseAge += deltaTime;
        float progress = Mathf.Clamp01(pulseAge / PulseDuration);
        float attack = Mathf.Clamp01(pulseAge / 0.12f);
        float release = pulseAge <= PulseDuration
            ? 1f
            : 1f - Mathf.Clamp01(
                (pulseAge - PulseDuration) / PulseAfterglow);
        float strength = attack * release;
        float easedProgress = progress * progress * (3f - 2f * progress);
        float radius = easedProgress * PulseMaximumRadius;
        overlayGroup.alpha = strength;
        overlayGraphic.SetPulse(progress, strength);
        SetScanShader(radius, strength);

        if (pulseAge < PulseDuration + PulseAfterglow)
            return;

        pulseActive = false;
        overlayGroup.alpha = 0f;
        overlayGraphic.SetPulse(1f, 0f);
        ClearScanShader();
        scanResultRemaining = 1.5f;
        scanResultLabel.text = scanFoundTrackableTarget
            ? "已锁定飞船信号"
            : "扫描完成 · 未发现可追踪信号";
        scanResultLabel.gameObject.SetActive(true);
    }

    void UpdateScanResult(float deltaTime)
    {
        if (scanResultRemaining <= 0f)
            return;

        scanResultRemaining = Mathf.Max(0f, scanResultRemaining - deltaTime);
        Color color = scanResultLabel.color;
        color.a = scanResultRemaining < 0.35f
            ? Mathf.Clamp01(scanResultRemaining / 0.35f)
            : 1f;
        scanResultLabel.color = color;
        if (scanResultRemaining <= 0f)
            scanResultLabel.gameObject.SetActive(false);
    }

    void UpdateTracking(float deltaTime)
    {
        if (trackingRemaining <= 0f)
        {
            indicatorGroup.alpha = 0f;
            return;
        }

        SurfaceSpacecraftController ship = SurfaceSpacecraftController.Current;
        if (ship == null || ship.IsDeparting)
        {
            CancelTracking();
            return;
        }

        trackingAge += deltaTime;
        trackingRemaining = Mathf.Max(0f, trackingRemaining - deltaTime);
        if (trackingAge < TrackingRevealDelay)
        {
            indicatorGroup.alpha = 0f;
            return;
        }

        float reveal = Mathf.Clamp01(
            (trackingAge - TrackingRevealDelay) / 0.18f);
        float fade = trackingRemaining < TrackingFadeDuration
            ? Mathf.Clamp01(trackingRemaining / TrackingFadeDuration)
            : 1f;
        indicatorGroup.alpha = reveal * fade;
        UpdateIndicatorPose(ship);

        if (trackingRemaining <= 0f)
            CancelTracking();
    }

    void UpdateIndicatorPose(SurfaceSpacecraftController ship)
    {
        if (playerCamera == null || canvasRect == null)
            return;

        Vector3 target = ship.TrackingPosition;
        Vector3 screen = playerCamera.WorldToScreenPoint(target);
        Rect safe = Screen.safeArea;
        Rect paddedSafe = new Rect(
            safe.xMin + ScreenMargin,
            safe.yMin + ScreenMargin,
            Mathf.Max(1f, safe.width - ScreenMargin * 2f),
            Mathf.Max(1f, safe.height - ScreenMargin * 2f));
        bool onscreen = screen.z > 0f
            && paddedSafe.Contains(new Vector2(screen.x, screen.y));

        Vector2 screenPoint;
        Vector2 direction;
        if (onscreen)
        {
            screenPoint = new Vector2(screen.x, screen.y);
            direction = Vector2.up;
        }
        else
        {
            Vector3 localTarget =
                playerCamera.transform.InverseTransformPoint(target);
            direction = screen.z > 0f
                ? new Vector2(screen.x, screen.y) - safe.center
                : CalculateBehindCameraDirection(
                    localTarget,
                    playerCamera.aspect,
                    playerCamera.fieldOfView,
                    lastOffscreenDirection);
            if (direction.sqrMagnitude < 0.0001f)
                direction = lastOffscreenDirection;
            lastOffscreenDirection = direction.normalized;
            screenPoint = ClampDirectionToSafeRect(
                safe.center,
                direction,
                safe,
                ScreenMargin);
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPoint,
            null,
            out Vector2 localPoint);
        indicatorRoot.anchoredPosition = localPoint;
        indicatorGraphic.SetOffscreen(!onscreen);
        indicatorGraphicRect.localRotation = onscreen
            ? Quaternion.identity
            : Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);

        float distance = Vector3.Distance(transform.position, target);
        indicatorLabel.text =
            $"{ship.TrackingStatusText}\n{FormatDistance(distance)}";
    }

    void SetScanShader(float radius, float strength)
    {
        Shader.SetGlobalVector(
            ScanOriginId,
            new Vector4(scanOrigin.x, scanOrigin.y, scanOrigin.z, 1f));
        Shader.SetGlobalFloat(ScanRadiusId, Mathf.Max(0f, radius));
        Shader.SetGlobalFloat(ScanWidthId, PulseWidth);
        Shader.SetGlobalFloat(ScanStrengthId, Mathf.Clamp01(strength));
        Shader.SetGlobalColor(ScanColorId, scanColor);
    }

    void CancelTracking()
    {
        trackingAge = 0f;
        trackingRemaining = 0f;
        if (indicatorGroup != null)
            indicatorGroup.alpha = 0f;
    }

    void CancelScan()
    {
        pulseActive = false;
        pulseAge = 0f;
        scanResultRemaining = 0f;
        CancelTracking();
        if (overlayGroup != null)
            overlayGroup.alpha = 0f;
        if (overlayGraphic != null)
            overlayGraphic.SetPulse(0f, 0f);
        if (scanResultLabel != null)
            scanResultLabel.gameObject.SetActive(false);
        ClearScanShader();
    }

    static void ClearScanShader()
    {
        Shader.SetGlobalFloat(ScanRadiusId, 0f);
        Shader.SetGlobalFloat(ScanWidthId, PulseWidth);
        Shader.SetGlobalFloat(ScanStrengthId, 0f);
        Shader.SetGlobalColor(ScanColorId, Color.clear);
    }

    void BuildUI()
    {
        GameObject canvasObject = new GameObject(
            "SurfaceScannerCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        scanCanvas = canvasObject.GetComponent<Canvas>();
        scanCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        scanCanvas.sortingOrder = 175;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = canvasObject.GetComponent<RectTransform>();

        GameObject overlayObject = CreateUiObject(
            "ScanPulseOverlay",
            canvasObject.transform);
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayGroup = overlayObject.AddComponent<CanvasGroup>();
        overlayGroup.alpha = 0f;
        overlayGroup.blocksRaycasts = false;
        overlayGraphic = overlayObject.AddComponent<SurfaceScanOverlayGraphic>();
        overlayGraphic.raycastTarget = false;

        GameObject indicatorObject = CreateUiObject(
            "ShipScanIndicator",
            canvasObject.transform);
        indicatorRoot = indicatorObject.GetComponent<RectTransform>();
        indicatorRoot.anchorMin = indicatorRoot.anchorMax =
            new Vector2(0.5f, 0.5f);
        indicatorRoot.pivot = new Vector2(0.5f, 0.5f);
        indicatorRoot.sizeDelta = new Vector2(220f, 112f);
        indicatorGroup = indicatorObject.AddComponent<CanvasGroup>();
        indicatorGroup.alpha = 0f;
        indicatorGroup.blocksRaycasts = false;
        indicatorGroup.interactable = false;

        GameObject graphicObject = CreateUiObject(
            "DirectionGraphic",
            indicatorObject.transform);
        indicatorGraphicRect = graphicObject.GetComponent<RectTransform>();
        indicatorGraphicRect.anchorMin = indicatorGraphicRect.anchorMax =
            new Vector2(0.5f, 0.5f);
        indicatorGraphicRect.pivot = new Vector2(0.5f, 0.5f);
        indicatorGraphicRect.sizeDelta = new Vector2(72f, 72f);
        indicatorGraphicRect.anchoredPosition = new Vector2(0f, 17f);
        indicatorGraphic =
            graphicObject.AddComponent<SurfaceShipIndicatorGraphic>();
        indicatorGraphic.raycastTarget = false;

        indicatorLabel = CreateText(
            "ShipStatus",
            indicatorObject.transform,
            string.Empty,
            18,
            TextAnchor.UpperCenter);
        RectTransform labelRect = indicatorLabel.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -20f);
        labelRect.sizeDelta = new Vector2(220f, 58f);
        indicatorLabel.color = new Color(0.72f, 0.97f, 1f, 1f);
        indicatorLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

        scanResultLabel = CreateText(
            "ScanResult",
            canvasObject.transform,
            string.Empty,
            20,
            TextAnchor.MiddleCenter);
        RectTransform resultRect = scanResultLabel.rectTransform;
        resultRect.anchorMin = resultRect.anchorMax = new Vector2(0.5f, 0.28f);
        resultRect.pivot = new Vector2(0.5f, 0.5f);
        resultRect.sizeDelta = new Vector2(520f, 42f);
        scanResultLabel.color = new Color(0.58f, 0.96f, 1f, 1f);
        scanResultLabel.gameObject.SetActive(false);
    }

    static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    static Text CreateText(
        string objectName,
        Transform parent,
        string value,
        int fontSize,
        TextAnchor alignment)
    {
        GameObject gameObject = CreateUiObject(objectName, parent);
        Text text = gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    void OnDisable()
    {
        CancelScan();
    }

    void OnDestroy()
    {
        CancelScan();
    }
}
