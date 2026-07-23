using System;
using NUnit.Framework;
using UnityEngine;

public sealed class InterstellarPlanetGenerationTests
{
    [Test]
    public void GeneratedPlanetsAreDeterministicAndDoNotGenerateRivers()
    {
        var firstGenerator = new ProceduralInterstellarGenerator(
            7319,
            Array.Empty<GalaxyResourceCatalogEntry>());
        var secondGenerator = new ProceduralInterstellarGenerator(
            7319,
            Array.Empty<GalaxyResourceCatalogEntry>());

        GalaxyPlanetDefinition first = firstGenerator.GeneratePlanet(InterstellarCoordinate.Zero);
        GalaxyPlanetDefinition second = secondGenerator.GeneratePlanet(InterstellarCoordinate.Zero);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(first.planetId, Is.EqualTo(second.planetId));
        Assert.That(first.seed, Is.EqualTo(second.seed));
        Assert.That(first.terrain.continentScale, Is.EqualTo(second.terrain.continentScale));
        Assert.That(first.celestial.surfaceGravity, Is.EqualTo(second.celestial.surfaceGravity));
        Assert.That(first.celestial.rotationPeriod, Is.EqualTo(second.celestial.rotationPeriod));
        Assert.That(first.celestial.surfaceGenerationMode,
            Is.EqualTo(PlanetSurfaceGenerationMode.StreamingLargeSphere));
        Assert.That(first.celestial.radius, Is.EqualTo(PlanetCelestialProfile.LargePlanetRadius));
        Assert.That(first.celestial.maximumTerrainElevation, Is.InRange(90f, 160f));
        Assert.That(first.celestial.rotationPeriod, Is.InRange(600f, 1200f));
        Assert.That(first.rivers, Is.Not.Null);
        Assert.That(first.rivers.enabled, Is.False);
        Assert.That(first.rivers.riverCount, Is.Zero);
    }

    [Test]
    public void LargePlanetConfiguresStreamingWithoutChangingLegacyPlanetDimensions()
    {
        GameObject worldObject = new GameObject("VoxelWorld");
        try
        {
            VoxelQuadSphereWorld world = worldObject.AddComponent<VoxelQuadSphereWorld>();
            PlanetCelestialProfile large = PlanetCelestialProfile.CreateLargeDefault();
            world.ConfigurePlanet(
                12345,
                null,
                Color.green,
                Color.gray,
                new PlanetTerrainSettings(),
                false,
                null,
                null,
                new PlanetRiverSettings { enabled = true, riverCount = 4 },
                null,
                large);

            Assert.That(world.IsStreamingLargePlanet, Is.True);
            Assert.That(world.PlanetRadius, Is.EqualTo(PlanetCelestialProfile.LargePlanetRadius));
            Assert.That(world.FaceGridSize, Is.EqualTo(2048));
            Assert.That(world.VoxelOuterRadius, Is.GreaterThan(world.PlanetRadius));

            PlanetCelestialProfile legacy = PlanetCelestialProfile.CreateCompatibleDefault();
            world.ConfigurePlanet(
                12345,
                null,
                Color.green,
                Color.gray,
                new PlanetTerrainSettings(),
                false,
                null,
                null,
                new PlanetRiverSettings(),
                null,
                legacy);

            Assert.That(world.IsStreamingLargePlanet, Is.False);
            Assert.That(world.PlanetRadius, Is.EqualTo(PlanetCelestialProfile.CompatibleRadius));
            Assert.That(world.FaceGridSize, Is.EqualTo(100));
            Assert.That(world.MaxDepth, Is.EqualTo(64));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(worldObject);
        }
    }

