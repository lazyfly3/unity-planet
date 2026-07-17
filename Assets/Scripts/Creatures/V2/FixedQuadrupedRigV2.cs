using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class FixedLegChainV2
{
    public string id;
    public string upperBone;
    public string lowerBone;
    public string ankleBone;
    public string footBone;
}

// This component validates the imported rig but never writes to its bones.
[DefaultExecutionOrder(2000)]
[DisallowMultipleComponent]
public sealed class FixedQuadrupedRigV2 : MonoBehaviour
{
    sealed class BoneSnapshot
    {
        public Transform bone;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public float parentDistance;
    }

    sealed class LegSegmentSnapshot
    {
        public string id;
        public Transform parent;
        public Transform child;
        public float length;
    }

    sealed class RendererBoundsSnapshot
    {
        public SkinnedMeshRenderer renderer;
        public float diagonal;
    }

    [SerializeField] Transform rigRoot;
    [SerializeField] SkinnedMeshRenderer[] moduleRenderers = Array.Empty<SkinnedMeshRenderer>();
    [SerializeField] FixedLegChainV2[] loadBearingLegs = Array.Empty<FixedLegChainV2>();
    [SerializeField, Min(.000001f)] float poseTolerance = .0001f;
    [SerializeField] bool allowAuthoredAnimation;
    [SerializeField] bool validateContinuously = true;
    [SerializeField, Range(.001f, .1f)] float maximumLegLengthError = .02f;
    [SerializeField, Min(1f)] float maximumBoundsScale = 2f;

    readonly List<BoneSnapshot> snapshots = new List<BoneSnapshot>();
    readonly List<LegSegmentSnapshot> legSegments = new List<LegSegmentSnapshot>();
    readonly List<RendererBoundsSnapshot> rendererBounds = new List<RendererBoundsSnapshot>();
    bool reportedRuntimeMutation;

    public bool IsConfigurationValid { get; private set; }
    public string ValidationMessage { get; private set; }

    public void Configure(
        Transform targetRigRoot,
        SkinnedMeshRenderer[] renderers,
        FixedLegChainV2[] legs,
        bool authoredAnimationMayPlay = false)
    {
        rigRoot = targetRigRoot;
        moduleRenderers = renderers ?? Array.Empty<SkinnedMeshRenderer>();
        loadBearingLegs = legs ?? Array.Empty<FixedLegChainV2>();
        allowAuthoredAnimation = authoredAnimationMayPlay;
    }

    public void SetModuleRenderers(SkinnedMeshRenderer[] renderers)
    {
        moduleRenderers = renderers ?? Array.Empty<SkinnedMeshRenderer>();
        if (snapshots.Count == 0)
            return;

        CaptureRendererBounds();
        reportedRuntimeMutation = false;
    }

    void Awake()
    {
        IsConfigurationValid = ValidateConfiguration(out string message);
        ValidationMessage = message;
        if (!IsConfigurationValid)
        {
            Debug.LogError($"Fixed quadruped V2 validation failed: {message}", this);
            enabled = false;
            return;
        }

        CaptureRestPose();
        Debug.Log(
            $"Fixed quadruped V2 ready. Renderers={moduleRenderers.Length}, " +
            $"Bones={snapshots.Count}, Legs={loadBearingLegs.Length}, " +
            $"Mode={(allowAuthoredAnimation ? "native full-TRS animation" : "static rest pose")}. " +
            "The imported Animator owns the pose; only bounded foot-placement rotations may follow it.",
            this);
    }

    void LateUpdate()
    {
        if (!validateContinuously || reportedRuntimeMutation)
            return;

        if (allowAuthoredAnimation)
        {
            ValidateAuthoredPose();
            return;
        }

        for (int i = 0; i < snapshots.Count; i++)
        {
            BoneSnapshot snapshot = snapshots[i];
            Transform bone = snapshot.bone;
            if (bone == null)
            {
                ReportMutation("A captured bone was destroyed.");
                return;
            }

            bool positionChanged =
                Vector3.Distance(bone.localPosition, snapshot.localPosition) > poseTolerance;
            bool rotationChanged =
                Quaternion.Angle(bone.localRotation, snapshot.localRotation) > poseTolerance;
            bool scaleChanged =
                Vector3.Distance(bone.localScale, snapshot.localScale) > poseTolerance;
            if (positionChanged || rotationChanged || scaleChanged)
            {
                ReportMutation(
                    $"Bone '{bone.name}' changed a forbidden channel " +
                    $"(position={positionChanged}, rotation={rotationChanged}, scale={scaleChanged}).");
                return;
            }

            if (bone.parent != null
                && Mathf.Abs(Vector3.Distance(bone.position, bone.parent.position) - snapshot.parentDistance)
                    > poseTolerance)
            {
                ReportMutation($"Bone segment ending at '{bone.name}' changed length.");
                return;
            }
        }
    }

