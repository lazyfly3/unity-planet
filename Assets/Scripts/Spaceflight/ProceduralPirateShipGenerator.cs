using System;
using System.Collections.Generic;
using System.Text;
using SpacecraftEditor;
using UnityEngine;

[Serializable]
public sealed class ProceduralPirateBlueprint
{
    public int generatorVersion = 1;
    public int seed;
    public int tier;
    public string signature;
    public SpacecraftBlueprintData spacecraft = new SpacecraftBlueprintData();
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ProceduralPirateShipGenerator : MonoBehaviour
{
    [SerializeField] HullCatalog hullCatalog;
    [SerializeField] PartCatalog partCatalog;
    [SerializeField] SpacecraftMaterialCatalog materialCatalog;
    [SerializeField] SpacecraftHardpointLayout[] layouts = Array.Empty<SpacecraftHardpointLayout>();
    [SerializeField] ShipHullController hullController;
    [SerializeField] ShipAssembly assembly;
    [SerializeField] SpacecraftIfcsMotor ifcsMotor;
    [SerializeField] SpacecraftWeaponSystem weaponSystem;
    [SerializeField] PirateCommandBuffer commandBuffer;
    [SerializeField] bool generateOnStart;
    [SerializeField] int previewSeed = 7319;
    [SerializeField, Range(1, 5)] int previewTier = 1;

    readonly List<ShipPartDefinition> thrusterCandidates = new List<ShipPartDefinition>(8);
    readonly List<ShipPartDefinition> weaponCandidates = new List<ShipPartDefinition>(16);
    readonly List<ShipPartDefinition> decorationCandidates = new List<ShipPartDefinition>(16);

    public ProceduralPirateBlueprint CurrentBlueprint { get; private set; }
    public bool UsedSafeFallback { get; private set; }
    public string LastValidationError { get; private set; } = string.Empty;

    void Awake()
    {
        ResolveReferences();
    }

    void Start()
    {
        if (generateOnStart)
            Generate(previewSeed, previewTier);
    }

    public bool Generate(int seed, int tier = 1)
    {
        ResolveReferences();
        UsedSafeFallback = false;
        LastValidationError = string.Empty;
        if (!TryBuildBlueprint(seed, tier, out ProceduralPirateBlueprint generated, out string error))
        {
            Debug.LogError($"Pirate blueprint generation failed for seed {seed}: {error}", this);
            return false;
        }

        string visualError = string.Empty;
        bool generatedApplied = ApplyBlueprint(generated, out error);
        if (generatedApplied && ValidateCurrentVisuals(out visualError))
        {
            CurrentBlueprint = generated;
            return true;
        }

        LastValidationError = generatedApplied ? visualError : error;
        SpacecraftHardpointLayout fallbackLayout = FindFirstValidLayout();
        string fallbackError = string.Empty;
        string fallbackVisualError = string.Empty;
        if (fallbackLayout == null ||
            !BuildSafeFallback(seed, tier, fallbackLayout, out ProceduralPirateBlueprint fallback, out fallbackError) ||
            !ApplyBlueprint(fallback, out fallbackError) ||
            !ValidateCurrentVisuals(out fallbackVisualError))
        {
            if (!string.IsNullOrEmpty(fallbackVisualError))
                fallbackError = fallbackVisualError;
            LastValidationError = $"Generated: {LastValidationError} Fallback: {fallbackError}";
            Debug.LogError($"Pirate visual validation failed for seed {seed}: {LastValidationError}", this);
            return false;
        }

        UsedSafeFallback = true;
        CurrentBlueprint = fallback;
        Debug.LogWarning($"Pirate seed {seed} used its safe blueprint after visual validation failed: {LastValidationError}", this);
        return true;
    }

    public bool TryBuildBlueprint(
        int seed,
        int tier,
        out ProceduralPirateBlueprint result,
        out string error)
    {
        result = null;
        error = string.Empty;
        ResolveReferences();
        if (!BuildCandidateLists(out error) || layouts == null || layouts.Length == 0)
            return false;

        tier = Mathf.Clamp(tier, 1, 5);
        for (int attempt = 0; attempt < 16; attempt++)
        {
            var random = new StableRandom64(CombineSeed(seed, attempt));
            SpacecraftHardpointLayout layout = SelectValidLayout(ref random);
            if (layout == null)
            {
                error = "No hardpoint layout matches a published hull.";
                return false;
            }
            if (TryBuildForLayout(seed, tier, layout, ref random, out result, out error))
                return true;
        }

        SpacecraftHardpointLayout fallback = FindFirstValidLayout();
        if (fallback == null)
        {
            error = "No valid fallback layout is available.";
            return false;
        }
        return BuildSafeFallback(seed, tier, fallback, out result, out error);
    }

    bool TryBuildForLayout(
        int seed,
        int tier,
        SpacecraftHardpointLayout layout,
        ref StableRandom64 random,
        out ProceduralPirateBlueprint result,
        out string error)
    {
        result = null;
        error = string.Empty;
        ShipHullDefinition hull = hullCatalog.Find(layout.HullId);
        string paint = SelectPaint(ref random);
        var parts = new List<PlacedPartState>(16);

        ShipPartDefinition thruster = thrusterCandidates[random.NextInt(thrusterCandidates.Count)];
        AddHardpointPairs(parts, layout, SpacecraftPartCategory.Thruster, thruster, paint, 0, ref random, false);

        ShipPartDefinition weapon = SelectWeapon(layout, tier, ref random);
        if (weapon == null)
        {
            error = "No compatible weapon exists for the selected hardpoints.";
            return false;
        }
        AddHardpointPairs(parts, layout, SpacecraftPartCategory.Weapon, weapon, paint,
            weapon.Weapon == null ? 1 : weapon.Weapon.DefaultFireGroup, ref random, true);

        AddDecorations(parts, layout, paint, ref random);
        if (!ValidateBlueprint(hull, layout, parts, out error))
            return false;

        result = CreateResult(seed, tier, hull, paint, parts);
        return true;
    }

    bool BuildSafeFallback(
        int seed,
        int tier,
        SpacecraftHardpointLayout layout,
        out ProceduralPirateBlueprint result,
        out string error)
    {
        result = null;
        error = string.Empty;
        ShipHullDefinition hull = hullCatalog.Find(layout.HullId);
        ShipPartDefinition thruster = partCatalog.Find("thruster.medium") ?? thrusterCandidates[0];
        ShipPartDefinition weapon = FindFirstCompatibleWeapon(layout) ?? weaponCandidates[0];
        var parts = new List<PlacedPartState>(12);
        var random = new StableRandom64(CombineSeed(seed, 0x53414645));
        string paint = materialCatalog == null ? "paint.gunmetal" : materialCatalog.DefaultMaterialId;
        AddHardpointPairs(parts, layout, SpacecraftPartCategory.Thruster, thruster, paint, 0, ref random, false);
        AddHardpointPairs(parts, layout, SpacecraftPartCategory.Weapon, weapon, paint,
            weapon.Weapon == null ? 1 : weapon.Weapon.DefaultFireGroup, ref random, true);
        if (!ValidateBlueprint(hull, layout, parts, out error))
            return false;
        result = CreateResult(seed, tier, hull, paint, parts);
        return true;
    }

    ProceduralPirateBlueprint CreateResult(
        int seed,
        int tier,
        ShipHullDefinition hull,
        string paint,
        List<PlacedPartState> parts)
    {
        var result = new ProceduralPirateBlueprint
        {
            seed = seed,
            tier = tier,
            spacecraft = new SpacecraftBlueprintData
            {
                hullId = hull.HullId,
                hullMaterialId = paint,
                parts = parts.ToArray(),
                savedUtcTicks = 0
            }
        };
        result.signature = BuildSignature(result);
        return result;
    }

    void AddHardpointPairs(
        List<PlacedPartState> parts,
        SpacecraftHardpointLayout layout,
        SpacecraftPartCategory category,
        ShipPartDefinition definition,
        string paint,
        int group,
        ref StableRandom64 random,
        bool onePairOnly)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        SpacecraftHardpoint[] points = layout.Hardpoints;
        for (int index = 0; index < points.Length; index++)
        {
            SpacecraftHardpoint point = points[index];
            if (point == null || point.Category != category || used.Contains(point.HardpointId) ||
                !point.Accepts(definition))
                continue;
            SpacecraftHardpoint mirror = FindHardpoint(points, point.MirrorHardpointId);
            if (mirror == null || !mirror.Accepts(definition))
                continue;
            string mirrorGroup = "pcg_" + category + "_" + point.HardpointId;
            float scale = definition.IsScalable
                ? Mathf.Lerp(definition.MinimumScale, definition.MaximumScale, random.NextFloat())
                : definition.FixedScale;
            parts.Add(CreatePartState(definition, point, scale, paint, group, mirrorGroup));
            parts.Add(CreatePartState(definition, mirror, scale, paint, group, mirrorGroup));
            used.Add(point.HardpointId);
            used.Add(mirror.HardpointId);
            if (onePairOnly)
                return;
        }
    }

