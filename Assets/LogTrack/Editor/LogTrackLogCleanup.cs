#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace LogTrack.Editor
{
    internal struct LogTrackLogCleanupResult
    {
        public string LogDir;
        public int DeletedCount;
        public long DeletedBytes;
        public int KeptCount;
    }

    /// <summary>
    /// 清理 persistentDataPath/LogTrack 下超过保留期的 .log / .bin 导出文件。
    /// </summary>
    internal static class LogTrackLogCleanup
    {
        internal const int DefaultRetentionDays = 1;

        internal static LogTrackLogCleanupResult CleanupExpiredLogs(int retentionDays = DefaultRetentionDays)
        {
            var result = new LogTrackLogCleanupResult
            {
                LogDir = Path.Combine(Application.persistentDataPath, "LogTrack"),
            };

            if (!Directory.Exists(result.LogDir))
            {
                return result;
            }

            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
            foreach (var pattern in new[] { "*.log", "*.bin" })
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(result.LogDir, pattern, SearchOption.TopDirectoryOnly);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[LogTrack] 扫描日志目录失败: " + ex.Message);
                    continue;
                }

                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc >= cutoff)
                        {
                            result.KeptCount++;
                            continue;
                        }

                        var size = info.Length;
                        File.Delete(file);
                        result.DeletedCount++;
                        result.DeletedBytes += size;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[LogTrack] 删除过期日志失败: " + file + " — " + ex.Message);
                    }
                }
            }

            return result;
        }

        internal static string FormatCompletionMessage(LogTrackLogCleanupResult result, int retentionDays)
        {
            var freedMb = result.DeletedBytes / (1024.0 * 1024.0);
            if (result.DeletedCount == 0)
            {
                return "未发现超过 "
                    + retentionDays
                    + " 天的过期日志。\n\n目录：\n"
                    + result.LogDir;
            }

            return "已清理 "
                + result.DeletedCount
                + " 个过期日志文件（超过 "
                + retentionDays
                + " 天），释放约 "
                + freedMb.ToString("F2")
                + " MB。\n\n仍保留 "
                + result.KeptCount
                + " 个未过期文件。\n\n目录：\n"
                + result.LogDir;
        }
    }
}
#endif
