#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LogTrack.Editor
{
    internal static class LogTrackExportNotifier
    {
        public static void ShowPendingIfAny()
        {
            if (!LogTrackLastExport.HasPending)
            {
                return;
            }

            if (LogTrackLastExport.Success)
            {
                var message = "文本日志：\n" + LogTrackLastExport.TextLogPath;
                if (!string.IsNullOrEmpty(LogTrackLastExport.BinLogPath))
                {
                    message += "\n\n二进制日志：\n" + LogTrackLastExport.BinLogPath;
                }

                EditorUtility.DisplayDialog("LogTrack 导出成功", message, "确定");
                Debug.Log("[LogTrack] 导出成功\n" + message);
            }
            else
            {
                var message = string.IsNullOrEmpty(LogTrackLastExport.ErrorMessage)
                    ? "未知原因。"
                    : LogTrackLastExport.ErrorMessage;
                EditorUtility.DisplayDialog("LogTrack 导出失败", message, "确定");
                Debug.LogWarning("[LogTrack] 导出失败\n" + message);
            }

            LogTrackLastExport.Clear();
        }
    }
}
#endif