    void AddDecorations(
        List<PlacedPartState> parts,
        SpacecraftHardpointLayout layout,
        string paint,
        ref StableRandom64 random)
    {
        if (decorationCandidates.Count == 0 || layout.DecorationRegions == null)
            return;
        int pairCount = random.NextInt(0, Mathf.Min(3, layout.DecorationRegions.Length) + 1);
        for (int index = 0; index < pairCount; index++)
        {
            SpacecraftDecorationRegion region = layout.DecorationRegions[index % layout.DecorationRegions.Length];
            ShipPartDefinition definition = decorationCandidates[random.NextInt(decorationCandidates.Count)];
            Vector3 jitter = new Vector3(
                random.NextSignedFloat() * region.LocalExtents.x,
                random.NextSignedFloat() * region.LocalExtents.y,
                random.NextSignedFloat() * region.LocalExtents.z);
            Vector3 left = region.LocalCenter + jitter;
            left.x = -Mathf.Abs(left.x);
            Vector3 right = left;
            right.x = -left.x;
            string mirrorGroup = "pcg_decor_" + index;
            float scale = definition.IsScalable
                ? Mathf.Lerp(definition.MinimumScale, definition.MaximumScale, random.NextFloat())
                : definition.FixedScale;
            parts.Add(CreatePartState(definition, left, region.LocalRotation, scale, paint, 0,
                mirrorGroup, "decor_l_" + index));
            parts.Add(CreatePartState(definition, right, MirrorRotation(region.LocalRotation), scale, paint, 0,
                mirrorGroup, "decor_r_" + index));
        }
    }

