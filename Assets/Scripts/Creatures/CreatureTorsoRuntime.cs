using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public static class CreatureTorsoRigBuilder
{
    public static void BuildOrUpdate(CreatureRig rig, CreatureTorsoSpline spline)
    {
        if (rig == null || rig.spineBones == null || rig.spineBones.Length == 0)
            throw new ArgumentException("A creature rig with spine bones is required.", nameof(rig));
        string error = null;
        if (spline == null || !spline.Validate(out error))
            throw new ArgumentException(error ?? "A valid torso spline is required.", nameof(spline));

        Vector3 previous = Vector3.zero;
        for (int i = 0; i < rig.spineBones.Length; i++)
        {
            float t = rig.spineBones.Length == 1 ? 0f : i / (float)(rig.spineBones.Length - 1);
            Vector3 position = SamplePosition(spline, t);
            Transform bone = rig.spineBones[i];
            bone.localPosition = i == 0 ? position : position - previous;
            bone.localRotation = Quaternion.identity;
            previous = position;

            int nodeIndex = FindNodeIndex(rig, bone);
            if (nodeIndex < 0) continue;
            CreatureBodyNode node = rig.graph.nodes[nodeIndex];
            CreatureTorsoControlPoint shape = SampleShape(spline, t);
            node.localPosition = bone.localPosition;
            node.size = new Vector3(shape.width, shape.height,
                i > 0 ? Vector3.Distance(position, SamplePosition(spline, (i - 1f) / (rig.spineBones.Length - 1f))) : 0.1f);
            node.radius = Mathf.Min(shape.width, shape.height) * 0.5f;
        }
    }

    public static Vector3 SamplePosition(CreatureTorsoSpline spline, float normalizedDistance)
    {
        normalizedDistance = Mathf.Clamp01(normalizedDistance);
        float total = 0f;
        for (int i = 0; i < spline.points.Count - 1; i++)
            total += Vector3.Distance(spline.points[i].localPosition, spline.points[i + 1].localPosition);
        if (total < 0.0001f) return spline.points[0].localPosition;

        float target = normalizedDistance * total;
        float traversed = 0f;
        for (int i = 0; i < spline.points.Count - 1; i++)
        {
            Vector3 a = spline.points[i].localPosition;
            Vector3 b = spline.points[i + 1].localPosition;
            float length = Vector3.Distance(a, b);
            if (traversed + length >= target || i == spline.points.Count - 2)
                return Vector3.LerpUnclamped(a, b, length > 0f ? (target - traversed) / length : 0f);
            traversed += length;
        }
        return spline.points[spline.points.Count - 1].localPosition;
    }

    public static CreatureTorsoControlPoint SampleShape(CreatureTorsoSpline spline, float normalizedDistance)
    {
        float scaled = Mathf.Clamp01(normalizedDistance) * (spline.points.Count - 1);
        int index = Mathf.Min(Mathf.FloorToInt(scaled), spline.points.Count - 2);
        float t = scaled - index;
        CreatureTorsoControlPoint a = spline.points[index];
        CreatureTorsoControlPoint b = spline.points[index + 1];
        return new CreatureTorsoControlPoint
        {
            localPosition = Vector3.LerpUnclamped(a.localPosition, b.localPosition, t),
            width = Mathf.LerpUnclamped(a.width, b.width, t),
            height = Mathf.LerpUnclamped(a.height, b.height, t),
            rollDegrees = Mathf.LerpAngle(a.rollDegrees, b.rollDegrees, t),
            blendRadius = Mathf.LerpUnclamped(a.blendRadius, b.blendRadius, t),
            taper = Mathf.LerpUnclamped(a.taper, b.taper, t)
        };
    }

    static int FindNodeIndex(CreatureRig rig, Transform bone)
    {
        for (int i = 0; i < rig.nodeBones.Length; i++)
            if (rig.nodeBones[i] == bone) return i;
        return -1;
    }
}

