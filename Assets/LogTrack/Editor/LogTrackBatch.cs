#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using LogTrack.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity BatchMode entry points for Vulcan logtrack_demo_run.
/// </summary>
public static class LogTrackBatch
{
    private const string DefaultPdbRelative = "Assets/LogTrackGenerated";
    public const string RingBufferPrefKey = "LogTrack.RingBufferSize";

    public static void InsertLogTrack()
    {
        RunInsertLogTrackWithCompile(result =>
        {
            EditorApplication.Exit(result ? 0 : 1);
        });
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

    /// <summary>
    /// BatchMode 插桩速度基准：编译完成后对 IncludeAssemblies 做 IL 插桩并输出 insert_ms。
    /// Unity -batchmode -executeMethod LogTrackBatch.BenchmarkInsertSpeed -projectPath ...
    /// </summary>
    public static void BenchmarkInsertSpeed()
    {
        RunInsertLogTrackWithCompile(success =>
        {
            if (!success)
            {
                EditorApplication.Exit(1);
                return;
            }

            var benchDir = Path.GetFullPath(DefaultPdbRelative);
            Directory.CreateDirectory(benchDir);
            var benchPath = Path.Combine(benchDir, "logtrack_benchmark.json");
            LogTrackBenchmark.Save(benchPath);

            var line = "[LogTrackBenchmark] insert_ms="
                + LogTrackBenchmark.InsertMs.ToString("F2")
                + " benchmark="
                + benchPath;
            Console.WriteLine(line);
            Debug.Log(line);
            EditorApplication.Exit(0);
        });
    }

    private static void RunInsertLogTrackWithCompile(Action<bool> onComplete)
    {
        LogTrackCompileWait.RequestCompileAndWait(
            () => onComplete?.Invoke(RunInsertLogTrack()),
            error =>
            {
                Debug.LogError("[LogTrackBatch] " + error);
                onComplete?.Invoke(false);
            });
    }

    private static bool RunInsertLogTrack()
    {
        try
        {
            if (!LogTrackInsertSettings.TryLoad(out var setting))
            {
                Debug.LogError("[LogTrackBatch] Missing LogTrackSetting.txt");
                return false;
            }

            var assemblies = ResolveTargetAssemblies(setting);
            if (assemblies.Length == 0)
            {
                Debug.LogError("[LogTrackBatch] No target assemblies resolved from LogTrackSetting.txt");
                return false;
            }

            var pdbFull = Path.GetFullPath(DefaultPdbRelative);
            Directory.CreateDirectory(pdbFull);

            var result = LogTrackIlInstrumenter.PatchAssemblies(
                assemblies,
                pdbFull,
                setting.logTrackClass);

            if (!result.success)
            {
                Debug.LogError("[LogTrackBatch] IL insert failed: " + (result.error ?? "unknown"));
                return false;
            }

            Debug.Log(
                "[LogTrackBatch] IL insert complete. assemblies="
                + string.Join(",", result.patchedAssemblies)
                + " methods="
                + result.patchedMethods
                + " pdb="
                + pdbFull);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("[LogTrackBatch] Insert failed: " + ex);
            return false;
        }
    }

    private static string[] ResolveTargetAssemblies(LogTrackSetting setting)
    {
        var overrideAssemblies = ResolveAssembliesFromArgs();
        if (overrideAssemblies.Length > 0)
        {
            return overrideAssemblies;
        }

        return LogTrackIlAssemblyCatalog.ResolveTargetAssemblies(setting);
    }

    private static string[] ResolveAssembliesFromArgs()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-logtrackAssemblies" && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1]
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(name => name.Trim())
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToArray();
            }
        }

        return Array.Empty<string>();
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
