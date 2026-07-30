using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CombatMap;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.CombatMap.Editor
{
    /// <summary>
    /// Creates and maintains the isolated CombatMapLab scene without
    /// modifying the ModularAssemblyLab template after the initial copy.
    /// </summary>
    public static class CombatMapLabAssetBuilder
    {
        public const string SourceScenePath =
            "Assets/Scenes/ModularAssemblyLab.unity";
        public const string ScenePath =
            "Assets/Scenes/CombatMapLab.unity";
        public const string AssetRoot = "Assets/CombatMapLab";
        public const string RecipeFolder =
            AssetRoot + "/Recipes";
        public const string DefaultRecipePath =
            RecipeFolder + "/AirDuel_Default.asset";
        public const string SemanticV2RecipePath =
            RecipeFolder + "/AirDuel_SemanticV2.asset";
        public const string ScreenshotFolder =
            "Assets/Screenshots/CombatMapLab";

        const string RuntimeControllerTypeName =
            "UnityPlanet.CombatMap.CombatMapRuntimeController";
        const string GeneratedRootName = "GeneratedCombatMap";

        static readonly string[] RequiredLayers =
        {
            "CombatTerrain",
            "CombatObstacle",
            "CombatBoundary",
            "ModularVehicle",
            "BuildOnly",
            "CombatDebug"
        };

        [MenuItem("Tools/Combat Map/Rebuild Combat Map Lab Scene")]
        public static void RebuildLaboratoryAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "进入或运行 Play 模式时不能重建战斗地图实验场。");
                return;
            }

            EnsureFolders();
            AirCombatMapRecipe recipe = EnsureSemanticV2Recipe();
            if (!EnsureSceneAssetExists())
                return;

            string result = ConfigureSceneAsset(recipe, true);
            RemoveSceneFromBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(result);
        }

        [MenuItem("Tools/Combat Map/Open Combat Map Lab")]
        public static void OpenLaboratoryFromMenu()
        {
            OpenLaboratoryScene();
        }

        [MenuItem("Tools/战斗地图/重建战斗地图实验场")]
        static void RebuildLaboratoryAssetsChinese()
        {
            RebuildLaboratoryAssets();
        }

        [MenuItem("Tools/战斗地图/打开战斗地图实验场")]
        static void OpenLaboratoryFromChineseMenu()
        {
            OpenLaboratoryScene();
        }

        public static bool EnsureLaboratoryAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            EnsureFolders();
            AirCombatMapRecipe recipe = EnsureSemanticV2Recipe();
            if (!EnsureSceneAssetExists())
                return false;

            ConfigureSceneAsset(recipe, false);
            RemoveSceneFromBuildSettings();
            AssetDatabase.SaveAssets();
            return true;
        }

        public static bool OpenLaboratoryScene(
            bool promptToSaveCurrentScene = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "Play 模式中不能切换战斗地图实验场场景。");
                return false;
            }

            if (!EnsureLaboratoryAssets())
                return false;

            Scene active = SceneManager.GetActiveScene();
            if (active.path == ScenePath)
                return true;

            if (promptToSaveCurrentScene
                && !EditorSceneManager
                    .SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
            return true;
        }

        public static AirCombatMapRecipe EnsureDefaultRecipe()
        {
            EnsureFolders();
            AirCombatMapRecipe recipe =
                AssetDatabase.LoadAssetAtPath<AirCombatMapRecipe>(
                    DefaultRecipePath);
            if (recipe != null)
            {
                recipe.name = "默认空战决斗配方";
                recipe.Clamp();
                EditorUtility.SetDirty(recipe);
                return recipe;
            }

            recipe =
                ScriptableObject.CreateInstance<AirCombatMapRecipe>();
            recipe.name = "默认空战决斗配方";
            recipe.Clamp();
            AssetDatabase.CreateAsset(recipe, DefaultRecipePath);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssets();
            return recipe;
        }

        public static AirCombatMapRecipe EnsureSemanticV2Recipe()
        {
            EnsureFolders();
            EnsureProjectLayers();
            AirCombatMapRecipe recipe =
                AssetDatabase.LoadAssetAtPath<AirCombatMapRecipe>(
                    SemanticV2RecipePath);
            if (recipe != null)
            {
                recipe.name = "语义优先空战决斗 V2";
                recipe.Clamp();
                EditorUtility.SetDirty(recipe);
                return recipe;
            }

            recipe =
                ScriptableObject.CreateInstance<AirCombatMapRecipe>();
            recipe.name = "语义优先空战决斗 V2";
            recipe.Clamp();
            AssetDatabase.CreateAsset(
                recipe,
                SemanticV2RecipePath);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssets();
            return recipe;
        }

        public static Component FindController()
        {
            return FindController(SceneManager.GetActiveScene());
        }

        public static Component FindController(Scene scene)
        {
            Type type = FindRuntimeControllerType();
            if (type == null || !scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Component value = root.GetComponentInChildren(
                    type,
                    true);
                if (value != null)
                    return value;
            }
            return null;
        }

        public static string InvokeController(
            Component controller,
            string operation,
            params object[] arguments)
        {
            if (controller == null)
                return "错误：缺少战斗地图运行时控制器。";

            MethodInfo method = FindCompatibleMethod(
                controller.GetType(),
                operation,
                arguments);
            if (method == null)
            {
                return "错误：运行时控制器不支持操作“" + operation
                    + "”（" + controller.GetType().FullName + "）。";
            }

            try
            {
                object result = method.Invoke(controller, arguments);
                return result == null ? "完成" : result.ToString();
            }
            catch (TargetInvocationException exception)
            {
                Exception cause =
                    exception.InnerException ?? exception;
                Debug.LogException(cause);
                return "错误：" + cause.Message;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return "错误：" + exception.Message;
            }
        }

        public static object ReadControllerMember(
            Component controller,
            params string[] names)
        {
            if (controller == null)
                return null;

            const BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            Type type = controller.GetType();
            foreach (string name in names)
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null
                    && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(controller, null);
                }

                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                    return field.GetValue(controller);
            }
            return null;
        }

        public static void AssignRecipe(
            Component controller,
            AirCombatMapRecipe recipe)
        {
            if (controller == null)
                return;
            BindRecipe(controller, recipe);
            EditorUtility.SetDirty(controller);
        }

        public static string CapturePreview(
            int width = 1600,
            int height = 900)
        {
            width = Mathf.Clamp(width, 320, 4096);
            height = Mathf.Clamp(height, 180, 2160);
            Directory.CreateDirectory(ScreenshotFolder);

            string fileName = "CombatMapLab_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                + "_" + width + "x" + height + ".png";
            string assetPath = ScreenshotFolder + "/" + fileName;

            Camera source =
                Application.isPlaying ? Camera.main : null;
            GameObject temporaryObject = null;
            Camera captureCamera = source;
            if (captureCamera == null)
            {
                temporaryObject = new GameObject(
                    "CombatMapLabCaptureCamera",
                    typeof(Camera));
                temporaryObject.hideFlags =
                    HideFlags.HideAndDontSave;
                captureCamera =
                    temporaryObject.GetComponent<Camera>();
                ConfigureOverviewCamera(captureCamera);
            }

            RenderTexture previousTarget =
                captureCamera.targetTexture;
            RenderTexture previousActive =
                RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var image = new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false);

            try
            {
                captureCamera.targetTexture = target;
                captureCamera.Render();
                RenderTexture.active = target;
                image.ReadPixels(
                    new Rect(0f, 0f, width, height),
                    0,
                    0,
                    false);
                image.Apply(false, false);
                File.WriteAllBytes(
                    assetPath,
                    image.EncodeToPNG());
            }
            finally
            {
                captureCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
                if (temporaryObject != null)
                    UnityEngine.Object.DestroyImmediate(
                        temporaryObject);
            }

            AssetDatabase.Refresh();
            UnityEngine.Object screenshot =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    assetPath);
            if (screenshot != null)
            {
                Selection.activeObject = screenshot;
                EditorGUIUtility.PingObject(screenshot);
            }
            Debug.Log("战斗地图实验场截图：" + assetPath);
            return Path.GetFullPath(assetPath);
        }

        static bool EnsureSceneAssetExists()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    ScenePath) != null)
            {
                return true;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    SourceScenePath) == null)
            {
                Debug.LogError(
                    "缺少战斗地图实验场的模板场景："
                    + SourceScenePath);
                return false;
            }

            if (!AssetDatabase.CopyAsset(
                    SourceScenePath,
                    ScenePath))
            {
                Debug.LogError(
                    "无法将 ModularAssemblyLab 模板复制到："
                    + ScenePath);
                return false;
            }

            AssetDatabase.ImportAsset(
                ScenePath,
                ImportAssetOptions.ForceUpdate);
            return true;
        }

        static string ConfigureSceneAsset(
            AirCombatMapRecipe recipe,
            bool rebuildGeneratedMap)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool loadedForWork =
                !scene.IsValid() || !scene.isLoaded;

            if (loadedForWork)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Additive);
            }

            SceneManager.SetActiveScene(scene);
            string generationResult = "未请求生成";
            try
            {
                GameObject host = FindOrCreateHost(scene);
                ModularLabSceneProfile profile =
                    host.GetComponent<ModularLabSceneProfile>();
                if (profile == null)
                    profile =
                        host.AddComponent<ModularLabSceneProfile>();
                // This first lab validates map generation only. Combat is
                // deliberately disabled until the greybox is accepted.
                profile.Configure(true, false, false, true);

                RemovePlanetLabFlightEnvironment(scene);
                EnsureCamera(scene);
                EnsureDirectionalLight(scene);

                Component controller =
                    EnsureRuntimeController(scene, host);
                EnsureCombatMapFlightEnvironment(scene, controller);
                if (controller == null)
                {
                    generationResult =
                        "缺少运行时控制器类型";
                }
                else
                {
                    BindRecipe(controller, recipe);
                    if (rebuildGeneratedMap)
                    {
                        generationResult =
                            InvokeFirstAvailable(
                                controller,
                                new[]
                                {
                                    "GenerateAndApplyBestCandidate",
                                    "GenerateBestAndApply",
                                    "RebuildCurrentMap",
                                    "RebuildMap",
                                    "Rebuild"
                                });
                    }
                }

                EditorUtility.SetDirty(profile);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previousActive.IsValid()
                    && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }
                if (loadedForWork
                    && scene.IsValid()
                    && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return "战斗地图实验场已就绪：" + ScenePath
                + "；生成结果=" + generationResult;
        }

        static GameObject FindOrCreateHost(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<ModularAssemblyLabBootstrap>()
                    != null)
                {
                    return root;
                }
            }

            GameObject host = new GameObject("CombatMapLab");
            SceneManager.MoveGameObjectToScene(host, scene);
            return host;
        }

        static Component EnsureRuntimeController(
            Scene scene,
            GameObject fallbackHost)
        {
            Component existing = FindController(scene);
            if (existing != null)
                return existing;

            Type type = FindRuntimeControllerType();
            if (type == null)
            {
                Debug.LogError(
                    "战斗地图运行时控制器类型不可用。");
                return null;
            }

            GameObject root = new GameObject("CombatMapRuntime");
            SceneManager.MoveGameObjectToScene(root, scene);
            return root.AddComponent(type);
        }

        static void BindRecipe(
            Component controller,
            AirCombatMapRecipe recipe)
        {
            string[] methods =
            {
                "Configure",
                "ConfigureRecipe",
                "SetRecipe"
            };
            foreach (string methodName in methods)
            {
                MethodInfo method = FindCompatibleMethod(
                    controller.GetType(),
                    methodName,
                    new object[] { recipe });
                if (method == null)
                    continue;
                method.Invoke(
                    controller,
                    new object[] { recipe });
                return;
            }

            var serialized = new SerializedObject(controller);
            SerializedProperty property =
                serialized.FindProperty("recipe");
            if (property == null)
                property =
                    serialized.FindProperty("mapRecipe");
            if (property == null)
            {
                Debug.LogWarning(
                    "战斗地图运行时控制器没有可用的配方绑定接口。");
                return;
            }
            property.objectReferenceValue = recipe;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void RemovePlanetLabFlightEnvironment(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                PlanetLabFlightEnvironmentController[] values =
                    root.GetComponentsInChildren<
                        PlanetLabFlightEnvironmentController>(true);
                foreach (PlanetLabFlightEnvironmentController value
                         in values)
                {
                    UnityEngine.Object.DestroyImmediate(value);
                }
            }
        }

        static void EnsureCamera(Scene scene)
        {
            Camera[] cameras = scene.GetRootGameObjects()
                .SelectMany(
                    root => root.GetComponentsInChildren<Camera>(true))
                .ToArray();
            Camera camera = cameras.FirstOrDefault(
                value => value.CompareTag("MainCamera"));
            if (camera == null)
                camera = cameras.FirstOrDefault();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject(
                    "Main Camera",
                    typeof(Camera),
                    typeof(AudioListener));
                SceneManager.MoveGameObjectToScene(
                    cameraObject,
                    scene);
                cameraObject.tag = "MainCamera";
                camera = cameraObject.GetComponent<Camera>();
            }
            camera.farClipPlane = 5000f;
            EditorUtility.SetDirty(camera);
        }

        static void EnsureDirectionalLight(Scene scene)
        {
            Light light = scene.GetRootGameObjects()
                .SelectMany(
                    root => root.GetComponentsInChildren<Light>(true))
                .FirstOrDefault(
                    value => value.type == LightType.Directional);
            if (light != null)
                return;

            GameObject lightObject = new GameObject(
                "Directional Light",
                typeof(Light));
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.rotation =
                Quaternion.Euler(38f, -32f, 0f);
            light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
        }

        static Type FindRuntimeControllerType()
        {
            Type direct = Type.GetType(
                RuntimeControllerTypeName + ", Assembly-CSharp",
                false);
            if (direct != null)
                return direct;

            foreach (Assembly assembly
                     in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type value = assembly.GetType(
                    RuntimeControllerTypeName,
                    false);
                if (value != null)
                    return value;
            }
            return null;
        }

        static MethodInfo FindCompatibleMethod(
            Type type,
            string name,
            object[] arguments)
        {
            const BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (!string.Equals(
                        method.Name,
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters =
                    method.GetParameters();
                if (parameters.Length != arguments.Length)
                    continue;

                bool compatible = true;
                for (int index = 0;
                     index < parameters.Length;
                     index++)
                {
                    object argument = arguments[index];
                    if (argument != null
                        && !parameters[index].ParameterType
                            .IsInstanceOfType(argument))
                    {
                        compatible = false;
                        break;
                    }
                }
                if (compatible)
                    return method;
            }
            return null;
        }

        static string InvokeFirstAvailable(
            Component controller,
            string[] operations)
        {
            foreach (string operation in operations)
            {
                MethodInfo method = FindCompatibleMethod(
                    controller.GetType(),
                    operation,
                    Array.Empty<object>());
                if (method == null)
                    continue;
                return InvokeController(
                    controller,
                    operation);
            }
            return "缺少生成接口";
        }

        static void ConfigureOverviewCamera(Camera camera)
        {
            Bounds bounds = FindGeneratedBounds();
            float extent = Mathf.Max(
                300f,
                Mathf.Max(bounds.extents.x, bounds.extents.z));
            Vector3 focus =
                bounds.center + Vector3.up * bounds.extents.y * 0.2f;
            camera.transform.position =
                focus + new Vector3(
                    -extent * 0.72f,
                    extent * 0.9f,
                    -extent * 0.88f);
            camera.transform.rotation = Quaternion.LookRotation(
                focus - camera.transform.position,
                Vector3.up);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 5000f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
        }

        static Bounds FindGeneratedBounds()
        {
            GameObject generated =
                GameObject.Find(GeneratedRootName);
            Renderer[] renderers = generated != null
                ? generated.GetComponentsInChildren<Renderer>(true)
                : Array.Empty<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(
                    new Vector3(0f, 60f, 1000f),
                    new Vector3(1536f, 220f, 1536f));
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1;
                 index < renderers.Length;
                 index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets", "CombatMapLab");
            EnsureFolder(AssetRoot, "Recipes");
            EnsureFolder("Assets", "Screenshots");
            EnsureFolder("Assets/Screenshots", "CombatMapLab");
            EnsureFolder("Assets", "Scenes");
        }

        static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        static void RemoveSceneFromBuildSettings()
        {
            EditorBuildSettingsScene[] filtered =
                EditorBuildSettings.scenes
                    .Where(scene => scene.path != ScenePath)
                    .ToArray();
            if (filtered.Length
                != EditorBuildSettings.scenes.Length)
            {
                EditorBuildSettings.scenes = filtered;
            }
        }
    

static void EnsureProjectLayers()
        {
            UnityEngine.Object tagManager =
                AssetDatabase.LoadAllAssetsAtPath(
                    "ProjectSettings/TagManager.asset")
                    .FirstOrDefault();
            if (tagManager == null)
                return;

            var serialized = new SerializedObject(tagManager);
            SerializedProperty layers =
                serialized.FindProperty("layers");
            if (layers == null || !layers.isArray)
                return;
            int[] preferred = { 24, 25, 26, 27, 28, 30 };
            bool changed = false;
            for (int nameIndex = 0;
                 nameIndex < RequiredLayers.Length;
                 nameIndex++)
            {
                string layerName = RequiredLayers[nameIndex];
                bool exists = false;
                for (int index = 8; index < layers.arraySize; index++)
                {
                    if (string.Equals(
                            layers.GetArrayElementAtIndex(index).stringValue,
                            layerName,
                            StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                    continue;

                int slot = preferred[nameIndex];
                if (slot >= layers.arraySize ||
                    !string.IsNullOrEmpty(
                        layers.GetArrayElementAtIndex(slot).stringValue))
                {
                    slot = -1;
                    for (int index = 8;
                         index < Mathf.Min(31, layers.arraySize);
                         index++)
                    {
                        if (string.IsNullOrEmpty(
                                layers.GetArrayElementAtIndex(index)
                                    .stringValue))
                        {
                            slot = index;
                            break;
                        }
                    }
                }
                if (slot < 0)
                {
                    Debug.LogError(
                        "没有空闲 Unity Layer 可创建：" + layerName);
                    continue;
                }
                layers.GetArrayElementAtIndex(slot).stringValue = layerName;
                changed = true;
            }
            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }


static void EnsureCombatMapFlightEnvironment(
            Scene scene,
            Component controller)
        {
            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
                return;

            CombatMapFlightEnvironmentController canonical =
                runtime.GetComponent<
                    CombatMapFlightEnvironmentController>();
            if (canonical == null)
            {
                canonical = runtime.gameObject.AddComponent<
                    CombatMapFlightEnvironmentController>();
            }
            canonical.Configure(runtime);
            EditorUtility.SetDirty(canonical);

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (CombatMapFlightEnvironmentController value in
                     root.GetComponentsInChildren<
                         CombatMapFlightEnvironmentController>(true))
            {
                if (value != null && value != canonical)
                    UnityEngine.Object.DestroyImmediate(value);
            }
        }
}
}
