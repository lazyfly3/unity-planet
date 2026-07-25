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
    public void BoundaryValidationRejectsDuplicatesAndDegenerateArea()
    {
        AssertInvalid(new[]
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
    public void PlaneBuildingIsUprightAndTouchesSharedPlatform()
    {
        var root = new GameObject("PlaneBuildingTest");
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
            Assert.That(bounds.min.y, Is.EqualTo(12.03f).Within(0.001f));
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
            var constructionAnimator =
                controller.GetComponent<CityConstructionAnimator>();
            Assert.NotNull(constructionAnimator);
            Assert.That(
                constructionAnimator.TotalDuration,
                Is.EqualTo(10f).Within(0.0001f));
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
    public void ConstructionTimelineUsesTenSecondsAndStableDistanceOrder()
    {
        var gameObject = new GameObject("ConstructionAnimatorTest");
        try
        {
            var animator =
                gameObject.AddComponent<CityConstructionAnimator>();
            Assert.That(
                animator.TotalDuration,
                Is.EqualTo(10f).Within(0.0001f));

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
        }
        finally
        {
            Object.DestroyImmediate(gameObject);
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
}
