using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class InventorySlotView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerClickHandler
{
    InventoryUI owner;
    int slotIndex;
    Image background;
    Image icon;
    Text amount;
    Text keyLabel;

    public int SlotIndex => slotIndex;
    public bool IsHotbar { get; private set; }

    public void Initialize(InventoryUI inventoryUI, int index, bool isHotbar)
    {
owner = inventoryUI;
        slotIndex = index;
        IsHotbar = isHotbar;
        background = GetComponent<Image>();
        icon = transform.Find("Icon").GetComponent<Image>();
        amount = transform.Find("Amount").GetComponent<Text>();
        keyLabel = transform.Find("Key").GetComponent<Text>();
        keyLabel.text = isHotbar ? (index + 1).ToString() : string.Empty;
    
}

    public void Refresh(InventorySlot slot, bool selected)
    {
bool hasItem = slot != null && !slot.IsEmpty;
        icon.enabled = hasItem;
        icon.sprite = hasItem ? slot.item.Icon : null;
        amount.text = hasItem && slot.amount > 1 ? slot.amount.ToString() : string.Empty;
        background.color = selected ? owner.SelectedColor : owner.SlotColor;
    
}

    public void OnBeginDrag(PointerEventData eventData)
    {
owner.BeginDrag(slotIndex, eventData.position);
    
}

    public void OnDrag(PointerEventData eventData)
    {
owner.UpdateDrag(eventData.position);
    
}

    public void OnEndDrag(PointerEventData eventData)
    {
owner.EndDrag();
    
}

    public void OnDrop(PointerEventData eventData)
    {
owner.DropOn(slotIndex, eventData.button == PointerEventData.InputButton.Right);
    
}

    public void OnPointerClick(PointerEventData eventData)
    {
if (eventData.button == PointerEventData.InputButton.Left && slotIndex < owner.Inventory.HotbarSize)
            owner.Inventory.SelectSlot(slotIndex);
    
}
}
