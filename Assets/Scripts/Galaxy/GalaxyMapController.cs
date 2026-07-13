using UnityEngine;
using UnityEngine.UI;

public sealed class GalaxyMapController : MonoBehaviour
{
    [Header("Navigation Input")]
    [SerializeField, Min(0f)] float initialRepeatDelay = 0.28f;
    [SerializeField, Min(0.03f)] float repeatInterval = 0.11f;

    Image[,] cells;

    GalaxyTravelManager travelManager;
    RectTransform shipIcon;
    Text locationText;
    Text actionText;
    Vector2Int heldMoveDirection;
    float nextMoveTime;

    void Awake()
    {
        EnsureRenderingCamera();
        FocusGameViewInEditor();

        travelManager = GalaxyTravelManager.Instance;
        if (travelManager == null)
        {
            Debug.LogError("GalaxyMapController: GalaxyTravelManager is missing.");
            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        BuildInterface();
        RefreshShipPosition();
    }

    void Update()
    {
        if (travelManager == null)
            return;

        HandleMovementInput();

        if (Input.GetKeyDown(KeyCode.F))
        {
            GalaxyPlanetDefinition planet = travelManager.GetPlanetAt(travelManager.ShipGridPosition);
            if (planet != null)
                travelManager.EnterPlanet(planet);
        }
    }

    void HandleMovementInput()
    {
        Vector2Int direction = ReadHeldDirection();
        if (direction == Vector2Int.zero)
        {
            heldMoveDirection = Vector2Int.zero;
            return;
        }

        bool directionChanged = direction != heldMoveDirection;
        if (!directionChanged && Time.unscaledTime < nextMoveTime)
            return;

        travelManager.MoveShip(direction);
        RefreshShipPosition();

        heldMoveDirection = direction;
        nextMoveTime = Time.unscaledTime + (directionChanged ? initialRepeatDelay : repeatInterval);
    }

    static Vector2Int ReadHeldDirection()
    {
        int horizontal = 0;
        int vertical = 0;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            horizontal--;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            horizontal++;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            vertical--;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            vertical++;

        return new Vector2Int(horizontal, vertical);
    }

    void BuildInterface()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject("GalaxyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image background = CreateImage("StarMapBackground", canvas.transform, null);
        Stretch(background.rectTransform);
        background.sprite = Resources.Load<Sprite>("Galaxy/star_map_background");
        background.color = background.sprite != null ? Color.white : new Color(0.015f, 0.025f, 0.06f, 1f);
        background.type = Image.Type.Simple;
        background.preserveAspect = false;

        Image veil = CreateImage("InterfaceVeil", background.transform, null);
        Stretch(veil.rectTransform);
        veil.color = new Color(0.01f, 0.025f, 0.055f, 0.32f);

        Text title = CreateText("Title", veil.transform, "DEEP SPACE NAVIGATION", font, 34, TextAnchor.MiddleCenter);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(900f, 62f), new Vector2(0f, -48f));
        title.color = new Color(0.75f, 0.91f, 1f);

        Text controls = CreateText("Controls", veil.transform, "WASD  MOVE ONE SECTOR     F  ENTER PLANET", font, 19, TextAnchor.MiddleCenter);
        SetRect(controls.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(900f, 38f), new Vector2(0f, -92f));
        controls.color = new Color(0.45f, 0.7f, 0.82f);

        RectTransform grid = CreateRect("SectorGrid", veil.transform);
        SetRect(grid, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1270f, 710f), new Vector2(0f, -5f));
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.spacing = new Vector2(10f, 10f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = travelManager.GridColumns;
        layout.childAlignment = TextAnchor.MiddleCenter;
        float cellWidth = (1270f - layout.spacing.x * (travelManager.GridColumns - 1)) / travelManager.GridColumns;
        float cellHeight = (710f - layout.spacing.y * (travelManager.GridRows - 1)) / travelManager.GridRows;
        layout.cellSize = new Vector2(cellWidth, cellHeight);
        cells = new Image[travelManager.GridColumns, travelManager.GridRows];

        for (int row = travelManager.GridRows - 1; row >= 0; row--)
        {
            for (int column = 0; column < travelManager.GridColumns; column++)
            {
                Image cell = CreateImage($"Sector_{column}_{row}", grid, null);
                cell.color = new Color(0.04f, 0.11f, 0.18f, 0.54f);
                Outline outline = cell.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.18f, 0.5f, 0.68f, 0.48f);
                outline.effectDistance = new Vector2(1f, -1f);
                cells[column, row] = cell;
            }
        }

