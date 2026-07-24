using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlanetSurfaceLodTransitionMeshData
{
    public readonly string MeshName;
    public readonly Vector3[] Vertices;
    public readonly Vector3[] Normals;
    public readonly Color[] Colors;
    public readonly int[] Triangles;
    public readonly Bounds Bounds;

    public PlanetSurfaceLodTransitionMeshData(
        string meshName,
        Vector3[] vertices,
        Vector3[] normals,
        Color[] colors,
        int[] triangles,
        Bounds bounds)
    {
        MeshName = meshName;
        Vertices = vertices ?? Array.Empty<Vector3>();
        Normals = normals ?? Array.Empty<Vector3>();
        Colors = colors ?? Array.Empty<Color>();
        Triangles = triangles ?? Array.Empty<int>();
        Bounds = bounds;
    }
}

public sealed class PlanetSurfaceLodTransitionBuildInput
{
    public string MeshName;
    public int Seed;
    public float PlanetRadius;
    public float MaximumTerrainElevation;
    public Color SurfaceColor;
    public Color RockColor;
    public PlanetTerrainSettings Terrain;
    public Vector3[] FarVertices;
    public Vector3 CenterDirection;
    public float InnerArcRadius;
    public float OuterArcRadius;
    public int AngularSegments;
    public int RadialSegments;
    public float SkirtDepth;
}

public static class PlanetSurfaceLodTransitionMeshBuilder
{
    const float SurfaceLift = 0.08f;

    // Compatibility entry point for editor tooling and tests. Runtime streaming
    // uses CaptureInput -> BuildData on a worker -> ApplyData on the main thread.
    public static Mesh Build(
        GalaxyPlanetDefinition definition,
        Mesh farLodMesh,
        Vector3 centerDirection,
        float innerArcRadius,
        float outerArcRadius,
        int angularSegments = 96,
        int radialSegments = 5,
        float skirtDepth = 18f)
    {
        PlanetSurfaceLodTransitionBuildInput input = CaptureInput(
            definition,
            farLodMesh != null ? farLodMesh.vertices : null,
            centerDirection,
            innerArcRadius,
            outerArcRadius,
            angularSegments,
            radialSegments,
            skirtDepth);
        return input == null ? null : ApplyData(BuildData(input), null);
    }

    // Must be called on the main thread because it snapshots a live Mesh.
    public static PlanetSurfaceLodTransitionBuildInput CaptureInput(
        GalaxyPlanetDefinition definition,
        Vector3[] farVertices,
        Vector3 centerDirection,
        float innerArcRadius,
        float outerArcRadius,
        int angularSegments = 96,
        int radialSegments = 5,
        float skirtDepth = 18f)
    {
        if (definition == null)
            return null;

        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        return new PlanetSurfaceLodTransitionBuildInput
        {
            MeshName = $"SurfaceLodTransition_{definition.planetId}",
            Seed = definition.seed,
            PlanetRadius = Mathf.Max(1f, celestial.radius),
            MaximumTerrainElevation = Mathf.Max(1f, celestial.maximumTerrainElevation),
            SurfaceColor = definition.surfaceColor,
            RockColor = definition.rockColor,
            Terrain = (definition.terrain ?? new PlanetTerrainSettings()).Clone(),
            // Mesh.vertices already returns a detached array. Keep that immutable
            // snapshot so repeated ring requests do not allocate another 25k+
            // element copy on the main thread.
            FarVertices = farVertices ?? Array.Empty<Vector3>(),
            CenterDirection = centerDirection.sqrMagnitude > 0.001f
                ? centerDirection.normalized
                : Vector3.up,
            InnerArcRadius = Mathf.Max(1f, innerArcRadius),
            OuterArcRadius = Mathf.Max(innerArcRadius + 2f, outerArcRadius),
            AngularSegments = Mathf.Clamp(angularSegments, 24, 192),
            RadialSegments = Mathf.Clamp(radialSegments, 2, 12),
            SkirtDepth = Mathf.Max(2f, skirtDepth)
        };
    }

