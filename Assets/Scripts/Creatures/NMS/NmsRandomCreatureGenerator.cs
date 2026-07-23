using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NmsRandomCreatureGenerator : MonoBehaviour
{
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int PrimaryColorId = Shader.PropertyToID("_PrimaryColor");
    static readonly int SecondaryColorId = Shader.PropertyToID("_SecondaryColor");
    static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");

    [Header("Catalog")]
    [SerializeField] NmsCreatureFamilyCatalog catalog;
    [SerializeField] int seed = 12345;
    [SerializeField] string forcedFamilyId;
    [SerializeField] bool regenerateWithR = true;
    [SerializeField] NmsCreatureMotionMode motionMode = NmsCreatureMotionMode.ImportedAnimator;
    [SerializeField] NmsProceduralMotionProfile proceduralMotionProfile;
    [SerializeField] NmsAuthorizedAntelopeLibrary authorizedAntelopeLibrary;

    [Header("Imported Walk")]
    [SerializeField, Range(0.5f, 3f)] float importedWalkPlaybackSpeed = 1.8f;

    [Header("Spherical Test")]
    [SerializeField] SphericalGravitySource gravitySource;
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField] BioCreatureFollowCamera followCamera;
    [SerializeField] Vector3 spawnDirection = Vector3.up;
    [SerializeField, Min(1f)] float surfaceProbeHeight = 20f;
    [SerializeField] bool moveAlongGreatCircle = true;
    [SerializeField] bool validateRigContinuously = true;

    [Header("Runtime Mesh Safety")]
    [SerializeField] bool rejectInvalidSkinnedMeshes = true;
    [SerializeField, Min(1.1f)] float maximumBakedBoundsMultiplier = 4f;
    [SerializeField, Min(1.1f)] float maximumTriangleEdgeStretch = 6f;
    [SerializeField, Range(1, 32)] int maximumAutomaticRetries = 12;

    readonly List<Renderer> activeRenderers = new List<Renderer>(32);
    readonly List<SegmentInvariant> segmentInvariants = new List<SegmentInvariant>(24);
    MaterialPropertyBlock propertyBlock;
    GameObject currentCreature;
    Animator currentAnimator;
    NmsProceduralMotionController currentMotionController;
    NmsImportedAnimatorMotionController currentImportedController;
    NmsCreatureFamilyDefinition currentFamily;
    NmsCreatureSpeciesDefinition currentSpecies;
    bool invariantReported;
    int generationRevision;
    int readyRevision = -1;
    int automaticRetryCount;
    bool forceStandardVariant;
    bool currentUsesAuthorizedAntelope;

    public int Seed => seed;
    public Transform CurrentCreature => currentCreature != null ? currentCreature.transform : null;
    public NmsCreatureSpeciesDefinition CurrentSpecies => currentSpecies;
    public NmsCreatureFamilyDefinition CurrentFamily => currentFamily;
    public Animator CurrentAnimator => currentAnimator;
    public NmsProceduralMotionController CurrentMotionController => currentMotionController;
    public NmsImportedAnimatorMotionController CurrentImportedController =>
        currentImportedController;
    public NmsCreatureMotionMode MotionMode => motionMode;
    public bool CurrentVariantValidated { get; private set; }
    public string LastValidationResult { get; private set; } = "Not generated";
    public event Action<NmsCreatureSpawnContext> CreatureReady;

    void Start()
    {
        Generate(seed);
    }

    void Update()
    {
        if (regenerateWithR && Input.GetKeyDown(KeyCode.R))
            Generate(unchecked(seed + 1));
    }

    void FixedUpdate()
    {
        if (currentCreature == null || currentFamily == null || gravitySource == null)
            return;
        if (moveAlongGreatCircle && motionMode == NmsCreatureMotionMode.ImportedAnimator)
            if (currentImportedController == null)
                MoveAlongSphere(currentFamily.WalkSpeed * Time.fixedDeltaTime);
    }

    void LateUpdate()
    {
        if (!validateRigContinuously || invariantReported)
            return;
        for (int i = 0; i < segmentInvariants.Count; i++)
        {
            SegmentInvariant invariant = segmentInvariants[i];
            if (invariant.parent == null || invariant.child == null)
            {
                ReportInvariant("A load-bearing bone was destroyed.");
                return;
            }
            float length = Vector3.Distance(invariant.parent.position, invariant.child.position);
            float error = Mathf.Abs(length / invariant.length - 1f);
            if (!IsFinite(length) || error > 0.02f)
            {
                ReportInvariant(
                    $"Load-bearing segment '{invariant.id}' changed length by {error * 100f:F2}%.");
                return;
            }
        }
    }

    public void Generate(int newSeed)
    {
        automaticRetryCount = 0;
        forceStandardVariant = false;
        GenerateInternal(newSeed);
    }

    void GenerateInternal(int newSeed)
    {
        generationRevision++;
        seed = newSeed;
        DestroyCurrent();
        if (catalog == null || catalog.Families.Count == 0)
        {
            Debug.LogError("NMS creature catalog is missing or empty.", this);
            return;
        }
        if (gravitySource == null)
        {
            Debug.LogError("NMS random creature generator requires a SphericalGravitySource.", this);
            return;
        }

        var familyRandom =
            new StableRandom(unchecked((ulong)(uint)seed) ^ 0xA0761D6478BD642FUL);
        currentFamily = SelectFamily(ref familyRandom);
        if (currentFamily == null
            || currentFamily.RuntimePrefab == null
            || !currentFamily.TryGetManifest(out NmsCreatureFamilyManifestData manifest))
        {
            Debug.LogError("The selected NMS family is incomplete or has an invalid manifest.", this);
            currentFamily = null;
            return;
        }

        var descriptorRandom =
            new StableRandom(unchecked((ulong)(uint)seed) ^ 0xE7037ED1A0B428DBUL);
        var colorRandom =
            new StableRandom(unchecked((ulong)(uint)seed) ^ StableHash(currentFamily.FamilyId));
        HashSet<string> selected = SelectModules(
            manifest.descriptorGroups,
            currentFamily.AllowRareModules,
            currentFamily.ExcludedModulePrefixes,
            BuildIncompatibleModuleSet(manifest.modules),
            ref descriptorRandom);
        if (forceStandardVariant)
        {
            selected.Clear();
            selected.Add("_Body_Deer");
            selected.Add("_Head_Deer");
            selected.Add("DeerEyes");
            selected.Add("_HDEars_1");
        }
        CreatePalette(
            ref colorRandom,
            out Color primary,
            out Color secondary,
            out Color accent);
        currentSpecies = new NmsCreatureSpeciesDefinition
        {
            seed = seed,
            familyId = currentFamily.FamilyId,
            selectedModules = Sorted(selected),
            primaryColor = primary,
            secondaryColor = secondary,
            accentColor = accent,
            signature = BuildSignature(currentFamily.FamilyId, seed, selected, primary, accent)
        };

        Vector3 direction = spawnDirection.sqrMagnitude > 0.0001f
            ? spawnDirection.normalized
            : Vector3.up;
        Vector3 surface = FindSurface(direction);
        Vector3 up = gravitySource.GetUp(surface);
        Vector3 forward = Vector3.Cross(Vector3.forward, up);
        if (forward.sqrMagnitude <= 0.000001f)
            forward = Vector3.Cross(Vector3.right, up);

        bool useAuthorizedAntelope = authorizedAntelopeLibrary != null
            && authorizedAntelopeLibrary.CanBuild(currentFamily.FamilyId);
        currentUsesAuthorizedAntelope = useAuthorizedAntelope;
        if (useAuthorizedAntelope)
        {
            if (!authorizedAntelopeLibrary.TryBuild(
                transform, selected, activeRenderers,
                out currentCreature, out currentAnimator, out string assemblyError))
            {
                RejectCurrent("Authorized antelope assembly failed. " + assemblyError);
                return;
            }
        }
        else
        {
            currentCreature = Instantiate(currentFamily.RuntimePrefab, transform);
        }
        currentCreature.name = $"NMS_{currentFamily.FamilyId}_{seed}";
        SetLayerRecursively(currentCreature.transform, 2);
        currentCreature.transform.SetPositionAndRotation(
            surface + up * currentFamily.SurfaceClearance,
            RotationForLocalForward(currentFamily.LocalForwardAxis, forward.normalized, up));
        currentCreature.SetActive(true);

        if (useAuthorizedAntelope)
            ApplyPalette(primary, secondary, accent);
        else
            ApplyModules(selected, primary, secondary, accent);
        if (!HasAnimatedBody())
        {
            Debug.LogError(
                $"NMS family '{currentFamily.FamilyId}' generated without an active skinned body. " +
                "The invalid Descriptor combination was discarded.",
                this);
            DestroyCurrent();
            return;
        }
        if (currentAnimator == null)
            currentAnimator = currentCreature.GetComponentInChildren<Animator>(true);
        if (motionMode == NmsCreatureMotionMode.ImportedAnimator && currentAnimator != null)
        {
            if (useAuthorizedAntelope)
            {
                currentImportedController =
                    currentCreature.AddComponent<NmsImportedAnimatorMotionController>();
                if (!currentImportedController.Configure(
                    gravitySource, currentFamily, currentAnimator,
                    authorizedAntelopeLibrary.AnimatorController,
                    authorizedAntelopeLibrary.LocomotionState,
                    authorizedAntelopeLibrary.SpeedParameter,
                    groundLayers, surfaceProbeHeight,
                    importedWalkPlaybackSpeed, out string importedError))
                {
                    RejectCurrent("Imported Animator configuration failed. " + importedError);
                    return;
                }
            }
            else
            {
                currentAnimator.runtimeAnimatorController = currentFamily.AnimatorController;
                currentAnimator.applyRootMotion = false;
                currentAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                currentAnimator.Play(currentFamily.WalkState, 0, 0f);
            }
        }
        else if (motionMode == NmsCreatureMotionMode.Procedural)
        {
            currentMotionController = currentCreature.AddComponent<NmsProceduralMotionController>();
            if (!currentMotionController.Configure(
                gravitySource, currentFamily, currentSpecies, proceduralMotionProfile,
                groundLayers, currentAnimator, out string motionError))
            {
                RejectCurrent("Procedural rig configuration failed. " + motionError);
                return;
            }
        }
        CaptureSegmentInvariants();
        CurrentVariantValidated = false;
        LastValidationResult = "Pending";
        if (followCamera != null)
            followCamera.SetTarget(currentCreature.transform, true);
        if (rejectInvalidSkinnedMeshes)
        {
            SetActiveRenderersForceOff(true);
            StartCoroutine(ValidateSkinnedMeshesAfterPose(generationRevision));
        }
        else
        {
            SetActiveRenderersForceOff(false);
            NotifyCreatureReady(generationRevision);
        }

        Debug.Log(
            $"NMS species generated. Family={currentFamily.FamilyId}, Seed={seed}, " +
            $"Modules={selected.Count}, Signature={currentSpecies.signature}",
            currentCreature);
    }

    IEnumerator ValidateSkinnedMeshesAfterPose(int revision)
    {
        // Keep renderers hidden until several points across the authored walk cycle are safe.
        yield return null;
        yield return new WaitForEndOfFrame();
        if (revision != generationRevision || currentCreature == null)
            yield break;

        string failure = null;
        int poseSamples = motionMode == NmsCreatureMotionMode.ImportedAnimator
            ? 8
            : 1;
        for (int sample = 0; sample < poseSamples; sample++)
        {
            if (motionMode == NmsCreatureMotionMode.ImportedAnimator && currentAnimator != null)
            {
                if (currentUsesAuthorizedAntelope)
                {
                    float normalizedTime = sample / 8f;
                    currentAnimator.SetFloat(
                        authorizedAntelopeLibrary.SpeedParameter, 0.5f);
                    currentAnimator.Play(
                        "Base Layer." + authorizedAntelopeLibrary.LocomotionState,
                        0, normalizedTime);
                }
                else
                {
                    float normalizedTime = sample / (float)poseSamples;
                    currentAnimator.Play(currentFamily.WalkState, 0, normalizedTime);
                }
                currentAnimator.Update(0f);
            }
            failure = ValidateActiveSkinnedMeshes();
            if (!string.IsNullOrEmpty(failure))
                break;
        }

        if (motionMode == NmsCreatureMotionMode.ImportedAnimator && currentAnimator != null)
        {
            if (currentUsesAuthorizedAntelope)
            {
                currentAnimator.SetFloat(authorizedAntelopeLibrary.SpeedParameter, 0.5f);
                currentAnimator.Play(
                    "Base Layer." + authorizedAntelopeLibrary.LocomotionState,
                    0, 0f);
                currentAnimator.speed = 0f;
            }
            else
            {
                currentAnimator.Play(currentFamily.WalkState, 0, 0f);
            }
            currentAnimator.Update(0f);
        }

        if (string.IsNullOrEmpty(failure))
        {
            SetActiveRenderersForceOff(false);
            CurrentVariantValidated = true;
            LastValidationResult = "Passed authoritative Walk skin validation";
            NotifyCreatureReady(revision);
            yield break;
        }
        RejectCurrent("Runtime skinned-mesh validation failed. " + failure);
    }

    string ValidateActiveSkinnedMeshes()
    {
        for (int rendererIndex = 0; rendererIndex < activeRenderers.Count; rendererIndex++)
        {
            if (!(activeRenderers[rendererIndex] is SkinnedMeshRenderer renderer)
                || !renderer.enabled
                || renderer.sharedMesh == null)
                continue;

            Mesh source = renderer.sharedMesh;
            if (!source.isReadable
                && motionMode == NmsCreatureMotionMode.ImportedAnimator)
                return $"Renderer '{renderer.name}' source mesh is not readable; " +
                    "animated deformation validation cannot run.";
            var baked = new Mesh { name = $"{renderer.name}_Validation" };
            try
            {
                renderer.BakeMesh(baked, false);
                Vector3[] bakedVertices = baked.vertices;
                if (bakedVertices.Length == 0
                    || bakedVertices.Length != source.vertexCount)
                    return $"Renderer '{renderer.name}' produced an invalid baked vertex count.";

                for (int vertexIndex = 0; vertexIndex < bakedVertices.Length; vertexIndex++)
                    if (!IsFinite(bakedVertices[vertexIndex]))
                        return $"Renderer '{renderer.name}' produced a non-finite vertex.";

                Vector3 rendererScale = renderer.transform.lossyScale;
                float sourceScale = Mathf.Max(
                    Mathf.Abs(rendererScale.x),
                    Mathf.Abs(rendererScale.y),
                    Mathf.Abs(rendererScale.z));
                sourceScale = Mathf.Max(0.0001f, sourceScale);
                float sourceSpan = Mathf.Max(
                    source.bounds.size.magnitude * sourceScale, 0.0001f);
                float bakedSpan = baked.bounds.size.magnitude;
                float boundsMultiplier = bakedSpan / sourceSpan;
                if (!IsFinite(bakedSpan) || bakedSpan <= 0.0001f)
                    return $"Renderer '{renderer.name}' produced invalid baked bounds.";
                if (motionMode == NmsCreatureMotionMode.ImportedAnimator
                    && boundsMultiplier > maximumBakedBoundsMultiplier)
                    return $"Renderer '{renderer.name}' bounds expanded {boundsMultiplier:F2}x " +
                        $"({sourceSpan:F3} -> {bakedSpan:F3}); its Bind Pose is incompatible.";

                if (motionMode == NmsCreatureMotionMode.Procedural)
                    continue;

                Vector3[] sourceVertices = source.vertices;
                int[] triangles = source.triangles;
                int triangleCount = triangles.Length / 3;
                int stride = Mathf.Max(1, triangleCount / 4096);
                float worstStretch = 1f;
                for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex += stride)
                {
                    int offset = triangleIndex * 3;
                    worstStretch = Mathf.Max(
                        worstStretch,
                        EdgeStretch(sourceVertices, bakedVertices,
                            triangles[offset], triangles[offset + 1], sourceScale),
                        EdgeStretch(sourceVertices, bakedVertices,
                            triangles[offset + 1], triangles[offset + 2], sourceScale),
                        EdgeStretch(sourceVertices, bakedVertices,
                            triangles[offset + 2], triangles[offset], sourceScale));
                    if (!IsFinite(worstStretch) || worstStretch > maximumTriangleEdgeStretch)
                        return $"Renderer '{renderer.name}' triangle edge stretched {worstStretch:F2}x; " +
                            "its skin weights or Bind Pose are incompatible with this animation.";
                }
            }
            finally
            {
                Destroy(baked);
            }
        }
        return null;
    }

    static float EdgeStretch(
        Vector3[] sourceVertices,
        Vector3[] bakedVertices,
        int first,
        int second,
        float sourceScale)
    {
        if ((uint)first >= sourceVertices.Length || (uint)second >= sourceVertices.Length)
            return float.PositiveInfinity;
        float sourceLength = Vector3.Distance(
            sourceVertices[first], sourceVertices[second]) * sourceScale;
        if (sourceLength <= 0.00001f)
            return 1f;
        return Vector3.Distance(bakedVertices[first], bakedVertices[second]) / sourceLength;
    }

    void SetActiveRenderersForceOff(bool value)
    {
        for (int i = 0; i < activeRenderers.Count; i++)
            if (activeRenderers[i] != null)
                activeRenderers[i].forceRenderingOff = value;
    }

    NmsCreatureFamilyDefinition SelectFamily(ref StableRandom random)
    {
        if (!string.IsNullOrWhiteSpace(forcedFamilyId))
        {
            if (catalog.TryGetFamily(forcedFamilyId.Trim(), out NmsCreatureFamilyDefinition forced))
                return forced;
            Debug.LogError($"Forced NMS family '{forcedFamilyId}' is not in the catalog.", this);
            return null;
        }

        int total = 0;
        for (int i = 0; i < catalog.Families.Count; i++)
        {
            NmsCreatureFamilyDefinition family = catalog.Families[i];
            if (family != null && IsFirstSkeletonFamily(i))
                total += family.SelectionWeight;
        }
        if (total <= 0)
            return null;

        int cursor = random.Next(total);
        for (int i = 0; i < catalog.Families.Count; i++)
        {
            NmsCreatureFamilyDefinition family = catalog.Families[i];
            if (family == null || !IsFirstSkeletonFamily(i))
                continue;
            cursor -= family.SelectionWeight;
            if (cursor < 0)
                return SelectVariantForSkeleton(
                    SkeletonSelectionKey(family),
                    ref random);
        }
        return null;
    }

    bool IsFirstSkeletonFamily(int index)
    {
        NmsCreatureFamilyDefinition family = catalog.Families[index];
        string key = SkeletonSelectionKey(family);
        for (int i = 0; i < index; i++)
        {
            NmsCreatureFamilyDefinition previous = catalog.Families[i];
            if (previous != null
                && string.Equals(
                    SkeletonSelectionKey(previous),
                    key,
                    StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    NmsCreatureFamilyDefinition SelectVariantForSkeleton(
        string skeletonKey,
        ref StableRandom random)
    {
        int total = 0;
        for (int i = 0; i < catalog.Families.Count; i++)
        {
            NmsCreatureFamilyDefinition family = catalog.Families[i];
            if (family != null
                && string.Equals(
                    SkeletonSelectionKey(family),
                    skeletonKey,
                    StringComparison.Ordinal))
                total += family.SelectionWeight;
        }
        if (total <= 0)
            return null;

        int cursor = random.Next(total);
        for (int i = 0; i < catalog.Families.Count; i++)
        {
            NmsCreatureFamilyDefinition family = catalog.Families[i];
            if (family == null
                || !string.Equals(
                    SkeletonSelectionKey(family),
                    skeletonKey,
                    StringComparison.Ordinal))
                continue;
            cursor -= family.SelectionWeight;
            if (cursor < 0)
                return family;
        }
        return null;
    }

    static string SkeletonSelectionKey(NmsCreatureFamilyDefinition family)
    {
        return !string.IsNullOrEmpty(family.SkeletonHash)
            ? family.SkeletonHash
            : family.FamilyId;
    }

    static HashSet<string> SelectModules(
        NmsDescriptorGroupData[] groups,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules,
        ref StableRandom random)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (groups == null)
            return selected;
        for (int i = 0; i < groups.Length; i++)
        {
            NmsDescriptorGroupData group = groups[i];
            if (group != null && (group.path == null || group.path.Length == 0))
                VisitGroup(
                    group,
                    selected,
                    allowRareModules,
                    excludedModulePrefixes,
                    incompatibleModules,
                    ref random);
        }
        return selected;
    }

    static void VisitGroup(
        NmsDescriptorGroupData group,
        HashSet<string> selected,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules,
        ref StableRandom random)
    {
        if (group.candidates == null || group.candidates.Length == 0)
            return;
        NmsDescriptorCandidateData chosen = WeightedChoice(
            group.candidates,
            allowRareModules,
            excludedModulePrefixes,
            incompatibleModules,
            ref random);
        if (chosen == null)
            return;
        if (!string.IsNullOrEmpty(chosen.name))
            selected.Add(chosen.name);
        if (chosen.moduleIds != null)
            for (int i = 0; i < chosen.moduleIds.Length; i++)
                if (!string.IsNullOrEmpty(chosen.moduleIds[i]))
                    selected.Add(chosen.moduleIds[i]);
        if (chosen.children == null)
            return;
        for (int i = 0; i < chosen.children.Length; i++)
            if (chosen.children[i] != null)
                VisitGroup(
                    chosen.children[i],
                    selected,
                    allowRareModules,
                    excludedModulePrefixes,
                    incompatibleModules,
                    ref random);
    }

    static NmsDescriptorCandidateData WeightedChoice(
        NmsDescriptorCandidateData[] candidates,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules,
        ref StableRandom random)
    {
        float total = 0f;
        int allowedCount = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            NmsDescriptorCandidateData candidate = candidates[i];
            if (!IsAllowedCandidate(
                candidate,
                allowRareModules,
                excludedModulePrefixes,
                incompatibleModules))
                continue;
            allowedCount++;
            if (candidate.chance > 0f)
                total += candidate.chance;
        }
        if (allowedCount == 0)
            return null;
        if (total <= 0f)
        {
            int selectedIndex = random.Next(allowedCount);
            for (int i = 0; i < candidates.Length; i++)
            {
                NmsDescriptorCandidateData candidate = candidates[i];
                if (!IsAllowedCandidate(
                        candidate,
                        allowRareModules,
                        excludedModulePrefixes,
                        incompatibleModules))
                    continue;
                if (selectedIndex-- == 0)
                    return candidate;
            }
            return null;
        }

        float cursor = random.NextFloat() * total;
        NmsDescriptorCandidateData fallback = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            NmsDescriptorCandidateData candidate = candidates[i];
            if (!IsAllowedCandidate(
                    candidate,
                    allowRareModules,
                    excludedModulePrefixes,
                    incompatibleModules)
                || candidate.chance <= 0f)
                continue;
            fallback = candidate;
            cursor -= candidate.chance;
            if (cursor <= 0f)
                return candidate;
        }
        return fallback;
    }

    static bool IsAllowedCandidate(
        NmsDescriptorCandidateData candidate,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules)
    {
        if (candidate == null)
            return false;
        if (MatchesExcludedPrefix(candidate.id, excludedModulePrefixes)
            || MatchesExcludedPrefix(candidate.name, excludedModulePrefixes)
            || IsIncompatible(candidate.id, incompatibleModules)
            || IsIncompatible(candidate.name, incompatibleModules))
            return false;
        if (!allowRareModules
            && (IsRareId(candidate.id) || IsRareId(candidate.name)))
            return false;
        if (candidate.moduleIds != null)
            for (int i = 0; i < candidate.moduleIds.Length; i++)
                if (MatchesExcludedPrefix(
                        candidate.moduleIds[i],
                        excludedModulePrefixes)
                    || IsIncompatible(candidate.moduleIds[i], incompatibleModules)
                    || (!allowRareModules && IsRareId(candidate.moduleIds[i])))
                    return false;
        return true;
    }

    static HashSet<string> BuildIncompatibleModuleSet(NmsCreatureModuleData[] modules)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (modules == null)
            return result;
        for (int i = 0; i < modules.Length; i++)
            if (modules[i] != null
                && !modules[i].compatible
                && !string.IsNullOrEmpty(modules[i].name))
                result.Add(modules[i].name);
        return result;
    }

    static bool IsIncompatible(string value, HashSet<string> incompatibleModules)
    {
        return !string.IsNullOrEmpty(value)
            && incompatibleModules != null
            && incompatibleModules.Contains(value);
    }

    static bool MatchesExcludedPrefix(
        string value,
        IReadOnlyList<string> excludedModulePrefixes)
    {
        if (string.IsNullOrEmpty(value) || excludedModulePrefixes == null)
            return false;
        for (int i = 0; i < excludedModulePrefixes.Count; i++)
        {
            string prefix = excludedModulePrefixes[i];
            if (!string.IsNullOrEmpty(prefix)
                && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static bool IsRareId(string value)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf("RARE", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void ApplyModules(HashSet<string> selected, Color primary, Color secondary, Color accent)
    {
        activeRenderers.Clear();
        Renderer[] renderers = currentCreature.GetComponentsInChildren<Renderer>(true);
        var controlled = new HashSet<Renderer>();
        for (int i = 0; i < currentFamily.ModuleBindings.Count; i++)
        {
            NmsCreatureModuleBinding binding = currentFamily.ModuleBindings[i];
            bool enable = selected.Contains(binding.moduleId);
            for (int pathIndex = 0; pathIndex < binding.rendererPaths.Length; pathIndex++)
            {
                Transform target = currentCreature.transform.Find(binding.rendererPaths[pathIndex]);
                Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
                if (renderer == null)
                    continue;
                controlled.Add(renderer);
                renderer.enabled = enable;
                if (enable)
                    activeRenderers.Add(renderer);
            }
        }
        for (int i = 0; i < renderers.Length; i++)
        {
            if (controlled.Contains(renderers[i]))
                continue;
            renderers[i].enabled = false;
        }

        ApplyPalette(primary, secondary, accent);
    }

    void ApplyPalette(Color primary, Color secondary, Color accent)
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
        for (int i = 0; i < activeRenderers.Count; i++)
        {
            Renderer renderer = activeRenderers[i];
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorId, primary);
            propertyBlock.SetColor(BaseColorId, primary);
            propertyBlock.SetColor(PrimaryColorId, primary);
            propertyBlock.SetColor(SecondaryColorId, secondary);
            propertyBlock.SetColor(AccentColorId, accent);
            renderer.SetPropertyBlock(propertyBlock);
            propertyBlock.Clear();
        }
    }

    void CaptureSegmentInvariants()
    {
        segmentInvariants.Clear();
        invariantReported = false;
        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        SkinnedMeshRenderer[] skinnedRenderers =
            currentCreature.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int rendererIndex = 0; rendererIndex < skinnedRenderers.Length; rendererIndex++)
        {
            Transform[] rendererBones = skinnedRenderers[rendererIndex].bones;
            for (int boneIndex = 0; boneIndex < rendererBones.Length; boneIndex++)
            {
                Transform bone = rendererBones[boneIndex];
                if (bone != null && !bones.ContainsKey(bone.name))
                    bones.Add(bone.name, bone);
            }
        }

        for (int chainIndex = 0; chainIndex < currentFamily.LoadBearingChains.Count; chainIndex++)
        {
            NmsCreatureLegChain chain = currentFamily.LoadBearingChains[chainIndex];
            for (int i = 0; i + 1 < chain.bones.Length; i++)
            {
                if (!bones.TryGetValue(chain.bones[i], out Transform parent)
                    || !bones.TryGetValue(chain.bones[i + 1], out Transform child))
                {
                    ReportInvariant($"Family '{currentFamily.FamilyId}' is missing chain '{chain.id}'.");
                    return;
                }
                float length = Vector3.Distance(parent.position, child.position);
                if (length <= 0.000001f || child.parent != parent)
                {
                    ReportInvariant($"Family '{currentFamily.FamilyId}' has a non-contiguous chain '{chain.id}'.");
                    return;
                }
                segmentInvariants.Add(new SegmentInvariant
                {
                    id = $"{chain.id}:{parent.name}->{child.name}",
                    parent = parent,
                    child = child,
                    length = length
                });
            }
        }
    }

    void MoveAlongSphere(float distance)
    {
        Transform target = currentCreature.transform;
        Vector3 center = gravitySource.Center;
        Vector3 currentUp = gravitySource.GetUp(target.position);
        Vector3 tangentForward = Vector3.ProjectOnPlane(
            target.TransformDirection(currentFamily.LocalForwardAxis),
            currentUp);
        if (tangentForward.sqrMagnitude <= 0.000001f)
            return;
        tangentForward.Normalize();
        Vector3 orbitAxis = Vector3.Cross(currentUp, tangentForward);
        if (orbitAxis.sqrMagnitude <= 0.000001f)
            return;
        orbitAxis.Normalize();

        float radius = Mathf.Max(gravitySource.Radius, Vector3.Distance(target.position, center));
        Vector3 nextUp = Quaternion.AngleAxis(distance / radius * Mathf.Rad2Deg, orbitAxis) * currentUp;
        nextUp.Normalize();
        Vector3 surface = FindSurface(nextUp);
        target.position = surface + nextUp * currentFamily.SurfaceClearance;

        Quaternion transported = Quaternion.FromToRotation(currentUp, nextUp) * target.rotation;
        Vector3 forward = Vector3.ProjectOnPlane(
            transported * currentFamily.LocalForwardAxis,
            nextUp);
        if (forward.sqrMagnitude > 0.000001f)
            target.rotation = RotationForLocalForward(
                currentFamily.LocalForwardAxis,
                forward.normalized,
                nextUp);
    }

    bool HasAnimatedBody()
    {
        for (int i = 0; i < activeRenderers.Count; i++)
        {
            if (activeRenderers[i] is SkinnedMeshRenderer skinned
                && skinned.enabled
                && skinned.sharedMesh != null
                && skinned.bones.Length > 0)
                return true;
        }
        return false;
    }

    static Quaternion RotationForLocalForward(
        Vector3 localForward,
        Vector3 worldForward,
        Vector3 worldUp)
    {
        Vector3 axis = localForward.sqrMagnitude > 0.000001f
            ? localForward.normalized
            : Vector3.forward;
        Quaternion localBasis = Quaternion.LookRotation(axis, Vector3.up);
        return Quaternion.LookRotation(worldForward, worldUp)
            * Quaternion.Inverse(localBasis);
    }

    Vector3 FindSurface(Vector3 radialUp)
    {
        Vector3 origin = gravitySource.Center
            + radialUp * (gravitySource.Radius + surfaceProbeHeight);
        if (Physics.Raycast(
            origin,
            -radialUp,
            out RaycastHit hit,
            surfaceProbeHeight * 2f,
            groundLayers,
            QueryTriggerInteraction.Ignore))
            return hit.point;
        return gravitySource.GetSurfacePoint(radialUp);
    }

    void DestroyCurrent()
    {
        generationRevision++;
        activeRenderers.Clear();
        segmentInvariants.Clear();
        currentAnimator = null;
        currentMotionController = null;
        currentImportedController = null;
        currentFamily = null;
        currentSpecies = null;
        readyRevision = -1;
        CurrentVariantValidated = false;
        if (currentCreature == null)
            return;
        currentCreature.SetActive(false);
        Destroy(currentCreature);
        currentCreature = null;
    }

    void OnDestroy()
    {
        DestroyCurrent();
    }

    void ReportInvariant(string message)
    {
        invariantReported = true;
        if (currentAnimator != null)
            currentAnimator.enabled = false;
        if (currentMotionController != null)
            currentMotionController.enabled = false;
        if (currentImportedController != null)
            currentImportedController.enabled = false;
        LastValidationResult = message;
        Debug.LogError("NMS native rig invariant failed: " + message, this);
    }

    void NotifyCreatureReady(int revision)
    {
        if (revision != generationRevision || revision == readyRevision || currentCreature == null)
            return;
        readyRevision = revision;
        CreatureReady?.Invoke(new NmsCreatureSpawnContext(
            currentCreature, currentFamily, currentSpecies,
            currentAnimator, currentMotionController, currentImportedController));
    }

    void RejectCurrent(string failure)
    {
        int failedSeed = seed;
        string failedFamily = currentFamily != null ? currentFamily.FamilyId : "<missing>";
        string failedSignature = currentSpecies != null ? currentSpecies.signature : "<missing>";
        Debug.LogError(
            $"NMS species rejected. Family={failedFamily}, Seed={failedSeed}, " +
            $"Signature={failedSignature}. {failure}",
            currentCreature != null ? currentCreature : gameObject);
        LastValidationResult = failure;
        automaticRetryCount++;
        if (automaticRetryCount > maximumAutomaticRetries)
        {
            if (!forceStandardVariant
                && authorizedAntelopeLibrary != null
                && string.Equals(failedFamily, authorizedAntelopeLibrary.FamilyId,
                    StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    "Random variants failed validation; using the canonical standard variant.",
                    this);
                forceStandardVariant = true;
                automaticRetryCount = 0;
                GenerateInternal(failedSeed);
                return;
            }
            Debug.LogError(
                $"NMS generation stopped after {maximumAutomaticRetries} invalid species.", this);
            DestroyCurrent();
            return;
        }
        GenerateInternal(unchecked(failedSeed + 1));
    }

    static void CreatePalette(
        ref StableRandom random,
        out Color primary,
        out Color secondary,
        out Color accent)
    {
        float hue = random.NextFloat();
        primary = Color.HSVToRGB(hue, random.Range(0.38f, 0.68f), random.Range(0.5f, 0.82f));
        secondary = Color.HSVToRGB(
            Mathf.Repeat(hue + random.Range(-0.12f, 0.12f), 1f),
            random.Range(0.28f, 0.58f),
            random.Range(0.58f, 0.9f));
        accent = Color.HSVToRGB(
            Mathf.Repeat(hue + random.Range(0.38f, 0.62f), 1f),
            random.Range(0.45f, 0.78f),
            random.Range(0.62f, 0.95f));
    }

    static string[] Sorted(HashSet<string> values)
    {
        var result = new string[values.Count];
        values.CopyTo(result);
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    static string BuildSignature(
        string familyId,
        int valueSeed,
        HashSet<string> modules,
        Color primary,
        Color accent)
    {
        string[] sorted = Sorted(modules);
        var builder = new StringBuilder(familyId).Append('|').Append(valueSeed);
        for (int i = 0; i < sorted.Length; i++)
            builder.Append('|').Append(sorted[i]);
        builder.Append('|').Append(ColorUtility.ToHtmlStringRGB(primary));
        builder.Append('|').Append(ColorUtility.ToHtmlStringRGB(accent));
        return builder.ToString();
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    static ulong StableHash(string value)
    {
        ulong hash = 1469598103934665603UL;
        if (value == null)
            return hash;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

#if UNITY_EDITOR
    public void ConfigureImported(
        NmsCreatureFamilyCatalog importedCatalog,
        SphericalGravitySource importedGravitySource,
        BioCreatureFollowCamera importedCamera,
        LayerMask importedGroundLayers)
    {
        catalog = importedCatalog;
        gravitySource = importedGravitySource;
        followCamera = importedCamera;
        groundLayers = importedGroundLayers;
        forcedFamilyId = string.Empty;
        regenerateWithR = true;
        moveAlongGreatCircle = true;
        validateRigContinuously = true;
        motionMode = NmsCreatureMotionMode.ImportedAnimator;
        proceduralMotionProfile = null;
        authorizedAntelopeLibrary = null;
    }
#endif

    struct SegmentInvariant
    {
        public string id;
        public Transform parent;
        public Transform child;
        public float length;
    }

    struct StableRandom
    {
        ulong state;

        public StableRandom(ulong seedValue)
        {
            state = seedValue != 0UL ? seedValue : 0x9E3779B97F4A7C15UL;
        }

        public int Next(int maximum)
        {
            return maximum <= 1 ? 0 : (int)(NextUInt64() % (uint)maximum);
        }

        public float NextFloat()
        {
            return (NextUInt64() >> 40) * (1f / 16777216f);
        }

        public float Range(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, NextFloat());
        }

        ulong NextUInt64()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
