using System.Collections.Generic;
using UnityEngine;

public static class ProceduralCreatureAssembler
{
    public static GameObject Build(
        CreatureGenome genome,
        Transform parent,
        SphericalGravitySource gravitySource,
        Material sharedMaterial,
        LayerMask groundLayers,
        Vector3 worldPosition,
        Quaternion worldRotation)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(37);}
    try
    {
        EnsureGraph(genome);
        var root = new GameObject($"Creature_{genome.topology}_{genome.seed}");
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(worldPosition, worldRotation);

        var body = root.AddComponent<Rigidbody>();
        body.mass = CalculateMass(genome);
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezeRotation;

        var capsule = root.AddComponent<CapsuleCollider>();
        float bodyClearance = ConfigureCollider(genome, capsule);
        PhysicMaterial generatedPhysicsMaterial = CreatePhysicsMaterial(genome);
        if (generatedPhysicsMaterial != null)
            capsule.sharedMaterial = generatedPhysicsMaterial;

        CreatureRig rig = BuildRig(genome, root.transform);
        RebuildTorsoColliders(genome, rig, root.transform, capsule, generatedPhysicsMaterial);
        Mesh skinMesh = CreatureSkinnedMeshBuilder.Build(genome, rig, root.transform);

        var skinObject = new GameObject("SkinnedBody");
        skinObject.transform.SetParent(root.transform, false);
        var renderer = skinObject.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = skinMesh;
        renderer.sharedMaterial = sharedMaterial;
        renderer.rootBone = rig.body;
        renderer.bones = rig.bones;
        renderer.quality = SkinQuality.Bone4;
        renderer.updateWhenOffscreen = false;
        float boundsSize = Mathf.Max(
            genome.bodyLength + genome.tailLength + genome.headLength + genome.neckLength,
            genome.legLength * 2f + genome.bodyHeight) * 2f;
        renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * Mathf.Max(10f, boundsSize));

        var meshOwner = root.AddComponent<GeneratedCreatureMeshOwner>();
        meshOwner.Configure(skinMesh, generatedPhysicsMaterial);

        var motor = root.AddComponent<CreatureSphereMotor>();
        float moveSpeed = genome.topology == CreatureTopology.Serpentine
            ? 6.5f
            : Mathf.Clamp(
                Mathf.Min(genome.frontLegLength, genome.rearLegLength) * genome.gaitFrequency * 0.72f,
                1.5f,
                4.5f);
        motor.Configure(gravitySource, body, capsule, groundLayers, moveSpeed, 28f, 18f,
            Vector3.forward, bodyClearance, genome.topology != CreatureTopology.Serpentine);

        if (genome.topology == CreatureTopology.Serpentine)
        {
            var serpentineLocomotion = root.AddComponent<SerpentineContactLocomotion>();
            serpentineLocomotion.Configure(gravitySource, body, rig, genome, groundLayers);
        }

        var semanticAnimator = root.AddComponent<CreatureSemanticAnimator>();
        semanticAnimator.Configure(gravitySource, body, genome, rig, groundLayers);
        var torsoRuntime = root.AddComponent<CreatureTorsoRuntime>();
        torsoRuntime.Configure(genome, rig, renderer, meshOwner, body, capsule, generatedPhysicsMaterial);
        return root;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public static float GetBodyClearance(CreatureGenome genome)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(38);}
    try
    {
        EnsureGraph(genome);
        if (genome.topology == CreatureTopology.Serpentine)
            return Mathf.Max(0.45f, genome.bodyHeight * 0.42f) + 0.08f;
        if (TryGetSupportBounds(genome.bodyGraph, out float supportBottom, out _))
            return -supportBottom + 0.08f;
        return Mathf.Max(genome.frontLegLength, genome.rearLegLength)
            + genome.bodyHeight * 0.35f + 0.08f;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    static void EnsureGraph(CreatureGenome genome)
    {
        if (genome.torsoSpline == null || !genome.torsoSpline.Validate(out _))
            genome.torsoSpline = CreatureTorsoSpline.CreateLegacyFallback(
                genome.bodyLength, genome.bodyWidth, genome.bodyHeight,
                genome.topology == CreatureTopology.Serpentine ? 9 : 5);
        if (genome.designLanguage == null)
            genome.designLanguage = CreatureBodyGraphBuilder.GenerateDesignLanguage(genome);
        if (genome.bodyGraph == null || !genome.bodyGraph.Validate(out _))
            genome.bodyGraph = CreatureBodyGraphBuilder.Build(genome);
    }

    static float CalculateMass(CreatureGenome genome)
    {
        float mass = 0f;
        foreach (CreatureBodyNode node in genome.bodyGraph.nodes)
        {
            if (node.type == CreatureBodyNodeType.Torso || node.type == CreatureBodyNodeType.Spine)
                mass += node.size.x * node.size.y * node.size.z * 0.18f;
        }
        return Mathf.Clamp(mass, 1f, 80f);
    }

    static PhysicMaterial CreatePhysicsMaterial(CreatureGenome genome)
    {
        if (genome.topology != CreatureTopology.Serpentine)
            return null;
        return new PhysicMaterial($"SerpentineContact_{genome.seed}")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounceCombine = PhysicMaterialCombine.Minimum
        };
    }

    static float ConfigureCollider(CreatureGenome genome, CapsuleCollider capsule)
    {
        if (genome.topology == CreatureTopology.Serpentine)
        {
            capsule.direction = 2;
            capsule.radius = Mathf.Clamp(Mathf.Max(genome.bodyWidth, genome.bodyHeight) * 0.32f, 0.28f, 0.72f);
            capsule.height = Mathf.Max(capsule.radius * 2f, genome.bodyLength * 0.82f);
            capsule.center = Vector3.zero;
            return GetBodyClearance(genome);
        }
        float maximumLegLength = Mathf.Max(genome.frontLegLength, genome.rearLegLength);
        float colliderBottom = -maximumLegLength - genome.bodyHeight * 0.35f;
        float colliderTop = genome.bodyHeight * 0.5f;
        if (TryGetSupportBounds(genome.bodyGraph, out float graphBottom, out float graphTop))
        {
            colliderBottom = graphBottom;
            colliderTop = graphTop;
        }
        capsule.direction = 1;
        capsule.radius = Mathf.Clamp(genome.bodyWidth * 0.24f, 0.28f, 0.7f);
        capsule.height = Mathf.Max(capsule.radius * 2f, colliderTop - colliderBottom);
        capsule.center = Vector3.up * (colliderBottom + capsule.height * 0.5f);
        return -colliderBottom + 0.08f;
    }

    public static float RefreshRootCollider(CreatureGenome genome, CapsuleCollider capsule)
    {
        EnsureGraph(genome);
        return ConfigureCollider(genome, capsule);
    }

    static bool TryGetSupportBounds(CreatureBodyGraph graph, out float supportBottom, out float bodyTop)
    {
        supportBottom = float.NegativeInfinity;
        bodyTop = float.NegativeInfinity;
        if (graph == null || graph.nodes == null || graph.nodes.Count == 0)
            return false;

        var nodeMatrices = new Matrix4x4[graph.nodes.Count];
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            Matrix4x4 local = Matrix4x4.TRS(
                node.localPosition,
                Quaternion.Euler(node.localEulerAngles),
                Vector3.one);
            nodeMatrices[i] = node.parentIndex >= 0 ? nodeMatrices[node.parentIndex] * local : local;
            Vector3 position = nodeMatrices[i].MultiplyPoint3x4(Vector3.zero);

            if (node.type == CreatureBodyNodeType.Spine || node.type == CreatureBodyNodeType.Torso)
                bodyTop = Mathf.Max(bodyTop, position.y + node.size.y * 0.5f);

            if (node.type != CreatureBodyNodeType.UpperLeg)
                continue;

            int lowerIndex = FindChild(graph, i, CreatureBodyNodeType.LowerLeg);
            int footIndex = lowerIndex >= 0 ? FindChild(graph, lowerIndex, CreatureBodyNodeType.Foot) : -1;
            if (lowerIndex < 0 || footIndex < 0)
                continue;

            float legLength = graph.nodes[lowerIndex].localPosition.magnitude
                + graph.nodes[footIndex].localPosition.magnitude;
            float soleOffset = graph.nodes[footIndex].size.y * 0.5f;
            // Use the shortest effective support height so every leg can reach the ground.
            // Keeping the legs partially bent also leaves horizontal reach for each step.
            float bentSupportHeight = legLength * 0.82f;
            supportBottom = Mathf.Max(supportBottom, position.y - bentSupportHeight - soleOffset);
        }

        if (float.IsNegativeInfinity(supportBottom))
            return false;
        if (float.IsNegativeInfinity(bodyTop))
            bodyTop = 0.5f;
        return true;
    }

    static CreatureRig BuildRig(CreatureGenome genome, Transform root)
    {
        CreatureBodyGraph graph = genome.bodyGraph;
        var bones = new List<Transform>(graph.nodes.Count + 1);
        var rig = new CreatureRig
        {
            topology = genome.topology,
            graph = graph,
            nodeBones = new Transform[graph.nodes.Count],
            nodeBoneIndices = new int[graph.nodes.Count]
        };
        rig.armature = CreateBone("Armature", root, Vector3.zero, Quaternion.identity, bones, out _);

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            Transform parent = node.parentIndex >= 0 ? rig.nodeBones[node.parentIndex] : rig.armature;
            Transform bone = CreateBone($"{node.type}_{node.id:00}", parent, node.localPosition,
                Quaternion.Euler(node.localEulerAngles), bones, out int boneIndex);
            rig.nodeBones[i] = bone;
            rig.nodeBoneIndices[i] = boneIndex;
        }

        PopulateSemanticRig(rig, bones);
        rig.bones = bones.ToArray();
        return rig;
    }

    static void PopulateSemanticRig(CreatureRig rig, List<Transform> bones)
    {
        var spineBones = new List<Transform>();
        var spineIndices = new List<int>();
        var legs = new List<CreatureLegRig>();
        var upperIndices = new List<int>();
        var lowerIndices = new List<int>();
        var footIndices = new List<int>();
        var secondary = new List<CreatureSecondaryRig>();
        int firstHead = -1;
        int firstTailRoot = -1;
        int firstTailTip = -1;

        for (int i = 0; i < rig.graph.nodes.Count; i++)
        {
            CreatureBodyNode node = rig.graph.nodes[i];
            if (node.type == CreatureBodyNodeType.Spine)
            {
                spineBones.Add(rig.nodeBones[i]);
                spineIndices.Add(rig.nodeBoneIndices[i]);
            }
            if (node.type == CreatureBodyNodeType.Head && firstHead < 0)
                firstHead = i;
            if (node.type == CreatureBodyNodeType.UpperLeg)
                TryBuildLegRig(rig, i, legs, upperIndices, lowerIndices, footIndices);
            if (IsSecondary(node.type))
            {
                secondary.Add(new CreatureSecondaryRig
                {
                    bone = rig.nodeBones[i],
                    restRotation = rig.nodeBones[i].localRotation,
                    type = node.type,
                    phase = node.animationPhase,
                    side = node.side
                });
            }
            if (node.type == CreatureBodyNodeType.Tail)
            {
                if (firstTailRoot < 0)
                    firstTailRoot = i;
                else if (rig.graph.nodes[i].chainIndex == rig.graph.nodes[firstTailRoot].chainIndex && firstTailTip < 0)
                    firstTailTip = i;
            }
        }

        rig.spineBones = spineBones.ToArray();
        rig.spineIndices = spineIndices.ToArray();
        rig.body = rig.spineBones.Length > 0 ? rig.spineBones[0] : rig.armature;
        rig.bodyIndex = rig.spineIndices.Length > 0 ? rig.spineIndices[0] : 0;
        rig.legs = legs.ToArray();
        rig.upperLegIndices = upperIndices.ToArray();
        rig.lowerLegIndices = lowerIndices.ToArray();
        rig.footIndices = footIndices.ToArray();
        rig.secondaryBones = secondary.ToArray();

        if (firstHead >= 0)
        {
            rig.head = rig.nodeBones[firstHead];
            rig.headIndex = rig.nodeBoneIndices[firstHead];
            int neckIndex = rig.graph.nodes[firstHead].parentIndex;
            rig.neck = neckIndex >= 0 ? rig.nodeBones[neckIndex] : rig.body;
            rig.neckIndex = neckIndex >= 0 ? rig.nodeBoneIndices[neckIndex] : rig.bodyIndex;
        }
        else
        {
            rig.neck = rig.head = rig.body;
            rig.neckIndex = rig.headIndex = rig.bodyIndex;
        }

        rig.tailBase = firstTailRoot >= 0 ? rig.nodeBones[firstTailRoot] : rig.body;
        rig.tailBaseIndex = firstTailRoot >= 0 ? rig.nodeBoneIndices[firstTailRoot] : rig.bodyIndex;
        rig.tailTip = firstTailTip >= 0 ? rig.nodeBones[firstTailTip] : rig.tailBase;
        rig.tailTipIndex = firstTailTip >= 0 ? rig.nodeBoneIndices[firstTailTip] : rig.tailBaseIndex;
    }

    static void TryBuildLegRig(
        CreatureRig rig,
        int upperNode,
        ICollection<CreatureLegRig> legs,
        ICollection<int> upperIndices,
        ICollection<int> lowerIndices,
        ICollection<int> footIndices)
    {
        int lowerNode = FindChild(rig.graph, upperNode, CreatureBodyNodeType.LowerLeg);
        int footNode = lowerNode >= 0 ? FindChild(rig.graph, lowerNode, CreatureBodyNodeType.Foot) : -1;
        if (lowerNode < 0 || footNode < 0)
            return;
        CreatureBodyNode upper = rig.graph.nodes[upperNode];
        CreatureBodyNode lower = rig.graph.nodes[lowerNode];
        legs.Add(new CreatureLegRig
        {
            side = upper.side,
            upper = rig.nodeBones[upperNode],
            lower = rig.nodeBones[lowerNode],
            foot = rig.nodeBones[footNode],
            upperLength = lower.localPosition.magnitude,
            lowerLength = rig.graph.nodes[footNode].localPosition.magnitude,
            footSoleOffset = Mathf.Max(0.01f, rig.graph.nodes[footNode].size.y * 0.5f),
            phaseOffset = upper.gaitGroup * 0.5f,
            gaitGroup = upper.gaitGroup,
            longitudinalPosition = upper.longitudinalPosition
        });
        upperIndices.Add(rig.nodeBoneIndices[upperNode]);
        lowerIndices.Add(rig.nodeBoneIndices[lowerNode]);
        footIndices.Add(rig.nodeBoneIndices[footNode]);
    }

    static int FindChild(CreatureBodyGraph graph, int parent, CreatureBodyNodeType type)
    {
        for (int i = parent + 1; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            if (node.parentIndex == parent && node.type == type)
                return i;
        }
        return -1;
    }

    static bool IsSecondary(CreatureBodyNodeType type)
    {
        return type == CreatureBodyNodeType.Neck
            || type == CreatureBodyNodeType.Head
            || type == CreatureBodyNodeType.UpperArm
            || type == CreatureBodyNodeType.LowerArm
            || type == CreatureBodyNodeType.Hand
            || type == CreatureBodyNodeType.Tail
            || type == CreatureBodyNodeType.Tentacle
            || type == CreatureBodyNodeType.Horn
            || type == CreatureBodyNodeType.BackPlate
            || type == CreatureBodyNodeType.Sensor;
    }

    public static void RebuildTorsoColliders(
        CreatureGenome genome,
        CreatureRig rig,
        Transform root,
        CapsuleCollider rootCapsule,
        PhysicMaterial physicsMaterial)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (!child.name.StartsWith("ImplicitTorsoCollider_", System.StringComparison.Ordinal)) continue;
            if (Application.isPlaying) Object.Destroy(child.gameObject);
            else Object.DestroyImmediate(child.gameObject);
        }

        if (genome.torsoSpline == null || genome.torsoSpline.points.Count < 2) return;
        float largestRadius = 0.1f;
        for (int i = 0; i < genome.torsoSpline.points.Count - 1; i++)
        {
            CreatureTorsoControlPoint a = genome.torsoSpline.points[i];
            CreatureTorsoControlPoint b = genome.torsoSpline.points[i + 1];
            Vector3 segment = b.localPosition - a.localPosition;
            float radius = Mathf.Max(0.08f,
                Mathf.Min(a.width, a.height, b.width, b.height) * 0.28f);
            largestRadius = Mathf.Max(largestRadius, radius);
            var colliderObject = new GameObject($"ImplicitTorsoCollider_{i:00}");
            colliderObject.transform.SetParent(root, false);
            colliderObject.transform.localPosition = (a.localPosition + b.localPosition) * 0.5f;
            colliderObject.transform.localRotation = segment.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(segment.normalized, Vector3.up) : Quaternion.identity;
            var capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = radius;
            capsule.height = Mathf.Max(radius * 2f, segment.magnitude + radius * 1.2f);
            if (physicsMaterial != null)
                capsule.sharedMaterial = physicsMaterial;
        }

        if (rootCapsule != null && genome.topology == CreatureTopology.Serpentine)
            rootCapsule.radius = Mathf.Clamp(largestRadius, 0.18f, 0.9f);
    }

    static Transform CreateBone(
        string name,
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        ICollection<Transform> bones,
        out int index)
    {
        var boneObject = new GameObject(name);
        Transform bone = boneObject.transform;
        bone.SetParent(parent, false);
        bone.localPosition = localPosition;
        bone.localRotation = localRotation;
        bone.localScale = Vector3.one;
        index = bones.Count;
        bones.Add(bone);
        return bone;
    }
}

public sealed class GeneratedCreatureMeshOwner : MonoBehaviour
{
    Mesh generatedMesh;
    PhysicMaterial generatedPhysicsMaterial;

    public void Configure(Mesh mesh, PhysicMaterial physicsMaterial = null)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(39);}
    try
    {
        generatedMesh = mesh;
        generatedPhysicsMaterial = physicsMaterial;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    public void ReplaceMesh(Mesh mesh)
    {
        if (generatedMesh == mesh) return;
        Mesh previous = generatedMesh;
        generatedMesh = mesh;
        if (previous == null) return;
        if (Application.isPlaying) Destroy(previous);
        else DestroyImmediate(previous);
    }

    void OnDestroy()
    {
        if (Application.isPlaying)
        {
            if (generatedMesh != null) Destroy(generatedMesh);
            if (generatedPhysicsMaterial != null) Destroy(generatedPhysicsMaterial);
        }
        else
        {
            if (generatedMesh != null) DestroyImmediate(generatedMesh);
            if (generatedPhysicsMaterial != null) DestroyImmediate(generatedPhysicsMaterial);
        }
    }
}
