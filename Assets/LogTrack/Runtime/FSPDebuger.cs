using System;
using System.Text;
using System.IO;

public interface ILogFrameTag
{
    string LOG_TAG { get; }
}

public static class FSPDebuger
{
    public static bool EnableLogTrack = true;
    public static bool EnableLogFrame = false;
    public static bool EnableLogFrameVerbose = false;
    public static int TrackBufferSize = 100;
    public static int ListLogTrackCapacityStep = 1024;
    public static bool EnableLogTrackInternal = false;

    private static int ms_currFrameIndex;
    private static LogTrackLoopQueue ms_currLogTrackQueue = new LogTrackLoopQueue(TrackBufferSize);
    private static LogTrackFrame ms_currLogTrackFrame;
    private static ILogTrackList<ushort> ms_currLogTrackItems = new LogTrackListEmpty<ushort>();
    private static ILogTrackList<int> ms_currLogTrackArgs = new LogTrackListEmpty<int>();
    private static ILogTrackList<byte> ms_currDepths = new LogTrackListEmpty<byte>();
    private static ILogTrackList<byte> ms_currPhases = new LogTrackListEmpty<byte>();
    private static LogTrackPdbFile ms_pdb;

    private static int ms_depthStack;
    private static LogTrackPhase ms_currentPhase = LogTrackPhase.Update;

    public static LogTrackPhase CurrentPhase => ms_currentPhase;
    public static int CurrentDepth => ms_depthStack > 0 ? ms_depthStack : 1;

    public static void IgnoreTrack() { }

    public static int GetRealUsedMemorySize() => ms_currLogTrackQueue.GetMemorySize();

    public static void SetPhase(LogTrackPhase phase)
    {
        ms_currentPhase = phase;
    }

    public static void ResetPhaseDepth()
    {
        ms_depthStack = 0;
    }

    public static void PushDepth()
    {
        ms_depthStack++;
    }

    public static void PopDepth()
    {
        if (ms_depthStack > 0)
        {
            ms_depthStack--;
        }
    }

    public static void BeginTrack(int ringSize = 0)
    {
        if (ringSize > 0)
        {
            TrackBufferSize = ringSize;
        }

        ms_currLogTrackQueue = new LogTrackLoopQueue(TrackBufferSize);
        ms_currLogTrackItems = new LogTrackListEmpty<ushort>();
        ms_currLogTrackArgs = new LogTrackListEmpty<int>();
        ms_currDepths = new LogTrackListEmpty<byte>();
        ms_currPhases = new LogTrackListEmpty<byte>();
        ms_depthStack = 0;
        ms_currentPhase = LogTrackPhase.Update;
        EnableLogTrackInternal = EnableLogTrack;
        LogTrackStatistics.BeginTrack();
    }

    public static void EndTrack()
    {
        EnableLogTrackInternal = false;
        LogTrackStatistics.EndTrack();
    }

    public static LogTrackFile CreateTrackFile(int errorFrameIndex = 0)
    {
        if (ms_currLogTrackQueue == null) return null;
        var file = new LogTrackFile
        {
            errorFrameIndex = errorFrameIndex,
            frames = ms_currLogTrackQueue.ToList(),
            saveDateTime = DateTime.Now.ToString("O")
        };
        file.Flush();
        return file;
    }

    public static string SaveTrack(int errorFrameIndex = 0)
    {
        var file = CreateTrackFile(errorFrameIndex);
        if (file == null)
        {
            Debuger.LogError("LogTrackFile无法创建！");
            return null;
        }

        var tag = errorFrameIndex > 0 ? "Error" : "Normal";
        var path = GenLogTrackFileFullPath(tag, "bin");
        Debuger.Log(path);
        file.Save(path);
        LogTrackBenchmark.RecordExportFile(path);
        return path;
    }

