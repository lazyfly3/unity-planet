#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CombatMap;

namespace UnityPlanet.EditorTools
{
    public static class CombatMapRuntimeSceneBaker
    {
        private const string SourceScenePath = "Assets/Scenes/CombatMapLab.unity";
        private const string RuntimeScenePath = "Assets/Scenes/CombatMapRuntime.unity";
        private const string GeneratedAssetDirectory = "Assets/Generated/CombatMapRuntime";
        private const string DuelAssetDirectory =
            GeneratedAssetDirectory + "/Duel";
        private const string HordeAssetDirectory =
            GeneratedAssetDirectory + "/Horde";
        private static readonly int[] TacticalHighRiseBuildingIndices =
        {
            2, 6, 7, 8, 14, 15, 19, 23, 25
        };

        [InitializeOnLoadMethod]
        private static void ScheduleMissingRuntimeBake()
        {
            if (!ShouldAutoBakeRuntimeScene())
            {
                return;
            }

            if (!RuntimeSceneNeedsBake())
            {
                return;
            }

            Debug.Log("[CombatMap] 检测到运行时场景缺失，准备自动烘焙。");
            EditorApplication.delayCall += TryBakeMissingRuntimeScene;
        }

        [MenuItem("Tools/Combat Map/烘焙运行时空战地图")]
        public static void BakeRuntimeScene()
        {
            CleanupFailedRuntimeBakeScenes();
            if (!File.Exists(SourceScenePath))
            {
                throw new FileNotFoundException("找不到 CombatMapLab 场景", SourceScenePath);
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string previousScene = SceneManager.GetActiveScene().path;
            Scene source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            MonoBehaviour runtime = FindRuntimeController(source);
            if (runtime == null)
            {
                throw new InvalidOperationException("CombatMapLab 缺少 CombatMapRuntimeController");
            }

            EnsureDirectory(GeneratedAssetDirectory);
            PrepareGeneratedModeDirectories();
            Scene runtimeScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            ModeBakeData duel = BuildModeEnvironment(
                runtime as CombatMapRuntimeController,
                AirCombatMapMode.Duel,
                runtimeScene);
            ModeBakeData horde = BuildModeEnvironment(
                runtime as CombatMapRuntimeController,
                AirCombatMapMode.Horde,
                runtimeScene);

            GameObject providerObject = new GameObject("BakedCombatMapFlightEnvironment");
            SceneManager.MoveGameObjectToScene(providerObject, runtimeScene);
            BakedCombatMapFlightEnvironment provider =
                providerObject.AddComponent<BakedCombatMapFlightEnvironment>();
            ConfigureDualProvider(provider, duel, horde);

            EditorSceneManager.SaveScene(runtimeScene, RuntimeScenePath);
            AddToBuildSettings(RuntimeScenePath);
            AssetDatabase.Refresh();
            EditorSceneManager.CloseScene(runtimeScene, true);

            if (!string.IsNullOrWhiteSpace(previousScene) && File.Exists(previousScene))
            {
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            }

            Debug.Log("[CombatMap] 运行时场景已烘焙：" + RuntimeScenePath);
        }

        public static void BuildFromCommandLine()
        {
            BakeRuntimeScene();
        }

        private static void TryBakeMissingRuntimeScene()
        {
            if (!ShouldAutoBakeRuntimeScene())
            {
                return;
            }

            if (!RuntimeSceneNeedsBake())
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += TryBakeMissingRuntimeScene;
                return;
            }

            try
            {
                BakeRuntimeSceneSilently();
            }
            catch (Exception exception)
            {
                CleanupFailedRuntimeBakeScenes();
                Debug.LogError(
                    "[CombatMap] 自动烘焙运行时场景失败：" +
                    exception);
            }
        }

        private static void BakeRuntimeSceneSilently()
        {
            CleanupFailedRuntimeBakeScenes();
            Scene previousActive = SceneManager.GetActiveScene();
            Scene source = SceneManager.GetSceneByPath(SourceScenePath);
            bool sourceWasLoaded = source.IsValid() && source.isLoaded;
            if (!sourceWasLoaded)
            {
                source = EditorSceneManager.OpenScene(
                    SourceScenePath,
                    OpenSceneMode.Additive);
            }

            MonoBehaviour runtime = FindRuntimeController(source);
            if (runtime == null)
            {
                throw new InvalidOperationException(
                    "CombatMapLab 缺少 CombatMapRuntimeController");
            }

            EnsureDirectory(GeneratedAssetDirectory);
            PrepareGeneratedModeDirectories();
            Scene runtimeScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            ModeBakeData duel = BuildModeEnvironment(
                runtime as CombatMapRuntimeController,
                AirCombatMapMode.Duel,
                runtimeScene);
            ModeBakeData horde = BuildModeEnvironment(
                runtime as CombatMapRuntimeController,
                AirCombatMapMode.Horde,
                runtimeScene);

            GameObject providerObject =
                new GameObject("BakedCombatMapFlightEnvironment");
            SceneManager.MoveGameObjectToScene(
                providerObject,
                runtimeScene);
            BakedCombatMapFlightEnvironment provider =
                providerObject.AddComponent<
                    BakedCombatMapFlightEnvironment>();
            ConfigureDualProvider(provider, duel, horde);

            EditorSceneManager.SaveScene(
                runtimeScene,
                RuntimeScenePath);
            AddToBuildSettings(RuntimeScenePath);
            EditorSceneManager.CloseScene(runtimeScene, true);
            if (!sourceWasLoaded)
            {
                EditorSceneManager.CloseScene(source, true);
            }

            if (previousActive.IsValid() &&
                previousActive.isLoaded)
            {
                SceneManager.SetActiveScene(previousActive);
            }

            AssetDatabase.Refresh();
            Debug.Log(
                "[CombatMap] 已自动烘焙运行时环境：" +
                RuntimeScenePath);
        }

