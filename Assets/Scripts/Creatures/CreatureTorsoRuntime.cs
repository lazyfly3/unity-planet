using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
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
    CreaturePhenotype phenotype;
    CreatureV5Phenotype v5Phenotype;
    CreatureRig rig;
    SkinnedMeshRenderer skinRenderer;
    SkinnedMeshRenderer[] v4Renderers;
    SkinnedMeshRenderer v4DetailRenderer;
    GeneratedCreatureMeshOwner meshOwner;
    Rigidbody body;
    CapsuleCollider rootCapsule;
    CreatureSphereMotor motor;
    CreatureSemanticAnimator animator;
    SerpentineContactLocomotion serpentine;
    CreatureProceduralController proceduralController;
    LODGroup lodGroup;
    PhysicMaterial physicsMaterial;
    int revision;
    bool editing;
    bool rebuildInProgress;
    bool previousKinematic;
    bool useV4;
    bool useV5;
    CancellationTokenSource rebuildCancellation;
    Coroutine scheduledRebuild;

    public CreatureGenome Genome => genome;
    public CreatureRig Rig => rig;
    public bool IsEditing => editing;
    public bool RebuildInProgress => rebuildInProgress;
    public bool IsV5 => useV5;
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

    public void ConfigureV4(
        CreatureGenome sourceGenome,
        CreaturePhenotype sourcePhenotype,
        CreatureRig sourceRig,
        SkinnedMeshRenderer[] sourceRenderers,
        SkinnedMeshRenderer sourceDetailRenderer,
        GeneratedCreatureMeshOwner owner,
        Rigidbody rigidbody,
        CapsuleCollider capsule,
        CreatureProceduralController controller,
        LODGroup sourceLodGroup,
        PhysicMaterial material)
    {
        genome = sourceGenome;
        phenotype = sourcePhenotype;
        rig = sourceRig;
        v4Renderers = sourceRenderers;
        v4DetailRenderer = sourceDetailRenderer;
        skinRenderer = sourceRenderers != null && sourceRenderers.Length > 0 ? sourceRenderers[0] : null;
        meshOwner = owner;
        body = rigidbody;
        rootCapsule = capsule;
        proceduralController = controller;
        lodGroup = sourceLodGroup;
        physicsMaterial = material;
        useV4 = true;
        useV5 = false;
    }

    public void ConfigureV5(
        CreatureGenome sourceGenome,
        CreatureV5Phenotype sourcePhenotype,
        CreatureRig sourceRig,
        SkinnedMeshRenderer[] sourceRenderers,
        SkinnedMeshRenderer sourceDetailRenderer,
        GeneratedCreatureMeshOwner owner,
        Rigidbody rigidbody,
        CapsuleCollider capsule,
        CreatureProceduralController controller,
        LODGroup sourceLodGroup,
        PhysicMaterial material)
    {
        genome = sourceGenome;
        v5Phenotype = sourcePhenotype;
        phenotype = sourcePhenotype.motion;
        rig = sourceRig;
        v4Renderers = sourceRenderers;
        v4DetailRenderer = sourceDetailRenderer;
        skinRenderer = sourceRenderers != null && sourceRenderers.Length > 0 ? sourceRenderers[0] : null;
        meshOwner = owner;
        body = rigidbody;
        rootCapsule = capsule;
        proceduralController = controller;
        lodGroup = sourceLodGroup;
        physicsMaterial = material;
        useV4 = false;
        useV5 = true;
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
            if (proceduralController != null) proceduralController.enabled = false;
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
            if (proceduralController != null)
            {
                proceduralController.ResetFootContacts();
                proceduralController.enabled = true;
            }
        }
    }

    public void ApplyBonePreview()
    {
        if (genome == null || genome.torsoSpline == null || rig == null) return;
        if (useV5)
        {
            CreatureV5Phenotype previewPhenotype = CreatureV5PhenotypeBuilder.Build(genome);
            previewPhenotype.motion.ApplyToGraph(genome.bodyGraph);
            ProceduralCreatureAssembler.ApplyRigRestPose(rig);
            return;
        }
        if (useV4)
        {
            CreaturePhenotype previewPhenotype = CreaturePhenotypeBuilder.Build(genome);
            previewPhenotype.ApplyToGraph(genome.bodyGraph);
            ProceduralCreatureAssembler.ApplyRigRestPose(rig);
            return;
        }
        CreatureTorsoRigBuilder.BuildOrUpdate(rig, genome.torsoSpline);
    }

    public void ScheduleFinalRebuild(float delaySeconds = 0.18f)
    {
        CancelPendingRebuild();
        scheduledRebuild = StartCoroutine(RebuildAfterDelay(Mathf.Max(0f, delaySeconds)));
    }

    public void CancelPendingRebuild()
    {
        revision++;
        if (scheduledRebuild != null)
        {
            StopCoroutine(scheduledRebuild);
            scheduledRebuild = null;
        }
        if (rebuildCancellation != null)
        {
            rebuildCancellation.Cancel();
            rebuildCancellation = null;
        }
        rebuildInProgress = false;
    }

    IEnumerator RebuildAfterDelay(float delaySeconds)
    {
        if (delaySeconds > 0f)
            yield return new WaitForSecondsRealtime(delaySeconds);
        scheduledRebuild = null;
        RebuildAsync(CreatureBodyMeshQuality.Final);
    }

    public async void RebuildAsync(CreatureBodyMeshQuality quality)
    {
        if (genome == null || genome.torsoSpline == null) return;
        CancelPendingRebuild();
        int requestedRevision = ++revision;
        var cancellation = new CancellationTokenSource();
        rebuildCancellation = cancellation;
        rebuildInProgress = true;
        try
        {
            if (useV5)
            {
                CreatureV5Phenotype nextPhenotype = CreatureV5PhenotypeBuilder.Build(genome);
                Task<CreatureV5MeshData> lod0Task = CreatureV5MeshGenerator.BuildAsync(
                    nextPhenotype, CreatureBodyMeshQuality.Final, requestedRevision, cancellation.Token);
                Task<CreatureV5MeshData> lod1Task = CreatureV5MeshGenerator.BuildAsync(
                    nextPhenotype, CreatureBodyMeshQuality.Lod1, requestedRevision, cancellation.Token);
                Task<CreatureV5MeshData> lod2Task = CreatureV5MeshGenerator.BuildAsync(
                    nextPhenotype, CreatureBodyMeshQuality.Lod2, requestedRevision, cancellation.Token);
                CreatureV5MeshData[] nextData = await Task.WhenAll(lod0Task, lod1Task, lod2Task);
                if (this == null || cancellation.IsCancellationRequested || requestedRevision != revision)
                    return;
                ApplyV5Meshes(nextPhenotype, nextData);
                return;
            }
            if (useV4)
            {
                CreaturePhenotype nextPhenotype = CreaturePhenotypeBuilder.Build(genome);
                Task<CreatureImplicitMeshData> lod0Task = CreatureImplicitBodyMesher.BuildAsync(
                    nextPhenotype, CreatureBodyMeshQuality.Final, requestedRevision, cancellation.Token);
                Task<CreatureImplicitMeshData> lod1Task = CreatureImplicitBodyMesher.BuildAsync(
                    nextPhenotype, CreatureBodyMeshQuality.Lod1, requestedRevision, cancellation.Token);
                Task<CreatureImplicitMeshData> lod2Task = CreatureImplicitBodyMesher.BuildAsync(
                    nextPhenotype, CreatureBodyMeshQuality.Lod2, requestedRevision, cancellation.Token);
                CreatureImplicitMeshData[] v4Data = await Task.WhenAll(lod0Task, lod1Task, lod2Task);
                if (this == null || cancellation.IsCancellationRequested || requestedRevision != revision)
                    return;
                ApplyV4Meshes(nextPhenotype, v4Data);
                return;
            }

            CreatureImplicitMeshData data = await CreatureImplicitBodyMesher.BuildAsync(
                genome.torsoSpline, quality, requestedRevision, cancellation.Token);
            if (this == null || cancellation.IsCancellationRequested || requestedRevision != revision)
                return;
            ApplyMesh(data);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Debug.LogError("Creature torso rebuild failed: " + exception, this);
        }
        finally
        {
            if (rebuildCancellation == cancellation)
            {
                rebuildCancellation = null;
                rebuildInProgress = false;
            }
            cancellation.Dispose();
        }
    }

    public void RebuildNow(CreatureBodyMeshQuality quality)
    {
        CancelPendingRebuild();
        int requestedRevision = ++revision;
        if (useV5)
        {
            CreatureV5Phenotype nextPhenotype = CreatureV5PhenotypeBuilder.Build(genome);
            var nextData = new[]
            {
                CreatureV5MeshGenerator.Build(nextPhenotype, CreatureBodyMeshQuality.Final, requestedRevision),
                CreatureV5MeshGenerator.Build(nextPhenotype, CreatureBodyMeshQuality.Lod1, requestedRevision),
                CreatureV5MeshGenerator.Build(nextPhenotype, CreatureBodyMeshQuality.Lod2, requestedRevision)
            };
            ApplyV5Meshes(nextPhenotype, nextData);
            return;
        }
        if (useV4)
        {
            CreaturePhenotype nextPhenotype = CreaturePhenotypeBuilder.Build(genome);
            var v4Data = new[]
            {
                CreatureImplicitBodyMesher.Build(nextPhenotype, CreatureBodyMeshQuality.Final, requestedRevision),
                CreatureImplicitBodyMesher.Build(nextPhenotype, CreatureBodyMeshQuality.Lod1, requestedRevision),
                CreatureImplicitBodyMesher.Build(nextPhenotype, CreatureBodyMeshQuality.Lod2, requestedRevision)
            };
            ApplyV4Meshes(nextPhenotype, v4Data);
            return;
        }
        CreatureImplicitMeshData data = CreatureImplicitBodyMesher.Build(genome.torsoSpline, quality, requestedRevision);
        ApplyMesh(data);
    }

    void ApplyV4Meshes(CreaturePhenotype nextPhenotype, CreatureImplicitMeshData[] data)
    {
        nextPhenotype.ApplyToGraph(genome.bodyGraph);
        ProceduralCreatureAssembler.ApplyRigRestPose(rig);
        CreatureBodyMeshQuality[] qualities =
        {
            CreatureBodyMeshQuality.Final,
            CreatureBodyMeshQuality.Lod1,
            CreatureBodyMeshQuality.Lod2
        };
        int count = Mathf.Min(v4Renderers.Length, data.Length);
        for (int i = 0; i < count; i++)
        {
            Mesh mesh = CreatureSkinnedMeshBuilder.BuildUnifiedV4(
                genome, nextPhenotype, rig, transform, data[i], qualities[i]);
            v4Renderers[i].sharedMesh = mesh;
            v4Renderers[i].bones = rig.bones;
            v4Renderers[i].localBounds = nextPhenotype.fieldBounds;
            if (i == 0) meshOwner.ReplaceMesh(mesh);
            else meshOwner.ReplaceAdditionalMesh(i - 1, mesh);
        }
        Mesh detailMesh = CreatureSkinnedMeshBuilder.BuildHardDetailsV4(genome, rig, transform);
        v4DetailRenderer.sharedMesh = detailMesh;
        v4DetailRenderer.bones = rig.bones;
        v4DetailRenderer.localBounds = nextPhenotype.fieldBounds;
        meshOwner.ReplaceAdditionalMesh(2, detailMesh);

        phenotype = nextPhenotype;
        float clearance = ProceduralCreatureAssembler.RefreshRootCollider(genome, rootCapsule);
        ProceduralCreatureAssembler.RebuildTorsoColliders(
            genome, rig, transform, rootCapsule, physicsMaterial);
        proceduralController.Reconfigure(nextPhenotype);
        lodGroup?.RecalculateBounds();
        MeshRebuilt?.Invoke();
    }

    void ApplyV5Meshes(CreatureV5Phenotype nextPhenotype, CreatureV5MeshData[] data)
    {
        nextPhenotype.motion.ApplyToGraph(genome.bodyGraph);
        rig.graph = genome.bodyGraph;
        ProceduralCreatureAssembler.ApplyRigRestPose(rig);
        int count = Mathf.Min(v4Renderers.Length, data.Length);
        for (int i = 0; i < count; i++)
        {
            Mesh mesh = CreatureV5MeshFactory.Create(data[i], rig, transform, genome.seed);
            v4Renderers[i].sharedMesh = mesh;
            v4Renderers[i].bones = rig.bones;
            v4Renderers[i].localBounds = data[i].bounds;
            if (i == 0) meshOwner.ReplaceMesh(mesh);
            else meshOwner.ReplaceAdditionalMesh(i - 1, mesh);
        }
        Mesh detailMesh = CreatureSkinnedMeshBuilder.BuildHardDetailsV4(genome, rig, transform);
        v4DetailRenderer.sharedMesh = detailMesh;
        v4DetailRenderer.bones = rig.bones;
        v4DetailRenderer.localBounds = nextPhenotype.motion.fieldBounds;
        meshOwner.ReplaceAdditionalMesh(2, detailMesh);

        v5Phenotype = nextPhenotype;
        phenotype = nextPhenotype.motion;
        ProceduralCreatureAssembler.RefreshRootCollider(genome, rootCapsule);
        ProceduralCreatureAssembler.RebuildTorsoColliders(
            genome, rig, transform, rootCapsule, physicsMaterial);
        proceduralController.Reconfigure(nextPhenotype.motion);
        lodGroup?.RecalculateBounds();
        MeshRebuilt?.Invoke();
    }

    void ApplyMesh(CreatureImplicitMeshData data)
    {
        CreatureTorsoRigBuilder.BuildOrUpdate(rig, genome.torsoSpline);
        Mesh mesh = CreatureSkinnedMeshBuilder.BuildTorso(genome, rig, transform, data);
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

    void OnDestroy()
    {
        CancelPendingRebuild();
    }
}