    // Pure data work. It does not create, read or mutate a UnityEngine.Object and
    // is therefore safe to run inside Task.Run or a Burst/Job wrapper.
    public static PlanetSurfaceLodTransitionMeshData BuildData(
        PlanetSurfaceLodTransitionBuildInput input)
    {
        if (input == null)
            return null;

        float planetRadius = input.PlanetRadius;
        Vector3 centerDirection = input.CenterDirection;
        int angularSegments = input.AngularSegments;
        int radialSegments = input.RadialSegments;
        Vector3 reference = Mathf.Abs(Vector3.Dot(centerDirection, Vector3.up)) < 0.9f
            ? Vector3.up
            : Vector3.right;
        Vector3 tangent = Vector3.Cross(reference, centerDirection).normalized;
        Vector3 bitangent = Vector3.Cross(centerDirection, tangent).normalized;

        var outerFarHeights = new float[angularSegments];
        for (int segment = 0; segment < angularSegments; segment++)
        {
            float phase = segment * Mathf.PI * 2f / angularSegments;
            Vector3 rimDirection =
                tangent * Mathf.Cos(phase)
                + bitangent * Mathf.Sin(phase);
            Vector3 direction = DirectionAtArc(
                centerDirection,
                rimDirection,
                input.OuterArcRadius,
                planetRadius);
            outerFarHeights[segment] = SampleNearestFarHeight(
                input.FarVertices,
                direction,
                planetRadius,
                input.Seed,
                input.Terrain);
        }

        int mainVertexCount = (radialSegments + 1) * angularSegments;
        int skirtVertexCount = angularSegments * 4;
        var vertices = new List<Vector3>(mainVertexCount + skirtVertexCount);
        var colors = new List<Color>(mainVertexCount + skirtVertexCount);
        var triangles = new List<int>(
            radialSegments * angularSegments * 6
            + angularSegments * 24);
        float colorRange = Mathf.Max(8f, input.MaximumTerrainElevation);

        for (int ring = 0; ring <= radialSegments; ring++)
        {
            float ratio = ring / (float)radialSegments;
            float arcRadius = Mathf.Lerp(input.InnerArcRadius, input.OuterArcRadius, ratio);
            float farMorph = SmoothStep(Mathf.InverseLerp(0.48f, 1f, ratio));
            for (int segment = 0; segment < angularSegments; segment++)
            {
                float phase = segment * Mathf.PI * 2f / angularSegments;
                Vector3 rimDirection =
                    tangent * Mathf.Cos(phase)
                    + bitangent * Mathf.Sin(phase);
                Vector3 direction = DirectionAtArc(
                    centerDirection,
                    rimDirection,
                    arcRadius,
                    planetRadius);
                float exactHeight = VoxelQuadSphereTerrain.GetSurfaceNoise(
                    direction * planetRadius,
                    input.Seed,
                    input.Terrain);
                float height = Mathf.Lerp(
                    exactHeight,
                    outerFarHeights[segment],
                    farMorph);
                AddVertex(
                    vertices,
                    colors,
                    direction,
                    planetRadius + height + SurfaceLift,
                    height,
                    colorRange,
                    input);
            }
        }

        for (int ring = 0; ring < radialSegments; ring++)
        for (int segment = 0; segment < angularSegments; segment++)
        {
            int next = (segment + 1) % angularSegments;
            int a = ring * angularSegments + segment;
            int b = ring * angularSegments + next;
            int c = (ring + 1) * angularSegments + segment;
            int d = (ring + 1) * angularSegments + next;
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(d);
        }

        AddSkirt(
            vertices,
            colors,
            triangles,
            0,
            angularSegments,
            input.SkirtDepth,
            planetRadius,
            colorRange,
            input);
        AddSkirt(
            vertices,
            colors,
            triangles,
            radialSegments * angularSegments,
            angularSegments,
            input.SkirtDepth,
            planetRadius,
            colorRange,
            input);

        Vector3[] vertexArray = vertices.ToArray();
        int[] triangleArray = triangles.ToArray();
        Vector3[] normals = CalculateNormals(vertexArray, triangleArray);
        EnsureOutwardWinding(vertexArray, normals, triangleArray);
        normals = CalculateNormals(vertexArray, triangleArray);
        return new PlanetSurfaceLodTransitionMeshData(
            input.MeshName,
            vertexArray,
            normals,
            colors.ToArray(),
            triangleArray,
            CalculateBounds(vertexArray));
    }

    // Must be called on the main thread. Reusing the Mesh avoids native object
    // allocation/destruction every time the player crosses a streaming boundary.
    public static Mesh ApplyData(
        PlanetSurfaceLodTransitionMeshData data,
        Mesh target)
    {
        if (data == null)
            return null;

        if (target == null)
        {
            target = new Mesh
            {
                name = data.MeshName,
                hideFlags = HideFlags.DontSave
            };
            target.MarkDynamic();
        }
        else
        {
            target.Clear(false);
            target.name = data.MeshName;
        }

        target.indexFormat = data.Vertices.Length > 65535
            ? IndexFormat.UInt32
            : IndexFormat.UInt16;
        target.vertices = data.Vertices;
        target.normals = data.Normals;
        target.colors = data.Colors;
        target.SetTriangles(data.Triangles, 0, false);
        target.bounds = data.Bounds;
        return target;
    }