    public static string SaveTrackAsText(string logTrackPdbPath, int errorFrameIndex = 0)
    {
        var file = CreateTrackFile(errorFrameIndex);
        if (file == null)
        {
            Debuger.LogError("LogTrackFile无法创建！");
            return null;
        }

        if (ms_pdb == null)
        {
            ms_pdb = LogTrackPdbFile.Open(logTrackPdbPath);
        }

        if (ms_pdb == null)
        {
            Debuger.LogError("LogPdb无法打开！");
            return null;
        }

        var tag = errorFrameIndex > 0 ? "Error" : "Normal";
        var path = GenLogTrackFileFullPath(tag, "log");
        Debuger.Log(path);
        file.SaveAsText(path, ms_pdb);
        LogTrackBenchmark.RecordExportFile(path);
        return path;
    }

    public static void EnterTrackFrame(int frameIndex)
    {
        if (!EnableLogTrackInternal) return;

        ms_currLogTrackFrame = ms_currLogTrackQueue.GetNext();
        ms_currLogTrackFrame.frameIndex = frameIndex;
        ms_currFrameIndex = frameIndex;
        ms_currLogTrackItems = ms_currLogTrackFrame.items_internal;
        ms_currLogTrackItems.Clear();
        ms_currLogTrackArgs = ms_currLogTrackFrame.args_internal;
        ms_currLogTrackArgs.Clear();
        ms_currDepths = ms_currLogTrackFrame.depths_internal;
        ms_currDepths.Clear();
        ms_currPhases = ms_currLogTrackFrame.phases_internal;
        ms_currPhases.Clear();
        ms_depthStack = 0;
        LogTrackStatistics.EnterTrackFrame(frameIndex);
    }

    public static string GenLogTrackFileFullPath(string tag, string ext = "bin")
    {
        var filedir = Debuger.CheckLogFileDir();
        if (string.IsNullOrEmpty(filedir)) return null;
        var filename = Debuger.GenLogFileName().Replace(".log", "_LogTrack_" + tag + "." + ext);
        return filedir + filename;
    }

    private static void WriteLogTrack(int hash, int argCount, params int[] args)
    {
        LogTrackStatistics.LogTrack(argCount);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (argCount & 7)));
        ms_currDepths.Add((byte)Math.Max(1, CurrentDepth));
        ms_currPhases.Add((byte)ms_currentPhase);
        for (int i = 0; i < argCount; i++)
        {
            ms_currLogTrackArgs.Add(args[i]);
        }
    }

    public static void LogTrack(int hash) => WriteLogTrack(hash, 0);
    public static void LogTrack(int hash, int arg1) => WriteLogTrack(hash, 1, arg1);
    public static void LogTrack(int hash, int arg1, int arg2) => WriteLogTrack(hash, 2, arg1, arg2);
    public static void LogTrack(int hash, int arg1, int arg2, int arg3) => WriteLogTrack(hash, 3, arg1, arg2, arg3);
    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4) => WriteLogTrack(hash, 4, arg1, arg2, arg3, arg4);
    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4, int arg5) => WriteLogTrack(hash, 5, arg1, arg2, arg3, arg4, arg5);
    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6) => WriteLogTrack(hash, 6, arg1, arg2, arg3, arg4, arg5, arg6);
    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7) => WriteLogTrack(hash, 7, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
}

public static class LogTrackStatistics
{
    public static int TotalFrameCnt;
    public static long TotalLogCnt;
    public static long TotalArgCnt;
    public static long TotalMemAlloc;
    public static long TotalMemAllocCnt;
    public static uint MaxMemLogCnt;
    public static uint MaxMemArgCnt;
    public static long MaxMemAlloc;
    public static long MaxMemAllocCnt;
    public static int MaxMemFrameIndex;
    public static uint CurrentFrameLogCnt;
    public static uint CurrentFrameArgCnt;
    public static long CurrentFrameMemAlloc;
    public static long CurrentFrameMemAllocCnt;
    public static long PeakMemoryBytes;

    public static void BeginTrack()
    {
        TotalLogCnt = 0;
        TotalArgCnt = 0;
        TotalMemAlloc = 0;
        TotalMemAllocCnt = 0;
        TotalFrameCnt = 0;
        CurrentFrameLogCnt = 0;
        CurrentFrameArgCnt = 0;
        CurrentFrameMemAlloc = 0;
        CurrentFrameMemAllocCnt = 0;
        MaxMemLogCnt = 0;
        MaxMemArgCnt = 0;
        MaxMemAlloc = 0;
        MaxMemAllocCnt = 0;
        MaxMemFrameIndex = 0;
        PeakMemoryBytes = 0;
    }

