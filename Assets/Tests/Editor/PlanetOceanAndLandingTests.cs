using NUnit.Framework;
using UnityEngine;

public sealed class PlanetOceanAndLandingTests
{
    sealed class FixedSampler : IPlanetWaterSampler
    {
        readonly WaterSample value;
        public FixedSampler(WaterSample sample) => value = sample;
        public bool TrySample(Vector3 worldPosition, out WaterSample sample)
        {
            sample = value;
            return true;
        }
    }

    [Test]
    public void AnimatedOceanProfileUpgradesFromVersionTwo()
    {
        var visual = new PlanetLowPolyVisualProfile
        {
            visualVersion = 2,
            oceanWaveScale = 0f,
            oceanWaveSpeed = 0f,
            oceanOpacity = 0f
        };

        visual.ClampValues();

        Assert.AreEqual(PlanetLowPolyVisualProfile.CurrentVersion, visual.visualVersion);
        Assert.AreEqual(1f, visual.oceanWaveScale);
        Assert.AreEqual(1f, visual.oceanWaveSpeed);
        Assert.AreEqual(0.82f, visual.oceanOpacity);
    }

    [Test]
    public void LandingResolverIsDeterministicAndFindsLandAndOcean()
    {
        GalaxyPlanetDefinition planet = CreatePlanet();
        bool foundTerrain = false;
        bool foundOcean = false;
        for (int i = 0; i < 512; i++)
        {
            Vector3 direction = FibonacciDirection(i, 512);
            PlanetLandingResolution first =
                PlanetLandingSiteResolver.Resolve(planet, direction);
            PlanetLandingResolution second =
                PlanetLandingSiteResolver.Resolve(planet, direction);
            Assert.AreEqual(first.mode, second.mode);
            Assert.AreEqual(first.direction, second.direction);
            foundTerrain |= first.mode == PlanetLandingMode.Terrain;
            foundOcean |= first.mode == PlanetLandingMode.OceanPlatform;
        }

        Assert.IsTrue(foundTerrain, "The test planet should contain a terrain landing area.");
        Assert.IsTrue(foundOcean, "The test planet should contain an ocean platform area.");
    }

    [Test]
    public void WaterRegistrySelectsNearestSurface()
    {
        var far = new FixedSampler(new WaterSample
        {
            signedDistance = -3f,
            depth = 8f,
            kind = PlanetWaterKind.River
        });
        var near = new FixedSampler(new WaterSample
        {
            signedDistance = -0.4f,
            depth = 10f,
            kind = PlanetWaterKind.Ocean
        });
        PlanetWaterRegistry.Register(far);
        PlanetWaterRegistry.Register(near);
        try
        {
            Assert.IsTrue(PlanetWaterRegistry.TrySampleAny(Vector3.zero, out WaterSample sample));
            Assert.AreEqual(PlanetWaterKind.Ocean, sample.kind);
        }
        finally
        {
            PlanetWaterRegistry.Unregister(far);
            PlanetWaterRegistry.Unregister(near);
        }
    }

