using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlantLabAssetBuilder
{
    const string Root = "Assets/Plants/Procedural";
    const string SpeciesFolder = Root + "/Species";
    const string MaterialFolder = Root + "/Materials";
    const string ScenePath = "Assets/Scenes/plant.unity";

    [MenuItem("Tools/Plant Lab/Rebuild Experiment Assets")]
    public static void RebuildExperimentAssets()
    {
        EnsureFolders();
        Material bark = CreateOrUpdateMaterial(
            MaterialFolder + "/ProceduralBark.mat",
            "Plant/ProceduralBark");
        Material foliage = CreateOrUpdateMaterial(
            MaterialFolder + "/ProceduralFoliage.mat",
            "Plant/ProceduralFoliage");
        Material planet = CreateOrUpdateMaterial(
            MaterialFolder + "/PlantLabPlanet.mat",
            "Standard");
        planet.color = new Color(0.025f, 0.045f, 0.065f);
        planet.SetFloat("_Metallic", 0f);
        planet.SetFloat("_Glossiness", 0.08f);
        EditorUtility.SetDirty(planet);

        ProceduralPlantSpecies broadleaf = CreateOrUpdateSpecies(
            SpeciesFolder + "/AlienBroadleaf.asset",
            "alien-broadleaf",
            ProceduralPlantFamily.AlienBroadleaf,
            bark,
            foliage);
        ConfigureBroadleaf(broadleaf);

        ProceduralPlantSpecies canopy = CreateOrUpdateSpecies(
            SpeciesFolder + "/CanopyTree.asset",
            "canopy-tree",
            ProceduralPlantFamily.CanopyTree,
            bark,
            foliage);
        ConfigureCanopy(canopy);

        ProceduralPlantSpecies coral = CreateOrUpdateSpecies(
            SpeciesFolder + "/CoralSucculent.asset",
            "coral-succulent",
            ProceduralPlantFamily.CoralSucculent,
            bark,
            foliage);
        ConfigureCoral(coral);

        CreateScene(new[] { broadleaf, canopy, coral }, planet);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Plant Lab: rebuilt species, materials, and Assets/Scenes/plant.unity.");
    }

    static void EnsureFolders()
    {
        CreateFolder("Assets", "Plants");
        CreateFolder("Assets/Plants", "Procedural");
        CreateFolder(Root, "Species");
        CreateFolder(Root, "Materials");
        CreateFolder("Assets/Shaders", "Plants");
    }

    static void CreateFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, name);
    }

    static Material CreateOrUpdateMaterial(string path, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            throw new System.InvalidOperationException("Missing shader: " + shaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static ProceduralPlantSpecies CreateOrUpdateSpecies(
        string path,
        string id,
        ProceduralPlantFamily family,
        Material bark,
        Material foliage)
    {
        ProceduralPlantSpecies species = AssetDatabase.LoadAssetAtPath<ProceduralPlantSpecies>(path);
        if (species == null)
        {
            species = ScriptableObject.CreateInstance<ProceduralPlantSpecies>();
            AssetDatabase.CreateAsset(species, path);
        }
        species.speciesId = id;
        species.schemaVersion = ProceduralPlantSpecies.CurrentSchema;
        species.family = family;
        species.barkMaterial = bark;
        species.foliageMaterial = foliage;
        species.variantPoolSize = 12;
        if (species.parameters == null)
            species.parameters = new PlantGenerationParameters();
        EditorUtility.SetDirty(species);
        return species;
    }

    static void ConfigureBroadleaf(ProceduralPlantSpecies species)
    {
        species.baseSeed = 1301;
        species.parameters = new PlantGenerationParameters
        {
            age = 1f,
            trunkLength = 4.2f,
            trunkRadius = 0.42f,
            taper = 0.72f,
            curvature = 0.26f,
            trunkSegments = 8,
            branchDepth = 3,
            budsPerBranch = 3,
            branchAngle = 48f,
            lengthDecay = 0.64f,
            radiusDecay = 0.58f,
            phototropism = 0.52f,
            apicalDominance = 0.4f,
            crownWidth = 1.35f,
            crownHeight = 0.78f,
            leafMode = PlantLeafMode.SolidLeaf,
            leafDensity = 1.65f,
            leafSize = 0.82f,
            leafUpwardBias = 0.42f
        };
        species.naturalBark = new Color(0.26f, 0.17f, 0.08f);
        species.naturalLeaf = new Color(0.22f, 0.5f, 0.12f);
        species.naturalAccent = new Color(0.54f, 0.72f, 0.18f);
        species.alienBark = new Color(0.04f, 0.28f, 0.32f);
        species.alienLeaf = new Color(0.03f, 0.83f, 0.78f);
        species.alienAccent = new Color(0.76f, 0.18f, 0.95f);
        EditorUtility.SetDirty(species);
    }

    static void ConfigureCanopy(ProceduralPlantSpecies species)
    {
        species.baseSeed = 2609;
        species.parameters = new PlantGenerationParameters
        {
            age = 1f,
            trunkLength = 7.4f,
            trunkRadius = 0.46f,
            taper = 0.76f,
            curvature = 0.13f,
            trunkSegments = 10,
            branchDepth = 4,
            budsPerBranch = 4,
            branchAngle = 39f,
            lengthDecay = 0.62f,
            radiusDecay = 0.52f,
            phototropism = 0.64f,
            apicalDominance = 0.72f,
            crownWidth = 1.05f,
            crownHeight = 1.2f,
            leafMode = PlantLeafMode.CardLeaf,
            leafDensity = 1.35f,
            leafSize = 0.48f,
            leafUpwardBias = 0.28f
        };
        species.naturalBark = new Color(0.2f, 0.11f, 0.05f);
        species.naturalLeaf = new Color(0.1f, 0.42f, 0.15f);
        species.naturalAccent = new Color(0.38f, 0.67f, 0.2f);
        species.alienBark = new Color(0.12f, 0.13f, 0.31f);
        species.alienLeaf = new Color(0.12f, 0.66f, 0.9f);
        species.alienAccent = new Color(0.85f, 0.34f, 0.86f);
        EditorUtility.SetDirty(species);
    }

    static void ConfigureCoral(ProceduralPlantSpecies species)
    {
        species.baseSeed = 3917;
        species.parameters = new PlantGenerationParameters
        {
            age = 1f,
            trunkLength = 4.8f,
            trunkRadius = 0.58f,
            taper = 0.84f,
            curvature = 0.31f,
            trunkSegments = 8,
            branchDepth = 3,
            budsPerBranch = 2,
            branchAngle = 28f,
            lengthDecay = 0.72f,
            radiusDecay = 0.68f,
            phototropism = 0.84f,
            apicalDominance = 0.56f,
            crownWidth = 0.72f,
            crownHeight = 1.3f,
            leafMode = PlantLeafMode.None,
            leafDensity = 0f,
            leafSize = 0.35f,
            leafUpwardBias = 0.7f
        };
        species.naturalBark = new Color(0.25f, 0.42f, 0.24f);
        species.naturalLeaf = new Color(0.3f, 0.52f, 0.25f);
        species.naturalAccent = new Color(0.72f, 0.6f, 0.24f);
        species.alienBark = new Color(0.08f, 0.5f, 0.52f);
        species.alienLeaf = new Color(0.1f, 0.72f, 0.66f);
        species.alienAccent = new Color(0.96f, 0.26f, 0.52f);
        EditorUtility.SetDirty(species);
    }

    static void CreateScene(ProceduralPlantSpecies[] species, Material planetMaterial)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "plant";

        var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        planet.name = "AnalyticPlanet";
        planet.transform.position = Vector3.zero;
        planet.transform.localScale = Vector3.one * 44f;
        planet.GetComponent<MeshRenderer>().sharedMaterial = planetMaterial;
        SphereCollider planetCollider = planet.GetComponent<SphereCollider>();

        var labRoot = new GameObject("PlantLab");
        AnalyticSphereSurfacePlacementContext context =
            labRoot.AddComponent<AnalyticSphereSurfacePlacementContext>();
        context.Configure(41277, 22f, planetCollider);
        var experimentCenter = new GameObject("NorthExperimentClearing");
        experimentCenter.transform.SetParent(labRoot.transform, false);
        experimentCenter.transform.position = Vector3.up * 22f;
        SerializedObject contextData = new SerializedObject(context);
        contextData.FindProperty("playerSpawn").objectReferenceValue = experimentCenter.transform;
        contextData.ApplyModifiedPropertiesWithoutUndo();
        PlanetSurfaceDecorationSystem decoration =
            labRoot.AddComponent<PlanetSurfaceDecorationSystem>();
        var previewRoot = new GameObject("PlantPreviewArea");
        previewRoot.transform.SetParent(labRoot.transform, false);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.012f, 0.018f, 0.035f);
        camera.fieldOfView = 54f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 250f;
        PlantLabOrbitCamera orbit = cameraObject.AddComponent<PlantLabOrbitCamera>();

        var lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.94f, 0.82f);
        lightObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

        var fillObject = new GameObject("Fill Light");
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.36f;
        fill.color = new Color(0.42f, 0.62f, 1f);
        fillObject.transform.rotation = Quaternion.Euler(18f, 142f, 0f);

        PlantLabController controller = labRoot.AddComponent<PlantLabController>();
        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty speciesProperty = serialized.FindProperty("species");
        speciesProperty.arraySize = species.Length;
        for (int i = 0; i < species.Length; i++)
            speciesProperty.GetArrayElementAtIndex(i).objectReferenceValue = species[i];
        serialized.FindProperty("surfaceContext").objectReferenceValue = context;
        serialized.FindProperty("decorationSystem").objectReferenceValue = decoration;
        serialized.FindProperty("orbitCamera").objectReferenceValue = orbit;
        serialized.FindProperty("previewContainer").objectReferenceValue = previewRoot.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.2f, 0.26f);
        RenderSettings.fog = false;
        EditorSceneManager.SaveScene(scene, ScenePath);

        var buildScenes = new List<EditorBuildSettingsScene>();
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            if (!string.Equals(buildScene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase))
                buildScenes.Add(buildScene);
        EditorBuildSettings.scenes = buildScenes.ToArray();
    }
}
