using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct NmsAntelopeVariantDescriptor
{
    public readonly int seed;
    public readonly Vector3 visualScale;
    public readonly NmsCreatureSpeciesDefinition species;

    public NmsAntelopeVariantDescriptor(
        int variantSeed,
        Vector3 scale,
        NmsCreatureSpeciesDefinition definition)
    {
        seed = variantSeed;
        visualScale = scale;
        species = definition;
    }
}

[DisallowMultipleComponent]
public sealed class Bio1AntelopeVariantShowcase : MonoBehaviour
{
    const string FamilyId = "AntelopeQuadruped";
    const int VariantCount = 3;
    const float MinimumGroundClearance = 0.01f;
    const float MaximumBoundsMultiplier = 4f;
    const float MaximumTriangleStretch = 6f;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int PrimaryColorId = Shader.PropertyToID("_PrimaryColor");
    static readonly int SecondaryColorId = Shader.PropertyToID("_SecondaryColor");
    static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");

    [Header("Authorized Antelope")]
    [SerializeField] NmsCreatureFamilyCatalog catalog;
    [SerializeField] NmsAuthorizedAntelopeLibrary authorizedLibrary;
    [SerializeField] int masterSeed = 12345;

    [Header("Spherical Showcase")]
    [SerializeField] SphericalGravitySource gravitySource;
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField] BioCreatureFollowCamera followCamera;
    [SerializeField] Vector3 spawnDirection = Vector3.up;
    [SerializeField, Min(1f)] float surfaceProbeHeight = 20f;
    [SerializeField, Range(0.5f, 3f)] float walkPlaybackSpeed = 1.8f;
    [SerializeField, Min(0.1f)] float sideGap = 0.75f;
    [SerializeField] bool showTelemetry = true;
    [SerializeField] bool enableAppearanceEditor = true;

    readonly RuntimeVariant[] variants = new RuntimeVariant[VariantCount];
    readonly NmsAntelopeVariantDescriptor[] descriptors =
        new NmsAntelopeVariantDescriptor[VariantCount];

    GameObject activeTrioRoot;
    GameObject pendingTrioRoot;
    Transform cameraTarget;
    NmsCreatureFamilyDefinition family;
    Coroutine buildRoutine;
    Coroutine appearanceBuildRoutine;
    int buildRevision;
    int appearanceBuildRevision;
    float walkPhase;
    float clipLength = 1f;
    Vector3 groupUp;
    Vector3 groupForward;
    float groupWidth;
    float groupHeight;
    GUIStyle telemetryStyle;
    Vector2 appearanceScroll;
    NmsAntelopeEditingOptions editingOptions;
    NmsAntelopeVariantParameters editDraft;

    public bool IsAppearanceEditorOpen { get; private set; }
    public bool IsApplyingAppearance { get; private set; }
    public int SelectedVariantIndex { get; private set; }
    public NmsAntelopeVariantParameters EditDraft => editDraft;

    public int MasterSeed => masterSeed;
    public bool IsReady { get; private set; }
    public float WalkPhase => walkPhase;
    public IReadOnlyList<NmsAntelopeVariantDescriptor> CurrentVariants => descriptors;
    public string LastValidationResult { get; private set; } = "尚未生成";

    void Start()
    {
        GenerateTrio(masterSeed);
    }

    void Update()
    {
        if (enableAppearanceEditor && Input.GetKeyDown(KeyCode.E))
        {
            if (IsAppearanceEditorOpen)
                CloseAppearanceEditor();
            else
                OpenAppearanceEditor();
            return;
        }
        if (IsAppearanceEditorOpen)
            return;
        if (Input.GetKeyDown(KeyCode.R))
        {
            Regenerate();
            return;
        }
        if (!IsReady || family == null)
            return;

        walkPhase = Mathf.Repeat(
            walkPhase + Time.deltaTime * walkPlaybackSpeed / Mathf.Max(0.01f, clipLength),
            1f);
        SampleSynchronizedWalk(walkPhase);
    }

    void FixedUpdate()
    {
        if (IsAppearanceEditorOpen
            || !IsReady || family == null || gravitySource == null)
            return;
        AdvanceFormation(
            family.WalkSpeed * walkPlaybackSpeed * Time.fixedDeltaTime);
    }

    public void GenerateTrio(int seed)
    {
        masterSeed = seed;
        buildRevision++;
        if (buildRoutine != null)
            StopCoroutine(buildRoutine);
        if (pendingTrioRoot != null)
        {
            pendingTrioRoot.SetActive(false);
            Destroy(pendingTrioRoot);
            pendingTrioRoot = null;
        }
        buildRoutine = StartCoroutine(BuildTrio(buildRevision));
    }

    public void Regenerate()
    {
        GenerateTrio(unchecked(masterSeed + 1));
    }

    IEnumerator BuildTrio(int revision)
    {
        LastValidationResult = "正在后台生成三只生物";
        if (!ValidateConfiguration(out string configurationError))
        {
            FailBuild(configurationError, null);
            yield break;
        }

        pendingTrioRoot = new GameObject($"Bio1_AntelopeTrio_Pending_{masterSeed}");
        pendingTrioRoot.transform.SetParent(transform, false);
        Vector3[] scales = CreateVariantScales(masterSeed);
        var pendingVariants = new RuntimeVariant[VariantCount];
        RigRestSnapshot canonicalRest = null;
        AnimationClip canonicalClip = null;

        for (int i = 0; i < VariantCount; i++)
        {
            int variantSeed = DeriveVariantSeed(masterSeed, i);
            if (!TryBuildVariant(
                pendingTrioRoot.transform, i, variantSeed, scales[i],
                false, out RuntimeVariant variant, out string buildError))
            {
                FailBuild(buildError, pendingTrioRoot);
                yield break;
            }
            pendingVariants[i] = variant;

            if (canonicalRest == null)
            {
                canonicalRest = variant.restSnapshot;
                canonicalClip = variant.walkClip;
                clipLength = Mathf.Max(0.01f, canonicalClip.length);
            }
            else
            {
                if (!canonicalRest.Matches(variant.restSnapshot, out string restError))
                {
                    FailBuild("Canonical rig mismatch. " + restError, pendingTrioRoot);
                    yield break;
                }
                if (variant.walkClip != canonicalClip)
                {
                    FailBuild("The three variants do not reference the same Walk clip.", pendingTrioRoot);
                    yield break;
                }
            }
        }

        yield return null;
        if (revision != buildRevision || pendingTrioRoot == null)
            yield break;

        bool trioValidated = false;
        string lastPoseError = null;
        for (int validationPass = 0;
            validationPass <= VariantCount && !trioValidated;
            validationPass++)
        {
            for (int i = 0; i < VariantCount; i++)
                ResetAccumulatedBounds(pendingVariants[i]);
            int failedVariant = -1;
            for (int sample = 0; sample < 8 && failedVariant < 0; sample++)
            {
                float phase = sample / 8f;
                for (int i = 0; i < VariantCount; i++)
                {
                    RuntimeVariant variant = pendingVariants[i];
                    SampleAnimator(variant, phase);
                    if (!ValidateVariantPose(variant, sample, out string poseError))
                    {
                        failedVariant = i;
                        lastPoseError = $"Variant {i} failed Walk validation at "
                            + $"phase {phase:F3}. {poseError}";
                        break;
                    }
                    AccumulateBounds(variant);
                }
            }
            if (failedVariant < 0)
            {
                trioValidated = true;
                break;
            }

            RuntimeVariant failed = pendingVariants[failedVariant];
            if (failed.usesStandardModules)
                break;
            Debug.LogWarning(
                lastPoseError + " Retrying this slot with the standard safe modules.", this);
            failed.slot.gameObject.SetActive(false);
            Destroy(failed.slot.gameObject);
            int fallbackSeed = DeriveVariantSeed(masterSeed, failedVariant);
            if (!TryBuildVariant(
                pendingTrioRoot.transform, failedVariant, fallbackSeed,
                scales[failedVariant], true,
                out RuntimeVariant fallback, out string fallbackError))
            {
                lastPoseError += " Standard fallback failed. " + fallbackError;
                break;
            }
            if (!canonicalRest.Matches(fallback.restSnapshot, out string fallbackRestError)
                || fallback.walkClip != canonicalClip)
            {
                lastPoseError += " Standard fallback changed the canonical rig or Walk. "
                    + fallbackRestError;
                break;
            }
            pendingVariants[failedVariant] = fallback;
            yield return null;
        }

        if (!trioValidated)
        {
            FailBuild(lastPoseError ?? "Walk validation did not complete.", pendingTrioRoot);
            yield break;
        }

        for (int i = 0; i < VariantCount; i++)
        {
            RuntimeVariant variant = pendingVariants[i];
            variant.visualScale.localPosition += Vector3.up
                * (MinimumGroundClearance - variant.minimumLocalY);
            SampleAnimator(variant, 0f);
        }
        CalculateFormationOffsets(pendingVariants);

        if (revision != buildRevision)
            yield break;
        GameObject oldRoot = activeTrioRoot;
        activeTrioRoot = pendingTrioRoot;
        pendingTrioRoot = null;
        activeTrioRoot.name = $"Bio1_AntelopeTrio_{masterSeed}";
        for (int i = 0; i < VariantCount; i++)
        {
            variants[i] = pendingVariants[i];
            descriptors[i] = pendingVariants[i].descriptor;
            SetRenderingHidden(variants[i].renderers, false);
        }
        if (oldRoot != null)
        {
            oldRoot.SetActive(false);
            Destroy(oldRoot);
        }

        InitializeFormationIfNeeded();
        EnsureCameraTarget();
        ApplyFormation();
        followCamera.SetTarget(cameraTarget, true);
        followCamera.SetFraming(groupWidth, groupHeight, true);
        walkPhase = 0f;
        SampleSynchronizedWalk(walkPhase);
        IsReady = true;
        LastValidationResult = "已通过统一骨架与 8 相位步行动画验证";
        buildRoutine = null;
        Debug.Log(
            $"Bio 1 antelope trio ready. Seed={masterSeed}, "
            + $"Width={groupWidth:F2}, Height={groupHeight:F2}", this);
    }

    bool ValidateConfiguration(out string error)
    {
        error = null;
        if (catalog == null || authorizedLibrary == null
            || gravitySource == null || followCamera == null)
        {
            error = "Bio 1 showcase requires catalog, authorized library, gravity and camera.";
            return false;
        }
        if (!catalog.TryGetFamily(FamilyId, out family) || family == null)
        {
            error = $"NMS catalog has no '{FamilyId}' family.";
            return false;
        }
        if (!authorizedLibrary.CanBuild(FamilyId)
            || authorizedLibrary.AnimatorController == null)
        {
            error = "Authorized antelope library is incomplete.";
            return false;
        }
        if (!NmsAntelopeEditingOptions.TryCreate(
            family, out editingOptions, out error))
            return false;
        return true;
    }

    bool TryBuildVariant(
        Transform parent,
        int index,
        int variantSeed,
        Vector3 scale,
        bool forceStandardModules,
        out RuntimeVariant variant,
        out string error,
        NmsCreatureSpeciesDefinition explicitSpecies = null)
    {
        variant = null;
        error = null;
        NmsCreatureSpeciesDefinition species = explicitSpecies;
        if (species == null
            && !NmsCreatureVariantSampler.TrySample(
                family, variantSeed, out species,
                out error, forceStandardModules))
            return false;

        var slotObject = new GameObject($"FormationSlot_{index}");
        Transform slot = slotObject.transform;
        slot.SetParent(parent, false);
        var scaleObject = new GameObject($"VisualScale_{scale.x:F3}_{scale.y:F3}_{scale.z:F3}");
        Transform visualScale = scaleObject.transform;
        visualScale.SetParent(slot, false);
        visualScale.localScale = scale;
        var renderers = new List<Renderer>(16);

        bool built = authorizedLibrary.TryBuild(
            visualScale, species.selectedModules, renderers,
            out GameObject root, out Animator animator, out string assemblyError);
        bool usesStandardModules = forceStandardModules;
        if (!built && !forceStandardModules && explicitSpecies == null)
        {
            if (!NmsCreatureVariantSampler.TrySample(
                family, variantSeed, out species, out string fallbackSampleError, true))
            {
                error = "Standard variant sampling failed. " + fallbackSampleError;
                Destroy(slotObject);
                return false;
            }
            built = authorizedLibrary.TryBuild(
                visualScale, species.selectedModules, renderers,
                out root, out animator, out assemblyError);
            usesStandardModules = true;
        }
        if (!built || root == null || animator == null)
        {
            error = "Authorized assembly failed, including standard fallback. " + assemblyError;
            Destroy(slotObject);
            return false;
        }

        root.name = $"Variant_{index}_{variantSeed}";
        SetLayerRecursively(root.transform, 2);
        ApplyPalette(renderers, species);
        SetRenderingHidden(renderers, true);
        Transform armature = animator.transform.Find("Armature");
        if (armature == null)
        {
            error = "CanonicalRig has no direct Armature child.";
            Destroy(slotObject);
            return false;
        }
        RigRestSnapshot restSnapshot = RigRestSnapshot.Capture(armature);
        if (!ConfigureAnimator(animator, out AnimationClip walkClip, out error))
        {
            Destroy(slotObject);
            return false;
        }
        if (!ValidateRendererBindings(renderers, out error))
        {
            Destroy(slotObject);
            return false;
        }

        Dictionary<string, float> segmentLengths =
            CaptureSegmentLengths(visualScale, family);
        int expectedSegments = 0;
        for (int i = 0; i < family.LoadBearingChains.Count; i++)
            expectedSegments += Mathf.Max(
                0, family.LoadBearingChains[i].bones.Length - 1);
        if (segmentLengths.Count != expectedSegments)
        {
            error = $"Canonical load-bearing chains contain {segmentLengths.Count}/"
                + $"{expectedSegments} valid segments.";
            Destroy(slotObject);
            return false;
        }

        variant = new RuntimeVariant
        {
            slot = slot,
            visualScale = visualScale,
            animator = animator,
            renderers = renderers,
            walkClip = walkClip,
            restSnapshot = restSnapshot,
            family = this.family,
            descriptor = new NmsAntelopeVariantDescriptor(variantSeed, scale, species),
            parameters = NmsAntelopeVariantParameters.FromDescriptor(
                new NmsAntelopeVariantDescriptor(variantSeed, scale, species)),
            usesStandardModules = usesStandardModules,
            minimumLocalY = float.PositiveInfinity,
            maximumLocalY = float.NegativeInfinity,
            minimumLocalX = float.PositiveInfinity,
            maximumLocalX = float.NegativeInfinity,
            referenceSegmentLengths = segmentLengths
        };
        return true;
    }

    bool ConfigureAnimator(
        Animator animator, out AnimationClip walkClip, out string error)
    {
        walkClip = null;
        error = null;
        animator.enabled = true;
        animator.runtimeAnimatorController = authorizedLibrary.AnimatorController;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;

        bool hasSpeed = false;
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == authorizedLibrary.SpeedParameter
                && parameters[i].type == AnimatorControllerParameterType.Float)
                hasSpeed = true;
        int stateHash = Animator.StringToHash(
            "Base Layer." + authorizedLibrary.LocomotionState);
        if (!hasSpeed || !animator.HasState(0, stateHash))
        {
            error = "Authorized Animator is missing Locomotion or Speed.";
            return false;
        }

        AnimationClip[] clips = authorizedLibrary.AnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
                continue;
            if (walkClip == null)
                walkClip = clips[i];
            else if (clips[i] != walkClip)
            {
                error = "Authorized controller contains more than one runtime animation clip.";
                return false;
            }
        }
        if (walkClip == null || walkClip.name != "AntelopeSafeWalk")
        {
            error = "Authorized controller does not resolve exclusively to AntelopeSafeWalk.";
            return false;
        }

        animator.Rebind();
        animator.SetFloat(authorizedLibrary.SpeedParameter, 0.5f);
        animator.Play(stateHash, 0, 0f);
        animator.Update(0f);
        animator.speed = 0f;
        return true;
    }

    static bool ValidateRendererBindings(List<Renderer> renderers, out string error)
    {
        error = null;
        if (renderers.Count == 0)
        {
            error = "Variant contains no active renderers.";
            return false;
        }
        for (int i = 0; i < renderers.Count; i++)
        {
            if (!(renderers[i] is SkinnedMeshRenderer skinned)
                || skinned.sharedMesh == null
                || skinned.bones == null
                || skinned.bones.Length != skinned.sharedMesh.bindposes.Length)
            {
                error = $"Renderer '{renderers[i]?.name}' has an invalid Bind Pose.";
                return false;
            }
            if (!skinned.sharedMesh.isReadable)
            {
                error = $"Renderer '{skinned.name}' must have Read/Write enabled.";
                return false;
            }
        }
        return true;
    }

    bool ValidateVariantPose(RuntimeVariant variant, int sample, out string error)
    {
        error = null;
        if (!ValidateSegmentLengths(variant, out error))
            return false;
        for (int i = 0; i < variant.renderers.Count; i++)
        {
            var renderer = variant.renderers[i] as SkinnedMeshRenderer;
            Mesh source = renderer.sharedMesh;
            var baked = new Mesh { name = renderer.name + "_Bio1Validation" };
            try
            {
                renderer.BakeMesh(baked, false);
                Vector3[] bakedVertices = baked.vertices;
                if (bakedVertices.Length == 0 || bakedVertices.Length != source.vertexCount)
                {
                    error = $"Renderer '{renderer.name}' baked an invalid vertex count.";
                    return false;
                }
                for (int vertex = 0; vertex < bakedVertices.Length; vertex++)
                    if (!IsFinite(bakedVertices[vertex]))
                    {
                        error = $"Renderer '{renderer.name}' baked a non-finite vertex.";
                        return false;
                    }
                Vector3 rendererScale = renderer.transform.lossyScale;
                float sourceScale = Mathf.Max(
                    Mathf.Abs(rendererScale.x),
                    Mathf.Abs(rendererScale.y),
                    Mathf.Abs(rendererScale.z));
                sourceScale = Mathf.Max(0.0001f, sourceScale);
                float sourceSpan = Mathf.Max(
                    source.bounds.size.magnitude * sourceScale, 0.0001f);
                float bakedSpan = baked.bounds.size.magnitude;
                if (!IsFinite(bakedSpan) || bakedSpan <= 0.0001f
                    || bakedSpan / sourceSpan > MaximumBoundsMultiplier)
                {
                    error = $"Renderer '{renderer.name}' produced unsafe bounds.";
                    return false;
                }
                if (!ValidateTriangleStretch(
                    source, bakedVertices, sourceScale, out float worstStretch))
                {
                    error = $"Renderer '{renderer.name}' stretched an edge "
                        + $"{worstStretch:F2}x at sample {sample}.";
                    return false;
                }
            }
            finally
            {
                Destroy(baked);
            }
        }
        return true;
    }

    static bool ValidateTriangleStretch(
        Mesh source,
        Vector3[] bakedVertices,
        float sourceScale,
        out float worstStretch)
    {
        Vector3[] sourceVertices = source.vertices;
        int[] triangles = source.triangles;
        int triangleCount = triangles.Length / 3;
        int stride = Mathf.Max(1, triangleCount / 2048);
        worstStretch = 1f;
        for (int triangle = 0; triangle < triangleCount; triangle += stride)
        {
            int offset = triangle * 3;
            worstStretch = Mathf.Max(
                worstStretch,
                EdgeStretch(sourceVertices, bakedVertices,
                    triangles[offset], triangles[offset + 1], sourceScale),
                EdgeStretch(sourceVertices, bakedVertices,
                    triangles[offset + 1], triangles[offset + 2], sourceScale),
                EdgeStretch(sourceVertices, bakedVertices,
                    triangles[offset + 2], triangles[offset], sourceScale));
            if (!IsFinite(worstStretch) || worstStretch > MaximumTriangleStretch)
                return false;
        }
        return true;
    }

    static float EdgeStretch(
        Vector3[] source,
        Vector3[] baked,
        int first,
        int second,
        float sourceScale)
    {
        if ((uint)first >= source.Length || (uint)second >= source.Length)
            return float.PositiveInfinity;
        float sourceLength = Vector3.Distance(source[first], source[second])
            * sourceScale;
        if (sourceLength <= 0.00001f)
            return 1f;
        return Vector3.Distance(baked[first], baked[second]) / sourceLength;
    }

    void AccumulateBounds(RuntimeVariant variant)
    {
        for (int i = 0; i < variant.renderers.Count; i++)
        {
            Bounds bounds = variant.renderers[i].bounds;
            Vector3 minimum = variant.slot.InverseTransformPoint(bounds.min);
            Vector3 maximum = variant.slot.InverseTransformPoint(bounds.max);
            variant.minimumLocalX = Mathf.Min(variant.minimumLocalX, minimum.x, maximum.x);
            variant.maximumLocalX = Mathf.Max(variant.maximumLocalX, minimum.x, maximum.x);
            variant.minimumLocalY = Mathf.Min(variant.minimumLocalY, minimum.y, maximum.y);
            variant.maximumLocalY = Mathf.Max(variant.maximumLocalY, minimum.y, maximum.y);
        }
    }

    static void ResetAccumulatedBounds(RuntimeVariant variant)
    {
        variant.minimumLocalY = float.PositiveInfinity;
        variant.maximumLocalY = float.NegativeInfinity;
        variant.minimumLocalX = float.PositiveInfinity;
        variant.maximumLocalX = float.NegativeInfinity;
    }

    void CalculateFormationOffsets(RuntimeVariant[] pendingVariants)
    {
        float cursor = 0f;
        for (int i = 0; i < VariantCount; i++)
        {
            RuntimeVariant variant = pendingVariants[i];
            variant.halfWidth = Mathf.Max(
                0.1f, (variant.maximumLocalX - variant.minimumLocalX) * 0.5f);
            if (i == 0)
                variant.lateralOffset = 0f;
            else
            {
                cursor += pendingVariants[i - 1].halfWidth + sideGap + variant.halfWidth;
                variant.lateralOffset = cursor;
            }
        }
        float center = (pendingVariants[0].lateralOffset
            + pendingVariants[VariantCount - 1].lateralOffset) * 0.5f;
        float maxHeight = 0f;
        for (int i = 0; i < VariantCount; i++)
        {
            pendingVariants[i].lateralOffset -= center;
            maxHeight = Mathf.Max(
                maxHeight,
                pendingVariants[i].maximumLocalY - pendingVariants[i].minimumLocalY);
        }
        groupWidth = pendingVariants[VariantCount - 1].lateralOffset
            + pendingVariants[VariantCount - 1].halfWidth
            - (pendingVariants[0].lateralOffset - pendingVariants[0].halfWidth);
        groupHeight = Mathf.Max(1f, maxHeight);
    }

    void InitializeFormationIfNeeded()
    {
        if (groupUp.sqrMagnitude < 0.5f)
        {
            groupUp = spawnDirection.sqrMagnitude > 0.0001f
                ? spawnDirection.normalized : Vector3.up;
            groupForward = Vector3.Cross(Vector3.forward, groupUp);
            if (groupForward.sqrMagnitude < 0.0001f)
                groupForward = Vector3.Cross(Vector3.right, groupUp);
            groupForward.Normalize();
        }
    }

    void AdvanceFormation(float distance)
    {
        Vector3 orbitAxis = Vector3.Cross(groupUp, groupForward);
        if (orbitAxis.sqrMagnitude < 0.0001f)
            return;
        orbitAxis.Normalize();
        float radius = Mathf.Max(
            gravitySource.Radius,
            Vector3.Distance(FindSurface(groupUp), gravitySource.Center));
        Vector3 nextUp = Quaternion.AngleAxis(
            distance / radius * Mathf.Rad2Deg, orbitAxis) * groupUp;
        nextUp.Normalize();
        groupForward = Vector3.ProjectOnPlane(
            Quaternion.FromToRotation(groupUp, nextUp) * groupForward,
            nextUp).normalized;
        groupUp = nextUp;
        ApplyFormation();
    }

    void ApplyFormation()
    {
        Vector3 centerSurface = FindSurface(groupUp);
        Vector3 right = Vector3.Cross(groupUp, groupForward).normalized;
        for (int i = 0; i < VariantCount; i++)
        {
            RuntimeVariant variant = variants[i];
            if (variant == null || variant.slot == null)
                continue;
            Vector3 radialDirection = (
                centerSurface - gravitySource.Center
                + right * variant.lateralOffset).normalized;
            Vector3 surface = FindSurface(radialDirection);
            Vector3 localUp = gravitySource.GetUp(surface);
            Vector3 localForward = Vector3.ProjectOnPlane(groupForward, localUp).normalized;
            variant.slot.SetPositionAndRotation(
                surface,
                RotationForLocalForward(family.LocalForwardAxis, localForward, localUp));
        }
        if (cameraTarget != null)
        {
            cameraTarget.SetPositionAndRotation(
                centerSurface + groupUp * (groupHeight * 0.45f),
                Quaternion.LookRotation(groupForward, groupUp));
        }
    }

    void EnsureCameraTarget()
    {
        if (cameraTarget != null)
            return;
        var targetObject = new GameObject("Bio1_AntelopeTrio_CameraTarget");
        cameraTarget = targetObject.transform;
        cameraTarget.SetParent(transform, false);
    }

    void SampleSynchronizedWalk(float phase)
    {
        for (int i = 0; i < VariantCount; i++)
            if (variants[i] != null)
                SampleAnimator(variants[i], phase);
    }

    void SampleAnimator(RuntimeVariant variant, float phase)
    {
        int stateHash = Animator.StringToHash(
            "Base Layer." + authorizedLibrary.LocomotionState);
        variant.animator.SetFloat(authorizedLibrary.SpeedParameter, 0.5f);
        variant.animator.Play(stateHash, 0, Mathf.Repeat(phase, 1f));
        variant.animator.Update(0f);
        variant.animator.speed = 0f;
    }

    static Dictionary<string, float> CaptureSegmentLengths(
        Transform visualScale, NmsCreatureFamilyDefinition sourceFamily)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        Transform armature = visualScale.GetComponentInChildren<Animator>(true)
            ?.transform.Find("Armature");
        if (armature == null || sourceFamily == null)
            return result;
        Transform[] bones = armature.GetComponentsInChildren<Transform>(true);
        var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        for (int i = 0; i < bones.Length; i++)
            if (!byName.ContainsKey(bones[i].name))
                byName.Add(bones[i].name, bones[i]);
        for (int chainIndex = 0;
            chainIndex < sourceFamily.LoadBearingChains.Count;
            chainIndex++)
        {
            NmsCreatureLegChain chain = sourceFamily.LoadBearingChains[chainIndex];
            for (int i = 0; i + 1 < chain.bones.Length; i++)
            {
                if (!byName.TryGetValue(chain.bones[i], out Transform parent)
                    || !byName.TryGetValue(chain.bones[i + 1], out Transform child))
                    continue;
                Vector3 parentPoint = visualScale.InverseTransformPoint(parent.position);
                Vector3 childPoint = visualScale.InverseTransformPoint(child.position);
                result[$"{chain.id}:{parent.name}->{child.name}"] =
                    Vector3.Distance(parentPoint, childPoint);
            }
        }
        return result;
    }

    static bool ValidateSegmentLengths(RuntimeVariant variant, out string error)
    {
        error = null;
        Dictionary<string, float> current = CaptureSegmentLengths(
            variant.visualScale, variant.family);
        foreach (KeyValuePair<string, float> pair in variant.referenceSegmentLengths)
        {
            if (!current.TryGetValue(pair.Key, out float length) || pair.Value <= 0.000001f)
            {
                error = $"Bone segment '{pair.Key}' is missing or degenerate.";
                return false;
            }
            float relativeError = Mathf.Abs(length / pair.Value - 1f);
            if (!IsFinite(relativeError) || relativeError > 0.02f)
            {
                error = $"Canonical segment '{pair.Key}' changed by "
                    + $"{relativeError * 100f:F2}%.";
                return false;
            }
        }
        return true;
    }

    static void ApplyPalette(
        List<Renderer> renderers, NmsCreatureSpeciesDefinition species)
    {
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            renderer.GetPropertyBlock(block);
            block.SetColor(ColorId, Color.white);
            block.SetColor(BaseColorId, Color.white);
            block.SetColor(PrimaryColorId, species.primaryColor);
            block.SetColor(SecondaryColorId, species.secondaryColor);
            block.SetColor(AccentColorId, species.accentColor);
            renderer.SetPropertyBlock(block);
            block.Clear();
        }
    }

    static void SetRenderingHidden(List<Renderer> renderers, bool hidden)
    {
        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i] != null)
                renderers[i].forceRenderingOff = hidden;
    }

    void FailBuild(string error, GameObject failedRoot)
    {
        LastValidationResult = error;
        Debug.LogError("Bio 1 antelope trio build failed. " + error, this);
        if (failedRoot != null)
        {
            failedRoot.SetActive(false);
            Destroy(failedRoot);
        }
        if (failedRoot == pendingTrioRoot)
            pendingTrioRoot = null;
        buildRoutine = null;
    }

    public bool OpenAppearanceEditor()
    {
        if (!enableAppearanceEditor || !IsReady || IsApplyingAppearance)
            return false;
        IsAppearanceEditorOpen = true;
        SelectEditableVariant(Mathf.Clamp(SelectedVariantIndex, 0, VariantCount - 1));
        return true;
    }

    public void CloseAppearanceEditor()
    {
        if (!IsAppearanceEditorOpen || IsApplyingAppearance)
            return;
        RevertAppearancePreview();
        IsAppearanceEditorOpen = false;
        editDraft = null;
    }

    public bool SelectEditableVariant(int index)
    {
        if (!IsReady || IsApplyingAppearance
            || index < 0 || index >= VariantCount || variants[index] == null)
            return false;
        if (editDraft != null && SelectedVariantIndex != index)
            RestoreVariantPreview(variants[SelectedVariantIndex]);
        SelectedVariantIndex = index;
        editDraft = variants[index].parameters.Clone();
        return true;
    }

    public IReadOnlyList<string> GetEditingOptions(NmsAntelopeEditablePart part)
    {
        return editingOptions != null && editDraft != null
            ? editingOptions.GetOptions(part, editDraft.bodyModule)
            : Array.Empty<string>();
    }

    public void SetDraftScale(Vector3 scale)
    {
        if (!CanEditDraft())
            return;
        editDraft.visualScale = scale;
        editDraft.ClampScale();
        PreviewDraftAppearance();
    }

    public void SetDraftColors(Color primary, Color secondary, Color accent)
    {
        if (!CanEditDraft())
            return;
        editDraft.primaryColor = primary;
        editDraft.secondaryColor = secondary;
        editDraft.accentColor = accent;
        PreviewDraftAppearance();
    }

    public void CycleDraftModule(NmsAntelopeEditablePart part, int direction)
    {
        if (!CanEditDraft() || direction == 0)
            return;
        IReadOnlyList<string> options = editingOptions.GetOptions(
            part, editDraft.bodyModule);
        if (options.Count == 0)
            return;
        string current = editDraft.GetModule(part);
        int index = 0;
        for (int i = 0; i < options.Count; i++)
            if (string.Equals(options[i], current, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        index = (index + (direction > 0 ? 1 : options.Count - 1)) % options.Count;
        editDraft.SetModule(part, options[index]);
        if (part == NmsAntelopeEditablePart.Body)
            editDraft.accessoryModule = string.Empty;
        editingOptions.Constrain(editDraft);
    }

    public void RevertAppearancePreview()
    {
        if (!CanEditDraft())
            return;
        RuntimeVariant variant = variants[SelectedVariantIndex];
        RestoreVariantPreview(variant);
        editDraft = variant.parameters.Clone();
        LastValidationResult = "已恢复到当前应用值";
    }

    public void ApplyAppearanceDraft()
    {
        if (!CanEditDraft())
            return;
        editingOptions.Constrain(editDraft);
        NmsAntelopeVariantParameters target = editDraft.Clone();
        RuntimeVariant current = variants[SelectedVariantIndex];
        if (current.parameters.HasSameModules(target))
        {
            CommitAppearanceWithoutRebuild(current, target);
            return;
        }
        RestoreVariantPreview(current);
        appearanceBuildRevision++;
        if (appearanceBuildRoutine != null)
            StopCoroutine(appearanceBuildRoutine);
        appearanceBuildRoutine = StartCoroutine(RebuildEditedVariant(
            appearanceBuildRevision, SelectedVariantIndex, target));
    }

    bool CanEditDraft()
    {
        return IsAppearanceEditorOpen && IsReady && !IsApplyingAppearance
            && editDraft != null && SelectedVariantIndex >= 0
            && SelectedVariantIndex < VariantCount
            && variants[SelectedVariantIndex] != null;
    }

    void PreviewDraftAppearance()
    {
        RuntimeVariant variant = variants[SelectedVariantIndex];
        ApplyPalette(variant.renderers, NmsCreatureVariantSampler.CreateManualSpecies(
            FamilyId, editDraft.seed, editDraft.BuildModuleIds(),
            editDraft.primaryColor, editDraft.secondaryColor, editDraft.accentColor));
        RefreshVariantGeometry(variant, editDraft.visualScale);
    }

    void RestoreVariantPreview(RuntimeVariant variant)
    {
        if (variant?.parameters == null)
            return;
        NmsAntelopeVariantParameters applied = variant.parameters;
        ApplyPalette(variant.renderers, NmsCreatureVariantSampler.CreateManualSpecies(
            FamilyId, applied.seed, applied.BuildModuleIds(),
            applied.primaryColor, applied.secondaryColor, applied.accentColor));
        RefreshVariantGeometry(variant, applied.visualScale);
    }

    void RefreshVariantGeometry(RuntimeVariant variant, Vector3 scale)
    {
        variant.visualScale.localScale = scale;
        variant.visualScale.localPosition = Vector3.zero;
        SampleAnimator(variant, walkPhase);
        ResetAccumulatedBounds(variant);
        AccumulateBounds(variant);
        variant.visualScale.localPosition = Vector3.up
            * (MinimumGroundClearance - variant.minimumLocalY);
        ResetAccumulatedBounds(variant);
        AccumulateBounds(variant);
        CalculateFormationOffsets(variants);
        ApplyFormation();
        if (followCamera != null)
            followCamera.SetFraming(groupWidth, groupHeight, true);
    }

    void CommitAppearanceWithoutRebuild(
        RuntimeVariant variant, NmsAntelopeVariantParameters target)
    {
        NmsCreatureSpeciesDefinition species =
            NmsCreatureVariantSampler.CreateManualSpecies(
                FamilyId, target.seed, target.BuildModuleIds(),
                target.primaryColor, target.secondaryColor, target.accentColor);
        variant.parameters = target.Clone();
        variant.descriptor = new NmsAntelopeVariantDescriptor(
            target.seed, target.visualScale, species);
        descriptors[SelectedVariantIndex] = variant.descriptor;
        ApplyPalette(variant.renderers, species);
        RefreshVariantGeometry(variant, target.visualScale);
        editDraft = target.Clone();
        LastValidationResult = "体型与颜色已应用";
    }

    IEnumerator RebuildEditedVariant(
        int revision, int index, NmsAntelopeVariantParameters target)
    {
        IsApplyingAppearance = true;
        LastValidationResult = $"正在验证第 {index + 1} 只生物的外观";
        NmsCreatureSpeciesDefinition species =
            NmsCreatureVariantSampler.CreateManualSpecies(
                FamilyId, target.seed, target.BuildModuleIds(),
                target.primaryColor, target.secondaryColor, target.accentColor);
        if (!TryBuildVariant(
            activeTrioRoot.transform, index, target.seed, target.visualScale,
            false, out RuntimeVariant replacement, out string buildError, species))
        {
            FinishAppearanceFailure(buildError, null);
            yield break;
        }

        replacement.parameters = target.Clone();
        yield return null;
        if (revision != appearanceBuildRevision)
        {
            FinishAppearanceFailure("Appearance build was superseded.", replacement);
            yield break;
        }

        RuntimeVariant reference = variants[index == 0 ? 1 : 0];
        if (!reference.restSnapshot.Matches(
            replacement.restSnapshot, out string restError)
            || reference.walkClip != replacement.walkClip)
        {
            FinishAppearanceFailure(
                "Edited modules changed the canonical rig or Walk. " + restError,
                replacement);
            yield break;
        }

        ResetAccumulatedBounds(replacement);
        for (int sample = 0; sample < 8; sample++)
        {
            SampleAnimator(replacement, sample / 8f);
            if (!ValidateVariantPose(replacement, sample, out string poseError))
            {
                FinishAppearanceFailure(
                    $"Edited variant failed Walk phase {sample}/8. {poseError}",
                    replacement);
                yield break;
            }
            AccumulateBounds(replacement);
        }
        replacement.visualScale.localPosition += Vector3.up
            * (MinimumGroundClearance - replacement.minimumLocalY);
        SampleAnimator(replacement, walkPhase);

        RuntimeVariant old = variants[index];
        old.slot.gameObject.SetActive(false);
        variants[index] = replacement;
        descriptors[index] = replacement.descriptor;
        replacement.slot.name = $"FormationSlot_{index}";
        SetRenderingHidden(replacement.renderers, false);
        Destroy(old.slot.gameObject);
        ResetAccumulatedBounds(replacement);
        AccumulateBounds(replacement);
        CalculateFormationOffsets(variants);
        ApplyFormation();
        followCamera.SetFraming(groupWidth, groupHeight, true);
        SampleSynchronizedWalk(walkPhase);

        editDraft = target.Clone();
        IsApplyingAppearance = false;
        appearanceBuildRoutine = null;
        LastValidationResult = $"第 {index + 1} 只生物已通过 8 相位步行动画验证";
    }

    void FinishAppearanceFailure(string error, RuntimeVariant replacement)
    {
        if (replacement?.slot != null)
        {
            replacement.slot.gameObject.SetActive(false);
            Destroy(replacement.slot.gameObject);
        }
        IsApplyingAppearance = false;
        appearanceBuildRoutine = null;
        LastValidationResult = "外观应用被拒绝：" + error;
        Debug.LogError(LastValidationResult, this);
    }

    Vector3 FindSurface(Vector3 radialDirection)
    {
        Vector3 direction = radialDirection.sqrMagnitude > 0.0001f
            ? radialDirection.normalized : Vector3.up;
        Vector3 origin = gravitySource.Center
            + direction * (gravitySource.Radius + surfaceProbeHeight);
        if (Physics.Raycast(
            origin, -direction, out RaycastHit hit,
            surfaceProbeHeight * 2f, groundLayers,
            QueryTriggerInteraction.Ignore))
            return hit.point;
        return gravitySource.GetSurfacePoint(direction);
    }

    public static Vector3[] CreateVariantScales(int seed)
    {
        var random = new NmsStableRandom(
            unchecked((ulong)(uint)seed) ^ 0xD1B54A32D192ED03UL);
        var result = new Vector3[VariantCount];
        Vector2[] heightBands =
        {
            new Vector2(0.72f, 0.88f),
            new Vector2(0.91f, 1.08f),
            new Vector2(1.12f, 1.30f)
        };
        for (int i = 0; i < VariantCount; i++)
        {
            float height = random.Range(heightBands[i].x, heightBands[i].y);
            float height01 = Mathf.InverseLerp(0.72f, 1.30f, height);
            float width = Mathf.Clamp(
                Mathf.Lerp(1.22f, 0.82f, height01) + random.Range(-0.035f, 0.035f),
                0.82f, 1.22f);
            float length = random.Range(0.96f, 1.04f);
            result[i] = new Vector3(width, height, length);
        }
        for (int i = result.Length - 1; i > 0; i--)
        {
            int selected = random.Next(i + 1);
            Vector3 temporary = result[i];
            result[i] = result[selected];
            result[selected] = temporary;
        }
        return result;
    }

    static int DeriveVariantSeed(int seed, int index)
    {
        unchecked
        {
            uint value = (uint)seed + 0x9E3779B9u * (uint)(index + 1);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)value;
        }
    }

    static Quaternion RotationForLocalForward(
        Vector3 localForward, Vector3 worldForward, Vector3 worldUp)
    {
        Vector3 axis = localForward.sqrMagnitude > 0.000001f
            ? localForward.normalized : Vector3.forward;
        return Quaternion.LookRotation(worldForward, worldUp)
            * Quaternion.Inverse(Quaternion.LookRotation(axis, Vector3.up));
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    static string RelativePath(Transform root, Transform target)
    {
        if (target == root)
            return string.Empty;
        var names = new Stack<string>();
        Transform cursor = target;
        while (cursor != null && cursor != root)
        {
            names.Push(cursor.name);
            cursor = cursor.parent;
        }
        return string.Join("/", names.ToArray());
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    void OnGUI()
    {
        if (telemetryStyle == null)
        {
            telemetryStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };
        }
        if (showTelemetry)
        {
            GUI.Box(new Rect(12f, 12f, 510f, 190f), GUIContent.none);
            GUILayout.BeginArea(new Rect(24f, 20f, 486f, 176f));
            GUILayout.Label("BIO 1 同步羚羊变体", telemetryStyle);
            GUILayout.Label(
                $"种子：{masterSeed}   就绪：{(IsReady ? "是" : "否")}   "
                + $"步态相位：{walkPhase:F3}", telemetryStyle);
            GUILayout.Label(LastValidationResult, telemetryStyle);
            for (int i = 0; i < VariantCount; i++)
                if (descriptors[i].species != null && variants[i]?.parameters != null)
                    GUILayout.Label(
                        $"生物 {i + 1}：体型 {descriptors[i].visualScale:F2}  "
                        + NmsAntelopeEditingOptions.DisplayName(
                            variants[i].parameters.bodyModule), telemetryStyle);
            GUILayout.EndArea();
        }
        if (IsAppearanceEditorOpen)
            DrawRuntimeAppearanceEditor();
    }

    void DrawRuntimeAppearanceEditor()
    {
        float width = 404f;
        float height = Mathf.Min(720f, Screen.height - 24f);
        Rect panel = new Rect(Screen.width - width - 12f, 12f, width, height);
        GUI.Box(panel, GUIContent.none);
        GUILayout.BeginArea(new Rect(
            panel.x + 12f, panel.y + 10f, panel.width - 24f, panel.height - 20f));
        GUILayout.Label("羚羊外观编辑", telemetryStyle);
        GUILayout.Label(IsApplyingAppearance ? "正在验证……" : "已暂停并定格", telemetryStyle);

        GUI.enabled = !IsApplyingAppearance;
        GUILayout.BeginHorizontal();
        for (int i = 0; i < VariantCount; i++)
        {
            bool selected = i == SelectedVariantIndex;
            if (GUILayout.Toggle(selected, $"生物 {i + 1}", GUI.skin.button)
                && !selected)
                SelectEditableVariant(i);
        }
        GUILayout.EndHorizontal();

        appearanceScroll = GUILayout.BeginScrollView(appearanceScroll);
        if (editDraft != null)
        {
            Vector3 scale = editDraft.visualScale;
            float widthScale = DrawRuntimeSlider("宽度", scale.x, 0.82f, 1.22f);
            float heightScale = DrawRuntimeSlider("高度", scale.y, 0.72f, 1.30f);
            float lengthScale = DrawRuntimeSlider("长度", scale.z, 0.96f, 1.04f);
            Vector3 nextScale = new Vector3(widthScale, heightScale, lengthScale);
            if ((nextScale - scale).sqrMagnitude > 0.0000001f)
                SetDraftScale(nextScale);

            GUILayout.Space(8f);
            DrawRuntimeModule("身体", NmsAntelopeEditablePart.Body);
            DrawRuntimeModule("装饰", NmsAntelopeEditablePart.Accessory);
            DrawRuntimeModule("耳朵", NmsAntelopeEditablePart.Ears);
            DrawRuntimeModule("角", NmsAntelopeEditablePart.Horns);
            DrawRuntimeModule("尾巴", NmsAntelopeEditablePart.Tail);

            GUILayout.Space(8f);
            Color primary = DrawRuntimeColor("主色", editDraft.primaryColor);
            Color secondary = DrawRuntimeColor("辅色", editDraft.secondaryColor);
            Color accent = DrawRuntimeColor("强调色", editDraft.accentColor);
            if (primary != editDraft.primaryColor
                || secondary != editDraft.secondaryColor
                || accent != editDraft.accentColor)
                SetDraftColors(primary, secondary, accent);
        }
        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("应用", GUILayout.Height(30f)))
            ApplyAppearanceDraft();
        if (GUILayout.Button("恢复", GUILayout.Height(30f)))
            RevertAppearancePreview();
        if (GUILayout.Button("关闭", GUILayout.Height(30f)))
            CloseAppearanceEditor();
        GUILayout.EndHorizontal();
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    static float DrawRuntimeSlider(
        string label, float value, float minimum, float maximum)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(64f));
        float result = GUILayout.HorizontalSlider(value, minimum, maximum);
        GUILayout.Label(result.ToString("F2"), GUILayout.Width(38f));
        GUILayout.EndHorizontal();
        return result;
    }

    void DrawRuntimeModule(string label, NmsAntelopeEditablePart part)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(72f));
        if (GUILayout.Button("<", GUILayout.Width(30f)))
            CycleDraftModule(part, -1);
        GUILayout.Label(
            NmsAntelopeEditingOptions.DisplayName(editDraft.GetModule(part)),
            GUI.skin.box, GUILayout.MinWidth(190f));
        if (GUILayout.Button(">", GUILayout.Width(30f)))
            CycleDraftModule(part, 1);
        GUILayout.EndHorizontal();
    }

    static Color DrawRuntimeColor(string label, Color value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(64f));
        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = value;
        GUILayout.Box(GUIContent.none, GUILayout.Width(34f), GUILayout.Height(18f));
        GUI.backgroundColor = previous;
        GUILayout.Label("R", GUILayout.Width(12f));
        value.r = GUILayout.HorizontalSlider(value.r, 0f, 1f);
        GUILayout.Label("G", GUILayout.Width(12f));
        value.g = GUILayout.HorizontalSlider(value.g, 0f, 1f);
        GUILayout.Label("B", GUILayout.Width(12f));
        value.b = GUILayout.HorizontalSlider(value.b, 0f, 1f);
        GUILayout.EndHorizontal();
        value.a = 1f;
        return value;
    }

    void OnDestroy()
    {
        if (activeTrioRoot != null)
            Destroy(activeTrioRoot);
        if (pendingTrioRoot != null)
            Destroy(pendingTrioRoot);
    }

    sealed class RuntimeVariant
    {
        public Transform slot;
        public Transform visualScale;
        public Animator animator;
        public List<Renderer> renderers;
        public AnimationClip walkClip;
        public RigRestSnapshot restSnapshot;
        public NmsAntelopeVariantDescriptor descriptor;
        public NmsAntelopeVariantParameters parameters;
        public Dictionary<string, float> referenceSegmentLengths;
        public NmsCreatureFamilyDefinition family;
        public float minimumLocalX;
        public float maximumLocalX;
        public float minimumLocalY;
        public float maximumLocalY;
        public float halfWidth;
        public float lateralOffset;
        public bool usesStandardModules;
    }

    sealed class RigRestSnapshot
    {
        readonly BoneRest[] bones;

        RigRestSnapshot(BoneRest[] restBones)
        {
            bones = restBones;
        }

        public static RigRestSnapshot Capture(Transform armature)
        {
            Transform[] transforms = armature.GetComponentsInChildren<Transform>(true);
            var result = new BoneRest[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform bone = transforms[i];
                result[i] = new BoneRest
                {
                    path = RelativePath(armature, bone),
                    localPosition = bone.localPosition,
                    localRotation = bone.localRotation,
                    localScale = bone.localScale
                };
            }
            return new RigRestSnapshot(result);
        }

        public bool Matches(RigRestSnapshot other, out string error)
        {
            error = null;
            if (other == null || bones.Length != other.bones.Length)
            {
                error = "Bone counts differ.";
                return false;
            }
            for (int i = 0; i < bones.Length; i++)
            {
                BoneRest first = bones[i];
                BoneRest second = other.bones[i];
                if (first.path != second.path
                    || Vector3.Distance(first.localPosition, second.localPosition) > 0.00001f
                    || Quaternion.Angle(first.localRotation, second.localRotation) > 0.001f
                    || Vector3.Distance(first.localScale, second.localScale) > 0.00001f)
                {
                    error = $"Rest transform differs at '{first.path}'.";
                    return false;
                }
            }
            return true;
        }
    }

    struct BoneRest
    {
        public string path;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }
}
