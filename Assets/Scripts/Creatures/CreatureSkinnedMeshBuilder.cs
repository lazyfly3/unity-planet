using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class CreatureSkinnedMeshBuilder
{
    const int RingSegments = 10;

    public static Mesh Build(CreatureGenome genome, CreatureRig rig, Transform creatureRoot)
    {
CreatureImplicitMeshData implicitBody = CreatureImplicitBodyMesher.Build(
            genome.torsoSpline, CreatureBodyMeshQuality.Final);
        return Build(genome, rig, creatureRoot, implicitBody);
    
}

    public static Mesh Build(
        CreatureGenome genome,
        CreatureRig rig,
        Transform creatureRoot,
        CreatureImplicitMeshData implicitBody)
    {
        var vertices = new List<Vector3>(8192);
        var triangles = new List<int>(16384);
        var colors = new List<Color>(8192);
        var weights = new List<BoneWeight>(8192);

        AppendImplicitBody(vertices, triangles, colors, weights, genome, rig, implicitBody);
        for (int i = 0; i < rig.graph.nodes.Count; i++)
            AppendNodeGeometry(vertices, triangles, colors, weights, genome, rig, creatureRoot, i);

        return CreateMesh($"CreatureGraphSkin_{genome.seed}", vertices, triangles, colors, weights, rig, creatureRoot);
    }

    public static Mesh BuildTorso(
        CreatureGenome genome,
        CreatureRig rig,
        Transform creatureRoot,
        CreatureImplicitMeshData implicitBody)
    {
        var vertices = new List<Vector3>(implicitBody.vertices.Length);
        var triangles = new List<int>(implicitBody.triangles.Length);
        var colors = new List<Color>(implicitBody.vertices.Length);
        var weights = new List<BoneWeight>(implicitBody.vertices.Length);
        AppendImplicitBody(vertices, triangles, colors, weights, genome, rig, implicitBody);
        return CreateMesh($"CreatureTorsoSkin_{genome.seed}", vertices, triangles, colors, weights, rig, creatureRoot);
    }

    public static Mesh BuildAttachments(CreatureGenome genome, CreatureRig rig, Transform creatureRoot)
    {
        var vertices = new List<Vector3>(4096);
        var triangles = new List<int>(8192);
        var colors = new List<Color>(4096);
        var weights = new List<BoneWeight>(4096);
        for (int i = 0; i < rig.graph.nodes.Count; i++)
            AppendNodeGeometry(vertices, triangles, colors, weights, genome, rig, creatureRoot, i);
        return CreateMesh($"CreatureAttachmentSkin_{genome.seed}", vertices, triangles, colors, weights, rig, creatureRoot);
    }

    public static Mesh BuildUnifiedV4(
        CreatureGenome genome,
        CreaturePhenotype phenotype,
        CreatureRig rig,
        Transform creatureRoot,
        CreatureImplicitMeshData implicitBody,
        CreatureBodyMeshQuality quality)
    {
        var mesh = new Mesh
        {
            name = $"CreatureV4_{quality}_{genome.seed}",
            indexFormat = IndexFormat.UInt32
        };
        mesh.vertices = implicitBody.vertices;
        mesh.triangles = implicitBody.triangles;
        mesh.normals = implicitBody.normals;
        mesh.boneWeights = CreatureV4SkinWeightSolver.Calculate(implicitBody.vertices, phenotype, rig);

        var colors = new Color[implicitBody.vertices.Length];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = PatternColorV4(genome, implicitBody.vertices[i], phenotype.fieldBounds);
        mesh.colors = colors;

        var bindPoses = new Matrix4x4[rig.bones.Length];
        for (int i = 0; i < rig.bones.Length; i++)
            bindPoses[i] = rig.bones[i].worldToLocalMatrix * creatureRoot.localToWorldMatrix;
        mesh.bindposes = bindPoses;
        mesh.bounds = implicitBody.bounds;
        return mesh;
    }

    public static Mesh BuildHardDetailsV4(
        CreatureGenome genome,
        CreatureRig rig,
        Transform creatureRoot)
    {
        var vertices = new List<Vector3>(512);
        var triangles = new List<int>(1024);
        var colors = new List<Color>(512);
        var weights = new List<BoneWeight>(512);
        for (int i = 0; i < rig.graph.nodes.Count; i++)
        {
            CreatureBodyNode node = rig.graph.nodes[i];
            Vector3 center = creatureRoot.InverseTransformPoint(rig.nodeBones[i].position);
            int boneIndex = rig.nodeBoneIndices[i];
            switch (node.type)
            {
                case CreatureBodyNodeType.Head:
                    if (genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5)
                    {
                        Transform headBone = rig.nodeBones[i];
                        AppendAnatomicalEyes(vertices, triangles, colors, weights, genome, node, center,
                            boneIndex,
                            creatureRoot.InverseTransformDirection(headBone.right).normalized,
                            creatureRoot.InverseTransformDirection(headBone.up).normalized,
                            creatureRoot.InverseTransformDirection(headBone.forward).normalized);
                        AppendAnatomicalEars(vertices, triangles, colors, weights, genome, node, center,
                            boneIndex,
                            creatureRoot.InverseTransformDirection(headBone.right).normalized,
                            creatureRoot.InverseTransformDirection(headBone.up).normalized,
                            creatureRoot.InverseTransformDirection(headBone.forward).normalized);
                        AppendAnatomicalNose(vertices, triangles, colors, weights, node, center,
                            boneIndex,
                            creatureRoot.InverseTransformDirection(headBone.right).normalized,
                            creatureRoot.InverseTransformDirection(headBone.up).normalized,
                            creatureRoot.InverseTransformDirection(headBone.forward).normalized);
                    }
                    else
                    {
                        AppendEyes(vertices, triangles, colors, weights, genome, node, center, boneIndex);
                    }
                    break;
                case CreatureBodyNodeType.Horn:
                    if (genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5)
                        break;
                    AppendConnectionToParent(vertices, triangles, colors, weights, rig, creatureRoot, i,
                        Mathf.Max(0.08f, node.radius), 0.018f, genome.designLanguage.ornamentColor);
                    break;
                case CreatureBodyNodeType.BackPlate:
                    if (genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5)
                        break;
                    AppendEllipsoid(vertices, triangles, colors, weights, center, node.size,
                        genome.designLanguage.ornamentColor, boneIndex);
                    break;
                case CreatureBodyNodeType.Sensor:
                    if (genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5)
                        break;
                    AppendEllipsoid(vertices, triangles, colors, weights, center, node.size,
                        Color.white, boneIndex);
                    break;
            }
        }
        return CreateMesh($"CreatureV4Details_{genome.seed}",
            vertices, triangles, colors, weights, rig, creatureRoot);
    }

    static Mesh CreateMesh(
        string name,
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureRig rig,
        Transform creatureRoot)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.boneWeights = weights.ToArray();
        var bindPoses = new Matrix4x4[rig.bones.Length];
        for (int i = 0; i < rig.bones.Length; i++)
            bindPoses[i] = rig.bones[i].worldToLocalMatrix * creatureRoot.localToWorldMatrix;
        mesh.bindposes = bindPoses;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AppendImplicitBody(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureRig rig,
        CreatureImplicitMeshData body)
    {
        int start = vertices.Count;
        vertices.AddRange(body.vertices);
        BoneWeight[] bodyWeights = CreatureSkinWeightSolver.Calculate(
            body.vertices, genome.torsoSpline, rig.spineIndices);
        weights.AddRange(bodyWeights);
        for (int i = 0; i < body.vertices.Length; i++)
        {
            int spinePosition = System.Array.IndexOf(rig.spineIndices, bodyWeights[i].boneIndex0);
            float longitudinal = spinePosition >= 0
                ? spinePosition / (float)Mathf.Max(1, rig.spineIndices.Length - 1) : 0.5f;
            colors.Add(PatternColor(genome, longitudinal, i));
        }
        for (int i = 0; i < body.triangles.Length; i++)
            triangles.Add(start + body.triangles[i]);
    }

    static void AppendSpineLoft(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureRig rig,
        Transform root)
    {
        if (rig.spineBones.Length == 0)
            return;
        int first = vertices.Count;
        for (int ring = 0; ring < rig.spineBones.Length; ring++)
        {
            Transform bone = rig.spineBones[ring];
            CreatureBodyNode node = FindNode(rig, bone);
            Vector3 center = root.InverseTransformPoint(bone.position);
            Vector3 tangent = GetSpineTangent(rig, root, ring);
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 axisX = Vector3.Cross(reference, tangent).normalized;
            Vector3 axisY = Vector3.Cross(tangent, axisX).normalized;
            float width = node.size.x * 0.5f;
            float height = node.size.y * 0.5f;
            if (genome.topology == CreatureTopology.Serpentine)
                height *= genome.serpentineBodyFlattening;
            Color ringColor = PatternColor(genome, node.longitudinalPosition, ring);
            BoneWeight boneWeight = SingleBoneWeight(rig.spineIndices[ring]);
            for (int segment = 0; segment < RingSegments; segment++)
            {
                float angle = segment / (float)RingSegments * Mathf.PI * 2f;
                vertices.Add(center + axisX * (Mathf.Cos(angle) * width) + axisY * (Mathf.Sin(angle) * height));
                colors.Add(segment > RingSegments / 2 ? genome.designLanguage.bellyColor : ringColor);
                weights.Add(boneWeight);
            }
        }
        for (int ring = 0; ring < rig.spineBones.Length - 1; ring++)
        {
            for (int segment = 0; segment < RingSegments; segment++)
            {
                int next = (segment + 1) % RingSegments;
                int a = first + ring * RingSegments + segment;
                int b = first + ring * RingSegments + next;
                int c = first + (ring + 1) * RingSegments + next;
                int d = first + (ring + 1) * RingSegments + segment;
                AddQuad(triangles, a, b, c, d);
            }
        }
    }

    static void AppendNodeGeometry(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureRig rig,
        Transform root,
        int nodeIndex)
    {
        CreatureBodyNode node = rig.graph.nodes[nodeIndex];
        Transform bone = rig.nodeBones[nodeIndex];
        Vector3 center = root.InverseTransformPoint(bone.position);
        int boneIndex = rig.nodeBoneIndices[nodeIndex];
        Color primary = PatternColor(genome, node.longitudinalPosition, nodeIndex);
        switch (node.type)
        {
            case CreatureBodyNodeType.Spine:
            case CreatureBodyNodeType.Torso:
                return;
            case CreatureBodyNodeType.Head:
                AppendHead(vertices, triangles, colors, weights, genome, node, center, boneIndex);
                AppendConnectionToParent(vertices, triangles, colors, weights, rig, root, nodeIndex,
                    node.radius, node.radius * 0.92f, primary);
                return;
            case CreatureBodyNodeType.UpperLeg:
                AppendToChild(vertices, triangles, colors, weights, rig, root, nodeIndex,
                    CreatureBodyNodeType.LowerLeg, node.radius, node.radius * 0.82f, primary);
                return;
            case CreatureBodyNodeType.LowerLeg:
                AppendToChild(vertices, triangles, colors, weights, rig, root, nodeIndex,
                    CreatureBodyNodeType.Foot, node.radius, node.radius * 0.65f, genome.secondaryColor);
                return;
            case CreatureBodyNodeType.Foot:
            case CreatureBodyNodeType.Hand:
                AppendEllipsoid(vertices, triangles, colors, weights, center, node.size,
                    genome.secondaryColor, boneIndex);
                return;
            case CreatureBodyNodeType.UpperArm:
                AppendToChild(vertices, triangles, colors, weights, rig, root, nodeIndex,
                    CreatureBodyNodeType.LowerArm, node.radius, node.radius * 0.8f, primary);
                return;
            case CreatureBodyNodeType.LowerArm:
                AppendToChild(vertices, triangles, colors, weights, rig, root, nodeIndex,
                    CreatureBodyNodeType.Hand, node.radius, node.radius * 0.62f, genome.secondaryColor);
                return;
            case CreatureBodyNodeType.Tail:
            case CreatureBodyNodeType.Tentacle:
                AppendConnectionToParent(vertices, triangles, colors, weights, rig, root, nodeIndex,
                    Mathf.Max(node.radius * 1.35f, 0.05f), Mathf.Max(node.radius, 0.025f),
                    node.type == CreatureBodyNodeType.Tail ? genome.secondaryColor : primary);
                return;
            case CreatureBodyNodeType.Horn:
                AppendConnectionToParent(vertices, triangles, colors, weights, rig, root, nodeIndex,
                    Mathf.Max(0.08f, node.radius), 0.018f, genome.designLanguage.ornamentColor);
                return;
            case CreatureBodyNodeType.BackPlate:
                AppendEllipsoid(vertices, triangles, colors, weights, center, node.size,
                    genome.designLanguage.ornamentColor, boneIndex);
                return;
            case CreatureBodyNodeType.Sensor:
                AppendEllipsoid(vertices, triangles, colors, weights, center, node.size,
                    Color.white, boneIndex);
                return;
        }
    }

    static void AppendHead(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureBodyNode node,
        Vector3 center,
        int boneIndex)
    {
        Vector3 size = node.size;
        if (genome.topology == CreatureTopology.Serpentine)
            size = new Vector3(size.x * genome.serpentineHeadWidth, size.y * 0.62f, size.z);
        AppendEllipsoid(vertices, triangles, colors, weights, center, size,
            genome.secondaryColor, boneIndex);
        AppendEyes(vertices, triangles, colors, weights, genome, node, center, boneIndex);
    }

    static void AppendEyes(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureBodyNode node,
        Vector3 center,
        int boneIndex)
    {
        Vector3 size = node.size;
        if (genome.topology == CreatureTopology.Serpentine)
            size = new Vector3(size.x * genome.serpentineHeadWidth, size.y * 0.62f, size.z);
        float eyeSize = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.16f, 0.07f, 0.28f);
        float eyeX = size.x * 0.47f;
        float eyeY = size.y * 0.12f;
        float eyeZ = size.z * 0.22f;
        AppendEllipsoid(vertices, triangles, colors, weights,
            center + new Vector3(-eyeX, eyeY, eyeZ), Vector3.one * eyeSize, Color.white, boneIndex);
        AppendEllipsoid(vertices, triangles, colors, weights,
            center + new Vector3(eyeX, eyeY, eyeZ), Vector3.one * eyeSize, Color.white, boneIndex);
        if (genome.eyeCount >= 4)
        {
            AppendEllipsoid(vertices, triangles, colors, weights,
                center + new Vector3(-eyeX * 0.72f, eyeY + eyeSize * 1.5f, 0f),
                Vector3.one * eyeSize * 0.72f, genome.primaryColor, boneIndex);
            AppendEllipsoid(vertices, triangles, colors, weights,
                center + new Vector3(eyeX * 0.72f, eyeY + eyeSize * 1.5f, 0f),
                Vector3.one * eyeSize * 0.72f, genome.primaryColor, boneIndex);
        }
    }

    static void AppendAnatomicalEyes(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureBodyNode node,
        Vector3 center,
        int boneIndex,
        Vector3 headRight,
        Vector3 headUp,
        Vector3 headForward)
    {
        Vector3 size = node.size;
        float eyeSize = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.18f, 0.065f, 0.24f);
        float eyeX = size.x * 0.48f;
        float eyeY = size.y * 0.12f;
        float eyeZ = size.z * 0.2f;
        Color sclera = new Color(0.095f, 0.07f, 0.05f, 1f);
        Color iris = new Color(0.3f, 0.17f, 0.065f, 1f);
        Color pupil = new Color(0.008f, 0.006f, 0.004f, 1f);

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 outward = headRight * side;
            Vector3 eyeCenter = center + outward * (eyeX - eyeSize * 0.2f)
                + headUp * eyeY + headForward * eyeZ;
            Vector3 tangent = headForward;

            AppendOrientedEllipsoid(vertices, triangles, colors, weights,
                eyeCenter,
                new Vector3(eyeSize * 1.22f, eyeSize * 0.82f, eyeSize * 0.32f),
                tangent, headUp, outward, sclera, boneIndex);

            Vector3 irisCenter = eyeCenter + outward * eyeSize * 0.18f;
            AppendOrientedEllipsoid(vertices, triangles, colors, weights,
                irisCenter,
                new Vector3(eyeSize * 0.7f, eyeSize * 0.62f, eyeSize * 0.1f),
                tangent, headUp, outward, iris, boneIndex);

            Vector3 pupilCenter = eyeCenter + outward * eyeSize * 0.235f;
            AppendOrientedEllipsoid(vertices, triangles, colors, weights,
                pupilCenter,
                new Vector3(eyeSize * 0.46f, eyeSize * 0.13f, eyeSize * 0.07f),
                tangent, headUp, outward, pupil, boneIndex);

            Vector3 highlightCenter = eyeCenter + outward * eyeSize * 0.26f
                + headUp * eyeSize * 0.16f - tangent * eyeSize * 0.12f;
            AppendOrientedEllipsoid(vertices, triangles, colors, weights,
                highlightCenter, Vector3.one * eyeSize * 0.11f,
                tangent, headUp, outward, new Color(0.92f, 0.88f, 0.76f, 1f), boneIndex);
        }
    }

    static void AppendAnatomicalEars(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureBodyNode node,
        Vector3 center,
        int boneIndex,
        Vector3 headRight,
        Vector3 headUp,
        Vector3 headForward)
    {
        Color outer = new Color(0.48f, 0.31f, 0.18f, 1f);
        Color inner = new Color(0.68f, 0.42f, 0.36f, 1f);
        float width = node.size.x;
        float height = node.size.y;
        float length = node.size.z;
        CreatureV5EditableParameters parameters = genome.v5Parameters;
        float earLength = height * (parameters != null ? parameters.earLengthRatio : 0.92f);
        float outwardAngle = parameters != null ? parameters.earOutwardAngle : 28f;
        float earWidth = earLength * 0.42f;
        float thickness = Mathf.Max(0.018f, width * 0.035f);
        BoneWeight weight = SingleBoneWeight(boneIndex);

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 lateral = headRight * side;
            Vector3 baseCenter = center + lateral * width * 0.34f
                + headUp * height * 0.32f - headForward * length * 0.06f;
            float radians = outwardAngle * Mathf.Deg2Rad;
            Vector3 earDirection = (headUp * Mathf.Cos(radians)
                + lateral * Mathf.Sin(radians) - headForward * 0.08f).normalized;
            Vector3 leafWidthAxis = Vector3.ProjectOnPlane(headForward, earDirection).normalized;
            Vector3 leafNormal = Vector3.Cross(earDirection, leafWidthAxis).normalized;
            if (Vector3.Dot(leafNormal, lateral) < 0f) leafNormal = -leafNormal;
            float[] positions = { 0f, 0.16f, 0.52f, 0.8f, 1f };
            float[] widths = { 0.22f, 0.42f, 0.5f, 0.32f, 0.045f };
            int[] previous = null;
            const int ringSegments = 6;
            for (int ring = 0; ring < positions.Length; ring++)
            {
                Vector3 ringCenter = baseCenter + earDirection * earLength * positions[ring];
                var current = new int[ringSegments];
                for (int segment = 0; segment < ringSegments; segment++)
                {
                    float angle = segment / (float)ringSegments * Mathf.PI * 2f;
                    float leafHalfWidth = earWidth * widths[ring];
                    current[segment] = vertices.Count;
                    vertices.Add(ringCenter
                        + leafWidthAxis * Mathf.Cos(angle) * leafHalfWidth
                        + leafNormal * Mathf.Sin(angle) * thickness);
                    colors.Add(Mathf.Sin(angle) * side > 0.05f ? inner : outer);
                    weights.Add(weight);
                }
                if (previous != null)
                {
                    for (int segment = 0; segment < ringSegments; segment++)
                    {
                        int next = (segment + 1) % ringSegments;
                        AddQuad(triangles, previous[segment], previous[next],
                            current[next], current[segment]);
                    }
                }
                else
                {
                    int cap = vertices.Count;
                    vertices.Add(baseCenter - earDirection * thickness);
                    colors.Add(outer);
                    weights.Add(weight);
                    for (int segment = 0; segment < ringSegments; segment++)
                    {
                        int next = (segment + 1) % ringSegments;
                        triangles.Add(cap); triangles.Add(current[next]); triangles.Add(current[segment]);
                    }
                }
                previous = current;
            }
            int tip = vertices.Count;
            vertices.Add(baseCenter + earDirection * earLength);
            colors.Add(outer);
            weights.Add(weight);
            for (int segment = 0; segment < ringSegments; segment++)
            {
                int next = (segment + 1) % ringSegments;
                triangles.Add(tip); triangles.Add(previous[segment]); triangles.Add(previous[next]);
            }
        }
    }

    static void AppendAnatomicalNose(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureBodyNode node,
        Vector3 center,
        int boneIndex,
        Vector3 headRight,
        Vector3 headUp,
        Vector3 headForward)
    {
        Vector3 noseCenter = center + headForward * node.size.z * 0.82f
            - headUp * node.size.y * 0.07f;
        AppendOrientedEllipsoid(vertices, triangles, colors, weights,
            noseCenter,
            new Vector3(node.size.x * 0.4f, node.size.y * 0.21f, node.size.z * 0.075f),
            headRight, headUp, headForward,
            new Color(0.075f, 0.06f, 0.05f, 1f), boneIndex);
    }

    static void AppendAnatomicalAntlers(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureGenome genome,
        CreatureBodyNode node,
        Vector3 center,
        int boneIndex,
        Vector3 headRight,
        Vector3 headUp,
        Vector3 headForward)
    {
        float antlerLength = Mathf.Clamp(genome.hornLength, 0f, node.size.y * 1.15f);
        if (antlerLength < 0.12f) return;
        Color antler = new Color(0.16f, 0.105f, 0.065f, 1f);
        float radius = Mathf.Clamp(node.size.x * 0.055f, 0.025f, 0.075f);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 lateral = headRight * side;
            Vector3 root = center + lateral * node.size.x * 0.24f
                + headUp * node.size.y * 0.42f - headForward * node.size.z * 0.12f;
            Vector3 beam = root + headUp * antlerLength * 0.7f
                - headForward * antlerLength * 0.28f + lateral * antlerLength * 0.12f;
            AppendTube(vertices, triangles, colors, weights, root, beam,
                radius, radius * 0.48f, antler, boneIndex, boneIndex);
            Vector3 tineRoot = Vector3.Lerp(root, beam, 0.52f);
            Vector3 tine = tineRoot + headUp * antlerLength * 0.38f
                + headForward * antlerLength * 0.16f + lateral * antlerLength * 0.08f;
            AppendTube(vertices, triangles, colors, weights, tineRoot, tine,
                radius * 0.55f, radius * 0.18f, antler, boneIndex, boneIndex);
        }
    }

    static void AppendToChild(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureRig rig,
        Transform root,
        int nodeIndex,
        CreatureBodyNodeType childType,
        float startRadius,
        float endRadius,
        Color color)
    {
        int child = FindChild(rig.graph, nodeIndex, childType);
        if (child < 0)
            return;
        Vector3 start = root.InverseTransformPoint(rig.nodeBones[nodeIndex].position);
        Vector3 end = root.InverseTransformPoint(rig.nodeBones[child].position);
        AppendTube(vertices, triangles, colors, weights, start, end, startRadius, endRadius,
            color, rig.nodeBoneIndices[nodeIndex], rig.nodeBoneIndices[child]);
    }

    static void AppendConnectionToParent(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        CreatureRig rig,
        Transform root,
        int nodeIndex,
        float startRadius,
        float endRadius,
        Color color)
    {
        int parent = rig.graph.nodes[nodeIndex].parentIndex;
        if (parent < 0)
            return;
        Vector3 start = root.InverseTransformPoint(rig.nodeBones[parent].position);
        Vector3 end = root.InverseTransformPoint(rig.nodeBones[nodeIndex].position);
        AppendTube(vertices, triangles, colors, weights, start, end, startRadius, endRadius,
            color, rig.nodeBoneIndices[parent], rig.nodeBoneIndices[nodeIndex]);
    }

    static CreatureBodyNode FindNode(CreatureRig rig, Transform bone)
    {
        for (int i = 0; i < rig.nodeBones.Length; i++)
            if (rig.nodeBones[i] == bone) return rig.graph.nodes[i];
        return rig.graph.nodes[0];
    }

    static int FindChild(CreatureBodyGraph graph, int parent, CreatureBodyNodeType type)
    {
        for (int i = parent + 1; i < graph.nodes.Count; i++)
            if (graph.nodes[i].parentIndex == parent && graph.nodes[i].type == type) return i;
        return -1;
    }

    static Vector3 GetSpineTangent(CreatureRig rig, Transform root, int index)
    {
        Vector3 current = root.InverseTransformPoint(rig.spineBones[index].position);
        Vector3 tangent;
        if (index == 0 && rig.spineBones.Length > 1)
            tangent = root.InverseTransformPoint(rig.spineBones[1].position) - current;
        else if (index == rig.spineBones.Length - 1)
            tangent = current - root.InverseTransformPoint(rig.spineBones[index - 1].position);
        else
            tangent = root.InverseTransformPoint(rig.spineBones[index + 1].position)
                - root.InverseTransformPoint(rig.spineBones[index - 1].position);
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
    }

    static Color PatternColor(CreatureGenome genome, float longitudinal, int index)
    {
        float pattern = Mathf.Sin((longitudinal * genome.designLanguage.patternFrequency + index * 0.17f)
            * Mathf.PI * 2f) * 0.5f + 0.5f;
        pattern = Mathf.SmoothStep(0.15f, 0.85f, pattern);
        return Color.Lerp(genome.designLanguage.primaryColor, genome.designLanguage.secondaryColor, pattern * 0.68f);
    }

    static Color PatternColorV4(CreatureGenome genome, Vector3 position, Bounds bounds)
    {
        float longitudinal = Mathf.InverseLerp(bounds.min.z, bounds.max.z, position.z);
        float lateral = (position.x - bounds.center.x) / Mathf.Max(0.001f, bounds.extents.x);
        float vertical = (position.y - bounds.center.y) / Mathf.Max(0.001f, bounds.extents.y);
        float frequency = genome.designLanguage.patternFrequency;
        float broadBand = Mathf.Sin((longitudinal * frequency + lateral * 0.1f) * Mathf.PI * 2f)
            * 0.5f + 0.5f;
        float coherentDetail = Mathf.Sin(
            Vector3.Dot(position, new Vector3(1.17f, 0.63f, 0.91f)) * 1.35f
            + genome.seed * 0.013f) * 0.5f + 0.5f;
        float pattern = Mathf.SmoothStep(0.28f, 0.72f, broadBand * 0.82f + coherentDetail * 0.18f);

        Color primary = NaturalizeSkinColor(genome.designLanguage.primaryColor);
        Color accent = Color.Lerp(primary, NaturalizeSkinColor(genome.designLanguage.secondaryColor), 0.28f);
        Color bodyColor = Color.Lerp(primary, accent, pattern * 0.34f);
        float dorsalMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.78f, vertical));
        Color dorsal = new Color(primary.r * 0.74f, primary.g * 0.74f, primary.b * 0.76f, primary.a);
        bodyColor = Color.Lerp(bodyColor, dorsal, dorsalMask * 0.42f);
        float bellyMask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, -0.72f, vertical));
        Color belly = Color.Lerp(primary, Color.white, 0.1f);
        return Color.Lerp(bodyColor, belly, bellyMask * 0.28f);
    }

    static Color NaturalizeSkinColor(Color source)
    {
        Color.RGBToHSV(source, out float hue, out float saturation, out float value);
        saturation = Mathf.Clamp(saturation, 0.2f, 0.42f);
        value = Mathf.Clamp(value, 0.34f, 0.56f);
        Color result = Color.HSVToRGB(hue, saturation, value);
        result.a = source.a;
        return result;
    }

    static void AppendEllipsoid(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        Vector3 center,
        Vector3 size,
        Color color,
        int boneIndex)
    {
        const int resolution = 4;
        Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        BoneWeight weight = SingleBoneWeight(boneIndex);
        foreach (Vector3 normal in normals)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 axisU = Vector3.Cross(reference, normal).normalized;
            Vector3 axisV = Vector3.Cross(normal, axisU).normalized;
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float u0 = x / (float)resolution * 2f - 1f;
                float u1 = (x + 1) / (float)resolution * 2f - 1f;
                float v0 = y / (float)resolution * 2f - 1f;
                float v1 = (y + 1) / (float)resolution * 2f - 1f;
                int start = vertices.Count;
                AddEllipsoidVertex(vertices, colors, weights, center, size, normal + axisU * u0 + axisV * v0, color, weight);
                AddEllipsoidVertex(vertices, colors, weights, center, size, normal + axisU * u1 + axisV * v0, color, weight);
                AddEllipsoidVertex(vertices, colors, weights, center, size, normal + axisU * u1 + axisV * v1, color, weight);
                AddEllipsoidVertex(vertices, colors, weights, center, size, normal + axisU * u0 + axisV * v1, color, weight);
                AddQuad(triangles, start, start + 1, start + 2, start + 3);
            }
        }
    }

    static void AppendOrientedEllipsoid(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        Vector3 center,
        Vector3 size,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        Color color,
        int boneIndex)
    {
        const int resolution = 4;
        Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        BoneWeight weight = SingleBoneWeight(boneIndex);
        foreach (Vector3 normal in normals)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 axisU = Vector3.Cross(reference, normal).normalized;
            Vector3 axisV = Vector3.Cross(normal, axisU).normalized;
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float u0 = x / (float)resolution * 2f - 1f;
                float u1 = (x + 1) / (float)resolution * 2f - 1f;
                float v0 = y / (float)resolution * 2f - 1f;
                float v1 = (y + 1) / (float)resolution * 2f - 1f;
                int start = vertices.Count;
                AddOrientedEllipsoidVertex(vertices, colors, weights, center, size,
                    normal + axisU * u0 + axisV * v0, axisX, axisY, axisZ, color, weight);
                AddOrientedEllipsoidVertex(vertices, colors, weights, center, size,
                    normal + axisU * u1 + axisV * v0, axisX, axisY, axisZ, color, weight);
                AddOrientedEllipsoidVertex(vertices, colors, weights, center, size,
                    normal + axisU * u1 + axisV * v1, axisX, axisY, axisZ, color, weight);
                AddOrientedEllipsoidVertex(vertices, colors, weights, center, size,
                    normal + axisU * u0 + axisV * v1, axisX, axisY, axisZ, color, weight);
                AddQuad(triangles, start, start + 1, start + 2, start + 3);
            }
        }
    }

    static void AddOrientedEllipsoidVertex(
        ICollection<Vector3> vertices,
        ICollection<Color> colors,
        ICollection<BoneWeight> weights,
        Vector3 center,
        Vector3 size,
        Vector3 cubePoint,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        Color color,
        BoneWeight weight)
    {
        Vector3 local = Vector3.Scale(cubePoint.normalized * 0.5f, size);
        vertices.Add(center + axisX * local.x + axisY * local.y + axisZ * local.z);
        colors.Add(color);
        weights.Add(weight);
    }

    static void AddEllipsoidVertex(
        ICollection<Vector3> vertices,
        ICollection<Color> colors,
        ICollection<BoneWeight> weights,
        Vector3 center,
        Vector3 size,
        Vector3 cubePoint,
        Color color,
        BoneWeight weight)
    {
        vertices.Add(center + Vector3.Scale(cubePoint.normalized * 0.5f, size));
        colors.Add(color);
        weights.Add(weight);
    }

    static void AppendTube(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        List<BoneWeight> weights,
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        Color color,
        int startBone,
        int endBone)
    {
        const int segments = 8;
        const int rings = 5;
        Vector3 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
        Vector3 axisU = Vector3.Cross(reference, direction).normalized;
        Vector3 axisV = Vector3.Cross(direction, axisU).normalized;
        int first = vertices.Count;
        for (int ring = 0; ring < rings; ring++)
        {
            float t = ring / (float)(rings - 1);
            Vector3 center = Vector3.Lerp(start, end, t);
            float radius = Mathf.Lerp(startRadius, endRadius, t);
            BoneWeight weight = BlendWeight(startBone, endBone, t);
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment / (float)segments * Mathf.PI * 2f;
                vertices.Add(center + (axisU * Mathf.Cos(angle) + axisV * Mathf.Sin(angle)) * radius);
                colors.Add(color);
                weights.Add(weight);
            }
        }
        for (int ring = 0; ring < rings - 1; ring++)
        for (int segment = 0; segment < segments; segment++)
        {
            int next = (segment + 1) % segments;
            int a = first + ring * segments + segment;
            int b = first + ring * segments + next;
            int c = first + (ring + 1) * segments + next;
            int d = first + (ring + 1) * segments + segment;
            AddQuad(triangles, a, b, c, d);
        }
    }

    static BoneWeight SingleBoneWeight(int boneIndex)
    {
        return new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f };
    }

    static BoneWeight BlendWeight(int startBone, int endBone, float t)
    {
        if (startBone == endBone) return SingleBoneWeight(startBone);
        return new BoneWeight
        {
            boneIndex0 = startBone,
            boneIndex1 = endBone,
            weight0 = 1f - t,
            weight1 = t
        };
    }

    static void AddQuad(ICollection<int> triangles, int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(a); triangles.Add(c); triangles.Add(d);
    }
}
