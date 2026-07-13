using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(PlayerInventory))]
public sealed class InventoryUI : MonoBehaviour
{
    [SerializeField] Sprite panelSprite;
    [SerializeField] Font uiFont;
    [SerializeField] KeyCode toggleKey = KeyCode.E;
    [SerializeField, Range(4, 8)] int columns = 8;

    readonly List<InventorySlotView> views = new List<InventorySlotView>();
    GameObject inventoryPanel;
    RectTransform dragGhost;
    Image dragIcon;
    int dragSource = -1;
    bool isOpen;

    public static bool BlocksGameplayInput { get; private set; }
    public PlayerInventory Inventory { get; private set; }
    public Color SlotColor => new Color(0.07f, 0.075f, 0.08f, 0.92f);
    public Color SelectedColor => new Color(0.95f, 0.57f, 0.16f, 0.98f);

    void Awake()
    {
        Inventory = GetComponent<PlayerInventory>();
        BuildUI();
        Inventory.Changed += Refresh;
        Inventory.SelectedSlotChanged += OnSelectedChanged;
        Refresh();
    }

    void OnDestroy()
    {
        Inventory.Changed -= Refresh;
        Inventory.SelectedSlotChanged -= OnSelectedChanged;
        if (isOpen)
            BlocksGameplayInput = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            SetOpen(!isOpen);
    }

    public void BeginDrag(int index, Vector2 position)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(41, (int)index);
        InventorySlot slot = Inventory.Slots[index];
        if (slot.IsEmpty)
            return;

        dragSource = index;
        dragIcon.sprite = slot.item.Icon;
        dragIcon.enabled = true;
        UpdateDrag(position);
    }

    public void UpdateDrag(Vector2 position)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(42);
        if (dragSource >= 0)
            dragGhost.position = position;
    }

    public void DropOn(int target, bool split)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(43, (int)target, (split?1:0));
        if (dragSource < 0)
            return;

        if (split)
            Inventory.SplitHalf(dragSource, target);
        else
            Inventory.MoveOrMerge(dragSource, target);
        EndDrag();
    }

    public void EndDrag()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(44);
        dragSource = -1;
        if (dragIcon != null)
            dragIcon.enabled = false;
    }

    void SetOpen(bool open)
    {
        isOpen = open;
        BlocksGameplayInput = open;
        inventoryPanel.SetActive(open);
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        EndDrag();
    }

    void OnSelectedChanged(int _) => Refresh();

    void Refresh()
    {
        foreach (InventorySlotView view in views)
            view.Refresh(
                Inventory.Slots[view.SlotIndex],
                view.IsHotbar && view.SlotIndex == Inventory.SelectedSlotIndex
            );
    }

    void BuildUI()
    {
        Canvas canvas = CreateObject<Canvas>("Inventory Canvas", transform);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();
        BuildHotbar(canvas.transform);
        BuildInventoryPanel(canvas.transform);
        BuildDragGhost(canvas.transform);
        inventoryPanel.SetActive(false);
    }

    void BuildHotbar(Transform parent)
    {
        RectTransform bar = CreatePanel("Hotbar", parent, null, new Color(0.025f, 0.03f, 0.035f, 0.86f));
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 28f);
        bar.sizeDelta = new Vector2(Inventory.HotbarSize * 78f + 28f, 96f);
        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = false;
        for (int i = 0; i < Inventory.HotbarSize; i++)
            CreateSlot(bar, i, true);
    }

    void BuildInventoryPanel(Transform parent)
    {
        Color panelColor = panelSprite == null ? new Color(0.12f, 0.12f, 0.12f, 0.98f) : Color.white;
        RectTransform panel = CreatePanel("Backpack", parent, panelSprite, panelColor);
        inventoryPanel = panel.gameObject;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(820f, 580f);

        Text title = CreateText("Title", panel, "BACKPACK", 32, TextAnchor.MiddleCenter);
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -38f);
        titleRect.sizeDelta = new Vector2(0f, 48f);
        title.color = new Color(0.92f, 0.79f, 0.57f);

        RectTransform grid = CreateObject<RectTransform>("Grid", panel);
        grid.anchorMin = new Vector2(0.5f, 0.5f);
        grid.anchorMax = new Vector2(0.5f, 0.5f);
        grid.pivot = new Vector2(0.5f, 0.5f);
        grid.anchoredPosition = new Vector2(0f, -20f);
        grid.sizeDelta = new Vector2(columns * 78f, Mathf.Ceil(Inventory.Slots.Count / (float)columns) * 78f);
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(70f, 70f);
        layout.spacing = new Vector2(8f, 8f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        for (int i = 0; i < Inventory.Slots.Count; i++)
            CreateSlot(grid, i, false);
    }

    void CreateSlot(Transform parent, int index, bool hotbar)
    {
        string prefix = hotbar ? "HotbarSlot_" : "InventorySlot_";
        RectTransform slot = CreatePanel(prefix + index, parent, null, SlotColor);
        slot.sizeDelta = new Vector2(70f, 70f);
        Outline outline = slot.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.58f, 0.43f, 0.25f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);

        Image icon = CreateObject<Image>("Icon", slot);
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        SetStretch(icon.rectTransform, 9f);

        Text amount = CreateText("Amount", slot, string.Empty, 18, TextAnchor.LowerRight);
        SetStretch(amount.rectTransform, 4f);
        amount.raycastTarget = false;

        Text key = CreateText("Key", slot, string.Empty, 14, TextAnchor.UpperLeft);
        SetStretch(key.rectTransform, 5f);
        key.color = new Color(0.86f, 0.7f, 0.42f);
        key.raycastTarget = false;

        InventorySlotView view = slot.gameObject.AddComponent<InventorySlotView>();
        view.Initialize(this, index, hotbar);
        views.Add(view);
    }

    void BuildDragGhost(Transform parent)
    {
        dragGhost = CreateObject<RectTransform>("Dragged Item", parent);
        dragGhost.sizeDelta = new Vector2(58f, 58f);
        dragIcon = dragGhost.gameObject.AddComponent<Image>();
        dragIcon.raycastTarget = false;
        dragIcon.preserveAspect = true;
        dragIcon.enabled = false;
    }

    static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
            return;
        GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    static RectTransform CreatePanel(string name, Transform parent, Sprite sprite, Color color)
    {
        Image image = CreateObject<Image>(name, parent);
        image.sprite = sprite;
        image.color = color;
        image.type = Image.Type.Simple;
        return image.rectTransform;
    }

    Text CreateText(string name, Transform parent, string value, int size, TextAnchor alignment)
    {
        Text text = CreateObject<Text>(name, parent);
        text.font = uiFont != null
            ? uiFont
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    static T CreateObject<T>(string name, Transform parent) where T : Component
    {
        GameObject go = typeof(T) == typeof(RectTransform)
            ? new GameObject(name, typeof(RectTransform))
            : new GameObject(name, typeof(RectTransform), typeof(T));
        go.transform.SetParent(parent, false);
        return go.GetComponent<T>();
    }

    static void SetStretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
