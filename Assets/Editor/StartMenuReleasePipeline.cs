using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.Release
{
    /// <summary>
    /// Single release boundary for the player flow that starts at StartMenu.
    /// This file is Editor-only and is never compiled into the player.
    /// </summary>
    public static class StartMenuReleasePipeline
    {
        public static readonly string[] ScenePaths =
        {
            "Assets/Scenes/StartMenu.unity",
            "Assets/Scenes/InterstellarFlight.unity",
            "Assets/Scenes/PlanetApproach.unity",
            "Assets/Scenes/star.unity",
            "Assets/Scenes/GalaxyMap.unity",
            "Assets/Scenes/ModularAssemblyLab.unity",
            "Assets/Scenes/SpaceStationUpgradeTest.unity"
        };

        const string AuditOutputEnvironment = "UNITYPLANET_AUDIT_OUTPUT";
        const string BuildOutputEnvironment = "UNITYPLANET_BUILD_OUTPUT";
        const string BuildReportEnvironment = "UNITYPLANET_BUILD_REPORT";
        const string BatchBuildPendingSessionKey =
            "UnityPlanet.StartMenuRelease.BatchBuildPending";
        static int batchBuildDelayFrames;

        [InitializeOnLoadMethod]
        static void ResumePendingBatchBuildAfterReload()
        {
            if (!SessionState.GetBool(BatchBuildPendingSessionKey, false))
                return;

            batchBuildDelayFrames = 3;
            EditorApplication.delayCall += ContinueBatchBuildWhenReady;
        }

        [Serializable]
        sealed class AuditData
        {
            public string generatedAtUtc;
            public string unityVersion;
            public bool passed;
            public bool buildSettingsExact;
            public int dependencyCount;
            public int prefabCount;
            public int missingScriptCount;
            public string[] scenePaths;
            public string[] missingSceneAssets;
            public string[] missingScriptObjects;
            public string[] errors;
        }

        [Serializable]
        sealed class BuildData
        {
            public string generatedAtUtc;
            public string unityVersion;
            public string outputPath;
            public string result;
            public ulong totalSizeBytes;
            public double totalSeconds;
            public int totalErrors;
            public int totalWarnings;
            public string[] scenePaths;
        }

        [MenuItem("Tools/Release/Validate StartMenu Player Flow")]
        public static void ValidateFromMenu()
        {
            AuditData audit = RunAudit();
            string output = ResolveOutputPath(
                AuditOutputEnvironment,
                "StartMenuReleaseAudit.json");
            WriteJson(output, audit);
            if (!audit.passed)
                throw new BuildFailedException(
                    "StartMenu release audit failed. See " + output);
            UnityEngine.Debug.Log(
                "StartMenu release audit passed: " + output);
        }

        public static void AuditBatch()
        {
            int exitCode = 0;
            try
            {
                ValidateFromMenu();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        [MenuItem("Tools/Release/Build StartMenu Windows Player")]
        public static void BuildWindowsFromMenu()
        {
            AuditData audit = RunAudit();
            string auditOutput = ResolveOutputPath(
                AuditOutputEnvironment,
                "StartMenuReleaseAudit.json");
            WriteJson(auditOutput, audit);
            if (!audit.passed)
                throw new BuildFailedException(
                    "StartMenu release audit failed. See " + auditOutput);

            BuildWindowsPlayer();
        }

        static void BuildWindowsPlayer()
        {
            string output = Environment.GetEnvironmentVariable(
                BuildOutputEnvironment);
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "..",
                    "Builds",
                    "StartMenuWindows",
                    "UnityPlanet.exe"));
            }
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var stopwatch = Stopwatch.StartNew();
            BuildReport report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = ScenePaths,
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    // Lossless player-data compression: reduces transfer size
                    // without changing imported textures, meshes or gameplay data.
                    options = BuildOptions.CompressWithLz4HC
                });
            stopwatch.Stop();

            BuildSummary summary = report.summary;
            var buildData = new BuildData
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                outputPath = output,
                result = summary.result.ToString(),
                totalSizeBytes = summary.totalSize,
                totalSeconds = stopwatch.Elapsed.TotalSeconds,
                totalErrors = summary.totalErrors,
                totalWarnings = summary.totalWarnings,
                scenePaths = ScenePaths
            };
            string reportOutput = ResolveOutputPath(
                BuildReportEnvironment,
                "StartMenuBuildReport.json");
            WriteJson(reportOutput, buildData);

            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    "StartMenu Windows build failed. See " + reportOutput);
            }
            UnityEngine.Debug.Log(
                "StartMenu Windows build succeeded: " + output);
        }

        public static void BuildWindowsBatch()
        {
            SessionState.SetBool(BatchBuildPendingSessionKey, false);
            try
            {
                PrepareBatchAudit();
                AuditData audit = RunAudit();
                Lightmapping.Cancel();
                string auditOutput = ResolveOutputPath(
                    AuditOutputEnvironment,
                    "StartMenuReleaseAudit.json");
                WriteJson(auditOutput, audit);
                if (!audit.passed)
                    throw new BuildFailedException(
                        "StartMenu release audit failed. See " + auditOutput);
            }
            catch (Exception exception)
            {
                SessionState.SetBool(BatchBuildPendingSessionKey, false);
                UnityEngine.Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }

            // Opening and restoring the audited scenes can leave a domain reload
            // notification pending until the next editor updates. Building in the
            // same callback is rejected by Unity even though compilation is clean.
            SessionState.SetBool(BatchBuildPendingSessionKey, true);
            batchBuildDelayFrames = 3;
            EditorApplication.delayCall += ContinueBatchBuildWhenReady;
        }

        static void PrepareBatchAudit()
        {
            // Loading release scenes to inspect their object hierarchies must not
            // start an unrelated automatic GI bake in a headless build process.
            // This affects only the current batch-mode editor session; no scene
            // or LightingSettings asset is saved by the audit.
            Lightmapping.Cancel();
            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
        }

        static void ContinueBatchBuildWhenReady()
        {
            if (EditorApplication.isCompiling ||
                batchBuildDelayFrames-- > 0)
            {
                EditorApplication.delayCall += ContinueBatchBuildWhenReady;
                return;
            }

            SessionState.SetBool(BatchBuildPendingSessionKey, false);
            int exitCode = 0;
            try
            {
                BuildWindowsPlayer();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        static AuditData RunAudit()
        {
            var errors = new List<string>();
            var missingScenes = new List<string>();
            var missingScriptObjects = new List<string>();

            string[] enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            bool buildSettingsExact = enabledScenes.SequenceEqual(ScenePaths);
            if (!buildSettingsExact)
            {
                errors.Add(
                    "Enabled Build Settings scenes do not exactly match the " +
                    "StartMenu release boundary.");
            }

            foreach (string scenePath in ScenePaths)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                {
                    missingScenes.Add(scenePath);
                    errors.Add("Missing release scene: " + scenePath);
                }
            }

            string[] dependencies = AssetDatabase.GetDependencies(
                ScenePaths,
                true);
            string[] prefabPaths = dependencies
                .Where(path => path.EndsWith(
                    ".prefab",
                    StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string scenePath in ScenePaths)
                {
                    if (missingScenes.Contains(scenePath))
                        continue;
                    Scene scene = EditorSceneManager.OpenScene(
                        scenePath,
                        OpenSceneMode.Single);
                    if (Application.isBatchMode)
                        PrepareBatchAudit();
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        CollectMissingScripts(
                            root,
                            "scene " + scenePath,
                            missingScriptObjects);
                    }
                }
            }
            finally
            {
                if (Application.isBatchMode)
                {
                    // A batch build must not reopen an arbitrary cached editor
                    // workspace (for example a large legacy authoring scene).
                    Lightmapping.Cancel();
                }
                else if (originalSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                }
            }

            foreach (string prefabPath in prefabPaths)
            {
                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    CollectMissingScripts(
                        root,
                        "prefab " + prefabPath,
                        missingScriptObjects);
                }
                catch (Exception exception)
                {
                    errors.Add(
                        "Could not audit prefab " + prefabPath + ": " +
                        exception.Message);
                }
                finally
                {
                    if (root != null)
                        PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (missingScriptObjects.Count > 0)
            {
                errors.Add(
                    "Missing MonoBehaviour scripts found in release " +
                    "dependencies: " + missingScriptObjects.Count);
            }

            return new AuditData
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                passed = errors.Count == 0,
                buildSettingsExact = buildSettingsExact,
                dependencyCount = dependencies.Length,
                prefabCount = prefabPaths.Length,
                missingScriptCount = missingScriptObjects.Count,
                scenePaths = ScenePaths,
                missingSceneAssets = missingScenes.ToArray(),
                missingScriptObjects = missingScriptObjects.ToArray(),
                errors = errors.ToArray()
            };
        }

        static void CollectMissingScripts(
            GameObject root,
            string owner,
            List<string> results)
        {
            if (root == null)
                return;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility
                    .GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                for (int index = 0; index < count; index++)
                {
                    results.Add(
                        owner + " :: " + GetHierarchyPath(transform));
                }
            }
        }

        static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        static string ResolveOutputPath(
            string environmentVariable,
            string defaultFileName)
        {
            string value = Environment.GetEnvironmentVariable(
                environmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
                return Path.GetFullPath(value);
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Artifacts",
                defaultFileName));
        }

        static void WriteJson(string path, object data)
        {
            string fullPath = Path.GetFullPath(path);
            string parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(
                fullPath,
                JsonUtility.ToJson(data, true));
        }
    }
}
