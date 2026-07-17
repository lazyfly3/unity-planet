using System.Collections.Generic;
using UnityEngine;

public static class CreatureExternalEndpointLibrary
{
    const string ResourceRoot = "Creatures/SporeEndpoints/";
    const float HandVisualMultiplier = 2.5f;
    const float FootVisualMultiplier = 3.1f;
    const float EyeVisualMultiplier = 2.15f;
    const float MouthVisualMultiplier = 2.05f;
    static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

    public static bool HasModel(string endpointId)
    {
return Load(endpointId) != null;
    
    
}

    public static void AttachAvailableModels(
        CreatureGenome genome,
        CreatureRig rig,
        Material sharedMaterial)
    {
if (genome == null || rig == null || rig.graph == null) return;
        for (int i = 0; i < rig.graph.nodes.Count; i++)
        {
            CreatureBodyNode node = rig.graph.nodes[i];
            CreatureLimbKind kind;
            if (node.type == CreatureBodyNodeType.Hand) kind = CreatureLimbKind.Arm;
            else if (node.type == CreatureBodyNodeType.Foot) kind = CreatureLimbKind.Leg;
            else continue;

            CreatureLimbGenome limb = CreatureLimbGenomeGenerator.Find(
                genome, kind, node.symmetryGroup);
            if (limb == null) continue;
            GameObject source = Load(limb.endpointId);
            if (source == null) continue;

            GameObject instance = Object.Instantiate(source, rig.nodeBones[i], false);
            instance.name = "Endpoint_" + limb.endpointId;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            float targetSize = Mathf.Clamp(Mathf.Max(node.size.x, node.size.z), .18f, 1.4f);
            targetSize *= kind == CreatureLimbKind.Leg
                ? FootVisualMultiplier : HandVisualMultiplier;
            CreatureEndpointDefinition definition = CreatureLimbCatalog.FindEndpoint(limb.endpointId, kind);
            if (definition != null && definition.socket != null)
                targetSize *= definition.socket.visualScale;
            ApplyNormalizedScale(instance, Vector3.one * targetSize);
            AlignEndpointConnection(instance, rig.nodeBones[i], definition);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                if (sharedMaterial != null) renderers[rendererIndex].sharedMaterial = sharedMaterial;
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                Object.Destroy(colliders[colliderIndex]);
            if (kind == CreatureLimbKind.Leg)
                ApplyFootContactProfile(rig, rig.nodeBones[i], instance, targetSize);
        }
    
    
}

    public static void AttachHeadOrgans(
        CreatureGenome genome,
        CreatureRig rig,
        Material sharedMaterial)
    {
if (genome == null || rig == null) return;
        if (string.IsNullOrEmpty(genome.eyeOrganId))
            genome.eyeOrganId = (unchecked((uint)genome.seed) & 1u) == 0u ? "eye_stalk" : "eye_bug";
        if (string.IsNullOrEmpty(genome.mouthOrganId))
            genome.mouthOrganId = (unchecked((uint)genome.seed) & 2u) == 0u
                ? "mouth_herbivore" : "mouth_predator";
        Transform faceMount = GetFaceMount(rig);
        if (faceMount == null) return;
        if (!CreatureBodyMorphology.TryGetFaceFrame(genome, out CreatureFaceFrame frame)) return;
        Transform root = rig.armature != null ? rig.armature.parent : faceMount.root;
        CreatureFaceRegion region = genome.faceRegion ?? CreatureBodyMorphology.CreateFaceRegion(genome);
        float faceSize = Mathf.Min(frame.halfWidth, frame.halfHeight) * 2f;
        float eyeScale = Mathf.Clamp(faceSize * .22f * EyeVisualMultiplier, .22f, 1.15f);

        if (genome.eyeCount <= 1)
        {
            AttachFaceOrgan(genome.eyeOrganId, genome, frame, root, faceMount,
                0f, region.eyeHeight, region.organSurfaceOffset,
                Vector3.one * eyeScale * 1.15f, sharedMaterial, "Eye_Center");
        }
        else
        {
            AttachFaceOrgan(genome.eyeOrganId, genome, frame, root, faceMount,
                -region.eyeSpacing * .5f, region.eyeHeight, region.organSurfaceOffset,
                Vector3.one * eyeScale, sharedMaterial, "Eye_Left");
            AttachFaceOrgan(genome.eyeOrganId, genome, frame, root, faceMount,
                region.eyeSpacing * .5f, region.eyeHeight, region.organSurfaceOffset,
                new Vector3(-eyeScale, eyeScale, eyeScale), sharedMaterial, "Eye_Right");
            if (genome.eyeCount >= 4)
            {
                AttachFaceOrgan(genome.eyeOrganId, genome, frame, root, faceMount,
                    -region.eyeSpacing * .3f, region.eyeHeight + .24f, region.organSurfaceOffset,
                    Vector3.one * eyeScale * .7f, sharedMaterial, "Eye_UpperLeft");
                AttachFaceOrgan(genome.eyeOrganId, genome, frame, root, faceMount,
                    region.eyeSpacing * .3f, region.eyeHeight + .24f, region.organSurfaceOffset,
                    new Vector3(-eyeScale, eyeScale, eyeScale) * .7f,
                    sharedMaterial, "Eye_UpperRight");
            }
        }

        float mouthScale = Mathf.Clamp(faceSize * .3f * MouthVisualMultiplier, .34f, 1.35f);
        AttachFaceOrgan(genome.mouthOrganId, genome, frame, root, faceMount,
            0f, region.mouthHeight, region.organSurfaceOffset,
            Vector3.one * mouthScale, sharedMaterial, "Mouth");
    
    
}