        private static bool RuntimeSceneNeedsBake()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    RuntimeScenePath) == null)
            {
                return true;
            }

            string absolutePath = Path.GetFullPath(
                RuntimeScenePath);
            return !File.Exists(absolutePath) ||
                   File.ReadAllText(absolutePath)
                       .IndexOf(
                           "CombatMapEnvironment",
                           StringComparison.Ordinal) < 0 ||
                   File.ReadAllText(absolutePath)
                       .IndexOf(
                           "bakeVersion: 4",
                           StringComparison.Ordinal) < 0;
        }

        private static bool ShouldAutoBakeRuntimeScene()
        {
            // CombatMapRuntime is a legacy/generated scene. Do not let its
            // editor bootstrap silently expand an unrelated player build or
            // regenerate archived content during command-line validation.
            // The explicit Tools/Combat Map bake command remains available.
            if (Application.isBatchMode || BuildPipeline.isBuildingPlayer)
            {
                return false;
            }

            for (int index = 0; index < EditorBuildSettings.scenes.Length; index++)
            {
                EditorBuildSettingsScene scene = EditorBuildSettings.scenes[index];
                if (scene.enabled && string.Equals(
                        scene.path,
                        RuntimeScenePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CleanupFailedRuntimeBakeScenes()
        {
            for (int sceneIndex = SceneManager.sceneCount - 1;
                 sceneIndex >= 0;
                 sceneIndex--)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid()
                    || !scene.isLoaded
                    || !string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }
                bool generated = false;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] != null
                        && roots[i].name.StartsWith(
                            "CombatMapEnvironment_",
                            StringComparison.Ordinal))
                    {
                        generated = true;
                        break;
                    }
                }
                if (generated)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ClearHideFlags(GameObject root)
        {
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject gameObject = transforms[i].gameObject;
                gameObject.hideFlags = HideFlags.None;
                Component[] components =
                    gameObject.GetComponents<Component>();
                for (int componentIndex = 0;
                     componentIndex < components.Length;
                     componentIndex++)
                {
                    if (components[componentIndex] != null)
                    {
                        components[componentIndex].hideFlags =
                            HideFlags.None;
                    }
                }
            }
        }

        private sealed class ModeBakeData
        {
            public GameObject environment;
            public CombatArenaContext context;
        }

        private static ModeBakeData BuildModeEnvironment(
            CombatMapRuntimeController controller,
            AirCombatMapMode mode,
            Scene runtimeScene)
        {
            if (controller == null)
            {
                throw new InvalidOperationException(
                    "CombatMapLab 控制器类型不匹配，无法生成模式地图");
            }

            AirCombatMapSettings authored = controller.Recipe != null
                ? controller.Recipe.CreateValidatedSettings()
                : controller.CurrentSettings;
            AirCombatMapSettings settings =
                CombatMapModeProfiles.Create(authored, mode);
            int seed = CombatMapModeProfiles.SeedForMode(
                authored.seed,
                mode);
            if (!controller.RebuildRuntime(settings, seed))
            {
                throw new InvalidOperationException(
                    "CombatMap " + mode + " 候选未通过验证：\n"
                    + FormatValidation(controller.CurrentValidation));
            }

            GameObject generatedRoot = ReadGeneratedRoot(controller);
            if (generatedRoot == null || controller.CurrentPlan == null)
            {
                throw new InvalidOperationException(
                    "CombatMap " + mode + " 没有生成环境根节点");
            }

            CombatArenaContext context = controller.CurrentContext;
            ValidateModeArena(mode, settings, context, generatedRoot);

            GameObject environment =
                UnityEngine.Object.Instantiate(generatedRoot);
            environment.name = mode == AirCombatMapMode.Horde
                ? "CombatMapEnvironment_Horde"
                : "CombatMapEnvironment_Duel";
            ClearHideFlags(environment);
            SceneManager.MoveGameObjectToScene(environment, runtimeScene);
            ReplaceTacticalPlaceholderVisuals(
                environment,
                mode,
                controller.CurrentPlan.seed);
            AddDistantDressing(
                environment,
                settings,
                controller.CurrentPlan,
                mode);
            RemoveEditorOnlyObjects(environment);
            context = FitBakedFlightContainment(
                environment,
                settings,
                context);
            PersistGeneratedAssets(environment, mode);
            return new ModeBakeData
            {
                environment = environment,
                context = context
            };
        }

        private static CombatArenaContext FitBakedFlightContainment(
            GameObject environment,
            AirCombatMapSettings settings,
            CombatArenaContext context)
        {
            float highest = Mathf.Max(
                context.center.y,
                Mathf.Max(
                    context.playerSpawnPosition.y,
                    context.enemySpawnPosition.y));
            Renderer[] renderers =
                environment.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy
                    || renderer.name.StartsWith("PlaceholderVisual_"))
                {
                    continue;
                }
                highest = Mathf.Max(highest, renderer.bounds.max.y);
            }
            Transform containment = environment.transform.Find(
                "FlightContainmentAirWalls");
            if (containment == null)
                return context;
            Transform ceilingTransform = containment.Find("CeilingAirWall");
            BoxCollider ceilingCollider = ceilingTransform != null
                ? ceilingTransform.GetComponent<BoxCollider>()
                : null;
            if (ceilingCollider == null)
                return context;

            float margin = Mathf.Max(
                18f,
                settings.vehicleWingspan * 1.25f,
                settings.designCombatSpeed * 0.32f);
            float ceiling = Mathf.Max(
                context.center.y + 20f,
                highest + margin);
            float thickness = ceilingCollider.size.y;
            Vector3 ceilingPosition = ceilingTransform.position;
            ceilingPosition.y = ceiling + thickness * 0.5f;
            ceilingTransform.position = ceilingPosition;

            BoxCollider[] walls =
                containment.GetComponentsInChildren<BoxCollider>(true);
            for (int index = 0; index < walls.Length; index++)
            {
                BoxCollider wall = walls[index];
                if (wall == null || wall == ceilingCollider)
                    continue;
                float bottom = wall.bounds.min.y;
                float height = ceiling - bottom + thickness;
                Vector3 size = wall.size;
                size.y = height;
                wall.size = size;
                Vector3 position = wall.transform.position;
                position.y = bottom + height * 0.5f;
                wall.transform.position = position;
            }
            context.maximumFlightAltitude = ceiling;
            return context;
        }

        private static void ConfigureDualProvider(
            BakedCombatMapFlightEnvironment provider,
            ModeBakeData duel,
            ModeBakeData horde)
        {
            provider.ConfigureBakedModes(
                duel.environment,
                duel.context.playerSpawnPosition,
                duel.context.playerSpawnRotation,
                duel.context.enemySpawnPosition,
                duel.context.enemySpawnRotation,
                duel.context.center,
                duel.context.warningRadius,
                duel.context.forfeitRadius,
                horde.environment,
                horde.context.playerSpawnPosition,
                horde.context.playerSpawnRotation,
                horde.context.enemySpawnPosition,
                horde.context.enemySpawnRotation,
                horde.context.center,
                horde.context.warningRadius,
                horde.context.forfeitRadius,
                duel.context.maximumFlightAltitude,
                horde.context.maximumFlightAltitude);
        }

        private static void ValidateModeArena(
            AirCombatMapMode mode,
            AirCombatMapSettings settings,
            CombatArenaContext context,
            GameObject generatedRoot)
        {
            if (!context.IsValid)
            {
                throw new InvalidOperationException(
                    "CombatMap " + mode + " 生成了无效战区上下文");
            }
            if (context.forfeitRadius
                > settings.MaximumSafeForfeitRadius + 0.01f)
            {
                throw new InvalidOperationException(
                    "CombatMap " + mode
                    + " AI 战术活动外圈超出主地形安全缓冲："
                    + context.forfeitRadius.ToString("0.0")
                    + "m > "
                    + settings.MaximumSafeForfeitRadius.ToString("0.0")
                    + "m");
            }
            float playerRadius = HorizontalDistance(
                context.playerSpawnPosition,
                context.center);
            float enemyRadius = HorizontalDistance(
                context.enemySpawnPosition,
                context.center);
            if (playerRadius >= context.warningRadius
                || enemyRadius >= context.warningRadius)
            {
                throw new InvalidOperationException(
                    "CombatMap " + mode + " 出生点位于警告边界之外");
            }

            MeshCollider[] surfaces =
                generatedRoot.GetComponentsInChildren<MeshCollider>(true);
            MeshCollider collisionSurface = null;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] != null
                    && surfaces[i].name == "CollisionSurface")
                {
                    collisionSurface = surfaces[i];
                    break;
                }
            }
            if (collisionSurface == null)
            {
                throw new InvalidOperationException(
                    "CombatMap " + mode + " 缺少 CollisionSurface");
            }
            Bounds bounds = collisionSurface.bounds;
            float available = Mathf.Min(
                Mathf.Min(
                    context.center.x - bounds.min.x,
                    bounds.max.x - context.center.x),
                Mathf.Min(
                    context.center.z - bounds.min.z,
                    bounds.max.z - context.center.z));
            if (context.forfeitRadius + settings.EdgeSafetyMargin
                > available + 1f)
            {
                throw new InvalidOperationException(
                    "CombatMap " + mode
                    + " 主碰撞网格没有覆盖战术活动外圈与安全缓冲");
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(
                new Vector2(a.x, a.z),
                new Vector2(b.x, b.z));
        }

        private static string FormatValidation(
            CombatMapValidationReport report)
        {
            if (report == null)
                return "没有验证报告";
            var lines = new List<string>
            {
                "Score=" + report.score.ToString("0.0"),
                "Topology=" + report.topologyScore.ToString("0.0"),
                "Kinematic=" + report.kinematicScore.ToString("0.0"),
                "Cover=" + report.coverRhythmScore.ToString("0.0")
            };
            if (report.violations != null)
            {
                for (int i = 0; i < report.violations.Length; i++)
                {
                    CombatMapViolation violation = report.violations[i];
                    if (violation != null)
                    {
                        lines.Add(
                            violation.severity + " "
                            + violation.code + ": "
                            + violation.message);
                    }
                }
            }
            return string.Join("\n", lines.ToArray());
        }

        private static void ReplaceTacticalPlaceholderVisuals(
            GameObject environment,
            AirCombatMapMode mode,
            int seed)
        {
            Transform[] transforms =
                environment.GetComponentsInChildren<Transform>(true);
            int visualIndex = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform placeholder = transforms[i];
                const string prefix = "PlaceholderVisual_";
                if (placeholder == null
                    || !placeholder.name.StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                CombatDecorationKind kind;
                if (!Enum.TryParse(
                        placeholder.name.Substring(prefix.Length),
                        out kind))
                {
                    continue;
                }
                Transform anchor = placeholder.parent;
                BoxCollider proxy = anchor != null
                    ? anchor.GetComponent<BoxCollider>()
                    : null;
                if (anchor == null || proxy == null)
                    continue;
                Vector3 targetSize = proxy.size * 0.92f;
                int stableIndex = visualIndex++;
                GameObject model;
                bool useAuthoredUrbanBuilding =
                    kind == CombatDecorationKind.Building
                    || kind == CombatDecorationKind.Beacon
                    || kind == CombatDecorationKind.RockSpire;
                if (useAuthoredUrbanBuilding)
                {
                    CombatDecorationKind visualKind =
                        kind == CombatDecorationKind.Beacon
                            ? CombatDecorationKind.Beacon
                            : CombatDecorationKind.Building;
                    model = CreateProjectLibraryUrbanBuilding(
                        anchor,
                        environment.scene,
                        proxy,
                        targetSize,
                        seed,
                        stableIndex,
                        visualKind);
                    if (model == null)
                    {
                        throw new InvalidOperationException(
                            "Project-library urban model fitting failed for "
                            + anchor.name
                            + " at proxy size "
                            + targetSize.ToString("F2")
                            + ". All authored high-rise candidates were rejected.");
                    }
                }
                else
                {
                    string assetPath = ResolveVisualAssetPath(
                        kind,
                        mode,
                        seed,
                        stableIndex);
                    GameObject asset =
                        AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (asset == null)
                        continue;
                    model = InstantiateVisualAsset(
                        asset,
                        anchor,
                        environment.scene);
                    if (model != null)
                    {
                        model.name = "Model_" + kind;
                        if (!FitVisualToSize(model, anchor, targetSize))
                        {
                            UnityEngine.Object.DestroyImmediate(model);
                            model = null;
                        }
                    }
                }
                if (model == null)
                    continue;
                if (useAuthoredUrbanBuilding
                    && !HasCompleteBuildingShell(model))
                {
                    UnityEngine.Object.DestroyImmediate(model);
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(placeholder.gameObject);
            }
        }

        private static GameObject CreateProjectLibraryUrbanBuilding(
            Transform anchor,
            Scene scene,
            BoxCollider proxy,
            Vector3 targetSize,
            int seed,
            int index,
            CombatDecorationKind kind)
        {
            // PCG owns only the placement envelope. Geometry always comes from
            // the project's authored FBX library and is uniformly scaled so
            // the artist-authored proportions remain intact.
            int first = PositiveHash(unchecked(
                seed + index * 104729))
                % TacticalHighRiseBuildingIndices.Length;
            GameObject bestModel = null;
            Bounds bestBounds = default;
            float bestFill = float.NegativeInfinity;
            for (int attempt = 0;
                 attempt < TacticalHighRiseBuildingIndices.Length;
                 attempt++)
            {
                int assetIndex = TacticalHighRiseBuildingIndices[
                    (first + attempt * 5)
                    % TacticalHighRiseBuildingIndices.Length];
                string path = "Assets/Pack/Models/Building/Building "
                    + assetIndex.ToString("00") + ".FBX";
                GameObject asset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                    continue;
                GameObject model = InstantiateVisualAsset(
                    asset,
                    anchor,
                    scene);
                if (model == null)
                    continue;
                if (!TryOrientAuthoredBuildingUpright(
                        model,
                        anchor,
                        out Bounds authoredBounds))
                {
                    UnityEngine.Object.DestroyImmediate(model);
                    continue;
                }
                model.name = "Model_" + kind;
                if (!FitVisualUniformlyToSize(
                        model,
                        anchor,
                        targetSize,
                        out Bounds fitted)
                    || !HasCompleteBuildingShell(model))
                {
                    UnityEngine.Object.DestroyImmediate(model);
                    continue;
                }
                float fillX = fitted.size.x
                    / Mathf.Max(0.01f, targetSize.x);
                float fillY = fitted.size.y
                    / Mathf.Max(0.01f, targetSize.y);
                float fillZ = fitted.size.z
                    / Mathf.Max(0.01f, targetSize.z);
                float fill = fillX * 0.2f
                    + fillY * 0.6f
                    + fillZ * 0.2f;
                fill += (PositiveHash(unchecked(
                        seed ^ index * 486187739 ^ assetIndex * 104729))
                    % 1000) / 1000f * 0.12f;
                if (fill <= bestFill)
                {
                    UnityEngine.Object.DestroyImmediate(model);
                    continue;
                }
                if (bestModel != null)
                    UnityEngine.Object.DestroyImmediate(bestModel);
                bestModel = model;
                bestBounds = fitted;
                bestFill = fill;
            }
            if (bestModel == null)
                return null;

            float proxyBottom = proxy.center.y - proxy.size.y * 0.5f;
            float modelBottom = bestBounds.center.y - bestBounds.extents.y;
            bestModel.transform.localPosition += Vector3.up
                * (proxyBottom - modelBottom);
            if (!TryGetRendererBoundsInSpace(
                    bestModel,
                    anchor,
                    out bestBounds))
            {
                UnityEngine.Object.DestroyImmediate(bestModel);
                return null;
            }
            proxy.center = bestBounds.center;
            proxy.size = bestBounds.size * 1.015f;
            SetLayerRecursively(bestModel.transform, anchor.gameObject.layer);
            return bestModel;
        }

        private static bool TryOrientAuthoredBuildingUpright(
            GameObject model,
            Transform anchor,
            out Bounds bounds)
        {
            if (!TryGetRendererBoundsInSpace(model, anchor, out bounds))
                return false;
            float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
            if (bounds.size.y > horizontal * 1.45f)
                return true;

            Quaternion correction;
            if (bounds.size.x > bounds.size.z
                && bounds.size.x > bounds.size.y * 1.45f)
            {
                correction = Quaternion.Euler(0f, 0f, 90f);
            }
            else if (bounds.size.z > bounds.size.x
                     && bounds.size.z > bounds.size.y * 1.45f)
            {
                correction = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                return false;
            }
            model.transform.localRotation = correction
                * model.transform.localRotation;
            if (!TryGetRendererBoundsInSpace(model, anchor, out bounds))
                return false;
            return bounds.size.y
                > Mathf.Max(bounds.size.x, bounds.size.z) * 1.45f;
        }

        private static bool FitVisualUniformlyToSize(
            GameObject model,
            Transform anchor,
            Vector3 targetSize,
            out Bounds fitted)
        {
            fitted = default;
            if (!TryGetRendererBoundsInSpace(model, anchor, out Bounds bounds))
                return false;
            Vector3 source = bounds.size;
            float uniformScale = Mathf.Min(
                targetSize.x / Mathf.Max(0.01f, source.x),
                Mathf.Min(
                    targetSize.y / Mathf.Max(0.01f, source.y),
                    targetSize.z / Mathf.Max(0.01f, source.z)));
            if (uniformScale <= 0f
                || float.IsNaN(uniformScale)
                || float.IsInfinity(uniformScale))
            {
                return false;
            }
            model.transform.localScale *= uniformScale;
            if (!TryGetRendererBoundsInSpace(model, anchor, out bounds))
                return false;
            model.transform.localPosition -= bounds.center;
            if (!TryGetRendererBoundsInSpace(model, anchor, out fitted))
                return false;
            return fitted.size.x <= targetSize.x + 0.05f
                && fitted.size.y <= targetSize.y + 0.05f
                && fitted.size.z <= targetSize.z + 0.05f;
        }

        private static string ResolveVisualAssetPath(
            CombatDecorationKind kind,
            AirCombatMapMode mode,
            int seed,
            int index)
        {
            int stable = PositiveHash(unchecked(seed + index * 104729));
            switch (kind)
            {
                case CombatDecorationKind.Building:
                case CombatDecorationKind.Beacon:
                    return "Assets/Pack/Models/Building/Building "
                        + (1 + stable % 25).ToString("00")
                        + ".FBX";
                case CombatDecorationKind.Crystal:
                    return "Assets/PlanetDecoration/LowPolyPlanetKit/Crystal_0"
                        + (1 + stable % 4)
                        + ".prefab";
                default:
                    if (mode == AirCombatMapMode.Horde && index % 3 == 0)
                    {
                        return "Assets/Pack/Models/Building/Building "
                            + (1 + stable % 25).ToString("00")
                            + ".FBX";
                    }
                    return "Assets/PlanetDecoration/LowPolyPlanetKit/Rock_0"
                        + (1 + stable % 4)
                        + ".prefab";
            }
        }

        private static void AddDistantDressing(
            GameObject environment,
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            AirCombatMapMode mode)
        {
            var root = new GameObject("OuterDistrictDressing");
            SceneManager.MoveGameObjectToScene(root, environment.scene);
            root.transform.SetParent(environment.transform, false);
            int count = mode == AirCombatMapMode.Horde ? 10 : 8;
            float extent = settings.mapSize * 0.36f;
            var candidates = new List<Vector2>(16);
            const int gridSide = 5;
            for (int z = 0; z < gridSide; z++)
            for (int x = 0; x < gridSide; x++)
            {
                if (x != 0 && x != gridSide - 1
                    && z != 0 && z != gridSide - 1)
                {
                    continue;
                }
                candidates.Add(new Vector2(
                    Mathf.Lerp(-extent, extent, x / 4f),
                    Mathf.Lerp(-extent, extent, z / 4f)));
            }
            PhysicMaterial collisionMaterial =
                FindObstacleMaterial(environment);
            int obstacleLayer = LayerMask.NameToLayer("CombatObstacle");
            if (obstacleLayer < 0)
                obstacleLayer = environment.layer;
            int start = PositiveHash(plan.seed) % candidates.Count;
            for (int index = 0; index < count; index++)
            {
                int stable = PositiveHash(unchecked(
                    plan.seed ^ (index + 1) * 486187739));
                Vector2 local = candidates[
                    (start + index * 5) % candidates.Count];
                local += new Vector2(
                    ((stable >> 5) & 31) - 15f,
                    ((stable >> 11) & 31) - 15f);
                float targetHeight = mode == AirCombatMapMode.Horde
                    ? 68f + stable % 58
                    : 42f + stable % 34;
                Vector3 targetSize = mode == AirCombatMapMode.Horde
                    ? new Vector3(
                        targetHeight * 0.58f,
                        targetHeight,
                        targetHeight * 0.5f)
                    : new Vector3(
                        targetHeight * 0.72f,
                        targetHeight,
                        targetHeight * 0.72f);
                if (!OuterPlacementIsClear(
                        environment,
                        plan,
                        local,
                        targetSize,
                        settings))
                {
                    continue;
                }
                float worldX = plan.mapCenter.x + local.x;
                float worldZ = plan.mapCenter.z + local.y;
                float ground = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ);
                var anchor = new GameObject(
                    "OuterDistrict_" + index.ToString("D2"));
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.localPosition = new Vector3(
                    local.x,
                    ground - plan.mapCenter.y + targetHeight * 0.5f,
                    local.y);
                anchor.transform.localRotation = Quaternion.Euler(
                    0f,
                    stable % 2 == 0 ? 0f : 90f,
                    0f);
                string path = mode == AirCombatMapMode.Horde
                    ? "Assets/Pack/Models/Building/Building "
                        + (1 + stable % 25).ToString("00") + ".FBX"
                    : "Assets/PlanetDecoration/LowPolyPlanetKit/Rock_0"
                        + (1 + stable % 4) + ".prefab";
                GameObject asset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                    continue;
                GameObject model = InstantiateVisualAsset(
                    asset,
                    anchor.transform,
                    environment.scene);
                if (model != null)
                {
                    if (!FitVisualToSize(
                            model,
                            anchor.transform,
                            targetSize))
                    {
                        UnityEngine.Object.DestroyImmediate(anchor);
                        continue;
                    }
                    if (mode == AirCombatMapMode.Horde
                        && !HasCompleteBuildingShell(model))
                    {
                        UnityEngine.Object.DestroyImmediate(anchor);
                        continue;
                    }
                    model.name = mode == AirCombatMapMode.Horde
                        ? "OuterBuildingModel"
                        : "OuterRockModel";
                    anchor.layer = obstacleLayer;
                    SetLayerRecursively(anchor.transform, obstacleLayer);
                    BoxCollider collider = anchor.AddComponent<BoxCollider>();
                    collider.size = targetSize;
                    collider.isTrigger = false;
                    collider.sharedMaterial = collisionMaterial;
                }
            }
        }

        private static bool OuterPlacementIsClear(
            GameObject environment,
            CombatSemanticPlan plan,
            Vector2 local,
            Vector3 size,
            AirCombatMapSettings settings)
        {
            float radius = Mathf.Sqrt(
                size.x * size.x + size.z * size.z) * 0.5f;
            float requiredGap = Mathf.Max(
                settings.designTurnRadius * 0.9f,
                settings.vehicleWingspan * 2.5f);
            Vector2 world = new Vector2(
                plan.mapCenter.x + local.x,
                plan.mapCenter.z + local.y);
            BoxCollider[] existing =
                environment.GetComponentsInChildren<BoxCollider>(true);
            for (int index = 0; index < existing.Length; index++)
            {
                BoxCollider collider = existing[index];
                if (collider == null)
                    continue;
                Bounds bounds = collider.bounds;
                float otherRadius = Mathf.Sqrt(
                    bounds.size.x * bounds.size.x
                    + bounds.size.z * bounds.size.z) * 0.5f;
                if (Vector2.Distance(
                        world,
                        new Vector2(bounds.center.x, bounds.center.z))
                    < radius + otherRadius + requiredGap)
                {
                    return false;
                }
            }
            return true;
        }

        private static PhysicMaterial FindObstacleMaterial(
            GameObject environment)
        {
            Collider[] colliders =
                environment.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                if (colliders[index] != null
                    && colliders[index].sharedMaterial != null)
                {
                    return colliders[index].sharedMaterial;
                }
            }
            return null;
        }

        private static void SetLayerRecursively(
            Transform root,
            int layer)
        {
            if (root == null)
                return;
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
                SetLayerRecursively(root.GetChild(index), layer);
        }

        private static int PositiveHash(int value)
        {
            unchecked
            {
                uint result = (uint)value;
                result ^= result >> 16;
                result *= 0x7FEB352Du;
                result ^= result >> 15;
                return (int)(result & 0x7FFFFFFFu);
            }
        }

        private static GameObject InstantiateVisualAsset(
            GameObject asset,
            Transform parent,
            Scene scene)
        {
            GameObject instance = UnityEngine.Object.Instantiate(asset);
            if (instance == null)
                return null;
            ClearHideFlags(instance);
            SceneManager.MoveGameObjectToScene(instance, scene);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Collider[] colliders =
                instance.GetComponentsInChildren<Collider>(true);
            for (int i = colliders.Length - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(colliders[i]);
            Rigidbody[] bodies =
                instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = bodies.Length - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(bodies[i]);
            return instance;
        }

        private static bool FitVisualToSize(
            GameObject model,
            Transform anchor,
            Vector3 targetSize)
        {
            Bounds bounds;
            if (!TryGetRendererBoundsInSpace(
                    model,
                    anchor,
                    out bounds))
            {
                return false;
            }
            Vector3 source = bounds.size;
            Vector3 scale = new Vector3(
                targetSize.x / Mathf.Max(0.01f, source.x),
                targetSize.y / Mathf.Max(0.01f, source.y),
                targetSize.z / Mathf.Max(0.01f, source.z));
            model.transform.localScale = Vector3.Scale(
                model.transform.localScale,
                scale);
            if (!TryGetRendererBoundsInSpace(
                    model,
                    anchor,
                    out bounds))
            {
                return false;
            }
            model.transform.localPosition -= bounds.center;
            if (!TryGetRendererBoundsInSpace(
                    model,
                    anchor,
                    out bounds))
            {
                return false;
            }
            Vector3 fitted = bounds.size;
            return DimensionFits(fitted.x, targetSize.x)
                && DimensionFits(fitted.y, targetSize.y)
                && DimensionFits(fitted.z, targetSize.z);
        }

        private static bool DimensionFits(float actual, float target)
        {
            float tolerance = Mathf.Max(0.5f, target * 0.08f);
            return Mathf.Abs(actual - target) <= tolerance;
        }

        private static bool TryGetRendererBoundsInSpace(
            GameObject root,
            Transform targetSpace,
            out Bounds bounds)
        {
            bool found = false;
            bounds = new Bounds();
            MeshFilter[] filters =
                root.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < filters.Length; index++)
            {
                MeshFilter filter = filters[index];
                if (filter == null || filter.sharedMesh == null)
                    continue;
                EncapsulateTransformedBounds(
                    filter.sharedMesh.bounds,
                    filter.transform,
                    targetSpace,
                    ref bounds,
                    ref found);
            }
            SkinnedMeshRenderer[] skinned =
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int index = 0; index < skinned.Length; index++)
            {
                SkinnedMeshRenderer renderer = skinned[index];
                if (renderer == null || renderer.sharedMesh == null)
                    continue;
                EncapsulateTransformedBounds(
                    renderer.localBounds,
                    renderer.transform,
                    targetSpace,
                    ref bounds,
                    ref found);
            }
            return found;
        }

        private static void EncapsulateTransformedBounds(
            Bounds source,
            Transform sourceTransform,
            Transform targetSpace,
            ref Bounds result,
            ref bool found)
        {
            Vector3 center = source.center;
            Vector3 extents = source.extents;
            for (int z = -1; z <= 1; z += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int x = -1; x <= 1; x += 2)
            {
                Vector3 local = center + Vector3.Scale(
                    extents,
                    new Vector3(x, y, z));
                Vector3 point = targetSpace.InverseTransformPoint(
                    sourceTransform.TransformPoint(local));
                if (!found)
                {
                    result = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                {
                    result.Encapsulate(point);
                }
            }
        }

        private static bool TryGetRendererBounds(
            GameObject root,
            out Bounds bounds)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            bounds = new Bounds();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static bool HasCompleteBuildingShell(GameObject root)
        {
            if (root == null)
                return false;
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Material[] materials = renderers[index].sharedMaterials;
                bool validMaterial = false;
                for (int material = 0;
                     material < materials.Length;
                     material++)
                {
                    Shader shader = materials[material] != null
                        ? materials[material].shader
                        : null;
                    if (shader != null
                        && shader.name != "Hidden/InternalErrorShader")
                    {
                        validMaterial = true;
                        break;
                    }
                }
                if (!validMaterial)
                    return false;
            }

            Bounds bounds;
            if (!TryGetRendererBounds(root, out bounds)
                || bounds.size.x < 2f
                || bounds.size.y < 2f
                || bounds.size.z < 2f)
            {
                return false;
            }

            bool positiveX = false;
            bool negativeX = false;
            bool positiveZ = false;
            bool negativeZ = false;
            MeshFilter[] filters =
                root.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < filters.Length; index++)
            {
                AccumulateHorizontalFaces(
                    filters[index].sharedMesh,
                    filters[index].transform,
                    ref positiveX,
                    ref negativeX,
                    ref positiveZ,
                    ref negativeZ);
            }
            SkinnedMeshRenderer[] skinned =
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int index = 0; index < skinned.Length; index++)
            {
                AccumulateHorizontalFaces(
                    skinned[index].sharedMesh,
                    skinned[index].transform,
                    ref positiveX,
                    ref negativeX,
                    ref positiveZ,
                    ref negativeZ);
            }
            return positiveX && negativeX && positiveZ && negativeZ;
        }

        private static void AccumulateHorizontalFaces(
            Mesh mesh,
            Transform transform,
            ref bool positiveX,
            ref bool negativeX,
            ref bool positiveZ,
            ref bool negativeZ)
        {
            if (mesh == null || transform == null)
                return;
            Vector3[] normals;
            try
            {
                normals = mesh.normals;
            }
            catch (Exception)
            {
                return;
            }
            for (int index = 0; index < normals.Length; index++)
            {
                Vector3 normal = transform
                    .TransformDirection(normals[index])
                    .normalized;
                if (Mathf.Abs(normal.y) > 0.92f)
                    continue;
                positiveX |= normal.x > 0.45f;
                negativeX |= normal.x < -0.45f;
                positiveZ |= normal.z > 0.45f;
                negativeZ |= normal.z < -0.45f;
                if (positiveX && negativeX && positiveZ && negativeZ)
                    return;
            }
        }

        private static MonoBehaviour FindRuntimeController(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                MonoBehaviour[] behaviours = roots[i].GetComponentsInChildren<MonoBehaviour>(true);
                for (int j = 0; j < behaviours.Length; j++)
                {
                    if (behaviours[j] != null &&
                        behaviours[j].GetType().Name == "CombatMapRuntimeController")
                    {
                        return behaviours[j];
                    }
                }
            }

            return null;
        }

        private static GameObject ReadGeneratedRoot(MonoBehaviour runtime)
        {
            Type type = runtime.GetType();
            PropertyInfo property = type.GetProperty(
                "GeneratedRoot",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                Component component = property.GetValue(runtime, null) as Component;
                if (component != null)
                {
                    return component.gameObject;
                }

                GameObject gameObject = property.GetValue(runtime, null) as GameObject;
                if (gameObject != null)
                {
                    return gameObject;
                }
            }

            Transform[] transforms = runtime.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name.IndexOf("Generated", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return transforms[i].gameObject;
                }
            }

            return null;
        }

        private static void ResolveCombatSpawns(
            MonoBehaviour runtime,
            Transform root,
            out Vector3 playerPosition,
            out Quaternion playerRotation,
            out Vector3 enemyPosition,
            out Quaternion enemyRotation)
        {
            playerPosition = Vector3.zero;
            playerRotation = Quaternion.identity;
            enemyPosition = Vector3.zero;
            enemyRotation = Quaternion.identity;

            CombatMapRuntimeController controller =
                runtime as CombatMapRuntimeController;
            bool resolvedFromPlan = false;
            if (controller != null)
            {
                Vector3 resolvedPlayerPosition;
                Quaternion resolvedPlayerRotation;
                Vector3 resolvedEnemyPosition;
                Quaternion resolvedEnemyRotation;
                bool playerResolved = controller.TryGetSpawnPose(
                    true,
                    out resolvedPlayerPosition,
                    out resolvedPlayerRotation);
                bool enemyResolved = controller.TryGetSpawnPose(
                    false,
                    out resolvedEnemyPosition,
                    out resolvedEnemyRotation);
                if (playerResolved && enemyResolved)
                {
                    playerPosition = resolvedPlayerPosition;
                    playerRotation = resolvedPlayerRotation;
                    enemyPosition = resolvedEnemyPosition;
                    enemyRotation = resolvedEnemyRotation;
                    resolvedFromPlan = true;
                }
            }

            if (!resolvedFromPlan)
            {
                bool playerFound = TryResolveSpawnMarker(
                    root,
                    "spawn.player",
                    out playerPosition,
                    out playerRotation);
                bool enemyFound = TryResolveSpawnMarker(
                    root,
                    "spawn.enemy",
                    out enemyPosition,
                    out enemyRotation);
                if (!playerFound || !enemyFound)
                {
                    throw new InvalidOperationException(
                        "CombatMapLab 缺少精确的 spawn.player 或 spawn.enemy 语义锚点");
                }

                Vector3 playerToEnemy = enemyPosition - playerPosition;
                if (playerToEnemy.sqrMagnitude > 0.0001f)
                {
                    playerRotation = Quaternion.LookRotation(
                        playerToEnemy.normalized,
                        Vector3.up);
                    enemyRotation = Quaternion.LookRotation(
                        -playerToEnemy.normalized,
                        Vector3.up);
                }
            }

            if (!IsFinite(playerPosition) ||
                !IsFinite(enemyPosition) ||
                !IsFinite(playerRotation) ||
                !IsFinite(enemyRotation))
            {
                throw new InvalidOperationException(
                    "CombatMapLab 出生点包含非有限坐标或旋转");
            }

            if ((playerPosition - enemyPosition).sqrMagnitude < 100f)
            {
                throw new InvalidOperationException(
                    "CombatMapLab 玩家与敌机出生点重叠或距离不足10米");
            }
        }

        private static bool TryResolveSpawnMarker(
            Transform root,
            string stableId,
            out Vector3 position,
            out Quaternion rotation)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (string.Equals(
                        transforms[i].name,
                        stableId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    position = transforms[i].position;
                    rotation = transforms[i].rotation;
                    return true;
                }
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            float magnitudeSquared =
                value.x * value.x +
                value.y * value.y +
                value.z * value.z +
                value.w * value.w;
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w) &&
                   magnitudeSquared > 0.0001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void RemoveEditorOnlyObjects(GameObject root)
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                string typeName = behaviour.GetType().Name;
                if (typeName.IndexOf("RuntimeController", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }

            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
            for (int i = canvases.Length - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(canvases[i].gameObject);
            }

            Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
            for (int i = cameras.Length - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(cameras[i].gameObject);
            }
        }

        private static void PersistGeneratedAssets(
            GameObject root,
            AirCombatMapMode mode)
        {
            string assetDirectory = mode == AirCombatMapMode.Horde
                ? HordeAssetDirectory
                : DuelAssetDirectory;
            Dictionary<Mesh, Mesh> meshCopies = new Dictionary<Mesh, Mesh>();
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh source = filters[i].sharedMesh;
                if (source == null || AssetDatabase.Contains(source))
                {
                    continue;
                }

                Mesh copy;
                if (!meshCopies.TryGetValue(source, out copy))
                {
                    copy = UnityEngine.Object.Instantiate(source);
                    copy.name = "CombatMapMesh_" + meshCopies.Count.ToString("000");
                    string path = AssetDatabase.GenerateUniqueAssetPath(
                        assetDirectory + "/" + copy.name + ".asset");
                    AssetDatabase.CreateAsset(copy, path);
                    meshCopies[source] = copy;
                }

                filters[i].sharedMesh = copy;
                MeshCollider collider = filters[i].GetComponent<MeshCollider>();
                if (collider != null && collider.sharedMesh == source)
                {
                    collider.sharedMesh = copy;
                }
            }

            MeshCollider[] meshColliders =
                root.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < meshColliders.Length; i++)
            {
                Mesh source = meshColliders[i].sharedMesh;
                if (source == null || AssetDatabase.Contains(source))
                {
                    continue;
                }

                Mesh copy;
                if (!meshCopies.TryGetValue(source, out copy))
                {
                    copy = UnityEngine.Object.Instantiate(source);
                    copy.name =
                        "CombatMapCollision_" +
                        meshCopies.Count.ToString("000");
                    string path =
                        AssetDatabase.GenerateUniqueAssetPath(
                            assetDirectory +
                            "/" +
                            copy.name +
                            ".asset");
                    AssetDatabase.CreateAsset(copy, path);
                    meshCopies[source] = copy;
                }

                meshColliders[i].sharedMesh = copy;
            }

            Dictionary<Material, Material> materialCopies =
                new Dictionary<Material, Material>();
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Material[] materials =
                    renderers[rendererIndex].sharedMaterials;
                bool changed = false;
                for (int materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    Material source = materials[materialIndex];
                    if (source == null ||
                        AssetDatabase.Contains(source))
                    {
                        continue;
                    }

                    Material copy;
                    if (!materialCopies.TryGetValue(
                            source,
                            out copy))
                    {
                        copy =
                            UnityEngine.Object.Instantiate(source);
                        copy.name =
                            "CombatMapMaterial_" +
                            materialCopies.Count.ToString("000");
                        string path =
                            AssetDatabase.GenerateUniqueAssetPath(
                                assetDirectory +
                                "/" +
                                copy.name +
                                ".mat");
                        AssetDatabase.CreateAsset(copy, path);
                        materialCopies[source] = copy;
                    }

                    materials[materialIndex] = copy;
                    changed = true;
                }

                if (changed)
                {
                    renderers[rendererIndex].sharedMaterials =
                        materials;
                }
            }
        }

        private static void PrepareGeneratedModeDirectories()
        {
            RecreateGeneratedDirectory(DuelAssetDirectory);
            RecreateGeneratedDirectory(HordeAssetDirectory);
        }

        private static void RecreateGeneratedDirectory(string directory)
        {
            if (AssetDatabase.IsValidFolder(directory))
            {
                if (!AssetDatabase.DeleteAsset(directory))
                {
                    throw new IOException(
                        "无法清理 CombatMap 生成目录：" + directory);
                }
            }
            EnsureDirectory(directory);
        }

        private static void AddToBuildSettings(string scenePath)
        {
            List<EditorBuildSettingsScene> scenes =
                new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == scenePath)
                {
                    scenes[i].enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureDirectory(string assetDirectory)
        {
            string[] segments = assetDirectory.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }

        private static void Invoke(MonoBehaviour target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(
                method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (info == null)
            {
                throw new MissingMethodException(target.GetType().FullName, method);
            }

            info.Invoke(target, null);
        }
    }
}
#endif
