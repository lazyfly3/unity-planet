#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor Play 模式钩子：与 LogTrackPhaseDriver 互斥，不再驱动 EnterTrackFrame。
/// 仅在未启用 AutoStart 且缺少 PhaseDriver 时作导出兜底。
/// </summary>
[InitializeOnLoad]
public static class LogTrackPlayModeHook
{
    static LogTrackPlayModeHook()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            if (LogTrackSettings.AutoStartOnPlay)
            {
                Debug.Log("[LogTrackPlayModeHook] AutoStart enabled — PhaseDriver owns recording.");
            }
            return;
        }

        if (state != PlayModeStateChange.ExitingPlayMode) return;
        if (LogTrackSettings.AutoStartOnPlay) return;

        var pdbPath = Path.Combine(Application.dataPath, "LogTrackGenerated", "LogPdb.pdb.json");
        if (!File.Exists(pdbPath))
        {
            Debug.LogWarning("[LogTrackPlayModeHook] Skip export: missing " + pdbPath);
            return;
        }

        if (!FSPDebuger.EnableLogTrackInternal) return;

        var exported = FSPDebuger.SaveTrackAsText(pdbPath);
        if (!string.IsNullOrEmpty(exported))
        {
            Debug.Log("[LogTrackPlayModeHook] Exported track: " + exported);
        }
    }
}
#endif