    public static void RefreshHeadOrgans(
        CreatureGenome genome, CreatureRig rig, Material sharedMaterial)
    {
Transform mount = GetFaceMount(rig);
        if (mount == null) return;
        for (int i = mount.childCount - 1; i >= 0; i--)
        {
            GameObject child = mount.GetChild(i).gameObject;
            if (!child.name.StartsWith("Eye_") && !child.name.StartsWith("Mouth_"))
                continue;
            if (Application.isPlaying) Object.Destroy(child);
            else Object.DestroyImmediate(child);
        }
        AttachHeadOrgans(genome, rig, sharedMaterial);
    
    
}

    static Transform GetFaceMount(CreatureRig rig)
    {
        return rig.faceSocket != null ? rig.faceSocket : rig.body;
    }

    static Vector3 ToMountPosition(Transform root, Transform mount, Vector3 rootLocalPosition)
    {
        return mount.InverseTransformPoint(root.TransformPoint(rootLocalPosition));
    }

    static Quaternion ToMountRotation(Transform root, Transform mount, Quaternion rootLocalRotation)
    {
        return Quaternion.Inverse(mount.rotation) * root.rotation * rootLocalRotation;
    }

    static void AttachFaceOrgan(
        string organId,
        CreatureGenome genome,
        CreatureFaceFrame frame,
        Transform root,
        Transform mount,
        float horizontal,
        float vertical,
        float surfaceOffset,
        Vector3 scale,
        Material sharedMaterial,
        string instanceName)
    {
        Vector3 point = CreatureBodyMorphology.GetFaceSurfacePoint(
            genome, frame, horizontal, vertical, surfaceOffset);
        Vector3 normal = CreatureBodyMorphology.SampleSurfaceNormal(genome, point);
        Vector3 up = Vector3.ProjectOnPlane(frame.up, normal);
        if (up.sqrMagnitude < .0001f) up = Vector3.ProjectOnPlane(frame.right, normal);
        Quaternion orientation = Quaternion.LookRotation(normal, up.normalized);
        AttachOrgan(organId, mount, ToMountPosition(root, mount, point),
            ToMountRotation(root, mount, orientation), scale, sharedMaterial, instanceName);
    }

