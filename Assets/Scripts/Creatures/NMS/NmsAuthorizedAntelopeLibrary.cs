using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class NmsAuthorizedAntelopeModule
{
    [SerializeField] string selectionId;
    [SerializeField] string meshName;
    [SerializeField] GameObject modulePrefab;
    [SerializeField] Material[] materials = Array.Empty<Material>();

    public string SelectionId => selectionId;
    public string MeshName => meshName;
    public GameObject ModulePrefab => modulePrefab;
    public Material[] Materials => materials;

#if UNITY_EDITOR
    public NmsAuthorizedAntelopeModule(
        string id,
        string sourceMeshName,
        GameObject sourcePrefab,
        Material[] sourceMaterials)
    {
        selectionId = id;
        meshName = sourceMeshName;
        modulePrefab = sourcePrefab;
        materials = sourceMaterials ?? Array.Empty<Material>();
    }
#endif
}

[CreateAssetMenu(
    menuName = "Creatures/NMS Authorized Antelope Library",
    fileName = "NmsAuthorizedAntelopeLibrary")]
public sealed class NmsAuthorizedAntelopeLibrary : ScriptableObject
{
    [SerializeField] string familyId = "AntelopeQuadruped";
    [SerializeField] GameObject rigPrefab;
    [SerializeField] GameObject safeFallbackPrefab;
    [SerializeField] RuntimeAnimatorController animatorController;
    [SerializeField] string locomotionState = "Locomotion";
    [SerializeField] string speedParameter = "Speed";
    [SerializeField] NmsAuthorizedAntelopeModule[] modules =
        Array.Empty<NmsAuthorizedAntelopeModule>();

    public string FamilyId => familyId;
    public GameObject RigPrefab => rigPrefab;
    public GameObject SafeFallbackPrefab => safeFallbackPrefab;
    public RuntimeAnimatorController AnimatorController => animatorController;
    public string LocomotionState => locomotionState;
    public string SpeedParameter => speedParameter;
    public NmsAuthorizedAntelopeModule[] Modules => modules;

    public bool CanBuild(string requestedFamilyId)
    {
        return rigPrefab != null
            && string.Equals(familyId, requestedFamilyId, StringComparison.Ordinal);
    }

    public bool TryBuild(
        Transform parent,
        IEnumerable<string> selectedModuleIds,
        List<Renderer> activeRenderers,
        out GameObject root,
        out string error)
    {
        return TryBuild(
            parent, selectedModuleIds, activeRenderers,
            out root, out _, out error);
    }

    public bool TryBuild(
        Transform parent,
        IEnumerable<string> selectedModuleIds,
        List<Renderer> activeRenderers,
        out GameObject root,
        out Animator animator,
        out string error)
    {
        root = null;
        animator = null;
        error = null;
        if (parent == null || rigPrefab == null || activeRenderers == null)
        {
            error = "Authorized antelope assembly is missing its parent, rig, or renderer list.";
            return false;
        }

        var entries = new Dictionary<string, NmsAuthorizedAntelopeModule>(
            StringComparer.Ordinal);
        for (int i = 0; i < modules.Length; i++)
        {
            NmsAuthorizedAntelopeModule entry = modules[i];
            if (entry == null || string.IsNullOrEmpty(entry.MeshName))
                continue;
            string key = NormalizeId(entry.SelectionId);
            if (!string.IsNullOrEmpty(key) && !entries.ContainsKey(key))
                entries.Add(key, entry);
        }

        var requested = new List<NmsAuthorizedAntelopeModule>(12);
        var missing = new StringBuilder();
        foreach (string selectedId in selectedModuleIds)
        {
            string key = NormalizeId(selectedId);
            if (string.IsNullOrEmpty(key) || IsExplicitNone(key))
                continue;
            if (!entries.TryGetValue(key, out NmsAuthorizedAntelopeModule entry))
            {
                if (missing.Length > 0)
                    missing.Append(", ");
                missing.Append(selectedId);
                continue;
            }
            requested.Add(entry);
        }
        if (missing.Length > 0)
        {
            error = "Authorized antelope library has no module mapping for: " + missing;
            return false;
        }

        root = new GameObject("AuthorizedAntelopeRuntime");
        root.transform.SetParent(parent, false);
        GameObject rig = Instantiate(rigPrefab, root.transform, false);
        rig.name = "CanonicalRig";
        animator = rig.GetComponent<Animator>();
        if (animator == null)
            animator = rig.AddComponent<Animator>();
        animator.runtimeAnimatorController = animatorController;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        SkinnedMeshRenderer[] renderers =
            rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var renderersByMesh = new Dictionary<string, SkinnedMeshRenderer>(
            StringComparer.Ordinal);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            renderer.enabled = false;
            if (renderer.sharedMesh != null
                && !renderersByMesh.ContainsKey(renderer.sharedMesh.name))
                renderersByMesh.Add(renderer.sharedMesh.name, renderer);
        }
        activeRenderers.Clear();
        for (int i = 0; i < requested.Count; i++)
        {
            NmsAuthorizedAntelopeModule entry = requested[i];
            if (!renderersByMesh.TryGetValue(
                entry.MeshName, out SkinnedMeshRenderer renderer))
            {
                error = $"Canonical antelope source has no mesh '{entry.MeshName}' "
                    + $"for module '{entry.SelectionId}'.";
                Destroy(root);
                root = null;
                animator = null;
                activeRenderers.Clear();
                return false;
            }
            if (renderer.bones == null || renderer.sharedMesh == null
                || renderer.bones.Length != renderer.sharedMesh.bindposes.Length)
            {
                error = $"Validated module '{entry.SelectionId}' has an invalid bind pose.";
                Destroy(root);
                root = null;
                animator = null;
                activeRenderers.Clear();
                return false;
            }
            if (entry.Materials != null && entry.Materials.Length > 0)
                renderer.sharedMaterials = entry.Materials;
            renderer.enabled = true;
            renderer.updateWhenOffscreen = true;
            activeRenderers.Add(renderer);
        }

        if (activeRenderers.Count == 0)
        {
            error = "Authorized antelope assembly selected no visible modules.";
            Destroy(root);
            root = null;
            animator = null;
            return false;
        }
        return true;
    }

    public bool TryBuildFallback(
        Transform parent,
        List<Renderer> activeRenderers,
        out GameObject root,
        out Animator animator,
        out string error)
    {
        string[] standardModules =
        {
            "_Body_Deer", "_Head_Deer", "DeerEyes", "_HDEars_1"
        };
        return TryBuild(
            parent, standardModules, activeRenderers,
            out root, out animator, out error);
    }

    static bool IsExplicitNone(string normalizedId)
    {
        return normalizedId.EndsWith("none", StringComparison.Ordinal);
    }

    static string NormalizeId(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
            if (char.IsLetterOrDigit(value[i]))
                result.Append(char.ToLowerInvariant(value[i]));
        string normalized = result.ToString();
        return normalized.StartsWith("antelope", StringComparison.Ordinal)
            ? normalized.Substring("antelope".Length)
            : normalized;
    }

#if UNITY_EDITOR
    public void ConfigureEditor(
        GameObject sourceRig,
        GameObject fallback,
        RuntimeAnimatorController sourceController,
        NmsAuthorizedAntelopeModule[] sourceModules)
    {
        familyId = "AntelopeQuadruped";
        rigPrefab = sourceRig;
        safeFallbackPrefab = fallback;
        animatorController = sourceController;
        locomotionState = "Locomotion";
        speedParameter = "Speed";
        modules = sourceModules ?? Array.Empty<NmsAuthorizedAntelopeModule>();
    }
#endif
}
