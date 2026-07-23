using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

public struct CreatureV5LogicalWeight
{
    public int node0;
    public int node1;
    public int node2;
    public int node3;
    public float weight0;
    public float weight1;
    public float weight2;
    public float weight3;

    public static CreatureV5LogicalWeight Single(int node)
    {
        return new CreatureV5LogicalWeight { node0 = node, node1 = -1, node2 = -1, node3 = -1, weight0 = 1f };
    }

    public static CreatureV5LogicalWeight Two(int first, int second, float secondWeight)
    {
        secondWeight = Mathf.Clamp01(secondWeight);
        return new CreatureV5LogicalWeight
        {
            node0 = first,
            node1 = second,
            node2 = -1,
            node3 = -1,
            weight0 = 1f - secondWeight,
            weight1 = secondWeight
        };
    }

    public static CreatureV5LogicalWeight Blend(
        CreatureV5LogicalWeight a,
        CreatureV5LogicalWeight b,
        float t)
    {
        t = Mathf.Clamp01(t);
        int[] nodes = { -1, -1, -1, -1, -1, -1, -1, -1 };
        float[] weights = new float[8];
        Add(nodes, weights, a.node0, a.weight0 * (1f - t));
        Add(nodes, weights, a.node1, a.weight1 * (1f - t));
        Add(nodes, weights, a.node2, a.weight2 * (1f - t));
        Add(nodes, weights, a.node3, a.weight3 * (1f - t));
        Add(nodes, weights, b.node0, b.weight0 * t);
        Add(nodes, weights, b.node1, b.weight1 * t);
        Add(nodes, weights, b.node2, b.weight2 * t);
        Add(nodes, weights, b.node3, b.weight3 * t);

        for (int i = 0; i < nodes.Length - 1; i++)
        {
            for (int j = i + 1; j < nodes.Length; j++)
            {
                if (weights[j] <= weights[i]) continue;
                float w = weights[i]; weights[i] = weights[j]; weights[j] = w;
                int n = nodes[i]; nodes[i] = nodes[j]; nodes[j] = n;
            }
        }

        float sum = weights[0] + weights[1] + weights[2] + weights[3];
        if (sum <= 0.000001f) return Single(0);
        return new CreatureV5LogicalWeight
        {
            node0 = nodes[0], node1 = nodes[1], node2 = nodes[2], node3 = nodes[3],
            weight0 = weights[0] / sum, weight1 = weights[1] / sum,
            weight2 = weights[2] / sum, weight3 = weights[3] / sum
        };
    }

    static void Add(int[] nodes, float[] weights, int node, float value)
    {
        if (node < 0 || value <= 0f) return;
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] == node)
            {
                weights[i] += value;
                return;
            }
            if (nodes[i] >= 0) continue;
            nodes[i] = node;
            weights[i] = value;
            return;
        }
    }
}

public sealed class CreatureV5MeshData
{
    public int revision;
    public int shapeHash;
    public CreatureBodyMeshQuality quality;
    public Vector3[] vertices;
    public Vector3[] normals;
    public Vector2[] uv;
    public Color[] colors;
    public int[] triangles;
    public CreatureV5LogicalWeight[] logicalWeights;
    public CreatureV5SurfaceRegion[] regions;
    public byte[] smoothingGroups;
    public Bounds bounds;
    public int connectedShellCount;
    public CreatureV5MeshDiagnostics diagnostics;
    public string validationError;

    public bool Validate(out string error)
    {
        if (vertices == null || normals == null || uv == null || colors == null
            || triangles == null || logicalWeights == null || regions == null
            || smoothingGroups == null)
        {
            error = "V5 mesh arrays are missing.";
            return false;
        }
        int count = vertices.Length;
        if (count < 100 || normals.Length != count || uv.Length != count
            || colors.Length != count || logicalWeights.Length != count
            || regions.Length != count || smoothingGroups.Length != count)
        {
            error = "V5 mesh array lengths do not match.";
            return false;
        }
        if (triangles.Length == 0 || triangles.Length % 3 != 0)
        {
            error = "V5 triangle array is invalid.";
            return false;
        }
        for (int i = 0; i < count; i++)
        {
            if (!Finite(vertices[i]) || !Finite(normals[i]) || !Finite(uv[i]))
            {
                error = $"V5 vertex {i} is not finite.";
                return false;
            }
            float sum = logicalWeights[i].weight0 + logicalWeights[i].weight1
                + logicalWeights[i].weight2 + logicalWeights[i].weight3;
            if (Mathf.Abs(sum - 1f) > 0.001f)
            {
                error = $"V5 vertex {i} weights sum to {sum}.";
                return false;
            }
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count)
            {
                error = $"V5 triangle {i / 3} has an invalid index.";
                return false;
            }
            if (a == b || b == c || c == a
                || Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < 0.0000000001f)
            {
                error = $"V5 triangle {i / 3} is degenerate.";
                return false;
            }
        }
        if (!diagnostics.IsClosedManifold || diagnostics.invalidNormalCount != 0)
        {
            error = $"V5 topology invalid: boundary={diagnostics.boundaryEdgeCount}, "
                + $"nonManifold={diagnostics.nonManifoldEdgeCount}, "
                + $"winding={diagnostics.inconsistentWindingEdgeCount}, "
                + $"degenerate={diagnostics.degenerateTriangleCount}, "
                + $"normals={diagnostics.invalidNormalCount}.";
            return false;
        }
        error = null;
        return true;
    }

    static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
    static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}

public struct CreatureV5MeshDiagnostics
{
    public int connectedShellCount;
    public int boundaryEdgeCount;
    public int nonManifoldEdgeCount;
    public int inconsistentWindingEdgeCount;
    public int degenerateTriangleCount;
    public int invalidNormalCount;
    public float signedVolume;

    public bool IsClosedManifold => boundaryEdgeCount == 0
        && nonManifoldEdgeCount == 0 && inconsistentWindingEdgeCount == 0
        && degenerateTriangleCount == 0;
}

public static class CreatureV5MeshGenerator
{
    const int MaximumVertexCount = 26000;
    static readonly object CacheLock = new object();
    static readonly Dictionary<long, CreatureV5MeshData> Cache = new Dictionary<long, CreatureV5MeshData>();

    struct Frame
    {
        public Vector3 center;
        public Vector3 tangent;
        public Vector3 right;
        public Vector3 up;
    }

    struct SocketPatch
    {
        public int ringStart;
        public int segmentStart;
        public int ringSpan;
        public int segmentSpan;
        public CreaturePhenotypeLeg leg;
        public int legIndex;
    }

    struct LegRingSpec
    {
        public Vector3 center;
        public float radiusX;
        public float radiusZ;
        public CreatureV5LogicalWeight weight;
        public CreatureV5SurfaceRegion region;
        public byte smoothingGroup;
        public bool smoothable;
    }

    struct HeadRingSpec
    {
        public Vector3 center;
        public float halfWidth;
        public float halfHeight;
        public int node;
        public CreatureV5LogicalWeight weight;
        public CreatureV5SurfaceRegion region;
        public byte smoothingGroup;
    }