    bool ValidateBlueprint(
        ShipHullDefinition hull,
        SpacecraftHardpointLayout layout,
        List<PlacedPartState> parts,
        out string error)
    {
        error = string.Empty;
        if (hull == null || parts.Count < 4)
        {
            error = "The hull or required paired parts are missing.";
            return false;
        }
        float mass = hull.BaseMass;
        float thrust = 0f;
        float lateralMoment = 0f;
        int thrusterCount = 0;
        int weaponCount = 0;
        var occupiedHardpoints = new HashSet<string>(StringComparer.Ordinal);
        var functionalMirrorGroups = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < parts.Count; index++)
        {
            PlacedPartState state = parts[index];
            ShipPartDefinition definition = partCatalog.Find(state.partId);
            if (definition == null)
            {
                error = "A generated part is not present in the catalog: " + state.partId;
                return false;
            }
            float partMass = definition.BaseMass * state.uniformScale * state.uniformScale * state.uniformScale;
            mass += partMass;
            lateralMoment += state.localPosition.x * partMass;
            if (definition.Category == SpacecraftPartCategory.Thruster)
            {
                if (!ValidateFunctionalHardpoint(layout, state, definition, occupiedHardpoints, out error))
                    return false;
                Vector3 thrustDirection = -(state.localRotation * Vector3.forward);
                float forwardAuthority = Vector3.Dot(thrustDirection.normalized, Vector3.forward);
                if (forwardAuthority < 0.85f)
                {
                    error = "A generated thruster does not provide forward thrust: " + state.runtimeId;
                    return false;
                }
                thrusterCount++;
                thrust += definition.BaseThrust * state.uniformScale * state.uniformScale * forwardAuthority;
                CountMirrorGroup(functionalMirrorGroups, state.mirrorGroupId);
            }
            else if (definition.Category == SpacecraftPartCategory.Weapon)
            {
                if (!ValidateFunctionalHardpoint(layout, state, definition, occupiedHardpoints, out error))
                    return false;
                Vector3 fireDirection = state.localRotation * Vector3.up;
                if (Vector3.Dot(fireDirection.normalized, Vector3.forward) < 0.85f)
                {
                    error = "A generated weapon does not fire toward the bow: " + state.runtimeId;
                    return false;
                }
                weaponCount++;
                CountMirrorGroup(functionalMirrorGroups, state.mirrorGroupId);
            }
        }
        if (thrusterCount < 2 || weaponCount < 2 || (weaponCount & 1) != 0)
        {
            error = "The blueprint does not contain paired propulsion and weapons.";
            return false;
        }
        foreach (KeyValuePair<string, int> pair in functionalMirrorGroups)
        {
            if (string.IsNullOrEmpty(pair.Key) || pair.Value != 2)
            {
                error = "A functional mirror group is incomplete: " + pair.Key;
                return false;
            }
        }
        if (thrust / Mathf.Max(0.01f, mass) < layout.MinimumForwardAcceleration)
        {
            error = "The generated propulsion does not meet the layout acceleration requirement.";
            return false;
        }
        float normalizedImbalance = Mathf.Abs(lateralMoment) /
                                    Mathf.Max(0.01f, mass * Mathf.Max(0.1f, hull.Dimensions.x));
        if (normalizedImbalance > layout.MaximumLateralMassImbalance)
        {
            error = "The generated lateral mass imbalance is too high.";
            return false;
        }
        return true;
    }

    static bool ValidateFunctionalHardpoint(
        SpacecraftHardpointLayout layout,
        PlacedPartState state,
        ShipPartDefinition definition,
        HashSet<string> occupiedHardpoints,
        out string error)
    {
        error = string.Empty;
        const string Prefix = "pcg_";
        if (string.IsNullOrEmpty(state.runtimeId) || !state.runtimeId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            error = "A functional part has no hardpoint runtime ID: " + state.partId;
            return false;
        }

        string hardpointId = state.runtimeId.Substring(Prefix.Length);
        SpacecraftHardpoint hardpoint = FindHardpoint(layout.Hardpoints, hardpointId);
        if (hardpoint == null || !hardpoint.Accepts(definition))
        {
            error = "A generated part is not compatible with its hardpoint: " + state.runtimeId;
            return false;
        }
        if (!occupiedHardpoints.Add(hardpointId))
        {
            error = "A hardpoint is occupied more than once: " + hardpointId;
            return false;
        }
        if ((state.localPosition - hardpoint.LocalPosition).sqrMagnitude > 0.000001f ||
            Quaternion.Angle(state.localRotation, hardpoint.LocalRotation) > 0.01f)
        {
            error = "A generated functional part does not match its hardpoint pose: " + state.runtimeId;
            return false;
        }
        return true;
    }

    static void CountMirrorGroup(Dictionary<string, int> groups, string mirrorGroupId)
    {
        if (groups.TryGetValue(mirrorGroupId ?? string.Empty, out int count))
            groups[mirrorGroupId ?? string.Empty] = count + 1;
        else
            groups[mirrorGroupId ?? string.Empty] = 1;
    }

    public bool ApplyBlueprint(ProceduralPirateBlueprint data, out string error)
    {
        error = string.Empty;
        if (data?.spacecraft == null || hullController == null || assembly == null)
        {
            error = "Pirate ship runtime references are incomplete.";
            return false;
        }
        ShipHullDefinition hull = hullCatalog.Find(data.spacecraft.hullId);
        if (hull == null || !hullController.ApplyHull(hull))
        {
            error = "The requested pirate hull could not be applied.";
            return false;
        }
        hullController.ApplyPaint(data.spacecraft.hullMaterialId);
        assembly.Configure(GetComponent<Rigidbody>(), assembly.PartsRoot, partCatalog, hull.BaseMass);
        assembly.RestoreStates(data.spacecraft.parts);
        assembly.Configure(GetComponent<Rigidbody>(), assembly.PartsRoot, partCatalog, hull.BaseMass);
        if (commandBuffer != null)
        {
            ifcsMotor?.Configure(GetComponent<Rigidbody>(), assembly, hullController, commandBuffer, false);
            weaponSystem?.SetCommandSource(commandBuffer);
        }
        return true;
    }

    public bool ValidateCurrentVisuals(out string error)
    {
        error = string.Empty;
        if (hullController == null || hullController.CurrentHull == null || assembly == null)
        {
            error = "The instantiated pirate has no active hull or assembly.";
            return false;
        }

        Renderer[] hullRenderers = hullController.GetComponentsInChildren<Renderer>(false);
        if (!TryValidateRenderers(hullRenderers, "hull", out Bounds hullBounds, out error))
            return false;

        Vector3 authoredSize = hullController.CurrentHull.Dimensions;
        float authoredMaximum = Mathf.Max(authoredSize.x, Mathf.Max(authoredSize.y, authoredSize.z));
        float hullMaximum = Mathf.Max(hullBounds.size.x, Mathf.Max(hullBounds.size.y, hullBounds.size.z));
        float hullMinimum = Mathf.Min(hullBounds.size.x, Mathf.Min(hullBounds.size.y, hullBounds.size.z));
        if (hullMaximum > Mathf.Max(2f, authoredMaximum * 4f) || hullMinimum <= 0.01f ||
            hullMaximum / Mathf.Max(0.01f, hullMinimum) > 30f)
        {
            error = "The instantiated hull bounds are empty, stretched or inconsistent with the authored hull.";
            return false;
        }

        float allowedPartDistance = Mathf.Max(4f, authoredMaximum * 1.75f);
        IReadOnlyList<SpacecraftPart> parts = assembly.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            SpacecraftPart part = parts[index];
            if (part == null || part.Definition == null)
            {
                error = "The instantiated pirate contains an empty part.";
                return false;
            }
            if (!IsFinite(part.transform.localPosition) || !IsFinite(part.transform.localScale) ||
                part.transform.localPosition.magnitude > allowedPartDistance ||
                part.transform.localScale.x < 0.01f || part.transform.localScale.x > 5f)
            {
                error = "A pirate part has an invalid or disconnected transform: " + part.name;
                return false;
            }
            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(false);
            if (!TryValidateRenderers(renderers, part.name, out Bounds partBounds, out error))
                return false;
            float partExtent = partBounds.extents.magnitude;
            float connectedDistance = Vector3.Distance(partBounds.center, hullBounds.center);
            if (connectedDistance > hullBounds.extents.magnitude + partExtent + 2f)
            {
                error = "A pirate part is visually detached from the hull: " + part.name;
                return false;
            }

            if (part.Definition.Category != SpacecraftPartCategory.Thruster &&
                part.Definition.Category != SpacecraftPartCategory.Weapon)
                continue;
            SpacecraftPart mate = assembly.FindMirrorMate(part);
            if (mate == null || mate.Definition != part.Definition ||
                Mathf.Abs(mate.UniformScale - part.UniformScale) > 0.001f ||
                Mathf.Abs(mate.transform.localPosition.x + part.transform.localPosition.x) > 0.01f ||
                Mathf.Abs(mate.transform.localPosition.y - part.transform.localPosition.y) > 0.01f ||
                Mathf.Abs(mate.transform.localPosition.z - part.transform.localPosition.z) > 0.01f)
            {
                error = "A functional pirate part has no matching mirrored partner: " + part.name;
                return false;
            }
        }
        return true;
    }

    static bool TryValidateRenderers(Renderer[] renderers, string label, out Bounds combined, out string error)
    {
        combined = default;
        error = string.Empty;
        bool found = false;
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                continue;
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned)
                mesh = skinned.sharedMesh;
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null)
                    mesh = filter.sharedMesh;
            }
            if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount == 0 || !IsFinite(renderer.bounds))
            {
                error = "A visible " + label + " renderer has no valid mesh or finite bounds: " + renderer.name;
                return false;
            }
            Material[] materials = renderer.sharedMaterials;
            bool hasMaterial = false;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                hasMaterial |= materials[materialIndex] != null;
            if (!hasMaterial)
            {
                error = "A visible " + label + " renderer has no material: " + renderer.name;
                return false;
            }
            if (!found)
            {
                combined = renderer.bounds;
                found = true;
            }
            else
                combined.Encapsulate(renderer.bounds);
        }
        if (!found)
        {
            error = "The instantiated pirate has no visible " + label + " renderer.";
            return false;
        }
        return true;
    }

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    static bool IsFinite(Bounds value)
    {
        return IsFinite(value.center) && IsFinite(value.size) && value.size.sqrMagnitude > 0.000001f;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    bool BuildCandidateLists(out string error)
    {
        error = string.Empty;
        thrusterCandidates.Clear();
        weaponCandidates.Clear();
        decorationCandidates.Clear();
        if (hullCatalog == null || partCatalog == null || partCatalog.Definitions == null)
        {
            error = "Hull or part catalog is missing.";
            return false;
        }
        for (int index = 0; index < partCatalog.Definitions.Count; index++)
        {
            ShipPartDefinition definition = partCatalog.Definitions[index];
            if (definition == null || definition.Prefab == null)
                continue;
            switch (definition.Category)
            {
                case SpacecraftPartCategory.Thruster: thrusterCandidates.Add(definition); break;
                case SpacecraftPartCategory.Weapon:
                    if (definition.Weapon != null)
                        weaponCandidates.Add(definition);
                    break;
                case SpacecraftPartCategory.Decoration: decorationCandidates.Add(definition); break;
            }
        }
        if (thrusterCandidates.Count == 0 || weaponCandidates.Count == 0)
        {
            error = "The part catalog has no usable thruster or weapon definitions.";
            return false;
        }
        return true;
    }

    ShipPartDefinition SelectWeapon(SpacecraftHardpointLayout layout, int tier, ref StableRandom64 random)
    {
        int start = random.NextInt(weaponCandidates.Count);
        for (int offset = 0; offset < weaponCandidates.Count; offset++)
        {
            ShipPartDefinition candidate = weaponCandidates[(start + offset) % weaponCandidates.Count];
            if ((int)candidate.Weapon.MountSize > Mathf.Clamp(tier, 1, 3))
                continue;
            if (AnyHardpointAccepts(layout, candidate))
                return candidate;
        }
        return FindFirstCompatibleWeapon(layout);
    }

    ShipPartDefinition FindFirstCompatibleWeapon(SpacecraftHardpointLayout layout)
    {
        for (int index = 0; index < weaponCandidates.Count; index++)
        {
            if (AnyHardpointAccepts(layout, weaponCandidates[index]))
                return weaponCandidates[index];
        }
        return null;
    }

    static bool AnyHardpointAccepts(SpacecraftHardpointLayout layout, ShipPartDefinition definition)
    {
        for (int index = 0; index < layout.Hardpoints.Length; index++)
        {
            if (layout.Hardpoints[index] != null && layout.Hardpoints[index].Accepts(definition))
                return true;
        }
        return false;
    }

    SpacecraftHardpointLayout SelectValidLayout(ref StableRandom64 random)
    {
        int start = random.NextInt(layouts.Length);
        for (int offset = 0; offset < layouts.Length; offset++)
        {
            SpacecraftHardpointLayout layout = layouts[(start + offset) % layouts.Length];
            if (layout != null && hullCatalog.Find(layout.HullId) != null)
                return layout;
        }
        return null;
    }

    SpacecraftHardpointLayout FindFirstValidLayout()
    {
        if (layouts == null)
            return null;
        for (int index = 0; index < layouts.Length; index++)
        {
            if (layouts[index] != null && hullCatalog.Find(layouts[index].HullId) != null)
                return layouts[index];
        }
        return null;
    }

    string SelectPaint(ref StableRandom64 random)
    {
        if (materialCatalog == null || materialCatalog.Definitions == null || materialCatalog.Definitions.Count == 0)
            return "paint.gunmetal";
        SpacecraftMaterialDefinition definition = materialCatalog.Definitions[random.NextInt(materialCatalog.Definitions.Count)];
        return definition == null ? materialCatalog.DefaultMaterialId : definition.MaterialId;
    }

    void ResolveReferences()
    {
        if (hullCatalog == null)
            hullCatalog = GetComponentInChildren<HullCatalog>(true) ?? FindObjectOfType<HullCatalog>();
        if (partCatalog == null)
            partCatalog = GetComponentInChildren<PartCatalog>(true) ?? FindObjectOfType<PartCatalog>();
        if (materialCatalog == null)
            materialCatalog = GetComponentInChildren<SpacecraftMaterialCatalog>(true) ?? FindObjectOfType<SpacecraftMaterialCatalog>();
        if (hullController == null)
            hullController = GetComponentInChildren<ShipHullController>(true);
        if (assembly == null)
            assembly = GetComponent<ShipAssembly>();
        if (ifcsMotor == null)
            ifcsMotor = GetComponent<SpacecraftIfcsMotor>();
        if (weaponSystem == null)
            weaponSystem = GetComponent<SpacecraftWeaponSystem>();
        if (commandBuffer == null)
            commandBuffer = GetComponent<PirateCommandBuffer>();
    }

    static SpacecraftHardpoint FindHardpoint(SpacecraftHardpoint[] points, string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        for (int index = 0; index < points.Length; index++)
        {
            if (points[index] != null && string.Equals(points[index].HardpointId, id, StringComparison.Ordinal))
                return points[index];
        }
        return null;
    }

    static PlacedPartState CreatePartState(
        ShipPartDefinition definition,
        SpacecraftHardpoint point,
        float scale,
        string paint,
        int group,
        string mirrorGroup)
    {
        return CreatePartState(definition, point.LocalPosition, point.LocalRotation, scale, paint, group,
            mirrorGroup, point.HardpointId);
    }

    static PlacedPartState CreatePartState(
        ShipPartDefinition definition,
        Vector3 position,
        Quaternion rotation,
        float scale,
        string paint,
        int group,
        string mirrorGroup,
        string suffix)
    {
        return new PlacedPartState
        {
            runtimeId = "pcg_" + suffix,
            partId = definition.PartId,
            localPosition = position,
            localRotation = rotation,
            uniformScale = definition.IsScalable ? scale : definition.FixedScale,
            mirrorGroupId = mirrorGroup,
            materialId = paint,
            activationKey = KeyCode.None,
            weaponGroup = group
        };
    }

    static Quaternion MirrorRotation(Quaternion source)
    {
        Vector3 forward = source * Vector3.forward;
        Vector3 up = source * Vector3.up;
        forward.x = -forward.x;
        up.x = -up.x;
        return Quaternion.LookRotation(forward, up);
    }

    static string BuildSignature(ProceduralPirateBlueprint blueprint)
    {
        var builder = new StringBuilder(256);
        builder.Append("pirate|").Append(blueprint.generatorVersion).Append('|')
            .Append(blueprint.seed).Append('|').Append(blueprint.tier).Append('|')
            .Append(blueprint.spacecraft.hullId).Append('|').Append(blueprint.spacecraft.hullMaterialId);
        for (int index = 0; index < blueprint.spacecraft.parts.Length; index++)
        {
            PlacedPartState part = blueprint.spacecraft.parts[index];
            builder.Append('|').Append(part.partId).Append('@')
                .Append(part.localPosition.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(part.localPosition.y.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(part.localPosition.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.Append('|').Append(Fnv1a64(builder.ToString()).ToString("X16")).ToString();
    }

    static ulong CombineSeed(int seed, int salt)
    {
        return unchecked((ulong)(uint)seed | ((ulong)(uint)salt << 32));
    }

    static ulong Fnv1a64(string value)
    {
        ulong hash = 14695981039346656037UL;
        for (int index = 0; index < value.Length; index++)
        {
            hash ^= value[index];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    struct StableRandom64
    {
        ulong state;

        public StableRandom64(ulong seed)
        {
            state = seed + 0x9E3779B97F4A7C15UL;
        }

        public ulong Next()
        {
            ulong value = (state += 0x9E3779B97F4A7C15UL);
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        public int NextInt(int maximum)
        {
            return maximum <= 1 ? 0 : (int)(Next() % (uint)maximum);
        }

        public int NextInt(int minimum, int maximum)
        {
            return minimum >= maximum ? minimum : minimum + NextInt(maximum - minimum);
        }

        public float NextFloat()
        {
            return (Next() >> 40) / 16777216f;
        }

        public float NextSignedFloat()
        {
            return NextFloat() * 2f - 1f;
        }
    }
}

[Serializable]
public struct PirateEncounterPlan
{
    public int seed;
    public int tier;
    public Vector3 localSpawnOffset;
}
