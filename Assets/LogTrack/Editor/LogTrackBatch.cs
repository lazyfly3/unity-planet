#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity BatchMode entry points for Vulcan logtrack_demo_run.
/// </summary>
public static class LogTrackBatch
{
    private const string DefaultScriptsRelative = "Assets/Scripts";
    private const string DefaultPdbRelative = "Assets/LogTrackGenerated";
    private const string SettingFileName = "LogTrackSetting.txt";
    public const string RingBufferPrefKey = "LogTrack.RingBufferSize";

    public static void InsertLogTrack()
    {
        var ok = RunInsertLogTrack();
        EditorApplication.Exit(ok ? 0 : 1);
    }

    public static void FindLatestTrackLog()
    {
        var latest = FindLatestLogPath();
        if (string.IsNullOrEmpty(latest))
        {
            Debug.LogError("[LogTrackBatch] No track log found under " + Application.persistentDataPath);
            EditorApplication.Exit(1);
            return;
        }

        var line = "[LogTrackBatch] LATEST_LOG=" + latest;
        Console.WriteLine(line);
        Debug.Log(line);
        EditorApplication.Exit(0);
    }

    public static void SetRingBufferSize()
    {
        var size = ResolveRingBufferSizeFromArgs(100);
        EditorPrefs.SetInt(RingBufferPrefKey, size);
        Debug.Log("[LogTrackBatch] RingBufferSize=" + size);
        EditorApplication.Exit(0);
    }

    private static bool RunInsertLogTrack()
    {
        try
        {
            var scriptsRelative = ResolveScriptsRelativeDir();
            var scriptsFull = Path.GetFullPath(scriptsRelative);
            if (!scriptsFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                scriptsFull += Path.DirectorySeparatorChar;
            }

            if (!Directory.Exists(scriptsFull.TrimEnd(Path.DirectorySeparatorChar)))
            {
                Debug.LogError("[LogTrackBatch] Scripts directory not found: " + scriptsFull);
                return false;
            }

            var settingPath = Path.Combine(scriptsFull, SettingFileName);
            if (!File.Exists(settingPath))
            {
                Debug.LogError("[LogTrackBatch] Missing LogTrackSetting.txt: " + settingPath);
                return false;
            }

            var setting = new LogTrackSetting();
            setting.Load(scriptsFull, SettingFileName);

            var pdbRelative = DefaultPdbRelative;
            var pdbFull = Path.GetFullPath(pdbRelative);
            if (!pdbFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                pdbFull += Path.DirectorySeparatorChar;
            }
            Directory.CreateDirectory(pdbFull.TrimEnd(Path.DirectorySeparatorChar));

            FSPDebugerTool.InsertLogTrack(
                scriptsFull,
                pdbFull,
                setting.logTrackClass,
                setting.logTrackMacro,
                setting.excludeFiles);

            AssetDatabase.Refresh();
            Debug.Log("[LogTrackBatch] Insert complete. Pdb: " + pdbFull);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[LogTrackBatch] Insert failed: " + ex);
            return false;
        }
    }

    private static string ResolveScriptsRelativeDir()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-logtrackScriptsDir" && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1].Replace('\\', '/');
            }
        }

        return DefaultScriptsRelative;
    }

    private static int ResolveRingBufferSizeFromArgs(int fallback)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-logtrackRingBuffer" && int.TryParse(args[i + 1], out var n) && n > 0)
            {
                return n;
            }
        }

        return fallback;
    }

    private static string FindLatestLogPath()
    {
        var dir = Path.Combine(Application.persistentDataPath, "LogTrack");
        if (!Directory.Exists(dir)) return null;

        string latest = null;
        DateTime latestTime = DateTime.MinValue;
        foreach (var pattern in new[] { "*.log", "*.bin", "*.txt" })
        {
            foreach (var file in Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly))
            {
                var time = File.GetLastWriteTimeUtc(file);
                if (time > latestTime)
                {
                    latestTime = time;
                    latest = file;
                }
            }
        }

        return latest;
    }
}
#endif