[DisallowMultipleComponent]
public sealed class CreatureTorsoRuntime : MonoBehaviour
{
    CreatureGenome genome;
    CreatureRig rig;
    SkinnedMeshRenderer skinRenderer;
    GeneratedCreatureMeshOwner meshOwner;
    Rigidbody body;
    CapsuleCollider rootCapsule;
    CreatureSphereMotor motor;
    CreatureSemanticAnimator animator;
    SerpentineContactLocomotion serpentine;
    PhysicMaterial physicsMaterial;
    int revision;
    bool editing;
    bool rebuildInProgress;
    bool previousKinematic;

    public CreatureGenome Genome => genome;
    public CreatureRig Rig => rig;
    public bool IsEditing => editing;
    public bool RebuildInProgress => rebuildInProgress;
    public event Action MeshRebuilt;

    public void Configure(
        CreatureGenome sourceGenome,
        CreatureRig sourceRig,
        SkinnedMeshRenderer sourceRenderer,
        GeneratedCreatureMeshOwner owner,
        Rigidbody rigidbody,
        CapsuleCollider capsule,
        PhysicMaterial material)
    {
        genome = sourceGenome;
        rig = sourceRig;
        skinRenderer = sourceRenderer;
        meshOwner = owner;
        body = rigidbody;
        rootCapsule = capsule;
        physicsMaterial = material;
        motor = GetComponent<CreatureSphereMotor>();
        animator = GetComponent<CreatureSemanticAnimator>();
        serpentine = GetComponent<SerpentineContactLocomotion>();
    }

    public void SetEditing(bool value)
    {
        if (editing == value || body == null) return;
        editing = value;
        if (value)
        {
            previousKinematic = body.isKinematic;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            if (motor != null) motor.enabled = false;
            if (animator != null) animator.enabled = false;
            if (serpentine != null) serpentine.enabled = false;
        }
        else
        {
            body.isKinematic = previousKinematic;
            if (motor != null) motor.enabled = true;
            if (animator != null)
            {
                animator.RefreshRestPose();
                animator.enabled = true;
            }
            if (serpentine != null) serpentine.enabled = true;
        }
    }

    public async void RebuildAsync(CreatureBodyMeshQuality quality)
    {
        if (genome == null || genome.torsoSpline == null || rebuildInProgress && quality == CreatureBodyMeshQuality.Preview)
            return;
        int requestedRevision = ++revision;
        rebuildInProgress = true;
        CreatureImplicitMeshData data;
        try
        {
            data = await CreatureImplicitBodyMesher.BuildAsync(genome.torsoSpline, quality, requestedRevision);
        }
        catch (Exception exception)
        {
            rebuildInProgress = false;
            Debug.LogError("Creature torso rebuild failed: " + exception, this);
            return;
        }

        if (this == null || requestedRevision != revision)
            return;
        ApplyMesh(data);
        rebuildInProgress = false;
    }

    public void RebuildNow(CreatureBodyMeshQuality quality)
    {
        int requestedRevision = ++revision;
        CreatureImplicitMeshData data = CreatureImplicitBodyMesher.Build(genome.torsoSpline, quality, requestedRevision);
        ApplyMesh(data);
    }

    void ApplyMesh(CreatureImplicitMeshData data)
    {
        CreatureTorsoRigBuilder.BuildOrUpdate(rig, genome.torsoSpline);
        Mesh mesh = CreatureSkinnedMeshBuilder.Build(genome, rig, transform, data);
        skinRenderer.sharedMesh = mesh;
        float boundsSize = Mathf.Max(
            genome.bodyLength + genome.tailLength + genome.headLength + genome.neckLength,
            genome.legLength * 2f + genome.bodyHeight) * 2f;
        skinRenderer.localBounds = new Bounds(data.bounds.center,
            Vector3.Max(data.bounds.size, Vector3.one * Mathf.Max(10f, boundsSize)));
        meshOwner.ReplaceMesh(mesh);
        float clearance = ProceduralCreatureAssembler.RefreshRootCollider(genome, rootCapsule);
        ProceduralCreatureAssembler.RebuildTorsoColliders(
            genome, rig, transform, rootCapsule, physicsMaterial);
        if (motor != null) motor.UpdateBodyGeometry(rootCapsule, clearance);
        if (animator != null) animator.RefreshRestPose();
        MeshRebuilt?.Invoke();
    }
}
