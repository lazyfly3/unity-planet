using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

[Serializable]
public class LogTrackFrame
{
    public int frameIndex;
    public List<ushort> items = new List<ushort>();
    public List<int> args = new List<int>();

    internal ILogTrackList<ushort> items_internal = new LogTrackList<ushort>(FSPDebuger.ListLogTrackCapacityStep, 5);
    internal ILogTrackList<int> args_internal = new LogTrackList<int>(FSPDebuger.ListLogTrackCapacityStep, 5);
}

public sealed class LogTrackLoopQueue : IDisposable
{
    private bool m_disposed;
    private readonly int m_size;
    private readonly Queue<LogTrackFrame> m_queue;

    public LogTrackLoopQueue(int size)
    {
        m_size = size;
        m_queue = new Queue<LogTrackFrame>(size);
    }

    public void Dispose()
    {
        if (m_disposed) return;
        foreach (var frame in m_queue)
        {
            frame.args_internal.Dispose();
            frame.items_internal.Dispose();
        }
        m_disposed = true;
    }

    public LogTrackFrame GetNext()
    {
        if (m_size > 0 && m_queue.Count >= m_size)
        {
            var frame = m_queue.Dequeue();
            m_queue.Enqueue(frame);
            return frame;
        }

        var created = new LogTrackFrame();
        m_queue.Enqueue(created);
        return created;
    }

    public List<LogTrackFrame> ToList() => new List<LogTrackFrame>(m_queue);

    public int GetMemorySize()
    {
        int memSize = 0;
        foreach (var frame in m_queue)
        {
            memSize += frame.items_internal.GetMemorySize();
            memSize += frame.args_internal.GetMemorySize();
        }
        return memSize;
    }
}

[Serializable]
public class LogTrackFile
{
    public int errorFrameIndex;
    public string saveDateTime = string.Empty;
    public List<LogTrackFrame> frames = new List<LogTrackFrame>();

    public static LogTrackFile Open(string path)
    {
        if (!File.Exists(path))
        {
            Debuger.LogError("文件不存在:{0}", path);
            return null;
        }

        try
        {
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return LogTrackJson.DeserializeLogTrackFile(json);
            }

            var bytes = File.ReadAllBytes(path);
            using var source = new MemoryStream(bytes);
            using var zip = new GZipStream(source, CompressionMode.Decompress);
            using var target = new MemoryStream();
            zip.CopyTo(target);
            var jsonBytes = target.ToArray();
            return LogTrackJson.DeserializeLogTrackFile(Encoding.UTF8.GetString(jsonBytes));
        }
        catch (Exception e)
        {
            Debuger.LogError("文件打开失败:{0},{1}", path, e.Message);
            return null;
        }
    }

    public void Save(string path)
    {
        var json = LogTrackJson.Serialize(this);
        var bytes = Encoding.UTF8.GetBytes(json);

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(path, json, Encoding.UTF8);
            return;
        }

        using var target = new MemoryStream();
        using (var zip = new GZipStream(target, CompressionMode.Compress, true))
        using (var source = new MemoryStream(bytes))
        {
            source.CopyTo(zip);
        }
        File.WriteAllBytes(path, target.ToArray());
    }

    public void SaveAsText(string path, LogTrackPdbFile pdb)
    {
        var total = new StringBuilder();
        foreach (var frame in frames)
        {
            total.AppendLine("======================================================");
            total.AppendFormat("#{0} [H] [EnterFrame]\n", frame.frameIndex);
            total.AppendLine("------------------------------------------------------");

            int argIndex = 0;
            for (int j = 0; j < frame.items.Count; j++)
            {
                ushort item = frame.items[j];
                int hash = item >> 3;
                int argCount = item & 7;
                var pdbItem = pdb.GetItem(hash);
                if (pdbItem == null)
                {
                    total.AppendFormat("#{0} [H] <unknown hash {1}>({2})\n", frame.frameIndex, hash, argCount);
                    argIndex += argCount;
                    continue;
                }

                var methodArgs = new List<string>();
                for (int k = 0; k < argCount; k++)
                {
                    methodArgs.Add(frame.args[argIndex++].ToString());
                }

                total.AppendFormat("#{0} [H] {1},line:{2},{3}({4})\n",
                    frame.frameIndex,
                    pdbItem.file,
                    pdbItem.line,
                    pdbItem.dbgStr,
                    string.Join(",", methodArgs));
            }

            total.AppendLine("======================================================");
        }

        File.WriteAllText(path, total.ToString(), Encoding.UTF8);
    }

    internal void Flush()
    {
        foreach (var frame in frames)
        {
            frame.items = frame.items_internal.ToList();
            frame.args = frame.args_internal.ToList();
        }
    }
}

[Serializable]
public class LogTrackPdbItem
{
    public int hash;
    public int argCount;
    public string file = string.Empty;
    public int line;
    public string dbgStr = string.Empty;
}

[Serializable]
public class LogTrackPdbFile
{
    public List<LogTrackPdbItem> items = new List<LogTrackPdbItem>();
    private readonly SortedDictionary<int, LogTrackPdbItem> m_mapHash2Item = new SortedDictionary<int, LogTrackPdbItem>();
    private int m_lastValidHash;

    public static LogTrackPdbFile Open(string path)
    {
        if (!File.Exists(path))
        {
            Debuger.LogError("文件不存在：{0}", path);
            return null;
        }

        try
        {
            LogTrackPdbFile file;
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                file = LogTrackJson.DeserializeLogTrackPdbFile(File.ReadAllText(path, Encoding.UTF8));
            }
            else
            {
                file = LogTrackJson.DeserializeLogTrackPdbFile(File.ReadAllText(path, Encoding.UTF8));
            }

            if (file != null)
            {
                foreach (var item in file.items)
                {
                    file.m_mapHash2Item[item.hash] = item;
                }
            }
            return file;
        }
        catch (Exception e)
        {
            Debuger.LogError("文件打开失败：{0},{1}", path, e.Message);
            return null;
        }
    }

    public void Save(string path)
    {
        Flush();
        File.WriteAllText(path, LogTrackJson.Serialize(this), Encoding.UTF8);
    }

    public void SaveAsCSV(string path)
    {
        Flush();
        var sb = new StringBuilder();
        sb.Append(items.Count);
        foreach (var item in items)
        {
            sb.AppendFormat("\n{0,5},{1,5},{2,5}  {3},{4}",
                item.hash, item.argCount, item.line, item.file, item.dbgStr);
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    internal void Flush()
    {
        items.Clear();
        foreach (var item in m_mapHash2Item.Values)
        {
            items.Add(item);
        }
        items.Sort((a, b) => a.hash.CompareTo(b.hash));
    }

    public int AddItem(int hash, int argCnt, string file, int line, string dbgStr)
    {
        if (hash == 0 || m_mapHash2Item.ContainsKey(hash))
        {
            hash = GetNextValidHash();
        }

        var item = new LogTrackPdbItem
        {
            hash = hash,
            file = file,
            line = line,
            argCount = argCnt,
            dbgStr = dbgStr
        };
        m_mapHash2Item.Add(hash, item);
        return hash;
    }

    private int GetNextValidHash()
    {
        var hash = m_lastValidHash + 1;
        while (m_mapHash2Item.ContainsKey(hash))
        {
            hash++;
        }
        m_lastValidHash = hash;
        return hash;
    }

    public LogTrackPdbItem GetItem(int hash)
    {
        m_mapHash2Item.TryGetValue(hash, out var item);
        return item;
    }
}
