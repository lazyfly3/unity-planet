using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public enum NmsCreatureSurfaceMode
{
    Ground,
    Hover
}

[CreateAssetMenu(menuName = "Creatures/NMS Family Definition", fileName = "NmsCreatureFamily")]
public sealed class NmsCreatureFamilyDefinition : ScriptableObject
{
    [SerializeField] string familyId;
    [SerializeField] string skeletonHash;
    [SerializeField] string locomotionType;
    [SerializeField] NmsCreatureSurfaceMode surfaceMode;
    [SerializeField] string[] variantIds = Array.Empty<string>();
    [SerializeField, Min(1)] int selectionWeight = 1;
    [SerializeField, Min(0)] int legCount;
    [SerializeField] bool allowRareModules;
    [SerializeField] string[] excludedModulePrefixes = Array.Empty<string>();
    [SerializeField] GameObject runtimePrefab;
    [SerializeField] RuntimeAnimatorController animatorController;
    [SerializeField] TextAsset familyManifest;
    [SerializeField] NmsCreatureModuleBinding[] moduleBindings = Array.Empty<NmsCreatureModuleBinding>();
    [SerializeField] NmsCreatureMaterialDefinition[] materialDefinitions =
        Array.Empty<NmsCreatureMaterialDefinition>();
    [SerializeField] NmsCreatureRendererMaterialBinding[] rendererMaterialBindings =
        Array.Empty<NmsCreatureRendererMaterialBinding>();
    [SerializeField] NmsCreatureLegChain[] loadBearingChains = Array.Empty<NmsCreatureLegChain>();
    [SerializeField, Min(0f)] float walkSpeed = 1.35f;
    [SerializeField, Min(0f)] float surfaceRootOffset = 0.05f;
    [SerializeField, Min(0f)] float hoverClearance;
    [SerializeField] Vector3 localForwardAxis = Vector3.forward;
    [SerializeField] string idleState = "Idle";
    [SerializeField] string walkState = "Walk";
    [SerializeField] string runState = "Run";
    [SerializeField] bool supportsRun = true;

    [NonSerialized] NmsCreatureFamilyManifestData parsedManifest;

    public string FamilyId => familyId;
    public string SkeletonHash => skeletonHash;
    public string LocomotionType => locomotionType;
    public NmsCreatureSurfaceMode SurfaceMode => surfaceMode;
    public IReadOnlyList<string> VariantIds => variantIds;
    public int SelectionWeight => Mathf.Max(1, selectionWeight);
    public int LegCount => legCount;
    public bool AllowRareModules => allowRareModules;
    public IReadOnlyList<string> ExcludedModulePrefixes => excludedModulePrefixes;
    public GameObject RuntimePrefab => runtimePrefab;
    public RuntimeAnimatorController AnimatorController => animatorController;
    public IReadOnlyList<NmsCreatureModuleBinding> ModuleBindings => moduleBindings;
    public IReadOnlyList<NmsCreatureMaterialDefinition> MaterialDefinitions => materialDefinitions;
    public IReadOnlyList<NmsCreatureRendererMaterialBinding> RendererMaterialBindings =>
        rendererMaterialBindings;
    public IReadOnlyList<NmsCreatureLegChain> LoadBearingChains => loadBearingChains;
    public float WalkSpeed => walkSpeed;
    public float SurfaceRootOffset => surfaceRootOffset;
    public float HoverClearance => surfaceMode == NmsCreatureSurfaceMode.Hover
        ? hoverClearance
        : 0f;
    public float SurfaceClearance => surfaceRootOffset + HoverClearance;
    public Vector3 LocalForwardAxis => localForwardAxis.sqrMagnitude > 0.000001f
        ? localForwardAxis.normalized
        : Vector3.forward;
    public string IdleState => idleState;
    public string WalkState => walkState;
    public string RunState => runState;
    public bool SupportsRun => supportsRun;

    public bool TryGetManifest(out NmsCreatureFamilyManifestData manifest)
    {
        if (parsedManifest == null && familyManifest != null)
            parsedManifest = JsonConvert.DeserializeObject<NmsCreatureFamilyManifestData>(
                familyManifest.text);
        manifest = parsedManifest;
        return manifest != null
            && string.Equals(manifest.familyId, familyId, StringComparison.Ordinal)
            && string.Equals(manifest.skeletonHash, skeletonHash, StringComparison.Ordinal);
    }

    public bool TryGetBinding(string moduleId, out NmsCreatureModuleBinding binding)
    {
        for (int i = 0; i < moduleBindings.Length; i++)
        {
            if (!string.Equals(moduleBindings[i].moduleId, moduleId, StringComparison.Ordinal))
                continue;
            binding = moduleBindings[i];
            return true;
        }
        binding = null;
        return false;
    }

#if UNITY_EDITOR
    public void ConfigureImported(
        string importedFamilyId,
        string importedSkeletonHash,
        string importedLocomotionType,
        NmsCreatureSurfaceMode importedSurfaceMode,
        bool importedSupportsRun,
        string[] importedVariantIds,
        int importedSelectionWeight,
        int importedLegCount,
        bool importedAllowRareModules,
        string[] importedExcludedModulePrefixes,
        GameObject importedPrefab,
        RuntimeAnimatorController importedController,
        TextAsset importedManifest,
        NmsCreatureModuleBinding[] importedBindings,
        NmsCreatureMaterialDefinition[] importedMaterialDefinitions,
        NmsCreatureRendererMaterialBinding[] importedRendererMaterialBindings,
        NmsCreatureLegChain[] importedChains,
        float importedWalkSpeed,
        float importedSurfaceRootOffset,
        float importedHoverClearance,
        Vector3 importedLocalForwardAxis)
    {
        familyId = importedFamilyId;
        skeletonHash = importedSkeletonHash;
        locomotionType = importedLocomotionType;
        surfaceMode = importedSurfaceMode;
        supportsRun = importedSupportsRun;
        variantIds = importedVariantIds ?? Array.Empty<string>();
        selectionWeight = Mathf.Max(1, importedSelectionWeight);
        legCount = Mathf.Max(0, importedLegCount);
        allowRareModules = importedAllowRareModules;
        excludedModulePrefixes = importedExcludedModulePrefixes ?? Array.Empty<string>();
        runtimePrefab = importedPrefab;
        animatorController = importedController;
        familyManifest = importedManifest;
        moduleBindings = importedBindings ?? Array.Empty<NmsCreatureModuleBinding>();
        materialDefinitions =
            importedMaterialDefinitions ?? Array.Empty<NmsCreatureMaterialDefinition>();
        rendererMaterialBindings =
            importedRendererMaterialBindings ?? Array.Empty<NmsCreatureRendererMaterialBinding>();
        loadBearingChains = importedChains ?? Array.Empty<NmsCreatureLegChain>();
        walkSpeed = Mathf.Max(0f, importedWalkSpeed);
        surfaceRootOffset = Mathf.Max(0f, importedSurfaceRootOffset);
        hoverClearance = Mathf.Max(0f, importedHoverClearance);
        localForwardAxis = importedLocalForwardAxis.sqrMagnitude > 0.000001f
            ? importedLocalForwardAxis.normalized
            : Vector3.forward;
        parsedManifest = null;
    }
#endif
}
