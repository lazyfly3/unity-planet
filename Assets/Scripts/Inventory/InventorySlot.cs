using System;

[Serializable]
public sealed class InventorySlot
{
    public InventoryItem item;
    public int amount;

    public bool IsEmpty => item == null || amount <= 0;
    public int FreeSpace => IsEmpty ? 0 : item.MaxStack - amount;

    public void Set(InventoryItem newItem, int newAmount)
    {
item = newItem;
        amount = newItem == null ? 0 : Math.Min(newAmount, newItem.MaxStack);
        if (amount <= 0)
            Clear();
    
}

    public void Clear()
    {
item = null;
        amount = 0;
    
}
}
