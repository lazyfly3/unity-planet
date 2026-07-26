using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum SurfaceRadialAction
{
    CallSpacecraft = 0,
    Scan = 1
}

[DisallowMultipleComponent]
[RequireComponent(typeof(VoxelPlanetPlayerController))]
public sealed class SurfaceMultifunctionController : MonoBehaviour
{
    const float BoardDistance = 12f;
    const float SelectionRadius = 55f;
    const float ShipTrackingDuration = 15f;
    const int ActionsPerPage = 8;
    const string CallActionId = "call_spacecraft";
    const string ScanActionId = "scan";

    sealed class RadialActionEntry
    {
        public string id;
        public Func<string> getLabel;
        public Func<bool> isAvailable;
        public Action execute;
        public SurfaceRadialIconKind icon;
    }

    readonly List<RadialActionEntry> actions = new List<RadialActionEntry>();
    readonly List<Text> actionLabels = new List<Text>();
    readonly List<bool> visibleAvailability = new List<bool>();
    readonly List<SurfaceRadialIconKind> visibleIcons =
        new List<SurfaceRadialIconKind>();

    VoxelPlanetPlayerController player;
    SurfaceScannerController scanner;
    Camera playerCamera;
    CanvasGroup menuGroup;
    SurfaceRadialMenuGraphic menuGraphic;
    Transform labelLayer;
    Text selectedLabel;
    Text pageLabel;
    Text statusLabel;
    Text interactionLabel;
    Sprite runtimeFrameSprite;
    bool menuOpen;
    int selectedVisibleIndex = -1;
    int pageIndex;
    bool externalInputBlocked;
    bool pilotMenuContext;
    bool cancelled;
    float previousTimeScale = 1f;
    CursorLockMode previousCursorLock;
    bool previousCursorVisible;

    public bool IsOpen => menuOpen;
    public bool InputBlocked => externalInputBlocked;
    public bool PilotMenuContext => pilotMenuContext;
    public int RegisteredActionCount => actions.Count;
    public int PageCount => GetPageCount(actions.Count);

    void Awake()
    {
        player = GetComponent<VoxelPlanetPlayerController>();
        scanner = GetComponent<SurfaceScannerController>()
            ?? gameObject.AddComponent<SurfaceScannerController>();
        playerCamera = Camera.main;
        RegisterBuiltInActions();
        BuildUI();
        RefreshPageLayout();
    }

    void Update()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        bool uiBlocked = InventoryUI.BlocksGameplayInput;
        if ((externalInputBlocked || uiBlocked) && menuOpen)
            CloseMenu();

        SurfaceSpacecraftController ship = SurfaceSpacecraftController.Current;
        UpdateStatus(ship);
        bool canBoard = !externalInputBlocked
            && !uiBlocked
            && !menuOpen
            && ship != null
            && ship.CanBoardFrom(playerCamera, BoardDistance);
        if (canBoard)
            interactionLabel.text = ship.BoardPromptText;
        interactionLabel.gameObject.SetActive(canBoard);
        if (canBoard && Input.GetKeyDown(KeyCode.F))
        {
            ship.BeginSurfaceDeparture();
            return;
        }

        if (externalInputBlocked || uiBlocked)
            return;

        if (!menuOpen && Input.GetKeyDown(KeyCode.V))
        {
            OpenMenu();
            return;
        }

        if (!menuOpen)
            return;

        if (Input.GetKeyDown(KeyCode.Escape)
            || Input.GetMouseButtonDown(1))
        {
            cancelled = true;
            CloseMenu();
            return;
        }

        float scroll = Input.mouseScrollDelta.y;
        if (PageCount > 1 && Mathf.Abs(scroll) > 0.01f)
        {
            int direction = scroll < 0f ? 1 : -1;
            pageIndex = Mod(pageIndex + direction, PageCount);
            selectedVisibleIndex = -1;
            RefreshPageLayout();
        }

