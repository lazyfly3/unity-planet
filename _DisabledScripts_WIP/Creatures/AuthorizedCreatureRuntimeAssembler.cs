using UnityEngine;

public static class AuthorizedCreatureRuntimeAssembler
{
    public static GameObject BuildNaturalQuadruped(
        GameObject visualPrefab,
        CreatureGenome genome,
        Transform parent,
        SphericalGravitySource gravitySource,
        LayerMask groundLayers,
        Vector3 surfacePoint,
        Quaternion rotation,
        float visualScale)
    {
if (visualPrefab == null)
            return null;

        var root = new GameObject($"NaturalSciFiCreature_{genome.seed}");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(surfacePoint, rotation);

        GameObject visual = Object.Instantiate(visualPrefab, root.transform, false);
        visual.name = "AuthorizedVisual";
        visual.transform.localPosition = Vector3.zero;
        // The imported creature faces local -Z, while CreatureSphereMotor moves along root +Z.
        visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        visual.transform.localScale = Vector3.one * Mathf.Max(.01f, visualScale);
        ApplyImportedPalette(visual, genome);

        Animator[] importedAnimators = visual.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < importedAnimators.Length; i++)
            importedAnimators[i].enabled = false;

        Bounds bounds = CalculateLocalRendererBounds(root.transform, visual);
        float soleY = FindFootSoleLocalY(root.transform, visual, bounds.min.y);
        float rootToSole = -soleY;
        float groundProbeDistance = Mathf.Max(.1f, rootToSole);
        float horizontalRadius = Mathf.Max(.2f, Mathf.Min(bounds.extents.x, bounds.extents.z) * .62f);
        float height = Mathf.Max(
            horizontalRadius * 2f,
            Mathf.Min(bounds.size.y * .68f, groundProbeDistance * 1.25f));

        Rigidbody rigidbody = root.AddComponent<Rigidbody>();
        rigidbody.mass = Mathf.Clamp(bounds.size.magnitude * 7f, 15f, 180f);
        rigidbody.useGravity = false;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.constraints = RigidbodyConstraints.FreezeRotation;

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.center = new Vector3(bounds.center.x, soleY + height * .5f, bounds.center.z);
        capsule.radius = horizontalRadius;
        capsule.height = height;

        root.transform.position = surfacePoint + rotation * Vector3.up * (rootToSole + .025f);

        CreatureSphereMotor motor = root.AddComponent<CreatureSphereMotor>();
        motor.Configure(
            gravitySource,
            rigidbody,
            capsule,
            groundLayers,
            7.2f,
            22f,
            16f,
            Vector3.forward,
            groundProbeDistance,
            true);

        ImportedCreatureSemanticAnimator semanticAnimator = root.AddComponent<ImportedCreatureSemanticAnimator>();
        semanticAnimator.Configure(
            rigidbody,
            motor,
            gravitySource,
            groundLayers,
            genome != null ? genome.gaitFrequency : 1.5f,
            7.2f,
            null);

        return root;
}

    static float FindFootSoleLocalY(Transform root, GameObject visual, float fallbackY)
    {
        string[] footBoneNames =
        {
            "LF2FootJNT", "RF2FootJNT",
            "LBFootJNT", "RBFootJNT",
            "LBToeJNT", "RBToeJNT"
        };
        Transform[] transforms = visual.GetComponentsInChildren<Transform>(true);
        float lowest = float.PositiveInfinity;
        int found = 0;
        for (int i = 0; i < transforms.Length; i++)
        {
            for (int nameIndex = 0; nameIndex < footBoneNames.Length; nameIndex++)
            {
                if (transforms[i].name != footBoneNames[nameIndex])
                    continue;
                lowest = Mathf.Min(lowest, root.InverseTransformPoint(transforms[i].position).y);
                found++;
                break;
            }
        }

        // Bone origins sit slightly above the rendered hoof sole.
        return found >= 4 ? lowest - .055f : fallbackY;
    }

    static void ApplyImportedPalette(GameObject visual, CreatureGenome genome)
    {
        Color primary = genome != null ? genome.primaryColor : new Color(.28f, .48f, .62f, 1f);
        Color secondary = genome != null ? genome.secondaryColor : new Color(.18f, .3f, .42f, 1f);
        Color headTint = Color.Lerp(new Color(.22f, .38f, .54f, 1f), primary, .42f);
        Color mouthTint = Color.Lerp(new Color(.42f, .12f, .12f, 1f), secondary, .18f);
        Color accentTint = Color.Lerp(new Color(.16f, .48f, .64f, 1f), secondary, .35f);

        var block = new MaterialPropertyBlock();
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                    continue;

                Color tint;
                string materialName = material.name;
                if (materialName.Contains("AntelopeHeadMat"))
                    tint = headTint;
                else if (materialName.Contains("Gums_mat"))
                    tint = mouthTint;
                else if (materialName.Contains("SpikeFin") || materialName.Contains("Spores"))
                    tint = accentTint;
                else
                    continue;

                renderer.GetPropertyBlock(block, materialIndex);
                block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block, materialIndex);
                block.Clear();
            }
        }
    }

    static Bounds CalculateLocalRendererBounds(Transform root, GameObject visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(new Vector3(0f, -.8f, 0f), new Vector3(1.5f, 2f, 2.5f));

        bool initialized = false;
        Bounds localBounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Bounds world = renderers[i].bounds;
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 local = root.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(local);
                }
            }
        }
        return localBounds;
    }
}
