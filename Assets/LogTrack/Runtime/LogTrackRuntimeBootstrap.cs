using UnityEngine;

/// <summary>
/// Play 后自动创建 LogTrack PhaseDriver，无需手动挂组件。
/// </summary>
public static class LogTrackRuntimeBootstrap
{
    private const string DriverObjectName = "[LogTrack Phase Driver]";

    public static string PhaseDriverObjectName => DriverObjectName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePhaseDriver()
    {
        if (!LogTrackSettings.AutoStartOnPlay)
        {
            return;
        }

        TryStartRecording();
    }

    /// <summary>
    /// Play 中手动启动录制（创建 PhaseDriver）。AutoStart 关闭或未勾选时使用。
    /// </summary>
    public static bool TryStartRecording()
    {
        if (Object.FindObjectOfType<LogTrackSession>() != null)
        {
            return FSPDebuger.HasTrackSession;
        }

        var existing = Object.FindObjectOfType<LogTrackPhaseDriver>();
        if (existing != null)
        {
            if (FSPDebuger.HasTrackSession)
            {
                return true;
            }

            Object.Destroy(existing.gameObject);
        }

        LogTrackRecordingExport.Reset();

        var go = new GameObject(DriverObjectName);
        go.AddComponent<LogTrackPhaseDriver>();
        return FSPDebuger.HasTrackSession;
    }

    /// <summary>
    /// 结束一段录制后销毁 PhaseDriver，便于同一次 Play 内开始下一段。
    /// </summary>
    public static void DestroyPhaseDriver()
    {
        var existing = Object.FindObjectOfType<LogTrackPhaseDriver>();
        if (existing != null && existing.gameObject != null)
        {
            Object.Destroy(existing.gameObject);
        }
    }
}