        foreach (GalaxyPlanetDefinition planet in travelManager.Planets)
            CreatePlanetIcon(planet, font);

        Image ship = CreateImage("Ship", veil.transform, Resources.Load<Sprite>("Galaxy/ship"));
        ship.rectTransform.sizeDelta = new Vector2(62f, 62f);
        ship.preserveAspect = true;
        ship.color = ship.sprite != null ? Color.white : new Color(0.85f, 0.95f, 1f);
        shipIcon = ship.rectTransform;

        locationText = CreateText("Location", veil.transform, string.Empty, font, 24, TextAnchor.MiddleCenter);
        SetRect(locationText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(920f, 44f), new Vector2(0f, 72f));
        locationText.color = new Color(0.8f, 0.94f, 1f);

        actionText = CreateText("Action", veil.transform, string.Empty, font, 20, TextAnchor.MiddleCenter);
        SetRect(actionText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(920f, 38f), new Vector2(0f, 36f));
        actionText.color = new Color(1f, 0.73f, 0.3f);
    }

    void CreatePlanetIcon(GalaxyPlanetDefinition planet, Font font)
    {
        if (!IsInsideGrid(planet.gridPosition))
            return;

        RectTransform cell = cells[planet.gridPosition.x, planet.gridPosition.y].rectTransform;
        Image icon = CreateImage(planet.displayName, cell, Resources.Load<Sprite>(planet.iconResourcePath));
        Stretch(icon.rectTransform);
        icon.rectTransform.offsetMin = new Vector2(12f, 12f);
        icon.rectTransform.offsetMax = new Vector2(-12f, -12f);
        icon.preserveAspect = true;
        icon.color = icon.sprite != null ? Color.white : planet.mapColor;

        Text label = CreateText("Label", icon.transform, planet.displayName.ToUpperInvariant(), font, 11, TextAnchor.LowerCenter);
        Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(-22f, -18f);
        label.rectTransform.offsetMax = new Vector2(22f, 0f);
        label.color = new Color(0.82f, 0.92f, 1f);
    }

    void RefreshShipPosition()
    {
        Vector2Int position = travelManager.ShipGridPosition;
        if (!IsInsideGrid(position) || shipIcon == null)
            return;

        shipIcon.SetParent(cells[position.x, position.y].transform, false);
        shipIcon.anchorMin = shipIcon.anchorMax = new Vector2(0.5f, 0.5f);
        shipIcon.anchoredPosition = Vector2.zero;
        shipIcon.SetAsLastSibling();

        for (int x = 0; x < travelManager.GridColumns; x++)
        {
            for (int y = 0; y < travelManager.GridRows; y++)
            {
                bool selected = x == position.x && y == position.y;
                cells[x, y].color = selected
                    ? new Color(0.08f, 0.35f, 0.46f, 0.82f)
                    : new Color(0.04f, 0.11f, 0.18f, 0.54f);
            }
        }

        GalaxyPlanetDefinition planet = travelManager.GetPlanetAt(position);
        locationText.text = planet == null
            ? $"SECTOR  {position.x:00}:{position.y:00}   //   EMPTY SPACE"
            : $"SECTOR  {position.x:00}:{position.y:00}   //   {planet.displayName.ToUpperInvariant()}";
        actionText.text = planet == null ? string.Empty : "PRESS F TO ENTER ORBIT";
    }

    bool IsInsideGrid(Vector2Int position)
    {
        return position.x >= 0 && position.x < travelManager.GridColumns
            && position.y >= 0 && position.y < travelManager.GridRows;
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

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    static void FocusGameViewInEditor()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (!Application.isPlaying)
                return;

            System.Type gameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
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

    static Text CreateText(string objectName, Transform parent, string value, Font font, int size, TextAnchor alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void SetRect(RectTransform rect, Vector2 pivot, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.pivot = pivot;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }
}
