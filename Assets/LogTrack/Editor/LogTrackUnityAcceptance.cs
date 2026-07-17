#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LogTrack.Editor
{
    /// <summary>
    /// Unity BatchMode 验收：IL 插桩 → Play N 帧 → 自动导出 → 校验 log 格式。
    /// 命令: Unity -batchmode -executeMethod LogTrack.Editor.LogTrackUnityAcceptance.Run -projectPath ...
    /// </summary>
    public static class LogTrackUnityAcceptance
    {
        private const string SessionKey = "LogTrackUnityAcceptance.Pending";
        private const string RunnerName = "LogTrackAcceptance";
        private const int TargetFrames = 30;

        [InitializeOnLoadMethod]
        private static void RegisterPlayModeHook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void Run()
        {
            try
            {
                if (!SessionState.GetBool(SessionKey, false))
                {
                    PrepareAndEnterPlay();
                    return;
                }
            }
            catch (Exception ex)
            {
                Fail("exception before play: " + ex.Message);
            }
        }

        private static void PrepareAndEnterPlay()
        {
            LogTrackSettings.AutoStartOnPlay = true;
            LogTrackSettings.ExportOnStop = true;
            LogTrackSettings.DefaultRingBufferSize = 100;
            LogTrackSettings.PdbRelativePath = LogTrackSettings.DefaultPdbRelativePath;
            LogTrackProjectSettings.ClearAutoInstrumentOverride();

            LogTrackCompileWait.RequestCompileAndWait(
                () =>
                {
                    if (!RunInsert())
                    {
                        Fail("insert_logtrack failed");
                        return;
                    }

                    EnterPlayAfterInsert();
                },
                error => Fail(error));
        }

        private static void EnterPlayAfterInsert()
        {
            var scenePath = "Assets/Scenes/SampleScene.unity";
            if (!File.Exists(scenePath))
            {
                Fail("scene missing: " + scenePath);
                return;
            }

            EditorSceneManager.OpenScene(scenePath);
            EnsureRunnerInScene();
            EditorSceneManager.SaveOpenScenes();

            SessionState.SetBool(SessionKey, true);
            Debug.Log("[LogTrackUnityAcceptance] Entering Play Mode...");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.EraseBool(SessionKey);
            try
            {
                ValidateExport();
            }
            catch (Exception ex)
            {
                Fail("validate failed: " + ex.Message);
            }
        }

        private static bool RunInsert()
        {
            if (!LogTrackInsertSettings.TryLoad(out var setting))
            {
                Debug.LogError("[LogTrackUnityAcceptance] Missing LogTrackSetting.txt");
                return false;
            }

            var assemblies = LogTrackIlAssemblyCatalog.ResolveTargetAssemblies(setting);
            if (assemblies.Length == 0)
            {
                assemblies = new[] { "Assembly-CSharp" };
            }

            var pdbFull = Path.GetFullPath("Assets/LogTrackGenerated");
            Directory.CreateDirectory(pdbFull);

            var result = LogTrackIlInstrumenter.PatchAssemblies(
                assemblies,
                pdbFull,
                setting.logTrackClass);

            if (!result.success)
            {
                Debug.LogError("[LogTrackUnityAcceptance] IL insert failed: " + (result.error ?? "unknown"));
                return false;
            }

            Debug.Log(
                "[LogTrackUnityAcceptance] IL insert complete. methods="
                + result.patchedMethods
                + " assemblies="
                + string.Join(",", result.patchedAssemblies));
            return true;
        }

        private static void EnsureRunnerInScene()
        {
            var runnerGo = GameObject.Find(RunnerName);
            if (runnerGo == null)
            {
                runnerGo = new GameObject(RunnerName);
            }

            var runnerType = Type.GetType("LogTrackDemoRunner, Assembly-CSharp");
            if (runnerType == null)
            {
                Fail("LogTrackDemoRunner type not found in Assembly-CSharp");
                return;
            }

            if (runnerGo.GetComponent(runnerType) == null)
            {
                runnerGo.AddComponent(runnerType);
            }
        }

        private static void ValidateExport()
        {
            var logPath = FindLatestLogPath();
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
            {
                Fail("exported .log not found under " + Path.Combine(Application.persistentDataPath, "LogTrack"));
                return;
            }

            var text = File.ReadAllText(logPath);
            var errors = new System.Collections.Generic.List<string>();
            if (text.Contains("line:"))
            {
                errors.Add("log contains forbidden line:");
            }

            foreach (var phase in new[] { "FixedUpdate", "Update", "LateUpdate" })
            {
                if (!text.Contains("-- [Phase: " + phase + "] --"))
                {
                    errors.Add("missing phase marker: " + phase);
                }
            }

            if (!text.Contains("::"))
            {
                errors.Add("missing Class::Method");
            }

            if (text.Contains("Unknown::hash_"))
            {
                errors.Add("pdb missing className/funcName (Unknown::hash_)");
            }

            var frameMatches = Regex.Matches(text, @"#(\d+) \[H\] \[EnterFrame\]");
            if (frameMatches.Count < 1)
            {
                errors.Add("no EnterFrame headers");
            }

            if (errors.Count > 0)
            {
                Fail(string.Join("; ", errors) + " | log=" + logPath);
                return;
            }

            var line = "[LogTrackUnityAcceptance] PASS log=" + logPath;
            Console.WriteLine(line);
            Debug.Log(line);
            EditorApplication.Exit(0);
        }

        private static string FindLatestLogPath()
        {
            var dir = Path.Combine(Application.persistentDataPath, "LogTrack");
            if (!Directory.Exists(dir)) return null;

            return Directory.GetFiles(dir, "*_LogTrack_Normal.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static void Fail(string message)
        {
            var line = "[LogTrackUnityAcceptance] FAIL " + message;
            Console.WriteLine(line);
            Debug.LogError(line);
            EditorApplication.Exit(1);
        }
    }
}
#endif
