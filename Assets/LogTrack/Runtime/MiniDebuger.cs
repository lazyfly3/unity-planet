using System;
using System.IO;
using UnityEngine;

/// <summary>
/// LogTrack 在 Unity 下的最小日志与路径适配。
/// </summary>
public static class Debuger
{
    public static void Log(string message) => UnityEngine.Debug.Log(message);
    public static void Log(string format, params object[] args) => UnityEngine.Debug.Log(string.Format(format, args));
    public static void LogError(string message) => UnityEngine.Debug.LogError(message);
    public static void LogError(string format, params object[] args) => UnityEngine.Debug.LogError(string.Format(format, args));
    public static void LogWarning(string message) => UnityEngine.Debug.LogWarning(message);
    public static void LogWarning(string format, params object[] args) => UnityEngine.Debug.LogWarning(string.Format(format, args));
    public static void LogVerbose() { }
    public static void Internal_Log(string message) => UnityEngine.Debug.Log(message);
    public static void Internal_LogWarning(string message) => UnityEngine.Debug.LogWarning(message);
    public static void Internal_LogError(string message) => UnityEngine.Debug.LogError(message);

    public static string CheckLogFileDir()
    {
        var dir = Path.Combine(Application.persistentDataPath, "LogTrack");
        Directory.CreateDirectory(dir);
        var sep = Path.DirectorySeparatorChar;
        return dir.EndsWith(sep.ToString()) ? dir : dir + sep;
    }

    public static string GenLogFileName() => DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
}
