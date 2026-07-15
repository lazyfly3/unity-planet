using System;

[Serializable]
public sealed class InventorySlot
{
    public InventoryItem item;
    public int amount;

    public bool IsEmpty => item == null || amount <= 0;
    public int FreeSpace => IsEmpty ? 0 : item.MaxStack - amount;

    public void Set(InventoryItem newItem, int newAmount)
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(44, (int)newAmount);}
        item = newItem;
        amount = newItem == null ? 0 : Math.Min(newAmount, newItem.MaxStack);
        if (amount <= 0)
            Clear();
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}

    public void Clear()
    {if(FSPDebuger.EnableLogTrackInternal){FSPDebuger.PushDepth();FSPDebuger.LogTrack(45);}
        item = null;
        amount = 0;
    
    if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.PopDepth();}
}
