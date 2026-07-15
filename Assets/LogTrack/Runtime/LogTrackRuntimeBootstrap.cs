using UnityEngine;

/// <summary>
/// Play 后自动创建 LogTrack PhaseDriver，无需手动挂组件。
/// </summary>
public static class LogTrackRuntimeBootstrap
{
    private const string DriverObjectName = "[LogTrack Phase Driver]";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePhaseDriver()
    {
        if (!LogTrackSettings.AutoStartOnPlay)
        {
            return;
        }

        if (Object.FindObjectOfType<LogTrackSession>() != null)
        {
            return;
        }

        if (Object.FindObjectOfType<LogTrackPhaseDriver>() != null)
        {
            return;
        }

        var go = new GameObject(DriverObjectName);
        go.AddComponent<LogTrackPhaseDriver>();
    }
}
