using System;
using System.Collections.Generic;

public interface ILogTrackList<T> : IDisposable
{
    void Add(T item);
    void Clear();
    List<T> ToList();
    int GetMemorySize();
}

public sealed class LogTrackListEmpty<T> : ILogTrackList<T>
{
    public void Add(T item) { }
    public void Clear() { }
    public List<T> ToList() => new List<T>();
    public int GetMemorySize() => 0;
    public void Dispose() { }
}

/// <summary>
/// 原版可切换 Marshal 实现；复现版用 List + 预分配容量。
/// </summary>
public sealed class LogTrackList<T> : ILogTrackList<T>
{
    private readonly List<T> m_list;
    private readonly int m_capacityStep;
    private int m_allocCount;

    public LogTrackList(int capacityStep, int initialCapacity)
    {
        m_capacityStep = capacityStep;
        m_list = new List<T>(initialCapacity);
    }

    public void Add(T item)
    {
        if (m_list.Capacity == m_list.Count)
        {
            m_list.Capacity += m_capacityStep;
            m_allocCount++;
        }
        m_list.Add(item);
    }

    public void Clear() => m_list.Clear();
    public List<T> ToList() => new List<T>(m_list);
    public int GetMemorySize() => m_list.Count * System.Runtime.InteropServices.Marshal.SizeOf<T>();
    public void Dispose() => m_list.Clear();
}
