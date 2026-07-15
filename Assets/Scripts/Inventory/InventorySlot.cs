using System;

[Serializable]
public sealed class InventorySlot
{
    public InventoryItem item;
    public int amount;

    public bool IsEmpty => item == null || amount <= 0;
    public int FreeSpace => IsEmpty ? 0 : item.MaxStack - amount;

    public void Set(InventoryItem newItem, int newAmount)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(78, (int)newAmount);}
    try
    {
        item = newItem;
        amount = newItem == null ? 0 : Math.Min(newAmount, newItem.MaxStack);
        if (amount <= 0)
            Clear();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void Clear()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(79);}
    try
    {
        item = null;
        amount = 0;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}
