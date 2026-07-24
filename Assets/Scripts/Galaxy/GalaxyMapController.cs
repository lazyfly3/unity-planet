using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GalaxyMapController : MonoBehaviour
{
    [Header("中文界面")]
    [SerializeField] Font chineseFont;

    [Header("Navigation Input")]
    [SerializeField, Min(0f)] float initialRepeatDelay = 0.28f;
    [SerializeField, Min(0.03f)] float repeatInterval = 0.11f;

    Image[,] cells;
    Image[,] planetIcons;
    Text[,] planetLabels;

    GalaxyTravelManager travelManager;
    Canvas mapCanvas;
    GraphicRaycaster mapRaycaster;
    RectTransform shipIcon;
    Text locationText;
    Text actionText;
    Text controlsText;
    RectTransform contextRoot;
    RectTransform contextPanel;
    RectTransform renameRoot;
    InputField renameInput;
    Text renameErrorText;
    Vector2Int heldMoveDirection;
    float nextMoveTime;
    float mandatoryMessageUntil;
    int visitedSelectionIndex;
    int contextPlanetIndex = -1;
    bool travelSubmitted;

    void Awake()
    {
        EnsureRenderingCamera();
        EnsureEventSystem();
        FocusGameViewInEditor();

        travelManager = GalaxyTravelManager.Instance;
        if (travelManager == null)
        {
            Debug.LogError("GalaxyMapController: 缺少 GalaxyTravelManager。", this);
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        BuildInterface();
        RefreshShipPosition();
    }

    void Update()
    {
        if (travelManager == null || travelSubmitted)
            return;

        if (mandatoryMessageUntil > 0f && Time.unscaledTime >= mandatoryMessageUntil)
        {
            mandatoryMessageUntil = 0f;
            if (travelManager.IsInterstellarGalaxy)
                RefreshVisitedPlanetList();
        }

        if (travelManager.IsInterstellarGalaxy)
        {
            HandleVisitedPlanetInput();
            return;
        }

        HandleMovementInput();
        if (Input.GetKeyDown(KeyCode.F))
        {
            GalaxyPlanetDefinition planet = travelManager.GetPlanetAt(travelManager.ShipCoordinate);
            if (planet != null)
                travelManager.EnterPlanet(planet);
        }
    }

    void HandleVisitedPlanetInput()
    {
        if (IsRenameOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                CloseRenameDialog();
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                ConfirmRename();
            return;
        }

        if (IsContextMenuOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseContextMenu();
            return;
        }

        int count = Mathf.Min(
            travelManager.VisitedPlanets.Count,
            travelManager.GridColumns * travelManager.GridRows);
        if (count == 0)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.M))
                travelManager.ExitGalaxyMapWithoutTravel();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.M))
        {
            if (IsContextMenuOpen)
                CloseContextMenu();
            else
                travelManager.ExitGalaxyMapWithoutTravel();
            return;
        }

        if (IsContextMenuOpen)
        {
            if (Input.GetKeyDown(KeyCode.F)
                || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                SubmitVisitedTravel(contextPlanetIndex);
            }
            return;
        }

        int direction = 0;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)
            || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            direction = -1;
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)
            || Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            direction = 1;

        if (direction != 0)
        {
            visitedSelectionIndex = (visitedSelectionIndex + direction + count) % count;
            RefreshShipPosition();
        }

        if (Input.GetKeyDown(KeyCode.F)
            || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SubmitVisitedTravel(visitedSelectionIndex);
        }
    }

    void HandleMovementInput()
    {
        if (!TryReadSingleDirection(out Vector2Int direction))
        {
            heldMoveDirection = Vector2Int.zero;
            return;
        }

        bool directionChanged = direction != heldMoveDirection;
        if (!directionChanged && Time.unscaledTime < nextMoveTime)
            return;

        travelManager.MoveShip(new GalaxyCoordinateDelta(direction.x, direction.y));
        RefreshShipPosition();

        heldMoveDirection = direction;
        nextMoveTime = Time.unscaledTime + (directionChanged ? initialRepeatDelay : repeatInterval);
    }

    static bool TryReadSingleDirection(out Vector2Int direction)
    {
        bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        bool down = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool up = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        int pressedDirectionCount = (left ? 1 : 0) + (right ? 1 : 0)
            + (down ? 1 : 0) + (up ? 1 : 0);
        if (pressedDirectionCount != 1)
        {
            direction = Vector2Int.zero;
            return false;
        }

        direction = left ? Vector2Int.left
            : right ? Vector2Int.right
            : down ? Vector2Int.down
            : Vector2Int.up;
        return true;
    }

    void BuildInterface()
    {
        Font font = chineseFont != null
            ? chineseFont
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject(
            "GalaxyCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        mapCanvas = canvasObject.GetComponent<Canvas>();
        mapCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mapCanvas.sortingOrder = 100;
        mapRaycaster = canvasObject.GetComponent<GraphicRaycaster>();
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image background = CreateImage("星图背景", mapCanvas.transform, null);
        Stretch(background.rectTransform);
        background.sprite = Resources.Load<Sprite>("Galaxy/star_map_background");
        background.color = background.sprite != null
            ? Color.white
            : new Color(0.015f, 0.025f, 0.06f, 1f);
        background.type = Image.Type.Simple;
        background.preserveAspect = false;

        Image veil = CreateImage("界面遮罩", background.transform, null);
        Stretch(veil.rectTransform);
        veil.color = new Color(0.01f, 0.025f, 0.055f, 0.32f);

        string titleValue = travelManager.IsInterstellarGalaxy ? "已访问星球" : "深空导航";
        Text title = CreateText("标题", veil.transform, titleValue, font, 34, TextAnchor.MiddleCenter);
        SetRect(
            title.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(900f, 62f),
            new Vector2(0f, -48f));
        title.color = new Color(0.75f, 0.91f, 1f);

        string controlsValue = travelManager.IsInterstellarGalaxy
            ? "WASD / 方向键选择　　F / Enter 前往　　右键更多操作　　Esc / M 返回"
            : "WASD 移动一个星区　　F 进入星球轨道";
        controlsText = CreateText(
            "操作说明",
            veil.transform,
            controlsValue,
            font,
            19,
            TextAnchor.MiddleCenter);
        SetRect(
            controlsText.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(1100f, 38f),
            new Vector2(0f, -92f));
        controlsText.color = new Color(0.45f, 0.7f, 0.82f);

        RectTransform grid = CreateRect("星区网格", veil.transform);
        SetRect(
            grid,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1270f, 710f),
            new Vector2(0f, -5f));
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.spacing = new Vector2(10f, 10f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = travelManager.GridColumns;
        layout.childAlignment = TextAnchor.MiddleCenter;
        float cellWidth = (1270f - layout.spacing.x * (travelManager.GridColumns - 1))
            / travelManager.GridColumns;
        float cellHeight = (710f - layout.spacing.y * (travelManager.GridRows - 1))
            / travelManager.GridRows;
        layout.cellSize = new Vector2(cellWidth, cellHeight);
        cells = new Image[travelManager.GridColumns, travelManager.GridRows];
        planetIcons = new Image[travelManager.GridColumns, travelManager.GridRows];
        planetLabels = new Text[travelManager.GridColumns, travelManager.GridRows];

        int pointerIndex = 0;
        for (int row = travelManager.GridRows - 1; row >= 0; row--)
        {
            for (int column = 0; column < travelManager.GridColumns; column++)
            {
                Image cell = CreateImage($"星区_{column}_{row}", grid, null);
                cell.color = new Color(0.04f, 0.11f, 0.18f, 0.54f);
                Outline outline = cell.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.18f, 0.5f, 0.68f, 0.48f);
                outline.effectDistance = new Vector2(1f, -1f);
                cells[column, row] = cell;

                GalaxyMapVisitedCellInput pointer =
                    cell.gameObject.AddComponent<GalaxyMapVisitedCellInput>();
                pointer.Configure(this, pointerIndex++);

                Image icon = CreateImage("星球图标", cell.transform, null);
                Stretch(icon.rectTransform);
                icon.rectTransform.offsetMin = new Vector2(12f, 12f);
                icon.rectTransform.offsetMax = new Vector2(-12f, -12f);
                icon.preserveAspect = true;
                Text label = CreateText(
                    "星球名称",
                    icon.transform,
                    string.Empty,
                    font,
                    11,
                    TextAnchor.LowerCenter);
                Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(-22f, -18f);
                label.rectTransform.offsetMax = new Vector2(22f, 0f);
                label.color = new Color(0.82f, 0.92f, 1f);
                icon.gameObject.SetActive(false);
                planetIcons[column, row] = icon;
                planetLabels[column, row] = label;
            }
        }

        Image ship = CreateImage("飞船", veil.transform, Resources.Load<Sprite>("Galaxy/ship"));
        ship.rectTransform.sizeDelta = new Vector2(62f, 62f);
        ship.preserveAspect = true;
        ship.color = ship.sprite != null ? Color.white : new Color(0.85f, 0.95f, 1f);
        ship.raycastTarget = false;
        shipIcon = ship.rectTransform;

        locationText = CreateText(
            "当前位置",
            veil.transform,
            string.Empty,
            font,
            24,
            TextAnchor.MiddleCenter);
        SetRect(
            locationText.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1100f, 44f),
            new Vector2(0f, 72f));
        locationText.color = new Color(0.8f, 0.94f, 1f);

        actionText = CreateText(
            "操作状态",
            veil.transform,
            string.Empty,
            font,
            20,
            TextAnchor.MiddleCenter);
        SetRect(
            actionText.rectTransform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1100f, 38f),
            new Vector2(0f, 36f));
        actionText.color = new Color(1f, 0.73f, 0.3f);

        BuildContextMenu(veil.transform, font);
        BuildRenameDialog(veil.transform, font);
    }

    void BuildContextMenu(Transform parent, Font font)
    {
        contextRoot = CreateRect("星球右键菜单", parent);
        Stretch(contextRoot);

        Image dismiss = CreateImage("点击空白处关闭", contextRoot, null);
        Stretch(dismiss.rectTransform);
        dismiss.color = new Color(0f, 0f, 0f, 0.01f);
        Button dismissButton = dismiss.gameObject.AddComponent<Button>();
        dismissButton.transition = Selectable.Transition.None;
        dismissButton.onClick.AddListener(CloseContextMenu);

        Image panelImage = CreateImage("菜单面板", contextRoot, null);
        contextPanel = panelImage.rectTransform;
        SetRect(
            contextPanel,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(270f, 196f),
            Vector2.zero);
        panelImage.color = new Color(0.025f, 0.09f, 0.14f, 0.98f);
        Outline outline = panelImage.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.1f, 0.85f, 1f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup layout = panelImage.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;

        CreateButton("前往星球", panelImage.transform, "前往星球", font, () =>
            SubmitVisitedTravel(contextPlanetIndex));
        CreateButton("重命名", panelImage.transform, "重命名", font, OpenRenameDialog);
        CreateButton("关闭菜单", panelImage.transform, "关闭菜单", font, CloseContextMenu);
        contextRoot.gameObject.SetActive(false);
    }

    void BuildRenameDialog(Transform parent, Font font)
    {
        renameRoot = CreateRect("重命名弹窗", parent);
        Stretch(renameRoot);
        Image blocker = renameRoot.gameObject.AddComponent<Image>();
        blocker.color = new Color(0f, 0.01f, 0.025f, 0.76f);

        Image panel = CreateImage("重命名面板", renameRoot, null);
        SetRect(
            panel.rectTransform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(640f, 300f),
            Vector2.zero);
        panel.color = new Color(0.025f, 0.09f, 0.15f, 0.99f);
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.15f, 0.85f, 1f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text title = CreateText(
            "弹窗标题",
            panel.transform,
            "重命名星球",
            font,
            28,
            TextAnchor.MiddleCenter);
        SetRect(
            title.rectTransform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(560f, 54f),
            new Vector2(0f, -40f));
        title.color = new Color(0.78f, 0.94f, 1f);

        Image inputBackground = CreateImage("名称输入框", panel.transform, null);
        SetRect(
            inputBackground.rectTransform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(540f, 58f),
            new Vector2(0f, 35f));
        inputBackground.color = new Color(0.01f, 0.035f, 0.06f, 1f);
        Outline inputOutline = inputBackground.gameObject.AddComponent<Outline>();
        inputOutline.effectColor = new Color(0.12f, 0.5f, 0.65f, 0.9f);
        inputOutline.effectDistance = new Vector2(1f, -1f);

        Text inputText = CreateText(
            "输入文字",
            inputBackground.transform,
            string.Empty,
            font,
            23,
            TextAnchor.MiddleLeft);
        Stretch(inputText.rectTransform);
        inputText.rectTransform.offsetMin = new Vector2(18f, 5f);
        inputText.rectTransform.offsetMax = new Vector2(-18f, -5f);
        inputText.raycastTarget = true;
        inputText.color = Color.white;

        Text placeholder = CreateText(
            "输入提示",
            inputBackground.transform,
            "请输入星球名称（最多 24 个字符）",
            font,
            20,
            TextAnchor.MiddleLeft);
        Stretch(placeholder.rectTransform);
        placeholder.rectTransform.offsetMin = new Vector2(18f, 5f);
        placeholder.rectTransform.offsetMax = new Vector2(-18f, -5f);
        placeholder.color = new Color(0.45f, 0.62f, 0.68f, 0.75f);

        renameInput = inputBackground.gameObject.AddComponent<InputField>();
        renameInput.textComponent = inputText;
        renameInput.placeholder = placeholder;
        renameInput.lineType = InputField.LineType.SingleLine;
        renameInput.characterLimit = 64;
        renameInput.caretColor = new Color(0.1f, 0.9f, 1f);
        renameInput.selectionColor = new Color(0.1f, 0.55f, 0.7f, 0.55f);

        renameErrorText = CreateText(
            "错误提示",
            panel.transform,
            string.Empty,
            font,
            17,
            TextAnchor.MiddleCenter);
        SetRect(
            renameErrorText.rectTransform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(560f, 34f),
            new Vector2(0f, -18f));
        renameErrorText.color = new Color(1f, 0.45f, 0.35f);

        Button confirm = CreateButton(
            "确认重命名",
            panel.transform,
            "确认",
            font,
            ConfirmRename);
        SetRect(
            confirm.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(210f, 54f),
            new Vector2(-120f, 28f));

        Button cancel = CreateButton(
            "取消重命名",
            panel.transform,
            "取消",
            font,
            CloseRenameDialog);
        SetRect(
            cancel.GetComponent<RectTransform>(),
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(210f, 54f),
            new Vector2(120f, 28f));

        renameRoot.gameObject.SetActive(false);
    }

    public void SelectVisitedPlanet(int index)
    {
        if (travelSubmitted
            || travelManager == null
            || !travelManager.IsInterstellarGalaxy
            || IsRenameOpen
            || index < 0
            || index >= travelManager.VisitedPlanets.Count)
        {
            return;
        }

        visitedSelectionIndex = index;
        if (IsContextMenuOpen)
            CloseContextMenu();
        RefreshVisitedPlanetList();
    }

    public void OpenVisitedPlanetContextMenu(int index, Vector2 screenPosition)
    {
        if (travelSubmitted
            || travelManager == null
            || !travelManager.IsInterstellarGalaxy
            || IsRenameOpen
            || index < 0
            || index >= travelManager.VisitedPlanets.Count)
        {
            return;
        }

        visitedSelectionIndex = index;
        contextPlanetIndex = index;
        RefreshVisitedPlanetList();
        contextRoot.gameObject.SetActive(true);
        contextRoot.SetAsLastSibling();

        RectTransform canvasRect = mapCanvas.transform as RectTransform;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPosition,
            null,
            out Vector2 localPosition))
        {
            Vector2 halfPanel = contextPanel.sizeDelta * 0.5f;
            Rect canvasBounds = canvasRect.rect;
            localPosition.x = Mathf.Clamp(
                localPosition.x,
                canvasBounds.xMin + halfPanel.x + 12f,
                canvasBounds.xMax - halfPanel.x - 12f);
            localPosition.y = Mathf.Clamp(
                localPosition.y,
                canvasBounds.yMin + halfPanel.y + 12f,
                canvasBounds.yMax - halfPanel.y - 12f);
            contextPanel.anchoredPosition = localPosition;
        }
    }

    void OpenRenameDialog()
    {
        int index = contextPlanetIndex >= 0 ? contextPlanetIndex : visitedSelectionIndex;
        if (index < 0 || index >= travelManager.VisitedPlanets.Count)
            return;

        visitedSelectionIndex = index;
        CloseContextMenu();
        VisitedPlanetRecord selected = travelManager.VisitedPlanets[index];
        renameInput.SetTextWithoutNotify(travelManager.GetPlanetDisplayName(selected));
        renameInput.caretPosition = renameInput.text.Length;
        renameErrorText.text = string.Empty;
        renameRoot.gameObject.SetActive(true);
        renameRoot.SetAsLastSibling();
        EventSystem.current?.SetSelectedGameObject(renameInput.gameObject);
        renameInput.ActivateInputField();
        renameInput.Select();
    }

    void ConfirmRename()
    {
        if (!IsRenameOpen
            || visitedSelectionIndex < 0
            || visitedSelectionIndex >= travelManager.VisitedPlanets.Count)
        {
            return;
        }

        string planetId = travelManager.VisitedPlanets[visitedSelectionIndex].planetId;
        if (!travelManager.RenameVisitedPlanet(
            planetId,
            renameInput.text,
            out string error))
        {
            renameErrorText.text = error;
            renameInput.ActivateInputField();
            return;
        }

        CloseRenameDialog();
        RefreshVisitedPlanetList();
        actionText.text = "星球名称已保存";
        mandatoryMessageUntil = Time.unscaledTime + 1.5f;
    }

    void CloseRenameDialog()
    {
        if (renameRoot == null)
            return;
        renameInput.DeactivateInputField();
        renameRoot.gameObject.SetActive(false);
        renameErrorText.text = string.Empty;
        EventSystem.current?.SetSelectedGameObject(null);
    }

    void CloseContextMenu()
    {
        if (contextRoot != null)
            contextRoot.gameObject.SetActive(false);
        contextPlanetIndex = -1;
    }

    void SubmitVisitedTravel(int index)
    {
        if (travelSubmitted
            || index < 0
            || index >= travelManager.VisitedPlanets.Count)
        {
            return;
        }

        VisitedPlanetRecord selected = travelManager.VisitedPlanets[index];
        string displayName = travelManager.GetPlanetDisplayName(selected);
        CloseContextMenu();
        travelSubmitted = true;
        controlsText.text = "正在锁定目的地";
        locationText.text = $"正在前往：{displayName}";
        actionText.text = "正在生成着陆数据，请稍候……";
        if (mapRaycaster != null)
            mapRaycaster.enabled = false;

        if (!travelManager.FastTravelToVisitedPlanet(selected.planetId))
        {
            travelSubmitted = false;
            if (mapRaycaster != null)
                mapRaycaster.enabled = true;
            actionText.text = "无法前往该星球，请稍后重试";
            mandatoryMessageUntil = Time.unscaledTime + 2f;
            return;
        }
    }

    void RefreshPlanetCell(int column, int row, GalaxyCoordinate coordinate)
    {
        GalaxyPlanetDefinition planet = travelManager.GetPlanetAt(coordinate);
        Image icon = planetIcons[column, row];
        if (planet == null)
        {
            icon.gameObject.SetActive(false);
            return;
        }

        icon.gameObject.SetActive(true);
        string displayName = travelManager.GetPlanetDisplayName(planet);
        icon.name = displayName;
        icon.sprite = Resources.Load<Sprite>(planet.iconResourcePath);
        icon.color = planet.tintMapIcon || icon.sprite == null ? planet.mapColor : Color.white;
        planetLabels[column, row].text = displayName;
    }

    void RefreshShipPosition()
    {
        if (shipIcon == null)
            return;

        if (travelManager.IsInterstellarGalaxy)
        {
            RefreshVisitedPlanetList();
            return;
        }

        GalaxyCoordinate position = travelManager.ShipCoordinate;
        int shipColumn = travelManager.IsInfiniteGalaxy ? 6 : (int)position.x;
        int shipRow = travelManager.IsInfiniteGalaxy ? 4 : (int)position.y;
        if (shipColumn < 0 || shipColumn >= travelManager.GridColumns
            || shipRow < 0 || shipRow >= travelManager.GridRows)
            return;

        for (int x = 0; x < travelManager.GridColumns; x++)
        {
            for (int y = 0; y < travelManager.GridRows; y++)
            {
                GalaxyCoordinate coordinate = travelManager.IsInfiniteGalaxy
                    ? position.Offset(x - shipColumn, y - shipRow)
                    : new GalaxyCoordinate(x, y);
                RefreshPlanetCell(x, y, coordinate);
            }
        }

        shipIcon.gameObject.SetActive(true);
        shipIcon.SetParent(cells[shipColumn, shipRow].transform, false);
        shipIcon.anchorMin = shipIcon.anchorMax = new Vector2(0.5f, 0.5f);
        shipIcon.anchoredPosition = Vector2.zero;
        shipIcon.localRotation = Quaternion.Euler(
            0f,
            0f,
            GetShipRotation(travelManager.ShipFacing));
        shipIcon.SetAsLastSibling();

        for (int x = 0; x < travelManager.GridColumns; x++)
        {
            for (int y = 0; y < travelManager.GridRows; y++)
            {
                bool selected = x == shipColumn && y == shipRow;
                cells[x, y].color = selected
                    ? new Color(0.08f, 0.35f, 0.46f, 0.82f)
                    : new Color(0.04f, 0.11f, 0.18f, 0.54f);
            }
        }

        GalaxyPlanetDefinition planet = travelManager.GetPlanetAt(position);
        string coordinateText = travelManager.IsInfiniteGalaxy
            ? $"{FormatCoordinate(position.x)}:{FormatCoordinate(position.y)}"
            : $"{position.x:00}:{position.y:00}";
        locationText.text = planet == null
            ? $"星区 {coordinateText}　//　空旷深空"
            : $"星区 {coordinateText}　//　{travelManager.GetPlanetDisplayName(planet)}";
        actionText.text = planet == null ? string.Empty : "按 F 进入星球轨道";
    }

    void RefreshVisitedPlanetList()
    {
        int capacity = travelManager.GridColumns * travelManager.GridRows;
        int count = Mathf.Min(travelManager.VisitedPlanets.Count, capacity);
        visitedSelectionIndex = count == 0 ? 0 : Mathf.Clamp(visitedSelectionIndex, 0, count - 1);

        for (int x = 0; x < travelManager.GridColumns; x++)
        for (int y = 0; y < travelManager.GridRows; y++)
        {
            planetIcons[x, y].gameObject.SetActive(false);
            cells[x, y].color = new Color(0.04f, 0.11f, 0.18f, 0.3f);
        }

        shipIcon.gameObject.SetActive(false);
        for (int index = 0; index < count; index++)
        {
            int column = index % travelManager.GridColumns;
            int row = travelManager.GridRows - 1 - index / travelManager.GridColumns;
            VisitedPlanetRecord visited = travelManager.VisitedPlanets[index];
            GalaxyPlanetDefinition definition = travelManager.GetPlanetAt(visited.Coordinate);
            string cellDisplayName = travelManager.GetPlanetDisplayName(visited);
            Image icon = planetIcons[column, row];
            icon.gameObject.SetActive(true);
            icon.name = cellDisplayName;
            icon.sprite = definition == null
                ? null
                : Resources.Load<Sprite>(definition.iconResourcePath);
            icon.color = definition == null
                ? new Color(0.35f, 0.75f, 0.95f)
                : definition.mapColor;
            planetLabels[column, row].text = cellDisplayName;
            cells[column, row].color = index == visitedSelectionIndex
                ? new Color(0.08f, 0.42f, 0.54f, 0.9f)
                : new Color(0.04f, 0.15f, 0.23f, 0.62f);
        }

        if (count == 0)
        {
            controlsText.text = "尚未发现可前往的星球";
            locationText.text = "暂无已访问星球";
            actionText.text = "按 Esc 或 M 返回";
            return;
        }

        controlsText.text =
            "WASD / 方向键选择　　F / Enter 前往　　右键更多操作　　Esc / M 返回";
        VisitedPlanetRecord selected = travelManager.VisitedPlanets[visitedSelectionIndex];
        string displayName = travelManager.GetPlanetDisplayName(selected);
        GalaxyPlanetDefinition selectedDefinition = travelManager.GetPlanetAt(selected.Coordinate);
        UniversePosition selectedAddress = travelManager.GetInterstellarPlanetAddress(selected.Coordinate);
        double physicalDistance = UniversePosition.Distance(
            travelManager.SavedUniversePosition,
            selectedAddress);
        string orbitalDistance = selectedDefinition?.orbit == null
            ? "--"
            : SpaceflightUnitFormatter.FormatDistance(
                selectedDefinition.orbit.semiMajorAxisMeters);
        locationText.text =
            $"{displayName}　//　轨道 {orbitalDistance}　//　距离 {SpaceflightUnitFormatter.FormatDistance(physicalDistance)}";
        if (mandatoryMessageUntil <= 0f)
        {
            PlanetPhysicalProfile physical = selectedDefinition?.celestial?.Physical;
            actionText.text = physical == null
                ? "按 F 或 Enter 前往星球"
                : $"半径 {SpaceflightUnitFormatter.FormatDistance(physical.radiusMeters)}　" +
                    $"表面重力 {SpaceflightUnitFormatter.FormatAcceleration(physical.surfaceGravity)}　" +
                    $"自转 {SpaceflightUnitFormatter.FormatDuration(physical.rotationPeriodSeconds)}　" +
                    "按 F 或 Enter 前往";
        }
    }

    bool IsContextMenuOpen => contextRoot != null && contextRoot.gameObject.activeSelf;
    bool IsRenameOpen => renameRoot != null && renameRoot.gameObject.activeSelf;

    static string FormatCoordinate(long value) => value >= 0L ? $"+{value}" : value.ToString();

    static float GetShipRotation(GalaxyShipFacing facing)
    {
        switch (facing)
        {
            case GalaxyShipFacing.Right:
                return -90f;
            case GalaxyShipFacing.Down:
                return 180f;
            case GalaxyShipFacing.Left:
                return 90f;
            default:
                return 0f;
        }
    }

    static void EnsureRenderingCamera()
    {
        if (Object.FindObjectOfType<Camera>() != null)
            return;

        GameObject cameraObject = new GameObject("GalaxyMapCamera", typeof(Camera));
        Camera mapCamera = cameraObject.GetComponent<Camera>();
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = new Color(0.005f, 0.01f, 0.025f, 1f);
        mapCamera.cullingMask = 0;
        mapCamera.orthographic = true;
        mapCamera.depth = -100f;
    }

    static void EnsureEventSystem()
    {
        EventSystem[] eventSystems = Object.FindObjectsOfType<EventSystem>();
        EventSystem selected = EventSystem.current;
        if (selected == null && eventSystems.Length > 0)
            selected = eventSystems[0];
        if (selected == null)
        {
            GameObject eventObject = new GameObject(
                "GalaxyMapEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            selected = eventObject.GetComponent<EventSystem>();
        }
        else if (selected.GetComponent<BaseInputModule>() == null)
        {
            selected.gameObject.AddComponent<StandaloneInputModule>();
        }

        foreach (EventSystem eventSystem in eventSystems)
        {
            if (eventSystem != null && eventSystem != selected)
                Object.Destroy(eventSystem.gameObject);
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    static void FocusGameViewInEditor()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (!Application.isPlaying)
                return;

            System.Type gameViewType =
                typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
                return;

            Object[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
            if (gameViews.Length > 0 && gameViews[0] is UnityEditor.EditorWindow gameView)
                gameView.Focus();
        };
#endif
    }

    static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    static Image CreateImage(string objectName, Transform parent, Sprite sprite)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        return image;
    }

    static Text CreateText(
        string objectName,
        Transform parent,
        string value,
        Font font,
        int size,
        TextAnchor alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    static Button CreateButton(
        string objectName,
        Transform parent,
        string label,
        Font font,
        UnityEngine.Events.UnityAction onClick)
    {
        Image background = CreateImage(objectName, parent, null);
        background.color = new Color(0.035f, 0.2f, 0.28f, 0.96f);
        Button button = background.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.55f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.25f, 0.75f, 0.85f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        button.onClick.AddListener(onClick);

        Text text = CreateText("文字", background.transform, label, font, 21, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        text.color = new Color(0.86f, 0.97f, 1f);
        return button;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void SetRect(
        RectTransform rect,
        Vector2 pivot,
        Vector2 anchor,
        Vector2 size,
        Vector2 position)
    {
        rect.pivot = pivot;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }
}
