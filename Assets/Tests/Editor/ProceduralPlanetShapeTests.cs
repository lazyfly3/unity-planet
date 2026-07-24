using NUnit.Framework;
using UnityEngine;

public sealed class ProceduralPlanetShapeTests
{
    [Test]
    public void LayeredShapeIsDeterministicAndContainsLandAndOcean()
    {
        var settings = new PlanetTerrainSettings
        {
            shapeVersion = PlanetTerrainSettings.CurrentShapeVersion,
            continentScale = 0.012f,
            continentHeight = 30f,
            detailScale = 0.055f,
            detailHeight = 8f,
            ridgeHeight = 22f,
            continentThreshold = 0.51f,
            continentWarp = 0.8f,
            continentSharpness = 1.2f,
            mountainMask = 0.52f,
            oceanFloorDepth = 0.7f
        };

        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        const int sampleCount = 512;
        for (int index = 0; index < sampleCount; index++)
        {
            float y = 1f - (index + 0.5f) * 2f / sampleCount;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = index * 2.39996323f;
            Vector3 direction = new Vector3(
                Mathf.Cos(angle) * radius,
                y,
                Mathf.Sin(angle) * radius);
            float first = VoxelQuadSphereTerrain.GetSurfaceNoise(
                direction * 100f,
                74129,
                settings);
            float second = VoxelQuadSphereTerrain.GetSurfaceNoise(
                direction * 100f,
                74129,
                settings);
            Assert.AreEqual(first, second);
            minimum = Mathf.Min(minimum, first);
            maximum = Mathf.Max(maximum, first);
        }

        Assert.Less(minimum, -1f, "The profile should create an ocean basin.");
        Assert.Greater(maximum, 5f, "The profile should create raised continents.");
    }

    [Test]
    public void ShapeAndShadingProfilesSupportAlienOceanPalette()
    {
        var visual = new PlanetLowPolyVisualProfile
        {
            oceanEnabled = true,
            deepOceanColor = new Color(0.2f, 0.01f, 0.07f, 1f),
            shallowOceanColor = new Color(0.72f, 0.06f, 0.2f, 1f),
            lowlandColor = new Color(0.48f, 0.22f, 0.14f, 1f),
            snowLine = 0.58f,
            snowAmount = 0.72f
        };
        visual.ClampValues();

        Assert.IsTrue(visual.oceanEnabled);
        Assert.Greater(visual.shallowOceanColor.r, visual.shallowOceanColor.b);
        Assert.Greater(visual.snowAmount, 0.5f);
        Assert.AreEqual(PlanetLowPolyVisualProfile.CurrentVersion, visual.visualVersion);
    }

    [Test]
    public void OceanMeshUsesRequestedRadius()
    {
        Mesh mesh = PlanetLodMeshBuilder.BuildOcean(100f, 12);
        try
        {
            Assert.Greater(mesh.vertexCount, 0);
            foreach (Vector3 vertex in mesh.vertices)
                Assert.That(vertex.magnitude, Is.EqualTo(100f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(mesh);
        }
    }
}