    void ValidateAuthoredPose()
    {
        for (int i = 0; i < snapshots.Count; i++)
        {
            Transform bone = snapshots[i].bone;
            if (bone == null)
            {
                ReportMutation("An animated bone was destroyed.");
                return;
            }
            if (!IsFinite(bone.localPosition)
                || !IsFinite(bone.localRotation)
                || !IsFinite(bone.localScale))
            {
                ReportMutation($"Bone '{bone.name}' contains a non-finite authored transform.");
                return;
            }
        }

        for (int i = 0; i < legSegments.Count; i++)
        {
            LegSegmentSnapshot segment = legSegments[i];
            float currentLength = Vector3.Distance(segment.parent.position, segment.child.position);
            float relativeError = Mathf.Abs(currentLength / segment.length - 1f);
            if (!IsFinite(currentLength) || relativeError > maximumLegLengthError)
            {
                ReportMutation(
                    $"Load-bearing segment '{segment.id}' changed length by " +
                    $"{relativeError * 100f:F2}%.");
                return;
            }
        }

        for (int i = 0; i < rendererBounds.Count; i++)
        {
            RendererBoundsSnapshot snapshot = rendererBounds[i];
            if (snapshot.renderer == null)
            {
                ReportMutation("An animated renderer was destroyed.");
                return;
            }
            Bounds bounds = snapshot.renderer.bounds;
            float diagonal = bounds.size.magnitude;
            if (!IsFinite(bounds.center)
                || !IsFinite(bounds.extents)
                || !IsFinite(diagonal)
                || diagonal > snapshot.diagonal * maximumBoundsScale)
            {
                ReportMutation(
                    $"Renderer '{snapshot.renderer.name}' exceeded the validated bounds envelope.");
                return;
            }
        }
    }

    public bool ValidateConfiguration(out string message)
    {
        if (rigRoot == null)
        {
            message = "Rig root is missing.";
            return false;
        }
        if (moduleRenderers == null || moduleRenderers.Length == 0)
        {
            message = "No fixed module renderers are assigned.";
            return false;
        }

        var bonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        Transform[] allBones = rigRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allBones.Length; i++)
            if (!bonesByName.ContainsKey(allBones[i].name))
                bonesByName.Add(allBones[i].name, allBones[i]);

        for (int i = 0; i < moduleRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = moduleRenderers[i];
            if (renderer == null || renderer.sharedMesh == null)
            {
                message = $"Renderer slot {i} is missing its renderer or mesh.";
                return false;
            }
            if (renderer.bones == null
                || renderer.bones.Length == 0
                || renderer.bones.Length != renderer.sharedMesh.bindposes.Length)
            {
                message = $"Renderer '{renderer.name}' has an invalid bone/bind-pose count.";
                return false;
            }
            for (int boneIndex = 0; boneIndex < renderer.bones.Length; boneIndex++)
            {
                if (renderer.bones[boneIndex] == null)
                {
                    message = $"Renderer '{renderer.name}' has a null bone at index {boneIndex}.";
                    return false;
                }
                Transform bone = renderer.bones[boneIndex];
                if (bone != rigRoot && !bone.IsChildOf(rigRoot))
                {
                    message = $"Renderer '{renderer.name}' uses bone '{bone.name}' outside the imported rig.";
                    return false;
                }
                Matrix4x4 bindPose = renderer.sharedMesh.bindposes[boneIndex];
                if (!IsFinite(bindPose) || Mathf.Abs(bindPose.determinant) < .00000001f)
                {
                    message =
                        $"Renderer '{renderer.name}' has a non-finite or singular bind pose " +
                        $"at bone {boneIndex} ('{bone.name}').";
                    return false;
                }
            }
        }

