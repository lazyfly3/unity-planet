#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ModularAssembly;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.EDPCG;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.Tests.Editor
{
    public sealed class EdpcgRuntimeTests
    {
        [Test]
        public void DefaultDifficultyUsesExpandedEnvironmentalRosters()
        {
            EdpcgDifficultyProfile profile =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            int previous = 0;
            int[] expected = { 18, 26, 35, 43, 56, 68 };
            int[] environmental = { 2, 2, 3, 3, 4, 4 };
            int[] populationCaps = { 8, 10, 14, 18, 22, 28 };
            int[] engagementCaps = { 5, 7, 9, 11, 14, 16 };
            int[] attackTokenCaps = { 1, 2, 2, 3, 3, 4 };
            for (int tier = 0; tier < expected.Length; tier++)
            {
                EdpcgTierSettings settings = profile.Resolve(tier);
                Assert.That(settings.rosterCount, Is.EqualTo(expected[tier]));
                Assert.That(settings.environmentalPursuerCount,
                    Is.EqualTo(environmental[tier]));
                Assert.That(settings.populationCap,
                    Is.EqualTo(populationCaps[tier]));
                Assert.That(settings.engagementCap,
                    Is.EqualTo(engagementCaps[tier]));
                Assert.That(settings.attackTokenCap,
                    Is.EqualTo(attackTokenCaps[tier]));
                Assert.That(settings.rosterCount, Is.GreaterThan(previous));
                Assert.That(
                    settings.interceptorCount + settings.strikerCount +
                    settings.gunshipCount,
                    Is.EqualTo(settings.rosterCount));
                Assert.That(settings.populationCap, Is.LessThanOrEqualTo(28));
                Assert.That(settings.fullSimulationCap, Is.LessThanOrEqualTo(16));
                Assert.That(settings.attackTokenCap, Is.LessThanOrEqualTo(4));
                previous = settings.rosterCount;
            }
        }

        [Test]
        public void HighestTierAddsFourEnvironmentalInterceptorsOnly()
        {
            EdpcgTierSettings settings =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault().Resolve(5);
            Assert.That(settings.rosterCount, Is.EqualTo(68));
            Assert.That(settings.interceptorCount, Is.EqualTo(44));
            Assert.That(settings.strikerCount, Is.EqualTo(20));
            Assert.That(settings.gunshipCount, Is.EqualTo(4));
            Assert.That(settings.environmentalPursuerCount, Is.EqualTo(4));
            Assert.That(settings.requiredCreditedKills, Is.EqualTo(20));
        }

        [Test]
        public void PressureBandsStayOrderedAndBelowHardLimit()
        {
            EdpcgDifficultyProfile profile =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            for (int tier = 0; tier < 6; tier++)
            {
                EdpcgTierSettings settings = profile.Resolve(tier);
                foreach (EdpcgEncounterPhase phase in
                         System.Enum.GetValues(typeof(EdpcgEncounterPhase)))
                {
                    settings.ResolveTargetBand(phase, out float minimum,
                        out float maximum);
                    Assert.That(minimum, Is.InRange(0f, 1f));
                    Assert.That(maximum, Is.InRange(minimum, 1f));
                    if (phase == EdpcgEncounterPhase.Peak)
                        Assert.That(maximum,
                            Is.LessThanOrEqualTo(settings.hardPressureLimit));
                }
                Assert.That(settings.recoverSeconds, Is.GreaterThanOrEqualTo(8f));
            }
        }

        [Test]
        public void RuntimeBuildsStableUnique68MemberRosterWithFourPursuers()
        {
            GameObject root = new GameObject("EDPCG_Runtime_Test");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                VehicleStructureGraph graph =
                    root.AddComponent<VehicleStructureGraph>();
                HordeCombatDirector director =
                    root.AddComponent<HordeCombatDirector>();
                EdpcgEncounterRuntime runtime =
                    root.AddComponent<EdpcgEncounterRuntime>();
                runtime.Configure(
                    director,
                    body,
                    graph,
                    null,
                    "clearance",
                    5,
                    123456);
                runtime.BeginSession();

                Assert.That(runtime.RosterCount, Is.EqualTo(68));
                var ids = new HashSet<string>();
                int interceptors = 0;
                int strikers = 0;
                int gunships = 0;
                int environmentalPursuers = 0;
                for (int index = 0; index < runtime.Roster.Count; index++)
                {
                    EdpcgRosterMember member = runtime.Roster[index];
                    Assert.That(ids.Add(member.rosterMemberId), Is.True);
                    Assert.That(member.rosterIndex, Is.EqualTo(index));
                    if (member.role == HordeEnemyRole.Interceptor)
                        interceptors++;
                    else if (member.role == HordeEnemyRole.Striker)
                        strikers++;
                    else
                        gunships++;
                    if (member.environmentalPursuer)
                    {
                        environmentalPursuers++;
                        Assert.That(member.role,
                            Is.EqualTo(HordeEnemyRole.Interceptor));
                    }
                }
                Assert.That(interceptors, Is.EqualTo(44));
                Assert.That(strikers, Is.EqualTo(20));
                Assert.That(gunships, Is.EqualTo(4));
                Assert.That(environmentalPursuers, Is.EqualTo(4));
                runtime.EndSession();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RosterResolutionIsIdempotentAndSelfDetonationIsNotCredited()
        {
            GameObject root = new GameObject("EDPCG_Resolution_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 0, 991);
                EdpcgRosterMember member = runtime.Roster[0];
                Assert.That(runtime.ResolveMember(
                    member.rosterMemberId,
                    EdpcgEnemyResolutionReason.SelfDetonated,
                    Vector3.zero,
                    out bool credited), Is.True);
                Assert.That(credited, Is.False);
                Assert.That(runtime.ResolveMember(
                    member.rosterMemberId,
                    EdpcgEnemyResolutionReason.KilledByPlayer,
                    Vector3.zero,
                    out _), Is.False);
                Assert.That(runtime.ResolvedCount, Is.EqualTo(1));
                Assert.That(runtime.CreditedKills, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LiveTuningRecordsTransactionAndCanRevert()
        {
            GameObject root = new GameObject("EDPCG_Tuning_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 2, 17);
                float original = runtime.Settings.spawnIntervalSeconds;
                Assert.That(runtime.TryApplyTuning(
                    "spawnIntervalSeconds",
                    "1.75",
                    EdpcgLiveApplyPolicy.ApplyNow,
                    out string changeId,
                    out string error), Is.True, error);
                Assert.That(runtime.Settings.spawnIntervalSeconds,
                    Is.EqualTo(1.75f).Within(0.001f));
                Assert.That(runtime.Recorder.Changes.Count, Is.EqualTo(1));
                Assert.That(runtime.RevertChange(changeId), Is.True);
                Assert.That(runtime.Settings.spawnIntervalSeconds,
                    Is.EqualTo(original).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TelemetryRecorderKeepsBoundedRecentSamples()
        {
            var recorder = new EdpcgTelemetryRecorder(64, 128);
            for (int index = 0; index < 80; index++)
            {
                recorder.RecordSample(new EdpcgPressureSample
                {
                    missionTime = index,
                    actualPressure = index / 100f
                });
            }
            Assert.That(recorder.Samples.Count, Is.EqualTo(64));
            Assert.That(recorder.Samples[0].missionTime, Is.EqualTo(16f));
            Assert.That(recorder.Samples[63].missionTime, Is.EqualTo(79f));
        }

        [Test]
        public void MissionRulesExpose68EnemiesAtHighestNonBossTier()
        {
            FinitePlanetMissionRules clearance =
                FinitePlanetMissionRules.Resolve("clearance", 5);
            FinitePlanetMissionRules assault =
                FinitePlanetMissionRules.Resolve("industrial_outpost", 5);
            Assert.That(clearance.RosterCount, Is.EqualTo(68));
            Assert.That(assault.RosterCount, Is.EqualTo(68));
            Assert.That(clearance.Kind,
                Is.EqualTo(FinitePlanetMissionObjectiveKind.Clearance));
            Assert.That(assault.Kind,
                Is.EqualTo(FinitePlanetMissionObjectiveKind.Assault));
        }

        [TestCase("clearance", 0)]
        [TestCase("wind_canyon", 2)]
        [TestCase("industrial_outpost", 5)]
        public void FormalMissionRostersResolveWithEnvironmentalKills(
            string missionId,
            int tier)
        {
            GameObject root = new GameObject(
                "EDPCG_FormalMissionResolution_" + missionId);
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                VehicleStructureGraph graph =
                    root.AddComponent<VehicleStructureGraph>();
                HordeCombatDirector director =
                    root.AddComponent<HordeCombatDirector>();
                EdpcgEncounterRuntime runtime =
                    root.AddComponent<EdpcgEncounterRuntime>();
                runtime.Configure(
                    director,
                    body,
                    graph,
                    null,
                    missionId,
                    tier,
                    7319 + tier);
                runtime.BeginSession();
                FinitePlanetMissionRules rules =
                    FinitePlanetMissionRules.Resolve(missionId, tier);
                Assert.That(runtime.RosterCount,
                    Is.EqualTo(rules.RosterCount));
                for (int index = 0; index < runtime.Roster.Count; index++)
                {
                    Assert.That(runtime.ResolveMember(
                        runtime.Roster[index].rosterMemberId,
                        EdpcgEnemyResolutionReason.PlayerCausedEnvironment,
                        Vector3.forward * index,
                        out bool credited), Is.True);
                    Assert.That(credited, Is.True);
                }
                Assert.That(runtime.IsEncounterResolved, Is.True);
                Assert.That(runtime.CreditedKills,
                    Is.EqualTo(runtime.RosterCount));
                runtime.EndSession();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BossMissionKeepsHordeRosterDisabled()
        {
            FinitePlanetMissionRules rules =
                FinitePlanetMissionRules.Resolve("modular_boss", 5);
            Assert.That(rules.Kind,
                Is.EqualTo(FinitePlanetMissionObjectiveKind.Boss));
            Assert.That(rules.RosterCount, Is.Zero);
            Assert.That(rules.RequiredKills, Is.Zero);
        }

        [Test]
        public void VersionOneRosterMigrationAddsPursuersOnlyOnce()
        {
            EdpcgDifficultyProfile profile =
                ScriptableObject.CreateInstance<EdpcgDifficultyProfile>();
            try
            {
                profile.schemaVersion = 1;
                profile.tiers = new EdpcgTierSettings[6];
                int[] oldTotals = { 16, 24, 32, 40, 52, 64 };
                int[] additions = { 2, 2, 3, 3, 4, 4 };
                for (int tier = 0; tier < 6; tier++)
                {
                    EdpcgTierSettings settings =
                        EdpcgTierSettings.CreateDefault(tier);
                    settings.rosterCount = oldTotals[tier];
                    settings.interceptorCount -= additions[tier];
                    settings.environmentalPursuerCount = 0;
                    profile.tiers[tier] = settings;
                }

                profile.EnsureInitialized();
                int[] firstTotals = new int[6];
                for (int tier = 0; tier < 6; tier++)
                {
                    firstTotals[tier] = profile.tiers[tier].rosterCount;
                    Assert.That(profile.tiers[tier].environmentalPursuerCount,
                        Is.EqualTo(additions[tier]));
                }
                profile.EnsureInitialized();
                for (int tier = 0; tier < 6; tier++)
                    Assert.That(profile.tiers[tier].rosterCount,
                        Is.EqualTo(firstTotals[tier]));
                Assert.That(profile.schemaVersion,
                    Is.EqualTo(EdpcgDifficultyProfile.CurrentSchemaVersion));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ForwardOnlyTrapRouteRejectsReverseAndAdvancesMonotonically()
        {
            var map = new EdpcgCityTacticalRuntimeMap();
            var route = new EdpcgRuntimeRoute
            {
                StableId = "environment.test.forward-only",
                Points = new[]
                {
                    Vector3.zero,
                    Vector3.forward * 50f,
                    Vector3.forward * 100f,
                    Vector3.forward * 150f
                },
                Capacity = 2,
                EstimatedTravelSeconds = 4f,
                AllowReverse = false,
                IsEnvironmentalTrap = true
            };
            RegisterRoute(map, route);
            var reservations = new EdpcgRouteReservationService(map);
            Assert.That(reservations.TryReserve(
                "reverse", route.StableId, 0f, 4f, 8f, 1, -1, out _),
                Is.False);
            Assert.That(reservations.TryReserve(
                "first", route.StableId, 0f, 4f, 8f, 1, 1,
                out _), Is.True);
            Assert.That(reservations.TryReserve(
                "second", route.StableId, 0f, 4f, 8f, 1, 1,
                out _), Is.True);
            Assert.That(reservations.TryReserve(
                "over-capacity", route.StableId, 0f, 4f, 8f, 1, 1,
                out _), Is.False);

            var root = new GameObject("ForwardOnlyRouteProgressTest");
            try
            {
                EdpcgEncounterRuntime runtime =
                    root.AddComponent<EdpcgEncounterRuntime>();
                SetPrivateField(runtime, "tacticalMap", map);
                SetPrivateField(runtime, "reservations", reservations);
                var waypoints = new List<Vector3>();
                Assert.That(runtime.TryGetReservedRouteWaypoints(
                    "first", route.Points[0], waypoints), Is.True);
                Assert.That(waypoints[0], Is.EqualTo(route.Points[1]));
                Assert.That(runtime.TryGetReservedRouteWaypoints(
                    "first", route.Points[1], waypoints), Is.True);
                Assert.That(waypoints[0], Is.EqualTo(route.Points[2]));
                Assert.That(runtime.TryGetReservedRouteWaypoints(
                    "first", route.Points[0], waypoints), Is.True);
                Assert.That(waypoints[0], Is.EqualTo(route.Points[2]),
                    "A failed local path must not reverse a committed trap route.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FailedTrapRouteCannotRecommitUntilOpportunityResets()
        {
            var root = new GameObject("EDPCG_TrapFailureBlock_Test");
            try
            {
                BuildTrapCommitTestRuntime(
                    root,
                    out EdpcgEncounterRuntime runtime,
                    out HordeEnemyVehicle enemy,
                    out Rigidbody playerBody,
                    out _);

                Assert.That(runtime.TryAcquireOrMaintainEnvironmentalTrap(
                    enemy, out _), Is.True);
                runtime.ReleaseEnvironmentalTrapCommitment(
                    enemy,
                    "route-not-found");
                Assert.That(runtime.EnvironmentalPursuitCount, Is.Zero);
                Assert.That(runtime.Reservations.Reservations.Count, Is.Zero);
                Assert.That(runtime.TryAcquireOrMaintainEnvironmentalTrap(
                    enemy, out _), Is.False,
                    "The same failed opportunity must not be reacquired next frame.");
                Assert.That(runtime.CanAttemptEnvironmentalTrap(enemy),
                    Is.False,
                    "A blocked high-priority pursuer must not prevent another aircraft from applying.");

                playerBody.position = Vector3.right * 500f;
                runtime.Tick(0.1f);
                playerBody.position = Vector3.zero;
                Assert.That(runtime.TryAcquireOrMaintainEnvironmentalTrap(
                    enemy, out _), Is.True,
                    "Leaving the opportunity must permit a later deliberate retry.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ExpiredTrapLeaseAlsoReleasesCommitmentAndPressureCount()
        {
            var root = new GameObject("EDPCG_TrapLeaseExpiry_Test");
            try
            {
                BuildTrapCommitTestRuntime(
                    root,
                    out EdpcgEncounterRuntime runtime,
                    out HordeEnemyVehicle enemy,
                    out _,
                    out _);

                Assert.That(runtime.TryAcquireOrMaintainEnvironmentalTrap(
                    enemy, out _), Is.True);
                Assert.That(runtime.EnvironmentalPursuitCount, Is.EqualTo(1));
                Assert.That(runtime.Reservations.Reservations.Count,
                    Is.EqualTo(1));
                runtime.Reservations.Reservations[0].expiresAt =
                    Time.time - 0.1f;

                runtime.Tick(0.1f);

                Assert.That(runtime.Reservations.Reservations.Count, Is.Zero);
                Assert.That(runtime.EnvironmentalPursuitCount, Is.Zero,
                    "An expired lease must not retain an engagement seat.");
                Assert.That(runtime.CurrentSample.environmentalPursuitCount,
                    Is.Zero,
                    "Expired pursuits must disappear from pressure telemetry in the same tick.");
                Assert.That(runtime.TryAcquireOrMaintainEnvironmentalTrap(
                    enemy, out _), Is.True,
                    "Lease expiry is lifecycle cleanup, not a route-failure blacklist.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ExporterCreatesChartsDataReportAndLegendIcons()
        {
            GameObject root = new GameObject("EDPCG_Export_Test");
            string parent = Path.Combine(
                Path.GetTempPath(),
                "UnityPlanetEdpcgTests",
                System.Guid.NewGuid().ToString("N"));
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 1, 441);
                runtime.UpdateDirectorSnapshot(3, 2, 1, 1, 0, 0, 3);
                runtime.Tick(0.1f);
                string folder = EdpcgExportService.ExportAll(runtime, parent);
                Assert.That(File.Exists(Path.Combine(folder,
                    "pressure-curve.png")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "pressure-curve.svg")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "telemetry.csv")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "session.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "report.html")), Is.True);
                Assert.That(File.Exists(Path.Combine(folder,
                    "manifest.json")), Is.True);
                Assert.That(Directory.GetFiles(folder, "icon-*.svg").Length,
                    Is.GreaterThanOrEqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(root);
                if (Directory.Exists(parent))
                    Directory.Delete(parent, true);
            }
        }

        static EdpcgEncounterRuntime BuildRuntime(
            GameObject root,
            int tier,
            int seed)
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            VehicleStructureGraph graph =
                root.AddComponent<VehicleStructureGraph>();
            HordeCombatDirector director =
                root.AddComponent<HordeCombatDirector>();
            EdpcgEncounterRuntime runtime =
                root.AddComponent<EdpcgEncounterRuntime>();
            runtime.Configure(
                director,
                body,
                graph,
                null,
                "clearance",
                tier,
                seed);
            runtime.BeginSession();
            return runtime;
        }

        static void RegisterRoute(
            EdpcgCityTacticalRuntimeMap map,
            EdpcgRuntimeRoute route)
        {
            FieldInfo routesField = typeof(EdpcgCityTacticalRuntimeMap)
                .GetField("routes", BindingFlags.Instance |
                                    BindingFlags.NonPublic);
            FieldInfo routeByIdField = typeof(EdpcgCityTacticalRuntimeMap)
                .GetField("routeById", BindingFlags.Instance |
                                       BindingFlags.NonPublic);
            Assert.That(routesField, Is.Not.Null);
            Assert.That(routeByIdField, Is.Not.Null);
            ((List<EdpcgRuntimeRoute>)routesField.GetValue(map)).Add(route);
            ((Dictionary<string, EdpcgRuntimeRoute>)routeByIdField
                .GetValue(map))[route.StableId] = route;
        }

        static void BuildTrapCommitTestRuntime(
            GameObject root,
            out EdpcgEncounterRuntime runtime,
            out HordeEnemyVehicle enemy,
            out Rigidbody playerBody,
            out EdpcgRuntimeRoute trapRoute)
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(root.transform, false);
            playerBody = player.AddComponent<Rigidbody>();
            playerBody.useGravity = false;
            playerBody.isKinematic = true;
            VehicleStructureGraph graph =
                player.AddComponent<VehicleStructureGraph>();
            HordeCombatDirector director =
                root.AddComponent<HordeCombatDirector>();
            runtime = root.AddComponent<EdpcgEncounterRuntime>();
            runtime.Configure(
                director,
                playerBody,
                graph,
                null,
                "trap-regression",
                0,
                7319);
            runtime.BeginSession();

            GameObject fieldObject = new GameObject("MagneticTrap");
            fieldObject.transform.SetParent(root.transform, false);
            UrbanEnvironmentalFieldVolume field =
                fieldObject.AddComponent<UrbanEnvironmentalFieldVolume>();
            var descriptor = new UrbanEnvironmentalPursuitRouteDescriptor
            {
                stableId = "environment.test.magnetic",
                kind = UrbanEnvironmentalFieldKind.MagneticCourtyard,
                field = field,
                worldWaypoints = new[]
                {
                    Vector3.back * 120f,
                    Vector3.back * 40f,
                    Vector3.forward * 40f
                },
                worldImpactPoint = Vector3.forward * 40f,
                localApproachCenter = Vector3.zero,
                localApproachSize = Vector3.one * 400f,
                capacity = 2,
                allowReverse = false
            };
            trapRoute = new EdpcgRuntimeRoute
            {
                StableId = descriptor.stableId,
                SourceKind = AirCombatRouteKind.MaskedFlank,
                Points = (Vector3[])descriptor.worldWaypoints.Clone(),
                Width = 60f,
                Capacity = descriptor.capacity,
                EstimatedTravelSeconds = 3f,
                AllowReverse = false,
                IsEnvironmentalTrap = true,
                EnvironmentalTrap = descriptor
            };
            var map = new EdpcgCityTacticalRuntimeMap();
            RegisterRoute(map, trapRoute);
            SetPrivateField(runtime, "tacticalMap", map);
            SetPrivateField(
                runtime,
                "reservations",
                new EdpcgRouteReservationService(map));

            EdpcgRosterMember member = null;
            for (int index = 0; index < runtime.Roster.Count; index++)
            {
                if (!runtime.Roster[index].environmentalPursuer)
                    continue;
                member = runtime.Roster[index];
                break;
            }
            Assert.That(member, Is.Not.Null);
            GameObject enemyObject = new GameObject("RealHordeEnemy");
            enemyObject.transform.SetParent(root.transform, false);
            enemy = enemyObject.AddComponent<HordeEnemyVehicle>();
            Assert.That(enemy.Initialize(
                director,
                null,
                null,
                out string initializationError), Is.True,
                initializationError);
            HordeEnemyProfile enemyProfile =
                HordeEnemyProfile.ForRole(member.role);
            Vector3 spawn = trapRoute.Points[0];
            enemy.Activate(
                enemyProfile,
                playerBody,
                spawn,
                Quaternion.LookRotation(Vector3.forward),
                0,
                director.SessionId,
                member.rosterMemberId,
                member.rosterIndex,
                member.environmentalPursuer);
            enemy.Body.isKinematic = true;
            runtime.MarkSpawned(member.rosterMemberId, spawn);
            runtime.UpdateDirectorSnapshot(
                1,
                0,
                0,
                0,
                0,
                0,
                enemyProfile.threatCost);
            runtime.Tick(0.1f);
        }

        static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}
#endif
