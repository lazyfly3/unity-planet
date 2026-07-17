using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Voxel Planet/Inventory Item", fileName = "New Inventory Item")]
public sealed class InventoryItem : ScriptableObject
{
    static readonly Dictionary<string, InventoryItem> RuntimeItems = new Dictionary<string, InventoryItem>();

    [SerializeField] string itemId = "item";
    [SerializeField] string displayName = "Item";
    [SerializeField, TextArea] string description;
    [SerializeField] Sprite icon;
    [SerializeField, Min(1)] int maxStack = 99;

    public string ItemId => itemId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public int MaxStack => Mathf.Max(1, maxStack);

    public static InventoryItem GetOrCreateRuntime(
        string id,
        string name,
        Sprite itemIcon,
        int itemMaxStack)
    {
id = string.IsNullOrWhiteSpace(id) ? "item" : id.Trim();
        if (RuntimeItems.TryGetValue(id, out InventoryItem existing) && existing != null)
        {
            if (!string.IsNullOrWhiteSpace(name))
                existing.displayName = name.Trim();
            if (itemIcon != null)
                existing.icon = itemIcon;
            existing.maxStack = Mathf.Max(1, itemMaxStack);
            return existing;
        }

        InventoryItem item = CreateInstance<InventoryItem>();
        item.name = $"RuntimeItem_{id}";
        item.hideFlags = HideFlags.DontUnloadUnusedAsset;
        item.itemId = id;
        item.displayName = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
        item.icon = itemIcon;
        item.maxStack = Mathf.Max(1, itemMaxStack);
        RuntimeItems[id] = item;
        return item;
    
}

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeItems()
    {
        RuntimeItems.Clear();
    }
}
