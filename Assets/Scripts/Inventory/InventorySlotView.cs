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
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(80, (int)index, (isHotbar?1:0));}
    try
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
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void Refresh(InventorySlot slot, bool selected)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(81, (selected?1:0));}
    try
    {
        bool hasItem = slot != null && !slot.IsEmpty;
        icon.enabled = hasItem;
        icon.sprite = hasItem ? slot.item.Icon : null;
        amount.text = hasItem && slot.amount > 1 ? slot.amount.ToString() : string.Empty;
        background.color = selected ? owner.SelectedColor : owner.SlotColor;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void OnBeginDrag(PointerEventData eventData)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(82);}
    try
    {
        owner.BeginDrag(slotIndex, eventData.position);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void OnDrag(PointerEventData eventData)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(83);}
    try
    {
        owner.UpdateDrag(eventData.position);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void OnEndDrag(PointerEventData eventData)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(84);}
    try
    {
        owner.EndDrag();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void OnDrop(PointerEventData eventData)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(85);}
    try
    {
        owner.DropOn(slotIndex, eventData.button == PointerEventData.InputButton.Right);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void OnPointerClick(PointerEventData eventData)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(86);}
    try
    {
        if (eventData.button == PointerEventData.InputButton.Left && slotIndex < owner.Inventory.HotbarSize)
            owner.Inventory.SelectSlot(slotIndex);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