        if (loadBearingLegs == null || loadBearingLegs.Length != 4)
        {
            message = "A fixed quadruped must define exactly four load-bearing chains.";
            return false;
        }
        for (int i = 0; i < loadBearingLegs.Length; i++)
        {
            FixedLegChainV2 definition = loadBearingLegs[i];
            if (definition == null
                || !bonesByName.TryGetValue(definition.upperBone, out Transform upper)
                || !bonesByName.TryGetValue(definition.lowerBone, out Transform lower)
                || !bonesByName.TryGetValue(definition.ankleBone, out Transform ankle)
                || !bonesByName.TryGetValue(definition.footBone, out Transform foot))
            {
                message = $"Leg chain {i} is missing one or more named bones.";
                return false;
            }
            if (lower.parent != upper || ankle.parent != lower || foot.parent != ankle)
            {
                message = $"Leg '{definition.id}' is not a continuous parent-child chain.";
                return false;
            }
            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, ankle.position);
            float footLength = Vector3.Distance(ankle.position, foot.position);
            if (upperLength < .000001f || lowerLength < .000001f || footLength < .000001f)
            {
                message =
                    $"Leg '{definition.id}' contains a zero-length segment: " +
                    $"{definition.upperBone}->{definition.lowerBone}={upperLength:F8}, " +
                    $"{definition.lowerBone}->{definition.ankleBone}={lowerLength:F8}, " +
                    $"{definition.ankleBone}->{definition.footBone}={footLength:F8}.";
                return false;
            }
        }

        message = "OK";
        return true;
    }

    static bool IsFinite(Matrix4x4 value)
    {
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                if (!IsFinite(value[row, column]))
                    return false;
        return true;
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

    void CaptureRestPose()
    {
        snapshots.Clear();
        legSegments.Clear();
        rendererBounds.Clear();
        Transform[] bones = rigRoot.GetComponentsInChildren<Transform>(true);
        var bonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            if (!bonesByName.ContainsKey(bone.name))
                bonesByName.Add(bone.name, bone);
            snapshots.Add(new BoneSnapshot
            {
                bone = bone,
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale,
                parentDistance = bone.parent != null
                    ? Vector3.Distance(bone.position, bone.parent.position)
                    : 0f
            });
        }

        for (int i = 0; i < loadBearingLegs.Length; i++)
        {
            FixedLegChainV2 leg = loadBearingLegs[i];
            string[] names = { leg.upperBone, leg.lowerBone, leg.ankleBone, leg.footBone };
            for (int segmentIndex = 0; segmentIndex < names.Length - 1; segmentIndex++)
            {
                Transform parent = bonesByName[names[segmentIndex]];
                Transform child = bonesByName[names[segmentIndex + 1]];
                legSegments.Add(new LegSegmentSnapshot
                {
                    id = $"{leg.id}:{parent.name}->{child.name}",
                    parent = parent,
                    child = child,
                    length = Vector3.Distance(parent.position, child.position)
                });
            }
        }

        CaptureRendererBounds();
    }

    void CaptureRendererBounds()
    {
        rendererBounds.Clear();
        for (int i = 0; i < moduleRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = moduleRenderers[i];
            if (renderer == null)
                continue;
            rendererBounds.Add(new RendererBoundsSnapshot
            {
                renderer = renderer,
                diagonal = Mathf.Max(.0001f, renderer.bounds.size.magnitude)
            });
        }
    }

    void ReportMutation(string message)
    {
        reportedRuntimeMutation = true;
        Debug.LogError($"Fixed quadruped V2 deformation invariant failed: {message}", this);
    }

    void OnDrawGizmosSelected()
    {
        if (rigRoot == null || loadBearingLegs == null)
            return;

        var bonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        Transform[] bones = rigRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
            if (!bonesByName.ContainsKey(bones[i].name))
                bonesByName.Add(bones[i].name, bones[i]);

        for (int i = 0; i < loadBearingLegs.Length; i++)
        {
            FixedLegChainV2 leg = loadBearingLegs[i];
            if (leg == null
                || !bonesByName.TryGetValue(leg.upperBone, out Transform upper)
                || !bonesByName.TryGetValue(leg.lowerBone, out Transform lower)
                || !bonesByName.TryGetValue(leg.ankleBone, out Transform ankle)
                || !bonesByName.TryGetValue(leg.footBone, out Transform foot))
                continue;

            Gizmos.color = i < 2 ? Color.green : new Color(1f, .65f, .1f);
            Gizmos.DrawLine(upper.position, lower.position);
            Gizmos.DrawLine(lower.position, ankle.position);
            Gizmos.DrawLine(ankle.position, foot.position);
        }
    }
}