    static Vector3 DirectionAtArc(
        Vector3 centerDirection,
        Vector3 rimDirection,
        float arcRadius,
        float planetRadius)
    {
        float angle = Mathf.Clamp(
            arcRadius / planetRadius,
            0f,
            Mathf.PI * 0.45f);
        return (
            centerDirection * Mathf.Cos(angle)
            + rimDirection * Mathf.Sin(angle)).normalized;
    }

    static float SampleNearestFarHeight(
        Vector3[] farVertices,
        Vector3 direction,
        float planetRadius,
        int seed,
        PlanetTerrainSettings terrain)
    {
        if (farVertices == null || farVertices.Length == 0)
        {
            return VoxelQuadSphereTerrain.GetSurfaceNoise(
                direction * planetRadius,
                seed,
                terrain);
        }

        float bestDot = -2f;
        float bestHeight = 0f;
        for (int i = 0; i < farVertices.Length; i++)
        {
            Vector3 vertex = farVertices[i];
            float magnitude = vertex.magnitude;
            if (magnitude <= 0.001f)
                continue;
            float dot = Vector3.Dot(vertex / magnitude, direction);
            if (dot <= bestDot)
                continue;
            bestDot = dot;
            bestHeight = magnitude - planetRadius;
        }
        return bestHeight;
    }

    static void AddVertex(
        List<Vector3> vertices,
        List<Color> colors,
        Vector3 direction,
        float radius,
        float height,
        float colorRange,
        PlanetSurfaceLodTransitionBuildInput input)
    {
        vertices.Add(direction * radius);
        colors.Add(Color.Lerp(
            input.RockColor,
            input.SurfaceColor,
            Mathf.InverseLerp(-colorRange * 0.35f, colorRange, height)));
    }

    static void AddSkirt(
        List<Vector3> vertices,
        List<Color> colors,
        List<int> triangles,
        int topStart,
        int angularSegments,
        float skirtDepth,
        float planetRadius,
        float colorRange,
        PlanetSurfaceLodTransitionBuildInput input)
    {
        int duplicateTopStart = vertices.Count;
        for (int segment = 0; segment < angularSegments; segment++)
        {
            Vector3 top = vertices[topStart + segment];
            float height = top.magnitude - planetRadius;
            AddVertex(
                vertices,
                colors,
                top.normalized,
                top.magnitude,
                height,
                colorRange,
                input);
        }

        int bottomStart = vertices.Count;
        for (int segment = 0; segment < angularSegments; segment++)
        {
            Vector3 top = vertices[topStart + segment];
            float height = top.magnitude - planetRadius - skirtDepth;
            AddVertex(
                vertices,
                colors,
                top.normalized,
                top.magnitude - skirtDepth,
                height,
                colorRange,
                input);
        }

        for (int segment = 0; segment < angularSegments; segment++)
        {
            int next = (segment + 1) % angularSegments;
            int a = duplicateTopStart + segment;
            int b = duplicateTopStart + next;
            int c = bottomStart + segment;
            int d = bottomStart + next;
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(d);
        }
    }

    static float SmoothStep(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    static Vector3[] CalculateNormals(Vector3[] vertices, int[] triangles)
    {
        var normals = new Vector3[vertices.Length];
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            Vector3 normal = Vector3.Cross(
                vertices[b] - vertices[a],
                vertices[c] - vertices[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].sqrMagnitude > 0.000001f
                ? normals[i].normalized
                : vertices[i].normalized;
        return normals;
    }

    static void EnsureOutwardWinding(
        Vector3[] vertices,
        Vector3[] normals,
        int[] triangles)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].sqrMagnitude <= 0.001f
                || normals[i].sqrMagnitude <= 0.001f)
            {
                continue;
            }
            if (Vector3.Dot(vertices[i], normals[i]) >= 0f)
                return;

            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                int swap = triangles[triangle + 1];
                triangles[triangle + 1] = triangles[triangle + 2];
                triangles[triangle + 2] = swap;
            }
            return;
        }
    }

    static Bounds CalculateBounds(Vector3[] vertices)
    {
        if (vertices == null || vertices.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);
        Vector3 min = vertices[0];
        Vector3 max = vertices[0];
        for (int i = 1; i < vertices.Length; i++)
        {
            min = Vector3.Min(min, vertices[i]);
            max = Vector3.Max(max, vertices[i]);
        }
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }
}
