using System.IO;

/// <summary>
/// 结束录制时导出日志（PhaseDriver OnDestroy 与 Editor「结束录制」共用）。
/// </summary>
public static class LogTrackRecordingExport
{
    private static bool s_exported;

    public static void Reset()
    {
        s_exported = false;
    }

    public static bool ExportIfNeeded()
    {
        if (s_exported || !FSPDebuger.HasTrackSession)
        {
            return false;
        }

        s_exported = true;

        LogTrackBenchmark.RecordPeakMemory(FSPDebuger.GetRealUsedMemorySize());
        LogTrackStatistics.EndTrack();

        var binPath = FSPDebuger.SaveTrack();
        var textPath = FSPDebuger.SaveTrackAsText(LogTrackSettings.PdbRelativePath);

        var benchPath = Path.Combine(Debuger.CheckLogFileDir(), "logtrack_benchmark.json");
        LogTrackBenchmark.Save(benchPath);

        if (!string.IsNullOrEmpty(textPath))
        {
            Debuger.Log("LogTrack 文本日志已导出: " + textPath);
            LogTrackLastExport.RecordSuccess(textPath, binPath);
        }
        else if (!string.IsNullOrEmpty(binPath))
        {
            LogTrackLastExport.RecordFailure(
                "二进制日志已写入，但文本日志导出失败。\n\n二进制路径：\n" + binPath
                + "\n\n请先执行「IL 插桩」生成 Pdb。");
        }
        else
        {
            LogTrackLastExport.RecordFailure("录制缓冲区为空，未导出任何日志。");
        }

        FSPDebuger.CloseTrackSession();
        return LogTrackLastExport.HasPending && LogTrackLastExport.Success;
    }
}