    static void AttachOrgan(
        string organId,
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Material sharedMaterial,
        string instanceName)
    {
        GameObject source = Load(organId);
        if (source == null) return;
        GameObject instance = Object.Instantiate(source, parent, false);
        instance.name = instanceName + "_" + organId;
        instance.transform.localPosition = localPosition;
        instance.transform.localRotation = localRotation;
        ApplyNormalizedScale(instance, localScale);
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        Color tint = organId.StartsWith("eye_")
            ? new Color(.92f, .98f, 1f, 1f)
            : organId == "mouth_predator"
                ? new Color(.52f, .12f, .1f, 1f)
                : new Color(.72f, .38f, .26f, 1f);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (sharedMaterial != null) renderers[i].sharedMaterial = sharedMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_Color", tint);
            renderers[i].SetPropertyBlock(properties);
        }
        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) Object.Destroy(colliders[i]);
    }

    static void ApplyNormalizedScale(GameObject instance, Vector3 requestedSize)
    {
        Vector3 signs = new Vector3(
            requestedSize.x < 0f ? -1f : 1f,
            requestedSize.y < 0f ? -1f : 1f,
            requestedSize.z < 0f ? -1f : 1f);
        Vector3 absoluteSize = new Vector3(
            Mathf.Abs(requestedSize.x),
            Mathf.Abs(requestedSize.y),
            Mathf.Abs(requestedSize.z));
        float targetMaximum = Mathf.Max(absoluteSize.x, absoluteSize.y, absoluteSize.z);
        instance.transform.localScale = Vector3.one;
        if (targetMaximum <= .0001f || !TryCalculateLocalBounds(instance, out Bounds bounds))
        {
            instance.transform.localScale = Vector3.Scale(signs, absoluteSize);
            return;
        }

        float sourceMaximum = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (sourceMaximum <= .0001f)
        {
            instance.transform.localScale = Vector3.Scale(signs, absoluteSize);
            return;
        }

        float uniform = targetMaximum / sourceMaximum;
        Vector3 proportions = absoluteSize / targetMaximum;
        instance.transform.localScale = Vector3.Scale(signs, proportions * uniform);
    }

    static void ApplyFootContactProfile(
        CreatureRig rig, Transform footBone, GameObject instance, float fallbackSize)
    {
        if (rig.legs == null) return;
        CreatureFootContactProfile profile = TryCalculateBoundsRelativeTo(instance, footBone, out Bounds bounds)
            ? CreateFootProfile(bounds)
            : CreatureFootContactProfile.CreateFallback(fallbackSize);
        for (int i = 0; i < rig.legs.Length; i++)
        {
            CreatureLegRig leg = rig.legs[i];
            if (leg.foot != footBone) continue;
            leg.footContact = profile;
            leg.footSoleOffset = profile.soleOffset;
            return;
        }
    }

    static void AlignEndpointConnection(
        GameObject instance, Transform endpointBone, CreatureEndpointDefinition definition)
    {
        if (instance == null || endpointBone == null || definition == null || definition.socket == null)
            return;
        if (!TryCalculateBoundsRelativeTo(instance, endpointBone, out Bounds bounds)) return;
        // Imported endpoint meshes are centered around their origin. Move the
        // upper connection plane into the wrist/ankle transition instead of
        // attaching the middle of the model to the end bone.
        float inset = Mathf.Clamp(definition.socket.surfaceInset, .015f, bounds.size.y * .35f);
        float shift = inset - bounds.max.y;
        instance.transform.localPosition += Vector3.up * shift;
    }

    static CreatureFootContactProfile CreateFootProfile(Bounds bounds)
    {
        float padding = Mathf.Max(.012f, bounds.size.y * .018f);
        float soleY = bounds.min.y;
        float soleOffset = Mathf.Max(.015f, -soleY + padding);
        float x = Mathf.Max(.025f, bounds.extents.x * .72f);
        float heel = bounds.min.z + bounds.size.z * .16f;
        float toe = bounds.max.z - bounds.size.z * .12f;
        Vector3 heelPoint = new Vector3(0f, soleY, heel);
        Vector3 toePoint = new Vector3(0f, soleY, toe);
        Vector3 leftPoint = new Vector3(-x, soleY, bounds.center.z);
        Vector3 rightPoint = new Vector3(x, soleY, bounds.center.z);
        return new CreatureFootContactProfile
        {
            soleOffset = soleOffset,
            heelPoint = heelPoint,
            toePoint = toePoint,
            leftPoint = leftPoint,
            rightPoint = rightPoint,
            soleSamples = new[] { heelPoint, toePoint, leftPoint, rightPoint }
        };
    }

    static bool TryCalculateBoundsRelativeTo(GameObject instance, Transform relativeTo, out Bounds result)
    {
        result = default;
        bool initialized = false;
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Bounds sourceBounds;
            Matrix4x4 sourceToWorld;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                sourceBounds = skinned.localBounds;
                sourceToWorld = skinned.transform.localToWorldMatrix;
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                sourceBounds = filter.sharedMesh.bounds;
                sourceToWorld = filter.transform.localToWorldMatrix;
            }
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = new Vector3(
                    (corner & 1) == 0 ? sourceBounds.min.x : sourceBounds.max.x,
                    (corner & 2) == 0 ? sourceBounds.min.y : sourceBounds.max.y,
                    (corner & 4) == 0 ? sourceBounds.min.z : sourceBounds.max.z);
                Vector3 point = relativeTo.InverseTransformPoint(sourceToWorld.MultiplyPoint3x4(localCorner));
                if (!initialized)
                {
                    result = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else result.Encapsulate(point);
            }
        }
        return initialized;
    }

    static bool TryCalculateLocalBounds(GameObject instance, out Bounds result)
    {
        result = default;
        bool initialized = false;
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            Bounds sourceBounds;
            Matrix4x4 sourceToWorld;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                sourceBounds = skinned.localBounds;
                sourceToWorld = skinned.transform.localToWorldMatrix;
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                sourceBounds = filter.sharedMesh.bounds;
                sourceToWorld = filter.transform.localToWorldMatrix;
            }

            Vector3 minimum = sourceBounds.min;
            Vector3 maximum = sourceBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 localCorner = new Vector3(
                    (corner & 1) == 0 ? minimum.x : maximum.x,
                    (corner & 2) == 0 ? minimum.y : maximum.y,
                    (corner & 4) == 0 ? minimum.z : maximum.z);
                Vector3 instanceLocal = instance.transform.InverseTransformPoint(
                    sourceToWorld.MultiplyPoint3x4(localCorner));
                if (!initialized)
                {
                    result = new Bounds(instanceLocal, Vector3.zero);
                    initialized = true;
                }
                else result.Encapsulate(instanceLocal);
            }
        }
        return initialized;
    }

    static GameObject Load(string endpointId)
    {
        if (string.IsNullOrEmpty(endpointId)) return null;
        if (Cache.TryGetValue(endpointId, out GameObject cached)) return cached;
        GameObject loaded = Resources.Load<GameObject>(ResourceRoot + endpointId);
        Cache[endpointId] = loaded;
        return loaded;
    }
}
