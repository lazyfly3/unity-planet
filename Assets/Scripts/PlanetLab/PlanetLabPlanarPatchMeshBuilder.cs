using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlanetLabPlanarPatchBuildResult
{
    public Mesh terrainMesh;
    public Mesh oceanMesh;
    public Vector3 anchorDirection;
    public Vector3 spawnPosition;
    public float centerHeight;
    public float seaHeight;
}

public static class PlanetLabPlanarPatchMeshBuilder
{
    const int AnchorCandidateCount = 512;
    const float MaximumSpawnSlope = 25f;
    const float MinimumLandClearance = 2f;
    const float SpawnEyeClearance = 1.15f;
    const float GoldenAngle = 2.39996323f;

    public static PlanetLabPlanarPatchBuildResult Build(
        GalaxyPlanetDefinition definition,
        PlanetLabPlanarSettings settings,
        int resolution = PlanetLabPlanarSettings.PatchResolution)
    {
        if (definition == null)
            throw new System.ArgumentNullException(nameof(definition));

        settings = settings ?? new PlanetLabPlanarSettings();
        settings.ClampValues();
        resolution = Mathf.Clamp(resolution, 4, PlanetLabPlanarSettings.PatchResolution);

        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        PlanetTerrainSettings terrain = definition.terrain
            ?? new PlanetTerrainSettings();
        PlanetLowPolyVisualProfile visual = definition.lowPolyVisual
            ?? new PlanetLowPolyVisualProfile();
        float radius = Mathf.Max(1f, celestial.radius);
        float heightScale = Mathf.Max(1f, celestial.maximumTerrainElevation);
        float seaHeight = visual.oceanLevel * heightScale;
        Vector3 anchor = settings.autoAnchor
            ? FindBestLandAnchor(definition)
            : settings.AnchorDirection;
        BuildTangentBasis(anchor, out Vector3 east, out Vector3 north);

        int vertexSide = resolution + 1;
        var vertices = new List<Vector3>(vertexSide * vertexSide);
        var triangles = new List<int>(resolution * resolution * 6);
        var colors = new List<Color>(vertices.Capacity);
        var uvs = new List<Vector2>(vertices.Capacity);
        float halfSize = PlanetLabPlanarSettings.PatchSize * 0.5f;
        float centerHeight = SampleHeight(definition, anchor);

        for (int zIndex = 0; zIndex <= resolution; zIndex++)
        for (int xIndex = 0; xIndex <= resolution; xIndex++)
        {
            float x01 = xIndex / (float)resolution;
            float z01 = zIndex / (float)resolution;
            float x = Mathf.Lerp(-halfSize, halfSize, x01);
            float z = Mathf.Lerp(-halfSize, halfSize, z01);
            Vector3 sampleDirection = ProjectPlanarPointToSphere(
                anchor,
                east,
                north,
                radius,
                x,
                z);
            float height = SampleHeight(definition, sampleDirection);
            vertices.Add(new Vector3(x, height, z));
            colors.Add(Color.Lerp(
                definition.rockColor,
                definition.surfaceColor,
                Mathf.InverseLerp(-heightScale * 0.35f, heightScale, height)));
            uvs.Add(new Vector2(x01, z01));
        }

        for (int zIndex = 0; zIndex < resolution; zIndex++)
        for (int xIndex = 0; xIndex < resolution; xIndex++)
        {
            int a = zIndex * vertexSide + xIndex;
            int b = a + 1;
            int c = a + vertexSide;
            int d = c + 1;
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(d);
        }

        var terrainMesh = new Mesh
        {
            name = $"PlanetLabPlanarTerrain_{definition.seed}_{resolution}",
            hideFlags = HideFlags.HideAndDontSave
        };
        if (vertices.Count > 65535)
            terrainMesh.indexFormat = IndexFormat.UInt32;
        terrainMesh.SetVertices(vertices);
        terrainMesh.SetTriangles(triangles, 0);
        terrainMesh.SetColors(colors);
        terrainMesh.SetUVs(0, uvs);
        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();

        return new PlanetLabPlanarPatchBuildResult
        {
            terrainMesh = terrainMesh,
            oceanMesh = BuildOceanMesh(
                PlanetLabPlanarSettings.PatchSize,
                seaHeight,
                Mathf.Min(64, resolution)),
            anchorDirection = anchor,
            centerHeight = centerHeight,
            seaHeight = seaHeight,
            spawnPosition = new Vector3(0f, centerHeight + SpawnEyeClearance, 0f)
        };
    }