    public static void EndTrack()
    {
        PeakMemoryBytes = Math.Max(PeakMemoryBytes, FSPDebuger.GetRealUsedMemorySize());
        LogTrackBenchmark.RecordPeakMemory(PeakMemoryBytes);
        Debuger.LogWarning("Statistics:\n{0}", ToDumpString());
    }

    public static void EnterTrackFrame(int frameIndex)
    {
        TotalFrameCnt++;
        CurrentFrameLogCnt = 0;
        CurrentFrameArgCnt = 0;
        CurrentFrameMemAlloc = 0;
        CurrentFrameMemAllocCnt = 0;
        PeakMemoryBytes = Math.Max(PeakMemoryBytes, FSPDebuger.GetRealUsedMemorySize());
    }

    public static void LogTrack(int argCount)
    {
        TotalLogCnt++;
        CurrentFrameLogCnt++;
        TotalArgCnt += argCount;
        CurrentFrameArgCnt += (uint)argCount;
    }

    public static string ToDumpString()
    {
        var sb = new StringBuilder();
        sb.AppendFormat("TotalFrameCnt:{0}\n", TotalFrameCnt);
        sb.AppendFormat("  TotalLogCnt:{0}\n", TotalLogCnt);
        sb.AppendFormat("  TotalArgCnt:{0}\n", TotalArgCnt);
        sb.AppendFormat("RealUsedMemory:{0}\n", FSPDebuger.GetRealUsedMemorySize());
        if (TotalFrameCnt > 0)
        {
            sb.AppendFormat("AvgLogCnt/Frame:{0}\n", TotalLogCnt / (uint)TotalFrameCnt);
            sb.AppendFormat("AvgArgCnt/Frame:{0}\n", TotalArgCnt / (uint)TotalFrameCnt);
        }
        return sb.ToString();
    }
}

/// <summary>Phase 3 benchmark collector (steps 19-21).</summary>
public static class LogTrackBenchmark
{
    private const string PrefInsertMs = "LogTrack.Benchmark.InsertMs";

    public static double InsertMs;
    public static long PeakMemoryBytes;
    public static long ExportFileBytes;
    public static double ParseMs;

    public static void RecordInsertMs(double ms)
    {
        InsertMs = ms;
#if CONSOLE_DEMO
        s_persistedInsertMs = ms;
#else
        UnityEngine.PlayerPrefs.SetString(PrefInsertMs, ms.ToString("F2"));
        UnityEngine.PlayerPrefs.Save();
#endif
    }
#if CONSOLE_DEMO
    private static double s_persistedInsertMs;
#endif

    public static void RecordPeakMemory(long bytes) => PeakMemoryBytes = Math.Max(PeakMemoryBytes, bytes);
    public static void RecordExportFile(string path)
    {
        if (File.Exists(path))
        {
            ExportFileBytes = Math.Max(ExportFileBytes, new FileInfo(path).Length);
        }
    }

    public static void RecordParseMs(double ms) => ParseMs = ms;

    public static string ToJson()
    {
        return "{" +
               "\"insert_ms\":" + InsertMs.ToString("F2") + "," +
               "\"peak_memory_bytes\":" + PeakMemoryBytes + "," +
               "\"export_file_bytes\":" + ExportFileBytes + "," +
               "\"parse_ms\":" + ParseMs.ToString("F2") +
               "}";
    }

    public static void Save(string path)
    {
        if (InsertMs <= 0)
        {
#if CONSOLE_DEMO
            InsertMs = s_persistedInsertMs;
#else
            if (UnityEngine.PlayerPrefs.HasKey(PrefInsertMs) &&
                double.TryParse(UnityEngine.PlayerPrefs.GetString(PrefInsertMs), out var stored))
            {
                InsertMs = stored;
            }
#endif
        }

        if (PeakMemoryBytes <= 0)
        {
            RecordPeakMemory(FSPDebuger.GetRealUsedMemorySize());
        }

        File.WriteAllText(path, ToJson());
    }
}
