using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
    public void LargePlanetBuildsARealColliderAtTheRequestedLandingDirection()
    {
        GameObject worldObject = new GameObject("VoxelWorld");
        try
        {
            VoxelQuadSphereWorld world = worldObject.AddComponent<VoxelQuadSphereWorld>();
            PlanetCelestialProfile large = PlanetCelestialProfile.CreateLargeDefault();
            Vector3 landingDirection = new Vector3(0.31f, 0.83f, -0.46f).normalized;
            world.ConfigurePlanet(
                24680,
                null,
                Color.green,
                Color.gray,
                new PlanetTerrainSettings(),
                false,
                null,
                null,
                new PlanetRiverSettings(),
                null,
                large);

            bool found = world.PrepareLandingSurface(
                landingDirection,
                out RaycastHit hit);

            string diagnostics = BuildSurfaceDiagnostics(world);
            Assert.That(found, Is.True,
                "The requested landing column must synchronously create high-detail terrain collision.\n"
                + diagnostics);
            Assert.That(hit.collider, Is.Not.Null);
            Assert.That(world.IsTerrainCollider(hit.collider), Is.True);
            Assert.That(world.IsInitialSurfaceReady, Is.True,
                "A live landing collider must make the high-detail surface ready.");
            Assert.That(world.PinnedLandingChunkCount, Is.GreaterThan(0),
                "The landing terrain must remain pinned after the initial surface is prepared.\n"
                + diagnostics);
            Assert.That(
                world.TryGetReadySurfaceCutout(out Vector3 cutoutCenter, out float cutoutRadius),
                Is.True,
                "Far LOD must not remain over the prepared high-detail landing terrain.\n"
                + diagnostics);
            Assert.That(cutoutRadius, Is.GreaterThan(0f));
            Assert.That(
                Vector3.Angle(
                    (cutoutCenter - world.GetPlanetCenterWorld()).normalized,
                    landingDirection),
                Is.LessThan(1f));
            Assert.That(
                Vector3.Angle(hit.point.normalized, landingDirection),
                Is.LessThan(1f));
            Assert.That(
                hit.point.magnitude,
                Is.InRange(
                    world.PlanetRadius - large.maximumTerrainElevation,
                    world.VoxelOuterRadius + 1f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(worldObject);
        }
    }

    [Test]
    public void CurrentSaveTerrainBuildsARealColliderAtItsLandingDirection()
    {
        GameObject worldObject = new GameObject("VoxelWorld");
        try
        {
            VoxelQuadSphereWorld world = worldObject.AddComponent<VoxelQuadSphereWorld>();
            var terrain = new PlanetTerrainSettings
            {
                continentScale = 0.000800157f,
                continentHeight = 78.8388f,
                detailScale = 0.009093739f,
                detailHeight = 17.6671f,
                ridgeHeight = 8.35927f,
                surfaceLayerDepth = 0.62038f,
                stoneDepth = 11.90096f,
                generateCaves = true,
                caveScale = 0.0289118f,
                caveThreshold = 0.704483f,
                caveSurfaceClearance = 8.65368f
            };
            PlanetCelestialProfile celestial = PlanetCelestialProfile.CreateLargeDefault();
            celestial.radius = 2000f;
            celestial.surfaceGravity = 3.87118f;
            celestial.maximumTerrainElevation = 92.16296f;
            celestial.editableDepth = 96f;
            celestial.ClampValues();
            Vector3 landingDirection = new Vector3(
                719.8912f,
                -724.0453f,
                -1729.424f).normalized;

            world.ConfigurePlanet(
                523540906,
                null,
                new Color(0.3f, 0.2f, 0.45f),
                Color.gray,
                terrain,
                false,
                null,
                null,
                new PlanetRiverSettings(),
                null,
                celestial);

            bool found = world.PrepareLandingSurface(
                landingDirection,
                out RaycastHit hit);

            string diagnostics = BuildSurfaceDiagnostics(world);
            Assert.That(found, Is.True,
                "The current save's landing column must create high-detail terrain collision.\n"
                + diagnostics);
            Assert.That(hit.collider, Is.Not.Null);
            Assert.That(world.IsTerrainCollider(hit.collider), Is.True);
            Assert.That(world.IsInitialSurfaceReady, Is.True,
                "The current save must expose its live high-detail landing collider.");
            Assert.That(world.PinnedLandingChunkCount, Is.GreaterThan(0),
                "The current save's landing terrain must stay loaded while the player is on it.\n"
                + diagnostics);
            Assert.That(
                world.TryGetReadySurfaceCutout(out Vector3 cutoutCenter, out float cutoutRadius),
                Is.True,
                "The current save must replace far LOD with high-detail terrain at landing.\n"
                + diagnostics);
            Assert.That(cutoutRadius, Is.GreaterThan(0f));
            Assert.That(
                Vector3.Angle(
                    (cutoutCenter - world.GetPlanetCenterWorld()).normalized,
                    landingDirection),
                Is.LessThan(1f));
            Assert.That(
                Vector3.Angle(hit.point.normalized, landingDirection),
                Is.LessThan(1f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(worldObject);
        }
    }

    [Test]
    public void CurrentSaveStartupPublishesReadyOnlyAfterHighDetailColliderExists()
    {
        GameObject worldObject = new GameObject("VoxelWorldStartup");
        try
        {
            VoxelQuadSphereWorld world = worldObject.AddComponent<VoxelQuadSphereWorld>();
            var terrain = new PlanetTerrainSettings
            {
                continentScale = 0.000800157f,
                continentHeight = 78.8388f,
                detailScale = 0.009093739f,
                detailHeight = 17.6671f,
                ridgeHeight = 8.35927f,
                surfaceLayerDepth = 0.62038f,
                stoneDepth = 11.90096f,
                generateCaves = true,
                caveScale = 0.0289118f,
                caveThreshold = 0.704483f,
                caveSurfaceClearance = 8.65368f
            };
            PlanetCelestialProfile celestial = PlanetCelestialProfile.CreateLargeDefault();
            celestial.radius = 2000f;
            celestial.surfaceGravity = 3.87118f;
            celestial.maximumTerrainElevation = 92.16296f;
            celestial.editableDepth = 96f;
            celestial.ClampValues();
            Vector3 landingDirection = new Vector3(
                719.8912f,
                -724.0453f,
                -1729.424f).normalized;

            world.ConfigurePlanet(
                523540906,
                null,
                new Color(0.3f, 0.2f, 0.45f),
                Color.gray,
                terrain,
                false,
                null,
                null,
                new PlanetRiverSettings(),
                null,
                celestial);
            world.SetSurfaceSpawnDirection(landingDirection);

            // The test only verifies startup ownership. Avoid launching the later
            // background expansion coroutine from an EditMode test.
            SetPrivateField(world, "streamingSurfaceCompletionStarted", true);
            MethodInfo startMethod = typeof(VoxelQuadSphereWorld).GetMethod(
                "Start",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(startMethod, Is.Not.Null);
            RunCoroutineSynchronously((IEnumerator)startMethod.Invoke(world, null));

            string diagnostics = BuildSurfaceDiagnostics(world);
            Assert.That(world.HasStartedSurfaceGeneration, Is.True);
            Assert.That(world.HasFinishedSurfaceGeneration, Is.True);
            Assert.That(world.InitialSurfaceGenerationFailed, Is.False, diagnostics);
            Assert.That(world.IsInitialSurfaceReady, Is.True, diagnostics);
            Assert.That(
                world.IsSurfaceEntryVisualReady,
                Is.True,
                "The entry blackout may only lift after the contiguous initial visual region is ready.\n"
                + diagnostics);
            Assert.That(world.TryFindInitialSurface(out RaycastHit hit), Is.True, diagnostics);
            Assert.That(world.IsTerrainCollider(hit.collider), Is.True, diagnostics);
            Assert.That(
                world.TryGetReadySurfaceCutout(out _, out float cutoutRadius),
                Is.True,
                diagnostics);
            Assert.That(cutoutRadius, Is.GreaterThan(0f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(worldObject);
        }
    }

    [Test]
    public void QuadSphereDirectionMappingRoundTripsSpherifiedCubeDirections()
    {
        const int gridSize = 2048;
        Vector3[] directions =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back,
            new Vector3(0.31f, 0.83f, -0.46f).normalized,
            new Vector3(-0.71f, 0.49f, 0.50f).normalized,
            new Vector3(1f, 0.99f, 0.98f).normalized,
            new Vector3(-1f, -0.98f, 0.99f).normalized
        };

        foreach (Vector3 direction in directions)
        {
            VoxelQuadSphereMapping.DirectionToFaceCell(
                direction,
                gridSize,
                out QuadSphereFace face,
                out int cellU,
                out int cellV);
            Vector3 reconstructed = VoxelQuadSphereMapping.GetRadialDirection(
                face,
                cellU,
                cellV,
                gridSize);

            Assert.That(
                Vector3.Angle(direction, reconstructed),
                Is.LessThan(0.1f),
                $"Direction {direction} mapped to {face} ({cellU}, {cellV}) " +
                $"but reconstructed as {reconstructed}.");
        }
    }

    static string BuildSurfaceDiagnostics(VoxelQuadSphereWorld world)
    {
        FieldInfo chunksField = typeof(VoxelQuadSphereWorld).GetField(
            "chunks",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var chunks = chunksField?.GetValue(world)
            as Dictionary<QuadSphereChunkKey, VoxelQuadSphereChunk>;
        if (chunks == null)
            return "Chunk diagnostics unavailable.";

        var result = new StringBuilder();
        result.Append("Loaded chunks: ").Append(chunks.Count);
        foreach (KeyValuePair<QuadSphereChunkKey, VoxelQuadSphereChunk> pair in chunks)
        {
            int solidCount = 0;
            byte[] voxels = pair.Value.Voxels;
            for (int i = 0; i < voxels.Length; i++)
            {
                if (VoxelTypes.IsSolid(voxels[i]))
                    solidCount++;
            }

            result.Append("\n")
                .Append(pair.Key)
                .Append(" solids=").Append(solidCount)
                .Append(" vertices=").Append(pair.Value.MeshVertexCount)
                .Append(" collider=").Append(pair.Value.HasCollider)
                .Append(" baked=").Append(pair.Value.HasBakedCollider);
        }

        return result.ToString();
    }

    static void RunCoroutineSynchronously(IEnumerator routine)
    {
        Assert.That(routine, Is.Not.Null);
        while (routine.MoveNext())
        {
            if (routine.Current is IEnumerator nested)
                RunCoroutineSynchronously(nested);
        }
    }

    static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field {fieldName}.");
        field.SetValue(target, value);
    }

    [Test]
    public void CircularOrbitProducesBoundNearCircularState()
    {
        GameObject root = new GameObject("CelestialRoot");
        GameObject fieldObject = new GameObject("CelestialField");
        try
        {
            var profile = PlanetCelestialProfile.CreateCompatibleDefault();
            profile.atmosphereSurfaceDensity = 0f;
            profile.ClampValues();

            CelestialGravityField field = fieldObject.AddComponent<CelestialGravityField>();
            field.Configure(root.transform, profile);
            float orbitalRadius = 140f;
            float circularSpeed = (float)profile.Physical.CircularOrbitSpeed(40d);
            OrbitalState state = field.CalculateOrbit(
                root.transform.position + Vector3.right * orbitalRadius,
                Vector3.forward * circularSpeed);

            Assert.That(state.isBound, Is.True);
            Assert.That(state.eccentricity, Is.LessThan(0.001f));
            Assert.That(state.periapsisAltitude, Is.EqualTo(40f).Within(2f));
            Assert.That(state.apoapsisAltitude, Is.EqualTo(40f).Within(2f));
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
    public void WarpEntryCorridorUsesPhysicalRadiusAndClearsAtmosphere()
    {
        PlanetCelestialProfile profile = PlanetCelestialProfile.CreateLargeDefault();
        double corridorDistance = InterstellarCruiseController.CalculateEntryCorridorDistance(profile);
        double atmosphereEdge = profile.Physical.radiusMeters
            + profile.Physical.atmosphereTopAltitudeMeters;
        double corridorAltitude = corridorDistance - profile.Physical.radiusMeters;

        Assert.That(corridorDistance, Is.GreaterThan(atmosphereEdge));
        Assert.That(corridorAltitude, Is.InRange(500_000d, 1_000_000d));
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
