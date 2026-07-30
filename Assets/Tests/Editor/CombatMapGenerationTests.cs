using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CombatMap;
using UnityPlanet.CombatMap.Editor;
using UnityPlanet.ModularAssembly;

public sealed class CombatMapGenerationTests
{
    static readonly Vector3 DefaultCenter =
        new Vector3(0f, 0f, 1000f);

    [Test]
    public void SameSeedProducesSameSemanticsHeightsAndChecksum()
    {
        AirCombatMapSettings settings =
            AirCombatMapSettings.CreateDefault();
        CombatMapGenerationResult first = CombatMapGenerator.Generate(
            settings,
            DefaultCenter,
            settings.seed);
        CombatMapGenerationResult repeated = CombatMapGenerator.Generate(
            settings,
            DefaultCenter,
            settings.seed);

        Assert.AreEqual(first.derivedSeed, repeated.derivedSeed);
        Assert.AreEqual(first.plan.checksum, repeated.plan.checksum);
        AssertPlansEqual(first.plan, repeated.plan);

        const int sampleSide = 9;
        float half = settings.mapSize * 0.5f;
        for (int z = 0; z < sampleSide; z++)
        for (int x = 0; x < sampleSide; x++)
        {
            float worldX = DefaultCenter.x
                + Mathf.Lerp(-half, half, x / 8f);
            float worldZ = DefaultCenter.z
                + Mathf.Lerp(-half, half, z / 8f);
            float firstHeight = CombatMapGenerator.SampleHeight(
                settings,
                first.plan,
                worldX,
                worldZ);
            float repeatedHeight = CombatMapGenerator.SampleHeight(
                settings,
                repeated.plan,
                worldX,
                worldZ);
            Assert.That(
                repeatedHeight,
                Is.EqualTo(firstHeight).Within(0.00001f),
                $"Height changed at ({worldX}, {worldZ}).");
        }
    }

    [Test]
    public void DifferentSeedsChangeLayoutButKeepThreePrimaryRoutes()
    {
        AirCombatMapSettings settings =
            AirCombatMapSettings.CreateDefault();
        CombatMapGenerationResult first = CombatMapGenerator.Generate(
            settings,
            DefaultCenter,
            settings.seed);
        CombatMapGenerationResult second = CombatMapGenerator.Generate(
            settings,
            DefaultCenter,
            settings.seed + 1);

        Assert.AreNotEqual(first.plan.checksum, second.plan.checksum);
        AssertThreePrimaryRoutes(first.plan);
        AssertThreePrimaryRoutes(second.plan);
    }

    [Test]
    public void DefaultSeedCommitsAboveThresholdAndBlocksOpeningSightline()
    {
        AirCombatMapSettings settings =
            AirCombatMapSettings.CreateDefault();
        CombatMapGenerationResult result = CombatMapGenerator.GenerateBest(
            settings,
            DefaultCenter,
            settings.seed);

        Assert.IsNotNull(result);
        Assert.IsTrue(
            result.CanCommit,
            FormatViolations(result.validation));
        Assert.That(
            result.validation.score,
            Is.GreaterThanOrEqualTo(80f));
        Assert.IsFalse(result.validation.HasHardErrors);

        CombatSemanticAnchor player = result.plan.FindAnchor(
            CombatAnchorType.PlayerSpawn);
        CombatSemanticAnchor enemy = result.plan.FindAnchor(
            CombatAnchorType.EnemySpawn);
        Assert.IsNotNull(player);
        Assert.IsNotNull(enemy);
        Assert.IsFalse(
            CombatMapGenerator.HasTerrainLineOfSight(
                settings,
                result.plan,
                player.position,
                enemy.position),
            "The central terrain must mask the opening spawn sightline.");

        AssertSpawnClearance(settings, result.plan, player);
        AssertSpawnClearance(settings, result.plan, enemy);
    }