    sealed class SurfaceBuilder
    {
        public readonly List<Vector3> vertices = new List<Vector3>(1800);
        public readonly List<Vector2> uv = new List<Vector2>(1800);
        public readonly List<Color> colors = new List<Color>(1800);
        public readonly List<CreatureV5LogicalWeight> weights = new List<CreatureV5LogicalWeight>(1800);
        public readonly List<bool> smoothable = new List<bool>(1800);
        public readonly List<CreatureV5SurfaceRegion> regions = new List<CreatureV5SurfaceRegion>(1800);
        public readonly List<byte> smoothingGroups = new List<byte>(1800);
        public readonly List<int> triangles = new List<int>(8000);

        public int Add(
            Vector3 position,
            Vector2 texture,
            Color color,
            CreatureV5LogicalWeight weight,
            bool allowSmoothing = true,
            CreatureV5SurfaceRegion region = CreatureV5SurfaceRegion.Torso,
            byte smoothingGroup = 1)
        {
            int index = vertices.Count;
            vertices.Add(position);
            uv.Add(texture);
            colors.Add(color);
            weights.Add(weight);
            smoothable.Add(allowSmoothing);
            regions.Add(region);
            smoothingGroups.Add(smoothingGroup);
            return index;
        }

        public void Triangle(int a, int b, int c)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }

        public void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }
    }

    public static Task<CreatureV5MeshData> BuildAsync(
        CreatureV5Phenotype phenotype,
        CreatureBodyMeshQuality quality,
        int revision,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Build(phenotype, quality, revision, cancellationToken), cancellationToken);
    }

    public static CreatureV5MeshData Build(
        CreatureV5Phenotype phenotype,
        CreatureBodyMeshQuality quality,
        int revision = 0,
        CancellationToken cancellationToken = default)
    {
        if (phenotype == null) throw new ArgumentNullException(nameof(phenotype));
        long key = ((long)phenotype.shapeHash << 32)
            ^ ((long)CreatureV5MeshSchema.Current << 8) ^ (uint)quality;
        if (revision == 0)
        {
            lock (CacheLock)
                if (Cache.TryGetValue(key, out CreatureV5MeshData cached)) return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SurfaceBuilder surface = BuildBase(phenotype, cancellationToken);
        EnsureOutwardWinding(surface);
        TaubinSmooth(surface, 2, 0.12f, -0.125f);
        int subdivisions = quality == CreatureBodyMeshQuality.Final ? 2
            : quality == CreatureBodyMeshQuality.Lod1 ? 1 : 0;
        for (int i = 0; i < subdivisions; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            surface = Subdivide(surface);
        }

        if (surface.vertices.Count > MaximumVertexCount)
            throw new InvalidOperationException($"V5 vertex budget exceeded: {surface.vertices.Count}.");

        Vector3[] normals = CalculateNormals(surface);
        Bounds bounds = CalculateBounds(surface.vertices);
        CreatureV5MeshDiagnostics diagnostics = Diagnose(surface, normals);
        var result = new CreatureV5MeshData
        {
            revision = revision,
            shapeHash = phenotype.shapeHash,
            quality = quality,
            vertices = surface.vertices.ToArray(),
            normals = normals,
            uv = surface.uv.ToArray(),
            colors = surface.colors.ToArray(),
            triangles = surface.triangles.ToArray(),
            logicalWeights = surface.weights.ToArray(),
            regions = surface.regions.ToArray(),
            smoothingGroups = surface.smoothingGroups.ToArray(),
            bounds = bounds,
            connectedShellCount = diagnostics.connectedShellCount,
            diagnostics = diagnostics
        };
        if (!result.Validate(out string error))
        {
            result.validationError = error;
            throw new InvalidOperationException(error);
        }
        if (revision == 0)
            lock (CacheLock) Cache[key] = result;
        return result;
    }

    static SurfaceBuilder BuildBase(CreatureV5Phenotype phenotype, CancellationToken token)
    {
        var result = new SurfaceBuilder();
        CreatureAnatomyProfile profile = phenotype.anatomy;
        int ringCount = profile.torsoRingCount;
        int segments = profile.torsoRingSegments;
        var centers = new Vector3[ringCount];
        for (int i = 0; i < ringCount; i++)
        {
            float t = i / (float)(ringCount - 1);
            CreatureV5TorsoSection section = CreatureV5PhenotypeBuilder.SampleTorsoSection(
                phenotype.torsoSections, t);
            centers[i] = new Vector3(0f, section.centerY,
                Mathf.Lerp(-phenotype.parameters.bodyLength * 0.5f,
                    phenotype.parameters.bodyLength * 0.5f, t));
        }
        Frame[] frames = BuildFrames(centers, Vector3.up);
        var torso = new int[ringCount, segments];

        for (int ring = 0; ring < ringCount; ring++)
        {
            token.ThrowIfCancellationRequested();
            float t = ring / (float)(ringCount - 1);
            CreatureV5TorsoSection section = CreatureV5PhenotypeBuilder.SampleTorsoSection(
                phenotype.torsoSections, t);
            float rx = section.width * 0.5f;
            CreatureV5LogicalWeight weight = SpineWeight(phenotype, t);
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment / (float)segments * Mathf.PI * 2f;
                Vector2 radial = Superellipse(angle, section.exponent);
                float ry = radial.y >= 0f ? section.topRadius : section.bottomRadius;
                Vector3 position = frames[ring].center
                    + frames[ring].right * radial.x * rx
                    + frames[ring].up * radial.y * ry;
                torso[ring, segment] = result.Add(position,
                    new Vector2(segment / (float)segments, t),
                    BodyColor(phenotype, t, radial.y), weight, true,
                    CreatureV5SurfaceRegion.Torso, 1);
            }
        }

        SocketPatch[] legPatches = BuildSocketPatches(phenotype, ringCount, segments);
        SocketPatch neckPatch = BuildSocketPatch(
            phenotype.neckSocket, null, -1, ringCount, segments);
        SocketPatch tailPatch = BuildSocketPatch(
            phenotype.tailSocket, null, -1, ringCount, segments);
        var patches = new SocketPatch[legPatches.Length + 2];
        Array.Copy(legPatches, patches, legPatches.Length);
        patches[patches.Length - 2] = neckPatch;
        patches[patches.Length - 1] = tailPatch;
        for (int ring = 0; ring < ringCount - 1; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                if (InsidePatch(patches, ring, segment, segments)) continue;
                int next = (segment + 1) % segments;
                result.Quad(torso[ring, segment], torso[ring, next],
                    torso[ring + 1, next], torso[ring + 1, segment]);
            }
        }

        for (int i = 0; i < legPatches.Length; i++)
        {
            SocketPatch patch = legPatches[i];
            int[] boundary = GetPatchBoundary(torso, patch, segments);
            BuildLeg(result, phenotype, patch.leg, phenotype.restLegPoses[patch.legIndex], boundary);
        }

        int[] rearLoop = GetTorsoLoop(torso, 0, segments, false);
        int[] frontLoop = GetTorsoLoop(torso, ringCount - 1, segments, false);
        CapLoop(result, rearLoop, frames[0].center, SpineWeight(phenotype, 0f),
            BodyColor(phenotype, 0f, 0f), true, true,
            CreatureV5SurfaceRegion.Torso, 1);
        CapLoop(result, frontLoop, frames[ringCount - 1].center, SpineWeight(phenotype, 1f),
            BodyColor(phenotype, 1f, 0f), false, true,
            CreatureV5SurfaceRegion.Torso, 1);
        BuildHeadAndNeck(result, phenotype, GetPatchBoundary(torso, neckPatch, segments));
        BuildTail(result, phenotype, GetPatchBoundary(torso, tailPatch, segments));
        BuildHooves(result, phenotype);
        return result;
    }

    static SocketPatch[] BuildSocketPatches(
        CreatureV5Phenotype phenotype,
        int ringCount,
        int segmentCount)
    {
        var patches = new SocketPatch[phenotype.motion.legs.Length];
        for (int i = 0; i < patches.Length; i++)
        {
            CreaturePhenotypeLeg leg = phenotype.motion.legs[i];
            patches[i] = BuildSocketPatch(
                phenotype.legSockets[i], leg, i, ringCount, segmentCount);
        }
        return patches;
    }

    static SocketPatch BuildSocketPatch(
        CreatureV5SocketDescriptor descriptor,
        CreaturePhenotypeLeg leg,
        int legIndex,
        int ringCount,
        int segmentCount)
    {
        int ringStart = Mathf.RoundToInt(
            descriptor.longitudinalPosition * (ringCount - 1) - descriptor.axialSpan * 0.5f);
        int segmentStart = Mathf.FloorToInt(
            descriptor.circumferenceAngleDegrees / 360f * segmentCount
            - descriptor.circumferentialSpan * 0.5f);
        return new SocketPatch
        {
            ringStart = Mathf.Clamp(ringStart, 1, ringCount - descriptor.axialSpan - 2),
            segmentStart = Wrap(segmentStart, segmentCount),
            ringSpan = descriptor.axialSpan,
            segmentSpan = descriptor.circumferentialSpan,
            leg = leg,
            legIndex = legIndex
        };
    }

    static bool InsidePatch(SocketPatch[] patches, int ring, int segment, int segmentCount)
    {
        for (int i = 0; i < patches.Length; i++)
        {
            if (ring < patches[i].ringStart
                || ring >= patches[i].ringStart + patches[i].ringSpan) continue;
            for (int offset = 0; offset < patches[i].segmentSpan; offset++)
                if (segment == (patches[i].segmentStart + offset) % segmentCount) return true;
        }
        return false;
    }

    static int[] GetPatchBoundary(int[,] grid, SocketPatch patch, int segmentCount)
    {
        int u = patch.ringStart;
        int v = patch.segmentStart;
        var boundary = new List<int>((patch.ringSpan + patch.segmentSpan) * 2);
        for (int ring = 0; ring <= patch.ringSpan; ring++)
            boundary.Add(grid[u + ring, Wrap(v, segmentCount)]);
        for (int segment = 1; segment <= patch.segmentSpan; segment++)
            boundary.Add(grid[u + patch.ringSpan, Wrap(v + segment, segmentCount)]);
        for (int ring = patch.ringSpan - 1; ring >= 0; ring--)
            boundary.Add(grid[u + ring, Wrap(v + patch.segmentSpan, segmentCount)]);
        for (int segment = patch.segmentSpan - 1; segment >= 1; segment--)
            boundary.Add(grid[u, Wrap(v + segment, segmentCount)]);
        boundary.Reverse();
        return boundary.ToArray();
    }

    static int[] GetTorsoLoop(int[,] grid, int ring, int segments, bool reverse)
    {
        var result = new int[segments];
        for (int i = 0; i < segments; i++) result[i] = grid[ring, reverse ? segments - 1 - i : i];
        return result;
    }

    static void BuildLeg(
        SurfaceBuilder result,
        CreatureV5Phenotype phenotype,
        CreaturePhenotypeLeg leg,
        CreatureV5RestLegPose restPose,
        int[] boundary)
    {
        Vector3 socket = Average(result.vertices, boundary);
        Vector3 alignment = socket - restPose.hip;
        Vector3 knee = restPose.knee + alignment;
        Vector3 hock = restPose.hock + alignment;
        Vector3 foot = restPose.hoof + alignment;
        int parentNode = phenotype.motion.graph.nodes[leg.upperNode].parentIndex;
        if (parentNode < 0) parentNode = leg.upperNode;
        float thickness = phenotype.parameters.legThickness;
        var specs = new List<LegRingSpec>(18);

        AddLegSegment(specs, socket, knee, thickness,
            new[] { 0.06f, 0.24f, 0.5f, 0.78f, 0.94f },
            new[] { 1.06f, 0.98f, 0.84f, 0.72f, 0.66f },
            parentNode, leg.upperNode, CreatureV5SurfaceRegion.UpperLeg, 3, true);
        AddJointRing(specs, knee, thickness * 0.64f, leg.upperNode, leg.lowerNode, 6);
        AddLegSegment(specs, knee, hock, thickness,
            new[] { 0.08f, 0.3f, 0.58f, 0.86f },
            new[] { 0.62f, 0.56f, 0.49f, 0.44f },
            leg.upperNode, leg.lowerNode, CreatureV5SurfaceRegion.LowerLeg, 4, true);
        AddJointRing(specs, hock, thickness * 0.46f, leg.lowerNode, leg.distalNode, 6);
        AddLegSegment(specs, hock, foot, thickness,
            new[] { 0.1f, 0.35f, 0.65f, 0.88f, 1f },
            new[] { 0.45f, 0.4f, 0.36f, 0.35f, 0.42f },
            leg.lowerNode, leg.distalNode, CreatureV5SurfaceRegion.DistalLeg, 5, true,
            leg.footNode);

        Vector3[] centers = new Vector3[specs.Count];
        for (int i = 0; i < specs.Count; i++) centers[i] = specs[i].center;
        Frame[] frames = BuildFrames(centers, leg.IsLeft ? Vector3.left : Vector3.right);
        int[] previous = boundary;

        for (int ring = 0; ring < specs.Count; ring++)
        {
            LegRingSpec spec = specs[ring];
            var current = new int[phenotype.anatomy.limbRingSegments];
            for (int i = 0; i < current.Length; i++)
            {
                float angle = i / (float)current.Length * Mathf.PI * 2f;
                Vector2 radial = Superellipse(angle,
                    spec.region == CreatureV5SurfaceRegion.DistalLeg ? 2.15f : 2f);
                Vector3 position = spec.center
                    + frames[ring].right * radial.x * spec.radiusX
                    + frames[ring].up * radial.y * spec.radiusZ;
                current[i] = result.Add(position,
                    new Vector2(i / (float)current.Length, ring / (float)(specs.Count - 1)),
                    BodyColor(phenotype, leg.longitudinalPosition, radial.y), spec.weight,
                    spec.smoothable, spec.region, spec.smoothingGroup);
            }
            BridgeLoops(result, previous, current, false);
            previous = current;
        }
        CapLoop(result, previous, foot, CreatureV5LogicalWeight.Single(leg.footNode),
            BodyColor(phenotype, leg.longitudinalPosition, -1f), false, false,
            CreatureV5SurfaceRegion.HoofCrown, 7);
    }

    static void AddLegSegment(
        ICollection<LegRingSpec> specs,
        Vector3 start,
        Vector3 end,
        float thickness,
        float[] positions,
        float[] radii,
        int parentNode,
        int node,
        CreatureV5SurfaceRegion region,
        byte smoothingGroup,
        bool smoothable,
        int endNode = -1)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            float t = positions[i];
            CreatureV5LogicalWeight weight;
            if (i == 0 && parentNode != node)
                weight = CreatureV5LogicalWeight.Two(parentNode, node, 0.75f);
            else if (endNode >= 0 && i >= positions.Length - 2)
                weight = CreatureV5LogicalWeight.Two(node, endNode,
                    i == positions.Length - 1 ? 0.7f : 0.3f);
            else
                weight = CreatureV5LogicalWeight.Single(node);
            float rx = thickness * radii[i];
            specs.Add(new LegRingSpec
            {
                center = Vector3.Lerp(start, end, t),
                radiusX = rx,
                radiusZ = rx * (region == CreatureV5SurfaceRegion.UpperLeg ? 0.9f : 0.78f),
                weight = weight,
                region = region,
                smoothingGroup = smoothingGroup,
                smoothable = smoothable
            });
        }
    }

    static void AddJointRing(
        ICollection<LegRingSpec> specs,
        Vector3 center,
        float radius,
        int parentNode,
        int childNode,
        byte smoothingGroup)
    {
        specs.Add(new LegRingSpec
        {
            center = center,
            radiusX = radius,
            radiusZ = radius * 0.86f,
            weight = CreatureV5LogicalWeight.Two(parentNode, childNode, 0.5f),
            region = CreatureV5SurfaceRegion.Joint,
            smoothingGroup = smoothingGroup,
            smoothable = false
        });
    }

    static void BuildHeadAndNeck(
        SurfaceBuilder result,
        CreatureV5Phenotype phenotype,
        int[] boundary)
    {
        Vector3 socket = Average(result.vertices, boundary);
        Vector3 neck = NodePosition(phenotype, phenotype.neckNode,
            socket + Vector3.up * phenotype.parameters.neckLength * 0.42f
                + Vector3.forward * phenotype.parameters.neckLength * 0.38f);
        Vector3 head = NodePosition(phenotype, phenotype.headNode,
            neck + Vector3.up * phenotype.parameters.neckLength * 0.28f
                + Vector3.forward * phenotype.parameters.neckLength * 0.48f);
        Vector3 muzzle = NodePosition(phenotype, phenotype.muzzleNode,
            head + Vector3.forward * phenotype.parameters.headLength * 0.5f);
        Vector3 faceForward = muzzle - head;
        if (faceForward.sqrMagnitude < 0.0001f) faceForward = Vector3.forward;
        faceForward.Normalize();
        Vector3 up = Vector3.up;
        float headLength = phenotype.parameters.headLength;
        float headHeight = phenotype.headProfile.skullHeight;
        float skullWidth = phenotype.headProfile.skullWidth;
        int neckNode = phenotype.neckNode >= 0 ? phenotype.neckNode : phenotype.spineNodes[phenotype.spineNodes.Length - 1];
        int headNode = phenotype.headNode >= 0 ? phenotype.headNode : neckNode;
        int muzzleNode = phenotype.muzzleNode >= 0 ? phenotype.muzzleNode : headNode;
        var specs = new List<HeadRingSpec>(16);

        AddHeadRing(specs, Vector3.Lerp(socket, neck, 0.08f),
            phenotype.headProfile.neckRootWidth * 0.5f,
            phenotype.parameters.chestDepth * 0.28f, neckNode,
            CreatureV5SurfaceRegion.NeckRoot, 8);
        AddHeadRing(specs, Vector3.Lerp(socket, neck, 0.22f),
            phenotype.headProfile.neckRootWidth * 0.47f,
            phenotype.parameters.chestDepth * 0.26f, neckNode,
            CreatureV5SurfaceRegion.NeckRoot, 8);
        AddHeadRing(specs, Vector3.Lerp(socket, neck, 0.42f),
            Mathf.Lerp(phenotype.headProfile.neckRootWidth,
                phenotype.headProfile.upperNeckWidth, 0.35f) * 0.5f,
            headHeight * 0.48f, neckNode, CreatureV5SurfaceRegion.Neck, 9);
        AddHeadRing(specs, Vector3.Lerp(socket, neck, 0.68f),
            Mathf.Lerp(phenotype.headProfile.neckRootWidth,
                phenotype.headProfile.upperNeckWidth, 0.7f) * 0.5f,
            headHeight * 0.44f, neckNode, CreatureV5SurfaceRegion.Neck, 9);
        AddHeadRing(specs, neck,
            phenotype.headProfile.upperNeckWidth * 0.5f,
            headHeight * 0.42f, neckNode, CreatureV5SurfaceRegion.Neck, 9);

        Vector3 skullBack = head - faceForward * headLength * 0.2f;
        AddHeadRing(specs, Vector3.Lerp(neck, skullBack, 0.58f),
            skullWidth * 0.43f, headHeight * 0.48f, headNode,
            CreatureV5SurfaceRegion.Head, 10);
        AddHeadRing(specs, skullBack,
            skullWidth * 0.5f, headHeight * 0.54f, headNode,
            CreatureV5SurfaceRegion.Head, 10);
        AddHeadRing(specs, head + up * headHeight * 0.05f,
            skullWidth * 0.52f, headHeight * 0.57f, headNode,
            CreatureV5SurfaceRegion.Head, 10);
        AddHeadRing(specs, head + faceForward * headLength * 0.14f,
            skullWidth * 0.5f, headHeight * 0.5f, headNode,
            CreatureV5SurfaceRegion.Head, 10);
        AddHeadRing(specs, head + faceForward * headLength * 0.28f - up * headHeight * 0.025f,
            skullWidth * 0.44f, headHeight * 0.45f, headNode,
            CreatureV5SurfaceRegion.Head, 10);
        AddHeadRing(specs, Vector3.Lerp(head, muzzle, 0.48f) - up * headHeight * 0.04f,
            skullWidth * 0.36f, headHeight * 0.36f, muzzleNode,
            CreatureV5SurfaceRegion.Head, 10);
        AddHeadRing(specs, muzzle - faceForward * headLength * 0.08f - up * headHeight * 0.06f,
            skullWidth * 0.32f, headHeight * 0.3f, muzzleNode,
            CreatureV5SurfaceRegion.Head, 10);
        AddHeadRing(specs, muzzle + faceForward * headLength * phenotype.headProfile.muzzleLengthRatio * 0.28f
                - up * headHeight * 0.07f,
            skullWidth * 0.29f, headHeight * 0.26f, muzzleNode,
            CreatureV5SurfaceRegion.Head, 10);
        Vector3 tip = muzzle + faceForward * headLength * phenotype.headProfile.muzzleLengthRatio * 0.52f
            - up * headHeight * 0.075f;
        AddHeadRing(specs, tip,
            skullWidth * 0.235f, headHeight * 0.2f, muzzleNode,
            CreatureV5SurfaceRegion.SemanticEdge, 11);

        SetHeadRingWeight(specs, 4, CreatureV5LogicalWeight.Two(neckNode, headNode, 0.25f));
        SetHeadRingWeight(specs, 5, CreatureV5LogicalWeight.Two(neckNode, headNode, 0.65f));
        SetHeadRingWeight(specs, 9, CreatureV5LogicalWeight.Two(headNode, muzzleNode, 0.25f));
        SetHeadRingWeight(specs, 10, CreatureV5LogicalWeight.Two(headNode, muzzleNode, 0.65f));

        Vector3[] centers = new Vector3[specs.Count];
        for (int i = 0; i < specs.Count; i++) centers[i] = specs[i].center;
        Frame[] frames = BuildFrames(centers, Vector3.up);
        int[] previous = boundary;

        for (int ring = 0; ring < specs.Count; ring++)
        {
            HeadRingSpec spec = specs[ring];
            float t = ring / (float)(specs.Count - 1);
            var current = new int[phenotype.anatomy.headRingSegments];
            for (int i = 0; i < current.Length; i++)
            {
                float angle = i / (float)current.Length * Mathf.PI * 2f;
                float exponent = spec.region == CreatureV5SurfaceRegion.Head ? 2.05f : 1.9f;
                Vector2 radial = Superellipse(angle, exponent);
                float jawDrop = spec.region == CreatureV5SurfaceRegion.Head
                    ? Mathf.Max(0f, -radial.y) * headHeight * 0.08f : 0f;
                Vector3 position = spec.center
                    + frames[ring].right * radial.x * spec.halfWidth
                    + frames[ring].up * (radial.y * spec.halfHeight - jawDrop);
                CreatureV5SurfaceRegion vertexRegion = jawDrop > headHeight * 0.015f
                    ? CreatureV5SurfaceRegion.Jaw : spec.region;
                current[i] = result.Add(position, new Vector2(i / (float)current.Length, t),
                    BodyColor(phenotype, 1f, radial.y), spec.weight,
                    vertexRegion != CreatureV5SurfaceRegion.SemanticEdge,
                    vertexRegion, spec.smoothingGroup);
            }
            BridgeLoops(result, previous, current, false);
            previous = current;
        }
        CapLoop(result, previous, tip + faceForward * headLength * 0.025f,
            CreatureV5LogicalWeight.Single(muzzleNode), BodyColor(phenotype, 1f, 0f),
            false, false, CreatureV5SurfaceRegion.SemanticEdge, 11);
    }

    static void AddHeadRing(
        ICollection<HeadRingSpec> specs,
        Vector3 center,
        float halfWidth,
        float halfHeight,
        int node,
        CreatureV5SurfaceRegion region,
        byte smoothingGroup)
    {
        specs.Add(new HeadRingSpec
        {
            center = center,
            halfWidth = Mathf.Max(0.035f, halfWidth),
            halfHeight = Mathf.Max(0.035f, halfHeight),
            node = node,
            weight = CreatureV5LogicalWeight.Single(node),
            region = region,
            smoothingGroup = smoothingGroup
        });
    }

    static void SetHeadRingWeight(
        IList<HeadRingSpec> specs,
        int index,
        CreatureV5LogicalWeight weight)
    {
        HeadRingSpec spec = specs[index];
        spec.weight = weight;
        specs[index] = spec;
    }

    static void BuildTail(
        SurfaceBuilder result,
        CreatureV5Phenotype phenotype,
        int[] boundary)
    {
        Vector3 root = Average(result.vertices, boundary);
        float length = phenotype.parameters.bodyLength * 0.12f;
        Vector3[] centers =
        {
            root + Vector3.back * length * 0.08f,
            root + Vector3.back * length * 0.35f + Vector3.up * length * 0.04f,
            root + Vector3.back * length * 0.68f - Vector3.up * length * 0.05f,
            root + Vector3.back * length - Vector3.up * length * 0.16f
        };
        Frame[] frames = BuildFrames(centers, Vector3.up);
        int[] previous = boundary;
        int tailNode = phenotype.tailNodes.Length > 0 ? phenotype.tailNodes[0] : phenotype.spineNodes[0];
        for (int ring = 0; ring < centers.Length; ring++)
        {
            float t = ring / (float)(centers.Length - 1);
            float radius = phenotype.parameters.pelvisWidth * Mathf.Lerp(0.105f, 0.035f, t);
            var current = new int[phenotype.anatomy.tailRingSegments];
            for (int i = 0; i < current.Length; i++)
            {
                float angle = i / (float)current.Length * Mathf.PI * 2f;
                current[i] = result.Add(centers[ring]
                        + frames[ring].right * Mathf.Cos(angle) * radius
                        + frames[ring].up * Mathf.Sin(angle) * radius,
                    new Vector2(i / (float)current.Length, t),
                    BodyColor(phenotype, 0f, Mathf.Sin(angle)),
                    CreatureV5LogicalWeight.Single(tailNode), true,
                    CreatureV5SurfaceRegion.Tail, 12);
            }
            BridgeLoops(result, previous, current, false);
            previous = current;
        }
        CapLoop(result, previous, centers[centers.Length - 1],
            CreatureV5LogicalWeight.Single(tailNode), BodyColor(phenotype, 0f, 0f),
            false, false, CreatureV5SurfaceRegion.SemanticEdge, 12);
    }

    static void BuildHooves(SurfaceBuilder result, CreatureV5Phenotype phenotype)
    {
        for (int i = 0; i < phenotype.motion.legs.Length; i++)
        {
            CreaturePhenotypeLeg leg = phenotype.motion.legs[i];
            CreatureBodyNode footNode = phenotype.motion.graph.nodes[leg.footNode];
            Vector3 crown = phenotype.motion.nodePositions[leg.footNode];
            Quaternion rotation = phenotype.motion.nodeRotations[leg.footNode];
            float width = phenotype.parameters.hoofWidth * (leg.IsFront ? 1.04f : 0.98f);
            for (int lobe = 0; lobe < 2; lobe++)
            {
                float side = lobe == 0 ? -1f : 1f;
                BuildHoofLobe(result, phenotype, leg, crown, rotation,
                    side, width, phenotype.parameters.hoofLength,
                    Mathf.Max(width * 0.72f, footNode.size.y));
            }
        }
    }

    static void BuildHoofLobe(
        SurfaceBuilder result,
        CreatureV5Phenotype phenotype,
        CreaturePhenotypeLeg leg,
        Vector3 crown,
        Quaternion rotation,
        float side,
        float totalWidth,
        float length,
        float height)
    {
        const int segments = 8;
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        Vector3 forward = rotation * Vector3.forward;
        float lobeWidth = totalWidth * 0.43f;
        Vector3 center = crown + right * side * totalWidth * 0.27f
            + forward * length * 0.1f - up * height * 0.3f;
        int[][] rings = new int[3][];
        float[] heights = { height * 0.48f, -height * 0.02f, -height * 0.52f };
        float[] scales = { 0.72f, 1f, 0.92f };
        float[] forwardOffsets = { -length * 0.08f, length * 0.06f, length * 0.17f };
        for (int ring = 0; ring < rings.Length; ring++)
        {
            rings[ring] = new int[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector2 radial = Superellipse(angle, 4f);
                float toeTaper = 1f - Mathf.Max(0f, radial.y) * 0.12f;
                Vector3 position = center + up * heights[ring] + forward * forwardOffsets[ring]
                    + right * radial.x * lobeWidth * 0.5f * scales[ring] * toeTaper
                    + forward * radial.y * length * 0.5f * scales[ring];
                Color hoofColor = Color.Lerp(
                    new Color(0.075f, 0.065f, 0.06f),
                    phenotype.motion.graph.nodes[leg.footNode].side == CreatureBodySide.Left
                        ? new Color(0.16f, 0.14f, 0.12f) : new Color(0.13f, 0.12f, 0.11f),
                    ring * 0.18f);
                rings[ring][i] = result.Add(position,
                    new Vector2(i / (float)segments, ring / 2f), hoofColor,
                    CreatureV5LogicalWeight.Single(leg.footNode), false,
                    ring == 0 ? CreatureV5SurfaceRegion.HoofCrown
                        : CreatureV5SurfaceRegion.HoofShell, 13);
            }
            if (ring > 0) BridgeLoops(result, rings[ring - 1], rings[ring], false);
        }
        CapLoop(result, rings[0], center + up * heights[0],
            CreatureV5LogicalWeight.Single(leg.footNode), new Color(0.1f, 0.09f, 0.08f),
            true, false, CreatureV5SurfaceRegion.HoofCrown, 13);
        CapLoop(result, rings[2], center + up * heights[2],
            CreatureV5LogicalWeight.Single(leg.footNode), new Color(0.055f, 0.05f, 0.045f),
            false, false, CreatureV5SurfaceRegion.HoofShell, 13);
    }

    static void BridgeLoops(SurfaceBuilder result, int[] a, int[] b, bool reverse)
    {
        if (a.Length == b.Length)
        {
            for (int i = 0; i < a.Length; i++)
            {
                int next = (i + 1) % a.Length;
                if (reverse) result.Quad(a[i], b[i], b[next], a[next]);
                else result.Quad(a[i], a[next], b[next], b[i]);
            }
            return;
        }

        int ia = 0;
        int ib = 0;
        while (ia < a.Length || ib < b.Length)
        {
            float nextA = (ia + 1f) / a.Length;
            float nextB = (ib + 1f) / b.Length;
            int currentA = a[ia % a.Length];
            int currentB = b[ib % b.Length];
            if (Mathf.Abs(nextA - nextB) < 0.00001f)
            {
                int followingA = a[(ia + 1) % a.Length];
                int followingB = b[(ib + 1) % b.Length];
                if (reverse) result.Quad(currentA, currentB, followingB, followingA);
                else result.Quad(currentA, followingA, followingB, currentB);
                ia++; ib++;
            }
            else if (nextA < nextB)
            {
                int followingA = a[(ia + 1) % a.Length];
                if (reverse) result.Triangle(currentA, currentB, followingA);
                else result.Triangle(currentA, followingA, currentB);
                ia++;
            }
            else
            {
                int followingB = b[(ib + 1) % b.Length];
                if (reverse) result.Triangle(currentA, followingB, currentB);
                else result.Triangle(currentA, currentB, followingB);
                ib++;
            }
        }
    }

    static void CapLoop(
        SurfaceBuilder result,
        int[] loop,
        Vector3 center,
        CreatureV5LogicalWeight weight,
        Color color,
        bool reverse,
        bool smoothable = true,
        CreatureV5SurfaceRegion region = CreatureV5SurfaceRegion.Torso,
        byte smoothingGroup = 1)
    {
        int centerIndex = result.Add(center, new Vector2(0.5f, 0.5f), color, weight,
            smoothable, region, smoothingGroup);
        for (int i = 0; i < loop.Length; i++)
        {
            int next = (i + 1) % loop.Length;
            if (reverse) result.Triangle(centerIndex, loop[next], loop[i]);
            else result.Triangle(centerIndex, loop[i], loop[next]);
        }
    }

    static SurfaceBuilder Subdivide(SurfaceBuilder source)
    {
        var result = new SurfaceBuilder();
        for (int i = 0; i < source.vertices.Count; i++)
            result.Add(source.vertices[i], source.uv[i], source.colors[i], source.weights[i],
                source.smoothable[i], source.regions[i], source.smoothingGroups[i]);
        var midpointCache = new Dictionary<ulong, int>(source.triangles.Count);
        for (int i = 0; i < source.triangles.Count; i += 3)
        {
            int a = source.triangles[i];
            int b = source.triangles[i + 1];
            int c = source.triangles[i + 2];
            int ab = Midpoint(result, source, midpointCache, a, b);
            int bc = Midpoint(result, source, midpointCache, b, c);
            int ca = Midpoint(result, source, midpointCache, c, a);
            result.Triangle(a, ab, ca);
            result.Triangle(ab, b, bc);
            result.Triangle(ca, bc, c);
            result.Triangle(ab, bc, ca);
        }
        return result;
    }

    static int Midpoint(
        SurfaceBuilder target,
        SurfaceBuilder source,
        IDictionary<ulong, int> cache,
        int a,
        int b)
    {
        uint min = (uint)Mathf.Min(a, b);
        uint max = (uint)Mathf.Max(a, b);
        ulong key = ((ulong)min << 32) | max;
        if (cache.TryGetValue(key, out int existing)) return existing;
        int index = target.Add(
            (source.vertices[a] + source.vertices[b]) * 0.5f,
            (source.uv[a] + source.uv[b]) * 0.5f,
            Color.Lerp(source.colors[a], source.colors[b], 0.5f),
            CreatureV5LogicalWeight.Blend(source.weights[a], source.weights[b], 0.5f),
            source.smoothable[a] && source.smoothable[b]
                && source.smoothingGroups[a] == source.smoothingGroups[b],
            source.regions[a] == source.regions[b]
                ? source.regions[a] : CreatureV5SurfaceRegion.SemanticEdge,
            source.smoothingGroups[a] == source.smoothingGroups[b]
                ? source.smoothingGroups[a] : (byte)0);
        cache[key] = index;
        return index;
    }

    static void TaubinSmooth(SurfaceBuilder surface, int iterations, float lambda, float mu)
    {
        var neighbors = new HashSet<int>[surface.vertices.Count];
        for (int i = 0; i < neighbors.Length; i++) neighbors[i] = new HashSet<int>();
        for (int i = 0; i < surface.triangles.Count; i += 3)
        {
            AddNeighbor(neighbors, surface.triangles[i], surface.triangles[i + 1]);
            AddNeighbor(neighbors, surface.triangles[i + 1], surface.triangles[i + 2]);
            AddNeighbor(neighbors, surface.triangles[i + 2], surface.triangles[i]);
        }
        var next = new Vector3[surface.vertices.Count];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            SmoothPass(surface, neighbors, next, lambda);
            SmoothPass(surface, neighbors, next, mu);
        }
    }

    static void SmoothPass(
        SurfaceBuilder surface,
        HashSet<int>[] neighbors,
        Vector3[] next,
        float strength)
    {
        for (int i = 0; i < surface.vertices.Count; i++)
        {
            if (!surface.smoothable[i] || neighbors[i].Count == 0)
            {
                next[i] = surface.vertices[i];
                continue;
            }
            Vector3 average = Vector3.zero;
            int compatible = 0;
            foreach (int neighbor in neighbors[i])
            {
                if (surface.smoothingGroups[neighbor] != surface.smoothingGroups[i]
                    || surface.regions[neighbor] != surface.regions[i]) continue;
                average += surface.vertices[neighbor];
                compatible++;
            }
            if (compatible == 0)
            {
                next[i] = surface.vertices[i];
                continue;
            }
            average /= compatible;
            next[i] = surface.vertices[i] + (average - surface.vertices[i]) * strength;
        }
        for (int i = 0; i < next.Length; i++) surface.vertices[i] = next[i];
    }

    static void AddNeighbor(HashSet<int>[] neighbors, int a, int b)
    {
        neighbors[a].Add(b);
        neighbors[b].Add(a);
    }

    static Frame[] BuildFrames(Vector3[] centers, Vector3 preferredUp)
    {
        var frames = new Frame[centers.Length];
        Vector3 previousTangent = Vector3.forward;
        Vector3 previousRight = Vector3.right;
        Vector3 previousUp = Vector3.up;
        for (int i = 0; i < centers.Length; i++)
        {
            Vector3 tangent = i == 0
                ? centers[Mathf.Min(1, centers.Length - 1)] - centers[0]
                : i == centers.Length - 1
                    ? centers[i] - centers[i - 1]
                    : centers[i + 1] - centers[i - 1];
            if (tangent.sqrMagnitude < 0.000001f) tangent = previousTangent;
            tangent.Normalize();
            Vector3 right;
            Vector3 up;
            if (i == 0)
            {
                right = Vector3.Cross(preferredUp, tangent);
                if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(Vector3.forward, tangent);
                if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
                right.Normalize();
                up = Vector3.Cross(tangent, right).normalized;
            }
            else
            {
                Quaternion transport = Quaternion.FromToRotation(previousTangent, tangent);
                right = Vector3.ProjectOnPlane(transport * previousRight, tangent).normalized;
                if (right.sqrMagnitude < 0.0001f) right = previousRight;
                up = Vector3.Cross(tangent, right).normalized;
                if (Vector3.Dot(up, transport * previousUp) < 0f)
                {
                    right = -right;
                    up = -up;
                }
            }
            frames[i] = new Frame { center = centers[i], tangent = tangent, right = right, up = up };
            previousTangent = tangent;
            previousRight = right;
            previousUp = up;
        }
        return frames;
    }

    static void AppendSegmentSamples(List<Vector3> result, Vector3 start, Vector3 end, int count)
    {
        for (int i = 1; i <= count; i++) result.Add(Vector3.Lerp(start, end, i / (float)count));
    }

    static Vector3[] SamplePolyline(Vector3[] anchors, int count)
    {
        var result = new Vector3[count];
        var lengths = new float[Mathf.Max(1, anchors.Length - 1)];
        float total = 0f;
        for (int i = 0; i < lengths.Length; i++)
        {
            lengths[i] = Vector3.Distance(anchors[i], anchors[i + 1]);
            total += lengths[i];
        }
        if (total < 0.0001f)
        {
            for (int i = 0; i < count; i++) result[i] = anchors[0];
            return result;
        }
        for (int sample = 0; sample < count; sample++)
        {
            float target = sample / (float)(count - 1) * total;
            float traversed = 0f;
            for (int segment = 0; segment < lengths.Length; segment++)
            {
                if (traversed + lengths[segment] >= target || segment == lengths.Length - 1)
                {
                    result[sample] = Vector3.Lerp(anchors[segment], anchors[segment + 1],
                        lengths[segment] > 0f ? (target - traversed) / lengths[segment] : 0f);
                    break;
                }
                traversed += lengths[segment];
            }
        }
        return result;
    }

    static Vector3[] SampleCatmullRom(Vector3[] anchors, int count)
    {
        if (anchors == null || anchors.Length < 3) return SamplePolyline(anchors, count);
        var result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float scaled = i / (float)(count - 1) * (anchors.Length - 1);
            int segment = Mathf.Min(Mathf.FloorToInt(scaled), anchors.Length - 2);
            float t = scaled - segment;
            Vector3 p0 = anchors[Mathf.Max(0, segment - 1)];
            Vector3 p1 = anchors[segment];
            Vector3 p2 = anchors[segment + 1];
            Vector3 p3 = anchors[Mathf.Min(anchors.Length - 1, segment + 2)];
            float t2 = t * t;
            float t3 = t2 * t;
            result[i] = 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
        return result;
    }

    static CreatureV5LogicalWeight SpineWeight(CreatureV5Phenotype phenotype, float t)
    {
        int count = phenotype.spineNodes.Length;
        if (count == 0) return CreatureV5LogicalWeight.Single(0);
        if (count == 1) return CreatureV5LogicalWeight.Single(phenotype.spineNodes[0]);
        float scaled = Mathf.Clamp01(t) * (count - 1);
        int left = Mathf.Min(Mathf.FloorToInt(scaled), count - 2);
        return CreatureV5LogicalWeight.Two(
            phenotype.spineNodes[left], phenotype.spineNodes[left + 1], scaled - left);
    }

    static Vector3 NodePosition(CreatureV5Phenotype phenotype, int node, Vector3 fallback)
    {
        return node >= 0 && node < phenotype.motion.nodePositions.Length
            ? phenotype.motion.nodePositions[node] : fallback;
    }

    static Vector2 Superellipse(float angle, float exponent)
    {
        float c = Mathf.Cos(angle);
        float s = Mathf.Sin(angle);
        float power = 2f / Mathf.Max(0.25f, exponent);
        return new Vector2(Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), power),
            Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), power));
    }

    static Color BodyColor(CreatureV5Phenotype phenotype, float longitudinal, float vertical)
    {
        Color primary = phenotype.motion.graph.nodes.Count > 0
            ? phenotype.motion.graph.nodes[0].type == CreatureBodyNodeType.Spine
                ? phenotype.motion.graph.nodes[0].side == CreatureBodySide.Center
                    ? Color.Lerp(new Color(0.28f, 0.18f, 0.11f), new Color(0.5f, 0.34f, 0.2f),
                        Mathf.Repeat(phenotype.motion.seed * 0.173f, 1f))
                    : new Color(0.4f, 0.27f, 0.16f)
                : new Color(0.4f, 0.27f, 0.16f)
            : new Color(0.4f, 0.27f, 0.16f);
        float belly = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, -0.75f, vertical));
        float dorsal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.95f, vertical));
        Color color = Color.Lerp(primary, new Color(0.72f, 0.58f, 0.4f), belly * 0.48f);
        color = Color.Lerp(color, new Color(0.19f, 0.12f, 0.08f), dorsal * 0.22f);
        float pattern = Mathf.Sin((longitudinal * 3.2f + phenotype.motion.seed * 0.013f) * Mathf.PI * 2f) * 0.5f + 0.5f;
        return Color.Lerp(color, Color.white, pattern * 0.035f);
    }

    static Vector3 Average(IList<Vector3> vertices, int[] indices)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < indices.Length; i++) sum += vertices[indices[i]];
        return sum / Mathf.Max(1, indices.Length);
    }

    static Vector3[] CalculateNormals(SurfaceBuilder surface)
    {
        var normals = new Vector3[surface.vertices.Count];
        for (int i = 0; i < surface.triangles.Count; i += 3)
        {
            int a = surface.triangles[i];
            int b = surface.triangles[i + 1];
            int c = surface.triangles[i + 2];
            Vector3 ab = surface.vertices[b] - surface.vertices[a];
            Vector3 ac = surface.vertices[c] - surface.vertices[a];
            Vector3 face = Vector3.Cross(ab, ac);
            if (face.sqrMagnitude < 0.0000000001f) continue;
            face.Normalize();
            normals[a] += face * CornerAngle(surface.vertices[b] - surface.vertices[a],
                surface.vertices[c] - surface.vertices[a]);
            normals[b] += face * CornerAngle(surface.vertices[c] - surface.vertices[b],
                surface.vertices[a] - surface.vertices[b]);
            normals[c] += face * CornerAngle(surface.vertices[a] - surface.vertices[c],
                surface.vertices[b] - surface.vertices[c]);
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].sqrMagnitude > 0.000001f ? normals[i].normalized : Vector3.up;
        return normals;
    }

    static float CornerAngle(Vector3 a, Vector3 b)
    {
        float denominator = Mathf.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
        if (denominator < 0.000001f) return 0f;
        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b) / denominator, -1f, 1f));
    }

    static void EnsureOutwardWinding(SurfaceBuilder surface)
    {
        int[] parents = BuildComponentParents(surface);
        var volumes = new Dictionary<int, float>();
        for (int i = 0; i < surface.triangles.Count; i += 3)
        {
            int a = surface.triangles[i];
            int root = FindRoot(parents, a);
            float volume = Vector3.Dot(surface.vertices[a],
                Vector3.Cross(surface.vertices[surface.triangles[i + 1]],
                    surface.vertices[surface.triangles[i + 2]])) / 6f;
            volumes[root] = volumes.TryGetValue(root, out float current) ? current + volume : volume;
        }
        for (int i = 0; i < surface.triangles.Count; i += 3)
        {
            int root = FindRoot(parents, surface.triangles[i]);
            if (!volumes.TryGetValue(root, out float volume) || volume >= 0f) continue;
            int swap = surface.triangles[i + 1];
            surface.triangles[i + 1] = surface.triangles[i + 2];
            surface.triangles[i + 2] = swap;
        }
    }

    static CreatureV5MeshDiagnostics Diagnose(SurfaceBuilder surface, Vector3[] normals)
    {
        var edges = new Dictionary<ulong, int>(surface.triangles.Count);
        var edgeDirections = new Dictionary<ulong, int>(surface.triangles.Count);
        int degenerate = 0;
        float signedVolume = 0f;
        for (int i = 0; i < surface.triangles.Count; i += 3)
        {
            int a = surface.triangles[i];
            int b = surface.triangles[i + 1];
            int c = surface.triangles[i + 2];
            AddEdge(edges, a, b);
            AddEdge(edges, b, c);
            AddEdge(edges, c, a);
            AddDirectedEdge(edgeDirections, a, b);
            AddDirectedEdge(edgeDirections, b, c);
            AddDirectedEdge(edgeDirections, c, a);
            Vector3 cross = Vector3.Cross(surface.vertices[b] - surface.vertices[a],
                surface.vertices[c] - surface.vertices[a]);
            if (cross.sqrMagnitude < 0.0000000001f) degenerate++;
            signedVolume += Vector3.Dot(surface.vertices[a],
                Vector3.Cross(surface.vertices[b], surface.vertices[c])) / 6f;
        }
        int boundary = 0;
        int nonManifold = 0;
        int inconsistentWinding = 0;
        foreach (int count in edges.Values)
        {
            if (count == 1) boundary++;
            else if (count != 2) nonManifold++;
        }
        foreach (KeyValuePair<ulong, int> edge in edgeDirections)
            if (edges[edge.Key] == 2 && edge.Value != 0) inconsistentWinding++;
        int invalidNormals = 0;
        for (int i = 0; i < normals.Length; i++)
            if (float.IsNaN(normals[i].x) || float.IsInfinity(normals[i].x)
                || normals[i].sqrMagnitude < 0.25f) invalidNormals++;
        int[] parents = BuildComponentParents(surface);
        var roots = new HashSet<int>();
        for (int i = 0; i < surface.triangles.Count; i++)
            roots.Add(FindRoot(parents, surface.triangles[i]));
        return new CreatureV5MeshDiagnostics
        {
            connectedShellCount = roots.Count,
            boundaryEdgeCount = boundary,
            nonManifoldEdgeCount = nonManifold,
            inconsistentWindingEdgeCount = inconsistentWinding,
            degenerateTriangleCount = degenerate,
            invalidNormalCount = invalidNormals,
            signedVolume = signedVolume
        };
    }

    static int[] BuildComponentParents(SurfaceBuilder surface)
    {
        var parents = new int[surface.vertices.Count];
        for (int i = 0; i < parents.Length; i++) parents[i] = i;
        for (int i = 0; i < surface.triangles.Count; i += 3)
        {
            Union(parents, surface.triangles[i], surface.triangles[i + 1]);
            Union(parents, surface.triangles[i + 1], surface.triangles[i + 2]);
        }
        return parents;
    }

    static int FindRoot(int[] parents, int value)
    {
        while (parents[value] != value)
        {
            parents[value] = parents[parents[value]];
            value = parents[value];
        }
        return value;
    }

    static void Union(int[] parents, int a, int b)
    {
        int rootA = FindRoot(parents, a);
        int rootB = FindRoot(parents, b);
        if (rootA != rootB) parents[rootB] = rootA;
    }

    static void AddEdge(IDictionary<ulong, int> edges, int a, int b)
    {
        uint minimum = (uint)Mathf.Min(a, b);
        uint maximum = (uint)Mathf.Max(a, b);
        ulong key = ((ulong)minimum << 32) | maximum;
        edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    static void AddDirectedEdge(IDictionary<ulong, int> directions, int a, int b)
    {
        uint minimum = (uint)Mathf.Min(a, b);
        uint maximum = (uint)Mathf.Max(a, b);
        ulong key = ((ulong)minimum << 32) | maximum;
        int direction = a < b ? 1 : -1;
        directions[key] = directions.TryGetValue(key, out int current)
            ? current + direction : direction;
    }

    static Bounds CalculateBounds(IList<Vector3> vertices)
    {
        if (vertices.Count == 0) return new Bounds(Vector3.zero, Vector3.one);
        Vector3 min = vertices[0];
        Vector3 max = vertices[0];
        for (int i = 1; i < vertices.Count; i++)
        {
            min = Vector3.Min(min, vertices[i]);
            max = Vector3.Max(max, vertices[i]);
        }
        return new Bounds((min + max) * 0.5f, max - min);
    }

    static int Wrap(int value, int count)
    {
        value %= count;
        return value < 0 ? value + count : value;
    }
}

