using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(VoxelPlanetPlayerController), typeof(PlayerInventory))]
public sealed class SurfaceToolController : MonoBehaviour
{
    const string ToolResourcePath = "SurfaceTools";
    const string ScannerItemId = "tool.scanner";

    readonly Dictionary<string, SurfaceToolDefinition> definitions =
        new Dictionary<string, SurfaceToolDefinition>(StringComparer.Ordinal);
    VoxelPlanetPlayerController player;
    PlayerInventory inventory;
    BuildingPlacer buildingPlacer;
    SurfaceFirstPersonArmsController arms;
    SurfaceBiotaScannerTool biotaScanner;
    SurfaceMultifunctionController multifunction;
    bool externalInputBlocked;

    public SurfaceToolDefinition ActiveTool { get; private set; }
    public bool IsActionBusy =>
        biotaScanner != null && biotaScanner.IsScanActive
        || arms != null && arms.IsActionPlaying;
    public bool ConsumesPrimaryAction =>
        ActiveTool != null
        && !IsBuildMode
        && !IsInputBlocked;
    public bool ConsumesSecondaryAction =>
        ActiveTool != null
        && !IsBuildMode
        && !IsInputBlocked;
    public bool IsInputBlocked =>
        externalInputBlocked
        || InventoryUI.BlocksGameplayInput
        || (multifunction != null && multifunction.IsOpen);
    bool IsBuildMode => buildingPlacer != null && buildingPlacer.IsBuildMode;

    void Awake()
    {
        player = GetComponent<VoxelPlanetPlayerController>();
        inventory = GetComponent<PlayerInventory>();
        buildingPlacer = GetComponent<BuildingPlacer>();
        multifunction = GetComponent<SurfaceMultifunctionController>();
        arms = GetComponent<SurfaceFirstPersonArmsController>()
            ?? gameObject.AddComponent<SurfaceFirstPersonArmsController>();
        biotaScanner = GetComponent<SurfaceBiotaScannerTool>()
            ?? gameObject.AddComponent<SurfaceBiotaScannerTool>();
        LoadDefinitions();
        inventory.SelectedSlotChanged += HandleSelectedSlotChanged;
        inventory.Changed += HandleInventoryChanged;
        SurfaceToolInputRouter.Register(this);
    }

    void Start()
    {
        InventoryItem scannerItem =
            Resources.Load<InventoryItem>("SurfaceTools/ScannerItem");
        if (scannerItem != null)
        {
            GalaxyTravelManager manager = GalaxyTravelManager.Instance;
            if (manager != null)
                manager.EnsureStarterScanner(inventory, scannerItem);
            else
                inventory.TryAddUniqueById(scannerItem);
        }
        EquipSelectedItem();
    }

    void Update()
    {
        bool blocked = IsInputBlocked;
        arms?.SetInputBlocked(blocked);
        biotaScanner?.SetInputBlocked(blocked);
        if (blocked || IsBuildMode || ActiveTool == null)
            return;
        if (Input.GetMouseButtonDown(0))
            TryPrimaryUse();
    }

    public bool TryPrimaryUse()
    {
        if (ActiveTool == null || IsInputBlocked || IsBuildMode || IsActionBusy)
            return false;
        switch (ActiveTool.ActionType)
        {
            case SurfaceToolActionType.BiotaScanner:
                if (biotaScanner == null
                    || !biotaScanner.BeginScan(
                        ActiveTool.ActionDuration,
                        ActiveTool.ActionRange))
                {
                    return false;
                }
                arms?.BeginToolAction(ActiveTool.ActionDuration);
                return true;
            default:
                return false;
        }
    }

    public void SetInputBlocked(bool blocked)
    {
        externalInputBlocked = blocked;
        arms?.SetInputBlocked(blocked);
        biotaScanner?.SetInputBlocked(blocked);
    }

    public void RefreshEquipment()
    {
        EquipSelectedItem();
    }

    void LoadDefinitions()
    {
        definitions.Clear();
        SurfaceToolDefinition[] loaded =
            Resources.LoadAll<SurfaceToolDefinition>(ToolResourcePath);
        for (int index = 0; index < loaded.Length; index++)
        {
            SurfaceToolDefinition definition = loaded[index];
            if (definition == null
                || string.IsNullOrWhiteSpace(definition.ItemId)
                || definitions.ContainsKey(definition.ItemId))
            {
                continue;
            }
            definitions.Add(definition.ItemId, definition);
        }
    }

    void EquipSelectedItem()
    {
        InventorySlot slot = inventory != null ? inventory.SelectedSlot : null;
        string itemId = slot != null && !slot.IsEmpty && slot.item != null
            ? slot.item.ItemId
            : string.Empty;
        definitions.TryGetValue(itemId, out SurfaceToolDefinition definition);
        if (ActiveTool == definition)
            return;
        biotaScanner?.SetInputBlocked(true);
        ActiveTool = definition;
        arms?.EquipTool(definition);
        biotaScanner?.SetInputBlocked(IsInputBlocked);
    }

    void HandleSelectedSlotChanged(int _)
    {
        EquipSelectedItem();
    }

    void HandleInventoryChanged()
    {
        EquipSelectedItem();
    }

    void OnDestroy()
    {
        if (inventory != null)
        {
            inventory.SelectedSlotChanged -= HandleSelectedSlotChanged;
            inventory.Changed -= HandleInventoryChanged;
        }
        SurfaceToolInputRouter.Unregister(this);
    }

    public static bool IsScannerItem(InventoryItem item)
    {
        return item != null
            && string.Equals(item.ItemId, ScannerItemId, StringComparison.Ordinal);
    }
}