    public static Vector3 FindBestLandAnchor(GalaxyPlanetDefinition definition)
    {
        if (definition == null)
            return Vector3.up;

        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        PlanetLowPolyVisualProfile visual = definition.lowPolyVisual
            ?? new PlanetLowPolyVisualProfile();
        float seaHeight = visual.oceanLevel
            * Mathf.Max(1f, celestial.maximumTerrainElevation);
        Vector3 bestWalkable = Vector3.up;
        Vector3 bestOverall = Vector3.up;
        float bestWalkableHeight = float.NegativeInfinity;
        float bestOverallHeight = float.NegativeInfinity;

        for (int index = 0; index < AnchorCandidateCount; index++)
        {
            float y = 1f - 2f * ((index + 0.5f) / AnchorCandidateCount);
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = GoldenAngle * index;
            Vector3 direction = new Vector3(
                Mathf.Cos(angle) * radial,
                y,
                Mathf.Sin(angle) * radial);
            float height = SampleHeight(definition, direction);
            if (height > bestOverallHeight)
            {
                bestOverallHeight = height;
                bestOverall = direction;
            }

            if (height < seaHeight + MinimumLandClearance
                || EstimateSlope(definition, direction) > MaximumSpawnSlope
                || height <= bestWalkableHeight)
            {
                continue;
            }

            bestWalkableHeight = height;
            bestWalkable = direction;
        }

        return float.IsNegativeInfinity(bestWalkableHeight)
            ? bestOverall.normalized
            : bestWalkable.normalized;
    }

    public static Vector3 ProjectPlanarPointToSphere(
        Vector3 anchor,
        Vector3 east,
        Vector3 north,
        float radius,
        float x,
        float z)
    {
        anchor = anchor.sqrMagnitude > 0.0001f ? anchor.normalized : Vector3.up;
        return (anchor * Mathf.Max(1f, radius) + east * x + north * z).normalized;
    }

    public static void BuildTangentBasis(
        Vector3 anchor,
        out Vector3 east,
        out Vector3 north)
    {
        anchor = anchor.sqrMagnitude > 0.0001f ? anchor.normalized : Vector3.up;
        Vector3 reference = Mathf.Abs(Vector3.Dot(anchor, Vector3.up)) < 0.98f
            ? Vector3.up
            : Vector3.forward;
        east = Vector3.Cross(reference, anchor).normalized;
        north = Vector3.Cross(anchor, east).normalized;
    }

    static float EstimateSlope(
        GalaxyPlanetDefinition definition,
        Vector3 direction)
    {
        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        float radius = Mathf.Max(1f, celestial.radius);
        BuildTangentBasis(direction, out Vector3 east, out Vector3 north);
        const float sampleDistance = 4f;
        float westHeight = SampleHeight(
            definition,
            ProjectPlanarPointToSphere(
                direction, east, north, radius, -sampleDistance, 0f));
        float eastHeight = SampleHeight(
            definition,
            ProjectPlanarPointToSphere(
                direction, east, north, radius, sampleDistance, 0f));
        float southHeight = SampleHeight(
            definition,
            ProjectPlanarPointToSphere(
                direction, east, north, radius, 0f, -sampleDistance));
        float northHeight = SampleHeight(
            definition,
            ProjectPlanarPointToSphere(
                direction, east, north, radius, 0f, sampleDistance));
        float gradientX = (eastHeight - westHeight) / (sampleDistance * 2f);
        float gradientZ = (northHeight - southHeight) / (sampleDistance * 2f);
        return Mathf.Atan(Mathf.Sqrt(
            gradientX * gradientX + gradientZ * gradientZ)) * Mathf.Rad2Deg;
    }

    static float SampleHeight(
        GalaxyPlanetDefinition definition,
        Vector3 direction)
    {
        PlanetCelestialProfile celestial = definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        return VoxelQuadSphereTerrain.GetSurfaceNoise(
            direction.normalized * celestial.radius,
            definition.seed,
            definition.terrain);
    }

    static Mesh BuildOceanMesh(float size, float seaHeight, int resolution)
    {
        resolution = Mathf.Clamp(resolution, 2, 64);
        int vertexSide = resolution + 1;
        var vertices = new List<Vector3>(vertexSide * vertexSide);
        var normals = new List<Vector3>(vertices.Capacity);
        var uvs = new List<Vector2>(vertices.Capacity);
        var triangles = new List<int>(resolution * resolution * 6);
        float halfSize = size * 0.5f;

        for (int zIndex = 0; zIndex <= resolution; zIndex++)
        for (int xIndex = 0; xIndex <= resolution; xIndex++)
        {
            float x01 = xIndex / (float)resolution;
            float z01 = zIndex / (float)resolution;
            vertices.Add(new Vector3(
                Mathf.Lerp(-halfSize, halfSize, x01),
                seaHeight,
                Mathf.Lerp(-halfSize, halfSize, z01)));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(x01, z01));
        }

        for (int zIndex = 0; zIndex < resolution; zIndex++)
        for (int xIndex = 0; xIndex < resolution; xIndex++)
        {
            int a = zIndex * vertexSide + xIndex;
            int b = a + 1;
            int c = a + vertexSide;
            int d = c + 1;
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(b);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(d);
        }

        var mesh = new Mesh
        {
            name = $"PlanetLabPlanarOcean_{resolution}",
            hideFlags = HideFlags.HideAndDontSave
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.bounds = new Bounds(
            new Vector3(0f, seaHeight, 0f),
            new Vector3(size, 4f, size));
        return mesh;
    }
}
