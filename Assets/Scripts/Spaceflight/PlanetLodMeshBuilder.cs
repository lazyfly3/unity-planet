using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class PlanetLodMeshBuilder
{
    public static Mesh Build(GalaxyPlanetDefinition definition, int resolution = 18)
    {
        resolution = Mathf.Clamp(resolution, 6, 128);
        PlanetCelestialProfile celestial = definition.celestial ?? PlanetCelestialProfile.CreateCompatibleDefault();
        PlanetTerrainSettings terrain = definition.terrain ?? new PlanetTerrainSettings();
        float colorRange = Mathf.Max(8f, celestial.maximumTerrainElevation);
        var vertices = new List<Vector3>(6 * (resolution + 1) * (resolution + 1));
        var triangles = new List<int>(6 * resolution * resolution * 6);
        var colors = new List<Color>(vertices.Capacity);

        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            QuadSphereFace face = (QuadSphereFace)faceIndex;
            int start = vertices.Count;
            for (int y = 0; y <= resolution; y++)
            for (int x = 0; x <= resolution; x++)
            {
                float u = x / (float)resolution * 2f - 1f;
                float v = y / (float)resolution * 2f - 1f;
                Vector3 radial = VoxelQuadSphereMapping.CubeToSphere(
                    VoxelQuadSphereMapping.GetFaceCubePoint(face, u, v)).normalized;
                float height = VoxelQuadSphereTerrain.GetSurfaceNoise(
                    radial * celestial.radius,
                    definition.seed,
                    terrain);
                vertices.Add(radial * (celestial.radius + height));
                colors.Add(Color.Lerp(
                    definition.rockColor,
                    definition.surfaceColor,
                    Mathf.InverseLerp(-colorRange * 0.35f, colorRange, height)));
            }

            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int a = start + y * (resolution + 1) + x;
                int b = a + 1;
                int c = a + resolution + 1;
                int d = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        var mesh = new Mesh { name = "PlanetLod_" + definition.planetId };
        if (vertices.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh BuildOcean(float radius, int resolution = 48)
    {
        resolution = Mathf.Clamp(resolution, 8, 128);
        var vertices = new List<Vector3>(6 * (resolution + 1) * (resolution + 1));
        var normals = new List<Vector3>(vertices.Capacity);
        var triangles = new List<int>(6 * resolution * resolution * 6);

        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            QuadSphereFace face = (QuadSphereFace)faceIndex;
            int start = vertices.Count;
            for (int y = 0; y <= resolution; y++)
            for (int x = 0; x <= resolution; x++)
            {
                float u = x / (float)resolution * 2f - 1f;
                float v = y / (float)resolution * 2f - 1f;
                Vector3 radial = VoxelQuadSphereMapping.CubeToSphere(
                    VoxelQuadSphereMapping.GetFaceCubePoint(face, u, v)).normalized;
                vertices.Add(radial * radius);
                normals.Add(radial);
            }

            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int a = start + y * (resolution + 1) + x;
                int b = a + 1;
                int c = a + resolution + 1;
                int d = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        var mesh = new Mesh { name = "ProceduralPlanetOcean" };
        if (vertices.Count > 65535)
            mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
