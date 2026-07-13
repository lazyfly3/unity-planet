using System;
using System.Text;

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
    private static LogTrackPdbFile ms_pdb;

    public static void IgnoreTrack() { }

    public static int GetRealUsedMemorySize() => ms_currLogTrackQueue.GetMemorySize();

    public static void BeginTrack(int ringSize = 0)
    {
        if (ringSize > 0)
        {
            TrackBufferSize = ringSize;
        }

        ms_currLogTrackQueue = new LogTrackLoopQueue(TrackBufferSize);
        ms_currLogTrackItems = new LogTrackListEmpty<ushort>();
        ms_currLogTrackArgs = new LogTrackListEmpty<int>();
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
        LogTrackStatistics.EnterTrackFrame(frameIndex);
    }

    public static string GenLogTrackFileFullPath(string tag, string ext = "bin")
    {
        var filedir = Debuger.CheckLogFileDir();
        if (string.IsNullOrEmpty(filedir)) return null;
        var filename = Debuger.GenLogFileName().Replace(".log", "_LogTrack_" + tag + "." + ext);
        return filedir + filename;
    }

    public static void LogTrack(int hash)
    {
        LogTrackStatistics.LogTrack(0);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (0 & 7)));
    }

    public static void LogTrack(int hash, int arg1)
    {
        LogTrackStatistics.LogTrack(1);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (1 & 7)));
        ms_currLogTrackArgs.Add(arg1);
    }

    public static void LogTrack(int hash, int arg1, int arg2)
    {
        LogTrackStatistics.LogTrack(2);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (2 & 7)));
        ms_currLogTrackArgs.Add(arg1);
        ms_currLogTrackArgs.Add(arg2);
    }

    public static void LogTrack(int hash, int arg1, int arg2, int arg3)
    {
        LogTrackStatistics.LogTrack(3);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (3 & 7)));
        ms_currLogTrackArgs.Add(arg1);
        ms_currLogTrackArgs.Add(arg2);
        ms_currLogTrackArgs.Add(arg3);
    }

    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4)
    {
        LogTrackStatistics.LogTrack(4);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (4 & 7)));
        ms_currLogTrackArgs.Add(arg1);
        ms_currLogTrackArgs.Add(arg2);
        ms_currLogTrackArgs.Add(arg3);
        ms_currLogTrackArgs.Add(arg4);
    }

    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4, int arg5)
    {
        LogTrackStatistics.LogTrack(5);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (5 & 7)));
        ms_currLogTrackArgs.Add(arg1);
        ms_currLogTrackArgs.Add(arg2);
        ms_currLogTrackArgs.Add(arg3);
        ms_currLogTrackArgs.Add(arg4);
        ms_currLogTrackArgs.Add(arg5);
    }

    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6)
    {
        LogTrackStatistics.LogTrack(6);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (6 & 7)));
        ms_currLogTrackArgs.Add(arg1);
        ms_currLogTrackArgs.Add(arg2);
        ms_currLogTrackArgs.Add(arg3);
        ms_currLogTrackArgs.Add(arg4);
        ms_currLogTrackArgs.Add(arg5);
        ms_currLogTrackArgs.Add(arg6);
    }

    public static void LogTrack(int hash, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7)
    {
        LogTrackStatistics.LogTrack(7);
        ms_currLogTrackItems.Add((ushort)((hash << 3) | (7 & 7)));
        ms_currLogTrackArgs.Add(arg1);
        ms_currLogTrackArgs.Add(arg2);
        ms_currLogTrackArgs.Add(arg3);
        ms_currLogTrackArgs.Add(arg4);
        ms_currLogTrackArgs.Add(arg5);
        ms_currLogTrackArgs.Add(arg6);
        ms_currLogTrackArgs.Add(arg7);
    }
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
    }

    public static void EndTrack()
    {
        Debuger.LogWarning("Statistics:\n{0}", ToDumpString());
    }

    public static void EnterTrackFrame(int frameIndex)
    {
        TotalFrameCnt++;
        CurrentFrameLogCnt = 0;
        CurrentFrameArgCnt = 0;
        CurrentFrameMemAlloc = 0;
        CurrentFrameMemAllocCnt = 0;
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
