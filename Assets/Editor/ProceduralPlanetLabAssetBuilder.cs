using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class ProceduralPlanetLabAssetBuilder
{
    public const string ScenePath = "Assets/Scenes/PlanetLab.unity";
    public const string PresetFolder = "Assets/PlanetLab/Presets";
    public const string ScreenshotFolder = "Assets/Screenshots/PlanetLab";
    public const string DefaultPresetPath = PresetFolder + "/TemperateOcean.asset";

    [MenuItem("Tools/Voxel Planet/Rebuild Procedural Planet Lab Scene")]
    public static void RebuildLaboratoryAssets()
    {
        EnsureFolders();
        EnsureExamplePresets();
        CreateOrReplaceSceneAsset();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Procedural Planet Lab rebuilt: " + ScenePath);
    }

    public static void EnsureLaboratoryAssets()
    {
        EnsureFolders();
        EnsureExamplePresets();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            CreateOrReplaceSceneAsset();
        RemoveSceneFromBuildSettings();
        AssetDatabase.SaveAssets();
    }

    public static bool OpenLaboratoryScene(bool promptToSaveCurrentScene = true)
    {
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
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive);
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
            ProceduralPlanetPreset defaultPreset =
                AssetDatabase.LoadAssetAtPath<ProceduralPlanetPreset>(
                    DefaultPresetPath);
            controller.ApplyPreset(defaultPreset, true);
            controller.RebuildNow(defaultPreset.previewResolution);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded)
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
