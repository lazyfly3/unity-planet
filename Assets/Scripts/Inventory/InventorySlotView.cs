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
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(46, (int)index, (isHotbar?1:0));}
        owner = inventoryUI;
        slotIndex = index;
        IsHotbar = isHotbar;
        background = GetComponent<Image>();
        icon = transform.Find("Icon").GetComponent<Image>();
        amount = transform.Find("Amount").GetComponent<Text>();
        keyLabel = transform.Find("Key").GetComponent<Text>();
        keyLabel.text = isHotbar ? (index + 1).ToString() : string.Empty;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public void Refresh(InventorySlot slot, bool selected)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(47, (selected?1:0));}
        bool hasItem = slot != null && !slot.IsEmpty;
        icon.enabled = hasItem;
        icon.sprite = hasItem ? slot.item.Icon : null;
        amount.text = hasItem && slot.amount > 1 ? slot.amount.ToString() : string.Empty;
        background.color = selected ? owner.SelectedColor : owner.SlotColor;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public void OnBeginDrag(PointerEventData eventData)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(48);}
        owner.BeginDrag(slotIndex, eventData.position);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public void OnDrag(PointerEventData eventData)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(49);}
        owner.UpdateDrag(eventData.position);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public void OnEndDrag(PointerEventData eventData)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(50);}
        owner.EndDrag();
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public void OnDrop(PointerEventData eventData)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(51);}
        owner.DropOn(slotIndex, eventData.button == PointerEventData.InputButton.Right);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public void OnPointerClick(PointerEventData eventData)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(52);}
        if (eventData.button == PointerEventData.InputButton.Left && slotIndex < owner.Inventory.HotbarSize)
            owner.Inventory.SelectSlot(slotIndex);
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
}
