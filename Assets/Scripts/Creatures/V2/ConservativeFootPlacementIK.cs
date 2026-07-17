using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class ConservativeFootPlacementIK : MonoBehaviour
{
    sealed class LegRuntime
    {
        public FixedLegChainV2 definition;
        public Transform upper;
        public Transform lower;
        public Transform ankle;
        public Transform foot;
        public float upperLength;
        public float lowerLength;
        public float totalLength;
        public Vector3 localSoleOffset;
        public Vector3 localSoleNormal;
        public Vector3 lastSolePosition;
        public Vector3 lastGroundPoint;
        public Vector3 lastGroundNormal;
        public bool hasGround;
    }

    [SerializeField] Transform rigRoot;
    [SerializeField] SkinnedMeshRenderer[] moduleRenderers = Array.Empty<SkinnedMeshRenderer>();
    [SerializeField] FixedLegChainV2[] legs = Array.Empty<FixedLegChainV2>();
    [SerializeField] LayerMask groundLayers = ~0;
    [SerializeField] bool enabledForExperiment = true;
    [SerializeField] bool useSphericalUp;
    [SerializeField] Transform gravityCenter;
    [SerializeField, Range(.01f, .2f)] float maximumCorrectionRatio = .055f;
    [SerializeField, Range(.02f, .3f)] float plantHeightRatio = .13f;
    [SerializeField, Range(1f, 15f)] float maximumJointCorrectionDegrees = 7f;
    [SerializeField, Range(1f, 30f)] float maximumFootTiltDegrees = 12f;
    [SerializeField, Min(0f)] float soleClearance = .015f;
    [SerializeField, Range(.01f, .5f)] float blendSpeed = .18f;
    [SerializeField] bool drawDebugGizmos = true;

    readonly List<LegRuntime> runtimeLegs = new List<LegRuntime>(4);
    readonly Dictionary<string, Transform> bonesByName =
        new Dictionary<string, Transform>(StringComparer.Ordinal);
    bool calibrated;
    float currentWeight;

    public bool IsCalibrated => calibrated;
    public int GroundedFootCount { get; private set; }

    public void Configure(
        Transform targetRigRoot,
        SkinnedMeshRenderer[] renderers,
        FixedLegChainV2[] legDefinitions,
        LayerMask targetGroundLayers)
    {
        rigRoot = targetRigRoot;
        moduleRenderers = renderers ?? Array.Empty<SkinnedMeshRenderer>();
        legs = legDefinitions ?? Array.Empty<FixedLegChainV2>();
        groundLayers = targetGroundLayers;
    }

    public void SetModuleRenderers(SkinnedMeshRenderer[] renderers)
    {
        moduleRenderers = renderers ?? Array.Empty<SkinnedMeshRenderer>();
        calibrated = false;
        currentWeight = 0f;
    }

    void Awake()
    {
        BuildRuntimeLegs();
    }

    void LateUpdate()
    {
        if (!enabledForExperiment || runtimeLegs.Count == 0)
            return;

        if (!calibrated)
        {
            CalibrateSoles();
            calibrated = true;
            Debug.Log(
                $"Conservative foot IK calibrated {runtimeLegs.Count} feet. " +
                "Only bounded bone rotations are permitted; animated bone lengths remain unchanged.",
                this);
            return;
        }

        currentWeight = Mathf.MoveTowards(currentWeight, 1f, blendSpeed);
        GroundedFootCount = 0;
        for (int i = 0; i < runtimeLegs.Count; i++)
            ApplyFootPlacement(runtimeLegs[i], currentWeight);
    }

    void BuildRuntimeLegs()
    {
        runtimeLegs.Clear();
        bonesByName.Clear();
        if (rigRoot == null)
            return;

        Transform[] bones = rigRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < bones.Length; i++)
            if (!bonesByName.ContainsKey(bones[i].name))
                bonesByName.Add(bones[i].name, bones[i]);

        for (int i = 0; i < legs.Length; i++)
        {
            FixedLegChainV2 definition = legs[i];
            if (definition == null
                || !bonesByName.TryGetValue(definition.upperBone, out Transform upper)
                || !bonesByName.TryGetValue(definition.lowerBone, out Transform lower)
                || !bonesByName.TryGetValue(definition.ankleBone, out Transform ankle)
                || !bonesByName.TryGetValue(definition.footBone, out Transform foot))
                continue;

            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, ankle.position);
            if (upperLength <= .000001f || lowerLength <= .000001f)
                continue;

            runtimeLegs.Add(new LegRuntime
            {
                definition = definition,
                upper = upper,
                lower = lower,
                ankle = ankle,
                foot = foot,
                upperLength = upperLength,
                lowerLength = lowerLength,
                totalLength = upperLength + lowerLength
            });
        }
    }

    void CalibrateSoles()
    {
        Vector3 up = GetUp(transform.position);
        for (int i = 0; i < runtimeLegs.Count; i++)
        {
            LegRuntime leg = runtimeLegs[i];
            List<Vector3> influencedVertices = CollectInfluencedVertices(leg.foot, .08f);
            if (influencedVertices.Count == 0)
                influencedVertices = CollectInfluencedVertices(leg.ankle, .08f);

            if (influencedVertices.Count == 0)
            {
                leg.localSoleOffset = Vector3.zero;
                leg.localSoleNormal = leg.foot.InverseTransformDirection(up).normalized;
                continue;
            }

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int vertexIndex = 0; vertexIndex < influencedVertices.Count; vertexIndex++)
            {
                float height = Vector3.Dot(influencedVertices[vertexIndex], up);
                minimum = Mathf.Min(minimum, height);
                maximum = Mathf.Max(maximum, height);
            }

            float bottomBand = Mathf.Max(.005f, (maximum - minimum) * .16f);
            Vector3 soleCenter = Vector3.zero;
            int soleVertexCount = 0;
            for (int vertexIndex = 0; vertexIndex < influencedVertices.Count; vertexIndex++)
            {
                Vector3 vertex = influencedVertices[vertexIndex];
                if (Vector3.Dot(vertex, up) > minimum + bottomBand)
                    continue;
                soleCenter += vertex;
                soleVertexCount++;
            }

            if (soleVertexCount == 0)
                soleCenter = leg.foot.position;
            else
                soleCenter /= soleVertexCount;

            leg.localSoleOffset = leg.foot.InverseTransformPoint(soleCenter);
            leg.localSoleNormal = leg.foot.InverseTransformDirection(up).normalized;
        }
    }

    List<Vector3> CollectInfluencedVertices(Transform targetBone, float minimumWeight)
    {
        var result = new List<Vector3>();
        for (int rendererIndex = 0; rendererIndex < moduleRenderers.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = moduleRenderers[rendererIndex];
            if (renderer == null || renderer.sharedMesh == null)
                continue;

            int boneIndex = Array.IndexOf(renderer.bones, targetBone);
            if (boneIndex < 0)
                continue;

            BoneWeight[] weights = renderer.sharedMesh.boneWeights;
            var baked = new Mesh { name = "FootIKCalibration" };
            renderer.BakeMesh(baked);
            Vector3[] vertices = baked.vertices;
            int count = Mathf.Min(vertices.Length, weights.Length);
            for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                if (GetBoneWeight(weights[vertexIndex], boneIndex) < minimumWeight)
                    continue;
                result.Add(renderer.transform.TransformPoint(vertices[vertexIndex]));
            }

            if (Application.isPlaying)
                Destroy(baked);
            else
                DestroyImmediate(baked);
        }
        return result;
    }

    void ApplyFootPlacement(LegRuntime leg, float globalWeight)
    {
        Vector3 up = GetUp(leg.foot.position);
        Vector3 solePosition = leg.foot.TransformPoint(leg.localSoleOffset);
        float probeAbove = Mathf.Max(.08f, leg.totalLength * .2f);
        float probeBelow = Mathf.Max(.12f, leg.totalLength * .35f);
        Vector3 rayOrigin = solePosition + up * probeAbove;
        leg.hasGround = Physics.Raycast(
            rayOrigin,
            -up,
            out RaycastHit hit,
            probeAbove + probeBelow,
            groundLayers,
            QueryTriggerInteraction.Ignore);
        leg.lastSolePosition = solePosition;
        if (!leg.hasGround)
            return;

        leg.lastGroundPoint = hit.point;
        leg.lastGroundNormal = hit.normal;
        float signedHeight = Vector3.Dot(solePosition - hit.point, hit.normal);
        float plantHeight = Mathf.Max(.025f, leg.totalLength * plantHeightRatio);
        if (signedHeight > plantHeight)
            return;

        float contactWeight = 1f - Mathf.Clamp01(signedHeight / plantHeight);
        contactWeight = contactWeight * contactWeight * globalWeight;
        if (contactWeight <= .0001f)
            return;

        Vector3 targetSole = hit.point + hit.normal * soleClearance;
        Vector3 correction = targetSole - solePosition;
        float maximumCorrection = leg.totalLength * maximumCorrectionRatio;
        correction = Vector3.ClampMagnitude(correction, maximumCorrection) * contactWeight;

        Vector3 targetAnkle = leg.ankle.position + correction;
        SolveTwoBone(leg, targetAnkle);

        Vector3 currentSoleNormal = leg.foot.TransformDirection(leg.localSoleNormal).normalized;
        Quaternion tilt = Quaternion.FromToRotation(currentSoleNormal, hit.normal);
        tilt.ToAngleAxis(out float tiltAngle, out Vector3 tiltAxis);
        if (tiltAngle > 180f)
            tiltAngle -= 360f;
        float appliedTilt = Mathf.Clamp(tiltAngle, -maximumFootTiltDegrees, maximumFootTiltDegrees);
        if (tiltAxis.sqrMagnitude > .000001f && Mathf.Abs(appliedTilt) > .001f)
            leg.foot.rotation =
                Quaternion.AngleAxis(appliedTilt * contactWeight, tiltAxis.normalized) * leg.foot.rotation;

        GroundedFootCount++;
    }

    void SolveTwoBone(LegRuntime leg, Vector3 targetAnkle)
    {
        Vector3 upperPosition = leg.upper.position;
        Vector3 targetVector = targetAnkle - upperPosition;
        float targetDistance = targetVector.magnitude;
        if (targetDistance <= .000001f)
            return;

        float minimumReach = Mathf.Abs(leg.upperLength - leg.lowerLength) + .0001f;
        float maximumReach = leg.upperLength + leg.lowerLength - .0001f;
        float clampedDistance = Mathf.Clamp(targetDistance, minimumReach, maximumReach);
        Vector3 targetDirection = targetVector / targetDistance;

        Vector3 currentUpperVector = leg.lower.position - upperPosition;
        Vector3 currentKneeOffset = currentUpperVector
            - targetDirection * Vector3.Dot(currentUpperVector, targetDirection);
        if (currentKneeOffset.sqrMagnitude <= .000001f)
        {
            Vector3 fallback = Vector3.Cross(targetDirection, GetUp(upperPosition));
            currentKneeOffset = Vector3.Cross(fallback, targetDirection);
        }
        if (currentKneeOffset.sqrMagnitude <= .000001f)
            return;

        Vector3 bendDirection = currentKneeOffset.normalized;
        float along =
            (leg.upperLength * leg.upperLength
             - leg.lowerLength * leg.lowerLength
             + clampedDistance * clampedDistance)
            / (2f * clampedDistance);
        float perpendicular = Mathf.Sqrt(Mathf.Max(
            0f,
            leg.upperLength * leg.upperLength - along * along));
        Vector3 targetKnee = upperPosition
            + targetDirection * along
            + bendDirection * perpendicular;

        RotateTowardsLimited(
            leg.upper,
            leg.lower.position - leg.upper.position,
            targetKnee - leg.upper.position,
            maximumJointCorrectionDegrees);

        RotateTowardsLimited(
            leg.lower,
            leg.ankle.position - leg.lower.position,
            targetAnkle - leg.lower.position,
            maximumJointCorrectionDegrees);
    }

    Vector3 GetUp(Vector3 worldPosition)
    {
        if (useSphericalUp && gravityCenter != null)
        {
            Vector3 radial = worldPosition - gravityCenter.position;
            if (radial.sqrMagnitude > .000001f)
                return radial.normalized;
        }
        return transform.up;
    }

    static void RotateTowardsLimited(
        Transform bone,
        Vector3 currentDirection,
        Vector3 targetDirection,
        float maximumDegrees)
    {
        if (currentDirection.sqrMagnitude <= .000001f
            || targetDirection.sqrMagnitude <= .000001f)
            return;

        Quaternion delta = Quaternion.FromToRotation(currentDirection, targetDirection);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
            angle -= 360f;
        if (axis.sqrMagnitude <= .000001f || Mathf.Abs(angle) <= .001f)
            return;

        float limitedAngle = Mathf.Clamp(angle, -maximumDegrees, maximumDegrees);
        bone.rotation = Quaternion.AngleAxis(limitedAngle, axis.normalized) * bone.rotation;
    }

    static float GetBoneWeight(BoneWeight weight, int boneIndex)
    {
        float result = 0f;
        if (weight.boneIndex0 == boneIndex)
            result += weight.weight0;
        if (weight.boneIndex1 == boneIndex)
            result += weight.weight1;
        if (weight.boneIndex2 == boneIndex)
            result += weight.weight2;
        if (weight.boneIndex3 == boneIndex)
            result += weight.weight3;
        return result;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        for (int i = 0; i < runtimeLegs.Count; i++)
        {
            LegRuntime leg = runtimeLegs[i];
            Gizmos.color = leg.hasGround ? Color.cyan : Color.gray;
            Gizmos.DrawSphere(leg.lastSolePosition, .018f);
            if (!leg.hasGround)
                continue;
            Gizmos.DrawLine(leg.lastSolePosition, leg.lastGroundPoint);
            Gizmos.DrawRay(leg.lastGroundPoint, leg.lastGroundNormal * .12f);
        }
    }
}
