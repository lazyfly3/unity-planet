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

        [InitializeOnLoadMethod]
        private static void ScheduleMissingRuntimeBake()
        {
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

            Invoke(runtime, "Rebuild");
            GameObject generatedRoot = ReadGeneratedRoot(runtime);
            if (generatedRoot == null)
            {
                throw new InvalidOperationException("CombatMapLab 没有生成环境根节点");
            }

            Vector3 playerPosition;
            Quaternion playerRotation;
            Vector3 enemyPosition;
            Quaternion enemyRotation;
            ResolveCombatSpawns(
                runtime,
                generatedRoot.transform,
                out playerPosition,
                out playerRotation,
                out enemyPosition,
                out enemyRotation);

            EnsureDirectory(GeneratedAssetDirectory);
            Scene runtimeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject environment = UnityEngine.Object.Instantiate(generatedRoot);
            environment.name = "CombatMapEnvironment";
            SceneManager.MoveGameObjectToScene(environment, runtimeScene);
            RemoveEditorOnlyObjects(environment);
            PersistGeneratedAssets(environment);

            GameObject providerObject = new GameObject("BakedCombatMapFlightEnvironment");
            SceneManager.MoveGameObjectToScene(providerObject, runtimeScene);
            BakedCombatMapFlightEnvironment provider =
                providerObject.AddComponent<BakedCombatMapFlightEnvironment>();
            provider.ConfigureBaked(
                environment,
                playerPosition,
                playerRotation,
                enemyPosition,
                enemyRotation,
                (playerPosition + enemyPosition) * 0.5f,
                1200f,
                1500f);

            EditorSceneManager.SaveScene(runtimeScene, RuntimeScenePath);
            AddToBuildSettings(RuntimeScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

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
                Debug.LogError(
                    "[CombatMap] 自动烘焙运行时场景失败：" +
                    exception);
            }
        }

        private static void BakeRuntimeSceneSilently()
        {
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

            Invoke(runtime, "Rebuild");
            GameObject generatedRoot = ReadGeneratedRoot(runtime);
            if (generatedRoot == null)
            {
                throw new InvalidOperationException(
                    "CombatMapLab 没有生成环境根节点");
            }

            Vector3 playerPosition;
            Quaternion playerRotation;
            Vector3 enemyPosition;
            Quaternion enemyRotation;
            ResolveCombatSpawns(
                runtime,
                generatedRoot.transform,
                out playerPosition,
                out playerRotation,
                out enemyPosition,
                out enemyRotation);

            EnsureDirectory(GeneratedAssetDirectory);
            Scene runtimeScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            GameObject environment =
                UnityEngine.Object.Instantiate(generatedRoot);
            environment.name = "CombatMapEnvironment";
            ClearHideFlags(environment);
            SceneManager.MoveGameObjectToScene(
                environment,
                runtimeScene);
            RemoveEditorOnlyObjects(environment);
            PersistGeneratedAssets(environment);

            GameObject providerObject =
                new GameObject("BakedCombatMapFlightEnvironment");
            SceneManager.MoveGameObjectToScene(
                providerObject,
                runtimeScene);
            BakedCombatMapFlightEnvironment provider =
                providerObject.AddComponent<
                    BakedCombatMapFlightEnvironment>();
            provider.ConfigureBaked(
                environment,
                playerPosition,
                playerRotation,
                enemyPosition,
                enemyRotation,
                (playerPosition + enemyPosition) * 0.5f,
                1200f,
                1500f);

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

            AssetDatabase.SaveAssets();
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
                            "bakeVersion: 3",
                            StringComparison.Ordinal) < 0;
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

        private static void PersistGeneratedAssets(GameObject root)
        {
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
                        GeneratedAssetDirectory + "/" + copy.name + ".asset");
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
                            GeneratedAssetDirectory +
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
                                GeneratedAssetDirectory +
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
