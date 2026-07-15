using UnityEngine;
using UnityEngine.UI;

public sealed class GalaxyMapController : MonoBehaviour
{
    [Header("Navigation Input")]
    [SerializeField, Min(0f)] float initialRepeatDelay = 0.28f;
    [SerializeField, Min(0.03f)] float repeatInterval = 0.11f;

    Image[,] cells;
    Image[,] planetIcons;
    Text[,] planetLabels;

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
            GalaxyPlanetDefinition planet = travelManager.GetPlanetAt(travelManager.ShipCoordinate);
            if (planet != null)
                travelManager.EnterPlanet(planet);
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
        int pressedDirectionCount = (left ? 1 : 0) + (right ? 1 : 0) + (down ? 1 : 0) + (up ? 1 : 0);
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
        planetIcons = new Image[travelManager.GridColumns, travelManager.GridRows];
        planetLabels = new Text[travelManager.GridColumns, travelManager.GridRows];

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

                Image icon = CreateImage("PlanetIcon", cell.transform, null);
                Stretch(icon.rectTransform);
                icon.rectTransform.offsetMin = new Vector2(12f, 12f);
                icon.rectTransform.offsetMax = new Vector2(-12f, -12f);
                icon.preserveAspect = true;
                Text label = CreateText("Label", icon.transform, string.Empty, font, 11, TextAnchor.LowerCenter);
                Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(-22f, -18f);
                label.rectTransform.offsetMax = new Vector2(22f, 0f);
                label.color = new Color(0.82f, 0.92f, 1f);
                icon.gameObject.SetActive(false);
                planetIcons[column, row] = icon;
                planetLabels[column, row] = label;
            }
        }

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
        icon.name = planet.displayName;
        icon.sprite = Resources.Load<Sprite>(planet.iconResourcePath);
        icon.color = planet.tintMapIcon || icon.sprite == null ? planet.mapColor : Color.white;
        planetLabels[column, row].text = planet.displayName.ToUpperInvariant();
    }

    void RefreshShipPosition()
    {
        if (shipIcon == null)
            return;

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

        shipIcon.SetParent(cells[shipColumn, shipRow].transform, false);
        shipIcon.anchorMin = shipIcon.anchorMax = new Vector2(0.5f, 0.5f);
        shipIcon.anchoredPosition = Vector2.zero;
        shipIcon.localRotation = Quaternion.Euler(0f, 0f, GetShipRotation(travelManager.ShipFacing));
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
            ? $"SECTOR  {coordinateText}   //   EMPTY SPACE"
            : $"SECTOR  {coordinateText}   //   {planet.displayName.ToUpperInvariant()}";
        actionText.text = planet == null ? string.Empty : "PRESS F TO ENTER ORBIT";
    }

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
