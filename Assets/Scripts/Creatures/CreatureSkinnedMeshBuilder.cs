using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class CreatureSkinnedMeshBuilder
{
    const int RingSegments = 10;

    public static Mesh Build(CreatureGenome genome, CreatureRig rig, Transform creatureRoot)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(35);}
    try
    {
        CreatureImplicitMeshData implicitBody = CreatureImplicitBodyMesher.Build(
            genome.torsoSpline, CreatureBodyMeshQuality.Final);
        return Build(genome, rig, creatureRoot, implicitBody);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

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

        var mesh = new Mesh
        {
            name = $"CreatureGraphSkin_{genome.seed}",
            indexFormat = IndexFormat.UInt32
        };
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