    [Test]
    public void OceanSamplerOnlyReportsWaterOverASeaBasin()
    {
        GalaxyPlanetDefinition planet = CreatePlanet();
        Vector3 waterDirection = Vector3.zero;
        Vector3 landDirection = Vector3.zero;
        for (int i = 0; i < 512; i++)
        {
            Vector3 direction = FibonacciDirection(i, 512);
            PlanetLandingMode mode =
                PlanetLandingSiteResolver.Resolve(planet, direction, 0f).mode;
            if (mode == PlanetLandingMode.OceanPlatform
                && waterDirection == Vector3.zero)
            {
                waterDirection = direction;
            }
            if (mode == PlanetLandingMode.Terrain
                && landDirection == Vector3.zero)
            {
                landDirection = direction;
            }
        }

        var root = new GameObject("OceanSamplerTest");
        ProceduralPlanetOcean ocean = root.AddComponent<ProceduralPlanetOcean>();
        try
        {
            ocean.Configure(planet, Vector3.zero, 8, PlanetOceanRenderMode.Orbital);
            Assert.IsTrue(ocean.TrySample(
                waterDirection * ocean.OceanRadius,
                out WaterSample water));
            Assert.AreEqual(PlanetWaterKind.Ocean, water.kind);
            Assert.IsFalse(ocean.TrySample(
                landDirection * ocean.OceanRadius,
                out _));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PlatformContainsShipMarginAndStableDeckCollider()
    {
        Bounds shipBounds = new Bounds(Vector3.zero, new Vector3(20f, 6f, 12f));
        ProceduralOceanLandingPlatform platform =
            ProceduralOceanLandingPlatform.Build(
                shipBounds,
                new OceanLandingPose(
                    Vector3.zero,
                    Vector3.up,
                    Vector3.forward,
                    100f,
                    1f));
        try
        {
            Assert.That(platform.DeckSize.x, Is.GreaterThanOrEqualTo(28f));
            Assert.That(platform.DeckSize.y, Is.GreaterThanOrEqualTo(24f));
            Assert.That(platform.DeckSurfacePoint.y, Is.EqualTo(102.2f).Within(0.001f));
            Assert.AreEqual(1, platform.GetComponentsInChildren<BoxCollider>(true).Length);
            Assert.Greater(
                Vector3.Dot(platform.PlayerSpawnPoint - platform.DeckSurfacePoint, Vector3.up),
                1f);
        }
        finally
        {
            Object.DestroyImmediate(platform.gameObject);
        }
    }

    [Test]
    public void BothOceanQualityShadersAreAvailable()
    {
        Assert.IsNotNull(Shader.Find("VoxelPlanet/ProceduralOcean"));
        Assert.IsNotNull(Shader.Find("VoxelPlanet/ProceduralOceanSurface"));
        Assert.IsNotNull(Shader.Find("Hidden/Voxel Planet/Underwater Screen"));
    }

    [Test]
    public void ActualShipBoundsExpandLandingFootprintWithSafetyMargin()
    {
        Bounds largeShip = new Bounds(
            Vector3.zero,
            new Vector3(40f, 8f, 30f));

        float footprint = PlanetLandingSiteResolver.CalculateFootprintRadius(
            largeShip,
            Vector3.up);

        Assert.That(
            footprint,
            Is.GreaterThanOrEqualTo(Mathf.Sqrt(20f * 20f + 15f * 15f) + 4f));
    }

    [Test]
    public void ExplicitOceanPlatformCanNeverDowngradeToTerrain()
    {
        GalaxyPlanetDefinition planet = CreatePlanet();
        Vector3 terrainDirection = Vector3.up;
        for (int i = 0; i < 512; i++)
        {
            Vector3 candidate = FibonacciDirection(i, 512);
            if (PlanetLandingSiteResolver.Resolve(
                    planet,
                    candidate,
                    0f).mode == PlanetLandingMode.Terrain)
            {
                terrainDirection = candidate;
                break;
            }
        }

        PlanetLandingResolution resolution =
            PlanetLandingSiteResolver.ResolveLoadedSurface(
                null,
                planet,
                terrainDirection,
                new Bounds(Vector3.zero, Vector3.one * 8f),
                PlanetLandingMode.OceanPlatform);

        Assert.AreEqual(PlanetLandingMode.OceanPlatform, resolution.mode);
        Assert.That(
            Vector3.Dot(terrainDirection.normalized, resolution.direction),
            Is.GreaterThan(0.9999f));
    }

    [Test]
    public void UnderwaterHysteresisPreventsWaterlineFlicker()
    {
        Assert.IsFalse(WaterCameraEffects.ResolveUnderwaterState(
            false,
            true,
            -0.1f));
        Assert.IsTrue(WaterCameraEffects.ResolveUnderwaterState(
            false,
            true,
            -0.2f));
        Assert.IsTrue(WaterCameraEffects.ResolveUnderwaterState(
            true,
            true,
            0.1f));
        Assert.IsFalse(WaterCameraEffects.ResolveUnderwaterState(
            true,
            true,
            0.2f));
        Assert.IsFalse(WaterCameraEffects.ResolveUnderwaterState(
            true,
            false,
            -10f));
    }

    [Test]
    public void PlayerCapsuleSubmersionUsesBodyHeightInsteadOfOceanDepth()
    {
        const float capsuleHeight = 2f;

        Assert.That(
            VoxelPlanetPlayerController.CalculateCapsuleSubmersion(
                1f,
                capsuleHeight),
            Is.EqualTo(0f).Within(0.0001f));
        Assert.That(
            VoxelPlanetPlayerController.CalculateCapsuleSubmersion(
                0f,
                capsuleHeight),
            Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(
            VoxelPlanetPlayerController.CalculateCapsuleSubmersion(
                -1f,
                capsuleHeight),
            Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void PlayerSwimmingStateUsesEntryAndExitHysteresis()
    {
        Assert.IsFalse(VoxelPlanetPlayerController.ResolveSwimmingState(
            false,
            true,
            0.09f));
        Assert.IsTrue(VoxelPlanetPlayerController.ResolveSwimmingState(
            false,
            true,
            0.1f));
        Assert.IsTrue(VoxelPlanetPlayerController.ResolveSwimmingState(
            true,
            true,
            0.05f));
        Assert.IsFalse(VoxelPlanetPlayerController.ResolveSwimmingState(
            true,
            true,
            0.04f));
        Assert.IsFalse(VoxelPlanetPlayerController.ResolveSwimmingState(
            true,
            false,
            1f));
    }

    [Test]
    public void PlayerSwimVerticalTargetsMatchMinecraftStyleControls()
    {
        const float sink = 0.55f;
        const float ascend = 3f;
        const float descend = 2.5f;

        Assert.That(
            VoxelPlanetPlayerController.GetSwimVerticalTarget(
                false,
                false,
                sink,
                ascend,
                descend),
            Is.EqualTo(-sink).Within(0.0001f));
        Assert.That(
            VoxelPlanetPlayerController.GetSwimVerticalTarget(
                true,
                false,
                sink,
                ascend,
                descend),
            Is.EqualTo(ascend).Within(0.0001f));
        Assert.That(
            VoxelPlanetPlayerController.GetSwimVerticalTarget(
                false,
                true,
                sink,
                ascend,
                descend),
            Is.EqualTo(-descend).Within(0.0001f));
        Assert.That(
            VoxelPlanetPlayerController.GetSwimVerticalTarget(
                true,
                true,
                sink,
                ascend,
                descend),
            Is.EqualTo(-sink).Within(0.0001f));
    }

    [Test]
    public void SurfaceLodTransitionRingIsClosedFiniteAndOutwardFacing()
    {
        GalaxyPlanetDefinition planet = CreatePlanet();
        Mesh farMesh = PlanetLodMeshBuilder.Build(planet, 12);
        Mesh transition = PlanetSurfaceLodTransitionMeshBuilder.Build(
            planet,
            farMesh,
            new Vector3(0.3f, 0.9f, -0.2f),
            18f,
            42f,
            48,
            4,
            12f);
        try
        {
            Assert.IsNotNull(transition);
            Assert.Greater(transition.vertexCount, 48 * 5);
            Assert.Greater(transition.triangles.Length, 48 * 4 * 6);
            Assert.AreEqual(
                transition.vertexCount,
                transition.colors.Length);

            Vector3[] vertices = transition.vertices;
            Vector3[] normals = transition.normals;
            int[] triangles = transition.triangles;
            for (int i = 0; i < vertices.Length; i++)
            {
                Assert.IsTrue(float.IsFinite(vertices[i].x));
                Assert.IsTrue(float.IsFinite(vertices[i].y));
                Assert.IsTrue(float.IsFinite(vertices[i].z));
            }
            for (int i = 0; i < triangles.Length; i++)
            {
                Assert.That(triangles[i], Is.InRange(0, vertices.Length - 1));
            }

            int outwardSamples = 0;
            int mainVertexCount = 48 * 5;
            for (int i = 0; i < mainVertexCount; i += 11)
            {
                if (normals[i].sqrMagnitude < 0.001f)
                    continue;
                Assert.Greater(
                    Vector3.Dot(vertices[i].normalized, normals[i]),
                    0f);
                outwardSamples++;
            }
            Assert.Greater(outwardSamples, 8);
        }
        finally
        {
            Object.DestroyImmediate(transition);
            Object.DestroyImmediate(farMesh);
        }
    }

    [Test]
    public void SurfaceLodTransitionDataBuildsOffThreadAndReusesMesh()
    {
        GalaxyPlanetDefinition planet = CreatePlanet();
        Mesh farMesh = PlanetLodMeshBuilder.Build(planet, 12);
        Mesh transition = null;
        try
        {
            PlanetSurfaceLodTransitionBuildInput input =
                PlanetSurfaceLodTransitionMeshBuilder.CaptureInput(
                    planet,
                    farMesh.vertices,
                    new Vector3(-0.2f, 0.7f, 0.5f),
                    20f,
                    46f,
                    48,
                    4,
                    12f);
            PlanetSurfaceLodTransitionMeshData data =
                System.Threading.Tasks.Task.Run(
                    () => PlanetSurfaceLodTransitionMeshBuilder.BuildData(input))
                .GetAwaiter()
                .GetResult();

            transition =
                PlanetSurfaceLodTransitionMeshBuilder.ApplyData(
                    data,
                    transition);
            Mesh reused =
                PlanetSurfaceLodTransitionMeshBuilder.ApplyData(
                    data,
                    transition);

            Assert.IsNotNull(data);
            Assert.AreSame(transition, reused);
            Assert.AreEqual(data.Vertices.Length, transition.vertexCount);
            Assert.AreEqual(data.Triangles.Length, transition.triangles.Length);
        }
        finally
        {
            Object.DestroyImmediate(transition);
            Object.DestroyImmediate(farMesh);
        }
    }

    static GalaxyPlanetDefinition CreatePlanet()
    {
        return new GalaxyPlanetDefinition
        {
            planetId = "ocean-test",
            seed = 74129,
            celestial = new PlanetCelestialProfile
            {
                radius = 100f,
                maximumTerrainElevation = 30f
            },
            terrain = new PlanetTerrainSettings
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
            },
            lowPolyVisual = new PlanetLowPolyVisualProfile
            {
                oceanEnabled = true,
                oceanLevel = 0f
            }
        };
    }

    static Vector3 FibonacciDirection(int index, int count)
    {
        float y = 1f - (index + 0.5f) * 2f / count;
        float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        float angle = index * 2.39996323f;
        return new Vector3(
            Mathf.Cos(angle) * radius,
            y,
            Mathf.Sin(angle) * radius);
    }
}
