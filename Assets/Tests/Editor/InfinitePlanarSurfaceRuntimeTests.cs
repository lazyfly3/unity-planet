using NUnit.Framework;
using SpacecraftEditor;
using UnityEngine;

public sealed class InfinitePlanarSurfaceRuntimeTests
{
    [TestCase(
        PlanetClimate.Desert,
        true,
        ProceduralPlanetLabTemplate.Desert)]
    [TestCase(
        PlanetClimate.Tundra,
        true,
        ProceduralPlanetLabTemplate.Frozen)]
    [TestCase(
        PlanetClimate.Volcanic,
        true,
        ProceduralPlanetLabTemplate.CrimsonOcean)]
    [TestCase(
        PlanetClimate.Crystal,
        false,
        ProceduralPlanetLabTemplate.Crystal)]
    [TestCase(
        PlanetClimate.TemperateForest,
        true,
        ProceduralPlanetLabTemplate.TemperateOcean)]
    [TestCase(
        PlanetClimate.Tropical,
        true,
        ProceduralPlanetLabTemplate.TemperateOcean)]
    [TestCase(
        PlanetClimate.Barren,
        true,
        ProceduralPlanetLabTemplate.CrimsonOcean)]
    [TestCase(
        PlanetClimate.Barren,
        false,
        ProceduralPlanetLabTemplate.Desert)]
    public void ClimateMappingUsesApprovedPlanetLabTemplate(
        PlanetClimate climate,
        bool oceanEnabled,
        ProceduralPlanetLabTemplate expected)
    {
        Assert.AreEqual(
            expected,
            PlanetLabPlanarMaterialFactory.ResolveTemplate(
                climate,
                oceanEnabled));
    }

    [Test]
    public void StableContentIdsContainChunkCoordinates()
    {
        string first =
            PlanarSurfaceContentStreamer.BuildStableInstanceId(
                "flora",
                new Vector2Int(7, -4),
                3);
        string repeated =
            PlanarSurfaceContentStreamer.BuildStableInstanceId(
                "flora",
                new Vector2Int(7, -4),
                3);
        string neighbor =
            PlanarSurfaceContentStreamer.BuildStableInstanceId(
                "flora",
                new Vector2Int(8, -4),
                3);

        Assert.AreEqual(first, repeated);
        Assert.AreNotEqual(first, neighbor);
        StringAssert.Contains("7:-4", first);
    }

    [Test]
    public void PlanarSpacecraftStateKeepsWorldHorizontalForward()
    {
        var state = new SurfaceSpacecraftState
        {
            valid = true,
            surfaceTopology =
                PlanetSurfaceTopology.InfinitePlanar,
            radialDirection = new Vector3(1f, 1f, 0f),
            tangentForward = new Vector3(2f, 7f, 3f)
        };

        state.ClampValues();

        Assert.That(
            Vector3.Dot(state.tangentForward, Vector3.up),
            Is.EqualTo(0f).Within(0.0001f));
        Assert.That(
            state.tangentForward.magnitude,
            Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void StreamerKeepsBoundedGridAfterGlobalOriginChange()
    {
        var root = new GameObject("PlanarStreamerTest");
        var target = new GameObject("PlanarStreamerTarget");
        ProceduralPlanetPreset preset =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        try
        {
            preset.ApplyTemplate(
                ProceduralPlanetLabTemplate.TemperateOcean);
            GalaxyPlanetDefinition definition =
                preset.CloneDefinition();
            var settings = new PlanetLabPlanarSettings
            {
                autoAnchor = false
            };
            settings.SetAnchorDirection(Vector3.up);
            PlanetLabInfiniteTerrainStreamer streamer =
                root.AddComponent<
                    PlanetLabInfiniteTerrainStreamer>();
            streamer.Configure(
                definition,
                settings,
                target.transform,
                null,
                null,
                false);

            streamer.SetGlobalOrigin(2560d, -1280d);

            Assert.AreEqual(
                new Vector2Int(20, -10),
                streamer.CenterCoordinate);
            Assert.AreEqual(25, streamer.ActiveChunkCount);
            Assert.IsTrue(streamer.IsCenterChunkReady);
            Assert.IsTrue(streamer.IsFullyReady);
            Assert.IsTrue(streamer.TrySampleSurface(
                2560d,
                -1280d,
                out float height,
                out Vector3 normal));
            Assert.IsTrue(float.IsFinite(height));
            Assert.That(normal.magnitude, Is.EqualTo(1f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void SaveMetadataDefaultsPreserveLegacyCompatibility()
    {
        var metadata = new GalaxySaveSlotMetadata();

        Assert.AreEqual(9, metadata.formatVersion);
        Assert.AreEqual(
            PlanetSurfaceTopology.LegacySphere,
            metadata.surfaceTopology);
    }

    [Test]
    public void SurfaceAtmosphereDensityUsesPlanetPhysicalProfile()
    {
        PlanetCelestialProfile profile =
            PlanetCelestialProfile.CreateCompatibleDefault();
        float surface =
            PlanetSurfaceFlightEnvironment.CalculateAtmosphereDensity(
                profile,
                0f);
        float oneScaleHeight =
            PlanetSurfaceFlightEnvironment.CalculateAtmosphereDensity(
                profile,
                (float)profile.Physical.atmosphereScaleHeightMeters);

        Assert.Greater(surface, 0f);
        Assert.That(
            oneScaleHeight / surface,
            Is.EqualTo(Mathf.Exp(-1f)).Within(0.001f));

        profile.Physical.atmosphereSurfaceDensityKgPerCubicMeter = 0d;
        profile.Physical.atmosphereTopAltitudeMeters = 0d;
        Assert.AreEqual(
            0f,
            PlanetSurfaceFlightEnvironment.CalculateAtmosphereDensity(
                profile,
                0f));
    }

    [Test]
    public void AerodynamicDragOpposesVelocityAndScalesWithSpeedSquared()
    {
        Vector3 slow =
            PlanetSurfaceFlightEnvironment.CalculateDragAcceleration(
                Vector3.forward * 10f,
                1.2f,
                0.2f,
                20f,
                1000f,
                100f);
        Vector3 fast =
            PlanetSurfaceFlightEnvironment.CalculateDragAcceleration(
                Vector3.forward * 20f,
                1.2f,
                0.2f,
                20f,
                1000f,
                100f);

        Assert.Less(Vector3.Dot(slow, Vector3.forward), 0f);
        Assert.That(
            fast.magnitude / slow.magnitude,
            Is.EqualTo(4f).Within(0.001f));
    }

    [Test]
    public void IfcsAcceptsPlanetaryEnvironmentAcceleration()
    {
        var root = new GameObject("IfcsEnvironmentTest");
        try
        {
            root.AddComponent<Rigidbody>();
            SpacecraftIfcsMotor motor =
                root.AddComponent<SpacecraftIfcsMotor>();
            Vector3 gravity = new Vector3(0f, -6.2f, 0f);

            motor.SetEnvironmentalAcceleration(gravity);

            Assert.AreEqual(
                gravity,
                motor.EnvironmentalAccelerationWorld);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
