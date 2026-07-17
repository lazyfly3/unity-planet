using System;
using System.Collections.Generic;
using UnityEngine;

public static class DescriptorCreatureRuntimeAssembler
{
    public static GameObject Build(
        CreatureRigFamilyDefinition family,
        GeneratedCreatureSpeciesDefinition species,
        CreatureGenome genome,
        Transform parent,
        SphericalGravitySource gravitySource,
        LayerMask groundLayers,
        Vector3 surfacePoint,
        Quaternion rotation,
        bool enableMorphology)
    {
if (family == null || species == null || gravitySource == null)
            return null;

        CreatureFamilyVariantDefinition variant = family.FindVariant(species.variantId);
        if (variant == null || variant.rigPrefab == null)
            return BuildFallback(variant, genome, parent, gravitySource, groundLayers, surfacePoint, rotation);

        var root = new GameObject($"DescriptorCreature_{species.seed}");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(surfacePoint, rotation);

        var visualRoot = new GameObject("DescriptorVisual").transform;
        visualRoot.SetParent(root.transform, false);
        visualRoot.localRotation = family.visualRotation;
        // The authorized FBX modules and their armature roots carry the same import
        // scale (100 for the Antelope family). Keep that scale inside the visual
        // hierarchy so bind poses still match, then cancel it at the common parent.
        // Without this cancellation a 1.2 m leg became 120 m and the creature was
        // spawned farther from the center than the planet radius.
        float importScale = Mathf.Max(.0001f, family.rigImportScale);
        visualRoot.localScale = Vector3.one * (Mathf.Max(.01f, species.uniformScale) / importScale);

        GameObject rig = UnityEngine.Object.Instantiate(variant.rigPrefab, visualRoot, false);
        rig.name = "Rig";
        rig.transform.localScale = Vector3.one * importScale;
        DisableRigRenderers(rig);
        Dictionary<string, Transform> bones = BuildTransformMap(rig.transform);
        if (bones.Count == 0)
        {
            UnityEngine.Object.Destroy(root);
            return BuildFallback(variant, genome, parent, gravitySource, groundLayers, surfacePoint, rotation);
        }

        if (enableMorphology)
            ApplyMorphology(variant, species, bones);

        Dictionary<string, CreatureModuleDefinition> modules = BuildModuleMap(variant.modules);
        int moduleCount = 0;
        int rendererCount = 0;
        int vertexCount = 0;
        bool missingRequired = false;
        for (int i = 0; i < species.selectedModuleIds.Count; i++)
        {
            if (moduleCount >= family.maxVisibleModules || rendererCount >= family.maxRenderers)
                break;

            string moduleId = species.selectedModuleIds[i];
            if (!modules.TryGetValue(moduleId, out CreatureModuleDefinition module) || module == null || module.prefab == null)
            {
                if (module != null && module.required)
                    missingRequired = true;
                continue;
            }

            if (!HasRequiredBones(module, bones))
            {
                if (module.required)
                    missingRequired = true;
                Debug.LogWarning($"Creature module '{moduleId}' is missing a required bone and was skipped.");
                continue;
            }

            GameObject instance = UnityEngine.Object.Instantiate(module.prefab, visualRoot, false);
            instance.name = "Module_" + moduleId;
            if (!RebindModule(instance, bones, ref rendererCount, ref vertexCount))
            {
                UnityEngine.Object.Destroy(instance);
                if (module.required)
                    missingRequired = true;
                continue;
            }
            ApplyPalette(instance, species, variant.variant);
            moduleCount++;
        }

        if (missingRequired || rendererCount == 0)
        {
            UnityEngine.Object.Destroy(root);
            return BuildFallback(variant, genome, parent, gravitySource, groundLayers, surfacePoint, rotation);
        }

        Animator animator = null;
        if (family.locomotionController != null)
        {
            animator = rig.AddComponent<Animator>();
            animator.runtimeAnimatorController = family.locomotionController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.Rebind();
            animator.Update(0f);
            // The imported NMS clips use a different joint rotation basis. Keep the
            // evaluated idle pose as a stable rest pose, then let the sphere-aware
            // procedural gait rotate the existing chains without changing bone length.
            animator.enabled = false;
        }

        Bounds bounds = CalculateLocalRendererBounds(root.transform, visualRoot.gameObject);
        float soleY = FindFootSoleLocalY(root.transform, bones, variant.semantics, bounds);
        float rootToSole = Mathf.Max(.1f, -soleY);
        float horizontalRadius = Mathf.Max(.2f, Mathf.Min(bounds.extents.x, bounds.extents.z) * .58f);
        float height = Mathf.Max(horizontalRadius * 2f, Mathf.Min(bounds.size.y * .7f, rootToSole * 1.3f));

        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = Mathf.Clamp(bounds.size.magnitude * 7f, 15f, 190f);
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.center = new Vector3(bounds.center.x, soleY + height * .5f, bounds.center.z);
        capsule.radius = horizontalRadius;
        capsule.height = height;

        root.transform.position = surfacePoint + rotation * Vector3.up * (rootToSole + .025f);

        // Imported families have very different physical sizes. A fixed 7.2 m/s
        // makes short-legged variants travel a whole leg length during one stance.
        float locomotionSpeed = Mathf.Clamp(rootToSole * 2.35f, 1.6f, 4.2f);
        CreatureSphereMotor motor = root.AddComponent<CreatureSphereMotor>();
        motor.Configure(gravitySource, body, capsule, groundLayers, locomotionSpeed, 22f, 16f,
            Vector3.forward, rootToSole, true);

        ImportedCreatureSemanticAnimator semanticAnimator = root.AddComponent<ImportedCreatureSemanticAnimator>();
        semanticAnimator.Configure(
            body,
            motor,
            gravitySource,
            groundLayers,
            genome != null ? genome.gaitFrequency : 1.5f,
            locomotionSpeed,
            variant.semantics);

        Debug.Log(
            $"Descriptor creature generated. Family={family.stableId}, Variant={variant.stableId}, " +
            $"Seed={species.seed}, Modules={moduleCount}, Renderers={rendererCount}, Vertices={vertexCount}, " +
            $"RootToSole={rootToSole:F2}, Capsule={capsule.height:F2}x{capsule.radius:F2}, " +
            $"Signature={species.BuildSignature()}", root);
        return root;
}