public static class CreatureV5MeshFactory
{
    public static Mesh Create(
        CreatureV5MeshData data,
        CreatureRig rig,
        Transform creatureRoot,
        int seed)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (rig == null) throw new ArgumentNullException(nameof(rig));
        var mesh = new Mesh
        {
            name = $"CreatureV5_{data.quality}_{seed}",
            indexFormat = IndexFormat.UInt32
        };
        mesh.vertices = data.vertices;
        mesh.normals = data.normals;
        mesh.uv = data.uv;
        mesh.colors = data.colors;
        mesh.triangles = data.triangles;
        var boneWeights = new BoneWeight[data.logicalWeights.Length];
        for (int i = 0; i < boneWeights.Length; i++)
        {
            CreatureV5LogicalWeight source = data.logicalWeights[i];
            boneWeights[i] = new BoneWeight
            {
                boneIndex0 = BoneIndex(rig, source.node0), weight0 = source.weight0,
                boneIndex1 = BoneIndex(rig, source.node1), weight1 = source.weight1,
                boneIndex2 = BoneIndex(rig, source.node2), weight2 = source.weight2,
                boneIndex3 = BoneIndex(rig, source.node3), weight3 = source.weight3
            };
        }
        mesh.boneWeights = boneWeights;
        var bindPoses = new Matrix4x4[rig.bones.Length];
        for (int i = 0; i < bindPoses.Length; i++)
            bindPoses[i] = rig.bones[i].worldToLocalMatrix * creatureRoot.localToWorldMatrix;
        mesh.bindposes = bindPoses;
        mesh.bounds = data.bounds;
        return mesh;
    }

    static int BoneIndex(CreatureRig rig, int node)
    {
        return node >= 0 && node < rig.nodeBoneIndices.Length ? rig.nodeBoneIndices[node] : rig.bodyIndex;
    }
}
