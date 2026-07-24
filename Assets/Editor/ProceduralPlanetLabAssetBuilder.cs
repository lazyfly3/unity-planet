using System;
using System.IO;
using System.Linq;
using KDL.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ProceduralPlanetLabAssetBuilder
{
    public const string ScenePath = "Assets/Scenes/PlanetLab.unity";
    public const string PresetFolder = "Assets/PlanetLab/Presets";
    public const string ScreenshotFolder = "Assets/Screenshots/PlanetLab";
    public const string DefaultPresetPath = PresetFolder + "/TemperateOcean.asset";

    [MenuItem("Tools/Voxel Planet/Rebuild Procedural Planet Lab Scene")]
    [AICallable(
        "Rebuild the isolated PlanetLab scene and bind its planar PBR library.",
        Category = "PlanetLab.PBR",
        Kind = ToolKind.Write)]
    public static void RebuildLaboratoryAssets()
    {
        EnsureFolders();
        EnsureExamplePresets();
        PlanetLabTerrainPbrLibraryBuilder.EnsureLibrary();
        PlanetLabSkyboxLibraryBuilder.EnsureLibrary();
        CreateOrReplaceSceneAsset();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Procedural Planet Lab rebuilt: " + ScenePath);
    }

    [AICallable(
        "Activate and rebuild the PlanetLab planar PBR experiment for visual validation.",
        Category = "PlanetLab.PBR")]
    public static string ActivatePlanarForValidation()
    {
        ProceduralPlanetLabController controller = FindController();
        if (controller == null)
            return "controller-missing";
        controller.SetSurfaceMode(PlanetLabSurfaceMode.Planar);
        controller.RebuildPlanarNow();
        MeshCollider collider =
            controller.GetComponentInChildren<MeshCollider>(true);
        return $"mode={controller.SurfaceMode} vertices="
            + $"{controller.PlanarTerrainMesh?.vertexCount ?? 0} "
            + $"collider={(collider != null && collider.sharedMesh != null)} "
            + $"pbr={controller.PlanarPbrLibrary?.IsReady == true}";
    }

    [AICallable(
        "Activate and rebuild the PlanetLab streaming infinite planar experiment.",
        Category = "PlanetLab.Infinite")]
    public static string ActivateInfinitePlanarForValidation()
    {
        ProceduralPlanetLabController controller = FindController();
        if (controller == null)
            return "controller-missing";
        controller.SetSurfaceMode(PlanetLabSurfaceMode.InfinitePlanar);
        controller.RebuildInfinitePlanarNow();
        controller.ResetPlanarPlayerToSpawn();
        return $"mode={controller.SurfaceMode} chunks="
            + $"{controller.InfiniteActiveChunkCount} "
            + $"pbr={controller.PlanarPbrLibrary?.IsReady == true}";
    }

    [AICallable(
        "Inspect the active PlanetLab infinite planar streaming pool.",
        Category = "PlanetLab.Infinite")]
    public static string InspectInfinitePlanarStreaming()
    {
        ProceduralPlanetLabController controller = FindController();
        PlanetLabInfiniteTerrainStreamer streamer =
            controller != null ? controller.InfiniteTerrainStreamer : null;
        if (controller == null || streamer == null)
            return "streamer-missing";
        return $"playing={Application.isPlaying} mode={controller.SurfaceMode} "
            + $"active={streamer.ActiveChunkCount} pooled={streamer.PooledChunkCount} "
            + $"created={streamer.CreatedChunkCount} center={streamer.CenterCoordinate} "
            + $"lastMs={streamer.LastChunkBuildMilliseconds:0.00} "
            + $"maxMs={streamer.MaximumChunkBuildMilliseconds:0.00}";
    }

    [AICallable(
        "Move the PlanetLab planar player one chunk east for streaming validation.",
        Category = "PlanetLab.Infinite",
        Kind = ToolKind.Write)]
    public static string MoveInfinitePlayerForValidation()
    {
        ProceduralPlanetLabController controller = FindController();
        if (controller == null)
            return "controller-missing";
        Transform player = controller.transform.Find(
            "PlanarExperiment/PlanarPlayer");
        if (player == null)
            return "player-missing";
        CharacterController character = player.GetComponent<CharacterController>();
        bool wasEnabled = character != null && character.enabled;
        if (character != null)
            character.enabled = false;
        player.position += Vector3.right
            * (PlanetLabPlanarSettings.InfiniteChunkSize + 8f);
        if (character != null)
            character.enabled = wasEnabled;
        return $"player={player.position}";
    }

    [AICallable(
        "Render a safe offscreen preview of the PlanetLab infinite planar experiment.",
        Category = "PlanetLab.Infinite",
        Kind = ToolKind.Write)]
    public static string CaptureInfinitePlanarPreview()
    {
        Scene previousActive = SceneManager.GetActiveScene();
        Scene labScene = SceneManager.GetSceneByPath(ScenePath);
        bool loadedForCapture = !labScene.IsValid() || !labScene.isLoaded;
        if (loadedForCapture)
        {
            labScene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive);
        }

        PlanetLabSurfaceMode previousMode = PlanetLabSurfaceMode.Globe;
        try
        {
            SceneManager.SetActiveScene(labScene);
            ProceduralPlanetLabController controller = labScene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    ProceduralPlanetLabController>(true))
                .FirstOrDefault();
            if (controller == null)
                return "controller-missing";

            previousMode = controller.SurfaceMode;
            controller.SetSurfaceMode(PlanetLabSurfaceMode.InfinitePlanar);
            controller.RebuildInfinitePlanarNow();
            string path = Path.GetFullPath(
                "Temp/CodexPlanetLab/InfinitePlanarPreview.png");
            controller.CapturePreview(path, 1280, 720);
            return path;
        }
        finally
        {
            ProceduralPlanetLabController controller = labScene.IsValid()
                && labScene.isLoaded
                ? labScene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        ProceduralPlanetLabController>(true))
                    .FirstOrDefault()
                : null;
            if (controller != null)
                controller.SetSurfaceMode(previousMode);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (loadedForCapture && labScene.IsValid() && labScene.isLoaded)
                EditorSceneManager.CloseScene(labScene, true);
        }
    }

    [AICallable(
        "Render a safe offscreen PlanetLab planar PBR preview without replacing the user's active scene.",
        Category = "PlanetLab.PBR",
        Kind = ToolKind.Write)]
    public static string CapturePlanarPbrPreview()
    {
        Scene previousActive = SceneManager.GetActiveScene();
        Scene labScene = SceneManager.GetSceneByPath(ScenePath);
        bool loadedForCapture = !labScene.IsValid() || !labScene.isLoaded;
        if (loadedForCapture)
        {
            labScene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Additive);
        }

        PlanetLabSurfaceMode previousMode = PlanetLabSurfaceMode.Globe;
        try
        {
            SceneManager.SetActiveScene(labScene);
            ProceduralPlanetLabController controller = labScene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<
                    ProceduralPlanetLabController>(true))
                .FirstOrDefault();
            if (controller == null)
                return "controller-missing";

            previousMode = controller.SurfaceMode;
            controller.SetSurfaceMode(PlanetLabSurfaceMode.Planar);
            controller.RebuildPlanarNow();
            string path = Path.GetFullPath(
                "Temp/CodexPlanetLab/PbrPlanarPreview.png");
            controller.CapturePreview(path, 1280, 720);
            Camera camera = controller.PreviewCamera;
            string closePath = Path.GetFullPath(
                "Temp/CodexPlanetLab/PbrPlanarCloseup.png");
            if (camera != null)
            {
                Vector3 focus = controller.PlanarSpawnPosition;
                camera.transform.position =
                    focus + new Vector3(18f, 22f, -28f);
                camera.transform.rotation = Quaternion.LookRotation(
                    focus - camera.transform.position,
                    Vector3.up);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1200f;
                controller.CapturePreview(closePath, 1280, 720);
            }
            return path + "|" + closePath;
        }
        finally
        {
            ProceduralPlanetLabController controller = labScene.IsValid()
                && labScene.isLoaded
                ? labScene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<
                        ProceduralPlanetLabController>(true))
                    .FirstOrDefault()
                : null;
            if (controller != null)
                controller.SetSurfaceMode(previousMode);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (loadedForCapture && labScene.IsValid() && labScene.isLoaded)
                EditorSceneManager.CloseScene(labScene, true);
        }
    }

    public static void EnsureLaboratoryAssets()
    {
        EnsureFolders();
        EnsureExamplePresets();
        PlanetLabTerrainPbrLibraryBuilder.EnsureLibrary();
        PlanetLabSkyboxLibraryBuilder.EnsureLibrary();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            CreateOrReplaceSceneAsset();
        RemoveSceneFromBuildSettings();
        AssetDatabase.SaveAssets();
    }

    public static bool OpenLaboratoryScene(bool promptToSaveCurrentScene = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "PlanetLab scene switching is disabled while entering or running Play Mode.");
            return false;
        }
        EnsureLaboratoryAssets();
        Scene active = SceneManager.GetActiveScene();
        if (active.path == ScenePath)
            return true;
        if (promptToSaveCurrentScene
            && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return false;
        }
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        return true;
    }

    public static ProceduralPlanetPreset GetExamplePreset(
        ProceduralPlanetLabTemplate template)
    {
        EnsureLaboratoryAssets();
        return AssetDatabase.LoadAssetAtPath<ProceduralPlanetPreset>(
            GetPresetPath(template));
    }

    public static ProceduralPlanetLabController FindController()
    {
        return UnityEngine.Object.FindObjectOfType<ProceduralPlanetLabController>();
    }

    static void EnsureFolders()
    {
        CreateFolder("Assets", "PlanetLab");
        CreateFolder("Assets/PlanetLab", "Presets");
        CreateFolder("Assets", "Screenshots");
        CreateFolder("Assets/Screenshots", "PlanetLab");
        CreateFolder("Assets", "Scenes");
    }

    static void CreateFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, name);
    }

    static void EnsureExamplePresets()
    {
        foreach (ProceduralPlanetLabTemplate template
                 in Enum.GetValues(typeof(ProceduralPlanetLabTemplate)))
        {
            string path = GetPresetPath(template);
            if (AssetDatabase.LoadAssetAtPath<ProceduralPlanetPreset>(path) != null)
                continue;

            ProceduralPlanetPreset preset =
                ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
            preset.ApplyTemplate(template);
            AssetDatabase.CreateAsset(preset, path);
        }
    }

    static string GetPresetPath(ProceduralPlanetLabTemplate template)
    {
        switch (template)
        {
            case ProceduralPlanetLabTemplate.CrimsonOcean:
                return PresetFolder + "/CrimsonOcean.asset";
            case ProceduralPlanetLabTemplate.Desert:
                return PresetFolder + "/Desert.asset";
            case ProceduralPlanetLabTemplate.Frozen:
                return PresetFolder + "/Frozen.asset";
            case ProceduralPlanetLabTemplate.Crystal:
                return PresetFolder + "/Crystal.asset";
            default:
                return DefaultPresetPath;
        }
    }

    static void CreateOrReplaceSceneAsset()
    {
        Scene previousActive = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool createdTemporaryScene = !scene.IsValid() || !scene.isLoaded;
        if (createdTemporaryScene)
        {
            scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
        }
        else
        {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
                UnityEngine.Object.DestroyImmediate(rootObject);
        }

        SceneManager.SetActiveScene(scene);

        try
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.18f, 0.22f);

            var root = new GameObject("PlanetLabRoot");
            SceneManager.MoveGameObjectToScene(root, scene);
            ProceduralPlanetLabController controller =
                root.AddComponent<ProceduralPlanetLabController>();

            var planet = new GameObject("Planet");
            planet.transform.SetParent(root.transform, false);
            MeshFilter terrainFilter = planet.AddComponent<MeshFilter>();
            MeshRenderer terrainRenderer = planet.AddComponent<MeshRenderer>();
            terrainRenderer.shadowCastingMode = ShadowCastingMode.On;
            terrainRenderer.receiveShadows = true;

            var ocean = new GameObject("Ocean");
            ocean.transform.SetParent(planet.transform, false);
            MeshFilter oceanFilter = ocean.AddComponent<MeshFilter>();
            MeshRenderer oceanRenderer = ocean.AddComponent<MeshRenderer>();
            oceanRenderer.shadowCastingMode = ShadowCastingMode.Off;
            oceanRenderer.receiveShadows = false;

            var planarRoot = new GameObject("PlanarExperiment");
            planarRoot.transform.SetParent(root.transform, false);

            var planarTerrain = new GameObject("Terrain");
            planarTerrain.transform.SetParent(planarRoot.transform, false);
            MeshFilter planarTerrainFilter =
                planarTerrain.AddComponent<MeshFilter>();
            MeshRenderer planarTerrainRenderer =
                planarTerrain.AddComponent<MeshRenderer>();
            MeshCollider planarTerrainCollider =
                planarTerrain.AddComponent<MeshCollider>();
            planarTerrainRenderer.shadowCastingMode = ShadowCastingMode.On;
            planarTerrainRenderer.receiveShadows = true;

            var planarOcean = new GameObject("Ocean");
            planarOcean.transform.SetParent(planarRoot.transform, false);
            MeshFilter planarOceanFilter =
                planarOcean.AddComponent<MeshFilter>();
            MeshRenderer planarOceanRenderer =
                planarOcean.AddComponent<MeshRenderer>();
            planarOceanRenderer.shadowCastingMode = ShadowCastingMode.Off;
            planarOceanRenderer.receiveShadows = false;

            var infiniteTerrain = new GameObject("InfiniteTerrain");
            infiniteTerrain.transform.SetParent(planarRoot.transform, false);
            PlanetLabInfiniteTerrainStreamer infiniteTerrainStreamer =
                infiniteTerrain.AddComponent<PlanetLabInfiniteTerrainStreamer>();

            var player = new GameObject("PlanarPlayer");
            player.transform.SetParent(planarRoot.transform, false);
            CharacterController character = player.AddComponent<CharacterController>();
            character.height = 1.8f;
            character.radius = 0.35f;
            character.center = new Vector3(0f, 0.9f, 0f);
            character.stepOffset = 0.35f;
            character.slopeLimit = 55f;
            PlanetLabFirstPersonController playerController =
                player.AddComponent<PlanetLabFirstPersonController>();

            var playerCameraObject = new GameObject("FirstPersonCamera");
            playerCameraObject.transform.SetParent(player.transform, false);
            playerCameraObject.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            playerCameraObject.tag = "MainCamera";
            Camera playerCamera = playerCameraObject.AddComponent<Camera>();
            playerCamera.fieldOfView = 68f;
            playerCamera.nearClipPlane = 0.05f;
            playerCamera.farClipPlane = 1500f;
            playerCamera.allowHDR = true;
            playerCamera.allowMSAA = true;
            playerCamera.enabled = false;
            playerController.Configure(playerCameraObject.transform, Vector3.zero);

            var hudObject = new GameObject(
                "PlanarHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            hudObject.transform.SetParent(planarRoot.transform, false);
            Canvas hudCanvas = hudObject.GetComponent<Canvas>();
            hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler hudScaler = hudObject.GetComponent<CanvasScaler>();
            hudScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            hudScaler.referenceResolution = new Vector2(1920f, 1080f);

            var helpObject = new GameObject(
                "HelpText",
                typeof(RectTransform),
                typeof(Text));
            helpObject.transform.SetParent(hudObject.transform, false);
            RectTransform helpRect = helpObject.GetComponent<RectTransform>();
            helpRect.anchorMin = new Vector2(0f, 1f);
            helpRect.anchorMax = new Vector2(0f, 1f);
            helpRect.pivot = new Vector2(0f, 1f);
            helpRect.anchoredPosition = new Vector2(22f, -22f);
            helpRect.sizeDelta = new Vector2(720f, 105f);
            Text helpText = helpObject.GetComponent<Text>();
            helpText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            helpText.fontSize = 22;
            helpText.color = Color.white;
            helpText.alignment = TextAnchor.UpperLeft;
            helpText.text =
                "PLANETLAB / PLANAR WORLD\n"
                + "WASD Move  |  Mouse Look  |  Space Jump  |  R Reset  |  Esc Cursor\n"
                + "Fixed 512m or streaming infinite terrain; water is visual only.";
            Shadow helpShadow = helpObject.AddComponent<Shadow>();
            helpShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            helpShadow.effectDistance = new Vector2(2f, -2f);

            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = 40f;
            camera.allowHDR = true;
            camera.allowMSAA = true;

            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(root.transform, false);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;

            var fillObject = new GameObject("FillLight");
            fillObject.transform.SetParent(root.transform, false);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.shadows = LightShadows.None;

            controller.ConfigureSceneReferences(
                planet.transform,
                terrainFilter,
                terrainRenderer,
                oceanFilter,
                oceanRenderer,
                camera,
                sun,
                fill);
            controller.ConfigurePlanarReferences(
                planarRoot,
                planarTerrainFilter,
                planarTerrainRenderer,
                planarTerrainCollider,
                planarOceanFilter,
                planarOceanRenderer,
                player,
                playerController,
                playerCamera,
                infiniteTerrain,
                infiniteTerrainStreamer);
            controller.ConfigurePbrLibrary(
                AssetDatabase.LoadAssetAtPath<PlanetLabTerrainPbrLibrary>(
                    PlanetLabTerrainPbrLibraryBuilder.LibraryPath));
            controller.ConfigureSkyboxLibrary(
                AssetDatabase.LoadAssetAtPath<PlanetLabSkyboxLibrary>(
                    PlanetLabSkyboxLibraryBuilder.LibraryPath));
            ProceduralPlanetPreset defaultPreset =
                AssetDatabase.LoadAssetAtPath<ProceduralPlanetPreset>(
                    DefaultPresetPath);
            controller.ApplyPreset(defaultPreset, true);
            controller.RebuildNow(defaultPreset.previewResolution);
            controller.SetSurfaceMode(PlanetLabSurfaceMode.InfinitePlanar);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            if (createdTemporaryScene && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
        }

        RemoveSceneFromBuildSettings();
    }

    static void RemoveSceneFromBuildSettings()
    {
        EditorBuildSettingsScene[] filtered = EditorBuildSettings.scenes
            .Where(entry => entry.path != ScenePath)
            .ToArray();
        if (filtered.Length != EditorBuildSettings.scenes.Length)
            EditorBuildSettings.scenes = filtered;
    }
}
