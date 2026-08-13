#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ModularAssembly;
using NUnit.Framework;
using UnityEditor;
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
            int[] attackTokenCaps = { 2, 2, 2, 3, 3, 4 };
            int[] rangedLaneCaps = { 1, 2, 2, 3, 3, 4 };
            float previousHealthMultiplier = 0f;
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
                Assert.That(settings.rangedFireLaneCap,
                    Is.EqualTo(rangedLaneCaps[tier]));
                Assert.That(settings.enemyThreatWeight, Is.EqualTo(0.45f));
                Assert.That(settings.navigationWeight, Is.EqualTo(0.25f));
                Assert.That(settings.environmentWeight, Is.EqualTo(0.14f));
                Assert.That(settings.playerStrainWeight, Is.EqualTo(0.16f));
                Assert.That(settings.enemyHealthMultiplier,
                    Is.EqualTo(
                        EdpcgTierSettings.DefaultEnemyHealthMultiplierForTier(
                            tier)).Within(0.0001f));
                Assert.That(settings.enemyHealthMultiplier,
                    Is.GreaterThan(previousHealthMultiplier));
                Assert.That(settings.rosterCount, Is.GreaterThan(previous));
                Assert.That(
                    settings.interceptorCount + settings.strikerCount +
                    settings.gunshipCount,
                    Is.EqualTo(settings.rosterCount));
                Assert.That(settings.populationCap, Is.LessThanOrEqualTo(28));
                Assert.That(settings.fullSimulationCap, Is.LessThanOrEqualTo(16));
                Assert.That(settings.attackTokenCap, Is.LessThanOrEqualTo(4));
                previous = settings.rosterCount;
                previousHealthMultiplier = settings.enemyHealthMultiplier;
            }
        }

        [Test]
        public void DefaultTiersKeepRangedRosterMajorityAndSuicideMinority()
        {
            EdpcgDifficultyProfile profile =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            for (int tier = 0; tier < 6; tier++)
            {
                EdpcgTierSettings settings = profile.Resolve(tier);
                int rangedCount = settings.strikerCount +
                                  settings.gunshipCount;

                Assert.That(rangedCount,
                    Is.GreaterThan(settings.interceptorCount),
                    "Tier " + (tier + 1) +
                    " must keep ordinary ranged enemies as the majority.");
                Assert.That(settings.interceptorCount,
                    Is.LessThan(settings.rosterCount / 2f),
                    "Tier " + (tier + 1) +
                    " must keep suicide interceptors as a minority role.");
                Assert.That(settings.interceptorCount + rangedCount,
                    Is.EqualTo(settings.rosterCount));
                Assert.That(settings.environmentalPursuerCount,
                    Is.LessThanOrEqualTo(settings.interceptorCount));
            }
        }

        [Test]
        public void EnemyHealthScalesWithDifficultyProfile()
        {
            EdpcgDifficultyProfile profile =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault();
            for (int tier = 0; tier < 6; tier++)
            {
                EdpcgTierSettings settings = profile.Resolve(tier);
                HordeEnemyProfile interceptor = HordeEnemyProfile.ForRole(
                    HordeEnemyRole.Interceptor,
                    settings.enemyHealthMultiplier);
                Assert.That(interceptor.maximumHealth,
                    Is.EqualTo(220f * settings.enemyHealthMultiplier)
                        .Within(0.001f));
            }
        }

        [Test]
        public void HighestTierAddsFourEnvironmentalInterceptorsOnly()
        {
            EdpcgTierSettings settings =
                EdpcgDifficultyProfile.LoadOrCreateMemoryDefault().Resolve(5);
            Assert.That(settings.rosterCount, Is.EqualTo(68));
            Assert.That(settings.interceptorCount, Is.EqualTo(16));
            Assert.That(settings.strikerCount, Is.EqualTo(42));
            Assert.That(settings.gunshipCount, Is.EqualTo(10));
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
        public void PursuitPressureUsesCurrentPhaseCapsAndExcludesRecovery()
        {
            GameObject root = new GameObject("EDPCG_Pursuit_Pressure_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 0, 712);
                Assert.That(runtime.PopulationCap, Is.EqualTo(4));
                Assert.That(runtime.EngagementCap, Is.EqualTo(3));

                runtime.UpdateDirectorSnapshot(
                    4, 0, 0, 0, 0, 0, 4, 1, 1, 3, 2);
                runtime.Tick(0.1f);

                Assert.That(runtime.CurrentSample.enemyThreatPressure,
                    Is.EqualTo(0.59f).Within(0.001f),
                    "Preview pressure must normalize against its live 4/3 caps, not full-tier 8/5 caps.");
                Assert.That(runtime.CurrentSample.pursuingThreatCount,
                    Is.EqualTo(3));
                Assert.That(runtime.CurrentSample.closeApproachThreatCount,
                    Is.EqualTo(2));

                runtime.UpdateDirectorSnapshot(
                    4, 0, 0, 0, 0, 4, 4, 1, 1, 3, 2);
                runtime.Tick(0.1f);
                Assert.That(runtime.CurrentSample.navigationPressure,
                    Is.Zero.Within(0.000001f),
                    "Navigation recovery is a health fault and must not masquerade as combat pressure.");
                Assert.That(runtime.CurrentSample.systemHealthIssueRatio,
                    Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PressureBrakeDoesNotRaiseMeasuredPressureByShrinkingItsDenominator()
        {
            GameObject baselineRoot = new GameObject("EDPCG_Pressure_Base_Cap");
            GameObject brakedRoot = new GameObject("EDPCG_Pressure_Braked_Cap");
            try
            {
                EdpcgEncounterRuntime baseline = BuildRuntime(baselineRoot, 5, 713);
                EdpcgEncounterRuntime braked = BuildRuntime(brakedRoot, 5, 713);
                SetPrivateField(braked, "pressureBrakeLevel", 2);
                baseline.UpdateDirectorSnapshot(8, 2, 1, 0, 1, 0, 12f,
                    2, 3, 4, 3);
                braked.UpdateDirectorSnapshot(8, 2, 1, 0, 1, 0, 12f,
                    2, 3, 4, 3);

                baseline.Tick(0.1f);
                braked.Tick(0.1f);

                Assert.That(braked.PopulationCap,
                    Is.LessThan(baseline.PopulationCap));
                Assert.That(braked.CurrentSample.enemyThreatPressure,
                    Is.EqualTo(baseline.CurrentSample.enemyThreatPressure)
                        .Within(0.000001f),
                    "Pressure braking may reduce execution budget, but must not make the same combat snapshot look more dangerous.");
            }
            finally
            {
                Object.DestroyImmediate(baselineRoot);
                Object.DestroyImmediate(brakedRoot);
            }
        }

        [Test]
        public void SustainedLowPressureReleasesBudgetOneStepAtATime()
        {
            GameObject root = new GameObject("EDPCG_Pressure_Control_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 5, 813);
                int initialCap = runtime.PopulationCap;
                float initialInterval = runtime.SpawnInterval;

                runtime.UpdateDirectorSnapshot(0, 0, 0, 0, 0, 0, 0);
                runtime.Tick(2f);
                Assert.That(runtime.CurrentSample.pressureAssistLevel, Is.Zero,
                    "A transient low sample must not cause an instant enemy burst.");
                Assert.That(runtime.PopulationCap, Is.EqualTo(initialCap));

                runtime.Tick(1.8f);
                Assert.That(runtime.CurrentSample.pressureAssistLevel,
                    Is.EqualTo(1));
                Assert.That(runtime.PopulationCap, Is.EqualTo(initialCap + 1));
                Assert.That(runtime.SpawnInterval, Is.LessThan(initialInterval));

                runtime.Tick(1.3f);
                Assert.That(runtime.Phase, Is.EqualTo(EdpcgEncounterPhase.Engage));
                int engageCapAtOneStep = runtime.PopulationCap;
                runtime.Tick(2.6f);
                Assert.That(runtime.CurrentSample.pressureAssistLevel,
                    Is.EqualTo(2));
                Assert.That(runtime.PopulationCap,
                    Is.EqualTo(engageCapAtOneStep + 1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PressureAssistNeverBypassesFormalCapsOrHardPressureGate()
        {
            GameObject root = new GameObject("EDPCG_Pressure_Safety_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 5, 864);
                SetPrivateField(runtime, "pressureAssistLevel", 99);

                Assert.That(runtime.PopulationCap,
                    Is.LessThanOrEqualTo(runtime.Settings.populationCap));
                Assert.That(runtime.EngagementCap,
                    Is.LessThanOrEqualTo(runtime.Settings.engagementCap));
                Assert.That(runtime.AttackTokenCap,
                    Is.LessThanOrEqualTo(runtime.Settings.attackTokenCap));

                SetPrivateField(runtime, "currentSample",
                    new EdpcgPressureSample
                    {
                        actualPressure = runtime.Settings.hardPressureLimit
                    });
                Assert.That(runtime.ShouldSpawn, Is.False,
                    "The low-pressure helper must never bypass the formal hard-pressure spawn gate.");

                GameObject enemyRoot = new GameObject("PressureGateEnemy");
                enemyRoot.transform.SetParent(root.transform, false);
                HordeEnemyVehicle enemy =
                    enemyRoot.AddComponent<HordeEnemyVehicle>();
                Assert.That(runtime.CanGrantAttack(enemy), Is.False,
                    "The low-pressure helper must never bypass the formal hard-pressure attack gate.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void LowAndHighestTierPeakBandsAreReachableFromRealCombatSignals()
        {
            int[] tiers = { 0, 5 };
            for (int index = 0; index < tiers.Length; index++)
            {
                GameObject root = new GameObject(
                    "EDPCG_Peak_Reachability_Test_" + tiers[index]);
                try
                {
                    EdpcgEncounterRuntime runtime = BuildRuntime(
                        root,
                        tiers[index],
                        914 + tiers[index]);
                    SetPrivateField(
                        runtime,
                        "phase",
                        EdpcgEncounterPhase.Peak);
                    SetPrivateField(runtime, "elapsed", 23f);
                    bool highestTier = tiers[index] == 5;
                    runtime.UpdateDirectorSnapshot(
                        highestTier ? 28 : 8,
                        highestTier ? 4 : 1,
                        highestTier ? 4 : 1,
                        highestTier ? 2 : 1,
                        highestTier ? 2 : 0,
                        0,
                        highestTier ? 42 : 12,
                        highestTier ? 3 : 1,
                        highestTier ? 7 : 1,
                        highestTier ? 16 : 5,
                        highestTier ? 16 : 5);
                    runtime.Tick(0.1f);
                    EdpcgPressureSample sample = runtime.CurrentSample;
                    float combatOnlyPressure =
                        sample.enemyThreatPressure *
                        runtime.Settings.enemyThreatWeight +
                        sample.navigationPressure *
                        runtime.Settings.navigationWeight;
                    Assert.That(combatOnlyPressure,
                        Is.GreaterThanOrEqualTo(
                            runtime.Settings.peakPressureMin),
                        "Tier " + (tiers[index] + 1) +
                        " peak must be reachable without damage, environment, or navigation failures.");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
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
                Assert.That(interceptors, Is.EqualTo(16));
                Assert.That(strikers, Is.EqualTo(42));
                Assert.That(gunships, Is.EqualTo(10));
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
        public void OrdinaryMaskedFlankSkipsEnvironmentalRoutesAndUsesNearestAreaRoute()
        {
            var map = new EdpcgCityTacticalRuntimeMap();
            RegisterArea(map, new EdpcgRuntimeArea
            {
                StableId = "area.central",
                Kind = EdpcgTacticalAreaKind.CentralManeuverDistrict,
                Bounds = new Bounds(Vector3.zero, Vector3.one * 80f),
                Entrances = new[] { Vector3.zero },
                Exits = new[] { Vector3.forward * 20f }
            });
            RegisterRoute(map, new EdpcgRuntimeRoute
            {
                StableId = "route.far",
                SourceKind = AirCombatRouteKind.MaskedFlank,
                Points = new[]
                {
                    Vector3.right * 600f,
                    Vector3.right * 700f
                },
                Width = 60f,
                Capacity = 2
            });
            RegisterRoute(map, new EdpcgRuntimeRoute
            {
                StableId = "route.environment-only",
                SourceKind = AirCombatRouteKind.MaskedFlank,
                Points = new[] { Vector3.back * 10f, Vector3.forward * 10f },
                Width = 60f,
                Capacity = 1,
                IsEnvironmentalTrap = true
            });
            RegisterRoute(map, new EdpcgRuntimeRoute
            {
                StableId = "route.near",
                SourceKind = AirCombatRouteKind.MaskedFlank,
                Points = new[] { Vector3.left * 30f, Vector3.forward * 50f },
                Width = 60f,
                Capacity = 2
            });

            Assert.That(map.TrySelectRoleDestination(
                HordeEnemyRole.Interceptor,
                0,
                Vector3.zero,
                EdpcgPathIntent.MaskedFlank,
                out _,
                out string areaId,
                out string routeId), Is.True);
            Assert.That(areaId, Is.EqualTo("area.central"));
            Assert.That(routeId, Is.EqualTo("route.near"));
        }

        [Test]
        public void TacticalAssignmentsGiveStrikerAndGunshipDifferentCityRoles()
        {
            var map = new EdpcgCityTacticalRuntimeMap();
            RegisterArea(map, new EdpcgRuntimeArea
            {
                StableId = "area.cover",
                Kind = EdpcgTacticalAreaKind.HighRiseOcclusionChain,
                Bounds = new Bounds(Vector3.forward * 135f, Vector3.one * 30f),
                Entrances = new[] { Vector3.forward * 135f }
            });
            RegisterArea(map, new EdpcgRuntimeArea
            {
                StableId = "area.exposed",
                Kind = EdpcgTacticalAreaKind.ExposedFireShortcut,
                Bounds = new Bounds(Vector3.right * 190f, Vector3.one * 30f),
                Entrances = new[] { Vector3.right * 190f }
            });
            RegisterRoute(map, new EdpcgRuntimeRoute
            {
                StableId = "route.flank",
                SourceKind = AirCombatRouteKind.MaskedFlank,
                Points = new[] { Vector3.forward * 100f, Vector3.forward * 160f },
                Width = 48f,
                Capacity = 2
            });
            RegisterRoute(map, new EdpcgRuntimeRoute
            {
                StableId = "route.fire",
                SourceKind = AirCombatRouteKind.LongRange,
                Points = new[] { Vector3.right * 150f, Vector3.right * 230f },
                Width = 72f,
                Capacity = 3
            });

            Assert.That(map.TrySelectRoleDestination(
                HordeEnemyRole.Striker, 0, Vector3.zero,
                EdpcgPathIntent.RangedPerch, true,
                out _, out string strikerArea, out string strikerRoute), Is.True);
            Assert.That(strikerArea, Is.EqualTo("area.cover"));
            Assert.That(strikerRoute, Is.EqualTo("route.flank"));

            Assert.That(map.TrySelectRoleDestination(
                HordeEnemyRole.Gunship, 0, Vector3.zero,
                EdpcgPathIntent.RangedPerch, true,
                out _, out string gunshipArea, out string gunshipRoute), Is.True);
            Assert.That(gunshipArea, Is.EqualTo("area.exposed"));
            Assert.That(gunshipRoute, Is.EqualTo("route.fire"));
        }

        [Test]
        public void TacticalAnnotationsCoverEveryAreaAndUseVerifiedOcclusionLines()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
            Assert.That(template, Is.Not.Null);
            GameObject city = Object.Instantiate(template);
            GameObject urbanObject = new GameObject("EDPCG_Annotation_Urban");
            try
            {
                AirCombatCityPcgLab lab =
                    city.GetComponent<AirCombatCityPcgLab>();
                Assert.That(lab, Is.Not.Null);
                lab.ConfigureRuntimeMission(
                    7319,
                    AirCombatCityMission.Clearance,
                    2);
                Assert.That(lab.HasValidPlan, Is.True, lab.LastSummary);

                FinitePlanetUrbanCombatRuntime urban =
                    urbanObject.AddComponent<FinitePlanetUrbanCombatRuntime>();
                SetPrivateField(urban, "cityLab", lab);
                EdpcgCityTacticalRuntimeMap map =
                    EdpcgCityTacticalRuntimeMap.Build(urban);
                EdpcgCityTacticalRuntimeMap labPreviewMap =
                    EdpcgCityTacticalRuntimeMap.Build(lab);

                Assert.That(map.Areas.Count, Is.GreaterThan(0));
                Assert.That(labPreviewMap.Areas.Count,
                    Is.EqualTo(map.Areas.Count));
                Assert.That(labPreviewMap.TacticalArrows.Count,
                    Is.EqualTo(map.TacticalArrows.Count));
                for (int areaIndex = 0;
                     areaIndex < map.Areas.Count;
                     areaIndex++)
                {
                    string areaId = map.Areas[areaIndex].StableId;
                    bool found = false;
                    for (int arrowIndex = 0;
                         arrowIndex < map.TacticalArrows.Count;
                         arrowIndex++)
                    {
                        if (map.TacticalArrows[arrowIndex].AreaId != areaId)
                            continue;
                        found = true;
                        break;
                    }
                    Assert.That(found, Is.True,
                        "Every displayed tactical area needs a truthful intent arrow: " +
                        areaId);
                }

                int verifiedBlockedLines = 0;
                for (int index = 0;
                     index < map.TacticalArrows.Count;
                     index++)
                {
                    EdpcgRuntimeTacticalArrow arrow = map.TacticalArrows[index];
                    if (arrow.Kind !=
                        EdpcgTacticalArrowKind.VerifiedBlockedFireLine)
                    {
                        continue;
                    }
                    Assert.That(arrow.Verified, Is.True);
                    Assert.That(arrow.Marker, Is.Not.EqualTo(Vector3.zero));
                    verifiedBlockedLines++;
                }
                Assert.That(verifiedBlockedLines, Is.GreaterThan(0),
                    "Occlusion labels must be backed by the same physical tower test as the city validator.");
            }
            finally
            {
                Object.DestroyImmediate(urbanObject);
                Object.DestroyImmediate(city);
            }
        }

        [Test]
        public void GridFireAnalysisCoversThreeAltitudeLayersWithoutSceneMutation()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
            Assert.That(template, Is.Not.Null);
            GameObject city = Object.Instantiate(template);
            try
            {
                UnityEngine.SceneManagement.Scene activeScene =
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                bool initialSceneDirty = activeScene.isDirty;
                int initialRootCount = activeScene.rootCount;
                AirCombatCityPcgLab lab =
                    city.GetComponent<AirCombatCityPcgLab>();
                Assert.That(lab, Is.Not.Null);
                lab.ConfigureRuntimeMission(
                    7319,
                    AirCombatCityMission.Clearance,
                    2);
                Assert.That(lab.HasValidPlan, Is.True, lab.LastSummary);

                var layers = new EdpcgGridFireAnalysis[3];
                foreach (EdpcgFireAnalysisAltitudeLayer layer in
                         (EdpcgFireAnalysisAltitudeLayer[])System.Enum.GetValues(
                             typeof(EdpcgFireAnalysisAltitudeLayer)))
                {
                    EdpcgGridFireAnalysis analysis =
                        EdpcgGridFireAnalyzer.Build(lab, layer);
                    layers[(int)layer] = analysis;
                    Assert.That(analysis.IsUsable, Is.True);
                    Assert.That(analysis.cells.Count, Is.EqualTo(64));
                    Assert.That(analysis.flyableCellCount,
                        Is.GreaterThan(0));
                    Assert.That(analysis.sourceCount, Is.GreaterThan(0));
                    Assert.That(analysis.incomingLineCount,
                        Is.GreaterThan(0));
                    Assert.That(analysis.blockedLineCount,
                        Is.GreaterThan(0));
                    Assert.That(analysis.buildMilliseconds,
                        Is.LessThan(500f));
                    Assert.That(analysis.TryGetCell(3, 3,
                        out EdpcgGridFireCell center), Is.True);
                    Assert.That(center.worldCorners.Length, Is.EqualTo(4));
                    for (int lineIndex = 0;
                         lineIndex < center.fireLines.Count;
                         lineIndex++)
                    {
                        EdpcgGridFireLine line = center.fireLines[lineIndex];
                        Assert.That(line.sourceKind,
                            Is.Not.EqualTo((EdpcgGridFireSourceKind)99));
                        Assert.That(line.distance,
                            Is.LessThanOrEqualTo(420.01f));
                        Assert.That(line.exposureRatio,
                            Is.InRange(0f, 1f));
                        if (!line.incoming)
                        {
                            Assert.That(line.blockerId,
                                Is.Not.Empty);
                            Assert.That(line.blockerWorldPosition,
                                Is.Not.EqualTo(Vector3.zero));
                        }
                    }
                }
                EdpcgCityTacticalChallengeReport challengeReport =
                    EdpcgCityTacticalChallengeEvaluator.Evaluate(
                        EdpcgCityTacticalChallengeProfile
                            .LoadOrCreateMemoryDefault().Resolve(2),
                        layers[0],
                        layers[1],
                        layers[2],
                        EdpcgCityTacticalRuntimeMap.Build(lab));
                Assert.That(challengeReport.evaluable, Is.True);
                Assert.That(challengeReport.flyableCellCount,
                    Is.GreaterThan(0));
                Assert.That(challengeReport.summary, Is.Not.Empty);
                Assert.That(activeScene.rootCount, Is.EqualTo(initialRootCount),
                    "只读分析不能创建额外场景根对象。");
                Assert.That(activeScene.isDirty, Is.EqualTo(initialSceneDirty),
                    "只读分析不能改变场景脏状态。");
            }
            finally
            {
                Object.DestroyImmediate(city);
            }
        }

        [Test]
        public void AirThreatDomainFindsTimedGapWindowsAndVerticalDirections()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
            Assert.That(template, Is.Not.Null);
            GameObject city = Object.Instantiate(template);
            try
            {
                AirCombatCityPcgLab lab =
                    city.GetComponent<AirCombatCityPcgLab>();
                lab.ConfigureRuntimeMission(
                    7319,
                    AirCombatCityMission.Clearance,
                    2);
                Assert.That(lab.HasValidPlan, Is.True, lab.LastSummary);
                EdpcgAirThreatAnalysisOptions options =
                    EdpcgAirThreatAnalysisOptions.CreateDefault(
                        lab.Settings, 2);
                var layers = new EdpcgGridFireAnalysis[3];
                for (int layer = 0; layer < layers.Length; layer++)
                {
                    layers[layer] = EdpcgGridFireAnalyzer.Build(
                        lab,
                        (EdpcgFireAnalysisAltitudeLayer)layer,
                        -1f,
                        options);
                }
                EdpcgGridFireAnalyzer.LinkAltitudeLayers(
                    layers[0], layers[1], layers[2],
                    lab.RuntimeGeometrySnapshot, options);

                int gapWindows = 0;
                int fleetingWindows = 0;
                int verticalDirections = 0;
                int reachableVerticalExits = 0;
                int firingVolumeLines = 0;
                int nonLegacyAltitudeLines = 0;
                int belowConfiguredLowLayer = 0;
                int occludedSpatialDirections = 0;
                int outsideOperationalAltitude = 0;
                float requiredVisible =
                    options.tierSettings.lineOfSightHysteresisSeconds +
                    options.tierSettings.rangedTelegraphSeconds;
                for (int layer = 0; layer < layers.Length; layer++)
                {
                    EdpcgGridFireAnalysis analysis = layers[layer];
                    Assert.That(analysis.algorithmVersion, Is.EqualTo(3));
                    Assert.That(analysis.geometryConfidence,
                        Is.InRange(0.5f, 1f));
                    Assert.That(analysis.buildMilliseconds,
                        Is.LessThan(500f));
                    gapWindows += analysis.gapWindowCount;
                    fleetingWindows += analysis.fleetingWindowCount;
                    for (int cellIndex = 0;
                         cellIndex < analysis.cells.Count;
                         cellIndex++)
                    {
                        EdpcgGridFireCell cell = analysis.cells[cellIndex];
                        Assert.That(cell.flyableSubSampleCount,
                            Is.InRange(0, 9));
                        Assert.That(cell.threatenedVolumeRatio,
                            Is.InRange(0f, 1f));
                        occludedSpatialDirections +=
                            cell.firingVolumeOcclusions.Count;
                        for (int lineIndex = 0;
                             lineIndex < cell.fireLines.Count;
                             lineIndex++)
                        {
                            EdpcgGridFireLine line =
                                cell.fireLines[lineIndex];
                            if (line.sourceId == null ||
                                !line.sourceId.StartsWith(
                                    "有效包络",
                                    System.StringComparison.Ordinal))
                            {
                                continue;
                            }
                            firingVolumeLines++;
                            float sourceY = line.sourceWorldPosition.y;
                            if (Mathf.Abs(sourceY - 60f) > 0.1f &&
                                Mathf.Abs(sourceY - 110f) > 0.1f &&
                                Mathf.Abs(sourceY - 160f) > 0.1f)
                            {
                                nonLegacyAltitudeLines++;
                            }
                            if (sourceY < lab.Settings.lowAltitude - 0.1f)
                                belowConfiguredLowLayer++;
                            if (sourceY <
                                    analysis.minimumFiringAltitude - 0.1f ||
                                sourceY >
                                    analysis.maximumFiringAltitude + 0.1f)
                            {
                                outsideOperationalAltitude++;
                            }
                        }
                        for (int windowIndex = 0;
                             windowIndex < cell.fireWindows.Count;
                             windowIndex++)
                        {
                            EdpcgAirFireWindow window =
                                cell.fireWindows[windowIndex];
                            Assert.That(window.visibleWindowSeconds,
                                Is.GreaterThanOrEqualTo(
                                    requiredVisible - 0.001f));
                            Assert.That(window.firstHitSeconds,
                                Is.GreaterThanOrEqualTo(
                                    window.setupSeconds));
                            Assert.That(window.directionIndex,
                                Is.InRange(0, 23));
                            float firingDistance = Vector3.Distance(
                                    window.firingWorldPosition,
                                    window.playerWorldPosition);
                            Assert.That(firingDistance,
                                Is.LessThanOrEqualTo(420.01f));
                            if (window.stableId.StartsWith(
                                    "有效包络",
                                    System.StringComparison.Ordinal))
                            {
                                float roleRadius = window.sourceKind ==
                                                   EdpcgGridFireSourceKind.Striker
                                    ? 170f
                                    : 220f;
                                Assert.That(firingDistance,
                                    Is.LessThanOrEqualTo(roleRadius + 0.01f),
                                    "主动占位枪线不能膨胀到420米理论射程。");
                            }
                            if (window.elevationBand != 0)
                                verticalDirections++;
                        }
                        for (int exitIndex = 0;
                             exitIndex < cell.evasions.Count;
                             exitIndex++)
                        {
                            EdpcgGridEvasionLink link =
                                cell.evasions[exitIndex];
                            if (link.verticalTransfer && link.reachable)
                                reachableVerticalExits++;
                        }
                    }
                }
                Assert.That(gapWindows, Is.GreaterThan(0));
                Assert.That(fleetingWindows, Is.GreaterThan(0));
                Assert.That(verticalDirections, Is.GreaterThan(0));
                Assert.That(reachableVerticalExits, Is.GreaterThan(0),
                    "三层之间仍需存在物理可达的转移；是否推荐取决于当格风险差。");
                Assert.That(firingVolumeLines, Is.GreaterThan(0));
                Assert.That(nonLegacyAltitudeLines,
                    Is.GreaterThan(firingVolumeLines * 0.9f),
                    "有效枪位包络不应退化回60/110/160米三档枪位。");
                Assert.That(belowConfiguredLowLayer, Is.GreaterThan(0),
                    "有效枪位包络必须覆盖贴地追击形成的合法低空枪位。");
                Assert.That(outsideOperationalAltitude, Is.Zero,
                    "有效枪位不能越出普通小怪真实航层与重接敌高度包络。");
                Assert.That(occludedSpatialDirections, Is.GreaterThan(0),
                    "可视化需要记录有效枪位包络被城市实体裁切的代表方向。");
            }
            finally
            {
                Object.DestroyImmediate(city);
            }
        }

        [Test]
        public void TierOneHasBudgetForTwoTokenGunshipFireWindows()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab");
            Assert.That(template, Is.Not.Null);
            GameObject city = Object.Instantiate(template);
            try
            {
                AirCombatCityPcgLab lab =
                    city.GetComponent<AirCombatCityPcgLab>();
                lab.ConfigureRuntimeMission(
                    7319,
                    AirCombatCityMission.Clearance,
                    0);
                EdpcgAirThreatAnalysisOptions options =
                    EdpcgAirThreatAnalysisOptions.CreateDefault(
                        lab.Settings, 0);
                Assert.That(options.tierSettings.attackTokenCap,
                    Is.EqualTo(2));
                EdpcgGridFireAnalysis analysis =
                    EdpcgGridFireAnalyzer.Build(
                        lab,
                        EdpcgFireAnalysisAltitudeLayer.Medium,
                        -1f,
                        options);
                int gunshipWindows = 0;
                for (int cellIndex = 0;
                     cellIndex < analysis.cells.Count;
                     cellIndex++)
                {
                    EdpcgGridFireCell cell = analysis.cells[cellIndex];
                    for (int windowIndex = 0;
                         windowIndex < cell.fireWindows.Count;
                         windowIndex++)
                    {
                        EdpcgAirFireWindow window =
                            cell.fireWindows[windowIndex];
                        if (window.sourceKind !=
                            EdpcgGridFireSourceKind.Gunship)
                        {
                            continue;
                        }
                        gunshipWindows++;
                    }
                }
                Assert.That(gunshipWindows, Is.GreaterThan(0));
                Assert.That(options.tierSettings.gunshipCount,
                    Is.GreaterThan(0));
                Assert.That(options.tierSettings.attackTokenCap,
                    Is.GreaterThanOrEqualTo(2),
                    "最低档必须具备授权一次炮艇射击所需的两枚令牌；" +
                    "单格静态排序仍可由更快的突击机先占预算。" );
            }
            finally
            {
                Object.DestroyImmediate(city);
            }
        }

        [Test]
        public void DiagonalBuildingGapIsDetectedButWideStreetIsNot()
        {
            System.Type solverType = typeof(EdpcgGridFireAnalyzer).Assembly.GetType(
                "UnityPlanet.EDPCG.EdpcgAirThreatSolver", true);
            System.Type geometryType = solverType.GetNestedType(
                "GeometryIndex", BindingFlags.NonPublic);
            Assert.That(geometryType, Is.Not.Null);
            MethodInfo resolveGap = geometryType.GetMethod(
                "TryResolveOpposingBuildingGap",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(resolveGap, Is.Not.Null);

            Vector3 across = Quaternion.Euler(0f, 45f, 0f) * Vector3.right;
            AirCombatCityRuntimeGeometrySnapshot snapshot =
                BuildTwoBuildingSnapshot(across * 20f, -across * 20f, 12f);
            object geometry = System.Activator.CreateInstance(
                geometryType,
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                new object[] { snapshot },
                null);
            object[] arguments = { new Vector3(0f, 60f, 0f), Vector3.zero };
            Assert.That((bool)resolveGap.Invoke(geometry, arguments), Is.True);
            Assert.That(((Vector3)arguments[1]).sqrMagnitude,
                Is.GreaterThan(0.9f));

            snapshot = BuildTwoBuildingSnapshot(
                across * 65f, -across * 65f, 10f);
            geometry = System.Activator.CreateInstance(
                geometryType,
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
                null,
                new object[] { snapshot },
                null);
            arguments = new object[]
                { new Vector3(0f, 60f, 0f), Vector3.zero };
            Assert.That((bool)resolveGap.Invoke(geometry, arguments), Is.False);
        }

        [Test]
        public void CrossfireRequiresOverlappingIndependentFireWindows()
        {
            System.Type solverType = typeof(EdpcgGridFireAnalyzer).Assembly.GetType(
                "UnityPlanet.EDPCG.EdpcgAirThreatSolver", true);
            MethodInfo method = solverType.GetMethod(
                "HasSeparatedDirections",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var windows = new List<EdpcgAirFireWindow>
            {
                new EdpcgAirFireWindow
                {
                    stableId = "north",
                    routeId = "route.north",
                    azimuthSector = 0,
                    setupSeconds = 1f,
                    approachSeconds = 0f,
                    visibleWindowSeconds = 3f,
                    authorizedByTier = true
                },
                new EdpcgAirFireWindow
                {
                    stableId = "south",
                    routeId = "route.south",
                    azimuthSector = 4,
                    setupSeconds = 6f,
                    approachSeconds = 5f,
                    visibleWindowSeconds = 3f,
                    authorizedByTier = true
                }
            };
            Assert.That((bool)method.Invoke(null,
                new object[] { windows, true }), Is.False);
            windows[1].setupSeconds = 2f;
            windows[1].approachSeconds = 1f;
            Assert.That((bool)method.Invoke(null,
                new object[] { windows, true }), Is.True);
        }

        [Test]
        public void GridDifficulty_QuietCellWithoutExitRemainsEasy()
        {
            var cell = new EdpcgGridFireCell
            {
                flyable = true,
                subSampleCount = 9,
                flyableSubSampleCount = 9
            };

            EdpcgGridDifficultyBreakdown result =
                EdpcgGridDifficultyEvaluator.Evaluate(
                    cell, 0, 0f, float.PositiveInfinity, false);

            Assert.That(result.ResponseRequired, Is.False);
            Assert.That(result.escapeDifficulty, Is.EqualTo(0f));
            Assert.That(result.score, Is.LessThan(0.18f));
            Assert.That(result.ChineseLevel, Is.EqualTo("容易"));
        }

        [Test]
        public void GridDifficulty_ReachableTimedExitReducesThreatenedCellDifficulty()
        {
            var trapped = new EdpcgGridFireCell
            {
                flyable = true,
                subSampleCount = 9,
                flyableSubSampleCount = 9
            };
            var escapable = new EdpcgGridFireCell
            {
                flyable = true,
                subSampleCount = 9,
                flyableSubSampleCount = 9,
                bestEscapeMarginSeconds = 2.5f
            };
            escapable.evasions.Add(new EdpcgGridEvasionLink
            {
                reachable = true,
                recommended = true,
                pressureReduction = 0.35f,
                escapeMarginSeconds = 2.5f
            });

            EdpcgGridDifficultyBreakdown trappedResult =
                EdpcgGridDifficultyEvaluator.Evaluate(
                    trapped, 2, 0.55f, 2f, false);
            EdpcgGridDifficultyBreakdown escapableResult =
                EdpcgGridDifficultyEvaluator.Evaluate(
                    escapable, 2, 0.55f, 2f, false);

            Assert.That(trappedResult.escapeDifficulty,
                Is.GreaterThan(escapableResult.escapeDifficulty));
            Assert.That(trappedResult.score,
                Is.GreaterThan(escapableResult.score));
        }

        [Test]
        public void GridDifficulty_FasterCrossfireIsHarderThanSlowSingleFire()
        {
            var cell = new EdpcgGridFireCell
            {
                flyable = true,
                subSampleCount = 9,
                flyableSubSampleCount = 9
            };

            EdpcgGridDifficultyBreakdown slow =
                EdpcgGridDifficultyEvaluator.Evaluate(
                    cell, 1, 0.4f, 7f, false);
            EdpcgGridDifficultyBreakdown fastCrossfire =
                EdpcgGridDifficultyEvaluator.Evaluate(
                    cell, 2, 0.4f, 1.5f, true);

            Assert.That(fastCrossfire.fireThreat,
                Is.GreaterThan(slow.fireThreat));
            Assert.That(fastCrossfire.score, Is.GreaterThan(slow.score));
            Assert.That(fastCrossfire.ChineseLevel, Is.EqualTo("高危"));
        }

        [Test]
        public void GridDifficulty_MoreReachableTransitionsImproveManeuverability()
        {
            var narrow = new EdpcgGridFireCell
            {
                flyable = true,
                subSampleCount = 9,
                flyableSubSampleCount = 9
            };
            var open = new EdpcgGridFireCell
            {
                flyable = true,
                subSampleCount = 9,
                flyableSubSampleCount = 9
            };
            narrow.evasions.Add(new EdpcgGridEvasionLink
                { reachable = true });
            for (int index = 0; index < 5; index++)
            {
                open.evasions.Add(new EdpcgGridEvasionLink
                    { reachable = true });
            }

            EdpcgGridDifficultyBreakdown narrowResult =
                EdpcgGridDifficultyEvaluator.Evaluate(
                    narrow, 0, 0f, float.PositiveInfinity, false);
            EdpcgGridDifficultyBreakdown openResult =
                EdpcgGridDifficultyEvaluator.Evaluate(
                    open, 0, 0f, float.PositiveInfinity, false);

            Assert.That(narrowResult.maneuverDifficulty,
                Is.GreaterThan(openResult.maneuverDifficulty));
            Assert.That(narrowResult.score,
                Is.GreaterThan(openResult.score));
        }

        [Test]
        public void CityChallengeProfilePersistsBothOwnersAndSixValidatedTiers()
        {
            EdpcgCityTacticalChallengeProfile challenge =
                AssetDatabase.LoadAssetAtPath<
                    EdpcgCityTacticalChallengeProfile>(
                    "Assets/Resources/EDPCG/EdpcgCityTacticalChallengeProfile.asset");
            Assert.That(challenge, Is.Not.Null);
            challenge.EnsureInitialized();
            Assert.That(challenge.cityGeometryProfile, Is.Not.Null);
            Assert.That(challenge.ordinaryEnemyProfile, Is.Not.Null);
            Assert.That(challenge.tiers.Length, Is.EqualTo(6));
            for (int tier = 0; tier < challenge.tiers.Length; tier++)
            {
                EdpcgCityTacticalChallengeSettings settings =
                    challenge.Resolve(tier);
                Assert.That(settings.playerCellDwellSeconds,
                    Is.InRange(2f, 20f));
                Assert.That(settings.maximumPressureDirections,
                    Is.InRange(1, 3));
                Assert.That(settings.minimumSafeExitCount,
                    Is.InRange(1, 4));
                Assert.That(settings.maximumCrossfireCellCount,
                    Is.GreaterThanOrEqualTo(
                        settings.minimumCrossfireCellCount));
            }
        }

        [Test]
        public void GridPressureRepositionUsesVerifiedWindowAndHonorsDirectionCap()
        {
            GameObject runtimeObject = new GameObject("EDPCG_GridPressure_Runtime");
            GameObject enemyObject = new GameObject("EDPCG_GridPressure_Enemy");
            GameObject directorObject = new GameObject("EDPCG_GridPressure_Director");
            GameObject playerObject = new GameObject("EDPCG_GridPressure_Player");
            try
            {
                EdpcgEncounterRuntime runtime =
                    runtimeObject.AddComponent<EdpcgEncounterRuntime>();
                EdpcgTierSettings tier =
                    EdpcgDifficultyProfile.LoadOrCreateMemoryDefault().Resolve(2);
                tier.integrationMode = EdpcgIntegrationMode.TacticalAssignments;
                var challenge = new EdpcgCityTacticalChallengeSettings
                {
                    enableOrdinaryRangedReposition = true,
                    playerCellDwellSeconds = 5f,
                    maximumPressureDirections = 1,
                    minimumSafeExitCount = 2,
                    maximumAcceptedCellPressure = 0.8f,
                    allowStrikerReposition = true,
                    allowGunshipReposition = true
                };
                var cell = new EdpcgGridFireCell
                {
                    stableId = "tactical-block.03.03",
                    flyable = true,
                    pressureScore = 0.45f,
                    safeExitCount = 2
                };
                Vector3 isolatedPlayer = new Vector3(20000f, 110f, 0f);
                Vector3 isolatedFiring = isolatedPlayer + Vector3.right * 120f;
                cell.fireWindows.Add(new EdpcgAirFireWindow
                {
                    stableId = "window.masked.0",
                    routeId = "route.masked",
                    sourceKind = EdpcgGridFireSourceKind.Striker,
                    routeSegmentIndex = 0,
                    routeSegmentT = 0.5f,
                    azimuthSector = 2,
                    routeEntryWorldPosition = isolatedFiring,
                    firingWorldPosition = isolatedFiring,
                    playerWorldPosition = isolatedPlayer,
                    authorizedByTier = true,
                    robustHullClear = true,
                    navigationCorridorClear = true,
                    aiPermissionCorridorClear = true,
                    projectileCorridorClear = true
                });
                Rigidbody playerBody = playerObject.AddComponent<Rigidbody>();
                playerBody.isKinematic = true;
                playerObject.transform.position = isolatedPlayer;
                Physics.SyncTransforms();
                HordeCombatDirector director =
                    directorObject.AddComponent<HordeCombatDirector>();
                SetPrivateField(director, "navigation",
                    new HordeAirNavigationService());
                SetPrivateField(runtime, "settings", tier);
                SetPrivateField(runtime, "cityChallengeSettings", challenge);
                SetPrivateField(runtime, "activeGridFireCell", cell);
                SetPrivateField(runtime, "director", director);
                SetPrivateField(runtime, "playerBody", playerBody);
                SetPrivateField(runtime, "elapsed", 10f);
                SetPrivateField(runtime, "gridCellCandidateSince", 0f);
                SetPrivateField(runtime, "pressureDirectionCount", 0);
                SetPrivateField(runtime, "pressureDirectionMask", 0);

                HordeEnemyVehicle enemy =
                    enemyObject.AddComponent<HordeEnemyVehicle>();
                SetPrivateField(enemy, "profile",
                    HordeEnemyProfile.ForRole(HordeEnemyRole.Striker));
                SetAutoProperty(enemy, "RosterMemberId", "striker-1");
                SetAutoProperty(enemy, "RosterIndex", 0);

                MethodInfo method = typeof(EdpcgEncounterRuntime).GetMethod(
                    "TryResolveGridPressureDestination",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                object[] arguments =
                {
                    enemy,
                    EdpcgPathIntent.RangedPerch,
                    Vector3.zero,
                    string.Empty,
                    string.Empty
                };
                Assert.That(method.GetParameters().Length,
                    Is.EqualTo(arguments.Length));
                Assert.That((bool)method.Invoke(runtime, arguments), Is.True);
                Assert.That((Vector3)arguments[2],
                    Is.EqualTo(isolatedFiring));
                Assert.That((string)arguments[3],
                    Is.EqualTo("tactical-block.03.03"));
                Assert.That((string)arguments[4],
                    Is.EqualTo("route.masked"));

                // Gap anchors are editor diagnostics until the real movement
                // controller can validate turning, braking and loiter space.
                cell.fireWindows[0].throughBuildingGap = true;
                arguments[2] = Vector3.zero;
                arguments[3] = string.Empty;
                arguments[4] = string.Empty;
                Assert.That((bool)method.Invoke(runtime, arguments), Is.False);
                cell.fireWindows[0].throughBuildingGap = false;

                // A diagnostic gap direction must never consume the sector
                // choice and hide a separate, runtime-usable firing window.
                Vector3 diagnosticGap =
                    isolatedPlayer + Vector3.forward * 120f;
                cell.fireWindows.Insert(0, new EdpcgAirFireWindow
                {
                    stableId = "window.gap.diagnostic",
                    routeId = "route.gap",
                    sourceKind = EdpcgGridFireSourceKind.Striker,
                    routeSegmentIndex = 0,
                    routeSegmentT = 0.5f,
                    azimuthSector = 0,
                    routeEntryWorldPosition = diagnosticGap,
                    firingWorldPosition = diagnosticGap,
                    playerWorldPosition = isolatedPlayer,
                    authorizedByTier = true,
                    throughBuildingGap = true,
                    robustHullClear = true,
                    navigationCorridorClear = true,
                    aiPermissionCorridorClear = true,
                    projectileCorridorClear = true
                });
                arguments[2] = Vector3.zero;
                arguments[3] = string.Empty;
                arguments[4] = string.Empty;
                Assert.That((bool)method.Invoke(runtime, arguments), Is.True);
                Assert.That((Vector3)arguments[2],
                    Is.EqualTo(isolatedFiring));
                cell.fireWindows.RemoveAt(0);

                // The one-direction budget is already occupied by a different
                // direction. The same enemy must not open a second direction.
                SetPrivateField(runtime, "pressureDirectionCount", 1);
                SetPrivateField(runtime, "pressureDirectionMask", 1 << 6);
                arguments[2] = Vector3.zero;
                arguments[3] = string.Empty;
                arguments[4] = string.Empty;
                Assert.That((bool)method.Invoke(runtime, arguments), Is.False);

                // Suicide interceptors never enter the ranged reposition path.
                SetPrivateField(enemy, "profile",
                    HordeEnemyProfile.ForRole(HordeEnemyRole.Interceptor));
                SetPrivateField(runtime, "pressureDirectionCount", 0);
                SetPrivateField(runtime, "pressureDirectionMask", 0);
                Assert.That((bool)method.Invoke(runtime, arguments), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(enemyObject);
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void ReversibleReservationKeepsItsDirectionAndWaypointProgress()
        {
            GameObject root = new GameObject("EDPCG_RouteProgress_Test");
            try
            {
                EdpcgEncounterRuntime runtime = BuildRuntime(root, 0, 113);
                var map = new EdpcgCityTacticalRuntimeMap();
                var route = new EdpcgRuntimeRoute
                {
                    StableId = "route.reversible",
                    SourceKind = AirCombatRouteKind.Main,
                    Points = new[]
                    {
                        Vector3.zero,
                        Vector3.right * 100f,
                        Vector3.right * 200f
                    },
                    Width = 60f,
                    Capacity = 2,
                    AllowReverse = true
                };
                RegisterRoute(map, route);
                var reservations = new EdpcgRouteReservationService(map);
                Assert.That(reservations.TryReserve(
                    "enemy-1",
                    route.StableId,
                    Time.time,
                    5f,
                    8f,
                    1,
                    -1,
                    out _), Is.True);
                Assert.That(reservations.Reservations[0].direction,
                    Is.EqualTo(-1));
                Assert.That(reservations.Reservations[0].waypointIndex,
                    Is.EqualTo(2));
                SetPrivateField(runtime, "tacticalMap", map);
                SetPrivateField(runtime, "reservations", reservations);

                var output = new List<Vector3>();
                Assert.That(runtime.TryGetReservedRouteWaypoints(
                    "enemy-1",
                    Vector3.right * 195f,
                    output), Is.True);
                Assert.That(output, Is.EqualTo(new[]
                {
                    Vector3.right * 100f,
                    Vector3.zero
                }));
                Assert.That(reservations.Reservations[0].waypointIndex,
                    Is.EqualTo(1));

                Assert.That(runtime.TryGetReservedRouteWaypoints(
                    "enemy-1",
                    Vector3.right * 105f,
                    output), Is.True);
                Assert.That(output, Is.EqualTo(new[] { Vector3.zero }));
                Assert.That(reservations.Reservations[0].waypointIndex,
                    Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TargetedReservationStopsAtRouteProgressWithoutOvershoot()
        {
            GameObject root = new GameObject("EDPCG_TargetedRoute_Test");
            try
            {
                EdpcgEncounterRuntime runtime = root.AddComponent<
                    EdpcgEncounterRuntime>();
                var map = new EdpcgCityTacticalRuntimeMap();
                var route = new EdpcgRuntimeRoute
                {
                    StableId = "route.targeted",
                    SourceKind = AirCombatRouteKind.MaskedFlank,
                    Points = new[]
                    {
                        Vector3.zero,
                        Vector3.right * 100f,
                        Vector3.right * 200f,
                        Vector3.right * 300f
                    },
                    Width = 60f,
                    Capacity = 2,
                    AllowReverse = true
                };
                RegisterRoute(map, route);
                var reservations = new EdpcgRouteReservationService(map);
                Assert.That(reservations.TryReserveToProgress(
                    "enemy-targeted",
                    route.StableId,
                    Time.time,
                    3f,
                    4f,
                    1,
                    1,
                    1,
                    0.35f,
                    new Vector3(135f, 0f, 0f),
                    "gap-anchor-1",
                    out _), Is.True);
                Assert.That(reservations.TryReserveToProgress(
                    "enemy-nan",
                    route.StableId,
                    Time.time,
                    float.NaN,
                    4f,
                    1,
                    1,
                    1,
                    0.35f,
                    new Vector3(135f, 0f, 0f),
                    "invalid-nan",
                    out _), Is.False);
                Assert.That(reservations.TryReserveToProgress(
                    "enemy-drift",
                    route.StableId,
                    Time.time,
                    3f,
                    4f,
                    1,
                    1,
                    1,
                    0.35f,
                    new Vector3(140f, 0f, 0f),
                    "invalid-drift",
                    out _), Is.False);
                SetPrivateField(runtime, "tacticalMap", map);
                SetPrivateField(runtime, "reservations", reservations);
                var waypoints = new List<Vector3>();
                Assert.That(runtime.TryGetReservedRouteWaypoints(
                    "enemy-targeted",
                    new Vector3(20f, 0f, 0f),
                    waypoints), Is.True);
                Assert.That(waypoints.Count, Is.GreaterThan(0));
                Assert.That(waypoints[waypoints.Count - 1].x,
                    Is.EqualTo(135f).Within(0.01f));
                for (int index = 0; index < waypoints.Count; index++)
                {
                    Assert.That(waypoints[index].x,
                        Is.LessThanOrEqualTo(135.01f));
                }
                Assert.That(waypoints,
                    Has.None.EqualTo(new Vector3(200f, 0f, 0f)));
                Assert.That(waypoints,
                    Has.None.EqualTo(new Vector3(300f, 0f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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

        [Test]
        public void ObservationPressureKeepsLegacyControlAndSeparatesSystemHealth()
        {
            GameObject legacyRoot = new GameObject("EDPCG_Legacy_Pressure");
            GameObject observedRoot = new GameObject("EDPCG_Observed_Pressure");
            try
            {
                EdpcgEncounterRuntime legacy = BuildRuntime(legacyRoot, 1, 441);
                EdpcgEncounterRuntime observed = BuildRuntime(observedRoot, 1, 441);

                legacy.UpdateDirectorSnapshot(3, 2, 1, 1, 1, 2, 3);
                observed.UpdateDirectorSnapshot(3, 2, 1, 1, 1, 2, 3, 3);
                legacy.Tick(0.1f);
                observed.Tick(0.1f);

                Assert.That(observed.CurrentSample.actualPressure,
                    Is.EqualTo(legacy.CurrentSample.actualPressure).Within(0.000001f),
                    "Pressure directions must not change Legacy control pressure.");
                Assert.That(observed.CurrentSample.observedCombatPressure,
                    Is.GreaterThan(0f));
                Assert.That(observed.CurrentSample.systemHealthIssueRatio,
                    Is.GreaterThan(0f));
                Assert.That(observed.CurrentSample.environmentObservationAvailable,
                    Is.False,
                    "Authored trap metadata must not be presented as measured force exposure.");

                float combatBeforeRecovery = observed.CurrentSample.observedCombatPressure;
                observed.UpdateDirectorSnapshot(3, 2, 1, 1, 1, 6, 3, 3);
                observed.Tick(0.1f);
                Assert.That(observed.CurrentSample.observedCombatPressure,
                    Is.EqualTo(combatBeforeRecovery).Within(0.000001f),
                    "Navigation recovery is a system-health issue, not combat pressure.");
            }
            finally
            {
                Object.DestroyImmediate(legacyRoot);
                Object.DestroyImmediate(observedRoot);
            }
        }

        static AirCombatCityRuntimeGeometrySnapshot BuildTwoBuildingSnapshot(
            Vector3 firstCenter,
            Vector3 secondCenter,
            float horizontalSize)
        {
            firstCenter.y = 75f;
            secondCenter.y = 75f;
            return new AirCombatCityRuntimeGeometrySnapshot
            {
                instantiatedBuildingCount = 2,
                buildings = new[]
                {
                    new AirCombatRuntimeBuildingGeometry
                    {
                        stableId = "building.first",
                        localBounds = new Bounds(firstCenter,
                            new Vector3(horizontalSize, 150f,
                                horizontalSize))
                    },
                    new AirCombatRuntimeBuildingGeometry
                    {
                        stableId = "building.second",
                        localBounds = new Bounds(secondCenter,
                            new Vector3(horizontalSize, 150f,
                                horizontalSize))
                    }
                }
            };
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

        static void RegisterArea(
            EdpcgCityTacticalRuntimeMap map,
            EdpcgRuntimeArea area)
        {
            FieldInfo areasField = typeof(EdpcgCityTacticalRuntimeMap)
                .GetField("areas", BindingFlags.Instance |
                                   BindingFlags.NonPublic);
            FieldInfo areaByIdField = typeof(EdpcgCityTacticalRuntimeMap)
                .GetField("areaById", BindingFlags.Instance |
                                      BindingFlags.NonPublic);
            Assert.That(areasField, Is.Not.Null);
            Assert.That(areaByIdField, Is.Not.Null);
            ((List<EdpcgRuntimeArea>)areasField.GetValue(map)).Add(area);
            ((Dictionary<string, EdpcgRuntimeArea>)areaByIdField
                .GetValue(map))[area.StableId] = area;
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

        static void SetAutoProperty(
            object target,
            string propertyName,
            object value)
        {
            SetPrivateField(
                target,
                "<" + propertyName + ">k__BackingField",
                value);
        }
    }
}
#endif