        UpdateSelection();
        if (Input.GetKeyUp(KeyCode.V))
        {
            RadialActionEntry entry = GetSelectedEntry();
            bool execute = entry != null
                && !cancelled
                && IsAvailable(entry);
            CloseMenu();
            if (execute)
                entry.execute?.Invoke();
        }
    }

    public void SetInputBlocked(bool blocked)
    {
        externalInputBlocked = blocked;
        if (blocked && menuOpen)
            CloseMenu();
        player?.SetGameplayInputBlocked(blocked);
        scanner?.SetInputBlocked(blocked || pilotMenuContext);
        GetComponent<SurfaceToolController>()
            ?.SetInputBlocked(blocked || pilotMenuContext);
        if (interactionLabel != null && blocked)
            interactionLabel.gameObject.SetActive(false);
    }

    public void SetPilotMenuContext(bool enabled)
    {
        if (!enabled && menuOpen && pilotMenuContext)
            CloseMenu();
        pilotMenuContext = enabled;
        scanner?.SetInputBlocked(enabled || externalInputBlocked);
        GetComponent<SurfaceToolController>()
            ?.SetInputBlocked(enabled || externalInputBlocked);
    }

    public bool RegisterAction(
        string id,
        string label,
        Action execute,
        Func<bool> isAvailable = null,
        SurfaceRadialIconKind icon = SurfaceRadialIconKind.None)
    {
        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(label)
            || execute == null
            || actions.Exists(entry => entry.id == id))
        {
            return false;
        }

        actions.Add(new RadialActionEntry
        {
            id = id,
            getLabel = () => label,
            isAvailable = isAvailable ?? (() => true),
            execute = execute,
            icon = icon
        });
        ClampPage();
        if (menuGraphic != null)
            RefreshPageLayout();
        return true;
    }

    public bool RegisterDynamicAction(
        string id,
        Func<string> getLabel,
        Action execute,
        Func<bool> isAvailable = null,
        SurfaceRadialIconKind icon = SurfaceRadialIconKind.None)
    {
        if (string.IsNullOrWhiteSpace(id)
            || getLabel == null
            || execute == null
            || actions.Exists(entry => entry.id == id))
        {
            return false;
        }
        actions.Add(new RadialActionEntry
        {
            id = id,
            getLabel = getLabel,
            isAvailable = isAvailable ?? (() => true),
            execute = execute,
            icon = icon
        });
        ClampPage();
        if (menuGraphic != null)
            RefreshPageLayout();
        return true;
    }

    public bool UnregisterAction(string id)
    {
        if (id == CallActionId || id == ScanActionId)
            return false;

        int index = actions.FindIndex(entry => entry.id == id);
        if (index < 0)
            return false;

        actions.RemoveAt(index);
        ClampPage();
        selectedVisibleIndex = -1;
        if (menuGraphic != null)
            RefreshPageLayout();
        return true;
    }

    public static int? ResolveActionIndex(
        Vector2 pointer,
        int actionCount,
        float selectionRadius = SelectionRadius)
    {
        if (pointer.magnitude < selectionRadius || actionCount <= 0)
            return null;

        float angle = Mathf.Atan2(pointer.y, pointer.x) * Mathf.Rad2Deg;
        int slotCount = Mathf.Max(4, actionCount);
        float halfAngle = Mathf.Min(38f, 360f / slotCount * 0.44f);
        for (int i = 0; i < actionCount; i++)
        {
            float center = SurfaceRadialMenuGraphic.GetActionCenterAngle(
                i,
                actionCount);
            if (Mathf.Abs(Mathf.DeltaAngle(angle, center)) <= halfAngle)
                return i;
        }
        return null;
    }

    public static SurfaceRadialAction? ResolveAction(
        Vector2 pointer,
        float selectionRadius = SelectionRadius)
    {
        int? index = ResolveActionIndex(pointer, 2, selectionRadius);
        return index.HasValue
            ? (SurfaceRadialAction?)index.Value
            : null;
    }

    public static int GetPageCount(
        int actionCount,
        int actionsPerPage = ActionsPerPage)
    {
        actionsPerPage = Mathf.Max(1, actionsPerPage);
        return Mathf.Max(1, Mathf.CeilToInt(
            Mathf.Max(0, actionCount) / (float)actionsPerPage));
    }

    void RegisterBuiltInActions()
    {
        actions.Add(new RadialActionEntry
        {
            id = CallActionId,
            getLabel = () => GetCallText(SurfaceSpacecraftController.Current),
            isAvailable = () => IsCallAvailable(
                SurfaceSpacecraftController.Current),
            execute = ExecuteCallAction,
            icon = SurfaceRadialIconKind.Spacecraft
        });
        actions.Add(new RadialActionEntry
        {
            id = ScanActionId,
            getLabel = () => "扫描",
            isAvailable = () => IsScanAvailable(
                SurfaceSpacecraftController.Current),
            execute = () => scanner.BeginScan(ShipTrackingDuration),
            icon = SurfaceRadialIconKind.Scan
        });
    }

    void ExecuteCallAction()
    {
        SurfaceSpacecraftController ship = SurfaceSpacecraftController.Current;
        if (ship != null && ship.CallToPlayer(player))
            scanner.BeginShipScan(ShipTrackingDuration);
    }

    void OpenMenu()
    {
        menuOpen = true;
        selectedVisibleIndex = -1;
        cancelled = false;
        previousTimeScale = Time.timeScale;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Time.timeScale = Mathf.Max(0f, previousTimeScale * 0.2f);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        menuGroup.alpha = 1f;
        menuGroup.blocksRaycasts = false;
        player.SetGameplayInputBlocked(true);
        GetComponent<SurfaceToolController>()?.SetInputBlocked(true);
        if (pilotMenuContext)
            SurfaceSpacecraftController.Current
                ?.SetPilotMenuOpen(true);
        RefreshPageLayout();
        UpdateSelection();
    }

    void CloseMenu()
    {
        if (!menuOpen)
            return;

        menuOpen = false;
        selectedVisibleIndex = -1;
        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        menuGroup.alpha = 0f;
        menuGroup.blocksRaycasts = false;
        selectedLabel.text = string.Empty;
        RefreshPageLayout();
        player.SetGameplayInputBlocked(externalInputBlocked);
        GetComponent<SurfaceToolController>()?.SetInputBlocked(
            externalInputBlocked || pilotMenuContext);
        if (pilotMenuContext)
            SurfaceSpacecraftController.Current
                ?.SetPilotMenuOpen(false);
    }

    void UpdateSelection()
    {
        int visibleCount = GetVisibleActionCount();
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 pointer = (Vector2)Input.mousePosition - center;
        selectedVisibleIndex = ResolveActionIndex(pointer, visibleCount) ?? -1;
        RefreshPageLayout();

        RadialActionEntry selected = GetSelectedEntry();
        selectedLabel.text = selected != null
            ? GetLabel(selected)
            : string.Empty;
        selectedLabel.color = selected != null && IsAvailable(selected)
            ? new Color(0.72f, 0.96f, 1f, 1f)
            : new Color(0.48f, 0.56f, 0.59f, 1f);
    }

    void RefreshPageLayout()
    {
        if (menuGraphic == null)
            return;

        ClampPage();
        int visibleCount = GetVisibleActionCount();
        EnsureActionLabels(visibleCount);
        visibleAvailability.Clear();
        visibleIcons.Clear();

        for (int i = 0; i < visibleCount; i++)
        {
            RadialActionEntry entry = actions[pageIndex * ActionsPerPage + i];
            bool available = IsAvailable(entry);
            visibleAvailability.Add(available);
            visibleIcons.Add(entry.icon);

            Text label = actionLabels[i];
            label.gameObject.SetActive(true);
            label.text = GetLabel(entry);
            label.color = available
                ? Color.white
                : new Color(0.48f, 0.56f, 0.59f, 1f);
            label.fontSize = visibleCount <= 4
                ? 18
                : visibleCount <= 6 ? 16 : 14;

            float angle = SurfaceRadialMenuGraphic.GetActionCenterAngle(
                i,
                visibleCount);
            float radians = angle * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians));
            label.rectTransform.anchoredPosition = direction * 136f;
            label.rectTransform.sizeDelta = visibleCount <= 4
                ? new Vector2(138f, 34f)
                : new Vector2(98f, 46f);
        }

        for (int i = visibleCount; i < actionLabels.Count; i++)
            actionLabels[i].gameObject.SetActive(false);

        menuGraphic.SetActions(
            visibleCount,
            selectedVisibleIndex,
            visibleAvailability,
            visibleIcons);
        pageLabel.text = PageCount > 1
            ? $"{pageIndex + 1}/{PageCount}  ·  滚轮切换"
            : string.Empty;
    }

    void EnsureActionLabels(int count)
    {
        while (actionLabels.Count < count)
        {
            Text label = CreateText(
                $"ActionLabel{actionLabels.Count}",
                labelLayer,
                string.Empty,
                18,
                TextAnchor.MiddleCenter);
            SetCenteredRect(
                label.rectTransform,
                Vector2.zero,
                new Vector2(138f, 34f));
            actionLabels.Add(label);
        }
    }

    int GetVisibleActionCount()
    {
        int start = pageIndex * ActionsPerPage;
        return Mathf.Clamp(actions.Count - start, 0, ActionsPerPage);
    }

    RadialActionEntry GetSelectedEntry()
    {
        if (selectedVisibleIndex < 0)
            return null;
        int index = pageIndex * ActionsPerPage + selectedVisibleIndex;
        return index >= 0 && index < actions.Count ? actions[index] : null;
    }

    void ClampPage()
    {
        pageIndex = Mathf.Clamp(pageIndex, 0, PageCount - 1);
    }

    static int Mod(int value, int modulus)
    {
        return (value % modulus + modulus) % modulus;
    }

    static bool IsAvailable(RadialActionEntry entry)
    {
        return entry != null
            && (entry.isAvailable == null || entry.isAvailable());
    }

    static string GetLabel(RadialActionEntry entry)
    {
        string label = entry?.getLabel?.Invoke();
        return string.IsNullOrWhiteSpace(label) ? "未命名功能" : label;
    }

    void UpdateStatus(SurfaceSpacecraftController ship)
    {
        string status = ship != null ? ship.StatusText : string.Empty;
        statusLabel.text = status;
        statusLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(status));
    }

    bool IsCallAvailable(SurfaceSpacecraftController ship)
    {
        return ship != null
            && ship.CanBeCalled
            && !ship.IsBoardingReachable(
                transform.position,
                BoardDistance);
    }

    static bool IsScanAvailable(SurfaceSpacecraftController ship)
    {
        return ship == null
            || (!ship.IsDeparting && !ship.IsPiloting);
    }

    string GetCallText(SurfaceSpacecraftController ship)
    {
        if (ship == null)
            return "未找到飞船";
        if (ship.IsMoving)
            return "飞船正在赶来";
        if (ship.IsDeparting)
            return "飞船正在起飞";
        if (ship.IsBoardingReachable(transform.position, BoardDistance))
            return "飞船已在附近";
        return "呼叫飞船";
    }

    void BuildUI()
    {
        GameObject canvasObject = new GameObject(
            "SurfaceMultifunctionCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 180;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject radialObject = CreateUiObject("RadialMenu", canvas.transform);
        menuGroup = radialObject.AddComponent<CanvasGroup>();
        menuGroup.alpha = 0f;
        menuGroup.blocksRaycasts = false;
        menuGroup.interactable = false;
        RectTransform radialRect = radialObject.GetComponent<RectTransform>();
        radialRect.anchorMin = radialRect.anchorMax = new Vector2(0.5f, 0.5f);
        radialRect.pivot = new Vector2(0.5f, 0.5f);
        radialRect.sizeDelta = new Vector2(430f, 430f);

        GameObject frameObject = CreateUiObject("RadialFrame", radialObject.transform);
        SetCenteredRect(
            frameObject.GetComponent<RectTransform>(),
            Vector2.zero,
            new Vector2(430f, 430f));
        Image frame = frameObject.AddComponent<Image>();
        Texture2D frameTexture = Resources.Load<Texture2D>(
            "UI/SurfaceScanner/SurfaceRadialFrame");
        if (frameTexture != null)
        {
            runtimeFrameSprite = Sprite.Create(
                frameTexture,
                new Rect(0f, 0f, frameTexture.width, frameTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            frame.sprite = runtimeFrameSprite;
        }
        frame.color = new Color(1f, 1f, 1f, 0.94f);
        frame.raycastTarget = false;

        GameObject sectorObject = CreateUiObject(
            "DynamicSectors",
            radialObject.transform);
        SetCenteredRect(
            sectorObject.GetComponent<RectTransform>(),
            Vector2.zero,
            new Vector2(430f, 430f));
        menuGraphic = sectorObject.AddComponent<SurfaceRadialMenuGraphic>();
        menuGraphic.raycastTarget = false;

        GameObject labels = CreateUiObject("ActionLabels", radialObject.transform);
        SetCenteredRect(
            labels.GetComponent<RectTransform>(),
            Vector2.zero,
            new Vector2(430f, 430f));
        labelLayer = labels.transform;

        selectedLabel = CreateText(
            "SelectedAction",
            radialObject.transform,
            string.Empty,
            17,
            TextAnchor.MiddleCenter);
        SetCenteredRect(
            selectedLabel.rectTransform,
            Vector2.zero,
            new Vector2(178f, 48f));

        pageLabel = CreateText(
            "PageIndicator",
            radialObject.transform,
            string.Empty,
            13,
            TextAnchor.MiddleCenter);
        SetCenteredRect(
            pageLabel.rectTransform,
            new Vector2(0f, -54f),
            new Vector2(180f, 28f));
        pageLabel.color = new Color(0.42f, 0.78f, 0.86f, 0.9f);

        Text hint = CreateText(
            "Hint",
            radialObject.transform,
            "移动鼠标选择 · 松开 V 执行 · 右键取消",
            17,
            TextAnchor.MiddleCenter);
        SetCenteredRect(
            hint.rectTransform,
            new Vector2(0f, -238f),
            new Vector2(600f, 32f));
        hint.color = new Color(0.65f, 0.88f, 0.94f, 0.92f);

        statusLabel = CreateText(
            "FlightStatus",
            canvas.transform,
            string.Empty,
            24,
            TextAnchor.MiddleCenter);
        RectTransform statusRect = statusLabel.rectTransform;
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(0.5f, 0.13f);
        statusRect.sizeDelta = new Vector2(600f, 44f);
        statusLabel.color = new Color(0.2f, 0.92f, 1f, 1f);
        statusLabel.gameObject.SetActive(false);

        interactionLabel = CreateText(
            "BoardPrompt",
            canvas.transform,
            "按 F 登上飞船并返回宇宙",
            24,
            TextAnchor.MiddleCenter);
        RectTransform interactionRect = interactionLabel.rectTransform;
        interactionRect.anchorMin = interactionRect.anchorMax =
            new Vector2(0.5f, 0.2f);
        interactionRect.sizeDelta = new Vector2(600f, 44f);
        interactionLabel.color = new Color(0.94f, 0.98f, 1f, 1f);
        interactionLabel.gameObject.SetActive(false);
    }

    static void SetCenteredRect(
        RectTransform rect,
        Vector2 position,
        Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
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
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    void OnDisable()
    {
        if (menuOpen)
            CloseMenu();
    }

    void OnDestroy()
    {
        if (menuOpen)
            CloseMenu();
        if (runtimeFrameSprite != null)
            Destroy(runtimeFrameSprite);
    }
}