    [Test]
    public void CircularOrbitProducesBoundNearCircularState()
    {
        GameObject root = new GameObject("CelestialRoot");
        GameObject fieldObject = new GameObject("CelestialField");
        try
        {
            var profile = PlanetCelestialProfile.CreateCompatibleDefault();
            profile.surfaceGravity = 6f;
            profile.radius = 100f;
            profile.atmosphereSurfaceDensity = 0f;
            profile.ClampValues();

            CelestialGravityField field = fieldObject.AddComponent<CelestialGravityField>();
            field.Configure(root.transform, profile);
            float orbitalRadius = 140f;
            float circularSpeed = Mathf.Sqrt(profile.gravitationalParameter / orbitalRadius);
            OrbitalState state = field.CalculateOrbit(
                root.transform.position + Vector3.right * orbitalRadius,
                Vector3.forward * circularSpeed);

            Assert.That(state.isBound, Is.True);
            Assert.That(state.eccentricity, Is.LessThan(0.001f));
            Assert.That(state.periapsisAltitude, Is.EqualTo(40f).Within(0.02f));
            Assert.That(state.apoapsisAltitude, Is.EqualTo(40f).Within(0.02f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fieldObject);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void WarpDestinationStopsAtConfiguredDistanceFromPlanet()
    {
        var origin = new DoubleVector3(1200d, -3400d, 5600d);
        var target = new DoubleVector3(81200d, 46600d, 105600d);
        DoubleVector3 destination = InterstellarCruiseController.CalculateWarpDestination(
            origin,
            target,
            14000d);

        double x = target.x - destination.x;
        double y = target.y - destination.y;
        double z = target.z - destination.z;
        Assert.That(Math.Sqrt(x * x + y * y + z * z), Is.EqualTo(14000d).Within(0.01d));
    }

    [Test]
    public void WarpExitDirectionPointsFromOriginToTarget()
    {
        var origin = new DoubleVector3(-50000d, 2000d, 14000d);
        var target = new DoubleVector3(150000d, -18000d, 74000d);
        Vector3 direction = InterstellarCruiseController.CalculateTravelDirection(origin, target);

        Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.00001f));
        Assert.That(Vector3.Dot(direction, new Vector3(200000f, -20000f, 60000f).normalized),
            Is.GreaterThan(0.99999f));
    }

    [Test]
    public void PlanetApproachContextCarriesAutomaticLandingRequest()
    {
        var planet = new GalaxyPlanetDefinition { planetId = "automatic-landing-test" };
        try
        {
            PlanetApproachContext.Set(
                planet,
                Vector3.forward * 7000f,
                Vector3.forward * 120f,
                Quaternion.identity,
                87f,
                true);

            Assert.That(PlanetApproachContext.IsValid, Is.True);
            Assert.That(PlanetApproachContext.AutomaticLanding, Is.True);
            Assert.That(PlanetApproachContext.HullIntegrity, Is.EqualTo(87f));
        }
        finally
        {
            PlanetApproachContext.Clear();
        }
        Assert.That(PlanetApproachContext.AutomaticLanding, Is.False);
    }

    [Test]
    public void WarpEntryCorridorIsAboveAtmosphereAndInsideApproachBoundary()
    {
        PlanetCelestialProfile profile = PlanetCelestialProfile.CreateLargeDefault();
        double corridorDistance = InterstellarCruiseController.CalculateEntryCorridorDistance(profile);
        double atmosphereEdge = profile.radius + profile.atmosphereTopAltitude;
        double approachBoundary = profile.radius + 6000d;

        Assert.That(corridorDistance, Is.GreaterThan(atmosphereEdge));
        Assert.That(corridorDistance, Is.LessThan(approachBoundary));
    }

    [Test]
    public void ControlledApproachContextCarriesEntryDirectionAndCruiseHeight()
    {
        var planet = new GalaxyPlanetDefinition { planetId = "controlled-entry-test" };
        try
        {
            PlanetApproachContext.Set(
                planet,
                new Vector3(0f, 0f, 3000f),
                Vector3.right * 90f,
                Quaternion.identity,
                73f,
                false,
                Vector3.forward,
                475f,
                PlanetApproachEntryMode.ControlledAtmosphericEntry);

            Assert.That(PlanetApproachContext.EntryMode,
                Is.EqualTo(PlanetApproachEntryMode.ControlledAtmosphericEntry));
            Assert.That(PlanetApproachContext.EntryDirection, Is.EqualTo(Vector3.forward));
            Assert.That(PlanetApproachContext.TargetCruiseHeight, Is.EqualTo(475f));
        }
        finally
        {
            PlanetApproachContext.Clear();
        }
    }

    [Test]
    public void AerodynamicLiftFallsAfterStall()
    {
        float preStall = Mathf.Abs(PlanetAtmosphericFlightModel.CalculateLiftFactor(20f, 24f));
        float atStall = Mathf.Abs(PlanetAtmosphericFlightModel.CalculateLiftFactor(24f, 24f));
        float deepStall = Mathf.Abs(PlanetAtmosphericFlightModel.CalculateLiftFactor(50f, 24f));

        Assert.That(atStall, Is.GreaterThan(preStall));
        Assert.That(deepStall, Is.LessThan(atStall));
    }

    [Test]
    public void BackgroundFlightMesherMatchesTheLegacyChunkTopology()
    {
        const int gridSize = 128;
        const int maxDepth = 64;
        const float radius = 1100f;
        var key = new QuadSphereChunkKey(QuadSphereFace.PosZ, 3, 4, 1);

        byte Sample(QuadSphereVoxelAddress address)
        {
            if (address.U < 0 || address.V < 0 || address.Depth < 0
                || address.U >= gridSize || address.V >= gridSize || address.Depth >= maxDepth)
            {
                return VoxelTypes.Air;
            }

            int surface = 22 + ((address.U + address.V) & 3);
            if (address.Depth < surface)
                return VoxelTypes.Air;
            return address.Depth > surface + 2 ? VoxelTypes.Stone : VoxelTypes.Dirt;
        }

        int paddedSize = VoxelQuadSphereMesher.PaddedChunkSize;
        byte[] padded = new byte[paddedSize * paddedSize * paddedSize];
        for (int z = 0; z < paddedSize; z++)
        {
            for (int y = 0; y < paddedSize; y++)
            {
                for (int x = 0; x < paddedSize; x++)
                {
                    var address = new QuadSphereVoxelAddress(
                        key.Face,
                        key.ChunkU * VoxelTypes.ChunkSize + x - 1,
                        key.ChunkV * VoxelTypes.ChunkSize + y - 1,
                        key.ChunkDepth * VoxelTypes.ChunkSize + z - 1);
                    address = VoxelQuadSphereMapping.RemapAcrossFace(address, gridSize);
                    padded[x + y * paddedSize + z * paddedSize * paddedSize] = Sample(address);
                }
            }
        }

        Mesh legacy = VoxelQuadSphereMesher.BuildChunkMesh(
            Sample,
            key,
            gridSize,
            maxDepth,
            radius,
            Vector3.zero);
        try
        {
            VoxelQuadSphereMeshData background =
                VoxelQuadSphereMesher.BuildChunkMeshDataFromPaddedVoxels(
                    padded,
                    key,
                    gridSize,
                    radius,
                    Vector3.zero);

            try
            {
                Assert.That(background.Vertices, Is.EqualTo(legacy.vertices));
                Assert.That(background.SubMeshTriangles.Length, Is.EqualTo(legacy.subMeshCount));
                for (int subMesh = 0; subMesh < legacy.subMeshCount; subMesh++)
                    Assert.That(background.SubMeshTriangles[subMesh], Is.EqualTo(legacy.GetTriangles(subMesh)));
                Assert.That(background.Normals.Count, Is.EqualTo(legacy.normals.Length));
                for (int i = 0; i < background.Normals.Count; i++)
                    Assert.That(Vector3.Dot(background.Normals[i], legacy.normals[i]), Is.GreaterThan(0.9999f));
            }
            finally
            {
                background.Release();
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(legacy);
        }
    }

    [Test]
    public void FlightMeshColliderCanBePrebakedBeforeAssignment()
    {
        Mesh mesh = new Mesh();
        GameObject colliderObject = new GameObject("BackgroundBakeTest");
        try
        {
            mesh.vertices = new[]
            {
                Vector3.zero,
                Vector3.right,
                Vector3.forward,
                Vector3.up
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Physics.BakeMesh(
                mesh.GetInstanceID(),
                false,
                MeshColliderCookingOptions.CookForFasterSimulation
                | MeshColliderCookingOptions.EnableMeshCleaning
                | MeshColliderCookingOptions.WeldColocatedVertices
                | MeshColliderCookingOptions.UseFastMidphase);

            MeshCollider collider = colliderObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Assert.That(collider.sharedMesh, Is.SameAs(mesh));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(colliderObject);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
