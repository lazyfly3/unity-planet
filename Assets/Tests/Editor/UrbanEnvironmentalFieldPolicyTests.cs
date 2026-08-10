using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;

public sealed class UrbanEnvironmentalFieldPolicyTests
{
    [Test]
    public void RuntimeForceMultiplierIsRealtimeAndSafetyClamped()
    {
        var root = new GameObject("EnvironmentalForceMultiplierTest");
        try
        {
            UrbanEnvironmentalFieldVolume field =
                root.AddComponent<UrbanEnvironmentalFieldVolume>();
            field.SetForceMultiplier(1.35f);
            Assert.That(field.ForceMultiplier, Is.EqualTo(1.35f).Within(0.001f));
            field.SetForceMultiplier(0.01f);
            Assert.That(field.ForceMultiplier, Is.EqualTo(0.25f).Within(0.001f));
            field.SetForceMultiplier(9f);
            Assert.That(field.ForceMultiplier, Is.EqualTo(2.5f).Within(0.001f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void WindUsesFiniteForceSoHeavierVehiclesAccelerateLess()
    {
        var lightObject = new GameObject("WindPolicyLight");
        var heavyObject = new GameObject("WindPolicyHeavy");
        try
        {
            Rigidbody light = lightObject.AddComponent<Rigidbody>();
            Rigidbody heavy = heavyObject.AddComponent<Rigidbody>();
            light.mass = 1000f;
            heavy.mass = 4000f;
            float lightForce =
                UrbanEnvironmentalFieldPolicy.ResolveWindForce(light, null);
            float heavyForce =
                UrbanEnvironmentalFieldPolicy.ResolveWindForce(heavy, null);
            Assert.That(lightForce, Is.GreaterThan(0f));
            Assert.That(float.IsNaN(lightForce), Is.False);
            Assert.That(heavyForce / heavy.mass,
                Is.LessThan(lightForce / light.mass));
        }
        finally
        {
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(heavyObject);
        }
    }

    [Test]
    public void WingsAndArcadeAssistReduceButDoNotRemoveDisturbance()
    {
        var bare = new RobocraftTelemetry
        {
            wingArea = 0f,
            positiveAcceleration = Vector3.zero,
            negativeAcceleration = Vector3.zero
        };
        var controlled = new RobocraftTelemetry
        {
            wingArea = 52f,
            positiveAcceleration = Vector3.one * 34f,
            negativeAcceleration = Vector3.one * 30f
        };
        float bareStandard =
            UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                bare,
                VehicleCoreAssistMode.Standard,
                UrbanEnvironmentalFieldKind.NaturalStreetGale);
        float wingStandard =
            UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                controlled,
                VehicleCoreAssistMode.Standard,
                UrbanEnvironmentalFieldKind.NaturalStreetGale);
        float wingArcade =
            UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                controlled,
                VehicleCoreAssistMode.Training,
                UrbanEnvironmentalFieldKind.NaturalStreetGale);
        Assert.That(wingStandard, Is.LessThan(bareStandard));
        Assert.That(wingArcade, Is.LessThan(wingStandard));
        Assert.That(wingArcade, Is.GreaterThanOrEqualTo(0.34f));
    }

    [Test]
    public void EnvironmentalPursuitKeepsGunshipsOutAndSelectsStableSubset()
    {
        Assert.That(
            UrbanEnvironmentalFieldPolicy.IsEnvironmentalPursuer(
                0,
                HordeEnemyRole.Interceptor),
            Is.True);
        Assert.That(
            UrbanEnvironmentalFieldPolicy.IsEnvironmentalPursuer(
                1,
                HordeEnemyRole.Interceptor),
            Is.False);
        Assert.That(
            UrbanEnvironmentalFieldPolicy.IsEnvironmentalPursuer(
                2,
                HordeEnemyRole.Striker),
            Is.True);
        Assert.That(
            UrbanEnvironmentalFieldPolicy.IsEnvironmentalPursuer(
                0,
                HordeEnemyRole.Gunship),
            Is.False);
    }

    [Test]
    public void MagnetUsesOpposingThrustersButDoesNotReuseWingAntiWind()
    {
        var bare = new RobocraftTelemetry();
        var wingOnly = new RobocraftTelemetry { wingArea = 64f };
        var directional = new RobocraftTelemetry
        {
            negativeAcceleration = new Vector3(32f, 0f, 0f)
        };
        float bareMagnet =
            UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                bare,
                VehicleCoreAssistMode.Standard,
                UrbanEnvironmentalFieldKind.MagneticCourtyard,
                Vector3.right);
        float wingMagnet =
            UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                wingOnly,
                VehicleCoreAssistMode.Standard,
                UrbanEnvironmentalFieldKind.MagneticCourtyard,
                Vector3.right);
        float resistedFromRight =
            UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                directional,
                VehicleCoreAssistMode.Standard,
                UrbanEnvironmentalFieldKind.MagneticCourtyard,
                Vector3.right);
        float unresistedForward =
            UrbanEnvironmentalFieldPolicy.ResolvePlayerForceScale(
                directional,
                VehicleCoreAssistMode.Standard,
                UrbanEnvironmentalFieldKind.MagneticCourtyard,
                Vector3.forward);
        Assert.That(wingMagnet, Is.EqualTo(bareMagnet).Within(0.0001f));
        Assert.That(resistedFromRight, Is.LessThan(unresistedForward));
    }

    [Test]
    public void MagnetSelectsOneStableRealWallInsteadOfBalancingThreeForces()
    {
        var root = new GameObject("SingleMagneticWallAssignmentTest");
        try
        {
            UrbanEnvironmentalFieldVolume magnetic =
                root.AddComponent<UrbanEnvironmentalFieldVolume>();
            magnetic.ConfigureMagneticCourtyard(
                new Vector3(52f, 34f, 58f),
                CreateTestMagneticWalls(52f, 34f, 58f));
            MethodInfo select = typeof(UrbanEnvironmentalFieldVolume)
                .GetMethod(
                    "SelectMagneticWall",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(select, Is.Not.Null);

            int left = (int)select.Invoke(
                magnetic,
                new object[] { new Vector3(-20f, 15f, 0f) });
            int right = (int)select.Invoke(
                magnetic,
                new object[] { new Vector3(20f, 15f, 0f) });
            int back = (int)select.Invoke(
                magnetic,
                new object[] { new Vector3(0f, 15f, -24f) });
            int centreFirst = (int)select.Invoke(
                magnetic,
                new object[] { new Vector3(0f, 15f, 0f) });
            int centreSecond = (int)select.Invoke(
                magnetic,
                new object[] { new Vector3(0f, 15f, 0f) });

            Assert.That(new[] { left, right, back }.Distinct().Count(),
                Is.EqualTo(3));
            Assert.That(centreSecond, Is.EqualTo(centreFirst));
            Assert.That(centreFirst, Is.InRange(0, 2));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void GeneratedCityBuildsOneStreetGaleAndTwoMagneticCourtyards()
    {
        AirCombatCitySettings settings = new AirCombatCitySettings
        {
            seed = 7319,
            mission = AirCombatCityMission.Clearance
        };
        AirCombatCityPlan plan = AirCombatCityGenerator.Generate(
            settings,
            out AirCombatCityReport report);
        Assert.That(report.valid, Is.True, report.failureReason);
        var root = new GameObject("UrbanEnvironmentalFieldPolicyTest");
        try
        {
            UrbanEnvironmentalFieldDirector director =
                root.AddComponent<UrbanEnvironmentalFieldDirector>();
            AirCombatCitySettings validated = settings.ValidatedCopy();
            director.Configure(plan, validated);
            Assert.That(director.HasRequiredCombatTraps, Is.True,
                director.ValidationError);
            Assert.That(director.ValidationError, Is.Empty);
            UrbanEnvironmentalPursuitRouteDescriptor windRoute =
                director.PursuitRoutes.Single(route => route.kind ==
                    UrbanEnvironmentalFieldKind.NaturalStreetGale);
            Assert.That(windRoute.worldWaypoints.Length, Is.EqualTo(4));
            Assert.That(windRoute.allowReverse, Is.False);
            Assert.That(windRoute.capacity, Is.GreaterThanOrEqualTo(1));
            Assert.That(windRoute.worldImpactPoint,
                Is.Not.EqualTo(Vector3.zero));
            Assert.That(director.PursuitRoutes
                    .Where(route => route.kind ==
                        UrbanEnvironmentalFieldKind.MagneticCourtyard)
                    .All(route => route.worldWaypoints.Length == 3 &&
                                  !route.allowReverse &&
                                  route.capacity >= 1),
                Is.True);
            Assert.That(UrbanEnvironmentalFieldPolicy.MagnetWarningSeconds,
                Is.GreaterThanOrEqualTo(2.4f));
            Assert.That(
                director.Fields.Count(field => field.Kind ==
                    UrbanEnvironmentalFieldKind.NaturalStreetGale),
                Is.EqualTo(1));
            Assert.That(
                director.Fields.Count(field => field.Kind ==
                    UrbanEnvironmentalFieldKind.MagneticCourtyard),
                Is.EqualTo(2));
            Assert.That(
                director.Fields.All(field =>
                    field.State == UrbanEnvironmentalFieldState.Dormant),
                Is.True);
            UrbanEnvironmentalFieldVolume wind = director.Fields.Single(
                field => field.Kind ==
                         UrbanEnvironmentalFieldKind.NaturalStreetGale);
            Assert.That(
                wind.LocalSize.y,
                Is.EqualTo(validated.maximumAltitude).Within(0.01f),
                "A flying player must not bypass the gale above a low box.");

            UrbanEnvironmentalFieldVolume[] courtyards = director.Fields
                .Where(field => field.Kind ==
                    UrbanEnvironmentalFieldKind.MagneticCourtyard)
                .ToArray();
            AirCombatTacticalVolume[] recoveryVolumes = plan.volumes
                .Where(volume => volume.kind ==
                    AirCombatVolumeKind.RecoveryPocket)
                .ToArray();
            for (int index = 0; index < courtyards.Length; index++)
            {
                UrbanEnvironmentalFieldVolume courtyard = courtyards[index];
                AirCombatTacticalVolume volume = recoveryVolumes[index];
                Vector3 opening = Vector3.ProjectOnPlane(
                    volume.center,
                    Vector3.up).normalized;
                Assert.That(
                    Vector3.Dot(courtyard.transform.forward, opening),
                    Is.GreaterThan(0.99f),
                    "The open side must face away from the city centre.");
                Assert.That(courtyard.MagneticWalls.Count, Is.EqualTo(3));
                Assert.That(courtyard.MagneticPanelCount, Is.EqualTo(3));
                foreach (UrbanMagneticWallSurface wall in courtyard.MagneticWalls)
                {
                    Vector3 center = courtyard.transform.TransformPoint(
                        wall.localCenter);
                    Vector3 inward = courtyard.transform.TransformDirection(
                        wall.localInwardNormal).normalized;
                    Vector3 towardCourtyard = Vector3.ProjectOnPlane(
                        volume.center - center,
                        Vector3.up).normalized;
                    Assert.That(
                        Vector3.Dot(inward, towardCourtyard),
                        Is.GreaterThan(0.85f),
                        wall.stableId + " must use the building's inner facade.");
                    Assert.That(
                        Vector3.Dot(center - volume.center, opening),
                        Is.LessThanOrEqualTo(0.1f),
                        "No magnetic facade may close the authored opening.");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void WindMergesCollinearRoadSegmentsToTheirRealStreetEnds()
    {
        var plan = new AirCombatCityPlan
        {
            resolvedSeed = 91,
            playerSpawn = Vector3.zero
        };
        AddRoadSegment(plan, "south", -300f, -100f);
        AddRoadSegment(plan, "middle", -100f, 100f);
        AddRoadSegment(plan, "north", 100f, 300f);
        AddWindTrapGeometry(plan, 0f, 300f);
        var root = new GameObject("ContinuousStreetWindTest");
        try
        {
            UrbanEnvironmentalFieldDirector director =
                root.AddComponent<UrbanEnvironmentalFieldDirector>();
            director.Configure(plan, new AirCombatCitySettings());
            UrbanEnvironmentalFieldVolume wind = director.Fields.Single();

            Assert.That(wind.LocalSize.z, Is.EqualTo(600f).Within(0.01f));
            Vector3 source = wind.transform.TransformPoint(
                Vector3.back * wind.LocalSize.z * 0.5f);
            Vector3 destination = wind.transform.TransformPoint(
                Vector3.forward * wind.LocalSize.z * 0.5f);
            Assert.That(source.z, Is.EqualTo(-300f).Within(0.01f));
            Assert.That(destination.z, Is.EqualTo(300f).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void MissingRoadSegmentStopsWindAtMergedBlockGap()
    {
        var plan = new AirCombatCityPlan
        {
            resolvedSeed = 92,
            playerSpawn = Vector3.zero
        };
        AddRoadSegment(plan, "before-block", -300f, -80f);
        AddRoadSegment(plan, "after-block", 80f, 260f);
        AddWindTrapGeometry(plan, -190f, -80f);
        var root = new GameObject("BlockedStreetWindTest");
        try
        {
            UrbanEnvironmentalFieldDirector director =
                root.AddComponent<UrbanEnvironmentalFieldDirector>();
            director.Configure(plan, new AirCombatCitySettings());
            UrbanEnvironmentalFieldVolume wind = director.Fields.Single();

            Assert.That(wind.LocalSize.z, Is.EqualTo(220f).Within(0.01f));
            Vector3 destination = wind.transform.TransformPoint(
                Vector3.forward * wind.LocalSize.z * 0.5f);
            Assert.That(
                destination.z,
                Is.EqualTo(-80f).Within(0.01f),
                "Wind must stop where a merged block removes the road.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void EnvironmentalFieldPresentationUsesAnimatedAdditiveTextures()
    {
        var windObject = new GameObject("TexturedWindPresentationTest");
        var magneticObject = new GameObject("TexturedMagneticPresentationTest");
        try
        {
            UrbanEnvironmentalFieldVolume wind =
                windObject.AddComponent<UrbanEnvironmentalFieldVolume>();
            wind.ConfigureNaturalWind(new Vector3(42f, 28f, 120f), 73);
            ParticleSystem[] windLayers =
                windObject.GetComponentsInChildren<ParticleSystem>();
            Assert.That(windLayers.Length, Is.EqualTo(2));
            ParticleSystem galeBands = windLayers.Single(particle =>
                particle.GetComponent<ParticleSystemRenderer>()
                    .sharedMaterial.GetTexture("_MainTex").name ==
                "NaturalGaleDustSheet");
            ParticleSystem speedLines = windLayers.Single(particle =>
                particle.GetComponent<ParticleSystemRenderer>()
                    .sharedMaterial.GetTexture("_MainTex").name ==
                "NaturalGaleDustTracers");
            Assert.That(galeBands.noise.enabled, Is.True);
            Assert.That(galeBands.colorOverLifetime.enabled, Is.True);
            Assert.That(galeBands.emission.rateOverTime.constant,
                Is.GreaterThanOrEqualTo(1f));
            Assert.That(speedLines.emission.rateOverTime.constant,
                Is.GreaterThanOrEqualTo(4f));
            Assert.That(galeBands.shape.scale.z, Is.LessThanOrEqualTo(2.01f));
            Assert.That(
                galeBands.shape.position.z,
                Is.EqualTo(-59f).Within(0.01f));
            Assert.That(
                galeBands.main.startLifetime.constant,
                Is.GreaterThanOrEqualTo(120f / 104f));
            Assert.That(speedLines.shape.scale.z, Is.LessThanOrEqualTo(1.61f));
            Assert.That(
                speedLines.shape.position.z,
                Is.EqualTo(-59.2f).Within(0.01f));
            Assert.That(galeBands.main.startSize.constantMax,
                Is.GreaterThanOrEqualTo(12f));
            Assert.That(galeBands.main.startColor.color.a,
                Is.GreaterThanOrEqualTo(0.15f));
            Assert.That(galeBands.main.startColor.color.b,
                Is.LessThanOrEqualTo(galeBands.main.startColor.color.r));
            Assert.That(windObject.GetComponentsInChildren<LineRenderer>().Length,
                Is.EqualTo(0));
            ParticleSystemRenderer windRenderer =
                galeBands.GetComponent<ParticleSystemRenderer>();
            Assert.That(windRenderer.sharedMaterial.shader.name,
                Does.Contain("Additive"));
            Assert.That(windRenderer.sharedMaterial.mainTexture.name,
                Is.EqualTo("NaturalGaleDustSheet"));
            Assert.That(windRenderer.sharedMaterial.GetColor("_TintColor").a,
                Is.GreaterThanOrEqualTo(0.85f));
            Assert.That(speedLines.velocityOverLifetime.x.mode,
                Is.EqualTo(speedLines.velocityOverLifetime.z.mode));
            Assert.That(speedLines.velocityOverLifetime.y.mode,
                Is.EqualTo(speedLines.velocityOverLifetime.z.mode));
            Assert.That(speedLines.trails.enabled, Is.True);
            Assert.That(speedLines.trails.ratio, Is.GreaterThan(0.3f));
            ParticleSystemRenderer speedRenderer =
                speedLines.GetComponent<ParticleSystemRenderer>();
            Assert.That(speedRenderer.trailMaterial, Is.Not.Null);
            Assert.That(speedRenderer.trailMaterial.mainTexture.name,
                Is.EqualTo("RuntimeNaturalWindSoftStreak"));

            UrbanEnvironmentalFieldVolume magnetic =
                magneticObject.AddComponent<UrbanEnvironmentalFieldVolume>();
            magnetic.ConfigureMagneticCourtyard(
                new Vector3(52f, 34f, 58f),
                CreateTestMagneticWalls(52f, 34f, 58f));
            MeshRenderer[] wallNodes = magneticObject
                .GetComponentsInChildren<MeshRenderer>()
                .Where(renderer => renderer.name.Contains(
                    "MagneticWallSurfaceNode"))
                .ToArray();
            Assert.That(wallNodes.Length, Is.EqualTo(105));
            Assert.That(magneticObject.GetComponentsInChildren<MeshRenderer>()
                .Any(renderer => renderer.name.Contains("AnimatedFieldCurtain")),
                Is.False);
            Assert.That(wallNodes.All(renderer =>
                renderer.sharedMaterial.shader.name.Contains("Additive")),
                Is.True);
            Assert.That(wallNodes.All(renderer =>
                renderer.sharedMaterial.mainTexture != null &&
                renderer.sharedMaterial.mainTexture.name ==
                "MagneticWallFluxTracks"), Is.True);
            Assert.That(wallNodes.All(renderer =>
                Mathf.Abs(renderer.transform.localPosition.x) > 20f ||
                renderer.transform.localPosition.z < -20f), Is.True);
            BoxCollider[] magneticSurfaces = magneticObject
                .GetComponentsInChildren<BoxCollider>()
                .Where(collider => collider.name.Contains("贴墙连续碰撞代理"))
                .ToArray();
            Assert.That(magneticSurfaces.Length, Is.EqualTo(3));
            Assert.That(magneticSurfaces.All(collider => !collider.isTrigger),
                Is.True);
            Light[] magneticLights = magneticObject
                .GetComponentsInChildren<Light>()
                .Where(light => light.name.Contains("MagneticPressureLight"))
                .ToArray();
            Assert.That(magneticLights.Length, Is.EqualTo(3));
            Assert.That(magneticLights.All(light =>
                light.type == LightType.Spot && light.enabled), Is.True);
            var dormantBlock = new MaterialPropertyBlock();
            wallNodes[0].GetPropertyBlock(dormantBlock);
            Assert.That(dormantBlock.GetColor("_TintColor").a,
                Is.GreaterThanOrEqualTo(0.14f));
        }
        finally
        {
            Object.DestroyImmediate(windObject);
            Object.DestroyImmediate(magneticObject);
        }
    }

    [Test]
    public void ManualEnvironmentControlCanForceAndRestoreBothFieldKinds()
    {
        var windObject = new GameObject("ManualWindControlTest");
        var magneticObject = new GameObject("ManualMagneticControlTest");
        try
        {
            UrbanEnvironmentalFieldVolume wind =
                windObject.AddComponent<UrbanEnvironmentalFieldVolume>();
            wind.ConfigureNaturalWind(new Vector3(60f, 80f, 280f), 17);
            UrbanEnvironmentalFieldVolume magnetic =
                magneticObject.AddComponent<UrbanEnvironmentalFieldVolume>();
            magnetic.ConfigureMagneticCourtyard(
                new Vector3(54f, 70f, 62f),
                CreateTestMagneticWalls(54f, 70f, 62f));

            wind.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedActive);
            magnetic.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedActive);
            Assert.That(wind.State,
                Is.EqualTo(UrbanEnvironmentalFieldState.Active));
            Assert.That(magnetic.State,
                Is.EqualTo(UrbanEnvironmentalFieldState.Active));

            wind.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedDisabled);
            magnetic.SetControlMode(
                UrbanEnvironmentalFieldControlMode.ForcedDisabled);
            Assert.That(wind.State,
                Is.EqualTo(UrbanEnvironmentalFieldState.Dormant));
            Assert.That(magnetic.State,
                Is.EqualTo(UrbanEnvironmentalFieldState.Dormant));

            wind.SetControlMode(UrbanEnvironmentalFieldControlMode.Automatic);
            magnetic.SetControlMode(
                UrbanEnvironmentalFieldControlMode.Automatic);
            Assert.That(wind.ControlMode,
                Is.EqualTo(UrbanEnvironmentalFieldControlMode.Automatic));
            Assert.That(magnetic.ControlMode,
                Is.EqualTo(UrbanEnvironmentalFieldControlMode.Automatic));
        }
        finally
        {
            Object.DestroyImmediate(windObject);
            Object.DestroyImmediate(magneticObject);
        }
    }

    static void AddRoadSegment(
        AirCombatCityPlan plan,
        string id,
        float fromZ,
        float toZ)
    {
        plan.roads.Add(new AirCombatRoadStrip
        {
            stableId = id,
            start = new Vector3(40f, 0f, fromZ),
            end = new Vector3(40f, 0f, toZ),
            width = 72f,
            laneTiles = 3,
            kind = AirCombatRouteKind.Main,
            dangerLane = false
        });
    }

    static void AddWindTrapGeometry(
        AirCombatCityPlan plan,
        float feederZ,
        float downwindRoadEndZ)
    {
        plan.roads.Add(new AirCombatRoadStrip
        {
            stableId = "perpendicular-feeder",
            start = new Vector3(-100f, 0f, feederZ),
            end = new Vector3(180f, 0f, feederZ),
            width = 48f,
            laneTiles = 2,
            kind = AirCombatRouteKind.MaskedFlank,
            dangerLane = false
        });
        float direction = downwindRoadEndZ >= feederZ ? 1f : -1f;
        plan.buildings.Add(new AirCombatBuildingLot
        {
            stableId = "real-downwind-impact-building",
            center = new Vector3(
                40f,
                60f,
                downwindRoadEndZ + direction * 30f),
            size = new Vector3(90f, 120f, 60f),
            yaw = 0f,
            band = AirCombatBuildingBand.High,
            archetype = AirCombatBuildingArchetype.CombatTower
        });
    }

    static IReadOnlyList<UrbanMagneticWallSurface> CreateTestMagneticWalls(
        float width,
        float height,
        float depth)
    {
        return new[]
        {
            new UrbanMagneticWallSurface
            {
                stableId = "test.inner.left",
                localCenter = new Vector3(-width * 0.5f, height * 0.5f, 0f),
                localInwardNormal = Vector3.right,
                width = depth,
                height = height
            },
            new UrbanMagneticWallSurface
            {
                stableId = "test.inner.right",
                localCenter = new Vector3(width * 0.5f, height * 0.5f, 0f),
                localInwardNormal = Vector3.left,
                width = depth,
                height = height
            },
            new UrbanMagneticWallSurface
            {
                stableId = "test.inner.back",
                localCenter = new Vector3(0f, height * 0.5f, -depth * 0.5f),
                localInwardNormal = Vector3.forward,
                width = width,
                height = height
            }
        };
    }
}