    static GameObject BuildFallback(
        CreatureFamilyVariantDefinition variant,
        CreatureGenome genome,
        Transform parent,
        SphericalGravitySource gravitySource,
        LayerMask groundLayers,
        Vector3 surfacePoint,
        Quaternion rotation)
    {
        if (variant == null || variant.safeFallbackPrefab == null)
            return null;
        return AuthorizedCreatureRuntimeAssembler.BuildNaturalQuadruped(
            variant.safeFallbackPrefab, genome, parent, gravitySource, groundLayers,
            surfacePoint, rotation, 1f);
    }

    static void DisableRigRenderers(GameObject rig)
    {
        Renderer[] renderers = rig.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = false;
        Animator[] animators = rig.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            animators[i].enabled = false;
    }

    static Dictionary<string, Transform> BuildTransformMap(Transform root)
    {
        var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            if (!result.ContainsKey(transforms[i].name))
                result.Add(transforms[i].name, transforms[i]);
        return result;
    }

    static Dictionary<string, CreatureModuleDefinition> BuildModuleMap(List<CreatureModuleDefinition> source)
    {
        var result = new Dictionary<string, CreatureModuleDefinition>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            CreatureModuleDefinition module = source[i];
            if (module != null && !string.IsNullOrEmpty(module.stableId) && !result.ContainsKey(module.stableId))
                result.Add(module.stableId, module);
        }
        return result;
    }

    static bool HasRequiredBones(CreatureModuleDefinition module, Dictionary<string, Transform> bones)
    {
        if (module.requiredBones == null)
            return true;
        for (int i = 0; i < module.requiredBones.Length; i++)
            if (!string.IsNullOrEmpty(module.requiredBones[i]) && !bones.ContainsKey(module.requiredBones[i]))
                return false;
        return true;
    }

    static bool RebindModule(
        GameObject module,
        Dictionary<string, Transform> bones,
        ref int rendererCount,
        ref int vertexCount)
    {
        CreatureSkinnedModuleBinding binding = module.GetComponent<CreatureSkinnedModuleBinding>();
        SkinnedMeshRenderer renderer = module.GetComponent<SkinnedMeshRenderer>();
        if (binding == null || renderer == null || renderer.sharedMesh == null || binding.boneNames == null)
            return false;

        var rebound = new Transform[binding.boneNames.Length];
        for (int i = 0; i < rebound.Length; i++)
        {
            if (!bones.TryGetValue(binding.boneNames[i], out rebound[i]))
            {
                Debug.LogWarning($"Module '{binding.moduleId}' could not bind bone '{binding.boneNames[i]}'.");
                return false;
            }
        }
        renderer.bones = rebound;
        if (!string.IsNullOrEmpty(binding.rootBoneName) && bones.TryGetValue(binding.rootBoneName, out Transform rootBone))
            renderer.rootBone = rootBone;
        renderer.enabled = true;
        // Animated module renderers are siblings of the rig, so Unity cannot derive
        // their visibility from the Animator hierarchy. Keep skinning and bounds
        // updates active or the first animated pose can cull the whole creature.
        renderer.updateWhenOffscreen = true;
        rendererCount++;
        vertexCount += renderer.sharedMesh.vertexCount;
        return true;
    }

    static void ApplyMorphology(
        CreatureFamilyVariantDefinition variant,
        GeneratedCreatureSpeciesDefinition species,
        Dictionary<string, Transform> bones)
    {
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        for (int i = 0; i < species.morphValues.Count; i++)
            values[species.morphValues[i].channelId] = Mathf.Clamp(species.morphValues[i].multiplier, .85f, 1.15f);

        for (int i = 0; i < variant.morphChannels.Count; i++)
        {
            CreatureBoneMorphChannel channel = variant.morphChannels[i];
            if (channel == null || !values.TryGetValue(channel.channelId, out float multiplier))
                continue;
            Vector3 scale = Vector3.one + Vector3.Scale(channel.axis, Vector3.one * (multiplier - 1f));
            for (int boneIndex = 0; boneIndex < channel.boneNames.Length; boneIndex++)
                if (bones.TryGetValue(channel.boneNames[boneIndex], out Transform bone))
                    bone.localScale = Vector3.Scale(bone.localScale, scale);
        }
    }

    static void ApplyPalette(GameObject module, GeneratedCreatureSpeciesDefinition species, AntelopeFamilyVariant variant)
    {
        Color primary = species.primaryColor;
        Color secondary = species.secondaryColor;
        if (variant == AntelopeFamilyVariant.Robot)
        {
            primary = Color.Lerp(primary, new Color(.28f, .34f, .38f, 1f), .62f);
            secondary = Color.Lerp(secondary, new Color(.12f, .72f, .9f, 1f), .35f);
        }
        else if (variant == AntelopeFamilyVariant.Bone)
        {
            primary = Color.Lerp(new Color(.78f, .72f, .58f, 1f), primary, .12f);
            secondary = Color.Lerp(new Color(.35f, .2f, .12f, 1f), secondary, .15f);
        }

        var block = new MaterialPropertyBlock();
        Renderer[] renderers = module.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", Color.Lerp(Color.white, primary, .48f));
            block.SetColor("_PatternColor", secondary);
            if (variant == AntelopeFamilyVariant.Glow)
            {
                block.SetColor("_EmissionColor", Color.Lerp(secondary, Color.white, .2f));
                block.SetFloat("_EmissionStrength", .65f);
            }
            renderer.SetPropertyBlock(block);
        }
    }

    static Bounds CalculateLocalRendererBounds(Transform root, GameObject visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        Bounds result = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled)
                continue;
            Bounds bounds = renderers[i].bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 world = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 local = root.InverseTransformPoint(world);
                if (!initialized)
                {
                    result = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(local);
                }
            }
        }
        return initialized ? result : new Bounds(Vector3.zero, Vector3.one);
    }

    static float FindFootSoleLocalY(
        Transform root,
        Dictionary<string, Transform> bones,
        CreatureRigSemantics semantics,
        Bounds rendererBounds)
    {
        float fallback = rendererBounds.min.y;
        if (semantics == null || semantics.footContactBones == null)
            return fallback;
        float lowest = float.PositiveInfinity;
        int found = 0;
        for (int i = 0; i < semantics.footContactBones.Length; i++)
        {
            if (!bones.TryGetValue(semantics.footContactBones[i], out Transform bone))
                continue;
            lowest = Mathf.Min(lowest, root.InverseTransformPoint(bone.position).y);
            found++;
        }
        if (found == 0)
            return fallback;

        float boneSole = lowest - .055f;
        float tolerance = Mathf.Max(.15f, rendererBounds.size.y * .2f);
        if (boneSole >= -.05f || Mathf.Abs(boneSole - fallback) > tolerance)
            return fallback;
        return boneSole;
    }
}
