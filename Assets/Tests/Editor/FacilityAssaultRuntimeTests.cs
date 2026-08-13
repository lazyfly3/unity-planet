#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.EDPCG;

public sealed class FacilityAssaultRuntimeTests
{
    [TestCase(1, 26)]
    [TestCase(2, 52)]
    [TestCase(3, 78)]
    public void FacilityShellLayout_BuildsConnectedSymmetricLayeredCube(
        int layers,
        int expectedCount)
    {
        for (int objectiveIndex = 0; objectiveIndex < 3; objectiveIndex++)
        {
            IReadOnlyList<Vector3Int> positions =
                FacilityAssaultShellLayout.CreatePositions(
                    layers,
                    objectiveIndex);

            Assert.That(positions, Is.Not.Null);
            Assert.That(positions.Count, Is.EqualTo(expectedCount));
            var unique = new HashSet<Vector3Int>(positions);
            Assert.That(unique.Count, Is.EqualTo(expectedCount),
                "壳体格点不能重复。目标=" + objectiveIndex);
            Assert.That(unique.Contains(Vector3Int.zero), Is.False,
                "壳体不能占用中央核心格。目标=" + objectiveIndex);

            for (int index = 0; index < positions.Count; index++)
            {
                Assert.That(unique.Contains(-positions[index]), Is.True,
                    "每个壳体格点必须存在中心反演对称点：" +
                    positions[index] + "，目标=" + objectiveIndex);
            }

            Assert.That(
                CountSixConnected(unique, positions[0]),
                Is.EqualTo(unique.Count),
                "所有壳体模块必须通过六邻接连成一个整体。目标=" +
                objectiveIndex);

            if (layers >= 1)
            {
                var expectedFirstLayer = new HashSet<Vector3Int>();
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int position = new Vector3Int(x, y, z);
                    if (position != Vector3Int.zero)
                        expectedFirstLayer.Add(position);
                }

                CollectionAssert.AreEquivalent(
                    expectedFirstLayer,
                    new List<Vector3Int>(positions).GetRange(0, 26),
                    "第一层必须是完整的3x3x3表面26格。目标=" +
                    objectiveIndex);
            }

            for (int layer = 1; layer < layers; layer++)
            {
                int previousOffset = (layer - 1) * 26;
                int currentOffset = layer * 26;
                for (int slot = 0; slot < 26; slot++)
                {
                    Vector3Int previous =
                        positions[previousOffset + slot];
                    Vector3Int current = positions[currentOffset + slot];
                    Assert.That(
                        ManhattanDistance(previous, current),
                        Is.EqualTo(1),
                        "同方向槽位的相邻壳层必须面连接：层=" +
                        (layer + 1) + "，槽=" + slot + "，目标=" +
                        objectiveIndex);
                }
            }

            FacilityAssaultDifficultySpec spec =
                FacilityAssaultDifficultySpec.Resolve((layers - 1) * 2);
            int maximumCoordinate = 0;
            for (int index = 0; index < positions.Count; index++)
            {
                Vector3Int position = positions[index];
                maximumCoordinate = Mathf.Max(
                    maximumCoordinate,
                    Mathf.Abs(position.x),
                    Mathf.Abs(position.y),
                    Mathf.Abs(position.z));
                Vector3 worldHalfExtent = new Vector3(
                    Mathf.Abs(position.x),
                    Mathf.Abs(position.y),
                    Mathf.Abs(position.z)) * spec.ModuleWorldSize +
                    Vector3.one * (spec.ModuleWorldSize * 0.5f);
                Assert.That(worldHalfExtent.x,
                    Is.LessThanOrEqualTo(spec.RequiredHalfExtents.x + 0.001f));
                Assert.That(worldHalfExtent.y,
                    Is.LessThanOrEqualTo(spec.RequiredHalfExtents.y + 0.001f));
                Assert.That(worldHalfExtent.z,
                    Is.LessThanOrEqualTo(spec.RequiredHalfExtents.z + 0.001f));
            }
            Assert.That(maximumCoordinate, Is.EqualTo(layers),
                "最外层至少要达到声明的层数包络。目标=" + objectiveIndex);
        }
    }

    [Test]
    public void FacilityAssaultDifficultySpec_AllTiersStayMonotonicAndBounded()
    {
        FacilityAssaultDifficultySpec previous = null;

        for (int tier = FacilityAssaultDifficultySpec.MinimumTier;
             tier <= FacilityAssaultDifficultySpec.MaximumTier;
             tier++)
        {
            FacilityAssaultDifficultySpec current =
                FacilityAssaultDifficultySpec.Resolve(tier);

            Assert.That(current.Tier, Is.EqualTo(tier));
            Assert.That(current.ShellLayers, Is.GreaterThanOrEqualTo(1));
            Assert.That(current.ModuleCount, Is.GreaterThan(0));
            Assert.That(
                current.ModuleCount,
                Is.LessThanOrEqualTo(
                    FacilityAssaultDifficultySpec.MaximumModulesPerFacility));
            Assert.That(
                current.ModuleCount,
                Is.EqualTo(
                    current.ShellLayers *
                    FacilityAssaultDifficultySpec.ModulesPerLayer));
            Assert.That(current.ModuleIntegrity, Is.GreaterThan(0f));
            Assert.That(current.CoreIntegrity, Is.GreaterThan(0f));

            if (previous != null)
            {
                Assert.That(
                    current.ShellLayers,
                    Is.GreaterThanOrEqualTo(previous.ShellLayers));
                Assert.That(
                    current.ModuleCount,
                    Is.GreaterThanOrEqualTo(previous.ModuleCount));
                Assert.That(
                    current.ModuleIntegrity,
                    Is.GreaterThan(previous.ModuleIntegrity));
                Assert.That(
                    current.CoreIntegrity,
                    Is.GreaterThan(previous.CoreIntegrity));
                Assert.That(
                    current.RequiredHalfExtents.x,
                    Is.GreaterThanOrEqualTo(previous.RequiredHalfExtents.x));
            }

            previous = current;
        }

        Assert.That(
            FacilityAssaultDifficultySpec.Resolve(-100).Tier,
            Is.EqualTo(FacilityAssaultDifficultySpec.MinimumTier));
        Assert.That(
            FacilityAssaultDifficultySpec.Resolve(100).Tier,
            Is.EqualTo(FacilityAssaultDifficultySpec.MaximumTier));

        Assert.That(
            FacilityAssaultShellLayout.IsBreachDirection(2),
            Is.False,
            "贴地设施的向下装甲链不能被当作玩家可用破口。");
        Assert.That(
            FacilityAssaultShellLayout.BreachLaneCount,
            Is.EqualTo(5));
    }

    [Test]
    public void FacilityArmorModule_DamageDoesNotLeakToCoreAndLethalDamageIsIdempotent()
    {
        GameObject facilityRoot = new GameObject("FacilityTestRoot");
        GameObject armorObject = new GameObject("ArmorModule");
        GameObject armorVisual = new GameObject("ArmorVisual");
        GameObject coreObject = new GameObject("Core");

        try
        {
            armorObject.transform.SetParent(facilityRoot.transform, false);
            armorVisual.transform.SetParent(armorObject.transform, false);
            coreObject.transform.SetParent(facilityRoot.transform, false);

            BoxCollider armorCollider =
                armorObject.AddComponent<BoxCollider>();
            FinitePlanetFacilityArmorModule armor =
                armorObject.AddComponent<FinitePlanetFacilityArmorModule>();
            armor.Configure(
                100f,
                0,
                0,
                1,
                0,
                Vector3Int.left,
                5f,
                armorCollider,
                armorVisual);

            FinitePlanetEnergyCoreObjective core =
                coreObject.AddComponent<FinitePlanetEnergyCoreObjective>();
            core.Configure(350f, 0, 2f, false);

            int damagedEvents = 0;
            int destroyedEvents = 0;
            armor.Damaged += (_, __) => damagedEvents++;
            armor.Destroyed += _ => destroyedEvents++;
            float initialCoreIntegrity = core.Integrity;
            SpaceDamageInfo lethalArmorHit = new SpaceDamageInfo(
                150f,
                armorObject.transform.position,
                Vector3.forward,
                SpaceDamageType.Projectile,
                null);

            armor.ApplyDamage(lethalArmorHit);
            armor.ApplyDamage(lethalArmorHit);

            Assert.That(armor.IsDestroyed, Is.True);
            Assert.That(armor.Integrity, Is.Zero);
            Assert.That(armorCollider.enabled, Is.False);
            Assert.That(armorVisual.activeSelf, Is.False);
            Assert.That(damagedEvents, Is.EqualTo(1));
            Assert.That(destroyedEvents, Is.EqualTo(1));
            Assert.That(core.Integrity, Is.EqualTo(initialCoreIntegrity));
            Assert.That(core.IsDestroyed, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(facilityRoot);
        }
    }

    [Test]
    public void AssaultHudProjection_OnScreenOffScreenAndBehindRemainFinite()
    {
        Rect safeViewport = Rect.MinMaxRect(0.1f, 0.15f, 0.9f, 0.85f);

        FinitePlanetAssaultObjectiveHud.FinitePlanetAssaultHudProjection
            onScreen = FinitePlanetAssaultObjectiveHud.ProjectViewportPoint(
                new Vector3(0.5f, 0.5f, 1f),
                safeViewport);
        FinitePlanetAssaultObjectiveHud.FinitePlanetAssaultHudProjection
            offScreen = FinitePlanetAssaultObjectiveHud.ProjectViewportPoint(
                new Vector3(1.8f, 0.65f, 1f),
                safeViewport);
        FinitePlanetAssaultObjectiveHud.FinitePlanetAssaultHudProjection
            behind = FinitePlanetAssaultObjectiveHud.ProjectViewportPoint(
                new Vector3(0.2f, 0.8f, -1f),
                safeViewport);

        Assert.That(onScreen.onScreen, Is.True);
        Assert.That(onScreen.behindCamera, Is.False);
        Assert.That(onScreen.viewportAnchor, Is.EqualTo(new Vector2(0.5f, 0.5f)));

        Assert.That(offScreen.onScreen, Is.False);
        Assert.That(offScreen.behindCamera, Is.False);
        AssertProjectionIsFiniteAndInside(offScreen, safeViewport);

        Assert.That(behind.onScreen, Is.False);
        Assert.That(behind.behindCamera, Is.True);
        AssertProjectionIsFiniteAndInside(behind, safeViewport);
    }

    [Test]
    public void EdpcgFacilityAssaultPacingPolicy_OnlyMatchesFormalMission()
    {
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.Applies("industrial_outpost"),
            Is.True);
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.Applies("Industrial_Outpost"),
            Is.False);
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.Applies("abandoned_mine"),
            Is.False);
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.Applies(null),
            Is.False);
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.SpawnIntervalMultiplier(
                "abandoned_mine"),
            Is.EqualTo(1f));
    }

    [Test]
    public void EdpcgFacilityAssaultPacingPolicy_KeepsMultipliersAndAssistBounded()
    {
        float facilityMultiplier =
            EdpcgFacilityAssaultPacingPolicy.SpawnIntervalMultiplier(
                "industrial_outpost");
        float lowPressureStep =
            EdpcgFacilityAssaultPacingPolicy.LowPressureStepSeconds(
                "industrial_outpost",
                8f);

        Assert.That(facilityMultiplier, Is.EqualTo(0.72f).Within(0.0001f));
        Assert.That(lowPressureStep, Is.EqualTo(1.5f).Within(0.0001f));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.FacilityCloseApproachDistance,
            Is.EqualTo(320f));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ObjectiveAlertCooldownSeconds,
            Is.EqualTo(3f));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.LowPressureStepSeconds(
                "abandoned_mine",
                -3f),
            Is.EqualTo(0.01f).Within(0.0001f));

        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveAssistFloor(0, 2, 3),
            Is.EqualTo(2));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveAssistFloor(5, 2, 3),
            Is.EqualTo(3));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveAssistFloor(2, 2, -1),
            Is.Zero);
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.MergeSpawnUrgency(-5, 99),
            Is.EqualTo(
                EdpcgFacilityAssaultPacingPolicy.MaximumSpawnUrgency));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.MergeSpawnUrgency(-5, -2),
            Is.Zero);
    }

    [Test]
    public void FacilityAssaultPacingPolicy_OpeningAndThreatBudgetsFollowEdpcgPhase()
    {
        const string mission = EdpcgFacilityAssaultPacingPolicy
            .FormalMissionId;
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveOpeningSeconds(
                mission,
                12f),
            Is.EqualTo(3f));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveOpeningEnemyCount(
                mission,
                2),
            Is.EqualTo(2));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveThreatBudget(
                mission,
                1,
                2),
            Is.EqualTo(4));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveThreatBudget(
                mission,
                2,
                2),
            Is.EqualTo(4));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveThreatBudget(
                mission,
                3,
                2),
            Is.EqualTo(4));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveThreatBudget(
                mission,
                4,
                4),
            Is.EqualTo(2));

        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveOpeningSeconds(
                "abandoned_mine",
                12f),
            Is.EqualTo(12f));
        Assert.That(
            EdpcgFacilityAssaultPacingPolicy.ResolveThreatBudget(
                "abandoned_mine",
                3,
                2),
            Is.EqualTo(2));
    }

    [Test]
    public void FacilityCore_RejectsAllDamageUntilArmorThresholdIsDestroyed()
    {
        GameObject facilityObject = new GameObject("FacilityShieldTest");
        FinitePlanetFacilityAssaultObjective facility =
            facilityObject.AddComponent<FinitePlanetFacilityAssaultObjective>();

        try
        {
            SetPrivateField(
                facility,
                "<DifficultySpec>k__BackingField",
                FacilityAssaultDifficultySpec.Resolve(5));
            SetPrivateField(facility, "built", true);

            FieldInfo modulesField = typeof(FinitePlanetFacilityAssaultObjective)
                .GetField(
                    "armorModules",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(modulesField, Is.Not.Null);
            var modules = (List<FinitePlanetFacilityArmorModule>)
                modulesField.GetValue(facility);

            IReadOnlyList<Vector3Int> shellPositions =
                FacilityAssaultShellLayout.CreatePositions(3, 0);
            for (int index = 0; index < shellPositions.Count; index++)
            {
                GameObject moduleObject = new GameObject("Armor_" + index);
                moduleObject.transform.SetParent(facilityObject.transform, false);
                moduleObject.transform.localPosition = shellPositions[index];
                GameObject visual = new GameObject("Visual");
                visual.transform.SetParent(moduleObject.transform, false);
                BoxCollider collider = moduleObject.AddComponent<BoxCollider>();
                FinitePlanetFacilityArmorModule module =
                    moduleObject.AddComponent<FinitePlanetFacilityArmorModule>();
                module.Configure(
                    10f,
                    0,
                    index,
                    FacilityAssaultShellLayout.ResolveLayer(index),
                    FacilityAssaultShellLayout.ResolveDirectionIndex(index),
                    shellPositions[index],
                    1f,
                    collider,
                    visual);
                modules.Add(module);
            }

            GameObject coreObject = new GameObject("Core");
            coreObject.transform.SetParent(facilityObject.transform, false);
            FinitePlanetEnergyCoreObjective core =
                coreObject.AddComponent<FinitePlanetEnergyCoreObjective>();
            core.Configure(350f, 0, 2f, false);
            SetPrivateField(facility, "core", core);

            SpaceDamageInfo projectile = new SpaceDamageInfo(
                50f,
                coreObject.transform.position,
                Vector3.forward,
                SpaceDamageType.Projectile,
                null);
            SpaceDamageInfo explosion = new SpaceDamageInfo(
                200f,
                coreObject.transform.position,
                Vector3.up,
                SpaceDamageType.Explosion,
                null);

            core.ApplyDamage(projectile);
            core.ApplyDamage(explosion);
            Assert.That(facility.CoreExposed, Is.False);
            Assert.That(core.Integrity, Is.EqualTo(350f));

            const int breachDirection = 0;
            int outerModule =
                2 * FacilityAssaultShellLayout.DirectionsPerLayer +
                breachDirection;
            int middleModule =
                FacilityAssaultShellLayout.DirectionsPerLayer +
                breachDirection;
            int innerModule = breachDirection;

            DestroyArmorModule(modules[outerModule]);
            // Destroying the same total number in unrelated directions must
            // not count as a straight route to the core.
            DestroyArmorModule(modules[outerModule + 1]);
            DestroyArmorModule(modules[outerModule + 2]);

            core.ApplyDamage(explosion);
            Assert.That(facility.CoreExposed, Is.False);
            Assert.That(core.Integrity, Is.EqualTo(350f));

            DestroyArmorModule(modules[middleModule]);
            Assert.That(facility.BestBreachDepth, Is.EqualTo(2));
            Assert.That(facility.CoreExposed, Is.False);
            core.ApplyDamage(explosion);
            Assert.That(core.Integrity, Is.EqualTo(350f));

            DestroyArmorModule(modules[innerModule]);

            Assert.That(facility.BestBreachDepth, Is.EqualTo(3));
            Assert.That(facility.CoreExposed, Is.True);
            core.ApplyDamage(projectile);
            Assert.That(core.Integrity, Is.EqualTo(300f));
            core.ApplyDamage(new SpaceDamageInfo(
                400f,
                coreObject.transform.position,
                Vector3.up,
                SpaceDamageType.Explosion,
                null));
            Assert.That(core.IsDestroyed, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(facilityObject);
        }
    }

    [Test]
    public void FacilityAutoAim_SelectsNearestLiveArmorThenExposedCore()
    {
        GameObject facilityObject = new GameObject("FacilityAutoAimTest");
        FinitePlanetFacilityAssaultObjective facility =
            facilityObject.AddComponent<FinitePlanetFacilityAssaultObjective>();
        try
        {
            SetPrivateField(
                facility,
                "<DifficultySpec>k__BackingField",
                FacilityAssaultDifficultySpec.Resolve(5));
            SetPrivateField(facility, "built", true);
            FieldInfo modulesField = typeof(FinitePlanetFacilityAssaultObjective)
                .GetField(
                    "armorModules",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(modulesField, Is.Not.Null);
            var modules = (List<FinitePlanetFacilityArmorModule>)
                modulesField.GetValue(facility);

            IReadOnlyList<Vector3Int> shellPositions =
                FacilityAssaultShellLayout.CreatePositions(3, 0);
            for (int index = 0; index < shellPositions.Count; index++)
            {
                GameObject moduleObject = new GameObject("Armor_" + index);
                moduleObject.transform.SetParent(facilityObject.transform, false);
                moduleObject.transform.localPosition = shellPositions[index];
                GameObject visual = new GameObject("Visual");
                visual.transform.SetParent(moduleObject.transform, false);
                BoxCollider collider = moduleObject.AddComponent<BoxCollider>();
                FinitePlanetFacilityArmorModule module =
                    moduleObject.AddComponent<FinitePlanetFacilityArmorModule>();
                module.Configure(
                    10f,
                    0,
                    index,
                    FacilityAssaultShellLayout.ResolveLayer(index),
                    FacilityAssaultShellLayout.ResolveDirectionIndex(index),
                    shellPositions[index],
                    1f,
                    collider,
                    visual);
                modules.Add(module);
            }

            GameObject coreObject = new GameObject("Core");
            coreObject.transform.SetParent(facilityObject.transform, false);
            FinitePlanetEnergyCoreObjective core =
                coreObject.AddComponent<FinitePlanetEnergyCoreObjective>();
            core.Configure(350f, 0, 2f, false);
            SetPrivateField(facility, "core", core);

            Assert.That(
                facility.TryResolveNearestLiveArmorTarget(
                    Vector3.left * 100f,
                    out Transform initialTarget),
                Is.True);
            int outerModule =
                2 * FacilityAssaultShellLayout.DirectionsPerLayer;
            int middleModule =
                FacilityAssaultShellLayout.DirectionsPerLayer;
            int innerModule = 0;
            Assert.That(initialTarget,
                Is.EqualTo(modules[outerModule].transform));

            DestroyArmorModule(modules[outerModule]);
            Assert.That(
                facility.TryResolveNearestLiveArmorTarget(
                    Vector3.left * 100f,
                    out Transform middleTarget),
                Is.True);
            Assert.That(middleTarget,
                Is.EqualTo(modules[middleModule].transform));

            DestroyArmorModule(modules[middleModule]);
            Assert.That(
                facility.TryResolveNearestLiveArmorTarget(
                    Vector3.left * 100f,
                    out Transform innerTarget),
                Is.True);
            Assert.That(innerTarget,
                Is.EqualTo(modules[innerModule].transform));

            DestroyArmorModule(modules[innerModule]);
            Assert.That(facility.CoreExposed, Is.True);
            Assert.That(
                facility.TryResolveAutoAimTarget(
                    Vector3.zero,
                    out Transform exposedTarget),
                Is.True);
            Assert.That(exposedTarget, Is.EqualTo(core.transform));
        }
        finally
        {
            Object.DestroyImmediate(facilityObject);
        }
    }

    [Test]
    public void FacilityPlacement_ResolvesThreeNonOverlappingRuntimeSitesAndRejectsBlocker()
    {
        GameObject runtimeObject = new GameObject("UrbanRuntimeTest");
        GameObject cityObject = new GameObject("InactiveCityGeometry");
        GameObject blockerObject = null;
        cityObject.SetActive(false);
        cityObject.transform.SetParent(runtimeObject.transform, false);
        FinitePlanetUrbanCombatRuntime runtime =
            runtimeObject.AddComponent<FinitePlanetUrbanCombatRuntime>();
        AirCombatCityPcgLab lab = cityObject.AddComponent<AirCombatCityPcgLab>();
        try
        {
            var settings = new AirCombatCitySettings
            {
                mapSize = 1000f,
                wingspan = 18f
            };
            var plan = new AirCombatCityPlan
            {
                mission = AirCombatCityMission.FacilityAssault,
                objective = Vector3.zero
            };
            plan.facilityCores.Add(new Vector3(-180f, 0f, -120f));
            plan.facilityCores.Add(new Vector3(180f, 0f, -120f));
            plan.facilityCores.Add(new Vector3(0f, 0f, 190f));
            var snapshot = new AirCombatCityRuntimeGeometrySnapshot
            {
                instantiatedBuildingCount = 0,
                buildings = System.Array.Empty<
                    AirCombatRuntimeBuildingGeometry>(),
                skybridges = System.Array.Empty<
                    AirCombatRuntimeConnectionGeometry>(),
                aerialCables = System.Array.Empty<
                    AirCombatRuntimeConnectionGeometry>(),
                routes = System.Array.Empty<AirCombatRuntimeRouteGeometry>()
            };
            SetPrivateField(lab, "settings", settings);
            SetPrivateField(lab, "plan", plan);
            SetPrivateField(lab, "runtimeGeometrySnapshot", snapshot);
            SetPrivateField(runtime, "cityRoot", cityObject);
            SetPrivateField(runtime, "cityLab", lab);
            SetPrivateField(runtime, "<IsReady>k__BackingField", true);
            SetPrivateField(runtime, "<GroundHeight>k__BackingField", 0f);

            FacilityAssaultDifficultySpec spec =
                FacilityAssaultDifficultySpec.Resolve(5);
            Vector3 size = spec.RequiredHalfExtents * 2f;
            var occupied = new List<Bounds>();
            for (int index = 0; index < plan.facilityCores.Count; index++)
            {
                Assert.That(
                    runtime.TryResolveAssaultFacilitySite(
                        plan.facilityCores[index],
                        size,
                        occupied,
                        out Vector3 ground,
                        out Quaternion rotation,
                        out string error),
                    Is.True,
                    error);
                Assert.That(ground.y, Is.Zero.Within(0.001f));
                Assert.That(float.IsNaN(rotation.x), Is.False);
                Bounds placed = new Bounds(
                    ground + Vector3.up * (size.y * 0.5f),
                    size);
                for (int other = 0; other < occupied.Count; other++)
                    Assert.That(placed.Intersects(occupied[other]), Is.False);
                occupied.Add(placed);
            }

            blockerObject = new GameObject("FacilitySiteBlocker");
            blockerObject.transform.position = plan.facilityCores[0] +
                                               Vector3.up *
                                               (size.y * 0.5f);
            BoxCollider blocker = blockerObject.AddComponent<BoxCollider>();
            blocker.size = new Vector3(
                AirCombatCityGenerator.FacilityPadSize,
                size.y,
                AirCombatCityGenerator.FacilityPadSize);
            Physics.SyncTransforms();
            Assert.That(
                runtime.TryResolveAssaultFacilitySite(
                    plan.facilityCores[0],
                    size,
                    null,
                    out _,
                    out _,
                    out _),
                Is.False,
                "最终物理占位必须拒绝覆盖整个设施预留地的阻挡体。");
        }
        finally
        {
            if (blockerObject != null)
                Object.DestroyImmediate(blockerObject);
            Object.DestroyImmediate(runtimeObject);
        }
    }

    [Test]
    public void AssaultCompletion_UsesEdpcgQuotaAndResolvedEmptyRosterCannotSoftLock()
    {
        const int edpcgRequiredCreditedKills = 7;
        const int missionFallbackRequiredKills = 2;

        Assert.That(
            FinitePlanetAssaultCompletionPolicy.ResolveRequiredCreditedKills(
                true,
                edpcgRequiredCreditedKills,
                missionFallbackRequiredKills),
            Is.EqualTo(edpcgRequiredCreditedKills));
        Assert.That(
            FinitePlanetAssaultCompletionPolicy.IsKillQuotaMet(
                6,
                true,
                edpcgRequiredCreditedKills,
                missionFallbackRequiredKills,
                false,
                0),
            Is.False);
        Assert.That(
            FinitePlanetAssaultCompletionPolicy.IsKillQuotaMet(
                edpcgRequiredCreditedKills,
                true,
                edpcgRequiredCreditedKills,
                missionFallbackRequiredKills,
                false,
                3),
            Is.True);

        // A self-detonating final roster member may resolve without crediting
        // a kill. Resolved + empty is terminal, but neither condition alone is.
        Assert.That(
            FinitePlanetAssaultCompletionPolicy.IsKillQuotaMet(
                3,
                true,
                edpcgRequiredCreditedKills,
                missionFallbackRequiredKills,
                true,
                0),
            Is.True);
        Assert.That(
            FinitePlanetAssaultCompletionPolicy.IsKillQuotaMet(
                3,
                true,
                edpcgRequiredCreditedKills,
                missionFallbackRequiredKills,
                true,
                1),
            Is.False);
        Assert.That(
            FinitePlanetAssaultCompletionPolicy.IsKillQuotaMet(
                3,
                true,
                edpcgRequiredCreditedKills,
                missionFallbackRequiredKills,
                false,
                0),
            Is.False);
    }

    static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }

    static void DestroyArmorModule(FinitePlanetFacilityArmorModule module)
    {
        Assert.That(module, Is.Not.Null);
        module.ApplyDamage(new SpaceDamageInfo(
            module.MaximumIntegrity + 1f,
            module.transform.position,
            Vector3.right,
            SpaceDamageType.Projectile,
            null));
        Assert.That(module.IsDestroyed, Is.True);
    }

    static int CountSixConnected(
        HashSet<Vector3Int> positions,
        Vector3Int start)
    {
        Vector3Int[] directions =
        {
            Vector3Int.left,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.up,
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 1)
        };
        var visited = new HashSet<Vector3Int> { start };
        var queue = new Queue<Vector3Int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector3Int current = queue.Dequeue();
            for (int index = 0; index < directions.Length; index++)
            {
                Vector3Int next = current + directions[index];
                if (positions.Contains(next) && visited.Add(next))
                    queue.Enqueue(next);
            }
        }
        return visited.Count;
    }

    static int ManhattanDistance(Vector3Int first, Vector3Int second)
    {
        Vector3Int delta = first - second;
        return Mathf.Abs(delta.x) + Mathf.Abs(delta.y) +
               Mathf.Abs(delta.z);
    }

    static void AssertProjectionIsFiniteAndInside(
        FinitePlanetAssaultObjectiveHud.FinitePlanetAssaultHudProjection value,
        Rect safeViewport)
    {
        Assert.That(float.IsNaN(value.viewportAnchor.x), Is.False);
        Assert.That(float.IsInfinity(value.viewportAnchor.x), Is.False);
        Assert.That(float.IsNaN(value.viewportAnchor.y), Is.False);
        Assert.That(float.IsInfinity(value.viewportAnchor.y), Is.False);
        Assert.That(float.IsNaN(value.outwardDirection.x), Is.False);
        Assert.That(float.IsInfinity(value.outwardDirection.x), Is.False);
        Assert.That(float.IsNaN(value.outwardDirection.y), Is.False);
        Assert.That(float.IsInfinity(value.outwardDirection.y), Is.False);
        Assert.That(value.viewportAnchor.x,
            Is.InRange(safeViewport.xMin, safeViewport.xMax));
        Assert.That(value.viewportAnchor.y,
            Is.InRange(safeViewport.yMin, safeViewport.yMax));
    }
}
#endif
