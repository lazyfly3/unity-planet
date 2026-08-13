using NUnit.Framework;
using SpacecraftEditor;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.CombatMap;
using System.Reflection;

public sealed class InfinitePlanarSurfaceRuntimeTests
{
    [Test]
    public void UrbanCombatCenterUsesManeuverBowlInsteadOfEdgeFacility()
    {
        var city = new AirCombatCityPlan
        {
            objective = new Vector3(0f, 135f, 652f)
        };
        city.volumes.Add(new AirCombatTacticalVolume
        {
            kind = AirCombatVolumeKind.ManeuverBowl,
            center = new Vector3(0f, 135f, 0f),
            size = new Vector3(300f, 350f, 300f)
        });

        Vector3 center =
            FinitePlanetUrbanCombatRuntime.ResolveFormalCombatCenter(
                city,
                12f);

        Assert.That(center, Is.EqualTo(new Vector3(0f, 147f, 0f)));
        Assert.That(center.z, Is.Not.EqualTo(city.objective.z));
    }

    [Test]
    public void FiniteCombatModeClampsArenaInsideFixedTerrainWindow()
    {
        var root = new GameObject("FiniteCombatWorldTest");
        try
        {
            InfinitePlanarSurfaceWorld world =
                root.AddComponent<InfinitePlanarSurfaceWorld>();

            world.ConfigureFiniteCombatMode(10000f, 20f);

            float terrainHalfExtent =
                (InfinitePlanarSurfaceWorld.FiniteCombatViewRadius + 0.5f)
                * InfinitePlanarSurfaceWorld.FiniteCombatChunkSize;
            Assert.IsTrue(world.IsFiniteCombatArea);
            Assert.AreEqual(
                terrainHalfExtent
                - InfinitePlanarSurfaceWorld
                    .FiniteCombatTerrainEdgeClearance,
                world.FiniteCombatRadius);
            Assert.AreEqual(80f, world.FiniteCombatFlightCeilingHeight);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void UrbanFoundationUsesFlatLightweightReadinessChunks()
    {
        var root = new GameObject("UrbanFlatFoundationTest");
        ProceduralPlanetPreset preset =
            ScriptableObject.CreateInstance<ProceduralPlanetPreset>();
        try
        {
            preset.ApplyTemplate(
                ProceduralPlanetLabTemplate.TemperateOcean);
            GalaxyPlanetDefinition definition = preset.CloneDefinition();
            FinitePlanetCombatTerrainPlan foundation =
                FinitePlanetCombatTerrainPlanner.CreateUrbanFoundation(
                    definition,
                    "industrial_outpost",
                    7319,
                    InfinitePlanarSurfaceWorld.DefaultFiniteCombatRadius,
                    0f,
                    false);

            Assert.That(foundation, Is.Not.Null);
            Assert.That(
                foundation.SemanticPlan.theme,
                Is.EqualTo(CombatMapTheme.Urban));
            Assert.That(foundation.DefenseLayout.IsValid, Is.True);
            Assert.That(foundation.Validation.CanCommit, Is.True);

            var settings = new PlanetLabPlanarSettings
            {
                autoAnchor = false
            };
            settings.SetAnchorDirection(Vector3.up);
            PlanetLabInfiniteTerrainStreamer streamer =
                root.AddComponent<PlanetLabInfiniteTerrainStreamer>();
            streamer.Configure(
                definition,
                settings,
                null,
                null,
                null,
                false,
                InfinitePlanarSurfaceWorld.FiniteCombatViewRadius,
                InfinitePlanarSurfaceWorld.FiniteCombatChunkResolution,
                InfinitePlanarSurfaceWorld.FiniteCombatChunkSize,
                foundation,
                true);

            Assert.That(streamer.UsesFlatCombatSurface, Is.True);
            Assert.That(streamer.ActiveChunkCount, Is.EqualTo(25));
            Assert.That(streamer.IsFullyReady, Is.True);
            Assert.That(
                streamer.SampleHeight(0f, 0f),
                Is.EqualTo(foundation.BaseGroundHeight).Within(0.001f));
            Assert.That(
                streamer.SampleHeight(700f, -700f),
                Is.EqualTo(foundation.BaseGroundHeight).Within(0.001f));

            MeshFilter[] filters =
                root.GetComponentsInChildren<MeshFilter>(true);
            int terrainMeshCount = 0;
            int terrainVertexCount = 0;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh == null ||
                    filter.sharedMesh.name != "UrbanFlatReadinessChunk")
                {
                    continue;
                }
                terrainMeshCount++;
                terrainVertexCount += filter.sharedMesh.vertexCount;
            }
            Assert.That(terrainMeshCount, Is.EqualTo(25));
            Assert.That(terrainVertexCount, Is.EqualTo(100));

            streamer.RefreshAppearance(null, null, true);
            MeshRenderer[] renderers =
                root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.name == "Ocean")
                    Assert.That(renderer.enabled, Is.False);
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(preset);
        }
    }

    [Test]
    public void FiniteCombatCameraFarClipCoversTheWholeFixedTerrainWindow()
    {
        float farClip =
            InfinitePlanarSurfaceWorld
                .CalculateFiniteCombatRequiredFarClip(
                    InfinitePlanarSurfaceWorld.DefaultFiniteCombatRadius,
                    InfinitePlanarSurfaceWorld
                        .DefaultFiniteCombatFlightCeilingHeight,
                    560f);
        float terrainHalfExtent =
            (InfinitePlanarSurfaceWorld.FiniteCombatViewRadius + 0.5f)
            * InfinitePlanarSurfaceWorld.FiniteCombatChunkSize;
        float maximumHorizontalDistance =
            terrainHalfExtent * Mathf.Sqrt(2f)
            + InfinitePlanarSurfaceWorld.DefaultFiniteCombatRadius;
        float requiredDiagonal = Mathf.Sqrt(
            maximumHorizontalDistance * maximumHorizontalDistance
            + 560f * 560f);

        Assert.Greater(farClip, requiredDiagonal);
        Assert.AreEqual(0f, farClip % 100f);
        Assert.Greater(farClip, 2000f);
    }

    [Test]
    public void FiniteCombatContainmentSupportsPcgClearanceMargin()
    {
        var root = new GameObject("FiniteCombatContainmentTest");
        try
        {
            root.transform.position = new Vector3(40f, 0f, -25f);
            InfinitePlanarSurfaceWorld world =
                root.AddComponent<InfinitePlanarSurfaceWorld>();
            world.ConfigureFiniteCombatMode(200f, 220f);

            Assert.That(
                world.FiniteCombatRadius,
                Is.EqualTo(InfinitePlanarSurfaceWorld.FiniteCombatChunkSize));
            Assert.IsTrue(world.ContainsFiniteCombatPoint(
                root.transform.position + Vector3.right * 270f,
                40f));
            Assert.IsFalse(world.ContainsFiniteCombatPoint(
                root.transform.position + Vector3.right * 285f,
                40f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void UrbanFlightCeilingUsesCityMaximumAltitude()
    {
        var root = new GameObject("UrbanFlightCeilingTest");
        try
        {
            InfinitePlanarSurfaceWorld world =
                root.AddComponent<InfinitePlanarSurfaceWorld>();
            world.ConfigureFiniteCombatMode(
                useUrbanFlatSurface: true);

            world.ApplyUrbanFlightCeiling(350f);

            Assert.That(world.UsesUrbanFlatSurface, Is.True);
            Assert.That(
                world.FiniteCombatFlightCeilingHeight,
                Is.EqualTo(350f).Within(0.001f));

            world.ApplyUrbanFlightCeiling(1350f);
            Assert.That(
                world.FiniteCombatFlightCeilingHeight,
                Is.EqualTo(1350f).Within(0.001f),
                "Urban height must not be clamped by hidden terrain.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void UrbanFarClipCoversTheVisualSkylineEnvelope()
    {
        const float MapSize = 1664f;
        const float OutsideDistance = 3200f;
        float farClip =
            InfinitePlanarSurfaceWorld.CalculateUrbanVisualRequiredFarClip(
                MapSize,
                InfinitePlanarSurfaceWorld.DefaultFiniteCombatRadius,
                OutsideDistance,
                470f);
        float farthestSkylineRadius = Mathf.Sqrt(2f) *
                                      (MapSize * 0.5f + OutsideDistance);

        Assert.That(
            farClip,
            Is.GreaterThan(
                farthestSkylineRadius +
                InfinitePlanarSurfaceWorld.DefaultFiniteCombatRadius));
        Assert.That(farClip % 100f, Is.EqualTo(0f));
    }

    [Test]
    public void CitySurveyExtractionDoesNotOverlapItsScanObjectives()
    {
        var root = new GameObject("UrbanSurveyAnchorTest");
        try
        {
            FinitePlanetUrbanCombatRuntime runtime =
                root.AddComponent<FinitePlanetUrbanCombatRuntime>();
            var layout = new FinitePlanetDefenseLayoutPlan
            {
                powerPositions = new[]
                {
                    new CombatSemanticAnchor(),
                    new CombatSemanticAnchor(),
                    new CombatSemanticAnchor()
                },
                retreatPoints = new[]
                {
                    new CombatSemanticAnchor(),
                    new CombatSemanticAnchor()
                }
            };
            var city = new AirCombatCityPlan
            {
                objective = Vector3.zero,
                playerSpawn = Vector3.zero
            };
            city.volumes.Add(new AirCombatTacticalVolume
            {
                kind = AirCombatVolumeKind.RecoveryPocket,
                center = new Vector3(-220f, 0f, 0f),
                size = new Vector3(120f, 220f, 110f)
            });
            city.volumes.Add(new AirCombatTacticalVolume
            {
                kind = AirCombatVolumeKind.RecoveryPocket,
                center = new Vector3(220f, 0f, 0f),
                size = new Vector3(120f, 220f, 110f)
            });
            city.volumes.Add(new AirCombatTacticalVolume
            {
                kind = AirCombatVolumeKind.ManeuverBowl,
                center = Vector3.zero,
                size = new Vector3(300f, 350f, 300f)
            });

            MethodInfo synchronize =
                typeof(FinitePlanetUrbanCombatRuntime).GetMethod(
                    "SynchronizeFormalMissionAnchors",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(synchronize, Is.Not.Null);
            synchronize.Invoke(runtime, new object[] { layout, city });

            Vector2 extraction = new Vector2(
                layout.retreatPoints[0].position.x,
                layout.retreatPoints[0].position.z);
            for (int index = 0; index < layout.powerPositions.Length; index++)
            {
                Vector2 scan = new Vector2(
                    layout.powerPositions[index].position.x,
                    layout.powerPositions[index].position.z);
                Assert.That(
                    Vector2.Distance(extraction, scan),
                    Is.GreaterThan(80f));
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void FiniteCombatBoundaryOnlyRecoversCatastrophicFalls()
    {
        Vector3 center = new Vector3(10f, 25f, -12f);

        Assert.IsFalse(FinitePlanetCombatBoundary.IsOutsideHardBounds(
            center + new Vector3(199f, 100f, 0f),
            center,
            200f,
            245f));
        Assert.IsFalse(FinitePlanetCombatBoundary.IsOutsideHardBounds(
            center + new Vector3(201f, 100f, 0f),
            center,
            200f,
            245f));
        Assert.IsFalse(FinitePlanetCombatBoundary.IsOutsideHardBounds(
            new Vector3(center.x, 246f, center.z),
            center,
            200f,
            245f));
        Assert.IsTrue(FinitePlanetCombatBoundary.IsOutsideHardBounds(
            center + Vector3.down * 81f,
            center,
            200f,
            245f));
    }

    [Test]
    public void FiniteCombatSoftCeilingBuildsForceWithoutTeleportThreshold()
    {
        Assert.AreEqual(
            0f,
            FinitePlanetCombatBoundary.CalculateSoftCeilingAcceleration(
                300f,
                420f));
        float insideBand =
            FinitePlanetCombatBoundary.CalculateSoftCeilingAcceleration(
                380f,
                420f);
        float atCeiling =
            FinitePlanetCombatBoundary.CalculateSoftCeilingAcceleration(
                420f,
                420f);
        float above =
            FinitePlanetCombatBoundary.CalculateSoftCeilingAcceleration(
                450f,
                420f);

        Assert.Greater(insideBand, 0f);
        Assert.Greater(atCeiling, insideBand);
        Assert.Greater(above, atCeiling);
    }

    [TestCase("abandoned_mine", 4)]
    [TestCase("wind_canyon", 5)]
    [TestCase("industrial_outpost", 6)]
    [TestCase("modular_boss", 6)]
    public void FinitePlanetDefensePcgIsOffCenterAndUsesOuterIngresses(
        string missionId,
        int expectedIngresses)
    {
        const float radius = 760f;
        var definition = new GalaxyPlanetDefinition
        {
            planetId = "finite-defence-test",
            seed = 918273,
            climate = PlanetClimate.Tundra
        };

        FinitePlanetCombatTerrainPlan plan =
            FinitePlanetCombatTerrainPlanner.Create(
                definition,
                missionId,
                271828,
                radius,
                0f,
                true);
        FinitePlanetDefenseLayoutPlan defence = plan.DefenseLayout;

        Assert.IsNotNull(defence);
        Assert.IsTrue(defence.IsValid);
        Assert.AreEqual(AirCombatMapMode.Horde, plan.SemanticPlan.mode);
        Assert.AreEqual(expectedIngresses, defence.enemyIngresses.Length);
        Assert.That(
            new Vector2(
                defence.combatCenter.x,
                defence.combatCenter.z).magnitude,
            Is.InRange(radius * 0.09f, radius * 0.25f));
        Assert.That(defence.powerPositions.Length, Is.EqualTo(3));
        Assert.That(defence.retreatPoints.Length, Is.EqualTo(2));

        for (int index = 0;
             index < defence.enemyIngresses.Length;
             index++)
        {
            CombatSemanticAnchor ingress =
                defence.enemyIngresses[index];
            float distance = new Vector2(
                ingress.position.x,
                ingress.position.z).magnitude;
            Assert.That(
                distance,
                Is.InRange(radius * 0.63f, radius * 0.73f));
        }

        int terrainCoverCount = 0;
        for (int index = 0;
             index < plan.SemanticPlan.terrainStamps.Length;
             index++)
        {
            CombatTerrainStamp stamp =
                plan.SemanticPlan.terrainStamps[index];
            if (stamp != null
                && stamp.stableId.StartsWith("defence.cover."))
            {
                terrainCoverCount++;
            }
        }
        Assert.GreaterOrEqual(
            terrainCoverCount,
            expectedIngresses * 2);

        float wall = plan.SampleHeight(radius * 0.98f, 0f);
        Assert.Greater(
            wall,
            plan.BaseGroundHeight
            + plan.RecommendedFlightCeilingHeight
            + 20f);
    }

    [Test]
    public void FinitePlanetTerrainPlanSurvivesUnityHotReloadSerialization()
    {
        var definition = new GalaxyPlanetDefinition
        {
            planetId = "finite-reload-test",
            seed = 73421,
            climate = PlanetClimate.Volcanic
        };
        FinitePlanetCombatTerrainPlan original =
            FinitePlanetCombatTerrainPlanner.Create(
                definition,
                "modular_boss",
                9137,
                760f,
                0f,
                false);
        string json = JsonUtility.ToJson(original);
        FinitePlanetCombatTerrainPlan restored =
            JsonUtility.FromJson<FinitePlanetCombatTerrainPlan>(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored.Settings, Is.Not.Null);
        Assert.That(restored.SemanticPlan, Is.Not.Null);
        Assert.That(restored.DefenseLayout, Is.Not.Null);
        Assert.That(restored.DefenseLayout.IsValid, Is.True);
        Assert.That(
            restored.SampleHeight(175f, -90f),
            Is.EqualTo(original.SampleHeight(175f, -90f)).Within(0.001f));
        Assert.That(
            restored.MaximumTerrainHeightAboveBase,
            Is.EqualTo(original.MaximumTerrainHeightAboveBase)
                .Within(0.001f));
    }

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
        Assert.IsFalse(metadata.bossBridgeHintSeen);
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

    [Test]
    public void PlanetaryGravitySupportCancelsOnlyDownwardEnvironmentLoad()
    {
        float hover = SpacecraftIfcsMotor.CalculateGravitySupportAcceleration(
            Vector3.down * 17.65f,
            Vector3.zero,
            Vector3.up,
            20f);
        float wingBorne =
            SpacecraftIfcsMotor.CalculateGravitySupportAcceleration(
                Vector3.down * 17.65f,
                Vector3.up * 12f,
                Vector3.up,
                20f);
        float capped = SpacecraftIfcsMotor.CalculateGravitySupportAcceleration(
            Vector3.down * 24f,
            Vector3.zero,
            Vector3.up,
            20f);
        float downforceIsNotHidden =
            SpacecraftIfcsMotor.CalculateGravitySupportAcceleration(
                Vector3.down * 9.8f,
                Vector3.down * 6f,
                Vector3.up,
                20f);

        Assert.That(hover, Is.EqualTo(17.65f).Within(0.001f));
        Assert.That(wingBorne, Is.EqualTo(5.65f).Within(0.001f));
        Assert.That(capped, Is.EqualTo(20f).Within(0.001f));
        Assert.That(
            downforceIsNotHidden,
            Is.EqualTo(9.8f).Within(0.001f));
    }

    [Test]
    public void BuiltInWingProfilesProduceLiftAndSoftStall()
    {
        ShipAerodynamicProfile wing =
            ShipAerodynamicProfile.CreateForPart(
                "decor.delta_wing",
                SpacecraftPartCategory.Decoration);

        float preStall = Mathf.Abs(
            wing.EvaluateLiftCoefficient(
                wing.StallAngleDegrees * 0.8f));
        float atStall = Mathf.Abs(
            wing.EvaluateLiftCoefficient(
                wing.StallAngleDegrees));
        float deepStall = Mathf.Abs(
            wing.EvaluateLiftCoefficient(
                wing.StallAngleDegrees + 35f));

        Assert.IsTrue(wing.IsEnabled);
        Assert.AreEqual(SpacecraftAerodynamicRole.Wing, wing.Role);
        Assert.Greater(atStall, preStall);
        Assert.Less(deepStall, atStall);
        Assert.Greater(
            wing.EvaluateDragCoefficient(atStall),
            wing.BaseDragCoefficient);
    }

    [Test]
    public void NonAerodynamicPartsRemainAerodynamicallyInactive()
    {
        ShipAerodynamicProfile thruster =
            ShipAerodynamicProfile.CreateForPart(
                "thruster.large",
                SpacecraftPartCategory.Thruster);
        ShipAerodynamicProfile weapon =
            ShipAerodynamicProfile.CreateForPart(
                "weapon.energy",
                SpacecraftPartCategory.Weapon);

        Assert.IsFalse(thruster.IsEnabled);
        Assert.IsFalse(weapon.IsEnabled);
    }
}
