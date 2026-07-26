using System.Collections.Generic;
using System.Linq;
using CityGeneration;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CityGenerationTests
{
    static readonly CityGenerationSettings Settings = new CityGenerationSettings();

    [Test]
    public void BoundaryValidationAcceptsTriangleQuadrilateralAndConcavePolygon()
    {
        AssertValid(new[]
        {
            new Vector2(-50f, -40f),
            new Vector2(50f, -40f),
            new Vector2(0f, 60f)
        });
        AssertValid(new[]
        {
            new Vector2(-60f, -60f),
            new Vector2(60f, -60f),
            new Vector2(60f, 60f),
            new Vector2(-60f, 60f)
        });
        AssertValid(CreateConcaveBoundary());
    }

    [Test]
    public void BoundaryValidationAcceptsRepeatedJunctionAndRejectsDegenerateArea()
    {
        AssertValid(new[]
        {
            new Vector2(-40f, -40f),
            new Vector2(40f, -40f),
            new Vector2(40f, 40f),
            new Vector2(-40f, -40f)
        });
        AssertInvalid(new[]
        {
            new Vector2(-40f, 0f),
            new Vector2(0f, 0f),
            new Vector2(40f, 0f)
        });
    }

    [Test]
    public void SelfIntersectingBowTieResolvesIntoTwoTriangles()
    {
        Vector2[] boundary = CreateBowTieBoundary();

        Assert.IsTrue(
            CityPolygonGeometry.ResolveClosedRegions(
                boundary,
                Settings,
                out List<List<Vector2>> regions,
                out string error),
            error);
        Assert.AreEqual(2, regions.Count);
        Assert.IsTrue(regions.All(region => region.Count == 3));
        Assert.IsTrue(regions.All(
            region => CityPolygonGeometry.SignedArea(region) > Settings.minimumBoundaryArea));
    }

    [Test]
    public void MultiIntersectionStarResolvesDeterministicallyAndGenerates()
    {
        Vector2[] boundary = CreateFivePointStarBoundary();

        Assert.IsTrue(
            CityPolygonGeometry.ResolveBoundary(
                boundary,
                Settings,
                out CityBoundaryResolution first,
                out string error),
            error);
        Assert.IsTrue(
            CityPolygonGeometry.ResolveBoundary(
                boundary,
                Settings,
                out CityBoundaryResolution second,
                out error),
            error);
        Assert.Greater(first.IntersectionCount, 1);
        Assert.Greater(first.Regions.Count, 1);
        Assert.AreEqual(first.Regions.Count, second.Regions.Count);
        for (int i = 0; i < first.Regions.Count; i++)
            CollectionAssert.AreEqual(first.Regions[i], second.Regions[i]);

        CityGenerationResult result =
            new CityGenerator().Generate(boundary, Settings, 7319);
        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.Greater(result.Buildings.Count, 0);
        foreach (CityBuildingData building in result.Buildings)
        {
            AssertInsideAnyRegion(
                result.Regions,
                building.Footprint,
                "star building");
        }
    }

    [Test]
    public void RepeatedAndOverlappingSegmentsRemainUsable()
    {
        Vector2[] boundary =
        {
            new Vector2(-80f, -80f),
            new Vector2(80f, -80f),
            new Vector2(80f, 80f),
            new Vector2(-80f, 80f),
            new Vector2(-80f, -80f),
            new Vector2(0f, -80f),
            new Vector2(80f, -80f)
        };

        Assert.IsTrue(
            CityPolygonGeometry.ResolveBoundary(
                boundary,
                Settings,
                out CityBoundaryResolution resolution,
                out string error),
            error);
        Assert.AreEqual(1, resolution.Regions.Count);
        Assert.IsFalse(resolution.WasAutoRepaired);
    }

    [Test]
    public void NarrowClosedFragmentIsIgnoredWithoutRejectingLargeRegion()
    {
        Vector2[] boundary =
        {
            new Vector2(-80f, -80f),
            new Vector2(80f, -80f),
            new Vector2(80f, 80f),
            new Vector2(-80f, 80f),
            new Vector2(-80f, -80f),
            new Vector2(-86f, -80f),
            new Vector2(-83f, -75f),
            new Vector2(-80f, -80f)
        };

        Assert.IsTrue(
            CityPolygonGeometry.ResolveBoundary(
                boundary,
                Settings,
                out CityBoundaryResolution resolution,
                out string error),
            error);
        Assert.AreEqual(1, resolution.Regions.Count);
        Assert.GreaterOrEqual(resolution.IgnoredRegions.Count, 1);
    }

    [Test]
    public void BacktrackedOpenGraphUsesAutomaticHull()
    {
        Vector2[] boundary =
        {
            new Vector2(-100f, -60f),
            new Vector2(100f, -60f),
            new Vector2(0f, 120f),
            new Vector2(100f, -60f),
            new Vector2(-100f, -60f)
        };

        Assert.IsTrue(
            CityPolygonGeometry.ResolveBoundary(
                boundary,
                Settings,
                out CityBoundaryResolution resolution,
                out string error),
            error);
        Assert.IsTrue(resolution.WasAutoRepaired);
        Assert.AreEqual(1, resolution.Regions.Count);
        Assert.IsFalse(
            CityPolygonGeometry.HasSelfIntersection(
                resolution.Regions[0]));
    }

    [Test]
    public void GeneratorIsDeterministicForSameBoundaryAndSeed()
    {
        Vector2[] boundary = CreateSquareBoundary();
        var generator = new CityGenerator();
        CityGenerationResult first = generator.Generate(boundary, Settings, 7781);
        CityGenerationResult second = generator.Generate(boundary, Settings, 7781);

        Assert.IsTrue(first.IsSuccess, first.Error);
        Assert.IsTrue(second.IsSuccess, second.Error);
        Assert.AreEqual(first.Roads.Count, second.Roads.Count);
        Assert.AreEqual(first.Blocks.Count, second.Blocks.Count);
        Assert.AreEqual(first.Buildings.Count, second.Buildings.Count);
        for (int i = 0; i < first.Roads.Count; i++)
        {
            Assert.AreEqual(first.Roads[i].Start, second.Roads[i].Start);
            Assert.AreEqual(first.Roads[i].End, second.Roads[i].End);
            Assert.AreEqual(first.Roads[i].Width, second.Roads[i].Width);
            Assert.AreEqual(first.Roads[i].IsMajor, second.Roads[i].IsMajor);
        }
        for (int i = 0; i < first.Buildings.Count; i++)
        {
            Assert.AreEqual(first.Buildings[i].Height, second.Buildings[i].Height);
            CollectionAssert.AreEqual(
                first.Buildings[i].Footprint,
                second.Buildings[i].Footprint);
        }
    }

    [Test]
    public void OrganicGeneratorCreatesVariedDirectionsAndLeavesOpenLots()
    {
        Vector2[] boundary = CreateSquareBoundary();
        CityGenerationResult result =
            new CityGenerator().Generate(boundary, Settings, 9147);

        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.Greater(result.Roads.Count, 12);
        Assert.Greater(result.Lots.Count, result.Buildings.Count);

        int distinctDirectionBuckets = result.Roads
            .Select(road =>
            {
                Vector2 direction = (road.End - road.Start).normalized;
                float angle = Mathf.Repeat(
                    Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg,
                    180f);
                return Mathf.RoundToInt(angle / 10f);
            })
            .Distinct()
            .Count();
        Assert.GreaterOrEqual(distinctDirectionBuckets, 4);

        Assert.IsTrue(result.Blocks.Any(block =>
            !HasOnlyRightAngleCorners(block.Footprint)));
        Assert.IsTrue(result.Buildings.Any(
            building => building.Footprint.Count > 4));
    }

    [Test]
    public void GeneratedRoadsBlocksLotsAndBuildingsStayInsideBoundary()
    {
        Vector2[] boundary = CreateConcaveBoundary();
        CityGenerationResult result =
            new CityGenerator().Generate(boundary, Settings, 1927);

        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.Greater(result.Roads.Count, 0);
        Assert.Greater(result.Blocks.Count, 0);
        Assert.Greater(result.Buildings.Count, 0);

        foreach (CityRoadSegment road in result.Roads)
            AssertFootprintInside(boundary, road.GetCorners(), "road");
        foreach (CityBlockData block in result.Blocks)
            AssertFootprintInside(boundary, block.Footprint, "block");
        foreach (CityLotData lot in result.Lots)
            AssertFootprintInside(boundary, lot.Footprint, "lot");
        foreach (CityBuildingData building in result.Buildings)
            AssertFootprintInside(boundary, building.Footprint, "building");
    }

    [Test]
    public void BowTieGeneratesCityInsideBothTriangularRegions()
    {
        Vector2[] boundary = CreateBowTieBoundary();
        CityGenerationResult result =
            new CityGenerator().Generate(boundary, Settings, 2459);

        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.AreEqual(2, result.Regions.Count);
        Assert.Greater(result.Roads.Count, 0);
        Assert.Greater(result.Buildings.Count, 0);

        for (int regionIndex = 0; regionIndex < result.Regions.Count; regionIndex++)
        {
            IReadOnlyList<Vector2> region = result.Regions[regionIndex];
            Assert.IsTrue(result.Buildings.Any(
                building => CityPolygonGeometry.ContainsPoint(
                    region,
                    Average(building.Footprint))));
        }

        foreach (CityRoadSegment road in result.Roads)
            AssertInsideAnyRegion(result.Regions, road.GetCorners(), "road");
        foreach (CityBuildingData building in result.Buildings)
            AssertInsideAnyRegion(result.Regions, building.Footprint, "building");
    }

    [Test]
    public void BuildingDensityIsBudgetedFromDevelopableArea()
    {
        Vector2[] boundary = CreateBowTieBoundary();
        CityGenerationResult result =
            new CityGenerator().Generate(boundary, Settings, 12345);

        Assert.IsTrue(result.IsSuccess, result.Error);
        Assert.GreaterOrEqual(
            result.Buildings.Count,
            20,
            "A large bow-tie selection should not collapse to one building per face.");
        Assert.Less(
            result.Buildings.Count,
            120,
            "The area budget must also prevent unbounded overfilling.");
    }

    [Test]
    public void GeneratorRejectsFewerThanThreePoints()
    {
        CityGenerationResult result = new CityGenerator().Generate(
            new[] { Vector2.zero, Vector2.right * 30f },
            Settings,
            1);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotEmpty(result.Error);
    }

    [Test]
    public void ConcavePolygonTriangulationStaysInsideBoundary()
    {
        Vector2[] polygon = CreateConcaveBoundary();
        Assert.IsTrue(
            CityPolygonGeometry.TryTriangulate(
                polygon,
                out List<int> triangles));
        Assert.AreEqual((polygon.Length - 2) * 3, triangles.Count);

        for (int i = 0; i < triangles.Count; i += 3)
        {
            Vector2 center = (
                polygon[triangles[i]]
                + polygon[triangles[i + 1]]
                + polygon[triangles[i + 2]]) / 3f;
            Assert.IsTrue(
                CityPolygonGeometry.ContainsPoint(polygon, center),
                "A platform cap triangle escaped the concave boundary.");
        }
    }

    [Test]
    public void ElevatedPlatformsShareHighestPointAndMergeSupports()
    {
        Assert.IsTrue(
            CityPolygonGeometry.ResolveClosedRegions(
                CreateBowTieBoundary(),
                Settings,
                out List<List<Vector2>> regions,
                out string error),
            error);
        var sampler = new SyntheticTerrainSampler();
        CityPlatformLayout layout = CityElevatedPlatformPlanner.Create(
            regions,
            sampler,
            0.5f,
            0.4f,
            2f);

        Assert.AreEqual(2, layout.FoundationCount);
        Assert.That(
            layout.TopHeight,
            Is.EqualTo(layout.MaximumGroundHeight + 0.5f).Within(0.0001f));

        var root = new GameObject("ElevatedPlatformTest");
        try
        {
            for (int regionIndex = 0;
                 regionIndex < regions.Count;
                 regionIndex++)
            {
                GameObject platform =
                    CityRuntimeMeshFactory.CreateElevatedCityPlatform(
                        "Platform_" + regionIndex,
                        regions[regionIndex],
                        layout.TopHeight,
                        layout.SlabThickness,
                        20f,
                        1.4f,
                        0.6f,
                        sampler,
                        null,
                        root.transform,
                        out int supports,
                        out float maximumSupportHeight);
                Mesh mesh = platform.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(
                    1,
                    platform.GetComponents<MeshRenderer>().Length);
                Assert.AreSame(
                    mesh,
                    platform.GetComponent<MeshCollider>().sharedMesh);
                Assert.That(
                    mesh.bounds.max.y,
                    Is.EqualTo(layout.TopHeight).Within(0.0001f));
                Assert.Greater(supports, 0);
                Assert.Greater(maximumSupportHeight, 0.6f);

                Vector3[] vertices = mesh.vertices;
                int[] meshTriangles = mesh.triangles;
                int topTriangleCount = 0;
                for (int i = 0; i < meshTriangles.Length; i += 3)
                {
                    Vector3 a = vertices[meshTriangles[i]];
                    Vector3 b = vertices[meshTriangles[i + 1]];
                    Vector3 c = vertices[meshTriangles[i + 2]];
                    if (!Mathf.Approximately(a.y, layout.TopHeight)
                        || !Mathf.Approximately(b.y, layout.TopHeight)
                        || !Mathf.Approximately(c.y, layout.TopHeight))
                    {
                        continue;
                    }
                    topTriangleCount++;
                    Vector2 center = new Vector2(
                        (a.x + b.x + c.x) / 3f,
                        (a.z + b.z + c.z) / 3f);
                    Assert.IsTrue(
                        CityPolygonGeometry.ContainsPoint(
                            regions[regionIndex],
                            center));
                }
                Assert.AreEqual(
                    regions[regionIndex].Count - 2,
                    topTriangleCount);
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void AdjacentPlatformRegionsUseOneMergedRendererAndCollider()
    {
        var regions = new List<List<Vector2>>
        {
            new List<Vector2>
            {
                new Vector2(-80f, -50f),
                new Vector2(0f, -50f),
                new Vector2(0f, 50f),
                new Vector2(-80f, 50f)
            },
            new List<Vector2>
            {
                new Vector2(0f, -50f),
                new Vector2(80f, -50f),
                new Vector2(80f, 50f),
                new Vector2(0f, 50f)
            }
        };
        var root = new GameObject("MergedPlatformTest");
        try
        {
            GameObject platform =
                CityRuntimeMeshFactory.CreateElevatedCityPlatforms(
                    "MergedFoundation",
                    regions,
                    20f,
                    0.4f,
                    20f,
                    1.4f,
                    0.6f,
                    new SyntheticTerrainSampler(),
                    null,
                    root.transform,
                    out int supports,
                    out float maximumSupportHeight);
            Mesh mesh = platform.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(
                1,
                platform.GetComponents<MeshRenderer>().Length);
            Assert.AreSame(
                mesh,
                platform.GetComponent<MeshCollider>().sharedMesh);
            Assert.Greater(supports, 0);
            Assert.Greater(maximumSupportHeight, 0f);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PlaneBuildingIsUprightAndTouchesSharedPlatform()
    {
        var root = new GameObject("PlaneBuildingTest");
        root.transform.position = new Vector3(420f, 7f, -315f);
        var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            GameObject building =
                CityRuntimeMeshFactory.CreatePrefabBuildingOnPlane(
                    "Building",
                    prefab,
                    new[]
                    {
                        new Vector2(-8f, -6f),
                        new Vector2(8f, -6f),
                        new Vector2(8f, 6f),
                        new Vector2(-8f, 6f)
                    },
                    12f,
                    null,
                    false,
                    root.transform);
            Transform model = building.transform.GetChild(0);
            Assert.That(
                Vector3.Angle(Vector3.up, model.up),
                Is.LessThan(0.01f));
            Bounds bounds = model.GetComponent<Renderer>().bounds;
            Assert.That(
                bounds.center.x,
                Is.EqualTo(root.transform.position.x).Within(0.001f));
            Assert.That(
                bounds.center.z,
                Is.EqualTo(root.transform.position.z).Within(0.001f));
            Assert.That(
                bounds.min.y,
                Is.EqualTo(
                    root.transform.position.y + 12.03f)
                    .Within(0.001f));
            Assert.AreEqual(0, building.GetComponents<MeshRenderer>().Length);
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(prefab);
        }
    }

    [Test]
    public void NoiseTerrainBuildsDeterministicClickableMesh()
    {
        var ground = new GameObject("NoiseTerrainTest");
        try
        {
            ground.AddComponent<MeshFilter>();
            ground.AddComponent<MeshRenderer>();
            MeshCollider collider = ground.AddComponent<MeshCollider>();
            CityNoiseTerrain terrain = ground.AddComponent<CityNoiseTerrain>();
            ConfigureTestTerrain(terrain, 64, true);
            terrain.Rebuild();

            float first = terrain.SampleHeight(new Vector2(37f, -52f));
            float second = terrain.SampleHeight(new Vector2(37f, -52f));
            float different = terrain.SampleHeight(new Vector2(-83f, 61f));

            Assert.AreEqual(first, second);
            Assert.AreNotEqual(first, different);
            Assert.NotNull(terrain.TerrainMesh);
            Assert.AreEqual(
                (terrain.Resolution + 1) * (terrain.Resolution + 1),
                terrain.TerrainMesh.vertexCount);
            Assert.AreSame(terrain.TerrainMesh, collider.sharedMesh);
            Assert.AreEqual(2, terrain.RiverCount);
            Assert.GreaterOrEqual(terrain.LakeCount, 1);
            Assert.Greater(terrain.Relief, 8f);

            float firstRelief = terrain.Relief;
            float firstWaterCoverage = terrain.WaterCoverage;
            Vector3 firstPatch = terrain.PatchDirection;
            terrain.Rebuild();
            Assert.That(terrain.Relief, Is.EqualTo(firstRelief).Within(0.0001f));
            Assert.That(
                terrain.WaterCoverage,
                Is.EqualTo(firstWaterCoverage).Within(0.0001f));
            Assert.That(terrain.PatchDirection, Is.EqualTo(firstPatch));
        }
        finally
        {
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void HydrologyFlowsDownhillAndClassifiesLakeWater()
    {
        var ground = new GameObject("CityHydrologyTest");
        try
        {
            ground.AddComponent<MeshFilter>();
            ground.AddComponent<MeshRenderer>();
            ground.AddComponent<MeshCollider>();
            CityNoiseTerrain terrain = ground.AddComponent<CityNoiseTerrain>();
            ConfigureTestTerrain(terrain, 64, true);
            terrain.Rebuild();

            for (int riverIndex = 0;
                 riverIndex < terrain.RiverCount;
                 riverIndex++)
            {
                IReadOnlyList<float> heights =
                    terrain.GetRiverWaterHeights(riverIndex);
                Assert.Greater(heights.Count, 2);
                for (int i = 1; i < heights.Count; i++)
                {
                    Assert.LessOrEqual(
                        heights[i],
                        heights[i - 1] + 0.0001f,
                        "River water must not run uphill.");
                }
            }

            CityTerrainSample lake =
                terrain.SampleTerrain(terrain.GetLakeCenter(0));
            Assert.AreEqual(CitySurfaceKind.Lake, lake.SurfaceKind);
            Assert.Greater(lake.WaterDepth, 0.2f);
            Assert.Greater(lake.WaterHeight, lake.GroundHeight);
        }
        finally
        {
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void ApplyingCityPlanKeepsHydrologyFeatures()
    {
        var ground = new GameObject("CityHydrologyStampTest");
        try
        {
            ground.AddComponent<MeshFilter>();
            ground.AddComponent<MeshRenderer>();
            ground.AddComponent<MeshCollider>();
            CityNoiseTerrain terrain = ground.AddComponent<CityNoiseTerrain>();
            ConfigureTestTerrain(terrain, 64, true);
            terrain.Rebuild();

            CityGenerationResult result = new CityGenerator().Generate(
                CreateBowTieBoundary(),
                Settings,
                12345,
                terrain);
            Assert.IsTrue(result.IsSuccess, result.Error);
            int riverCount = terrain.RiverCount;
            int lakeCount = terrain.LakeCount;

            terrain.ApplyCityPlan(
                result,
                Settings.maximumRoadGrade,
                Settings.terrainBlendWidth);

            Assert.AreEqual(riverCount, terrain.RiverCount);
            Assert.AreEqual(lakeCount, terrain.LakeCount);
            Assert.Greater(terrain.WaterCoverage, 0f);
        }
        finally
        {
            Object.DestroyImmediate(ground);
        }
    }

    [Test]
    public void TerrainAwareGeneratorLimitsLandGradeAndRemainsDeterministic()
    {
        var sampler = new SyntheticTerrainSampler();
        var generator = new CityGenerator();
        CityGenerationResult first = generator.Generate(
            CreateBowTieBoundary(),
            Settings,
            12345,
            sampler);
        CityGenerationResult second = generator.Generate(
            CreateBowTieBoundary(),
            Settings,
            12345,
            sampler);

        Assert.IsTrue(first.IsSuccess, first.Error);
        Assert.IsTrue(second.IsSuccess, second.Error);
        Assert.AreEqual(first.Roads.Count, second.Roads.Count);
        Assert.AreEqual(first.Buildings.Count, second.Buildings.Count);
        Assert.LessOrEqual(
            first.Diagnostics.MaximumRoadGrade,
            Settings.maximumRoadGrade * 1.05f + 0.0001f);
        Assert.Greater(first.Diagnostics.WaterRoadCount, 0);
        Assert.Greater(first.Diagnostics.SwitchbackRoadCount, 0);
        for (int i = 0; i < first.Roads.Count; i++)
        {
            Assert.AreEqual(first.Roads[i].Start, second.Roads[i].Start);
            Assert.AreEqual(first.Roads[i].End, second.Roads[i].End);
            Assert.AreEqual(
                first.Roads[i].CrossesWater,
                second.Roads[i].CrossesWater);
        }
    }

    [Test]
    public void AdaptivePlatformsTiltOnSlopesAndUsePilesOverWater()
    {
        var root = new GameObject("AdaptivePlatformTest");
        var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prefab.name = "TestBuilding";
        try
        {
            Vector2[] footprint =
            {
                new Vector2(32f, -6f),
                new Vector2(48f, -6f),
                new Vector2(48f, 6f),
                new Vector2(32f, 6f)
            };
            GameObject slope = CityRuntimeMeshFactory.CreateAdaptivePrefabBuildingObject(
                "Slope",
                prefab,
                footprint,
                new SyntheticTerrainSampler(false),
                null,
                false,
                root.transform,
                out bool isSloped,
                out bool isWater,
                out _);
            Assert.IsTrue(isSloped);
            Assert.IsFalse(isWater);
            Transform slopeModel = slope.transform.GetChild(0);
            Assert.Greater(Vector3.Angle(Vector3.up, slopeModel.up), 5f);
            Assert.LessOrEqual(Vector3.Angle(Vector3.up, slopeModel.up), 12.1f);
            Assert.That(slopeModel.position.x, Is.EqualTo(40f).Within(0.1f));
            Assert.That(slopeModel.position.z, Is.EqualTo(0f).Within(0.1f));

            GameObject water = CityRuntimeMeshFactory.CreateAdaptivePrefabBuildingObject(
                "Water",
                prefab,
                new[]
                {
                    new Vector2(-8f, -6f),
                    new Vector2(8f, -6f),
                    new Vector2(8f, 6f),
                    new Vector2(-8f, 6f)
                },
                new SyntheticTerrainSampler(true),
                null,
                false,
                root.transform,
                out bool waterSloped,
                out bool waterPlaced,
                out int supportPiles);
            Assert.IsFalse(waterSloped);
            Assert.IsTrue(waterPlaced);
            Assert.GreaterOrEqual(supportPiles, 4);
            Assert.That(
                Vector3.Angle(Vector3.up, water.transform.GetChild(0).up),
                Is.LessThan(0.1f));
            Assert.AreEqual(
                1,
                water.GetComponents<MeshRenderer>().Length,
                "Deck and piles should share one renderer.");
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(prefab);
        }
    }

    [Test]
    public void TerrainRoadMeshSamplesHeightAlongItsLength()
    {
        var root = new GameObject("TerrainRoadTest");
        try
        {
            var road = new CityRoadSegment(
                new Vector2(-20f, -5f),
                new Vector2(20f, 5f),
                4f,
                true);
            System.Func<Vector2, float> sampler =
                point => point.x * 0.1f + point.y * 0.05f;
            GameObject roadObject = CityRuntimeMeshFactory.CreateTerrainRoadObject(
                "Road",
                road,
                sampler,
                0.12f,
                3f,
                null,
                root.transform);

            Mesh mesh = roadObject.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount, 4);
            foreach (Vector3 vertex in mesh.vertices)
            {
                float expected = sampler(new Vector2(vertex.x, vertex.z)) + 0.12f;
                Assert.That(vertex.y, Is.EqualTo(expected).Within(0.0001f));
            }

            Object.DestroyImmediate(mesh);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void CityGenerateSceneContainsConfiguredHierarchyAndIsNotInBuild()
    {
        const string scenePath = "Assets/Scenes/citygenerate.unity";
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        try
        {
            GameObject root = scene.GetRootGameObjects().Single();
            Assert.AreEqual("CityGenerate", root.name);
            Assert.NotNull(root.transform.Find("Main Camera"));
            Assert.NotNull(root.transform.Find("Directional Light"));
            Assert.NotNull(root.transform.Find("Ground"));
            Assert.NotNull(root.transform.Find("CitySelectionController/BoundaryMarkers"));
            Assert.NotNull(root.transform.Find("CitySelectionController/BoundaryLine"));
            Assert.NotNull(root.transform.Find("GeneratedCity/Foundations"));
            Assert.NotNull(root.transform.Find("GeneratedCity/Roads"));
            Assert.NotNull(root.transform.Find("GeneratedCity/Blocks"));
            Assert.NotNull(root.transform.Find("GeneratedCity/Buildings"));
            Assert.NotNull(
                root.transform.Find("GeneratedCity/ConstructionEffects"));
            Assert.NotNull(root.transform.Find("TerrainFeatures/Rivers"));
            Assert.NotNull(root.transform.Find("TerrainFeatures/Lakes"));

            Camera camera = root.GetComponentInChildren<Camera>(true);
            Assert.NotNull(camera);
            Assert.IsTrue(camera.orthographic);
            Transform ground = root.transform.Find("Ground");
            Assert.NotNull(ground.GetComponent<MeshCollider>());
            Assert.NotNull(ground.GetComponent<CityNoiseTerrain>());
            var terrain = ground.GetComponent<CityNoiseTerrain>();
            var serializedTerrain = new SerializedObject(terrain);
            Assert.AreEqual(
                200,
                serializedTerrain.FindProperty("resolution").intValue);
            Assert.That(
                serializedTerrain.FindProperty("heightScale").floatValue,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.NotNull(
                serializedTerrain.FindProperty("planetPreset").objectReferenceValue);
            Assert.AreEqual(
                1,
                root.GetComponentsInChildren<CitySelectionController>(true).Length);
            Assert.AreEqual(
                0,
                root.GetComponentsInChildren<SimplePlayerController>(true).Length);
            var controller =
                root.GetComponentInChildren<CitySelectionController>(true);
            var serializedController = new SerializedObject(controller);
            Assert.IsFalse(
                serializedController.FindProperty("overridePrefabMaterials").boolValue);
            SerializedProperty modularLibrary =
                serializedController.FindProperty("modernCityRoadModules");
            Assert.NotNull(modularLibrary);
            Assert.NotNull(
                modularLibrary.FindPropertyRelative("roadStraight")
                    .objectReferenceValue,
                "roadStraight");
            Assert.NotNull(
                modularLibrary.FindPropertyRelative("roadX")
                    .objectReferenceValue,
                "roadX");
            Assert.NotNull(
                modularLibrary.FindPropertyRelative("roadAlbedo")
                    .objectReferenceValue,
                "roadAlbedo");
            Assert.NotNull(
                modularLibrary.FindPropertyRelative("roadNormal")
                    .objectReferenceValue,
                "roadNormal");
            Assert.NotNull(
                modularLibrary.FindPropertyRelative("pathwayAlbedo")
                    .objectReferenceValue,
                "pathwayAlbedo");
            Assert.AreEqual(
                4,
                modularLibrary.FindPropertyRelative("pathway4x4")
                    .arraySize);
            SerializedProperty generationSettings =
                serializedController.FindProperty("generationSettings");
            Assert.IsTrue(
                generationSettings
                    .FindPropertyRelative("useModularRoadLayout")
                    .boolValue);
            Assert.That(
                generationSettings
                    .FindPropertyRelative("modularRoadCellSize")
                    .floatValue,
                Is.EqualTo(10f).Within(0.001f));
            var constructionAnimator =
                controller.GetComponent<CityConstructionAnimator>();
            Assert.NotNull(constructionAnimator);
            Assert.That(
                constructionAnimator.TotalDuration,
                Is.EqualTo(10f).Within(0.0001f));
            Assert.That(
                constructionAnimator.DeconstructionDuration,
                Is.EqualTo(1.6f).Within(0.0001f));
            Assert.AreSame(
                root.transform.Find("GeneratedCity/ConstructionEffects"),
                serializedController
                    .FindProperty("constructionEffectsRoot")
                    .objectReferenceValue);

            Assert.IsFalse(EditorBuildSettings.scenes.Any(
                item => item.path == scenePath && item.enabled));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void ConstructionAndDeconstructionTimelinesUseStableDistanceOrder()
    {
        var gameObject = new GameObject("ConstructionAnimatorTest");
        try
        {
            var animator =
                gameObject.AddComponent<CityConstructionAnimator>();
            Assert.That(
                animator.TotalDuration,
                Is.EqualTo(10f).Within(0.0001f));
            Assert.That(
                animator.DeconstructionDuration,
                Is.EqualTo(1.6f).Within(0.0001f));

            Vector2[] positions =
            {
                new Vector2(8f, 0f),
                new Vector2(2f, 0f),
                new Vector2(-2f, 0f),
                new Vector2(5f, 0f)
            };
            CollectionAssert.AreEqual(
                new[] { 1, 2, 3, 0 },
                CityConstructionAnimator.GetStableDistanceOrder(
                    positions,
                    Vector2.zero));
            CollectionAssert.AreEqual(
                new[] { 0, 3, 1, 2 },
                CityConstructionAnimator.GetStableDeconstructionOrder(
                    positions,
                    Vector2.zero));
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void CompletedConstructionCanEnterAndSkipDeconstructionSafely()
    {
        var animatorObject =
            new GameObject("DeconstructionLifecycleTest");
        var generatedRoot =
            new GameObject("GeneratedObjects");
        try
        {
            var animator =
                animatorObject.AddComponent<CityConstructionAnimator>();
            var effectsRoot =
                new GameObject("ConstructionEffects");
            effectsRoot.transform.SetParent(animatorObject.transform, false);
            animator.Configure(effectsRoot.transform);

            var job = new CityConstructionJob(
                new[]
                {
                    new Vector2(-10f, -10f),
                    new Vector2(10f, -10f),
                    new Vector2(10f, 10f),
                    new Vector2(-10f, 10f)
                },
                Vector2.zero,
                0f,
                1f,
                1.4f);
            var item = new CityConstructionItem(
                0,
                Vector2.zero,
                () =>
                {
                    GameObject building =
                        GameObject.CreatePrimitive(PrimitiveType.Cube);
                    building.transform.SetParent(
                        generatedRoot.transform,
                        false);
                    return building;
                });
            job.Buildings.Add(item);

            bool constructionCompleted = false;
            Assert.IsTrue(animator.Begin(
                job,
                null,
                () => constructionCompleted = true));
            animator.CompleteImmediately();
            Assert.IsTrue(constructionCompleted);
            Assert.IsTrue(item.IsCreated);
            Assert.NotNull(item.CreatedObject);

            Collider collider = item.CreatedObject.GetComponent<Collider>();
            Renderer renderer = item.CreatedObject.GetComponent<Renderer>();
            Assert.IsTrue(collider.enabled);
            Assert.IsTrue(renderer.enabled);

            bool deconstructionCompleted = false;
            Assert.IsTrue(animator.BeginDeconstruction(
                job,
                null,
                () => deconstructionCompleted = true));
            Assert.IsTrue(animator.IsDeconstructing);
            Assert.That(animator.DeconstructionProgress, Is.EqualTo(0f));
            Assert.IsFalse(collider.enabled);
            Assert.IsTrue(renderer.enabled);

            animator.CompleteDeconstructionImmediately();
            Assert.IsTrue(deconstructionCompleted);
            Assert.IsFalse(animator.IsDeconstructing);
            Assert.That(animator.DeconstructionProgress, Is.EqualTo(1f));
            Assert.IsFalse(renderer.enabled);
            Assert.IsFalse(collider.enabled);
        }
        finally
        {
            Object.DestroyImmediate(animatorObject);
            Object.DestroyImmediate(generatedRoot);
        }
    }

    [Test]
    public void HolographicConstructionShaderSupportsRuntimeReveal()
    {
        Shader shader =
            Shader.Find("CityGeneration/HolographicConstruction");
        Assert.NotNull(shader);
        Assert.IsTrue(shader.isSupported);
        var material = new Material(shader);
        try
        {
            Assert.IsTrue(material.HasProperty("_RevealProgress"));
            Assert.IsTrue(material.HasProperty("_RevealMode"));
            Assert.IsTrue(material.HasProperty("_SolidLag"));
            Assert.IsTrue(material.HasProperty("_WireWidth"));
        }
        finally
        {
            Object.DestroyImmediate(material);
        }
    }

    [Test]
    public void ModularRoadLayoutIsDeterministicOrthogonalAndInsideBoundary()
    {
        var settings = new CityGenerationSettings
        {
            useModularRoadLayout = true,
            modularRoadCellSize = 10f,
            modularPathwayUnitSize = 2.5f,
            modularChunkSize = 16,
            minorRoadDensity = 0.72f,
            blockDensity = 0.72f,
            buildingDensity = 0.76f,
            minimumBoundaryArea = 400f
        };
        Vector2[] boundary =
        {
            new Vector2(-100f, -80f),
            new Vector2(100f, -80f),
            new Vector2(100f, 80f),
            new Vector2(-100f, 80f)
        };

        CityGenerationResult first =
            new CityGenerator().Generate(boundary, settings, 481516);
        CityGenerationResult second =
            new CityGenerator().Generate(boundary, settings, 481516);
        Assert.IsTrue(first.IsSuccess, first.Error);
        Assert.IsTrue(second.IsSuccess, second.Error);
        CityRoadLayoutValidationResult layoutValidation =
            CityModularRoadPlanner.ValidateLayout(first.RoadModules);
        Assert.IsTrue(layoutValidation.IsValid, layoutValidation.Error);
        Assert.AreEqual(
            first.Regions.Count,
            layoutValidation.ConnectedComponentCount);
        Assert.AreEqual(
            first.Regions.Count,
            first.Diagnostics.RoadConnectedComponentCount);
        Assert.NotNull(first.RoadLayoutValidation);
        Assert.IsTrue(
            first.RoadLayoutValidation.IsValid,
            first.RoadLayoutValidation.Error);
        Assert.Greater(first.RoadModules.Count, 3);
        Assert.Greater(first.PathwayModules.Count, 0);
        Assert.AreEqual(first.RoadModules.Count, second.RoadModules.Count);
        Assert.AreEqual(
            first.PathwayModules.Count,
            second.PathwayModules.Count);
        Assert.IsTrue(first.RoadModules.Any(
            value => value.Type == CityRoadModuleType.End));
        Assert.IsTrue(first.RoadModules.Any(
            value => value.Type == CityRoadModuleType.Straight));
        Assert.IsTrue(first.RoadModules.Any(
            value => value.Type == CityRoadModuleType.StraightCrossing));
        Assert.IsTrue(first.RoadModules.Any(
            value => value.Type == CityRoadModuleType.CurveSmall));
        Assert.IsFalse(first.RoadModules.Any(
            value => value.Type == CityRoadModuleType.CurveLong));
        Assert.IsTrue(first.RoadModules.Any(
            value => value.Type == CityRoadModuleType.TIntersection));
        Assert.IsTrue(first.RoadModules.Any(
            value => value.Type == CityRoadModuleType.CrossIntersection));

        Vector2 axis = first.ModularRoadAxis.normalized;
        Vector2 perpendicular = new Vector2(-axis.y, axis.x);
        var moduleDefinitions = new ModernCityRoadModuleLibrary();
        var moduleByGrid = first.RoadModules.ToDictionary(
            value => new Vector2Int(value.GridX, value.GridY));
        var occupiedRoadCells =
            new HashSet<Vector2Int>(moduleByGrid.Keys);
        CityRoadModulePlacement originModule =
            first.RoadModules[0];
        Vector2 gridOrigin =
            originModule.Position
            - axis
            * (originModule.GridX
                * settings.modularRoadCellSize)
            - perpendicular
            * (originModule.GridY
                * settings.modularRoadCellSize);
        CityRoadConnectionMask[] directionMasks =
        {
            CityRoadConnectionMask.North,
            CityRoadConnectionMask.East,
            CityRoadConnectionMask.South,
            CityRoadConnectionMask.West
        };
        Vector2Int[] directionOffsets =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0)
        };
        CityRoadConnectionMask[] oppositeMasks =
        {
            CityRoadConnectionMask.South,
            CityRoadConnectionMask.West,
            CityRoadConnectionMask.North,
            CityRoadConnectionMask.East
        };
        for (int i = 0; i < first.RoadModules.Count; i++)
        {
            CityRoadModulePlacement module = first.RoadModules[i];
            CityRoadModulePlacement repeated = second.RoadModules[i];
            Assert.AreEqual(module.Type, repeated.Type);
            Assert.AreEqual(module.GridX, repeated.GridX);
            Assert.AreEqual(module.GridY, repeated.GridY);
            Assert.AreEqual(module.QuarterTurns, repeated.QuarterTurns);
            Assert.AreEqual(
                module.ConnectionMask,
                repeated.ConnectionMask);
            Assert.That(
                Vector2.Distance(module.Position, repeated.Position),
                Is.LessThan(0.0001f));
            Assert.AreNotEqual(
                CityRoadConnectionMask.None,
                module.ConnectionMask);
            CityRoadModuleDefinition definition =
                moduleDefinitions.GetRoadDefinition(module.Type);
            Assert.AreEqual(
                module.ConnectionMask,
                ModernCityRoadModuleLibrary.RotateMask(
                    definition.CanonicalConnections,
                    module.QuarterTurns),
                module.Type + " used an incompatible rotation.");

            for (int direction = 0;
                 direction < directionMasks.Length;
                 direction++)
            {
                if ((module.ConnectionMask
                        & directionMasks[direction]) == 0)
                {
                    continue;
                }
                Vector2Int neighborCoordinate =
                    new Vector2Int(module.GridX, module.GridY)
                    + directionOffsets[direction];
                Assert.IsTrue(
                    moduleByGrid.TryGetValue(
                        neighborCoordinate,
                        out CityRoadModulePlacement neighbor),
                    module.Type + " has a dangling road port.");
                Assert.AreNotEqual(
                    CityRoadConnectionMask.None,
                    neighbor.ConnectionMask
                        & oppositeMasks[direction],
                    module.Type + " has a one-way module seam.");
            }

            if (module.Type
                == CityRoadModuleType.StraightCrossing)
            {
                CityRoadConnectionMask expectedCrosswalk =
                    FindExpectedCrosswalkEdge(
                        module,
                        moduleByGrid,
                        directionMasks,
                        directionOffsets);
                Assert.AreEqual(
                    expectedCrosswalk,
                    ModernCityRoadModuleLibrary.RotateMask(
                        definition.CanonicalCrosswalkEdge,
                        module.QuarterTurns));
            }

            float half =
                settings.modularRoadCellSize
                * module.FootprintCells
                * 0.5f;
            Vector2[] footprint =
            {
                module.Position - axis * half - perpendicular * half,
                module.Position + axis * half - perpendicular * half,
                module.Position + axis * half + perpendicular * half,
                module.Position - axis * half + perpendicular * half
            };
            AssertInsideAnyRegion(first.Regions, footprint, "road module");
        }

        foreach (CityRoadSegment road in first.Roads)
        {
            Vector2 direction = (road.End - road.Start).normalized;
            float alignment = Mathf.Max(
                Mathf.Abs(Vector2.Dot(direction, axis)),
                Mathf.Abs(Vector2.Dot(direction, perpendicular)));
            Assert.That(alignment, Is.GreaterThan(0.999f));
            Assert.That(
                road.Width,
                Is.EqualTo(settings.modularRoadCellSize)
                    .Within(0.0001f));
        }

        foreach (CityBuildingData building in first.Buildings)
        {
            Assert.IsFalse(
                OverlapsAnyRoadModule(
                    building.Footprint,
                    occupiedRoadCells,
                    gridOrigin,
                    axis,
                    perpendicular,
                    settings.modularRoadCellSize),
                "building overlapped a full modular road footprint.");
        }

        foreach (CityPathwayModulePlacement pathway
                 in first.PathwayModules)
        {
            float halfX =
                pathway.SizeX
                * settings.modularPathwayUnitSize
                * 0.5f;
            float halfY =
                pathway.SizeY
                * settings.modularPathwayUnitSize
                * 0.5f;
            Vector2[] footprint =
            {
                pathway.Position - axis * halfX - perpendicular * halfY,
                pathway.Position + axis * halfX - perpendicular * halfY,
                pathway.Position + axis * halfX + perpendicular * halfY,
                pathway.Position - axis * halfX + perpendicular * halfY
            };
            Assert.IsFalse(
                OverlapsAnyRoadModule(
                    footprint,
                    occupiedRoadCells,
                    gridOrigin,
                    axis,
                    perpendicular,
                    settings.modularRoadCellSize),
                "pathway overlapped a full modular road footprint.");
        }
    }

    [Test]
    public void RoadModuleDefinitionsSolveEveryEnabledOrientation()
    {
        var library = new ModernCityRoadModuleLibrary();
        CityRoadModuleType[] types =
        {
            CityRoadModuleType.End,
            CityRoadModuleType.Straight,
            CityRoadModuleType.StraightCrossing,
            CityRoadModuleType.CurveSmall,
            CityRoadModuleType.TIntersection,
            CityRoadModuleType.CrossIntersection
        };
        foreach (CityRoadModuleType type in types)
        {
            CityRoadModuleDefinition definition =
                library.GetRoadDefinition(type);
            Assert.IsTrue(definition.IsEnabled, type.ToString());
            Assert.AreEqual(
                definition.CanonicalConnections,
                library.GetCanonicalSocketTags(type).RoadConnectionMask,
                type + " canonical socket tags disagree with its topology.");
            for (int turns = 0; turns < 4; turns++)
            {
                CityRoadConnectionMask desired =
                    ModernCityRoadModuleLibrary.RotateMask(
                        definition.CanonicalConnections,
                        turns);
                CityRoadConnectionMask preferredCrosswalk =
                    ModernCityRoadModuleLibrary.RotateMask(
                        definition.CanonicalCrosswalkEdge,
                        turns);
                Assert.IsTrue(
                    library.TrySolveQuarterTurns(
                        type,
                        desired,
                        preferredCrosswalk,
                        out int solved),
                    type + " could not solve a legal port mask.");
                Assert.AreEqual(
                    desired,
                    ModernCityRoadModuleLibrary.RotateMask(
                        definition.CanonicalConnections,
                        solved));
                Assert.AreEqual(
                    desired,
                    library.GetSocketTags(type, solved)
                        .RoadConnectionMask);
                if (preferredCrosswalk
                    != CityRoadConnectionMask.None)
                {
                    Assert.AreEqual(
                        preferredCrosswalk,
                        ModernCityRoadModuleLibrary.RotateMask(
                            definition.CanonicalCrosswalkEdge,
                            solved));
                }
            }
        }

        Assert.IsFalse(
            library.GetRoadDefinition(
                CityRoadModuleType.CurveLong).IsEnabled);
        Assert.IsFalse(
            library.TrySolveQuarterTurns(
                CityRoadModuleType.CurveLong,
                CityRoadConnectionMask.North
                    | CityRoadConnectionMask.West,
                CityRoadConnectionMask.None,
                out _));
    }

    [Test]
    public void RoadSocketTagsConnectStraightSouthToCurveNorth()
    {
        var library = new ModernCityRoadModuleLibrary();
        CityRoadSocketSet straight =
            library.GetSocketTags(CityRoadModuleType.Straight, 0);
        CityRoadSocketSet curve =
            library.GetSocketTags(CityRoadModuleType.CurveSmall, 0);
        CityRoadSocketSet end =
            library.GetSocketTags(CityRoadModuleType.End, 0);

        Assert.AreEqual(
            CityRoadSocketTag.Road2LaneA,
            straight.South);
        Assert.AreEqual(
            CityRoadSocketTag.Road2LaneA,
            curve.North);
        Assert.IsTrue(
            ModernCityRoadModuleLibrary.AreOpposingSocketsCompatible(
                straight.South,
                curve.North));
        Assert.AreEqual(
            CityRoadSocketTag.SidewalkA,
            straight.East);
        Assert.AreEqual(
            CityRoadSocketTag.ClosedA,
            end.South);
        Assert.IsFalse(
            ModernCityRoadModuleLibrary.AreOpposingSocketsCompatible(
                straight.East,
                curve.North));
    }

    [Test]
    public void RoadSocketTagsRotateWithTheirModule()
    {
        var library = new ModernCityRoadModuleLibrary();
        CityRoadSocketSet curve =
            library.GetSocketTags(CityRoadModuleType.CurveSmall, 1);

        Assert.AreEqual(
            CityRoadSocketTag.Road2LaneA,
            curve.East);
        Assert.AreEqual(
            CityRoadSocketTag.Road2LaneA,
            curve.South);
        Assert.AreEqual(
            CityRoadSocketTag.SidewalkA,
            curve.North);
        Assert.AreEqual(
            CityRoadSocketTag.SidewalkA,
            curve.West);
    }

    [Test]
    public void ModularRoadWorldBasisMapsEastAndNorthToGridAxes()
    {
        Vector2[] axes =
        {
            Vector2.right,
            new Vector2(0.6f, 0.8f).normalized,
            Vector2.left,
            new Vector2(-0.8f, 0.6f).normalized
        };

        foreach (Vector2 axisX in axes)
        {
            Vector2 axisY = new Vector2(-axisX.y, axisX.x);
            float angle =
                CityModularRoadMeshFactory
                    .CalculateGridBasisAngleFromAxisX(axisX);
            Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
            Vector3 east3 = rotation * Vector3.right;
            Vector3 north3 = rotation * Vector3.forward;
            Vector2 worldEast = new Vector2(east3.x, east3.z);
            Vector2 worldNorth = new Vector2(north3.x, north3.z);

            Assert.That(
                Vector2.Distance(worldEast, axisX),
                Is.LessThan(0.0001f),
                "Model East did not align with grid AxisX.");
            Assert.That(
                Vector2.Distance(worldNorth, axisY),
                Is.LessThan(0.0001f),
                "Model North did not align with grid AxisY.");

            Quaternion oneTurn =
                Quaternion.Euler(0f, angle + 90f, 0f);
            Vector3 rotatedNorth3 =
                oneTurn * Vector3.forward;
            Vector2 rotatedNorth =
                new Vector2(rotatedNorth3.x, rotatedNorth3.z);
            Assert.That(
                Vector2.Distance(rotatedNorth, axisX),
                Is.LessThan(0.0001f),
                "One clockwise module turn must map North to East.");
        }
    }

    [Test]
    public void ModularRoadLayoutRejectsSocketTagThatDisagreesWithTopology()
    {
        var modules = new List<CityRoadModulePlacement>
        {
            new CityRoadModulePlacement(
                0,
                0,
                0,
                0,
                Vector2.zero,
                CityRoadModuleType.End,
                0,
                false,
                1,
                CityRoadConnectionMask.North,
                new CityRoadSocketSet(
                    CityRoadSocketTag.Road2LaneA,
                    CityRoadSocketTag.SidewalkA,
                    CityRoadSocketTag.ClosedA,
                    CityRoadSocketTag.SidewalkA)),
            new CityRoadModulePlacement(
                1,
                0,
                0,
                1,
                Vector2.up * 10f,
                CityRoadModuleType.End,
                2,
                false,
                1,
                CityRoadConnectionMask.South,
                new CityRoadSocketSet(
                    CityRoadSocketTag.SidewalkA,
                    CityRoadSocketTag.SidewalkA,
                    CityRoadSocketTag.SidewalkA,
                    CityRoadSocketTag.SidewalkA))
        };

        CityRoadLayoutValidationResult validation =
            CityModularRoadPlanner.ValidateLayout(modules);
        Assert.IsFalse(validation.IsValid);
        StringAssert.Contains(
            "incompatible opposing sockets",
            validation.Error);
    }

    [Test]
    public void ModernCityRoadKitUsesSharedCoordinatesAndValidSockets()
    {
        ModernCityRoadModuleLibrary library =
            CreateCompleteModernRoadLibrary();
        try
        {
            Assert.AreEqual(
                RoadModuleNormalizationMode.SharedKitCoordinates,
                library.normalizationMode);
            Assert.IsTrue(
                library.TryValidateGeometry(
                    10f,
                    out CityRoadGeometryCalibration calibration,
                    out string error),
                error);
            Assert.NotNull(calibration);
            Assert.Greater(calibration.SourceCellSize, 0f);
            Assert.Greater(calibration.UniformScale, 0f);
            Assert.That(
                calibration.MaximumSocketError,
                Is.LessThanOrEqualTo(library.SocketPositionTolerance));
            Assert.That(
                calibration.MaximumSurfaceHeightError,
                Is.LessThanOrEqualTo(library.SurfaceHeightTolerance));
        }
        finally
        {
            CityModularRoadMeshFactory.ClearSourceCache();
        }
    }

    [Test]
    public void ModularRoadLayoutValidationRejectsDisconnectedNetworks()
    {
        var modules = new List<CityRoadModulePlacement>
        {
            new CityRoadModulePlacement(
                0,
                0,
                0,
                0,
                Vector2.zero,
                CityRoadModuleType.Straight,
                0,
                true,
                1,
                CityRoadConnectionMask.North
                    | CityRoadConnectionMask.South),
            new CityRoadModulePlacement(
                1,
                0,
                0,
                1,
                Vector2.up * 10f,
                CityRoadModuleType.End,
                2,
                true,
                1,
                CityRoadConnectionMask.South),
            new CityRoadModulePlacement(
                2,
                0,
                0,
                -1,
                Vector2.down * 10f,
                CityRoadModuleType.End,
                0,
                true,
                1,
                CityRoadConnectionMask.North),
            new CityRoadModulePlacement(
                3,
                0,
                4,
                4,
                new Vector2(40f, 40f),
                CityRoadModuleType.End,
                0,
                false,
                1,
                CityRoadConnectionMask.North),
            new CityRoadModulePlacement(
                4,
                0,
                4,
                5,
                new Vector2(40f, 50f),
                CityRoadModuleType.End,
                2,
                false,
                1,
                CityRoadConnectionMask.South)
        };

        CityRoadLayoutValidationResult validation =
            CityModularRoadPlanner.ValidateLayout(modules);
        Assert.IsFalse(validation.IsValid);
        Assert.IsNotEmpty(validation.Error);
    }

    [Test]
    public void ModernCityRoadAssetsUseTwoKTextures()
    {
        const string albedoPath =
            "Assets/ModernCityRoads/Textures/ModernCityRoad_Albedo.png";
        const string normalPath =
            "Assets/ModernCityRoads/Textures/ModernCityRoad_Normal.png";
        const string pathwayPath =
            "Assets/ModernCityRoads/Textures/ModernCityPathway_Albedo.png";
        var albedo = AssetImporter.GetAtPath(albedoPath) as TextureImporter;
        var normal = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        var pathway =
            AssetImporter.GetAtPath(pathwayPath) as TextureImporter;
        Assert.NotNull(albedo);
        Assert.NotNull(normal);
        Assert.NotNull(pathway);
        Assert.IsTrue(albedo.sRGBTexture);
        Assert.IsFalse(normal.sRGBTexture);
        Assert.AreEqual(TextureImporterType.NormalMap, normal.textureType);
        Assert.AreEqual(2048, albedo.maxTextureSize);
        Assert.AreEqual(2048, normal.maxTextureSize);
        Assert.IsTrue(albedo.mipmapEnabled);
        Assert.IsTrue(normal.mipmapEnabled);
        Assert.AreEqual(8, albedo.anisoLevel);
        Assert.AreEqual(8, normal.anisoLevel);
        Assert.AreEqual(2048, pathway.maxTextureSize);
        Assert.IsTrue(pathway.sRGBTexture);
        Assert.IsTrue(pathway.mipmapEnabled);
        Assert.AreEqual(8, pathway.anisoLevel);
        Assert.AreEqual(
            TextureImporterFormat.BC7,
            albedo.GetPlatformTextureSettings("Standalone").format);
        Assert.AreEqual(
            TextureImporterFormat.BC7,
            pathway.GetPlatformTextureSettings("Standalone").format);

        string[] roadModels =
            AssetDatabase.FindAssets(
                "t:Model",
                new[] { "Assets/ModernCityRoads/Models/Road" });
        string[] pathwayModels =
            AssetDatabase.FindAssets(
                "t:Model",
                new[] { "Assets/ModernCityRoads/Models/Pathway" });
        Assert.AreEqual(7, roadModels.Length);
        Assert.AreEqual(16, pathwayModels.Length);
        Assert.NotNull(
            Shader.Find("CityGeneration/SciFiModularRoad"));
    }

    [Test]
    public void ModernCityModulesBakeIntoMergedRendererAndCollider()
    {
        GameObject root = new GameObject("ModernCityModuleBakeTest");
        Material material = new Material(Shader.Find("Standard"));
        var library = new ModernCityRoadModuleLibrary
        {
            roadStraight = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/ModernCityRoads/Models/Road/Road Straight.FBX"),
            pathway4x4 = new[]
            {
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/ModernCityRoads/Models/Pathway/Pathway A 4x4.FBX"),
                null,
                null,
                null
            }
        };
        GameObject roadObject = null;
        GameObject pathwayObject = null;
        try
        {
            var roadChunk = new CityRoadRenderChunk();
            roadChunk.Roads.Add(new CityRoadModulePlacement(
                0,
                0,
                0,
                0,
                Vector2.zero,
                CityRoadModuleType.Straight,
                0,
                true));
            roadObject = CityModularRoadMeshFactory.CreateRoadChunk(
                "RoadChunk",
                roadChunk,
                library,
                Vector2.right,
                10f,
                2f,
                material,
                root.transform);

            var pathwayChunk = new CityRoadRenderChunk();
            pathwayChunk.Pathways.Add(new CityPathwayModulePlacement(
                0,
                0,
                0,
                0,
                Vector2.zero,
                4,
                4,
                0,
                0));
            pathwayObject =
                CityModularRoadMeshFactory.CreatePathwayChunk(
                    "PathwayChunk",
                    pathwayChunk,
                    library,
                    Vector2.right,
                    2.5f,
                    2.1f,
                    material,
                    root.transform);

            Assert.NotNull(roadObject);
            Assert.NotNull(pathwayObject);
            Assert.AreEqual(
                1,
                roadObject.GetComponentsInChildren<MeshRenderer>().Length);
            Assert.AreEqual(
                1,
                pathwayObject.GetComponentsInChildren<MeshRenderer>().Length);
            Assert.NotNull(roadObject.GetComponent<MeshCollider>());
            Assert.NotNull(pathwayObject.GetComponent<MeshCollider>());
            Assert.That(
                roadObject.GetComponent<MeshFilter>().sharedMesh.bounds.size.x,
                Is.LessThanOrEqualTo(10.01f));
            Assert.That(
                roadObject.GetComponent<MeshFilter>().sharedMesh.bounds.size.z,
                Is.LessThanOrEqualTo(10.01f));
            Assert.That(
                pathwayObject.GetComponent<MeshFilter>().sharedMesh.bounds.size.x,
                Is.EqualTo(10f).Within(0.01f));
            Assert.That(
                pathwayObject.GetComponent<MeshFilter>().sharedMesh.bounds.size.z,
                Is.EqualTo(10f).Within(0.01f));
        }
        finally
        {
            if (roadObject != null)
            {
                Object.DestroyImmediate(
                    roadObject.GetComponent<MeshFilter>().sharedMesh);
            }
            if (pathwayObject != null)
            {
                Object.DestroyImmediate(
                    pathwayObject.GetComponent<MeshFilter>().sharedMesh);
            }
            CityModularRoadMeshFactory.ClearSourceCache();
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void SciFiBuildingModelsUseSharedPbrMaterials()
    {
        string[] modelPaths =
        {
            "Assets/CityBuildings/Models/Z_CY_B_002.obj",
            "Assets/CityBuildings/Models/Z_CY_B_005.obj",
            "Assets/CityBuildings/Models/Z_CY_B_010.obj",
            "Assets/CityBuildings/Models/Z_CY_B_012.obj"
        };

        foreach (string modelPath in modelPaths)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Assert.NotNull(model, modelPath);
            MeshRenderer[] renderers =
                model.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(
                1,
                renderers.Length,
                modelPath + " should remain a single renderer.");
            Assert.Greater(renderers[0].sharedMaterials.Length, 1);
            foreach (Material material in renderers[0].sharedMaterials)
            {
                Assert.NotNull(material, modelPath + " has a missing material.");
                Assert.IsTrue(
                    AssetDatabase.GetAssetPath(material)
                        .StartsWith("Assets/CityBuildings/Materials/"),
                    material.name + " is not a shared city material.");
                Assert.AreEqual("Standard", material.shader.name);
            }
        }
    }

    [Test]
    public void SciFiBuildingTexturesUseOneKImportBudget()
    {
        string[] textureGuids = AssetDatabase.FindAssets(
            "t:Texture2D",
            new[] { "Assets/CityBuildings/Textures" });
        Assert.AreEqual(91, textureGuids.Length);

        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.NotNull(texture, path);
            Assert.LessOrEqual(texture.width, 1024, path);
            Assert.LessOrEqual(texture.height, 1024, path);
            Assert.LessOrEqual(importer.maxTextureSize, 1024, path);

            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_Normal"))
            {
                Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType);
                Assert.IsFalse(importer.sRGBTexture, path);
            }
            else if (!name.EndsWith("_Albedo")
                     && !name.EndsWith("_Emission"))
            {
                Assert.IsFalse(importer.sRGBTexture, path);
            }
        }
    }

    static CityRoadConnectionMask FindExpectedCrosswalkEdge(
        CityRoadModulePlacement module,
        IReadOnlyDictionary<Vector2Int, CityRoadModulePlacement> modules,
        IReadOnlyList<CityRoadConnectionMask> directionMasks,
        IReadOnlyList<Vector2Int> directionOffsets)
    {
        Vector2Int coordinate =
            new Vector2Int(module.GridX, module.GridY);
        for (int direction = 0;
             direction < directionMasks.Count;
             direction++)
        {
            if ((module.ConnectionMask
                    & directionMasks[direction]) == 0
                || !modules.TryGetValue(
                    coordinate + directionOffsets[direction],
                    out CityRoadModulePlacement neighbor)
                || CountRoadConnections(
                    neighbor.ConnectionMask) < 3)
            {
                continue;
            }
            return directionMasks[direction];
        }
        return CityRoadConnectionMask.None;
    }

    static int CountRoadConnections(CityRoadConnectionMask mask)
    {
        int value = (int)mask;
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }

    static bool OverlapsAnyRoadModule(
        IReadOnlyList<Vector2> polygon,
        HashSet<Vector2Int> occupiedRoadCells,
        Vector2 gridOrigin,
        Vector2 axis,
        Vector2 perpendicular,
        float cellSize)
    {
        float minimumX = float.MaxValue;
        float maximumX = float.MinValue;
        float minimumY = float.MaxValue;
        float maximumY = float.MinValue;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 delta = polygon[i] - gridOrigin;
            float x = Vector2.Dot(delta, axis) / cellSize;
            float y =
                Vector2.Dot(delta, perpendicular) / cellSize;
            minimumX = Mathf.Min(minimumX, x);
            maximumX = Mathf.Max(maximumX, x);
            minimumY = Mathf.Min(minimumY, y);
            maximumY = Mathf.Max(maximumY, y);
        }

        int firstX = Mathf.FloorToInt(minimumX + 0.5f);
        int lastX = Mathf.CeilToInt(maximumX - 0.5f);
        int firstY = Mathf.FloorToInt(minimumY + 0.5f);
        int lastY = Mathf.CeilToInt(maximumY - 0.5f);
        for (int y = firstY; y <= lastY; y++)
        {
            for (int x = firstX; x <= lastX; x++)
            {
                if (occupiedRoadCells.Contains(
                        new Vector2Int(x, y)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    static ModernCityRoadModuleLibrary CreateCompleteModernRoadLibrary()
    {
        const string roadRoot =
            "Assets/ModernCityRoads/Models/Road/";
        const string pathwayRoot =
            "Assets/ModernCityRoads/Models/Pathway/";
        var library = new ModernCityRoadModuleLibrary
        {
            normalizationMode =
                RoadModuleNormalizationMode.SharedKitCoordinates,
            roadStraight = AssetDatabase.LoadAssetAtPath<GameObject>(
                roadRoot + "Road Straight.FBX"),
            roadStraightCrossing =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    roadRoot + "Road Straight Cross.FBX"),
            roadEnd = AssetDatabase.LoadAssetAtPath<GameObject>(
                roadRoot + "Road End.FBX"),
            roadCurveSmall = AssetDatabase.LoadAssetAtPath<GameObject>(
                roadRoot + "Road Curve Small.FBX"),
            roadCurveLong = AssetDatabase.LoadAssetAtPath<GameObject>(
                roadRoot + "Road Curve Long.FBX"),
            roadT = AssetDatabase.LoadAssetAtPath<GameObject>(
                roadRoot + "Road T.FBX"),
            roadX = AssetDatabase.LoadAssetAtPath<GameObject>(
                roadRoot + "Road X.FBX"),
            roadAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/ModernCityRoads/Textures/"
                + "ModernCityRoad_Albedo.png"),
            roadNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/ModernCityRoads/Textures/"
                + "ModernCityRoad_Normal.png"),
            pathwayAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/ModernCityRoads/Textures/"
                + "ModernCityPathway_Albedo.png")
        };

        for (int variant = 0; variant < 4; variant++)
        {
            char label = (char)('A' + variant);
            library.pathway1x1[variant] =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    pathwayRoot + $"Pathway {label} 1x1.FBX");
            library.pathway2x1[variant] =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    pathwayRoot + $"Pathway {label} 2x1.FBX");
            library.pathway2x2[variant] =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    pathwayRoot + $"Pathway {label} 2x2.FBX");
            library.pathway4x4[variant] =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    pathwayRoot + $"Pathway {label} 4x4.FBX");
        }
        return library;
    }

    static void AssertValid(IReadOnlyList<Vector2> points)
    {
        Assert.IsTrue(
            CityPolygonGeometry.ValidateBoundary(points, Settings, out string error),
            error);
    }

    static void ConfigureTestTerrain(
        CityNoiseTerrain terrain,
        int resolution,
        bool hydrology)
    {
        var serialized = new SerializedObject(terrain);
        serialized.FindProperty("resolution").intValue = resolution;
        serialized.FindProperty("selectBestPatch").boolValue = false;
        serialized.FindProperty("generateHydrology").boolValue = hydrology;
        serialized.FindProperty("riverCount").intValue = 2;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    sealed class SyntheticTerrainSampler : ICityTerrainSampler
    {
        readonly bool allWater;

        public SyntheticTerrainSampler(bool allWater = false)
        {
            this.allWater = allWater;
        }

        public CityTerrainSample SampleTerrain(Vector2 point)
        {
            float ground = point.x * 0.18f
                + Mathf.Sin(point.y * 0.045f) * 2.2f;
            bool water = allWater || Mathf.Abs(point.x) < 13f;
            if (water)
            {
                return new CityTerrainSample(
                    ground - 3f,
                    1f,
                    1f,
                    Mathf.Max(0.5f, 4f - ground),
                    Vector3.up,
                    0f,
                    allWater ? CitySurfaceKind.Lake : CitySurfaceKind.River);
            }

            Vector3 normal = new Vector3(
                -0.18f,
                1f,
                -Mathf.Cos(point.y * 0.045f) * 0.099f).normalized;
            float grade = Mathf.Sqrt(
                0.18f * 0.18f
                + Mathf.Pow(Mathf.Cos(point.y * 0.045f) * 0.099f, 2f));
            return new CityTerrainSample(
                ground,
                ground,
                ground,
                0f,
                normal,
                grade,
                CitySurfaceKind.Land);
        }
    }

    static void AssertInvalid(IReadOnlyList<Vector2> points)
    {
        Assert.IsFalse(
            CityPolygonGeometry.ValidateBoundary(points, Settings, out _));
    }

    static void AssertFootprintInside(
        IReadOnlyList<Vector2> boundary,
        IReadOnlyList<Vector2> footprint,
        string label)
    {
        Assert.IsTrue(
            CityPolygonGeometry.ContainsPolygon(boundary, footprint),
            label + " crossed the selected boundary.");
    }

    static void AssertInsideAnyRegion(
        IReadOnlyList<List<Vector2>> regions,
        IReadOnlyList<Vector2> footprint,
        string label)
    {
        Assert.IsTrue(
            regions.Any(region =>
                CityPolygonGeometry.ContainsPolygon(region, footprint)),
            label + " crossed all resolved regions.");
    }

    static Vector2 Average(IReadOnlyList<Vector2> points)
    {
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
            sum += points[i];
        return sum / points.Count;
    }

    static bool HasOnlyRightAngleCorners(IReadOnlyList<Vector2> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 incoming =
                (polygon[i] - polygon[(i - 1 + polygon.Count) % polygon.Count])
                .normalized;
            Vector2 outgoing =
                (polygon[(i + 1) % polygon.Count] - polygon[i])
                .normalized;
            if (Mathf.Abs(Vector2.Dot(incoming, outgoing)) > 0.035f)
                return false;
        }
        return true;
    }

    static Vector2[] CreateSquareBoundary()
    {
        return new[]
        {
            new Vector2(-96f, -96f),
            new Vector2(96f, -96f),
            new Vector2(96f, 96f),
            new Vector2(-96f, 96f)
        };
    }

    static Vector2[] CreateConcaveBoundary()
    {
        return new[]
        {
            new Vector2(-110f, -90f),
            new Vector2(110f, -90f),
            new Vector2(110f, 90f),
            new Vector2(35f, 90f),
            new Vector2(35f, 15f),
            new Vector2(-35f, 15f),
            new Vector2(-35f, 90f),
            new Vector2(-110f, 90f)
        };
    }

    static Vector2[] CreateBowTieBoundary()
    {
        return new[]
        {
            new Vector2(-120f, 100f),
            new Vector2(120f, 100f),
            new Vector2(-120f, -100f),
            new Vector2(120f, -100f)
        };
    }

    static Vector2[] CreateFivePointStarBoundary()
    {
        var outer = new Vector2[5];
        for (int i = 0; i < outer.Length; i++)
        {
            float radians = (90f - i * 72f) * Mathf.Deg2Rad;
            outer[i] = new Vector2(
                Mathf.Cos(radians),
                Mathf.Sin(radians)) * 135f;
        }
        return new[]
        {
            outer[0],
            outer[2],
            outer[4],
            outer[1],
            outer[3]
        };
    }
}
