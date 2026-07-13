using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerInventory : MonoBehaviour
{
    [SerializeField, Min(1)] int slotCount = 32;
    [SerializeField, Range(1, 9)] int hotbarSize = 8;
    [SerializeField] List<InventorySlot> slots = new List<InventorySlot>();

    public event Action Changed;
    public event Action<int> SelectedSlotChanged;
    public event Action<InventoryItem> ItemUsed;

    public IReadOnlyList<InventorySlot> Slots => slots;
    public int HotbarSize => Mathf.Min(hotbarSize, slots.Count);
    public int SelectedSlotIndex { get; private set; }
    public InventorySlot SelectedSlot => slots.Count == 0 ? null : slots[SelectedSlotIndex];

    void Awake()
    {
        EnsureSize();
    }

    void OnValidate()
    {
        EnsureSize();
    }

    void Update()
    {
        if (InventoryUI.BlocksGameplayInput)
            return;

        for (int i = 0; i < HotbarSize; i++)
        {
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                SelectSlot(i);
        }

        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0f && HotbarSize > 0)
            SelectSlot((SelectedSlotIndex + (wheel > 0f ? -1 : 1) + HotbarSize) % HotbarSize);

        if (Input.GetMouseButtonDown(1))
            UseSelectedItem();
    }

    public int Add(InventoryItem item, int amount = 1)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(40, (int)amount);
        if (item == null || amount <= 0)
            return amount;

        for (int i = 0; i < slots.Count && amount > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.item != item || slot.amount >= item.MaxStack)
                continue;

            int moved = Mathf.Min(amount, item.MaxStack - slot.amount);
            slot.amount += moved;
            amount -= moved;
        }

        for (int i = 0; i < slots.Count && amount > 0; i++)
        {
            if (!slots[i].IsEmpty)
                continue;

            int moved = Mathf.Min(amount, item.MaxStack);
            slots[i].Set(item, moved);
            amount -= moved;
        }

        Changed?.Invoke();
        return amount;
    }

    public bool CanAdd(InventoryItem item, int amount = 1)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(47, (int)amount);
        if (item == null || amount <= 0)
            return false;

        int capacity = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.IsEmpty)
                capacity += item.MaxStack;
            else if (slot.item == item)
                capacity += Mathf.Max(0, item.MaxStack - slot.amount);

            if (capacity >= amount)
                return true;
        }

        return false;
    }

    public bool Remove(InventoryItem item, int amount = 1)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(41, (int)amount);
        if (item == null || amount <= 0 || Count(item) < amount)
            return false;

        for (int i = slots.Count - 1; i >= 0 && amount > 0; i--)
        {
            InventorySlot slot = slots[i];
            if (slot.item != item)
                continue;

            int removed = Mathf.Min(amount, slot.amount);
            slot.amount -= removed;
            amount -= removed;
            if (slot.amount == 0)
                slot.Clear();
        }

        Changed?.Invoke();
        return true;
    }

    public int Count(InventoryItem item)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(42);
        int total = 0;
        foreach (InventorySlot slot in slots)
            if (slot.item == item)
                total += slot.amount;
        return total;
    }

    public void MoveOrMerge(int from, int to)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(43, (int)from, (int)to);
        if (!IsValid(from) || !IsValid(to) || from == to || slots[from].IsEmpty)
            return;

        InventorySlot source = slots[from];
        InventorySlot target = slots[to];
        if (target.IsEmpty)
        {
            target.Set(source.item, source.amount);
            source.Clear();
        }
        else if (target.item == source.item && target.amount < target.item.MaxStack)
        {
            int moved = Mathf.Min(source.amount, target.item.MaxStack - target.amount);
            target.amount += moved;
            source.amount -= moved;
            if (source.amount == 0)
                source.Clear();
        }
        else
        {
            InventoryItem item = source.item;
            int amount = source.amount;
            source.Set(target.item, target.amount);
            target.Set(item, amount);
        }

        Changed?.Invoke();
    }

    public void SplitHalf(int from, int to)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(44, (int)from, (int)to);
        if (!IsValid(from) || !IsValid(to) || from == to || slots[from].amount < 2 || !slots[to].IsEmpty)
            return;

        int moved = slots[from].amount / 2;
        slots[to].Set(slots[from].item, moved);
        slots[from].amount -= moved;
        Changed?.Invoke();
    }

    public void SelectSlot(int index)
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(45, (int)index);
        if (index < 0 || index >= HotbarSize || index == SelectedSlotIndex)
            return;

        SelectedSlotIndex = index;
        SelectedSlotChanged?.Invoke(index);
    }

    public void UseSelectedItem()
    {if(FSPDebuger.EnableLogTrackInternal)FSPDebuger.LogTrack(46);
        if (SelectedSlot != null && !SelectedSlot.IsEmpty)
            ItemUsed?.Invoke(SelectedSlot.item);
    }

    void EnsureSize()
    {
        slotCount = Mathf.Max(1, slotCount);
        while (slots.Count < slotCount)
            slots.Add(new InventorySlot());
        if (slots.Count > slotCount)
            slots.RemoveRange(slotCount, slots.Count - slotCount);
        SelectedSlotIndex = Mathf.Clamp(SelectedSlotIndex, 0, Mathf.Max(0, HotbarSize - 1));
    }

    bool IsValid(int index) => index >= 0 && index < slots.Count;
}
