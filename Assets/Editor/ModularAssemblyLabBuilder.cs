using System;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ModularAssemblyLabBuilder
{
    const string GeneratedRoot = "Assets/ModularAssemblyLab/Generated";
    const string ScenePath = "Assets/Scenes/ModularAssemblyLab.unity";

    [MenuItem("Tools/Modular Assembly/Build Lab")]
    public static void Build()
    {
        EnsureFolder("Assets/ModularAssemblyLab");
        EnsureFolder(GeneratedRoot);
        Material structureMat = CreateMaterial("Structure", new Color(0.18f, 0.25f, 0.29f), 0.72f, 0.28f);
        Material armorMat = CreateMaterial("Armor", new Color(0.08f, 0.16f, 0.20f), 0.88f, 0.48f);
        Material batteryMat = CreateMaterial("Battery", new Color(0.04f, 0.34f, 0.40f), 0.58f, 0.42f);
        Material coreMat = CreateMaterial("Core", new Color(0.06f, 0.22f, 0.28f), 0.82f, 0.52f, new Color(0.05f, 0.8f, 1.2f));

        GameObject cockpit = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/SpacecraftEditor/Art/Models/Cockpit/UniversalCockpit.fbx");
        GameObject largeThruster = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/SpacecraftEditor/Art/Generated/Modules/ThrusterLarge.fbx");
        GameObject smallThruster = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/SpacecraftEditor/Art/Generated/Modules/ThrusterSmall.fbx");
        GameObject kinetic = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/SpacecraftEditor/Art/Generated/Modules/KineticRepeater.fbx");

        GameObject corePrefab = CreateCorePrefab(cockpit, coreMat);
        GameObject structurePrefab = CreateBlockPrefab("StructureBlock", structureMat, Vector3.one, false);
        GameObject armorPrefab = CreateBlockPrefab("ArmorBlock", armorMat, Vector3.one, false);
        GameObject batteryPrefab = CreateBatteryPrefab(batteryMat);
        GameObject mainPrefab = CreateModelPrefab("MainThruster", largeThruster, new Vector3(1f, 1f, 2f), true);
        GameObject rcsPrefab = CreateModelPrefab("RcsThruster", smallThruster, Vector3.one, true);
        GameObject weaponPrefab = CreateModelPrefab("KineticWeapon", kinetic, new Vector3(1f, 1f, 2f), false);

        ShipPartDefinition structurePart = CreatePart("grid_structure", "结构块", structurePrefab, 50f, 0f, false);
        ShipPartDefinition armorPart = CreatePart("grid_armor", "装甲块", armorPrefab, 150f, 0f, false);
        ShipPartDefinition batteryPart = CreatePart("grid_battery", "电池", batteryPrefab, 120f, 0f, false);
        ShipPartDefinition mainPart = CreatePart("grid_main_thruster", "主推进器", mainPrefab, 180f, 6000f, true);
        ShipPartDefinition rcsPart = CreatePart("grid_rcs_thruster", "RCS推进器", rcsPrefab, 60f, 1500f, true);
        ShipPartDefinition weaponPart = CreatePart("grid_kinetic_weapon", "动能炮", weaponPrefab, 220f, 0f, false);

        var definitions = new[]
        {
            CreateGridDefinition("core", "驾驶核心", GridModuleCategory.Core, corePrefab, new Vector3Int(2,2,2), 1000f, 100f, 0f, 1000f, 0f, null),
            CreateGridDefinition("structure", "结构块", GridModuleCategory.Structure, structurePrefab, Vector3Int.one, 50f, 0f, 0f, 100f, 0f, structurePart),
            CreateGridDefinition("armor", "装甲块", GridModuleCategory.Armor, armorPrefab, Vector3Int.one, 150f, 0f, 0f, 300f, 0f, armorPart),
            CreateGridDefinition("main_thruster", "主推进器", GridModuleCategory.MainThruster, mainPrefab, new Vector3Int(1,1,2), 180f, 0f, 20f, 120f, 6000f, mainPart),
            CreateGridDefinition("rcs_thruster", "RCS推进器", GridModuleCategory.RcsThruster, rcsPrefab, Vector3Int.one, 60f, 0f, 6f, 80f, 1500f, rcsPart),
            CreateGridDefinition("kinetic_weapon", "动能炮", GridModuleCategory.KineticWeapon, weaponPrefab, new Vector3Int(1,1,2), 220f, 0f, 25f, 100f, 0f, weaponPart),
            CreateGridDefinition("battery", "电池", GridModuleCategory.Battery, batteryPrefab, Vector3Int.one, 120f, 50f, 0f, 80f, 0f, batteryPart)
        };

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject root = new GameObject("ModularAssemblyLab");
        var bootstrap = root.AddComponent<ModularAssemblyLabBootstrap>();
        var sceneProfile =
            root.AddComponent<UnityPlanet.ModularAssembly.ModularLabSceneProfile>();
        sceneProfile.Configure(
            buildExperience: true,
            combatTest: true,
            planetLabFlightEnvironment: true);

        GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.fieldOfView = 58f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 2000f;

        GameObject key = new GameObject("Directional Light", typeof(Light));
        key.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
        Light keyLight = key.GetComponent<Light>();
        keyLight.type = LightType.Directional;
        keyLight.intensity = 1.35f;
        keyLight.color = new Color(0.78f, 0.88f, 1f);

        GameObject fill = new GameObject("Fill Light", typeof(Light));
        fill.transform.position = new Vector3(-8f, 6f, -4f);
        Light fillLight = fill.GetComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.range = 35f;
        fillLight.intensity = 3.2f;
        fillLight.color = new Color(0.12f, 0.55f, 0.75f);

        GameObject probe = new GameObject("Workshop Reflection Probe", typeof(ReflectionProbe));
        probe.transform.position = Vector3.zero;
        ReflectionProbe reflection = probe.GetComponent<ReflectionProbe>();
        reflection.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        reflection.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
        reflection.size = new Vector3(35f, 20f, 35f);

        bootstrap.ConfigureAssets(definitions, camera);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("ModularAssemblyLab generated successfully.");
    }

    static GameObject CreateCorePrefab(GameObject cockpit, Material material)
    {
        GameObject root = new GameObject("GridCore");
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "CoreBase";
        block.transform.SetParent(root.transform, false);
        block.transform.localScale = Vector3.one * 2f;
        block.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
        if (cockpit != null)
        {
            GameObject visual = PrefabUtility.InstantiatePrefab(cockpit) as GameObject;
            visual.transform.SetParent(root.transform, false);
            FitVisual(visual, new Vector3(1.75f, 1.2f, 1.75f));
            visual.transform.localPosition += new Vector3(0f, 0.35f, 0.2f);
            RemoveColliders(visual);
        }
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = Vector3.one * 2f;
        string path = GeneratedRoot + "/GridCore.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject CreateBlockPrefab(string name, Material material, Vector3 size, bool thruster)
    {
        GameObject root = new GameObject(name);
        if (thruster) root.AddComponent<ThrusterPart>();
        else root.AddComponent<DecorationPart>();
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "IndustrialBlock";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = size * 0.94f;
        visual.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = size;
        string path = GeneratedRoot + "/" + name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject CreateBatteryPrefab(Material material)
    {
        GameObject root = new GameObject("GridBattery");
        root.AddComponent<DecorationPart>();
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "BatteryBody";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = Vector3.one * 0.92f;
        body.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject cell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cell.name = "EnergyCell";
            cell.transform.SetParent(root.transform, false);
            cell.transform.localPosition = new Vector3(side * 0.28f, 0f, 0f);
            cell.transform.localScale = new Vector3(0.16f, 0.44f, 0.16f);
            cell.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cell.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cell.GetComponent<Collider>());
        }
        root.AddComponent<BoxCollider>().size = Vector3.one;
        string path = GeneratedRoot + "/GridBattery.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject CreateModelPrefab(string name, GameObject model, Vector3 size, bool thruster)
    {
        GameObject root = new GameObject(name);
        if (thruster) root.AddComponent<ThrusterPart>();
        else root.AddComponent<DecorationPart>();
        if (model != null)
        {
            GameObject visual = PrefabUtility.InstantiatePrefab(model) as GameObject;
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            FitVisual(visual, size * 0.88f);
            RemoveColliders(visual);
        }
        root.AddComponent<BoxCollider>().size = size;
        string path = GeneratedRoot + "/" + name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static ShipPartDefinition CreatePart(string id, string label, GameObject prefab, float mass, float thrust, bool thruster)
    {
        string path = GeneratedRoot + "/" + id + ".asset";
        ShipPartDefinition definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<ShipPartDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }
        if (thruster)
            definition.Configure(id, label, prefab, null, mass, thrust, new Color(0.2f, 0.8f, 1f));
        else
            definition.ConfigureGeneral(
                id, label, prefab, null, SpacecraftPartCategory.Decoration, mass,
                SpacecraftPartScaleMode.Fixed, 1f, string.Empty);
        EditorUtility.SetDirty(definition);
        return definition;
    }

    static GridModuleDefinition CreateGridDefinition(
        string id, string label, GridModuleCategory category, GameObject prefab,
        Vector3Int footprint, float mass, float capacity, float cost, float integrity,
        float thrust, ShipPartDefinition part)
    {
        string path = GeneratedRoot + "/module_" + id + ".asset";
        GridModuleDefinition definition = AssetDatabase.LoadAssetAtPath<GridModuleDefinition>(path);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<GridModuleDefinition>();
            AssetDatabase.CreateAsset(definition, path);
        }
        definition.Configure(id, label, category, prefab, footprint, mass, capacity, cost, integrity, thrust, part);
        EditorUtility.SetDirty(definition);
        return definition;
    }

    static Material CreateMaterial(string name, Color color, float metallic, float smoothness, Color? emission = null)
    {
        string path = GeneratedRoot + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static void FitVisual(GameObject visual, Vector3 targetSize)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        float scale = Mathf.Min(
            targetSize.x / Mathf.Max(0.001f, bounds.size.x),
            Mathf.Min(
                targetSize.y / Mathf.Max(0.001f, bounds.size.y),
                targetSize.z / Mathf.Max(0.001f, bounds.size.z)));
        visual.transform.localScale *= scale;
        bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        visual.transform.position += visual.transform.parent.position - bounds.center;
    }

    static void RemoveColliders(GameObject target)
    {
        foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(collider);
    }

    static void AddSceneToBuildSettings(string path)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Exists(item => item.path == path))
            scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string name = path.Substring(path.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
