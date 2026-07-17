using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class AntelopeFamilyCatalogBuilder
{
    const string Root = "Assets/Creatures/Authorized/NMS/AntelopeFamily";
    const string ManifestFolder = Root + "/Manifests";
    const string RigFolder = Root + "/Rigs";
    const string ModuleFolder = Root + "/Modules";
    const string CatalogFolder = Root + "/Catalog";
    const string AnimationFolder = Root + "/Animations";
    const string AnimationFbxPath = AnimationFolder + "/AntelopeLocomotion.fbx";
    const string AnimationControllerPath = AnimationFolder + "/AntelopeLocomotion.controller";
    const string RuntimeAnimationFolder = AnimationFolder + "/Runtime";
    const string CatalogPath = CatalogFolder + "/AntelopeFamily.asset";
    const string FallbackPath = "Assets/Creatures/Authorized/NMS/Prefabs/AntelopeVariant_0042.prefab";
    const int BuilderVersion = 8;
    const string AutoBuildSessionKey = "AntelopeFamilyCatalogBuilder.AutoBuildV1";
    const string ValidationSessionKey = "AntelopeFamilyCatalogBuilder.ValidateV8";

    static AntelopeFamilyCatalogBuilder()
    {
        EditorApplication.delayCall += AutoBuildIfNeeded;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += AutoBuildIfNeeded;
    }

    static void AutoBuildIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        CreatureRigFamilyDefinition existing = AssetDatabase.LoadAssetAtPath<CreatureRigFamilyDefinition>(CatalogPath);
        if (existing != null)
        {
            if (existing.catalogVersion < BuilderVersion)
            {
                BuildAllSafely();
                return;
            }
            if (!SessionState.GetBool(ValidationSessionKey, false))
            {
                SessionState.SetBool(ValidationSessionKey, true);
                try
                {
                    ValidateCatalog(existing);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
            return;
        }
        if (SessionState.GetBool(AutoBuildSessionKey, false))
            return;
        if (AssetDatabase.FindAssets("t:TextAsset", new[] { ManifestFolder }).Length < 5)
            return;

        SessionState.SetBool(AutoBuildSessionKey, true);
        BuildAllSafely();
    }

    static void BuildAllSafely()
    {
        try
        {
            BuildAll();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    [Serializable]
    sealed class VariantManifest
    {
        public string stableId;
        public string variant;
        public string sourceFbxAssetPath;
        public List<CreatureDescriptorGroup> descriptorRoots = new List<CreatureDescriptorGroup>();
        public List<ManifestModule> modules = new List<ManifestModule>();
    }

    [Serializable]
    sealed class ManifestModule
    {
        public string stableId;
        public string sourceObjectName;
        public bool required;
    }

    public static void BuildAll()
    {
        EnsureFolder(RigFolder);
        EnsureFolder(ModuleFolder);
        EnsureFolder(CatalogFolder);
        EnsureFolder(AnimationFolder);
        EnsureFolder(RuntimeAnimationFolder);
        RuntimeAnimatorController locomotionController = BuildLocomotionController();

        string[] manifestGuids = AssetDatabase.FindAssets("t:TextAsset", new[] { ManifestFolder });
        Array.Sort(manifestGuids, StringComparer.Ordinal);
        var variants = new List<CreatureFamilyVariantDefinition>();
        int errors = 0;

        for (int i = 0; i < manifestGuids.Length; i++)
        {
            string manifestPath = AssetDatabase.GUIDToAssetPath(manifestGuids[i]);
            if (!manifestPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>(manifestPath);
            VariantManifest manifest = text != null ? JsonUtility.FromJson<VariantManifest>(text.text) : null;
            if (manifest == null || string.IsNullOrEmpty(manifest.stableId))
            {
                Debug.LogError("Invalid creature manifest: " + manifestPath);
                errors++;
                continue;
            }

            AssetDatabase.ImportAsset(manifest.sourceFbxAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(manifest.sourceFbxAssetPath);
            if (sourceAsset == null)
            {
                Debug.LogError("Missing creature module FBX: " + manifest.sourceFbxAssetPath);
                errors++;
                continue;
            }

            GameObject source = PrefabUtility.InstantiatePrefab(sourceAsset) as GameObject;
            if (source == null)
            {
                errors++;
                continue;
            }
            try
            {
                CreatureFamilyVariantDefinition variant = BuildVariant(manifest, source, ref errors);
                if (variant != null)
                    variants.Add(variant);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        CreatureRigFamilyDefinition family = AssetDatabase.LoadAssetAtPath<CreatureRigFamilyDefinition>(CatalogPath);
        if (family == null)
        {
            family = ScriptableObject.CreateInstance<CreatureRigFamilyDefinition>();
            AssetDatabase.CreateAsset(family, CatalogPath);
        }
        family.stableId = "nms_antelope";
        family.catalogVersion = BuilderVersion;
        family.maxVisibleModules = 24;
        family.maxRenderers = 32;
        family.rigImportScale = 100f;
        family.visualRotation = Quaternion.Euler(0f, 180f, 0f);
        family.uniformScaleRange = new Vector2(.9f, 1.1f);
        family.locomotionController = locomotionController;
        family.variants = variants;
        EditorUtility.SetDirty(family);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ValidateCatalog(family);
        Debug.Log($"Antelope family catalog built. Variants={variants.Count}, Errors={errors}, Asset={CatalogPath}");
        if (errors > 0)
            throw new InvalidOperationException($"Antelope family catalog contains {errors} build errors.");
    }

    public static void ValidateCatalog(CreatureRigFamilyDefinition family)
    {
        if (family == null)
            throw new ArgumentNullException(nameof(family));

        int errors = 0;
        int moduleCount = 0;
        int invalidModuleCount = 0;
        int bindPoseMismatchCount = 0;
        int missingBoneCount = 0;
        int invalidLegCount = 0;
        string firstValidationFailure = string.Empty;
        for (int variantIndex = 0; variantIndex < family.variants.Count; variantIndex++)
        {
            CreatureFamilyVariantDefinition variant = family.variants[variantIndex];
            if (variant == null || variant.rigPrefab == null)
            {
                errors++;
                continue;
            }
            var bones = new HashSet<string>(StringComparer.Ordinal);
            Transform[] rigTransforms = variant.rigPrefab.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < rigTransforms.Length; i++)
                bones.Add(rigTransforms[i].name);

            CreatureRigSemantics semantics = variant.semantics;
            if (semantics == null || semantics.legs == null || semantics.legs.Count == 0)
            {
                errors++;
                invalidLegCount++;
            }
            else
            {
                for (int legIndex = 0; legIndex < semantics.legs.Count; legIndex++)
                {
                    CreatureSemanticLegDefinition leg = semantics.legs[legIndex];
                    bool valid = leg != null
                        && bones.Contains(leg.upperBone)
                        && bones.Contains(leg.lowerBone)
                        && bones.Contains(leg.ankleBone)
                        && bones.Contains(leg.footBone)
                        && leg.upperLength > .001f
                        && leg.lowerLength > .001f
                        && leg.ankleLength > .001f
                        && leg.upperAxisLocal.sqrMagnitude > .5f
                        && leg.lowerAxisLocal.sqrMagnitude > .5f
                        && leg.ankleAxisLocal.sqrMagnitude > .5f
                        && leg.footContact != null
                        && leg.footContact.soleOffset > 0f;
                    if (valid)
                        continue;
                    errors++;
                    invalidLegCount++;
                    if (string.IsNullOrEmpty(firstValidationFailure))
                        firstValidationFailure = $"Variant '{variant.stableId}' has an invalid semantic leg at index {legIndex}.";
                }
            }

            var moduleIds = new HashSet<string>(StringComparer.Ordinal);
            for (int moduleIndex = 0; moduleIndex < variant.modules.Count; moduleIndex++)
            {
                CreatureModuleDefinition module = variant.modules[moduleIndex];
                moduleCount++;
                if (module == null || string.IsNullOrEmpty(module.stableId)
                    || !moduleIds.Add(module.stableId) || module.prefab == null)
                {
                    errors++;
                    invalidModuleCount++;
                    if (string.IsNullOrEmpty(firstValidationFailure))
                        firstValidationFailure = $"Invalid module metadata in variant '{variant.stableId}'.";
                    continue;
                }
                CreatureSkinnedModuleBinding binding = module.prefab.GetComponent<CreatureSkinnedModuleBinding>();
                SkinnedMeshRenderer renderer = module.prefab.GetComponent<SkinnedMeshRenderer>();
                if (binding == null || renderer == null || renderer.sharedMesh == null || binding.boneNames == null)
                {
                    errors++;
                    invalidModuleCount++;
                    if (string.IsNullOrEmpty(firstValidationFailure))
                        firstValidationFailure = $"Module '{module.stableId}' is missing its binding or mesh.";
                    continue;
                }
                if (renderer.sharedMesh.bindposes.Length != binding.boneNames.Length)
                {
                    errors++;
                    bindPoseMismatchCount++;
                    if (string.IsNullOrEmpty(firstValidationFailure))
                        firstValidationFailure = $"Module '{module.stableId}' has {renderer.sharedMesh.bindposes.Length} bind poses but {binding.boneNames.Length} bone names.";
                    continue;
                }
                for (int boneIndex = 0; boneIndex < binding.boneNames.Length; boneIndex++)
                    if (!bones.Contains(binding.boneNames[boneIndex]))
                    {
                        errors++;
                        missingBoneCount++;
                        if (string.IsNullOrEmpty(firstValidationFailure))
                            firstValidationFailure = $"Module '{module.stableId}' references missing bone '{binding.boneNames[boneIndex]}'.";
                    }
            }
        }

        var signatures = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 1000; seed++)
        {
            DescriptorCreatureSpeciesGenerator.ClearCache();
            GeneratedCreatureSpeciesDefinition first = DescriptorCreatureSpeciesGenerator.Generate(family, seed);
            DescriptorCreatureSpeciesGenerator.ClearCache();
            GeneratedCreatureSpeciesDefinition second = DescriptorCreatureSpeciesGenerator.Generate(family, seed);
            if (first == null || second == null || first.selectedModuleIds.Count == 0
                || first.selectedModuleIds.Count > family.maxVisibleModules
                || first.BuildSignature() != second.BuildSignature()
                || !Mathf.Approximately(first.uniformScale, second.uniformScale)
                || first.primaryColor != second.primaryColor
                || first.secondaryColor != second.secondaryColor)
                errors++;
            if (seed < 100 && first != null)
                signatures.Add(first.BuildSignature());
        }
        if (signatures.Count < 30)
        {
            Debug.LogError($"Descriptor creature variation is too low: {signatures.Count}/30 signatures.");
            errors++;
        }

        Debug.Log($"Antelope family validation complete. Variants={family.variants.Count}, " +
            $"Modules={moduleCount}, First100Signatures={signatures.Count}, Errors={errors}, " +
            $"InvalidModules={invalidModuleCount}, BindPoseMismatches={bindPoseMismatchCount}, " +
            $"MissingBones={missingBoneCount}, InvalidLegs={invalidLegCount}, " +
            $"FirstFailure={firstValidationFailure}", family);
        if (errors > 0)
            throw new InvalidOperationException($"Antelope family validation failed with {errors} errors.");
    }

    static CreatureFamilyVariantDefinition BuildVariant(
        VariantManifest manifest,
        GameObject source,
        ref int errors)
    {
        SkinnedMeshRenderer[] sourceRenderers = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var renderersByName = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            if (!renderersByName.ContainsKey(sourceRenderers[i].name))
                renderersByName.Add(sourceRenderers[i].name, sourceRenderers[i]);
            else
                Debug.LogWarning($"Duplicate renderer name '{sourceRenderers[i].name}' in {manifest.stableId}.");
        }

        string variantRigFolder = RigFolder + "/" + manifest.stableId;
        string variantModuleFolder = ModuleFolder + "/" + manifest.stableId;
        EnsureFolder(variantRigFolder);
        EnsureFolder(variantModuleFolder);

        GameObject rigPrefab = BuildRigPrefab(source, sourceRenderers,
            variantRigFolder + "/" + manifest.stableId + "_Rig.prefab");
        if (rigPrefab == null)
        {
            Debug.LogError("Could not build rig for " + manifest.stableId);
            errors++;
            return null;
        }

        var moduleDefinitions = new List<CreatureModuleDefinition>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < manifest.modules.Count; i++)
        {
            ManifestModule module = manifest.modules[i];
            if (module == null || !seenIds.Add(module.stableId))
            {
                Debug.LogError($"Duplicate or empty module ID in {manifest.stableId}: {module?.stableId}");
                errors++;
                continue;
            }
            if (!renderersByName.TryGetValue(module.sourceObjectName, out SkinnedMeshRenderer sourceRenderer))
            {
                Debug.LogError($"Renderer '{module.sourceObjectName}' was not found for module '{module.stableId}'.");
                errors++;
                continue;
            }
            if (!ValidateRenderer(sourceRenderer, module.stableId))
            {
                errors++;
                continue;
            }

            string prefabPath = variantModuleFolder + "/" + module.stableId + ".prefab";
            GameObject prefab = BuildModulePrefab(source.transform, sourceRenderer, module.stableId, prefabPath);
            if (prefab == null)
            {
                errors++;
                continue;
            }
            moduleDefinitions.Add(new CreatureModuleDefinition
            {
                stableId = module.stableId,
                prefab = prefab,
                required = module.required,
                requiredBones = Array.Empty<string>()
            });
        }

        ResolveVariantSettings(manifest.variant, out AntelopeFamilyVariant variantType,
            out CreatureTopology topology, out float weight);
        CreatureRigSemantics semantics = BuildSemantics(variantType);
        CalibrateSemantics(semantics, source, sourceRenderers);
        var result = new CreatureFamilyVariantDefinition
        {
            stableId = manifest.stableId,
            variant = variantType,
            bioWeight = weight,
            topology = topology,
            rigPrefab = rigPrefab,
            safeFallbackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPath),
            semantics = semantics,
            descriptorRoots = manifest.descriptorRoots ?? new List<CreatureDescriptorGroup>(),
            modules = moduleDefinitions,
            morphChannels = BuildMorphChannels(variantType)
        };
        return result;
    }

    static bool ValidateRenderer(SkinnedMeshRenderer renderer, string moduleId)
    {
        if (renderer.sharedMesh == null || renderer.sharedMesh.vertexCount == 0)
        {
            Debug.LogError("Creature module has no mesh: " + moduleId);
            return false;
        }
        if (renderer.bones == null || renderer.bones.Length == 0)
        {
            Debug.LogError("Creature module has no skin bones: " + moduleId);
            return false;
        }
        if (renderer.sharedMesh.bindposes == null || renderer.sharedMesh.bindposes.Length != renderer.bones.Length)
        {
            Debug.LogError($"Creature module Bind Pose mismatch: {moduleId}, " +
                $"bindposes={renderer.sharedMesh.bindposes?.Length ?? 0}, bones={renderer.bones.Length}");
            return false;
        }
        return true;
    }

    static GameObject BuildRigPrefab(
        GameObject source,
        SkinnedMeshRenderer[] renderers,
        string prefabPath)
    {
        var required = new HashSet<Transform>();
        required.Add(source.transform);
        for (int i = 0; i < renderers.Length; i++)
        {
            AddWithAncestors(renderers[i].rootBone, source.transform, required);
            Transform[] bones = renderers[i].bones;
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                AddWithAncestors(bones[boneIndex], source.transform, required);
        }

        var root = new GameObject("RigRoot");
        root.transform.localPosition = source.transform.localPosition;
        root.transform.localRotation = source.transform.localRotation;
        root.transform.localScale = source.transform.localScale;
        CloneRequiredChildren(source.transform, root.transform, required);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static void AddWithAncestors(Transform value, Transform stop, HashSet<Transform> result)
    {
        Transform current = value;
        while (current != null)
        {
            result.Add(current);
            if (current == stop)
                break;
            current = current.parent;
        }
    }

    static void CloneRequiredChildren(Transform source, Transform target, HashSet<Transform> required)
    {
        for (int i = 0; i < source.childCount; i++)
        {
            Transform child = source.GetChild(i);
            if (!required.Contains(child))
                continue;
            var clone = new GameObject(child.name).transform;
            clone.SetParent(target, false);
            clone.localPosition = child.localPosition;
            clone.localRotation = child.localRotation;
            clone.localScale = child.localScale;
            CloneRequiredChildren(child, clone, required);
        }
    }

    static GameObject BuildModulePrefab(
        Transform sourceRoot,
        SkinnedMeshRenderer source,
        string moduleId,
        string prefabPath)
    {
        var root = new GameObject(moduleId);
        Matrix4x4 relative = sourceRoot.worldToLocalMatrix * source.transform.localToWorldMatrix;
        root.transform.localPosition = relative.GetColumn(3);
        root.transform.localRotation = relative.rotation;
        root.transform.localScale = relative.lossyScale;

        SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = source.sharedMesh;
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.localBounds = source.localBounds;
        renderer.quality = source.quality;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        renderer.skinnedMotionVectors = source.skinnedMotionVectors;
        renderer.updateWhenOffscreen = false;

        CreatureSkinnedModuleBinding binding = root.AddComponent<CreatureSkinnedModuleBinding>();
        binding.moduleId = moduleId;
        binding.rootBoneName = source.rootBone != null ? source.rootBone.name : string.Empty;
        binding.boneNames = new string[source.bones.Length];
        for (int i = 0; i < source.bones.Length; i++)
            binding.boneNames[i] = source.bones[i] != null ? source.bones[i].name : string.Empty;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static CreatureRigSemantics BuildSemantics(AntelopeFamilyVariant variant)
    {
        var semantics = new CreatureRigSemantics();
        if (variant == AntelopeFamilyVariant.Biped)
        {
            semantics.legs.Add(Leg("LBLegJNT", "LBKneeJNT", "LBAnkleJNT", "LBFootJNT", true, false, 0f, 0f));
            semantics.legs.Add(Leg("RBLegJNT", "RBKneeJNT", "RBAnkleJNT", "RBFootJNT", false, false, .5f, .5f));
            semantics.longTailBones = new[] { "Tail1Joint", "Tail2Joint", "Tail3Joint", "Tail4Joint", "Tail5Joint", "Tail6Joint" };
            semantics.shortTailBones = Array.Empty<string>();
            semantics.footContactBones = new[] { "LBFootJNT", "RBFootJNT" };
            return semantics;
        }
        if (variant == AntelopeFamilyVariant.Bone)
        {
            semantics.pelvisBone = "RootJNT";
            semantics.legs.Add(Leg("LFShoulderJNT", "LFElbowJNT", "LFWristJNT", "LFFootJNT", true, true, 0f, 0f));
            semantics.legs.Add(Leg("RFShoulderJNT", "RFElbowJNT", "RFWristJNT", "RFFootJNT", false, true, .5f, .5f));
            semantics.legs.Add(Leg("LBHipJNT", "LBKneeJNT", "LBAnkleJNT", "LBFootJNT", true, false, .75f, .5f));
            semantics.legs.Add(Leg("RBHipJNT", "RBKneeJNT", "RBAnkleJNT", "RBFootJNT", false, false, .25f, 0f));
            semantics.longTailBones = new[] { "Tail1JNT", "Tail2JNT", "Tail3JNT", "Tail4JNT", "Tail5JNT" };
            semantics.shortTailBones = Array.Empty<string>();
            semantics.footContactBones = new[] { "LFFootJNT", "RFFootJNT", "LBFootJNT", "RBFootJNT" };
            return semantics;
        }

        semantics.legs.Add(Leg("LF2ShoulderJNT", "LF2ElbowJNT", "LF2WristJNT", "LF2FootJNT", true, true, 0f, 0f));
        semantics.legs.Add(Leg("RF2ShoulderJNT", "RF2ElbowJNT", "RF2WristJNT", "RF2FootJNT", false, true, .5f, .5f));
        semantics.legs.Add(Leg("LBLegJNT", "LBKneeJNT", "LBAnkleJNT", "LBFootJNT", true, false, .75f, .5f));
        semantics.legs.Add(Leg("RBLegJNT", "RBKneeJNT", "RBAnkleJNT", "RBFootJNT", false, false, .25f, 0f));
        return semantics;
    }

    static CreatureSemanticLegDefinition Leg(
        string upper, string lower, string ankle, string foot,
        bool isLeft, bool isFront, float walkPhase, float trotPhase)
    {
        return new CreatureSemanticLegDefinition
        {
            upperBone = upper,
            lowerBone = lower,
            ankleBone = ankle,
            footBone = foot,
            isLeft = isLeft,
            isFront = isFront,
            walkPhase = walkPhase,
            trotPhase = trotPhase
        };
    }

    static void CalibrateSemantics(
        CreatureRigSemantics semantics,
        GameObject source,
        SkinnedMeshRenderer[] renderers)
    {
        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        Transform[] transforms = source.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            if (!bones.ContainsKey(transforms[i].name))
                bones.Add(transforms[i].name, transforms[i]);

        for (int i = 0; i < semantics.legs.Count; i++)
        {
            CreatureSemanticLegDefinition leg = semantics.legs[i];
            if (!bones.TryGetValue(leg.upperBone, out Transform upper)
                || !bones.TryGetValue(leg.lowerBone, out Transform lower)
                || !bones.TryGetValue(leg.ankleBone, out Transform ankle)
                || !bones.TryGetValue(leg.footBone, out Transform foot))
                continue;
            if (lower.parent != upper || ankle.parent != lower || foot.parent != ankle)
            {
                Debug.LogError($"Antelope semantic leg is not a continuous Rest Pose chain: "
                    + $"{leg.upperBone} -> {leg.lowerBone} -> {leg.ankleBone} -> {leg.footBone}", source);
                continue;
            }
            Vector3 upperSegment = lower.position - upper.position;
            Vector3 lowerSegment = ankle.position - lower.position;
            Vector3 ankleSegment = foot.position - ankle.position;
            leg.upperLength = Mathf.Max(.001f, upperSegment.magnitude);
            leg.lowerLength = Mathf.Max(.001f, lowerSegment.magnitude);
            leg.ankleLength = Mathf.Max(.001f, ankleSegment.magnitude);
            leg.upperAxisLocal = upper.InverseTransformDirection(upperSegment.normalized);
            leg.lowerAxisLocal = lower.InverseTransformDirection(lowerSegment.normalized);
            leg.ankleAxisLocal = ankle.InverseTransformDirection(ankleSegment.normalized);

            Vector3 end = foot.position;
            Vector3 line = end - upper.position;
            Vector3 projected = upper.position + Vector3.Project(lower.position - upper.position, line);
            Vector3 bend = lower.position - projected;
            if (bend.sqrMagnitude < .000001f)
                bend = source.transform.forward;
            leg.bendHintRootLocal = source.transform.InverseTransformDirection(bend.normalized);
            leg.footContact = CalculateFootContact(source.transform, renderers, foot, leg);
        }
    }

    static ImportedFootContactProfile CalculateFootContact(
        Transform sourceRoot,
        SkinnedMeshRenderer[] renderers,
        Transform foot,
        CreatureSemanticLegDefinition leg)
    {
        float soleOffset = 0f;
        float halfWidth = 0f;
        float heel = 0f;
        float toe = 0f;
        int samples = 0;
        Vector3 rootUp = sourceRoot.up;
        Vector3 rootRight = sourceRoot.right;
        Vector3 rootForward = sourceRoot.forward;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0)
                continue;
            int footIndex = -1;
            for (int boneIndex = 0; boneIndex < renderer.bones.Length; boneIndex++)
                if (renderer.bones[boneIndex] != null && renderer.bones[boneIndex].name == leg.footBone)
                {
                    footIndex = boneIndex;
                    break;
                }
            if (footIndex < 0)
                continue;

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            for (int vertexIndex = 0; vertexIndex < vertices.Length && vertexIndex < weights.Length; vertexIndex++)
            {
                BoneWeight weight = weights[vertexIndex];
                float influence = 0f;
                if (weight.boneIndex0 == footIndex) influence = Mathf.Max(influence, weight.weight0);
                if (weight.boneIndex1 == footIndex) influence = Mathf.Max(influence, weight.weight1);
                if (weight.boneIndex2 == footIndex) influence = Mathf.Max(influence, weight.weight2);
                if (weight.boneIndex3 == footIndex) influence = Mathf.Max(influence, weight.weight3);
                if (influence < .15f)
                    continue;
                Vector3 world = renderer.transform.TransformPoint(vertices[vertexIndex]);
                Vector3 relative = world - foot.position;
                soleOffset = Mathf.Max(soleOffset, -Vector3.Dot(relative, rootUp));
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(relative, rootRight)));
                float forward = Vector3.Dot(relative, rootForward);
                toe = Mathf.Max(toe, forward);
                heel = Mathf.Max(heel, -forward);
                samples++;
            }
        }

        float totalLength = leg.upperLength + leg.lowerLength;
        return new ImportedFootContactProfile
        {
            soleOffset = samples > 0 ? Mathf.Clamp(soleOffset, .01f, totalLength * .28f) : totalLength * .035f,
            halfWidth = samples > 0 ? Mathf.Clamp(halfWidth, .025f, totalLength * .22f) : totalLength * .06f,
            heelDistance = samples > 0 ? Mathf.Clamp(heel, .025f, totalLength * .25f) : totalLength * .055f,
            toeDistance = samples > 0 ? Mathf.Clamp(toe, .035f, totalLength * .32f) : totalLength * .09f
        };
    }

    static List<CreatureBoneMorphChannel> BuildMorphChannels(AntelopeFamilyVariant variant)
    {
        var channels = new List<CreatureBoneMorphChannel>
        {
            new CreatureBoneMorphChannel
            {
                channelId = "body_mass",
                boneNames = new[] { "Back1JNT", "Back2JNT", "Back3JNT" },
                axis = new Vector3(.35f, .35f, .12f),
                multiplierRange = new Vector2(.94f, 1.06f)
            },
            new CreatureBoneMorphChannel
            {
                channelId = "neck_length",
                boneNames = new[] { "Neck1JNT", "Neck2JNT" },
                axis = new Vector3(0f, .35f, .35f),
                multiplierRange = new Vector2(.94f, 1.06f)
            }
        };
        return channels;
    }

    static void ResolveVariantSettings(
        string source,
        out AntelopeFamilyVariant variant,
        out CreatureTopology topology,
        out float weight)
    {
        topology = CreatureTopology.Quadruped;
        switch (source)
        {
            case "antelopetwolegs":
                variant = AntelopeFamilyVariant.Biped;
                topology = CreatureTopology.Biped;
                weight = 15f;
                return;
            case "antelopeglow":
                variant = AntelopeFamilyVariant.Glow;
                weight = 10f;
                return;
            case "anteloperobot":
                variant = AntelopeFamilyVariant.Robot;
                weight = 10f;
                return;
            case "antelope_bone":
                variant = AntelopeFamilyVariant.Bone;
                weight = 5f;
                return;
            default:
                variant = AntelopeFamilyVariant.Standard;
                weight = 60f;
                return;
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static RuntimeAnimatorController BuildLocomotionController()
    {
        ModelImporter importer = AssetImporter.GetAtPath(AnimationFbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("Missing antelope locomotion FBX: " + AnimationFbxPath);
            return null;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.importConstraints = false;
        importer.SaveAndReimport();

        ModelImporterClipAnimation[] importedClips = importer.defaultClipAnimations;
        for (int i = 0; i < importedClips.Length; i++)
        {
            importedClips[i].loopTime = true;
            importedClips[i].loopPose = true;
        }
        importer.clipAnimations = importedClips;
        importer.SaveAndReimport();

        AnimationClip sourceIdle = FindClip(AnimationFbxPath, "IDLE");
        AnimationClip sourceWalk = FindClip(AnimationFbxPath, "WALK");
        AnimationClip sourceRun = FindClip(AnimationFbxPath, "RUN");
        if (sourceIdle == null || sourceWalk == null || sourceRun == null)
        {
            Debug.LogError($"Antelope animation clips are incomplete. Idle={sourceIdle != null}, Walk={sourceWalk != null}, Run={sourceRun != null}.");
            return null;
        }

        AnimationClip idle = BuildRotationOnlyClip(sourceIdle, RuntimeAnimationFolder + "/AntelopeIdle.anim");
        AnimationClip walk = BuildRotationOnlyClip(sourceWalk, RuntimeAnimationFolder + "/AntelopeWalk.anim");
        AnimationClip run = BuildRotationOnlyClip(sourceRun, RuntimeAnimationFolder + "/AntelopeRun.anim");

        AssetDatabase.DeleteAsset(AnimationControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(AnimationControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState state = stateMachine.AddState("Locomotion");
        stateMachine.defaultState = state;

        var tree = new BlendTree
        {
            name = "AntelopeLocomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, .5f);
        tree.AddChild(run, 1f);
        state.motion = tree;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static AnimationClip BuildRotationOnlyClip(AnimationClip source, string assetPath)
    {
        AssetDatabase.DeleteAsset(assetPath);
        var result = new AnimationClip
        {
            name = Path.GetFileNameWithoutExtension(assetPath),
            frameRate = source.frameRate,
            wrapMode = WrapMode.Loop
        };

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(source);
        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding binding = bindings[i];
            if (!binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal))
                continue;
            AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
            if (curve != null)
                AnimationUtility.SetEditorCurve(result, binding, curve);
        }

        result.EnsureQuaternionContinuity();
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = true;
        settings.loopBlend = true;
        settings.keepOriginalPositionY = true;
        settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(result, settings);
        AssetDatabase.CreateAsset(result, assetPath);
        return result;
    }

    static AnimationClip FindClip(string assetPath, string clipName)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)
                && clip.name.IndexOf(clipName, StringComparison.OrdinalIgnoreCase) >= 0)
                return clip;
        }
        return null;
    }
}
