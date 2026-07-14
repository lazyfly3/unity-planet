using UnityEngine;

/// <summary>
/// Play 后自动创建 LogTrack 运行时，无需手动挂 LogTrackSession。
/// </summary>
public static class LogTrackRuntimeBootstrap
{
    private const string AutoRunnerName = "[LogTrack Auto Runner]";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureAutoRunner()
    {
        if (!LogTrackSettings.AutoStartOnPlay)
        {
            return;
        }

        if (Object.FindObjectOfType<LogTrackSession>() != null)
        {
            return;
        }

        if (Object.FindObjectOfType<LogTrackAutoRunner>() != null)
        {
            return;
        }

        var go = new GameObject(AutoRunnerName);
        Object.DontDestroyOnLoad(go);
        go.AddComponent<LogTrackAutoRunner>();
    }
}
