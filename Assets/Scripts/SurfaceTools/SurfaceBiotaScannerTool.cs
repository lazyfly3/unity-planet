using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(VoxelPlanetPlayerController))]
public sealed class SurfaceBiotaScannerTool : MonoBehaviour
{
    const int MaximumRaycastHits = 24;

    readonly RaycastHit[] raycastHits = new RaycastHit[MaximumRaycastHits];
    Camera viewCamera;
    BiotaScannable lockedTarget;
    CanvasGroup overlayGroup;
    Text targetText;
    Text progressText;
    Image progressFill;
    bool inputBlocked;
    float scanAge;
    float scanDuration;

    public bool IsScanActive { get; private set; }
    public float ScanProgress => IsScanActive
        ? Mathf.Clamp01(scanAge / Mathf.Max(0.01f, scanDuration))
        : 0f;
    public BiotaScannable LockedTarget => lockedTarget;

    void Awake()
    {
        viewCamera = Camera.main;
        BuildUi();
        SetUiVisible(false);
    }

    void Update()
    {
        if (!IsScanActive)
            return;
        if (inputBlocked)
        {
            CancelScan();
            return;
        }

        scanAge += Time.deltaTime;
        float progress = ScanProgress;
        if (progressFill != null)
            progressFill.fillAmount = progress;
        if (progressText != null)
            progressText.text = lockedTarget != null
                ? $"生物分析中  {progress * 100f:0}%"
                : $"扫描中  {progress * 100f:0}%";
        if (progress < 1f)
            return;

        CompleteScan();
    }

    public bool BeginScan(float duration, float range)
    {
        if (inputBlocked || IsScanActive)
            return false;
        if (viewCamera == null)
            viewCamera = Camera.main;

        lockedTarget = FindTarget(range);
        scanAge = 0f;
        scanDuration = Mathf.Max(0.1f, duration);
        IsScanActive = true;
        SetUiVisible(true);
        if (targetText != null)
        {
            targetText.text = lockedTarget != null
                ? lockedTarget.DisplayName
                : "未识别到动植物目标";
            targetText.color = lockedTarget != null
                ? new Color(0.55f, 0.98f, 1f)
                : new Color(1f, 0.62f, 0.25f);
        }
        if (progressText != null)
            progressText.text = lockedTarget != null ? "生物分析中  0%" : "扫描中  0%";
        if (progressFill != null)
            progressFill.fillAmount = 0f;
        return true;
    }

    public void SetInputBlocked(bool blocked)
    {
        inputBlocked = blocked;
        if (blocked)
            CancelScan();
    }

    BiotaScannable FindTarget(float range)
    {
        if (viewCamera == null)
            return null;
        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(
            ray,
            raycastHits,
            Mathf.Max(1f, range),
            ~0,
            QueryTriggerInteraction.Collide);
        BiotaScannable nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = raycastHits[index];
            if (hit.collider == null || hit.distance >= nearestDistance)
                continue;
            BiotaScannable candidate =
                hit.collider.GetComponentInParent<BiotaScannable>();
            if (candidate == null)
                continue;
            nearest = candidate;
            nearestDistance = hit.distance;
        }
        return nearest;
    }

    void CompleteScan()
    {
        IsScanActive = false;
        bool isNew = false;
        if (lockedTarget != null && GalaxyTravelManager.Instance != null)
        {
            isNew = GalaxyTravelManager.Instance.RegisterBiotaDiscovery(
                lockedTarget.StableId,
                lockedTarget.DiscoveryType,
                lockedTarget.DisplayName,
                lockedTarget.Description);
        }

        if (targetText != null)
        {
            targetText.text = lockedTarget == null
                ? "扫描完成 · 未发现可记录的动植物"
                : isNew
                    ? $"已录入图鉴 · {lockedTarget.DisplayName}"
                    : $"图鉴已有记录 · {lockedTarget.DisplayName}";
        }
        if (progressText != null)
            progressText.text = string.Empty;
        if (progressFill != null)
            progressFill.fillAmount = 1f;
        CancelInvoke(nameof(HideCompletedUi));
        Invoke(nameof(HideCompletedUi), 1.25f);
        lockedTarget = null;
    }

    void CancelScan()
    {
        IsScanActive = false;
        scanAge = 0f;
        lockedTarget = null;
        CancelInvoke(nameof(HideCompletedUi));
        SetUiVisible(false);
    }

    void HideCompletedUi()
    {
        SetUiVisible(false);
    }

    void SetUiVisible(bool visible)
    {
        if (overlayGroup == null)
            return;
        overlayGroup.alpha = visible ? 1f : 0f;
        overlayGroup.blocksRaycasts = false;
        overlayGroup.interactable = false;
    }

    void BuildUi()
    {
        GameObject root = new GameObject(
            "BiotaScannerOverlay",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 176;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        overlayGroup = root.GetComponent<CanvasGroup>();

        GameObject frame = CreateUiObject("TargetFrame", root.transform);
        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0.5f);
        frameRect.sizeDelta = new Vector2(430f, 150f);
        frameRect.anchoredPosition = new Vector2(0f, -38f);
        Image frameImage = frame.AddComponent<Image>();
        frameImage.color = new Color(0.01f, 0.08f, 0.11f, 0.76f);
        frameImage.raycastTarget = false;

        targetText = CreateText(
            "TargetName",
            frame.transform,
            24,
            TextAnchor.MiddleCenter);
        RectTransform targetRect = targetText.rectTransform;
        targetRect.anchorMin = targetRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRect.sizeDelta = new Vector2(390f, 42f);
        targetRect.anchoredPosition = new Vector2(0f, 35f);

        GameObject bar = CreateUiObject("ProgressBar", frame.transform);
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0.5f);
        barRect.sizeDelta = new Vector2(340f, 8f);
        barRect.anchoredPosition = new Vector2(0f, 0f);
        Image barBackground = bar.AddComponent<Image>();
        barBackground.color = new Color(0.05f, 0.25f, 0.30f, 0.9f);
        barBackground.raycastTarget = false;

        GameObject fill = CreateUiObject("Fill", bar.transform);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        progressFill = fill.AddComponent<Image>();
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillOrigin = 0;
        progressFill.color = new Color(0f, 0.9f, 1f, 1f);
        progressFill.raycastTarget = false;

        progressText = CreateText(
            "Progress",
            frame.transform,
            18,
            TextAnchor.MiddleCenter);
        RectTransform progressRect = progressText.rectTransform;
        progressRect.anchorMin = progressRect.anchorMax = new Vector2(0.5f, 0.5f);
        progressRect.sizeDelta = new Vector2(390f, 36f);
        progressRect.anchoredPosition = new Vector2(0f, -34f);
        progressText.color = new Color(0.48f, 0.88f, 0.94f);
    }

    static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var value = new GameObject(objectName, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    static Text CreateText(
        string objectName,
        Transform parent,
        int fontSize,
        TextAnchor alignment)
    {
        GameObject value = CreateUiObject(objectName, parent);
        Text text = value.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    void OnDisable()
    {
        CancelScan();
    }
}
