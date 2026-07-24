using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
sealed class NmsUnityPublishAction
{
    public string key;
    public string clipName;
    public string assetPath;
    public int frames;
    public bool loop = true;
}

[Serializable]
sealed class NmsUnityPublishVector3
{
    public float x;
    public float y;
    public float z;
    public Vector3 Value => new Vector3(x, y, z);
}

[Serializable]
sealed class NmsUnityPublishManifest
{
    public int pipelineVersion;
    public string familyId;
    public string skeletonHash;
    public string validationHash;
    public string sourceKind;
    public string sourceModelAsset;
    public string sourcePrefabAsset;
    public string familyManifestAsset;
    public string skeletonManifestAsset;
    public string materialManifestAsset;
    public string locomotionType;
    public string surfaceMode = "Ground";
    public bool supportsRun = true;
    public string[] variantIds = Array.Empty<string>();
    public int legCount;
    public bool allowRareModules;
    public string[] excludedModulePrefixes = Array.Empty<string>();
    public int selectionWeight = 1;
    public float walkSpeed = 1.35f;
    public float surfaceRootOffset = -1f;
    public float hoverClearance;
    public float importScale = 1f;
    public NmsUnityPublishVector3 importEuler = new NmsUnityPublishVector3();
    public NmsUnityPublishVector3 localForwardAxis;
    public NmsUnityPublishAction[] actions = Array.Empty<NmsUnityPublishAction>();
    public NmsCreatureLegChain[] loadBearingChains = Array.Empty<NmsCreatureLegChain>();
}

[Serializable]
sealed class NmsUnityMaterialManifest
{
    public int pipelineVersion;
    public string familyId;
    public string skeletonHash;
    public NmsUnityMaterialRecord[] materials = Array.Empty<NmsUnityMaterialRecord>();
    public NmsUnityRendererMaterialRecord[] renderers =
        Array.Empty<NmsUnityRendererMaterialRecord>();
}

[Serializable]
sealed class NmsUnityMaterialRecord
{
    public string materialId;
    public string sourceMaterialName;
    public string materialPreset;
    public string sourceMxmlPath;
    public string sourceMxmlSha256;
    public string mainTexture;
    public string mainTextureSourcePath;
    public string mainTextureSourceSha256;
    public string normalTexture;
    public string normalTextureSourcePath;
    public string normalTextureSourceSha256;
    public string maskTexture;
    public string maskTextureSourcePath;
    public string maskTextureSourceSha256;
    public string emissionTexture;
    public string emissionTextureSourcePath;
    public string emissionTextureSourceSha256;
    public string transparencyMode;
    public string maskChannelLayout;
    public float metallic = -1f;
    public float roughness = -1f;
    public float smoothness = -1f;
    public float glow = -1f;
    public float paletteStrength = -1f;
    public string[] warnings = Array.Empty<string>();
}

[Serializable]
sealed class NmsUnityRendererMaterialRecord
{
    public string rendererName;
    public string[] modules = Array.Empty<string>();
    public NmsUnityRendererMaterialSlot[] slots = Array.Empty<NmsUnityRendererMaterialSlot>();
}

[Serializable]
sealed class NmsUnityRendererMaterialSlot
{
    public int slot;
    public string materialId;
}

[InitializeOnLoad]
static class NmsCreatureFamilyAssetBootstrap
{
    const string SessionKey = "NmsCreatureFamilyAssetBootstrap.v11";

    static NmsCreatureFamilyAssetBootstrap()
    {
        EditorApplication.delayCall += BuildWhenReady;
    }

    static void BuildWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += BuildWhenReady;
            return;
        }
        if (SessionState.GetBool(SessionKey, false))
            return;

        SessionState.SetBool(SessionKey, true);
        try
        {
            if (NmsCreatureFamilyAssetBuilder.NeedsRebuild())
                NmsCreatureFamilyAssetBuilder.BuildAllPublishedFamilies();
            else
                NmsCreatureFamilyAssetBuilder.ConfigureBioSceneFromCatalog();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}

sealed class NmsCreatureFamilyAssetPostprocessor : AssetPostprocessor
{
    static bool buildQueued;

    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (buildQueued
            || !importedAssets.Any(path =>
                path.EndsWith(".publish.json", StringComparison.OrdinalIgnoreCase)))
            return;
        buildQueued = true;
        EditorApplication.delayCall += BuildPublishedFamilies;
    }

    static void BuildPublishedFamilies()
    {
        buildQueued = false;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            buildQueued = true;
            EditorApplication.delayCall += BuildPublishedFamilies;
            return;
        }

        try
        {
            NmsCreatureFamilyAssetBuilder.BuildAllPublishedFamilies();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}

public static class NmsCreatureFamilyAssetBuilder
{
    const string GeneratedRoot = "Assets/Creatures/Generated/NMS";
    const string CatalogPath = GeneratedRoot + "/NmsCreatureFamilyCatalog.asset";
    static bool isBuilding;

    public static void BuildAllPublishedFamilies()
    {
        if (isBuilding)
            return;
        isBuilding = true;
        try
        {
            BuildAllPublishedFamiliesCore();
        }
        finally
        {
            isBuilding = false;
        }
    }

