using UnityEngine;

[CreateAssetMenu(menuName = "Voxel Planet/Inventory Item", fileName = "New Inventory Item")]
public sealed class InventoryItem : ScriptableObject
{
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
}
