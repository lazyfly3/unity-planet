#if UNITY_EDITOR
using UnityEditor;

namespace LogTrack.Editor
{
    /// <summary>
    /// Play 模式切换：Stop 后（PhaseDriver OnDestroy 导出完成）弹窗提示日志路径。
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
                LogTrackLastExport.Clear();
                LogTrackRecordingExport.Reset();
                EditorApplication.delayCall += LogTrackRuntimePanel.TryStartPendingRecording;
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                LogTrackExportNotifier.ShowPendingIfAny();
            }
        }
    }
}
#endif