    static void BuildAllPublishedFamiliesCore()
    {
        if (!Directory.Exists(GeneratedRoot))
            return;

        string[] publishFiles = Directory.GetFiles(
            GeneratedRoot,
            "*.publish.json",
            SearchOption.AllDirectories);
        Array.Sort(publishFiles, StringComparer.Ordinal);
        var definitions = new List<NmsCreatureFamilyDefinition>(publishFiles.Length);
        for (int i = 0; i < publishFiles.Length; i++)
        {
            NmsCreatureFamilyDefinition definition = BuildFamily(ToAssetPath(publishFiles[i]));
            if (definition != null)
                definitions.Add(definition);
        }

        definitions.Sort((left, right) => string.CompareOrdinal(left.FamilyId, right.FamilyId));
        NmsCreatureFamilyCatalog catalog =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyCatalog>(CatalogPath);
        if (catalog == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
            catalog = ScriptableObject.CreateInstance<NmsCreatureFamilyCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.ConfigureImported(definitions.ToArray());
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        ConfigureBioScene(catalog);
        Debug.Log($"NMS creature catalog rebuilt with {definitions.Count} families.");
    }

    public static bool NeedsRebuild()
    {
        string[] publishFiles = Directory.Exists(GeneratedRoot)
            ? Directory.GetFiles(
                GeneratedRoot,
                "*.publish.json",
                SearchOption.AllDirectories)
            : Array.Empty<string>();
        NmsCreatureFamilyCatalog catalog =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyCatalog>(CatalogPath);
        if (catalog == null || catalog.Families.Count != publishFiles.Length)
            return true;
        for (int i = 0; i < catalog.Families.Count; i++)
        {
            NmsCreatureFamilyDefinition family = catalog.Families[i];
            if (family == null || family.MaterialDefinitions.Count == 0)
                return true;
            for (int materialIndex = 0;
                materialIndex < family.MaterialDefinitions.Count;
                materialIndex++)
            {
                NmsCreatureMaterialDefinition material =
                    family.MaterialDefinitions[materialIndex];
                if (material == null || string.IsNullOrEmpty(material.materialPreset))
                    return true;
            }
        }

        DateTime catalogTime = File.Exists(CatalogPath)
            ? File.GetLastWriteTimeUtc(CatalogPath)
            : DateTime.MinValue;
        for (int i = 0; i < publishFiles.Length; i++)
        {
            if (File.GetLastWriteTimeUtc(publishFiles[i]) > catalogTime)
                return true;
        }
        return false;
    }

    public static void ConfigureBioSceneFromCatalog()
    {
        NmsCreatureFamilyCatalog catalog =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyCatalog>(CatalogPath);
        if (catalog != null)
            ConfigureBioScene(catalog);
    }

    static void ConfigureBioScene(NmsCreatureFamilyCatalog catalog)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        const string scenePath = "Assets/Scenes/bio.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedTemporarily = !scene.IsValid() || !scene.isLoaded;
        if (openedTemporarily)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            GameObject creatureSystem = FindNamedObject(roots, "CreatureSystem");
            if (creatureSystem == null)
                throw new InvalidOperationException(
                    "bio scene is missing the CreatureSystem hierarchy object.");

            SphericalGravitySource gravitySource =
                FindComponentInScene<SphericalGravitySource>(roots);
            BioCreatureFollowCamera followCamera =
                FindComponentInScene<BioCreatureFollowCamera>(roots);
            if (gravitySource == null || followCamera == null)
                throw new InvalidOperationException(
                    "bio scene is missing its spherical gravity source or follow camera.");

            DisableLegacyCreatureSystems(roots);
            NmsRandomCreatureGenerator generator =
                creatureSystem.GetComponent<NmsRandomCreatureGenerator>();
            if (generator == null)
                generator = creatureSystem.AddComponent<NmsRandomCreatureGenerator>();
            generator.enabled = true;
            generator.ConfigureImported(catalog, gravitySource, followCamera, 1 << 0);
            EditorUtility.SetDirty(generator);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(
                $"Configured bio hierarchy for {catalog.Families.Count} NMS creature families.");
        }
        finally
        {
            if (openedTemporarily && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void DisableLegacyCreatureSystems(GameObject[] roots)
    {
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            MonoBehaviour[] behaviours =
                roots[rootIndex].GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                string typeName = behaviour.GetType().Name;
                if (typeName == "BioCreatureTestController"
                    || typeName == "RuntimeCreatureTorsoEditor"
                    || typeName == "StableAntelopeSpeciesRandomizer")
                {
                    behaviour.enabled = false;
                    EditorUtility.SetDirty(behaviour);
                }
            }

            Transform[] transforms =
                roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject value = transforms[i].gameObject;
                if (value.name.StartsWith(
                    "FixedAntelopeV2_SphericalTest",
                    StringComparison.Ordinal)
                    && value.activeSelf)
                {
                    value.SetActive(false);
                    EditorUtility.SetDirty(value);
                }
            }
        }
    }

    static GameObject FindNamedObject(GameObject[] roots, string objectName)
    {
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
        {
            Transform[] values =
                roots[rootIndex].GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].name == objectName)
                    return values[i].gameObject;
            }
        }
        return null;
    }

    static T FindComponentInScene<T>(GameObject[] roots) where T : Component
    {
        for (int i = 0; i < roots.Length; i++)
        {
            T value = roots[i].GetComponentInChildren<T>(true);
            if (value != null)
                return value;
        }
        return null;
    }

    static NmsCreatureFamilyDefinition BuildFamily(string publishAssetPath)
    {
        string json = File.ReadAllText(Path.GetFullPath(publishAssetPath));
        NmsUnityPublishManifest publish = JsonUtility.FromJson<NmsUnityPublishManifest>(json);
        ValidatePublish(publish, publishAssetPath);

        TextAsset manifestAsset =
            AssetDatabase.LoadAssetAtPath<TextAsset>(publish.familyManifestAsset);
        if (manifestAsset == null)
            throw new InvalidOperationException(
                $"{publish.familyId}: family manifest is not imported.");
        NmsCreatureFamilyManifestData family =
            JsonConvert.DeserializeObject<NmsCreatureFamilyManifestData>(
                manifestAsset.text);
        if (family == null
            || !string.Equals(family.familyId, publish.familyId, StringComparison.Ordinal)
            || !string.Equals(family.skeletonHash, publish.skeletonHash, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{publish.familyId}: manifest identity or skeleton hash mismatch.");

        ConfigureModelImporter(publish);
        GameObject source = LoadSource(publish);
        RuntimeAnimatorController controller = BuildController(publish);
        string familyDirectory = Path.GetDirectoryName(publishAssetPath).Replace('\\', '/');
        string prefabPath = familyDirectory + "/" + publish.familyId + "Runtime.prefab";
        string definitionPath = familyDirectory + "/" + publish.familyId + "Family.asset";

        var root = new GameObject(publish.familyId + "Runtime");
        try
        {
            Transform importSpace = new GameObject("ImportSpace").transform;
            importSpace.SetParent(root.transform, false);
            importSpace.localEulerAngles =
                publish.importEuler != null ? publish.importEuler.Value : Vector3.zero;
            importSpace.localScale =
                Vector3.one * Mathf.Max(0.0001f, publish.importScale);

            GameObject instance =
                (GameObject)PrefabUtility.InstantiatePrefab(source, importSpace);
            if (instance == null)
                instance = UnityEngine.Object.Instantiate(source, importSpace);
            instance.name = "CanonicalRig";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            StripLegacyRuntimeComponents(instance);

            Animator animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null)
                animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Dictionary<string, List<Renderer>> renderersByName = IndexRenderers(instance);
            NmsCreatureModuleBinding[] bindings =
                BuildBindings(root.transform, family, renderersByName);
            BuildAndAssignMaterials(
                familyDirectory,
                publish,
                root.transform,
                out NmsCreatureMaterialDefinition[] materialDefinitions,
                out NmsCreatureRendererMaterialBinding[] rendererMaterialBindings);
            ValidateRig(instance.transform, family, publish);
            ValidateNativeAnimations(instance, publish);
            float surfaceOffset = publish.surfaceRootOffset >= 0f
                ? publish.surfaceRootOffset
                : CalculateSurfaceOffset(family, bindings, root);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    $"{publish.familyId}: failed to save runtime prefab.");

            NmsCreatureFamilyDefinition definition =
                AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(definitionPath);
            if (definition == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(definitionPath) != null)
                    AssetDatabase.DeleteAsset(definitionPath);
                definition = ScriptableObject.CreateInstance<NmsCreatureFamilyDefinition>();
                AssetDatabase.CreateAsset(definition, definitionPath);
            }
            definition.ConfigureImported(
                publish.familyId,
                publish.skeletonHash,
                publish.locomotionType,
                GetSurfaceMode(publish),
                publish.supportsRun,
                publish.variantIds,
                publish.selectionWeight,
                publish.legCount,
                publish.allowRareModules,
                publish.excludedModulePrefixes,
                prefab,
                controller,
                manifestAsset,
                bindings,
                materialDefinitions,
                rendererMaterialBindings,
                publish.loadBearingChains,
                publish.walkSpeed,
                surfaceOffset,
                publish.hoverClearance,
                GetLocalForwardAxis(publish));
            EditorUtility.SetDirty(definition);
            return definition;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static GameObject LoadSource(NmsUnityPublishManifest publish)
    {
        string path =
            string.Equals(publish.sourceKind, "Prefab", StringComparison.OrdinalIgnoreCase)
                ? publish.sourcePrefabAsset
                : publish.sourceModelAsset;
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null)
            throw new InvalidOperationException(
                $"{publish.familyId}: source asset is missing at {path}.");
        return source;
    }

    static void ConfigureModelImporter(NmsUnityPublishManifest publish)
    {
        if (string.Equals(publish.sourceKind, "Prefab", StringComparison.OrdinalIgnoreCase))
        {
            string[] dependencies = AssetDatabase.GetDependencies(
                publish.sourcePrefabAsset,
                true);
            for (int i = 0; i < dependencies.Length; i++)
            {
                if (AssetImporter.GetAtPath(dependencies[i]) is not ModelImporter dependencyImporter
                    || dependencyImporter.isReadable)
                    continue;
                dependencyImporter.isReadable = true;
                dependencyImporter.SaveAndReimport();
            }
            return;
        }
        if (!string.Equals(publish.sourceKind, "Fbx", StringComparison.OrdinalIgnoreCase))
            return;
        ModelImporter importer =
            AssetImporter.GetAtPath(publish.sourceModelAsset) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException(
                $"{publish.familyId}: source FBX importer is missing.");

        bool changed = importer.animationType != ModelImporterAnimationType.Generic
            || importer.avatarSetup != ModelImporterAvatarSetup.NoAvatar
            || !importer.importAnimation
            || importer.optimizeGameObjects
            || importer.animationCompression != ModelImporterAnimationCompression.Off
            || importer.resampleCurves
            || !importer.isReadable;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.resampleCurves = false;
        importer.isReadable = true;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            NmsUnityPublishAction action = publish.actions.FirstOrDefault(value =>
                !string.IsNullOrEmpty(value.clipName)
                && (string.Equals(clips[i].name, value.clipName, StringComparison.Ordinal)
                    || clips[i].name.IndexOf(
                        value.clipName,
                        StringComparison.OrdinalIgnoreCase) >= 0));
            if (action == null || !action.loop)
                continue;
            if (!clips[i].loopTime || !clips[i].loopPose)
                changed = true;
            clips[i].loopTime = true;
            clips[i].loopPose = true;
        }
        if (clips.Length > 0)
            importer.clipAnimations = clips;
        if (changed)
            importer.SaveAndReimport();
    }

    static RuntimeAnimatorController BuildController(NmsUnityPublishManifest publish)
    {
        string directory =
            Path.GetDirectoryName(publish.familyManifestAsset).Replace('\\', '/');
        string path = directory + "/" + publish.familyId + "Locomotion.controller";
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        ChildAnimatorState[] states = machine.states;
        for (int i = 0; i < states.Length; i++)
            machine.RemoveState(states[i].state);

        AnimatorState defaultState = null;
        string[] keys = publish.supportsRun
            ? new[] { "idle", "walk", "run" }
            : new[] { "idle", "walk" };
        string[] stateNames = publish.supportsRun
            ? new[] { "Idle", "Walk", "Run" }
            : new[] { "Idle", "Walk" };
        for (int i = 0; i < keys.Length; i++)
        {
            NmsUnityPublishAction action = publish.actions.FirstOrDefault(
                value => string.Equals(value.key, keys[i], StringComparison.OrdinalIgnoreCase));
            AnimationClip clip = FindClip(action, publish);
            if (clip == null)
                throw new InvalidOperationException(
                    $"{publish.familyId}: {keys[i]} clip is missing.");
            ConfigureStandaloneClipLoop(action, clip);
            AnimatorState state = machine.AddState(stateNames[i]);
            state.motion = clip;
            if (keys[i] == "walk")
                defaultState = state;
        }
        machine.defaultState = defaultState;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static void ConfigureStandaloneClipLoop(
        NmsUnityPublishAction action,
        AnimationClip clip)
    {
        if (action == null
            || !action.loop
            || clip == null
            || string.IsNullOrEmpty(action.assetPath)
            || !action.assetPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            return;

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime && settings.loopBlend)
            return;
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static NmsCreatureSurfaceMode GetSurfaceMode(NmsUnityPublishManifest publish)
    {
        return string.Equals(
            publish.surfaceMode,
            "Hover",
            StringComparison.OrdinalIgnoreCase)
                ? NmsCreatureSurfaceMode.Hover
                : NmsCreatureSurfaceMode.Ground;
    }

    static Vector3 GetLocalForwardAxis(NmsUnityPublishManifest publish)
    {
        Vector3 value = publish.localForwardAxis != null
            ? publish.localForwardAxis.Value
            : Vector3.forward;
        return value.sqrMagnitude > 0.000001f
            ? value.normalized
            : Vector3.forward;
    }

    static AnimationClip FindClip(
        NmsUnityPublishAction action,
        NmsUnityPublishManifest publish)
    {
        if (action == null)
            return null;
        string path =
            string.IsNullOrEmpty(action.assetPath)
                ? publish.sourceModelAsset
                : action.assetPath;
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        AnimationClip fallback = null;
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is not AnimationClip clip
                || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                continue;
            fallback ??= clip;
            if (string.Equals(clip.name, action.clipName, StringComparison.Ordinal)
                || clip.name.IndexOf(action.clipName, StringComparison.OrdinalIgnoreCase) >= 0)
                return clip;
        }
        return fallback;
    }

    static NmsCreatureModuleBinding[] BuildBindings(
        Transform root,
        NmsCreatureFamilyManifestData family,
        Dictionary<string, List<Renderer>> renderersByName)
    {
        var bindings = new List<NmsCreatureModuleBinding>(family.modules.Length);
        var assigned = new HashSet<Renderer>();
        for (int i = 0; i < family.modules.Length; i++)
        {
            NmsCreatureModuleData module = family.modules[i];
            if (module == null || !module.compatible)
                continue;
            var paths = new List<string>();
            for (int objectIndex = 0; objectIndex < module.objects.Length; objectIndex++)
            {
                if (!renderersByName.TryGetValue(
                    module.objects[objectIndex],
                    out List<Renderer> matches))
                    continue;
                for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
                {
                    Renderer renderer = matches[matchIndex];
                    if (!assigned.Add(renderer))
                        throw new InvalidOperationException(
                            $"Renderer '{renderer.name}' belongs to more than one descriptor module.");
                    paths.Add(AnimationUtility.CalculateTransformPath(renderer.transform, root));
                    renderer.enabled = false;
                }
            }
            bindings.Add(new NmsCreatureModuleBinding
            {
                moduleId = module.name,
                rendererPaths = paths
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
            });
        }
        return bindings.ToArray();
    }

    static void BuildAndAssignMaterials(
        string familyDirectory,
        NmsUnityPublishManifest publish,
        Transform root,
        out NmsCreatureMaterialDefinition[] materialDefinitions,
        out NmsCreatureRendererMaterialBinding[] rendererMaterialBindings)
    {
        Shader shader = Shader.Find("Voxel Planet/NMS Creature Uber");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            throw new InvalidOperationException("No shader is available for NMS creature materials.");

        NmsUnityMaterialManifest materialManifest = LoadMaterialManifest(publish);
        var materialRecords = new Dictionary<string, NmsUnityMaterialRecord>(StringComparer.Ordinal);
        var rendererRecords = new Dictionary<string, NmsUnityRendererMaterialRecord>(StringComparer.Ordinal);
        if (materialManifest != null)
        {
            for (int i = 0; i < materialManifest.materials.Length; i++)
            {
                NmsUnityMaterialRecord record = materialManifest.materials[i];
                if (record != null && !string.IsNullOrEmpty(record.materialId))
                    materialRecords[record.materialId] = record;
            }
            for (int i = 0; i < materialManifest.renderers.Length; i++)
            {
                NmsUnityRendererMaterialRecord record = materialManifest.renderers[i];
                if (record != null && !string.IsNullOrEmpty(record.rendererName))
                    rendererRecords[record.rendererName] = record;
            }
        }

        string materialDirectory = familyDirectory + "/Materials";
        EnsureAssetFolder(materialDirectory);
        var builtMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, NmsCreatureMaterialDefinition>(StringComparer.Ordinal);
        var rendererBindings = new List<NmsCreatureRendererMaterialBinding>();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Material[] sourceMaterials = renderer.sharedMaterials;
            if (sourceMaterials == null || sourceMaterials.Length == 0)
                continue;

            rendererRecords.TryGetValue(
                renderer.name,
                out NmsUnityRendererMaterialRecord rendererRecord);
            var materialIds = new string[sourceMaterials.Length];
            var assigned = new Material[sourceMaterials.Length];
            for (int slot = 0; slot < sourceMaterials.Length; slot++)
            {
                string materialId = GetRendererSlotMaterialId(
                    rendererRecord,
                    sourceMaterials[slot],
                    renderer.name,
                    slot);
                if (string.IsNullOrEmpty(materialId))
                    materialId = renderer.name + "_Slot" + slot;
                materialIds[slot] = materialId;
                if (!materialRecords.TryGetValue(materialId, out NmsUnityMaterialRecord record))
                {
                    record = new NmsUnityMaterialRecord
                    {
                        materialId = materialId,
                        sourceMaterialName = sourceMaterials[slot] != null
                            ? sourceMaterials[slot].name
                            : materialId,
                        warnings = new[] { "material record was inferred from Unity renderer slot" }
                    };
                }

                if (!builtMaterials.TryGetValue(materialId, out Material material))
                {
                    material = BuildMaterialAsset(
                        materialDirectory,
                        publish,
                        shader,
                        record,
                        sourceMaterials[slot]);
                    builtMaterials.Add(materialId, material);
                    definitions.Add(materialId, CreateMaterialDefinition(record, material));
                }
                assigned[slot] = material;
            }

            renderer.sharedMaterials = assigned;
            rendererBindings.Add(new NmsCreatureRendererMaterialBinding
            {
                rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, root),
                materialIds = materialIds
            });
        }

        materialDefinitions = definitions.Values
            .OrderBy(value => value.materialId, StringComparer.Ordinal)
            .ToArray();
        rendererMaterialBindings = rendererBindings
            .OrderBy(value => value.rendererPath, StringComparer.Ordinal)
            .ToArray();
    }

    static NmsUnityMaterialManifest LoadMaterialManifest(NmsUnityPublishManifest publish)
    {
        if (publish == null || string.IsNullOrEmpty(publish.materialManifestAsset))
            return null;
        TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(publish.materialManifestAsset);
        if (asset == null)
        {
            Debug.LogWarning(
                $"{publish.familyId}: material manifest is missing at " +
                publish.materialManifestAsset + "; using imported material fallback.");
            return null;
        }
        NmsUnityMaterialManifest manifest =
            JsonConvert.DeserializeObject<NmsUnityMaterialManifest>(asset.text);
        if (manifest == null
            || !string.Equals(manifest.familyId, publish.familyId, StringComparison.Ordinal))
        {
            Debug.LogWarning(
                $"{publish.familyId}: material manifest identity mismatch; " +
                "using imported material fallback.");
            return null;
        }
        return manifest;
    }

    static string GetRendererSlotMaterialId(
        NmsUnityRendererMaterialRecord rendererRecord,
        Material sourceMaterial,
        string rendererName,
        int slot)
    {
        if (rendererRecord != null && rendererRecord.slots != null)
        {
            for (int i = 0; i < rendererRecord.slots.Length; i++)
            {
                if (rendererRecord.slots[i] != null && rendererRecord.slots[i].slot == slot)
                    return rendererRecord.slots[i].materialId;
            }
        }
        if (sourceMaterial != null && !string.IsNullOrEmpty(sourceMaterial.name))
            return sourceMaterial.name;
        return rendererName + "_Slot" + slot;
    }

    static Material BuildMaterialAsset(
        string materialDirectory,
        NmsUnityPublishManifest publish,
        Shader shader,
        NmsUnityMaterialRecord record,
        Material sourceMaterial)
    {
        string assetPath =
            materialDirectory + "/" + SanitizeAssetName(record.materialId) + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, assetPath);
        }
        material.shader = shader;
        material.name = record.materialId;

        Texture main = ResolveTexture(
            publish,
            record.mainTexture,
            sourceMaterial,
            "_MainTex",
            record,
            TextureKind.Main);
        Texture normal = ResolveTexture(
            publish,
            record.normalTexture,
            sourceMaterial,
            "_BumpMap",
            record,
            TextureKind.Normal);
        Texture mask = ResolveTexture(
            publish,
            record.maskTexture,
            sourceMaterial,
            "_MaskMap",
            record,
            TextureKind.Mask);
        Texture emission = ResolveTexture(
            publish,
            record.emissionTexture,
            sourceMaterial,
            "_EmissionMap",
            record,
            TextureKind.Emission);

        NmsCreatureMaterialPreset preset = ResolveMaterialPreset(record, main, normal, mask, emission);

        SetTextureIfPresent(material, "_MainTex", main);
        SetTextureIfPresent(material, "_NormalMap", normal);
        SetTextureIfPresent(material, "_BumpMap", normal);
        SetTextureIfPresent(material, "_MaskMap", mask);
        SetTextureIfPresent(material, "_MasksMap", mask);
        SetTextureIfPresent(material, "_EmissionMap", emission);
        SetColorIfPresent(material, "_PrimaryColor", preset.primaryFallback);
        SetColorIfPresent(material, "_SecondaryColor", preset.secondaryFallback);
        SetColorIfPresent(material, "_AccentColor", preset.accentFallback);
        SetColorIfPresent(material, "_EmissionColor", preset.emissionColor);
        SetFloatIfPresent(material, "_NormalStrength", normal != null ? preset.normalStrength : 0f);
        SetFloatIfPresent(material, "_MaskStrength", mask != null ? preset.maskStrength : 0f);
        SetFloatIfPresent(material, "_EmissionStrength", emission != null ? preset.emissionStrength : 0f);
        SetFloatIfPresent(material, "_PaletteStrength", preset.paletteStrength);
        SetFloatIfPresent(material, "_VertexColorStrength", preset.vertexColorStrength);
        SetFloatIfPresent(material, "_Glossiness", preset.smoothness);
        SetFloatIfPresent(material, "_Metallic", preset.metallic);
        SetFloatIfPresent(material, "_SurfaceNoiseStrength", preset.surfaceNoiseStrength);
        SetFloatIfPresent(
            material,
            "_AlphaCutoff",
            string.Equals(record.transparencyMode, "Cutout", StringComparison.OrdinalIgnoreCase)
                ? 0.35f
                : 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static NmsCreatureMaterialDefinition CreateMaterialDefinition(
        NmsUnityMaterialRecord record,
        Material material)
    {
        return new NmsCreatureMaterialDefinition
        {
            materialId = record.materialId,
            materialPreset = ResolveMaterialPreset(record, null, null, null, null).presetName,
            sourceMxmlPath = record.sourceMxmlPath,
            sourceMxmlSha256 = record.sourceMxmlSha256,
            mainTextureSourcePath = record.mainTextureSourcePath,
            mainTextureSourceSha256 = record.mainTextureSourceSha256,
            normalTextureSourcePath = record.normalTextureSourcePath,
            normalTextureSourceSha256 = record.normalTextureSourceSha256,
            maskTextureSourcePath = record.maskTextureSourcePath,
            maskTextureSourceSha256 = record.maskTextureSourceSha256,
            emissionTextureSourcePath = record.emissionTextureSourcePath,
            emissionTextureSourceSha256 = record.emissionTextureSourceSha256,
            transparencyMode = record.transparencyMode,
            maskChannelLayout = record.maskChannelLayout,
            material = material,
            hasMainTexture = material != null
                && material.HasProperty("_MainTex")
                && material.GetTexture("_MainTex") != null,
            hasNormalTexture = material != null
                && ((material.HasProperty("_NormalMap")
                        && material.GetTexture("_NormalMap") != null)
                    || (material.HasProperty("_BumpMap")
                        && material.GetTexture("_BumpMap") != null)),
            hasMaskTexture = material != null
                && ((material.HasProperty("_MaskMap")
                        && material.GetTexture("_MaskMap") != null)
                    || (material.HasProperty("_MasksMap")
                        && material.GetTexture("_MasksMap") != null)),
            hasEmissionTexture = material != null
                && material.HasProperty("_EmissionMap")
                && material.GetTexture("_EmissionMap") != null,
            metallic = material != null && material.HasProperty("_Metallic")
                ? material.GetFloat("_Metallic") : 0f,
            smoothness = material != null && material.HasProperty("_Glossiness")
                ? material.GetFloat("_Glossiness") : 0.35f,
            paletteStrength = material != null && material.HasProperty("_PaletteStrength")
                ? material.GetFloat("_PaletteStrength") : 0.35f,
            emissionStrength = material != null && material.HasProperty("_EmissionStrength")
                ? material.GetFloat("_EmissionStrength") : 0f,
            warnings = record.warnings ?? Array.Empty<string>()
        };
    }

    struct NmsCreatureMaterialPreset
    {
        public string presetName;
        public float metallic;
        public float smoothness;
        public float normalStrength;
        public float maskStrength;
        public float emissionStrength;
        public float paletteStrength;
        public float vertexColorStrength;
        public float surfaceNoiseStrength;
        public Color primaryFallback;
        public Color secondaryFallback;
        public Color accentFallback;
        public Color emissionColor;
    }

    static NmsCreatureMaterialPreset ResolveMaterialPreset(
        NmsUnityMaterialRecord record,
        Texture main,
        Texture normal,
        Texture mask,
        Texture emission)
    {
        string presetName = !string.IsNullOrEmpty(record.materialPreset)
            ? record.materialPreset
            : InferMaterialPreset(record);
        bool hasMain = main != null || !string.IsNullOrEmpty(record.mainTexture);
        var preset = new NmsCreatureMaterialPreset
        {
            presetName = presetName,
            metallic = 0f,
            smoothness = 0.34f,
            normalStrength = normal != null || !string.IsNullOrEmpty(record.normalTexture) ? 0.9f : 0f,
            maskStrength = mask != null || !string.IsNullOrEmpty(record.maskTexture) ? 1f : 0f,
            emissionStrength = emission != null || !string.IsNullOrEmpty(record.emissionTexture) ? 1f : 0f,
            paletteStrength = hasMain ? 0.25f : 1f,
            vertexColorStrength = 0f,
            surfaceNoiseStrength = 0.025f,
            primaryFallback = Color.white,
            secondaryFallback = new Color(0.62f, 0.7f, 0.75f, 1f),
            accentFallback = new Color(0.38f, 0.32f, 0.25f, 1f),
            emissionColor = Color.black
        };

        switch (presetName)
        {
            case "Fur":
                preset.smoothness = 0.18f;
                preset.normalStrength = Mathf.Max(preset.normalStrength, 0.55f);
                preset.paletteStrength = hasMain ? 0.18f : 0.85f;
                preset.surfaceNoiseStrength = 0.055f;
                break;
            case "Horn":
                preset.smoothness = 0.42f;
                preset.normalStrength = Mathf.Max(preset.normalStrength, 0.75f);
                preset.paletteStrength = hasMain ? 0.12f : 0.55f;
                preset.accentFallback = new Color(0.72f, 0.63f, 0.48f, 1f);
                break;
            case "Shell":
                preset.smoothness = 0.52f;
                preset.normalStrength = Mathf.Max(preset.normalStrength, 0.9f);
                preset.paletteStrength = hasMain ? 0.16f : 0.62f;
                preset.surfaceNoiseStrength = 0.018f;
                break;
            case "Bone":
                preset.smoothness = 0.31f;
                preset.normalStrength = Mathf.Max(preset.normalStrength, 0.65f);
                preset.paletteStrength = hasMain ? 0.08f : 0.35f;
                preset.primaryFallback = new Color(0.82f, 0.78f, 0.68f, 1f);
                preset.secondaryFallback = new Color(0.55f, 0.5f, 0.42f, 1f);
                break;
            case "Mechanical":
                preset.metallic = 0.38f;
                preset.smoothness = 0.58f;
                preset.normalStrength = Mathf.Max(preset.normalStrength, 0.75f);
                preset.paletteStrength = hasMain ? 0.18f : 0.7f;
                preset.primaryFallback = new Color(0.42f, 0.48f, 0.52f, 1f);
                break;
            case "Glow":
                preset.smoothness = 0.5f;
                preset.emissionStrength = Mathf.Max(preset.emissionStrength, 1.35f);
                preset.paletteStrength = hasMain ? 0.22f : 0.85f;
                preset.emissionColor = new Color(0.2f, 0.8f, 1f, 1f);
                break;
            default:
                preset.presetName = "Skin";
                preset.smoothness = 0.36f;
                preset.normalStrength = Mathf.Max(preset.normalStrength, 0.7f);
                preset.paletteStrength = hasMain ? 0.28f : 1f;
                preset.surfaceNoiseStrength = 0.03f;
                break;
        }

        if (record.metallic >= 0f)
            preset.metallic = Mathf.Clamp01(record.metallic);
        if (record.smoothness >= 0f)
            preset.smoothness = Mathf.Clamp01(record.smoothness);
        else if (record.roughness >= 0f)
            preset.smoothness = Mathf.Clamp01(1f - record.roughness);
        if (record.glow >= 0f)
            preset.emissionStrength = Mathf.Max(0f, record.glow);
        if (record.paletteStrength >= 0f)
            preset.paletteStrength = Mathf.Clamp01(record.paletteStrength);
        return preset;
    }

    static string InferMaterialPreset(NmsUnityMaterialRecord record)
    {
        string key = NormalizeName(
            (record.materialId ?? string.Empty) + " "
            + (record.sourceMaterialName ?? string.Empty) + " "
            + (record.mainTexture ?? string.Empty) + " "
            + (record.normalTexture ?? string.Empty) + " "
            + (record.maskTexture ?? string.Empty) + " "
            + (record.emissionTexture ?? string.Empty));
        if (ContainsAny(key, "glow", "emiss", "light", "neon", "energy"))
            return "Glow";
        if (ContainsAny(key, "metal", "mech", "robot", "tech", "armour", "armor"))
            return "Mechanical";
        if (ContainsAny(key, "bone", "skel", "teeth", "tooth", "claw", "rib"))
            return "Bone";
        if (ContainsAny(key, "horn", "tusk", "spine", "spike", "shell", "plate", "hoof", "antler"))
            return ContainsAny(key, "shell", "plate", "carapace", "chitin")
                ? "Shell"
                : "Horn";
        if (ContainsAny(key, "fur", "hair", "mane", "wool"))
            return "Fur";
        return "Skin";
    }

    static bool ContainsAny(string value, params string[] needles)
    {
        for (int i = 0; i < needles.Length; i++)
        {
            if (value.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    enum TextureKind
    {
        Main,
        Normal,
        Mask,
        Emission
    }

    static Texture ResolveTexture(
        NmsUnityPublishManifest publish,
        string explicitName,
        Material sourceMaterial,
        string sourceProperty,
        NmsUnityMaterialRecord record,
        TextureKind kind)
    {
        Texture explicitTexture = FindTextureAsset(publish, explicitName, kind);
        if (explicitTexture != null)
            return explicitTexture;
        if (sourceMaterial != null && sourceMaterial.HasProperty(sourceProperty))
        {
            Texture sourceTexture = sourceMaterial.GetTexture(sourceProperty);
            if (sourceTexture != null)
                return sourceTexture;
        }
        if (sourceMaterial != null && sourceMaterial.HasProperty("_MainTex"))
        {
            Texture sourceTexture = sourceMaterial.GetTexture("_MainTex");
            if (kind == TextureKind.Main && sourceTexture != null)
                return sourceTexture;
        }
        return GuessTextureAsset(publish, record, kind);
    }

    static Texture FindTextureAsset(
        NmsUnityPublishManifest publish,
        string textureName,
        TextureKind kind)
    {
        if (string.IsNullOrEmpty(textureName))
            return null;
        string familyDirectory =
            Path.GetDirectoryName(publish.familyManifestAsset).Replace('\\', '/');
        string fullFamilyDirectory = Path.GetFullPath(familyDirectory);
        string explicitPath = Path.Combine(
            fullFamilyDirectory,
            textureName.Replace('/', Path.DirectorySeparatorChar));
        string[] matches = File.Exists(explicitPath)
            ? new[] { explicitPath }
            : Directory.Exists(familyDirectory)
                ? Directory.GetFiles(
                    fullFamilyDirectory,
                    Path.GetFileName(textureName),
                    SearchOption.AllDirectories)
                : Array.Empty<string>();
        for (int i = 0; i < matches.Length; i++)
        {
            string assetPath = ToAssetPath(matches[i]);
            ConfigureTextureImporter(assetPath, kind);
            Texture texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
            if (texture != null)
                return texture;
        }
        return null;
    }

    static Texture GuessTextureAsset(
        NmsUnityPublishManifest publish,
        NmsUnityMaterialRecord record,
        TextureKind kind)
    {
        string familyDirectory =
            Path.GetDirectoryName(publish.familyManifestAsset).Replace('\\', '/');
        if (!Directory.Exists(familyDirectory))
            return null;
        string materialKey = NormalizeName(
            !string.IsNullOrEmpty(record.sourceMaterialName)
                ? record.sourceMaterialName
                : record.materialId);
        string[] files = Directory.GetFiles(
            Path.GetFullPath(familyDirectory),
            "*.*",
            SearchOption.AllDirectories);
        string best = null;
        int bestScore = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string extension = Path.GetExtension(files[i]);
            if (!IsTextureExtension(extension))
                continue;
            string name = NormalizeName(Path.GetFileNameWithoutExtension(files[i]));
            int score = TextureScore(name, materialKey, kind);
            if (score <= bestScore)
                continue;
            best = files[i];
            bestScore = score;
        }
        if (string.IsNullOrEmpty(best))
            return null;
        string assetPath = ToAssetPath(best);
        ConfigureTextureImporter(assetPath, kind);
        return AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
    }

    static int TextureScore(string textureName, string materialKey, TextureKind kind)
    {
        if (string.IsNullOrEmpty(textureName))
            return 0;
        int score = 0;
        if (!string.IsNullOrEmpty(materialKey)
            && (textureName.Contains(materialKey) || materialKey.Contains(textureName)))
            score += 20;
        bool normal = textureName.Contains("normal") || textureName.Contains("nrm");
        bool mask = textureName.Contains("mask") || textureName.Contains("rough")
            || textureName.Contains("metal");
        bool emission = textureName.Contains("emiss") || textureName.Contains("glow")
            || textureName.Contains("light");
        bool baseLike = textureName.Contains("base") || textureName.Contains("fur")
            || textureName.Contains("scale") || textureName.Contains("skin")
            || textureName.Contains("horn");
        switch (kind)
        {
            case TextureKind.Main:
                if (baseLike) score += 10;
                if (normal || mask || emission) score -= 20;
                break;
            case TextureKind.Normal:
                if (normal) score += 40;
                break;
            case TextureKind.Mask:
                if (mask) score += 40;
                break;
            case TextureKind.Emission:
                if (emission) score += 40;
                break;
        }
        return score;
    }

    static void ConfigureTextureImporter(string assetPath, TextureKind kind)
    {
        if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            return;
        bool changed = importer.wrapMode != TextureWrapMode.Repeat
            || !importer.mipmapEnabled;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        TextureImporterType type = kind == TextureKind.Normal
            ? TextureImporterType.NormalMap
            : TextureImporterType.Default;
        if (importer.textureType != type)
        {
            importer.textureType = type;
            changed = true;
        }
        bool srgb = kind == TextureKind.Main || kind == TextureKind.Emission;
        if (importer.sRGBTexture != srgb)
        {
            importer.sRGBTexture = srgb;
            changed = true;
        }
        if (changed)
            importer.SaveAndReimport();
    }

    static bool IsTextureExtension(string extension)
    {
        return string.Equals(extension, ".dds", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    static void SetTextureIfPresent(Material material, string propertyName, Texture texture)
    {
        if (material.HasProperty(propertyName))
            material.SetTexture(propertyName, texture);
    }

    static void SetColorIfPresent(Material material, string propertyName, Color value)
    {
        if (material.HasProperty(propertyName))
            material.SetColor(propertyName, value);
    }

    static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }

    static void EnsureAssetFolder(string assetFolder)
    {
        string[] parts = assetFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    static string SanitizeAssetName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Material";
        char[] invalid = Path.GetInvalidFileNameChars();
        var result = value.ToCharArray();
        for (int i = 0; i < result.Length; i++)
        {
            if (Array.IndexOf(invalid, result[i]) >= 0)
                result[i] = '_';
        }
        return new string(result);
    }

    static string NormalizeName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var chars = value.ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    static float CalculateSurfaceOffset(
        NmsCreatureFamilyManifestData family,
        NmsCreatureModuleBinding[] bindings,
        GameObject root)
    {
        var baseline = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < family.baselineChoices.Length; i++)
        {
            if (!string.IsNullOrEmpty(family.baselineChoices[i].name))
                baseline.Add(family.baselineChoices[i].name);
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            bool enable = baseline.Contains(bindings[i].moduleId);
            for (int pathIndex = 0; pathIndex < bindings[i].rendererPaths.Length; pathIndex++)
            {
                Transform target = root.transform.Find(bindings[i].rendererPaths[pathIndex]);
                Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
                if (renderer != null)
                    renderer.enabled = enable;
            }
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled)
                continue;
            if (!hasBounds)
            {
                bounds = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        for (int i = 0; i < bindings.Length; i++)
        {
            for (int pathIndex = 0; pathIndex < bindings[i].rendererPaths.Length; pathIndex++)
            {
                Transform target = root.transform.Find(bindings[i].rendererPaths[pathIndex]);
                Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
                if (renderer != null)
                    renderer.enabled = false;
            }
        }
        return hasBounds ? Mathf.Max(0.02f, -bounds.min.y + 0.02f) : 0.05f;
    }

    static void ValidateRig(
        Transform root,
        NmsCreatureFamilyManifestData family,
        NmsUnityPublishManifest publish)
    {
        SkinnedMeshRenderer[] renderers =
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            if (renderer.sharedMesh == null
                || renderer.bones.Length != renderer.sharedMesh.bindposes.Length
                || renderer.bones.Any(bone => bone == null))
                throw new InvalidOperationException(
                    $"{family.familyId}: renderer '{renderer.name}' " +
                    "has invalid bones or bind poses.");

            for (int boneIndex = 0; boneIndex < renderer.bones.Length; boneIndex++)
            {
                Transform bone = renderer.bones[boneIndex];
                if (!bones.ContainsKey(bone.name))
                    bones.Add(bone.name, bone);
            }
        }

        for (int chainIndex = 0;
            chainIndex < publish.loadBearingChains.Length;
            chainIndex++)
        {
            NmsCreatureLegChain chain = publish.loadBearingChains[chainIndex];
            for (int i = 0; i + 1 < chain.bones.Length; i++)
            {
                if (!bones.TryGetValue(chain.bones[i], out Transform parent))
                    throw new InvalidOperationException(
                        $"{family.familyId}: load-bearing bone '{chain.bones[i]}' " +
                        $"is missing from skinned renderer bones.");
                if (!bones.TryGetValue(chain.bones[i + 1], out Transform child))
                    throw new InvalidOperationException(
                        $"{family.familyId}: load-bearing bone '{chain.bones[i + 1]}' " +
                        $"is missing from skinned renderer bones.");
                if (child.parent != parent)
                    throw new InvalidOperationException(
                        $"{family.familyId}: chain '{chain.id}' expected " +
                        $"{parent.name}->{child.name}, but the imported parent is " +
                        $"'{(child.parent != null ? child.parent.name : "<none>")}'.");
            }
        }
    }

    static void ValidateNativeAnimations(
        GameObject instance,
        NmsUnityPublishManifest publish)
    {
        Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
        var positions = new Vector3[transforms.Length];
        var rotations = new Quaternion[transforms.Length];
        var scales = new Vector3[transforms.Length];
        for (int i = 0; i < transforms.Length; i++)
        {
            positions[i] = transforms[i].localPosition;
            rotations[i] = transforms[i].localRotation;
            scales[i] = transforms[i].localScale;
        }

        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        SkinnedMeshRenderer[] renderers =
            instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Transform[] values = renderers[rendererIndex].bones;
            for (int boneIndex = 0; boneIndex < values.Length; boneIndex++)
            {
                Transform bone = values[boneIndex];
                if (bone != null && !bones.ContainsKey(bone.name))
                    bones.Add(bone.name, bone);
            }
        }

        var segments = new List<AnimationSegment>();
        for (int chainIndex = 0;
            chainIndex < publish.loadBearingChains.Length;
            chainIndex++)
        {
            NmsCreatureLegChain chain = publish.loadBearingChains[chainIndex];
            for (int boneIndex = 0; boneIndex + 1 < chain.bones.Length; boneIndex++)
            {
                Transform parent = bones[chain.bones[boneIndex]];
                Transform child = bones[chain.bones[boneIndex + 1]];
                segments.Add(new AnimationSegment
                {
                    id = chain.id + ":" + parent.name + "->" + child.name,
                    parent = parent,
                    child = child,
                    length = Vector3.Distance(parent.position, child.position)
                });
            }
        }

        try
        {
            for (int actionIndex = 0; actionIndex < publish.actions.Length; actionIndex++)
            {
                NmsUnityPublishAction action = publish.actions[actionIndex];
                AnimationClip clip = FindClip(action, publish);
                int frames = Mathf.Max(1, action.frames);
                for (int frame = 0; frame < frames; frame++)
                {
                    float normalized = frames > 1 ? frame / (float)(frames - 1) : 0f;
                    clip.SampleAnimation(instance, normalized * clip.length);
                    for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
                    {
                        Transform value = transforms[transformIndex];
                        if (!IsFinite(value.localPosition)
                            || !IsFinite(value.localScale)
                            || !IsFinite(value.localRotation))
                            throw new InvalidOperationException(
                                $"{publish.familyId}: action '{action.key}' frame {frame} " +
                                $"contains a non-finite transform at '{value.name}'.");
                    }

                    for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                    {
                        AnimationSegment segment = segments[segmentIndex];
                        float length =
                            Vector3.Distance(segment.parent.position, segment.child.position);
                        float error =
                            Mathf.Abs(length / Mathf.Max(0.000001f, segment.length) - 1f);
                        if (error > 0.02f)
                            throw new InvalidOperationException(
                                $"{publish.familyId}: action '{action.key}' frame {frame} " +
                                $"changes '{segment.id}' length by {error * 100f:F2}%.");
                    }
                }
            }
        }
        finally
        {
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].localPosition = positions[i];
                transforms[i].localRotation = rotations[i];
                transforms[i].localScale = scales[i];
            }
        }
    }

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x)
            && IsFinite(value.y)
            && IsFinite(value.z)
            && IsFinite(value.w);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    struct AnimationSegment
    {
        public string id;
        public Transform parent;
        public Transform child;
        public float length;
    }

    static Dictionary<string, List<Renderer>> IndexRenderers(GameObject root)
    {
        var result = new Dictionary<string, List<Renderer>>(StringComparer.Ordinal);
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!result.TryGetValue(renderers[i].name, out List<Renderer> values))
            {
                values = new List<Renderer>();
                result.Add(renderers[i].name, values);
            }
            values.Add(renderers[i]);
        }
        return result;
    }

    static void StripLegacyRuntimeComponents(GameObject root)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
                UnityEngine.Object.DestroyImmediate(behaviours[i]);
        }
    }

    static void ValidatePublish(NmsUnityPublishManifest publish, string path)
    {
        if (publish == null
            || string.IsNullOrWhiteSpace(publish.familyId)
            || string.IsNullOrWhiteSpace(publish.skeletonHash)
            || string.IsNullOrWhiteSpace(publish.validationHash)
            || string.IsNullOrWhiteSpace(publish.familyManifestAsset))
            throw new InvalidOperationException(
                "Invalid NMS family publish manifest: " + path);
        int requiredActionCount = publish.supportsRun ? 3 : 2;
        if (publish.actions == null || publish.actions.Length < requiredActionCount)
            throw new InvalidOperationException(
                publish.supportsRun
                    ? $"{publish.familyId}: Idle, Walk and Run are required."
                    : $"{publish.familyId}: native Idle and Walk are required.");
    }

    static string ToAssetPath(string path)
    {
        string normalized = Path.GetFullPath(path).Replace('\\', '/');
        string project = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/');
        if (!normalized.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Path is outside the Unity project: " + path);
        return normalized.Substring(project.Length + 1);
    }
}