    [Test]
    public void PrimaryRoutesUseDisjointSemanticVolumes()
    {
        AirCombatMapSettings settings =
            AirCombatMapSettings.CreateDefault();
        CombatSemanticPlan plan = CombatMapGenerator.GenerateBest(
            settings,
            DefaultCenter,
            settings.seed).plan;
        CombatSemanticRoute main = plan.FindRoute(
            CombatRouteType.Main);
        CombatSemanticRoute west = plan.FindRoute(
            CombatRouteType.TerrainMaskedFlank);
        CombatSemanticRoute east = plan.FindRoute(
            CombatRouteType.LongRange);

        AssertRouteHasIntermediateNodes(main);
        AssertRouteHasIntermediateNodes(west);
        AssertRouteHasIntermediateNodes(east);
        Assert.That(
            main.controlVolumeIds.Length,
            Is.GreaterThanOrEqualTo(5));
        Assert.That(
            west.controlVolumeIds.Length,
            Is.GreaterThanOrEqualTo(5));
        Assert.That(
            east.controlVolumeIds.Length,
            Is.GreaterThanOrEqualTo(5));

        var mainInternal = new HashSet<string>(
            main.controlVolumeIds.Skip(1)
                .Take(main.controlVolumeIds.Length - 2));
        var westInternal = new HashSet<string>(
            west.controlVolumeIds.Skip(1)
                .Take(west.controlVolumeIds.Length - 2));
        var eastInternal = new HashSet<string>(
            east.controlVolumeIds.Skip(1)
                .Take(east.controlVolumeIds.Length - 2));
        Assert.IsFalse(mainInternal.Overlaps(westInternal));
        Assert.IsFalse(mainInternal.Overlaps(eastInternal));
        Assert.IsFalse(westInternal.Overlaps(eastInternal));

        Assert.That(
            plan.tacticalVolumes.Count(volume =>
                volume != null
                && volume.type
                == CombatTacticalVolumeType.ManeuverBowl),
            Is.EqualTo(3));
        Assert.That(
            plan.tacticalVolumes.Count(volume =>
                volume != null
                && volume.type
                == CombatTacticalVolumeType.RecoveryPocket),
            Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void V2MetricsValidateTurnSpaceTopologyAndLosLayers()
    {
        AirCombatMapSettings settings =
            AirCombatMapSettings.CreateDefault();
        CombatMapGenerationResult result =
            CombatMapGenerator.GenerateBest(
                settings,
                DefaultCenter,
                settings.seed);
        CombatMapValidationReport report = result.validation;

        Assert.IsTrue(result.CanCommit, FormatViolations(report));
        Assert.That(report.topologyScore, Is.GreaterThanOrEqualTo(60f));
        Assert.That(report.kinematicScore, Is.GreaterThanOrEqualTo(60f));
        Assert.That(
            report.minimumTurnRadius,
            Is.GreaterThanOrEqualTo(
                settings.designTurnRadius * 0.78f));
        Assert.That(
            report.minimumManeuverDiameter,
            Is.GreaterThanOrEqualTo(
                settings.designTurnRadius * 2.5f));
        Assert.That(report.minimumExitCount, Is.GreaterThanOrEqualTo(2));
        Assert.AreEqual(0, report.articulationPointCount);
        Assert.That(report.routeUsageEntropy, Is.GreaterThan(0.65f));
        Assert.AreEqual(4, report.lineOfSightOpenFractions.Length);
        Assert.IsTrue(
            report.lineOfSightOpenFractions.All(value =>
                value >= 0f && value <= 1f));
    }

    [Test]
    public void IncompatibleWeaponScaleCannotCommit()
    {
        AirCombatMapSettings settings =
            AirCombatMapSettings.CreateDefault();
        settings.designWeaponRange = settings.mapSize;
        settings.Clamp();

        CombatMapGenerationResult result =
            CombatMapGenerator.Generate(
                settings,
                DefaultCenter,
                settings.seed);

        Assert.IsFalse(result.CanCommit);
        Assert.IsTrue(result.validation.HasHardErrors);
        Assert.IsTrue(
            result.validation.violations.Any(value =>
                value != null
                && value.code == "scale.weapon-turn-envelope"));
    }

[Test]
    public void GeneratedChunkMeshesHaveFiniteContinuousSeams()
    {
        AirCombatMapRecipe recipe =
            ScriptableObject.CreateInstance<AirCombatMapRecipe>();
        var root = new GameObject("CombatMapMeshTest");
        root.SetActive(false);
        CombatMapRuntimeController controller =
            root.AddComponent<CombatMapRuntimeController>();
        controller.Configure(recipe);

        try
        {
            root.SetActive(true);
            if (!controller.IsReady)
                Assert.IsTrue(controller.Rebuild());
            Assert.IsTrue(
                controller.IsReady,
                FormatViolations(controller.CurrentValidation));

            Transform terrainRoot = root.transform.Find(
                CombatMapRuntimeController.GeneratedRootName +
                "/TerrainChunks");
            Assert.IsNotNull(terrainRoot);

            AirCombatMapSettings settings = controller.CurrentSettings;
            int chunkCount = Mathf.CeilToInt(
                settings.mapSize / settings.chunkSize);
            int resolution = settings.chunkResolution;
            int side = resolution + 1;
            MeshFilter[] filters = terrainRoot
                .GetComponentsInChildren<MeshFilter>(true);
            Assert.AreEqual(chunkCount * chunkCount, filters.Length);

            var chunks = new Dictionary<string, MeshFilter>();
            foreach (MeshFilter filter in filters)
            {
                Assert.IsNotNull(filter.sharedMesh);
                Assert.IsTrue(
                    chunks.TryAdd(filter.gameObject.name, filter),
                    "Duplicate generated chunk: " +
                    filter.gameObject.name);
                AssertFiniteMesh(filter.sharedMesh);
                Assert.AreEqual(
                    0,
                    filter.GetComponents<MeshCollider>().Length,
                    "Render chunks must not create physical seams.");
            }

            Transform collision = terrainRoot.Find("CollisionSurface");
            Assert.IsNotNull(collision);
            MeshCollider[] terrainColliders =
                terrainRoot.GetComponentsInChildren<MeshCollider>(true);
            Assert.AreEqual(1, terrainColliders.Length);
            Assert.AreSame(
                collision.GetComponent<MeshCollider>(),
                terrainColliders[0]);
            Assert.IsFalse(terrainColliders[0].isTrigger);
            Assert.IsFalse(terrainColliders[0].convex);
            Assert.AreEqual(
                LayerMask.NameToLayer("CombatTerrain"),
                collision.gameObject.layer);

            for (int z = 0; z < chunkCount; z++)
            for (int x = 0; x < chunkCount - 1; x++)
            {
                MeshFilter left = chunks[ChunkName(x, z)];
                MeshFilter right = chunks[ChunkName(x + 1, z)];
                for (int edge = 0; edge <= resolution; edge++)
                {
                    AssertSeamVertex(
                        left,
                        edge * side + resolution,
                        right,
                        edge * side);
                }
            }

            for (int z = 0; z < chunkCount - 1; z++)
            for (int x = 0; x < chunkCount; x++)
            {
                MeshFilter lower = chunks[ChunkName(x, z)];
                MeshFilter upper = chunks[ChunkName(x, z + 1)];
                for (int edge = 0; edge <= resolution; edge++)
                {
                    AssertSeamVertex(
                        lower,
                        resolution * side + edge,
                        upper,
                        edge);
                }
            }

            Collider[] debugColliders = root.transform.Find(
                    CombatMapRuntimeController.GeneratedRootName +
                    "/SemanticOverlay")
                .GetComponentsInChildren<Collider>(true);
            Assert.IsTrue(debugColliders.All(value => !value.enabled));

            Physics.SyncTransforms();
            Vector3 center = controller.CurrentPlan.mapCenter;
            int terrainMask = 1 << LayerMask.NameToLayer("CombatTerrain");
            for (int z = -2; z <= 2; z++)
            for (int x = -2; x <= 2; x++)
            {
                Vector3 sample = center + new Vector3(
                    x * settings.mapSize * 0.08f,
                    500f,
                    z * settings.mapSize * 0.08f);
                Assert.IsTrue(Physics.Raycast(
                    sample,
                    Vector3.down,
                    out RaycastHit hit,
                    1000f,
                    terrainMask,
                    QueryTriggerInteraction.Ignore));
                float collisionHeight =
                    controller.SampleCollisionHeight(sample);
                Assert.That(
                    hit.point.y,
                    Is.EqualTo(collisionHeight).Within(0.05f));
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(recipe);
        }
    }

    [Test]
    public void CombatMapLabSceneIsIsolatedUniqueAndNotInBuildSettings()
    {
        const string path = CombatMapLabAssetBuilder.ScenePath;
        Assert.IsNotNull(
            AssetDatabase.LoadAssetAtPath<SceneAsset>(path),
            "CombatMapLab scene asset has not been generated.");
        Assert.AreEqual(
            -1,
            SceneUtility.GetBuildIndexByScenePath(path));
        Assert.IsFalse(
            EditorBuildSettings.scenes.Any(scene => scene.path == path));

        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedForTest = !scene.IsValid() || !scene.isLoaded;
        if (openedForTest)
        {
            scene = EditorSceneManager.OpenScene(
                path,
                OpenSceneMode.Additive);
        }

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            Camera[] cameras = ComponentsInScene<Camera>(roots);
            Light[] directionalLights = ComponentsInScene<Light>(roots)
                .Where(light => light.type == LightType.Directional)
                .ToArray();
            ModularLabSceneProfile[] profiles =
                ComponentsInScene<ModularLabSceneProfile>(roots);
            CombatMapRuntimeController[] controllers =
                ComponentsInScene<CombatMapRuntimeController>(roots);
            PlanetLabFlightEnvironmentController[] planetEnvironments =
                ComponentsInScene<
                    PlanetLabFlightEnvironmentController>(roots);
            CombatMapFlightEnvironmentController[] combatEnvironments =
                ComponentsInScene<
                    CombatMapFlightEnvironmentController>(roots);
            Transform[] generatedRoots = ComponentsInScene<Transform>(
                    roots)
                .Where(transform =>
                    transform.name
                    == CombatMapRuntimeController.GeneratedRootName)
                .ToArray();

            Assert.AreEqual(1, cameras.Length);
            Assert.AreEqual(1, directionalLights.Length);
            Assert.AreEqual(1, profiles.Length);
            Assert.AreEqual(1, controllers.Length);
            Assert.AreEqual(1, generatedRoots.Length);
            Assert.AreEqual(0, planetEnvironments.Length);
            Assert.AreEqual(1, combatEnvironments.Length);

            Assert.IsTrue(profiles[0].EnableBuildExperience);
            Assert.IsFalse(
                profiles[0].EnableCombatTest,
                "The first CombatMapLab milestone is generation-only.");
            Assert.IsFalse(
                profiles[0].EnablePlanetLabFlightEnvironment);
            Assert.IsTrue(
                profiles[0].EnableCombatMapFlightEnvironment);
            var controllerState = new SerializedObject(controllers[0]);
            Assert.IsFalse(
                controllerState.FindProperty("showRuntimePanel").boolValue,
                "CombatMapLab must not cover the build UI by default.");
            Assert.IsFalse(
                controllerState.FindProperty("showSemanticOverlay").boolValue,
                "Semantic route ribbons must be opt-in in Game view.");
            var environmentState = new SerializedObject(combatEnvironments[0]);
            Assert.IsNull(
                environmentState.FindProperty("showRuntimeDiagnostics"),
                "The obsolete overlapping flight diagnostics panel must not return.");

            Assert.IsNotNull(controllers[0].Recipe);
            Assert.IsTrue(
                controllers[0].IsReady,
                FormatViolations(controllers[0].CurrentValidation));
        }
        finally
        {
            if (openedForTest)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void AssertPlansEqual(
        CombatSemanticPlan first,
        CombatSemanticPlan repeated)
    {
        Assert.AreEqual(first.seed, repeated.seed);
        Assert.AreEqual(first.mapCenter, repeated.mapCenter);
        Assert.AreEqual(first.mapSize, repeated.mapSize);
        Assert.AreEqual(
            first.topologyVariant,
            repeated.topologyVariant);
        Assert.AreEqual(first.anchors.Length, repeated.anchors.Length);
        Assert.AreEqual(
            first.tacticalVolumes.Length,
            repeated.tacticalVolumes.Length);
        Assert.AreEqual(first.routes.Length, repeated.routes.Length);
        Assert.AreEqual(
            first.terrainStamps.Length,
            repeated.terrainStamps.Length);
        Assert.AreEqual(first.occluders.Length, repeated.occluders.Length);

        for (int index = 0; index < first.anchors.Length; index++)
        {
            CombatSemanticAnchor a = first.anchors[index];
            CombatSemanticAnchor b = repeated.anchors[index];
            Assert.AreEqual(a.stableId, b.stableId);
            Assert.AreEqual(a.type, b.type);
            Assert.AreEqual(a.position, b.position);
            Assert.AreEqual(a.forward, b.forward);
            Assert.AreEqual(a.radius, b.radius);
        }

        for (int index = 0;
             index < first.tacticalVolumes.Length;
             index++)
        {
            CombatTacticalVolume a = first.tacticalVolumes[index];
            CombatTacticalVolume b = repeated.tacticalVolumes[index];
            Assert.AreEqual(a.stableId, b.stableId);
            Assert.AreEqual(a.type, b.type);
            Assert.AreEqual(a.position, b.position);
            Assert.AreEqual(a.size, b.size);
            Assert.AreEqual(
                a.preferredClearance,
                b.preferredClearance);
            Assert.AreEqual(a.teamBias, b.teamBias);
        }

        for (int index = 0; index < first.routes.Length; index++)
        {
            CombatSemanticRoute a = first.routes[index];
            CombatSemanticRoute b = repeated.routes[index];
            Assert.AreEqual(a.stableId, b.stableId);
            Assert.AreEqual(a.type, b.type);
            Assert.AreEqual(a.width, b.width);
            Assert.AreEqual(
                a.intendedExposure,
                b.intendedExposure);
            CollectionAssert.AreEqual(
                a.controlVolumeIds,
                b.controlVolumeIds);
            CollectionAssert.AreEqual(a.waypoints, b.waypoints);
        }

        for (int index = 0;
             index < first.terrainStamps.Length;
             index++)
        {
            CombatTerrainStamp a = first.terrainStamps[index];
            CombatTerrainStamp b = repeated.terrainStamps[index];
            Assert.AreEqual(a.stableId, b.stableId);
            Assert.AreEqual(a.type, b.type);
            Assert.AreEqual(a.start, b.start);
            Assert.AreEqual(a.end, b.end);
            Assert.AreEqual(a.radius, b.radius);
            Assert.AreEqual(a.falloff, b.falloff);
            Assert.AreEqual(a.height, b.height);
        }

        for (int index = 0; index < first.occluders.Length; index++)
        {
            CombatOccluderData a = first.occluders[index];
            CombatOccluderData b = repeated.occluders[index];
            Assert.AreEqual(a.stableId, b.stableId);
            Assert.AreEqual(a.type, b.type);
            Assert.AreEqual(a.position, b.position);
            Assert.AreEqual(a.size, b.size);
        }
    }

    static void AssertThreePrimaryRoutes(CombatSemanticPlan plan)
    {
        Assert.IsNotNull(plan.FindRoute(CombatRouteType.Main));
        Assert.IsNotNull(
            plan.FindRoute(CombatRouteType.TerrainMaskedFlank));
        Assert.IsNotNull(plan.FindRoute(CombatRouteType.LongRange));
    }

    static void AssertSpawnClearance(
        AirCombatMapSettings settings,
        CombatSemanticPlan plan,
        CombatSemanticAnchor spawn)
    {
        float ground = CombatMapGenerator.SampleHeight(
            settings,
            plan,
            spawn.position.x,
            spawn.position.z);
        float clearance = spawn.position.y - ground;
        Assert.That(
            clearance,
            Is.InRange(
                settings.minimumGroundClearance,
                settings.maximumGroundClearance));
    }

    static void AssertRouteHasIntermediateNodes(
        CombatSemanticRoute route)
    {
        Assert.IsNotNull(route);
        Assert.IsNotNull(route.waypoints);
        Assert.That(route.waypoints.Length, Is.GreaterThanOrEqualTo(3));
    }

    static Vector3 MiddleWaypoint(CombatSemanticRoute route)
    {
        return route.waypoints[route.waypoints.Length / 2];
    }

    static float MinimumIntermediateSeparation(
        CombatSemanticRoute first,
        CombatSemanticRoute second)
    {
        float minimum = float.PositiveInfinity;
        for (int a = 1; a < first.waypoints.Length - 1; a++)
        for (int b = 1; b < second.waypoints.Length - 1; b++)
        {
            var firstPoint = new Vector2(
                first.waypoints[a].x,
                first.waypoints[a].z);
            var secondPoint = new Vector2(
                second.waypoints[b].x,
                second.waypoints[b].z);
            minimum = Mathf.Min(
                minimum,
                Vector2.Distance(firstPoint, secondPoint));
        }
        return minimum;
    }

    static void AssertFiniteMesh(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Assert.AreEqual(vertices.Length, normals.Length);
        foreach (Vector3 vertex in vertices)
            Assert.IsTrue(IsFinite(vertex), "Non-finite mesh vertex.");
        foreach (Vector3 normal in normals)
        {
            Assert.IsTrue(IsFinite(normal), "Non-finite mesh normal.");
            Assert.That(normal.sqrMagnitude, Is.GreaterThan(0.99f));
        }
    }

    static void AssertSeamVertex(
        MeshFilter first,
        int firstIndex,
        MeshFilter second,
        int secondIndex)
    {
        Vector3 firstWorld = first.transform.TransformPoint(
            first.sharedMesh.vertices[firstIndex]);
        Vector3 secondWorld = second.transform.TransformPoint(
            second.sharedMesh.vertices[secondIndex]);
        Assert.That(
            Vector3.Distance(firstWorld, secondWorld),
            Is.LessThan(0.0001f),
            first.gameObject.name + " / "
            + second.gameObject.name + " has a height crack.");

        Vector3 firstNormal = first.transform.TransformDirection(
            first.sharedMesh.normals[firstIndex]).normalized;
        Vector3 secondNormal = second.transform.TransformDirection(
            second.sharedMesh.normals[secondIndex]).normalized;
        Assert.That(
            Vector3.Distance(firstNormal, secondNormal),
            Is.LessThan(0.0001f),
            first.gameObject.name + " / "
            + second.gameObject.name + " has a normal seam.");
    }

    static string ChunkName(int x, int z)
    {
        return "Chunk_" + x.ToString("D2")
            + "_" + z.ToString("D2");
    }

    static T[] ComponentsInScene<T>(GameObject[] roots)
        where T : Component
    {
        return roots
            .SelectMany(root =>
                root.GetComponentsInChildren<T>(true))
            .ToArray();
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x)
            && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y)
            && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z)
            && !float.IsInfinity(value.z);
    }

    static string FormatViolations(
        CombatMapValidationReport report)
    {
        if (report?.violations == null
            || report.violations.Length == 0)
        {
            return "No validation details.";
        }
        return $"Score={report.score:0.0}, "
            + $"Topology={report.topologyScore:0.0}, "
            + $"Kinematic={report.kinematicScore:0.0}, "
            + $"Rhythm={report.coverRhythmScore:0.0}, "
            + $"TurnR={report.minimumTurnRadius:0.0}, "
            + $"BowlD={report.minimumManeuverDiameter:0.0}, "
            + $"Occlusion={report.meanOcclusionSeconds:0.0}s, "
            + $"Exposure={report.maximumExposureSeconds:0.0}s, "
            + $"Eyes={report.globalEyePointCount}, "
            + $"Entropy={report.routeUsageEntropy:0.00}\n"
            + string.Join(
            "\n",
            report.violations
                .Where(value => value != null)
                .Select(value =>
                    value.severity + " " + value.code
                    + ": " + value.message));
    }


[Test]
    public void MeasuredEnvelopeDerivesConservativeMapAndZones()
    {
        AirCombatMapSettings authored =
            AirCombatMapSettings.CreateDefault();
        var envelope = new CombatMapFlightEnvelope
        {
            blueprintFingerprint = "TEST",
            localBounds = new Bounds(
                Vector3.zero,
                new Vector3(7f, 6f, 6f)),
            horizontalSpan = 7f,
            verticalSpan = 6f,
            hullRadius = 5.5f,
            measurementCompleted = true,
            usedFallback = false,
            sourceAircraftUnchanged = true,
            measuredSpeed = 55f,
            turnRadius = 95f,
            confidence = 0.9f
        };

        CombatMapScalePlan plan = CombatMapScalePlanner.Build(
            authored,
            envelope);
        Assert.IsNotNull(plan);
        Assert.IsNotNull(plan.settings);
        Assert.That(
            plan.settings.mapSize,
            Is.GreaterThanOrEqualTo(
                plan.clearanceTurnRadius * 16f));
        Assert.That(
            plan.settings.mainRouteWidth,
            Is.GreaterThanOrEqualTo(
                plan.clearanceTurnRadius * 2f));
        Assert.That(
            plan.maneuverBowlDiameter,
            Is.GreaterThanOrEqualTo(
                plan.clearanceTurnRadius * 4.2f));
        Assert.That(
            plan.settings.warningRadius,
            Is.LessThan(plan.settings.forfeitRadius));
        float nearEdge = plan.settings.mapCenterOffset.z -
                         plan.settings.mapSize * 0.5f;
        Assert.That(nearEdge, Is.GreaterThanOrEqualTo(128f));

        CombatMapGenerationResult generated =
            CombatMapGenerator.GenerateBest(
                plan.settings,
                plan.settings.mapCenterOffset,
                plan.settings.seed);
        Assert.IsTrue(
            generated.CanCommit,
            FormatViolations(generated.validation));
        Assert.That(
            generated.validation.minimumManeuverDiameter,
            Is.GreaterThanOrEqualTo(
                plan.settings.designTurnRadius * 4f));
    }
}
