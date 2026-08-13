using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModularAssembly;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.ModularAssembly;
using UnityPlanet.SpaceStation.Enhancement;
using UnityPlanet.SpaceStation.Skills;
using Object = UnityEngine.Object;

public sealed class CombatTestModeTests
{
    [TestCase(4)]
    [TestCase(8)]
    [TestCase(16)]
    [TestCase(28)]
    public void HordeSpawnCompositionKeepsRangedCraftAsTheMajority(
        int activeCap)
    {
        int suicideMaximum =
            HordeSpawnCompositionPolicy.MaximumInterceptors(activeCap);
        int rangedTarget = HordeSpawnCompositionPolicy.TargetStrikers(activeCap) +
                           HordeSpawnCompositionPolicy.TargetGunships(activeCap);
        Assert.That(suicideMaximum, Is.LessThan(activeCap * 0.5f));
        Assert.That(rangedTarget, Is.GreaterThan(suicideMaximum));
    }

    [Test]
    public void PathStarvedEnemyKeepsOnlyPresencePressure()
    {
        float actionable = HordeCombatPressurePolicy.EffectiveThreatCost(3, true);
        float pathStarved = HordeCombatPressurePolicy.EffectiveThreatCost(3, false);
        Assert.That(actionable, Is.EqualTo(3f));
        Assert.That(pathStarved, Is.EqualTo(0.6f).Within(0.0001f));
        Assert.That(pathStarved, Is.LessThan(actionable));
    }

    [Test]
    public void HordeReengagementStartsAfterLostContactOrHardDistance()
    {
        Assert.That(
            HordeReengagementPolicy.ShouldBegin(
                8f,
                HordeReengagementPolicy.LostContactSeconds,
                320f,
                false,
                false),
            Is.True);
        Assert.That(
            HordeReengagementPolicy.ShouldBegin(
                4f,
                1f,
                HordeReengagementPolicy.HardRecallDistance,
                false,
                false),
            Is.True);
    }

    [Test]
    public void HordeReengagementDoesNotInterruptOpeningOrAttackRecovery()
    {
        Assert.That(
            HordeReengagementPolicy.ShouldBegin(
                1f,
                20f,
                700f,
                false,
                false),
            Is.False);
        Assert.That(
            HordeReengagementPolicy.ShouldBegin(
                20f,
                20f,
                700f,
                true,
                false),
            Is.False);
        Assert.That(
            HordeReengagementPolicy.ShouldBegin(
                20f,
                20f,
                700f,
                false,
                true),
            Is.False);
    }

    [Test]
    public void HordeReengagementDestinationUsesAlternatingSideStandoff()
    {
        HordeEnemyProfile ranged = HordeEnemyProfile.ForRole(
            HordeEnemyRole.Striker);
        Vector3 player = new Vector3(100f, 20f, -40f);
        Vector3 evenDestination = HordeReengagementPolicy.ResolveDestination(
            ranged,
            player + Vector3.right * 400f,
            player,
            Vector3.zero,
            0);
        Vector3 oddDestination = HordeReengagementPolicy.ResolveDestination(
            ranged,
            player + Vector3.right * 400f,
            player,
            Vector3.zero,
            1);

        Vector3 evenHorizontal = Vector3.ProjectOnPlane(
            evenDestination - player,
            Vector3.up);
        Vector3 oddHorizontal = Vector3.ProjectOnPlane(
            oddDestination - player,
            Vector3.up);
        Assert.That(evenHorizontal.magnitude,
            Is.EqualTo(110f).Within(0.01f));
        Assert.That(oddHorizontal.magnitude,
            Is.EqualTo(110f).Within(0.01f));
        Assert.That(evenDestination.y, Is.EqualTo(36f).Within(0.01f));
        Assert.That(oddDestination.y, Is.EqualTo(36f).Within(0.01f));
        Assert.That(evenDestination.z, Is.LessThan(player.z));
        Assert.That(oddDestination.z, Is.GreaterThan(player.z));
        Assert.That(
            Mathf.Abs(Vector3.Dot(
                evenHorizontal.normalized,
                Vector3.right)),
            Is.LessThan(0.4f),
            "Re-engagement must flank instead of repeating the blocked " +
            "frontal line.");
        Assert.That(
            HordeReengagementPolicy.HasReestablishedContact(
                HordeEnemyAttackKind.Ranged,
                380f,
                true,
                false),
            Is.True);
        Assert.That(
            HordeReengagementPolicy.HasReestablishedContact(
                HordeEnemyAttackKind.Ranged,
                380f,
                false,
                false),
            Is.False);
    }

    [Test]
    public void HordeUrbanPolicyReservesDestructionForBossAndPlayerSkills()
    {
        Assert.That(
            HordeUrbanCombatPolicy.CanDamageUrbanStructures,
            Is.False);
    }

    [Test]
    public void EnemyWallImpactPolicyRequiresHordeOwnerAndUrbanCover()
    {
        GameObject enemy = CreateRoot("WallImpactHordeOwner", Vector3.zero);
        enemy.AddComponent<HordeEnemyVehicle>();
        GameObject player = CreateRoot("WallImpactPlayerOwner", Vector3.zero);
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(wall);
        wall.name = "WallImpactUrbanCover";
        wall.AddComponent<UrbanDestructibleBuilding>();
        Collider wallCollider = wall.GetComponent<Collider>();
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(ground);

        Assert.That(
            EnemyUrbanImpactPolicy.ShouldUseAnimatedWallImpact(
                enemy.transform,
                wallCollider),
            Is.True);
        Assert.That(
            EnemyUrbanImpactPolicy.ShouldUseAnimatedWallImpact(
                player.transform,
                wallCollider),
            Is.False);
        Assert.That(
            EnemyUrbanImpactPolicy.ShouldUseAnimatedWallImpact(
                enemy.transform,
                ground.GetComponent<Collider>()),
            Is.False);
    }

    public sealed class TestDamageable : MonoBehaviour, ISpaceDamageable
    {
        public float DamageReceived { get; private set; }
        public float Integrity => Mathf.Max(0f, 100f - DamageReceived);
        public float MaximumIntegrity => 100f;
        public bool IsDestroyed => Integrity <= 0f;

        public void ApplyDamage(SpaceDamageInfo damage)
        {
            DamageReceived += damage.amount;
        }
    }

    sealed class ZeroPlanetEnvironmentProvider : IPlanetEnvironmentProvider
    {
        public bool ForceNoWind { get; set; }

        public PlanetEnvironmentSample Sample(
            Vector3 worldPosition,
            double simulationTime)
        {
            return new PlanetEnvironmentSample
            {
                gravityAcceleration = Vector3.zero,
                altitude = 500f,
                airDensity = 0f,
                ambientPressure = 0f,
                surfaceDistance = 500f,
                hasSurface = false,
                hasAtmosphere = false
            };
        }
    }

    readonly List<GameObject> roots = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        CombatTransientRoot.ClearVisuals();
        for (int index = roots.Count - 1; index >= 0; index--)
            if (roots[index] != null)
                Object.DestroyImmediate(roots[index]);
        roots.Clear();
    }

    [Test]
    public void WeaponCoordinatorTabAutoAimStateCanBeToggledWithoutFlight()
    {
        GameObject root = CreateRoot("AutoAimCoordinator", Vector3.zero);
        WeaponSystemCoordinator coordinator =
            root.AddComponent<WeaponSystemCoordinator>();

        Assert.That(coordinator.AutoAimEnabled, Is.False);
        coordinator.SetAutoAimEnabled(true);
        Assert.That(coordinator.AutoAimEnabled, Is.True);
        Assert.That(coordinator.LockedTarget, Is.Null);
        Assert.That(coordinator.AimCandidate, Is.Null);

        coordinator.SetAutoAimEnabled(false);
        Assert.That(coordinator.AutoAimEnabled, Is.False);
        Assert.That(coordinator.LockProgress, Is.Zero);
    }

    [Test]
    public void BlueCardCatalogOnlyContainsImplementedCombatStats()
    {
        EnhancementStat[] stats = EnhancementTypeCatalog.All
            .Select(item => item.Stat)
            .ToArray();

        Assert.That(stats.Length, Is.EqualTo(3));
        CollectionAssert.AreEquivalent(
            new[]
            {
                EnhancementStat.ModuleIntegrity,
                EnhancementStat.WeaponDamage,
                EnhancementStat.FireRate
            },
            stats);
    }

    [Test]
    public void BlueCardModifiersApplyToWeaponAndEveryModuleCategory()
    {
        var progress = new GalaxyEnhancementProgressData
        {
            acquiredEnhancements = new[]
            {
                new AcquiredEnhancementData
                {
                    definitionId = "reinforced_hull_lattice",
                    stacks = 2,
                    totalMagnitude = 25f
                },
                new AcquiredEnhancementData
                {
                    definitionId = "structure_nanobond",
                    stacks = 1,
                    totalMagnitude = 5f
                },
                new AcquiredEnhancementData
                {
                    definitionId = "weapon_capacitor_overdrive",
                    stacks = 2,
                    totalMagnitude = 18f
                },
                new AcquiredEnhancementData
                {
                    definitionId = "adaptive_feed_cycle",
                    stacks = 2,
                    totalMagnitude = 12f
                }
            }
        };
        EnhancementRuntimeModifiers modifiers =
            EnhancementRuntimeModifiers.FromProgress(progress);
        var profile = new WeaponProfile
        {
            damage = 100f,
            shotsPerSecond = 5f
        };

        modifiers.ApplyToWeapon(profile);

        Assert.That(modifiers.ModuleIntegrityMultiplier,
            Is.EqualTo(1.3f).Within(0.0001f));
        Assert.That(profile.damage,
            Is.EqualTo(118f).Within(0.0001f));
        Assert.That(profile.shotsPerSecond,
            Is.EqualTo(5.6f).Within(0.0001f));

        GridModuleDefinition[] definitions =
        {
            ScriptableObject.CreateInstance<GridModuleDefinition>(),
            ScriptableObject.CreateInstance<GridModuleDefinition>(),
            ScriptableObject.CreateInstance<GridModuleDefinition>()
        };
        GameObject prefab = CreateRoot(
            "BlueCardIntegrityPrefab",
            Vector3.zero);
        prefab.AddComponent<BoxCollider>();
        definitions[0].Configure(
            "core",
            "core",
            GridModuleCategory.Core,
            prefab,
            new Vector3Int(2, 2, 2),
            100f,
            100f,
            0f,
            100f,
            0f,
            null);
        definitions[1].Configure(
            "blue-card-structure",
            "structure",
            GridModuleCategory.Structure,
            prefab,
            Vector3Int.one,
            10f,
            0f,
            0f,
            80f,
            0f,
            null);
        definitions[2].Configure(
            "blue-card-weapon",
            "weapon",
            GridModuleCategory.KineticWeapon,
            prefab,
            Vector3Int.one,
            10f,
            0f,
            0f,
            60f,
            0f,
            null);
        try
        {
            var model = new GridAssemblyModel(definitions);
            Assert.That(model.TryPlace(
                    definitions[1].ModuleId,
                    new GridModulePose(new Vector3Int(1, 0, 0), 0),
                    false,
                    out string structureId,
                    out string structureError),
                Is.True,
                structureError);
            Assert.That(model.TryPlace(
                    definitions[2].ModuleId,
                    new GridModulePose(new Vector3Int(-2, 0, 0), 0),
                    false,
                    out string weaponId,
                    out string weaponError),
                Is.True,
                weaponError);
            GameObject ship = CreateRoot(
                "BlueCardIntegrityShip",
                Vector3.zero);
            Rigidbody body = ship.AddComponent<Rigidbody>();
            body.isKinematic = true;
            Transform parts = CreateRoot(
                "BlueCardIntegrityParts",
                Vector3.zero).transform;
            parts.SetParent(ship.transform, false);
            Transform coreVisual = CreateRoot(
                "BlueCardIntegrityCore",
                Vector3.zero).transform;
            coreVisual.SetParent(ship.transform, false);
            var assembly = ship.AddComponent<
                SpacecraftEditor.ShipAssembly>();
            assembly.Configure(body, parts, null, 1f);
            GridAssemblyPresenter presenter =
                ship.AddComponent<GridAssemblyPresenter>();
            presenter.Initialize(
                model,
                assembly,
                coreVisual);
            ModularBossGridFlightSession flight =
                ship.AddComponent<ModularBossGridFlightSession>();
            VehicleStructureGraph graph =
                ship.AddComponent<VehicleStructureGraph>();
            graph.Initialize(model, presenter, flight);

            modifiers.ApplyToStructureGraph(graph);

            GridModuleRecord coreRecord = model.Records.Single(item =>
                item.Definition.Category == GridModuleCategory.Core);
            Assert.That(
                graph.MaximumIntegrity(coreRecord.RuntimeId),
                Is.EqualTo(130f).Within(0.0001f));
            Assert.That(
                graph.MaximumIntegrity(structureId),
                Is.EqualTo(104f).Within(0.0001f));
            Assert.That(
                graph.MaximumIntegrity(weaponId),
                Is.EqualTo(78f).Within(0.0001f));

            graph.ConfigureIntegrityMultipliers(
                1.3f,
                1.3f,
                1.3f,
                10f);
            Assert.That(
                graph.MaximumIntegrity(coreRecord.RuntimeId),
                Is.EqualTo(130f).Within(0.0001f));
            Assert.That(
                graph.MaximumIntegrity(structureId),
                Is.EqualTo(104f).Within(0.0001f));
            Assert.That(
                graph.MaximumIntegrity(weaponId),
                Is.EqualTo(600f).Within(0.0001f),
                "An explicit weapon multiplier must not raise other categories.");
        }
        finally
        {
            foreach (GridModuleDefinition definition in definitions)
            {
                Object.DestroyImmediate(definition);
            }
        }
    }

    [Test]
    public void BossMissionDisablesAutoAimUntilMissionRestrictionIsRemoved()
    {
        GameObject root = CreateRoot("BossAutoAimCoordinator", Vector3.zero);
        WeaponSystemCoordinator coordinator =
            root.AddComponent<WeaponSystemCoordinator>();

        coordinator.SetAutoAimEnabled(true);
        coordinator.SetAutoAimAllowed(false);
        coordinator.SetManualAimRequiredForFire(true);
        Assert.That(coordinator.AutoAimAllowed, Is.False);
        Assert.That(coordinator.AutoAimEnabled, Is.False);
        Assert.That(coordinator.ManualAimRequiredForFire, Is.True);

        coordinator.SetAutoAimEnabled(true);
        Assert.That(coordinator.AutoAimEnabled, Is.False);

        coordinator.SetAutoAimAllowed(true);
        coordinator.SetManualAimRequiredForFire(false);
        coordinator.SetAutoAimEnabled(true);
        Assert.That(coordinator.AutoAimEnabled, Is.True);
        Assert.That(coordinator.ManualAimRequiredForFire, Is.False);
    }

    [Test]
    public void ModularBossPcgProvidesRedundantThrustOnAllSixAxes()
    {
        GridModuleDefinition[] definitions = CreateBossDefinitions();
        try
        {
            Assert.That(ModularBossPcgGenerator.TryBuild(
                    definitions,
                    0,
                    7319,
                    out ModularBossBuildResult low,
                    out string lowError),
                Is.True,
                lowError);
            Assert.That(ModularBossPcgGenerator.TryBuild(
                    definitions,
                    5,
                    7319,
                    out ModularBossBuildResult high,
                    out string highError),
                Is.True,
                highError);

            foreach (ModularBossThrusterDirection direction in
                     Enum.GetValues(typeof(ModularBossThrusterDirection)))
            {
                Assert.That(low.CountThrusters(direction),
                    Is.GreaterThanOrEqualTo(2),
                    direction.ToString());
                Assert.That(low.CountThrusters(direction) % 2,
                    Is.Zero,
                    direction + " must be generated as opposite pairs.");
                Assert.That(high.CountThrusters(direction),
                    Is.GreaterThan(low.CountThrusters(direction)),
                    direction.ToString());
                Assert.That(high.CountThrusters(direction) % 2,
                    Is.Zero,
                    direction + " must be generated as opposite pairs.");
            }
            AssertBalancedBossThrusterPairs(low);
            AssertBalancedBossThrusterPairs(high);
            AssertBossThrusterTorqueLeverCoverage(high);
            Assert.That(low.Model.CalculateMetrics().localCenterOfMass.magnitude,
                Is.LessThan(0.0001f));
            Assert.That(high.Model.CalculateMetrics().localCenterOfMass.magnitude,
                Is.LessThan(0.0001f));
            Assert.That(low.Model.Validate().IsValid, Is.True);
            Assert.That(high.Model.Validate().IsValid, Is.True);
            Assert.That(high.Profile.CoreCoverDepth,
                Is.GreaterThan(low.Profile.CoreCoverDepth));
            Assert.That(high.StructureModuleCount,
                Is.GreaterThan(low.StructureModuleCount));
            Assert.That(high.Profile.StructureIntegrityMultiplier,
                Is.GreaterThan(low.Profile.StructureIntegrityMultiplier));
            Assert.That(high.WeaponCount, Is.GreaterThan(low.WeaponCount));

            GridModuleRecord sample = low.Model.Records.First(record =>
                record.Definition.Category == GridModuleCategory.Structure);
            GameObject scaledModule = CreateRoot(
                "FiveTimesBossModule",
                Vector3.zero);
            GridModuleView view = scaledModule.AddComponent<GridModuleView>();
            view.Initialize(sample);
            ModularBossModuleScalePolicy.Apply(view);
            Assert.That(ModularBossModuleScalePolicy.LinearScale,
                Is.EqualTo(5f));
            Assert.That(view.transform.localScale,
                Is.EqualTo(Vector3.one * 5f));
            Assert.That(view.transform.localPosition,
                Is.EqualTo(GridAssemblyModel.ModuleCenter(sample) * 5f));

            GameObject oversizedFallback = CreateRoot(
                "OversizedFallbackCollider",
                Vector3.zero);
            oversizedFallback.transform.SetParent(view.transform, false);
            oversizedFallback.AddComponent<BoxCollider>().size =
                Vector3.one * 1.6f;
            ModularBossModuleColliderPolicy.Apply(view);
            Collider[] liveColliders = view
                .GetComponentsInChildren<Collider>(true)
                .Where(collider => collider.enabled && !collider.isTrigger)
                .ToArray();
            Assert.That(liveColliders, Has.Length.EqualTo(1));
            Assert.That(liveColliders[0].transform, Is.EqualTo(view.transform));
            Assert.That(
                ((BoxCollider)liveColliders[0]).center,
                Is.EqualTo(Vector3.zero));
            Assert.That(
                ((BoxCollider)liveColliders[0]).size,
                Is.EqualTo((Vector3)sample.Definition.Footprint));
        }
        finally
        {
            foreach (GridModuleDefinition definition in definitions)
                Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void NeoXVisualNormalizationIsIndependentOfVehicleWorldRotation()
    {
        GameObject parent = CreateRoot(
            "RotatedNeoXNormalizationParent",
            new Vector3(13f, -7f, 29f));
        parent.transform.rotation = Quaternion.Euler(19f, 37f, 11f);
        GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(instance);
        instance.name = "NeoXNormalizationCube";
        instance.transform.SetParent(parent.transform, false);

        var record = new ModularContentRecord
        {
            sourceId = "test:rotation-independent-cube",
            neoXId = "rotation_independent_cube",
            footprint = new[] { 1, 1, 1 },
            visualEuler = new[] { 0f, 0f, 0f },
            visualOffset = new[] { 0f, 0f, 0f },
            visualScale = 1f,
            mountMode = "Center"
        };
        MethodInfo normalize = typeof(ModularContentService).GetMethod(
            "NormalizeVisual",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(normalize, Is.Not.Null);
        normalize.Invoke(null, new object[] { instance, record });

        Renderer renderer = instance.GetComponent<Renderer>();
        Bounds localBounds = renderer.localBounds;
        Matrix4x4 rendererToParent =
            parent.transform.worldToLocalMatrix *
            renderer.transform.localToWorldMatrix;
        Bounds measured = default;
        bool initialized = false;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = rendererToParent.MultiplyPoint3x4(new Vector3(
                (corner & 1) == 0
                    ? localBounds.min.x
                    : localBounds.max.x,
                (corner & 2) == 0
                    ? localBounds.min.y
                    : localBounds.max.y,
                (corner & 4) == 0
                    ? localBounds.min.z
                    : localBounds.max.z));
            if (!initialized)
            {
                measured = new Bounds(point, Vector3.zero);
                initialized = true;
            }
            else
                measured.Encapsulate(point);
        }

        Assert.That(instance.transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(instance.transform.localScale.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(instance.transform.localScale.z, Is.EqualTo(1f).Within(0.001f));
        Assert.That(measured.size.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(measured.size.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(measured.size.z, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void ModularBossPcgBuildsTieredNonCubicArchetypesAcrossSeeds()
    {
        GridModuleDefinition[] definitions = CreateBossDefinitions();
        try
        {
            int[] seeds = { 11, 7319, 98731 };
            ModularBossBuildResult spearhead = null;
            ModularBossBuildResult hammerhead = null;
            ModularBossBuildResult citadel = null;
            for (int tier = 0; tier < 6; tier++)
            foreach (int seed in seeds)
            {
                Assert.That(ModularBossPcgGenerator.TryBuild(
                        definitions,
                        tier,
                        seed,
                        out ModularBossBuildResult build,
                        out string error),
                    Is.True,
                    $"tier={tier}, seed={seed}: {error}");
                Assert.That(build.Model.Validate().IsValid, Is.True);
                AssertCentrallySymmetricBossHull(build);
                AssertNonCubicBossHull(build);
                if (seed != 7319)
                    continue;
                if (tier == 0)
                    spearhead = build;
                else if (tier == 2)
                    hammerhead = build;
                else if (tier == 5)
                    citadel = build;
            }

            Assert.That(spearhead, Is.Not.Null);
            Assert.That(hammerhead, Is.Not.Null);
            Assert.That(citadel, Is.Not.Null);
            Assert.That(spearhead.HullArchetype,
                Is.EqualTo(ModularBossHullArchetype.Spearhead));
            Assert.That(hammerhead.HullArchetype,
                Is.EqualTo(ModularBossHullArchetype.Hammerhead));
            Assert.That(citadel.HullArchetype,
                Is.EqualTo(ModularBossHullArchetype.Citadel));

            Vector3Int spearSpan = ResolveBossHullSpan(spearhead);
            Vector3Int hammerSpan = ResolveBossHullSpan(hammerhead);
            Vector3Int citadelSpan = ResolveBossHullSpan(citadel);
            Assert.That(spearSpan.z, Is.GreaterThan(spearSpan.x));
            Assert.That(hammerSpan.x, Is.GreaterThan(hammerSpan.y));
            Assert.That(citadelSpan.y, Is.GreaterThan(citadelSpan.z));

            Assert.That(ModularBossPcgGenerator.TryBuild(
                    definitions,
                    0,
                    7320,
                    out ModularBossBuildResult alternate,
                    out string alternateError),
                Is.True,
                alternateError);
            CollectionAssert.AreNotEquivalent(
                ResolveBossStructureCells(spearhead).ToArray(),
                ResolveBossStructureCells(alternate).ToArray(),
                "The seed must change the connected hull, not only equipment mounts.");
        }
        finally
        {
            foreach (GridModuleDefinition definition in definitions)
                Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void ModularBossFunctionalHarnessIsCollisionFreeAndPassesEveryTier()
    {
        GameObject root = CreateRoot("BossFunctionalHarness", Vector3.zero);
        ModularBossFunctionalTestHarness harness =
            root.AddComponent<ModularBossFunctionalTestHarness>();
        for (int tier = 0; tier < 6; tier++)
        {
            harness.ConfigurePreview(tier, 7319 + tier * 101, true);
            Assert.That(harness.AllChecksPassed, Is.True,
                $"tier={tier}: {harness.LastReport}");
            Assert.That(harness.CurrentBuild, Is.Not.Null);
            Assert.That(harness.PreviewBounds.size.sqrMagnitude,
                Is.GreaterThan(1f));
            Assert.That(root.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The scene viewer must never join city/trap physics.");
            Assert.That(root.GetComponentsInChildren<LineRenderer>(true).Length,
                Is.EqualTo(3),
                "The dynamically-sized shield is represented by three axes.");
        }
    }

    [Test]
    public void ModularBossThrustCalibrationGuaranteesLiftAndPreservesDamageLoss()
    {
        const float mass = 7800f;
        const float gravity = 9.81f;
        var baseActuators = new List<VehicleActuatorDiagnostic>();
        Vector3[] directions =
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back
        };
        foreach (Vector3 direction in directions)
        {
            for (int index = 0; index < 2; index++)
            {
                baseActuators.Add(new VehicleActuatorDiagnostic
                {
                    runtimeId = direction + "_" + index,
                    localForceDirection = direction,
                    configuredMaximumForce = 3000f,
                    effectiveMaximumForce = 2820f
                });
            }
        }

        float multiplier =
            ModularBossFlightAuthorityPolicy.ResolveMultiplier(
                mass,
                gravity,
                baseActuators);
        Assert.That(multiplier, Is.GreaterThan(1f));
        VehicleActuatorDiagnostic[] calibrated = baseActuators
            .Select(value =>
            {
                value.configuredMaximumForce *= multiplier;
                value.effectiveMaximumForce *= multiplier;
                return value;
            })
            .ToArray();
        Assert.That(
            ModularBossFlightAuthorityPolicy.HasMinimumAuthority(
                mass,
                gravity,
                calibrated),
            Is.True);

        VehicleActuatorDiagnostic[] damaged = calibrated
            .Where((value, index) =>
                value.localForceDirection != Vector3.up || index % 2 == 0)
            .ToArray();
        Assert.That(
            ModularBossFlightAuthorityPolicy.HasMinimumAuthority(
                mass,
                gravity,
                damaged),
            Is.False,
            "Losing an upward thruster must still reduce real lift authority.");
    }

    [Test]
    public void ModularBossActualScaledAssemblyHasCalibratedSixAxisAuthority()
    {
        GridModuleDefinition[] definitions = CreateBossDefinitions();
        GameObject corePrefab = CreateRoot("BossAuthorityCore", Vector3.zero);
        GameObject hullPrefab = CreateRoot("BossAuthorityHull", Vector3.zero);
        GameObject thrusterPrefab = CreateRoot(
            "BossAuthorityThruster",
            Vector3.zero);
        GameObject weaponPrefab = CreateRoot(
            "BossAuthorityWeapon",
            Vector3.zero);
        corePrefab.AddComponent<BoxCollider>();
        hullPrefab.AddComponent<BoxCollider>();
        thrusterPrefab.AddComponent<BoxCollider>();
        weaponPrefab.AddComponent<BoxCollider>();
        NeoXBehaviorModule behavior =
            thrusterPrefab.AddComponent<NeoXBehaviorModule>();
        behavior.Configure(new ModularContentRecord
        {
            sourceId = "block:speed:speed_rocketsmall_112",
            neoXId = "speed_rocketsmall_112",
            behavior = GridModuleBehaviorKind.Thruster.ToString(),
            exhaustAxisLocal = new[] { 0f, 0f, 1f }
        });
        try
        {
            foreach (GridModuleDefinition definition in definitions)
            {
                GameObject prefab;
                switch (definition.Category)
                {
                    case GridModuleCategory.Core:
                        prefab = corePrefab;
                        break;
                    case GridModuleCategory.MainThruster:
                        prefab = thrusterPrefab;
                        break;
                    case GridModuleCategory.KineticWeapon:
                        prefab = weaponPrefab;
                        break;
                    default:
                        prefab = hullPrefab;
                        break;
                }
                definition.Configure(
                    definition.ModuleId,
                    definition.DisplayName,
                    definition.Category,
                    prefab,
                    definition.Footprint,
                    definition.MassKg,
                    definition.EnergyCapacity,
                    definition.EnergyCost,
                    definition.MaxIntegrity,
                    definition.ThrustNewtons,
                    null);
            }
            Assert.That(ModularBossPcgGenerator.TryBuild(
                    definitions,
                    0,
                    87123,
                    out ModularBossBuildResult build,
                    out string error),
                Is.True,
                error);

            GameObject ship = CreateRoot("BossAuthorityShip", Vector3.zero);
            Rigidbody body = ship.AddComponent<Rigidbody>();
            body.isKinematic = true;
            Transform parts = CreateRoot(
                "BossAuthorityParts",
                Vector3.zero).transform;
            parts.SetParent(ship.transform, false);
            Transform core = CreateRoot(
                "BossAuthorityCoreVisual",
                Vector3.zero).transform;
            core.SetParent(ship.transform, false);
            var assembly = ship.AddComponent<SpacecraftEditor.ShipAssembly>();
            assembly.Configure(body, parts, null, 650f);
            GridAssemblyPresenter presenter =
                ship.AddComponent<GridAssemblyPresenter>();
            presenter.Initialize(build.Model, assembly, core);
            ModularBossModuleScalePolicy.Apply(presenter);

            RobocraftMotionCoordinator motion =
                ship.AddComponent<RobocraftMotionCoordinator>();
            motion.ConfigureExplicit(body, assembly, build.Model, presenter);
            Vector3 unscaledInertia = body.inertiaTensor;
            motion.SetMassGeometryScale(
                ModularBossModuleScalePolicy.LinearScale);
            Vector3 scaledInertia = body.inertiaTensor;
            Assert.That(motion.MassGeometryScale, Is.EqualTo(5f));
            Assert.That(scaledInertia.x / unscaledInertia.x,
                Is.EqualTo(25f).Within(0.02f));
            Assert.That(scaledInertia.y / unscaledInertia.y,
                Is.EqualTo(25f).Within(0.02f));
            Assert.That(scaledInertia.z / unscaledInertia.z,
                Is.EqualTo(25f).Within(0.02f));
            motion.SetPlanetEnvironment(Vector3.down * 9.81f, 1.225f, 80f);
            VehicleActuatorDiagnostic[] before =
                motion.CaptureActuatorDiagnostics();
            Assert.That(before.Length, Is.EqualTo(12));
            float multiplier =
                ModularBossFlightAuthorityPolicy.ResolveMultiplier(
                    motion.Telemetry.totalMass,
                    9.81f,
                    before);
            motion.SetActuatorForceMultiplier(multiplier);
            VehicleActuatorDiagnostic[] after =
                motion.CaptureActuatorDiagnostics();

            Assert.That(motion.ActuatorForceMultiplier,
                Is.EqualTo(multiplier).Within(0.001f));
            Assert.That(
                ModularBossFlightAuthorityPolicy.HasMinimumAuthority(
                    motion.Telemetry.totalMass,
                    9.81f,
                    after),
                Is.True);
            foreach (Vector3 direction in new[]
                     {
                         Vector3.right,
                         Vector3.left,
                         Vector3.up,
                         Vector3.down,
                         Vector3.forward,
                         Vector3.back
                     })
            {
                Assert.That(after.Count(value =>
                        Vector3.Dot(
                            value.localForceDirection,
                            direction) > 0.99f),
                    Is.EqualTo(2),
                    direction.ToString());
            }

            motion.SetEnvironmentProvider(
                new ZeroPlanetEnvironmentProvider());
            Assert.That(motion.TryBeginFlight(
                    false,
                    out string beginFlightError),
                Is.True,
                beginFlightError);
            body.position = new Vector3(0f, 500f, 0f);
            body.rotation = Quaternion.identity;
            body.isKinematic = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            var idle = new RobocraftControlFrame
            {
                aimForwardWorld = Vector3.forward,
                hasAimOverride = true
            };
            for (int index = 0; index < 30; index++)
                motion.SimulateDiagnosticStep(idle);
            Assert.That(motion.Telemetry.actualControlTorqueWorld.magnitude,
                Is.LessThan(0.01f),
                "A newly spawned balanced Boss must not command a spin.");

            var forwardControl = new RobocraftControlFrame
            {
                move = new Vector2(0f, 1f),
                aimForwardWorld = Vector3.forward,
                hasAimOverride = true
            };
            for (int index = 0; index < 60; index++)
                motion.SimulateDiagnosticStep(forwardControl);
            Vector3 actualForce =
                motion.Telemetry.actualControlForceWorld;
            Vector3 actualTorque =
                motion.Telemetry.actualControlTorqueWorld;
            VehicleActuatorDiagnostic[] commanded =
                motion.CaptureActuatorDiagnostics();
            string actuatorState = string.Join(
                " | ",
                commanded.Select(value =>
                    $"{value.localForceDirection}@{value.localPosition}=" +
                    $"{value.actualThrottle:0.000}"));
            Assert.That(Vector3.Dot(actualForce, Vector3.forward),
                Is.GreaterThan(100f));
            Assert.That(actualTorque.magnitude,
                Is.LessThan(actualForce.magnitude * 0.001f + 0.01f),
                "Balanced translation must not become a spin command. " +
                $"Force={actualForce}, torque={actualTorque}, " +
                $"COM={body.centerOfMass}. {actuatorState}");
            body.isKinematic = true;
        }
        finally
        {
            foreach (GridModuleDefinition definition in definitions)
                Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void ModularBossWorldAvoidanceUsesAimRelativeFlightAxes()
    {
        Vector3 up = Vector3.up;
        Vector3 aimForward = new Vector3(1f, 0.25f, 1f).normalized;
        Vector3 worldDemand = new Vector3(-0.58f, 0.31f, 0.72f).normalized;

        Vector3 axes = ModularBossSteeringPolicy.ToAimRelativeAxes(
            worldDemand,
            aimForward,
            up);
        Vector3 planarForward = Vector3.ProjectOnPlane(
            aimForward,
            up).normalized;
        Vector3 planarRight = Vector3.Cross(up, planarForward).normalized;
        Vector3 reconstructed = planarRight * axes.x +
                                up * axes.y +
                                planarForward * axes.z;

        Assert.That(Vector3.Distance(reconstructed, worldDemand),
            Is.LessThan(0.0001f));
    }

    [Test]
    public void ModularBossHealthyClimbReservesInstalledLiftAuthority()
    {
        float hover =
            ModularBossCombatPolicy.ResolveGravitySupportedVerticalInput(0f);
        float climb =
            ModularBossCombatPolicy.ResolveGravitySupportedVerticalInput(30f);

        Assert.That(hover,
            Is.EqualTo(ModularBossCombatPolicy.HealthyFlightHoverInput)
                .Within(0.001f));
        Assert.That(climb, Is.EqualTo(1f).Within(0.001f));
        Assert.That(
            ModularBossCombatPolicy.ResolveClimbPlanarInputScale(climb),
            Is.EqualTo(0.32f).Within(0.001f));
        Assert.That(
            ModularBossCombatPolicy.ResolveClimbPlanarInputScale(hover),
            Is.GreaterThan(0.32f).And.LessThan(1f));
    }

    [Test]
    public void ModularBossReadabilityInitializesUnityObjectsAfterConstruction()
    {
        GameObject root = CreateRoot(
            "BossReadabilityInitialization",
            Vector3.zero);
        GameObject visibleModule = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        visibleModule.name = "BossReadabilityVisibleModule";
        visibleModule.transform.SetParent(root.transform, false);
        GameObject existingKey = new GameObject("BossReadabilityKey");
        existingKey.transform.SetParent(root.transform, false);
        GameObject existingFill = new GameObject("BossReadabilityFill");
        existingFill.transform.SetParent(root.transform, false);
        GridAssemblyPresenter presenter =
            root.AddComponent<GridAssemblyPresenter>();
        ModularBossReadabilityPresentation presentation =
            root.AddComponent<ModularBossReadabilityPresentation>();
        Assert.DoesNotThrow(() =>
        {
            presentation.Configure(presenter, null);
        });
        Assert.That(existingKey.GetComponent<Light>(), Is.Not.Null);
        Assert.That(existingFill.GetComponent<Light>(), Is.Not.Null);
        presentation.SetRamWarning(true);
        Assert.That(existingKey.GetComponent<Light>().color.r,
            Is.GreaterThan(existingKey.GetComponent<Light>().color.b));
        Assert.That(existingFill.GetComponent<Light>().intensity,
            Is.GreaterThan(2f));
    }

    [Test]
    public void BatchedModuleDestructionRebuildsAssemblyOnlyOnce()
    {
        GridModuleDefinition[] definitions = CreateBossDefinitions();
        GameObject corePrefab = CreateRoot("BatchCorePrefab", Vector3.zero);
        corePrefab.AddComponent<BoxCollider>();
        GameObject blockPrefab = CreateRoot("BatchBlockPrefab", Vector3.zero);
        blockPrefab.AddComponent<BoxCollider>();
        definitions[0].Configure(
            "core", "core", GridModuleCategory.Core,
            corePrefab, new Vector3Int(2, 2, 2),
            100f, 100f, 0f, 100f, 0f, null);
        definitions[1].Configure(
            "neox@block:common:block_111", "block",
            GridModuleCategory.Structure,
            blockPrefab, Vector3Int.one,
            10f, 0f, 0f, 20f, 0f, null);
        try
        {
            var model = new GridAssemblyModel(definitions);
            Assert.That(model.TryPlace(
                    definitions[1].ModuleId,
                    new GridModulePose(new Vector3Int(1, 0, 0), 0),
                    false,
                    out string rightId,
                    out string rightError),
                Is.True,
                rightError);
            Assert.That(model.TryPlace(
                    definitions[1].ModuleId,
                    new GridModulePose(new Vector3Int(-2, 0, 0), 0),
                    false,
                    out string leftId,
                    out string leftError),
                Is.True,
                leftError);

            GameObject ship = CreateRoot("BatchGraphShip", Vector3.zero);
            Rigidbody body = ship.AddComponent<Rigidbody>();
            body.isKinematic = true;
            Transform parts = CreateRoot("BatchParts", Vector3.zero).transform;
            parts.SetParent(ship.transform, false);
            Transform core = CreateRoot("BatchCore", Vector3.zero).transform;
            core.SetParent(ship.transform, false);
            var assembly = ship.AddComponent<SpacecraftEditor.ShipAssembly>();
            assembly.Configure(body, parts, null, 1f);
            GridAssemblyPresenter presenter =
                ship.AddComponent<GridAssemblyPresenter>();
            presenter.Initialize(model, assembly, core);
            ModularBossGridFlightSession flight =
                ship.AddComponent<ModularBossGridFlightSession>();
            VehicleStructureGraph graph =
                ship.AddComponent<VehicleStructureGraph>();
            graph.Initialize(model, presenter, flight);
            graph.SetAutomaticReturnToBuild(false);
            graph.SetDamageEnabled(true);
            graph.BeginFlight();

            int modelChanges = 0;
            int presenterRebuilds = 0;
            model.Changed += () => modelChanges++;
            presenter.Rebuilt += () => presenterRebuilds++;
            var lethal = new SpaceDamageInfo(
                100f,
                Vector3.zero,
                Vector3.right * 10f,
                SpaceDamageType.Explosion,
                null);
            graph.BeginDamageBatch();
            graph.ApplyDamage(rightId, lethal);
            graph.ApplyDamage(leftId, lethal);
            graph.EndDamageBatch();

            Assert.That(model.Find(rightId), Is.Null);
            Assert.That(model.Find(leftId), Is.Null);
            Assert.That(modelChanges, Is.EqualTo(1));
            Assert.That(presenterRebuilds, Is.EqualTo(1));
        }
        finally
        {
            foreach (GridModuleDefinition definition in definitions)
                Object.DestroyImmediate(definition);
        }
    }

    [Test]
    public void FinitePlanetMissionRulesKeepClearanceAndAssaultDistinct()
    {
        FinitePlanetMissionRules clearance =
            FinitePlanetMissionRules.Resolve("abandoned_mine");
        FinitePlanetMissionRules assault =
            FinitePlanetMissionRules.Resolve("industrial_outpost");

        Assert.That(clearance.Kind,
            Is.EqualTo(FinitePlanetMissionObjectiveKind.Clearance));
        Assert.That(clearance.RequiredKills, Is.EqualTo(10));
        Assert.That(assault.Kind,
            Is.EqualTo(FinitePlanetMissionObjectiveKind.Assault));
        Assert.That(assault.ObjectiveCount, Is.EqualTo(3));
        Assert.That(assault.CoreIntegrity, Is.GreaterThan(0f));
    }

    [Test]
    public void EveryFormalMissionAlwaysRequiresAnUrbanBattlefield()
    {
        string[] missionIds =
        {
            "abandoned_mine",
            "wind_canyon",
            "industrial_outpost",
            "modular_boss",
            "future_formal_mission"
        };

        foreach (string missionId in missionIds)
        {
            Assert.That(
                FinitePlanetMissionRules.Resolve(missionId, 5)
                    .RequiresUrbanEnvironment,
                Is.True,
                missionId);

            for (int seed = -7; seed <= 7; seed++)
            {
                Assert.That(
                    PlanetMissionEnvironmentResolver.Resolve(
                        seed,
                        "planet-" + seed,
                        missionId),
                    Is.EqualTo(PlanetMissionEnvironmentKind.Urban),
                    missionId + " seed " + seed);
            }
        }

        Assert.That(
            PlanetMissionEnvironmentResolver.GetDisplayName(
                PlanetMissionEnvironmentKind.Natural),
            Is.EqualTo("城市城区"));
    }

    [Test]
    public void LegacyNaturalChapterSelectionIsCanonicalizedToUrban()
    {
        try
        {
            PlanetOrbitChapterSelectionContext.Set(
                "legacy-planet",
                "abandoned_mine",
                "旧存档任务",
                Vector3.forward,
                7319,
                PlanetMissionEnvironmentKind.Natural,
                3);

            Assert.That(
                PlanetOrbitChapterSelectionContext.EnvironmentKind,
                Is.EqualTo(PlanetMissionEnvironmentKind.Urban));
            Assert.That(
                PlanetOrbitChapterSelectionContext.HasSelection,
                Is.True);
            Assert.That(
                PlanetOrbitChapterSelectionContext.MissionId,
                Is.EqualTo("abandoned_mine"));
            Assert.That(
                PlanetOrbitChapterSelectionContext.LandingDirection,
                Is.EqualTo(Vector3.forward));
            Assert.That(
                PlanetOrbitChapterSelectionContext.PlanetDifficultyIndex,
                Is.EqualTo(3));
            Assert.That(
                PlanetOrbitChapterSelectionContext.MissionSeed,
                Is.EqualTo(7319));
        }
        finally
        {
            PlanetOrbitChapterSelectionContext.Clear();
        }
    }

    [Test]
    public void ModularBossBridgeWindowsScaleAndThenDiminish()
    {
        Assert.That(
            ModularBossObstaclePolicy.FirstCeaseFireSeconds(0),
            Is.EqualTo(4.5f).Within(0.001f));
        Assert.That(
            ModularBossObstaclePolicy.FirstCeaseFireSeconds(5),
            Is.EqualTo(2.5f).Within(0.001f));
        float first = ModularBossObstaclePolicy.ResolveCeaseFireSeconds(0, 0);
        float second = ModularBossObstaclePolicy.ResolveCeaseFireSeconds(0, 1);
        Assert.That(second, Is.EqualTo(first * 0.72f).Within(0.001f));
        Assert.That(
            ModularBossObstaclePolicy.ResolveCeaseFireSeconds(5, 100),
            Is.EqualTo(1f).Within(0.001f));
        Assert.That(
            ModularBossObstaclePolicy.RamImmunitySeconds,
            Is.EqualTo(14f));
        Assert.That(
            ModularBossObstaclePolicy.ShouldConfirmBridgeContact(10f, 10.24f),
            Is.False);
        Assert.That(
            ModularBossObstaclePolicy.ShouldConfirmBridgeContact(10f, 10.25f),
            Is.True);
        Assert.That(
            ModularBossObstaclePolicy.ShouldCommitRam(20f, 21f, true),
            Is.True,
            "A direct probe may keep the same bridge confirmed after physical " +
            "contact briefly separates during the local detour.");
        Assert.That(
            ModularBossObstaclePolicy.ShouldCommitRam(20f, 30f, false),
            Is.False);
        Assert.That(
            ModularBossObstaclePolicy.ShouldForceProbedBridgeApproach(
                30f,
                31.35f,
                false,
                true),
            Is.True,
            "A Boss that cannot find a detour must approach the bridge instead " +
            "of hovering forever.");
    }

    [Test]
    public void DestroyedBossBridgeStartsOnlyOneRecoveryAndReleasesTracking()
    {
        GameObject bossObject = new GameObject("BossBridgeRecoveryTest");
        GameObject bridgeObject = new GameObject("DestroyedBossBridgeTest");
        try
        {
            ModularBossCombatRuntime runtime =
                bossObject.AddComponent<ModularBossCombatRuntime>();
            UnityPlanet.CityPcg.UrbanDestructibleBridge bridge =
                bridgeObject.AddComponent<
                    UnityPlanet.CityPcg.UrbanDestructibleBridge>();
            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            typeof(UnityPlanet.CityPcg.UrbanDestructibleBridge)
                .GetField("destroyed", Flags)
                ?.SetValue(bridge, true);
            Type runtimeType = typeof(ModularBossCombatRuntime);
            runtimeType.GetField("lockedBridge", Flags)
                ?.SetValue(runtime, bridge);
            runtimeType.GetField("contactedBridge", Flags)
                ?.SetValue(runtime, bridge);
            runtimeType.GetField("probedBlockingBridge", Flags)
                ?.SetValue(runtime, bridge);
            runtimeType.GetField("obstacleState", Flags)
                ?.SetValue(runtime, ModularBossObstacleState.RamCharge);

            MethodInfo updateObstacleState = runtimeType.GetMethod(
                "UpdateObstacleState",
                Flags);
            Assert.That(updateObstacleState, Is.Not.Null);
            updateObstacleState.Invoke(runtime, null);

            Assert.That(
                runtime.ObstacleState,
                Is.EqualTo(ModularBossObstacleState.Recovery));
            Assert.That(
                runtimeType.GetField("lockedBridge", Flags)
                    ?.GetValue(runtime),
                Is.Null);
            Assert.That(
                runtimeType.GetField("contactedBridge", Flags)
                    ?.GetValue(runtime),
                Is.Null);
            Assert.That(
                runtimeType.GetField("probedBlockingBridge", Flags)
                    ?.GetValue(runtime),
                Is.Null);

            float firstRecoveryEnd = (float)runtimeType.GetField(
                "stateEndsAt",
                Flags).GetValue(runtime);
            updateObstacleState.Invoke(runtime, null);
            float secondRecoveryEnd = (float)runtimeType.GetField(
                "stateEndsAt",
                Flags).GetValue(runtime);
            Assert.That(
                secondRecoveryEnd,
                Is.EqualTo(firstRecoveryEnd),
                "The same destroyed bridge must not refresh Recovery every " +
                "physics frame.");
        }
        finally
        {
            Object.DestroyImmediate(bridgeObject);
            Object.DestroyImmediate(bossObject);
        }
    }

    [Test]
    public void ModularBossNavigationRejectsGenericSolidVolumesAndCorridors()
    {
        GameObject bossObject = new GameObject("BossNavigationProbeTest");
        GameObject obstacleObject = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        obstacleObject.name = "GenericCombinedCitySolid";
        try
        {
            ModularBossCombatRuntime runtime =
                bossObject.AddComponent<ModularBossCombatRuntime>();
            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            MethodInfo volumeClear = typeof(ModularBossCombatRuntime)
                .GetMethod("IsNavigationVolumeClear", Flags);
            MethodInfo corridorClear = typeof(ModularBossCombatRuntime)
                .GetMethod("IsNavigationCorridorClear", Flags);
            Assert.That(volumeClear, Is.Not.Null);
            Assert.That(corridorClear, Is.Not.Null);

            obstacleObject.transform.position = Vector3.zero;
            obstacleObject.transform.localScale = new Vector3(4f, 4f, 4f);
            Physics.SyncTransforms();
            Assert.That(
                (bool)volumeClear.Invoke(
                    runtime,
                    new object[] { Vector3.zero, Vector3.one }),
                Is.False,
                "A solid combined/world collider must not be recorded as " +
                "a safe Boss navigation point.");

            obstacleObject.transform.position = new Vector3(0f, 0f, 8f);
            obstacleObject.transform.localScale = new Vector3(4f, 4f, 2f);
            Physics.SyncTransforms();
            Assert.That(
                (bool)corridorClear.Invoke(
                    runtime,
                    new object[]
                    {
                        Vector3.zero,
                        new Vector3(0f, 0f, 16f),
                        Vector3.one
                    }),
                Is.False,
                "A clear destination behind a solid wall is not a valid " +
                "backtrack route.");
        }
        finally
        {
            Object.DestroyImmediate(obstacleObject);
            Object.DestroyImmediate(bossObject);
        }
    }

    [Test]
    public void ModularBossNavigationCanExitItsCurrentOverlap()
    {
        GameObject bossObject = new GameObject("BossOverlapEscapeTest");
        GameObject obstacleObject = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        obstacleObject.name = "SourceOverlapObstacle";
        try
        {
            ModularBossCombatRuntime runtime =
                bossObject.AddComponent<ModularBossCombatRuntime>();
            // A legal high-tier Boss can have hundreds of module colliders.
            // They must be filterable without overflowing the navigation
            // query and turning every escape route into a false blockage.
            for (int index = 0; index < 320; index++)
            {
                GameObject module = new GameObject("BossModuleCollider");
                module.transform.SetParent(bossObject.transform, false);
                module.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
            }
            obstacleObject.transform.position = Vector3.zero;
            obstacleObject.transform.localScale = new Vector3(4f, 4f, 4f);
            Physics.SyncTransforms();

            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            MethodInfo corridorClear = typeof(ModularBossCombatRuntime)
                .GetMethod("IsNavigationCorridorClear", Flags);
            Assert.That(corridorClear, Is.Not.Null);
            Assert.That(
                (bool)corridorClear.Invoke(
                    runtime,
                    new object[]
                    {
                        Vector3.zero,
                        new Vector3(0f, 0f, -12f),
                        Vector3.one
                    }),
                Is.True,
                "The collider already trapping the Boss must not prevent it " +
                "from following its recorded entry route back out.");
        }
        finally
        {
            Object.DestroyImmediate(obstacleObject);
            Object.DestroyImmediate(bossObject);
        }
    }

    [Test]
    public void ModularBossEscapeUsesSideLaneWhenUpwardLanesAreClosed()
    {
        GameObject bossObject = new GameObject("BossSideEscapeTest");
        GameObject roofObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofObject.name = "LowEscapeRoof";
        try
        {
            ModularBossCombatRuntime runtime =
                bossObject.AddComponent<ModularBossCombatRuntime>();
            VehicleStructureGraph graph =
                bossObject.AddComponent<VehicleStructureGraph>();
            roofObject.transform.position = new Vector3(0f, 4f, 0f);
            roofObject.transform.localScale = new Vector3(80f, 2f, 80f);
            Physics.SyncTransforms();

            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            typeof(ModularBossCombatRuntime)
                .GetField("structureGraph", Flags)
                ?.SetValue(runtime, graph);
            MethodInfo resolveEscape = typeof(ModularBossCombatRuntime)
                .GetMethod("TryResolveVerticalEscapeDirection", Flags);
            Assert.That(resolveEscape, Is.Not.Null);
            object[] arguments = { Vector3.zero };
            Assert.That(
                (bool)resolveEscape.Invoke(runtime, arguments),
                Is.True);
            Vector3 direction = (Vector3)arguments[0];
            Assert.That(Mathf.Abs(direction.y), Is.LessThan(0.2f),
                "A roofed-in Boss should take an open side lane instead of " +
                "repeating an impossible upward command.");
        }
        finally
        {
            Object.DestroyImmediate(roofObject);
            Object.DestroyImmediate(bossObject);
        }
    }

    [Test]
    public void ModularBossMuzzleProbeRecognizesWorldOcclusion()
    {
        GameObject bossObject = new GameObject("BossMuzzleProbeTest");
        GameObject obstacleObject = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        obstacleObject.name = "MuzzleBlockingCover";
        try
        {
            ModularBossCombatRuntime runtime =
                bossObject.AddComponent<ModularBossCombatRuntime>();
            obstacleObject.transform.position = new Vector3(0f, 0f, 5f);
            obstacleObject.transform.localScale = new Vector3(4f, 4f, 2f);
            Physics.SyncTransforms();

            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            MethodInfo findBlocker = typeof(ModularBossCombatRuntime)
                .GetMethod("TryFindWorldBlocker", Flags);
            Assert.That(findBlocker, Is.Not.Null);
            object[] arguments =
            {
                Vector3.zero,
                new Vector3(0f, 0f, 10f),
                null,
                0f
            };
            Assert.That(
                (bool)findBlocker.Invoke(runtime, arguments),
                Is.True);
            Assert.That(arguments[2], Is.EqualTo(
                obstacleObject.GetComponent<Collider>()));
            Assert.That((float)arguments[3], Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(obstacleObject);
            Object.DestroyImmediate(bossObject);
        }
    }

    [Test]
    public void ModularBossCityHuntPolicyRewardsCoverAndReadableDodges()
    {
        Assert.That(ModularBossCombatPolicy.InitialSpawnDistance(0),
            Is.EqualTo(310f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.InitialSpawnDistance(5),
            Is.EqualTo(270f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.PursuitForceMultiplier,
            Is.EqualTo(1.30f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.PlayerRamTelegraphSeconds,
            Is.GreaterThanOrEqualTo(0.48f));
        Assert.That(ModularBossCombatPolicy.PlayerRamChargeSeconds,
            Is.GreaterThan(ModularBossCombatPolicy.PlayerRamTelegraphSeconds));
        Assert.That(ModularBossCombatPolicy.CoverBreachDelay(0),
            Is.GreaterThan(ModularBossCombatPolicy.CoverBreachDelay(5)));
        Assert.That(ModularBossCombatPolicy.CoverBreachDelay(0),
            Is.LessThanOrEqualTo(3f),
            "Even the first Boss must answer persistent cover promptly.");
        Assert.That(ModularBossCombatPolicy.MaximumTurnRate,
            Is.GreaterThan(ModularBossCombatPolicy.MinimumTurnRate));

        const float baseSpread = 0.02f;
        float openSky = ModularBossCombatPolicy.ResolveWeaponSpread(
            baseSpread, false, false, 180f);
        float nearCover = ModularBossCombatPolicy.ResolveWeaponSpread(
            baseSpread, false, true, 180f);
        float occluded = ModularBossCombatPolicy.ResolveWeaponSpread(
            baseSpread, true, true, 180f);
        Assert.That(openSky, Is.LessThan(baseSpread));
        Assert.That(nearCover, Is.GreaterThan(openSky * 2f));
        Assert.That(occluded, Is.GreaterThan(nearCover));

        float ramDamage = ModularBossCombatPolicy.ResolvePlayerRamDamage(
            300f, 38f, 3);
        Assert.That(ramDamage, Is.GreaterThan(0f));
        Assert.That(ramDamage, Is.LessThan(300f),
            "One fair ram may damage a player module but must not guarantee a " +
            "fresh-module one-shot.");
        Assert.That(ModularBossCombatPolicy.ResolveBossCollisionModuleLoss(16f),
            Is.Zero);
        Assert.That(ModularBossCombatPolicy.ResolveBossCollisionModuleLoss(33f),
            Is.EqualTo(2));
        Assert.That(ModularBossCombatPolicy.ResolveBossCollisionModuleLoss(52f),
            Is.EqualTo(3));
    }

    [Test]
    public void ModularBossStrengthStartsAtFirstBossAndIncreasesEveryTier()
    {
        float previousShield = 0f;
        float previousEffectiveShield = 0f;
        float previousStructure = 0f;
        float previousCore = 0f;
        float previousSystem = 0f;
        float previousWeaponDamage = 0f;
        float previousWeaponIntegrity = 0f;

        for (int tier = 0; tier < 6; tier++)
        {
            float shield =
                ModularBossCombatPolicy.ResolveShieldCapacity(tier);
            int rechargeCount =
                ModularBossCombatPolicy.ResolveShieldRechargeCount(tier);
            float effectiveShield = shield *
                (1f + rechargeCount *
                 ModularBossCombatPolicy.ShieldRechargeFraction);
            ModularBossGenerationProfile profile =
                ModularBossGenerationProfile.ForTier(tier);
            float weaponDamage = ModularBossCombatPolicy.
                ResolveBossWeaponDamageMultiplier(tier);

            Assert.That(shield, Is.GreaterThan(previousShield),
                $"Tier {tier} shield must exceed the previous Boss tier.");
            Assert.That(effectiveShield,
                Is.GreaterThan(previousEffectiveShield),
                $"Tier {tier} total shield budget must increase.");
            Assert.That(profile.StructureIntegrityMultiplier,
                Is.GreaterThan(previousStructure));
            Assert.That(profile.CoreIntegrityMultiplier,
                Is.GreaterThan(previousCore));
            Assert.That(profile.SystemIntegrityMultiplier,
                Is.GreaterThan(previousSystem));
            Assert.That(profile.WeaponIntegrityMultiplier,
                Is.GreaterThan(previousWeaponIntegrity));
            Assert.That(profile.WeaponIntegrityMultiplier,
                Is.GreaterThan(profile.SystemIntegrityMultiplier),
                "Boss weapons need independent anti-dismantle durability.");
            Assert.That(weaponDamage, Is.GreaterThan(previousWeaponDamage),
                $"Tier {tier} weapon damage must exceed the previous Boss tier.");

            previousShield = shield;
            previousEffectiveShield = effectiveShield;
            previousStructure = profile.StructureIntegrityMultiplier;
            previousCore = profile.CoreIntegrityMultiplier;
            previousSystem = profile.SystemIntegrityMultiplier;
            previousWeaponDamage = weaponDamage;
            previousWeaponIntegrity = profile.WeaponIntegrityMultiplier;
        }

        Assert.That(ModularBossCombatPolicy.ResolveShieldCapacity(0),
            Is.EqualTo(10000f).Within(0.001f));
        Assert.That(ModularBossGenerationProfile.ForTier(0)
                .CoreIntegrityMultiplier,
            Is.EqualTo(3f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.
                ResolveBossWeaponDamageMultiplier(0),
            Is.EqualTo(1.35f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.
                ResolveBossWeaponDamageMultiplier(5),
            Is.EqualTo(1.80f).Within(0.001f));
        Assert.That(ModularBossGenerationProfile.ForTier(0)
                .WeaponIntegrityMultiplier,
            Is.EqualTo(10f).Within(0.001f));
        Assert.That(ModularBossGenerationProfile.ForTier(5)
                .WeaponIntegrityMultiplier,
            Is.EqualTo(26f).Within(0.001f));
        Assert.That(18f * ModularBossCombatPolicy.
                ResolveBossWeaponDamageMultiplier(5),
            Is.EqualTo(32.4f).Within(0.001f),
            "Even the final Boss machine gun should require multiple hits per module.");
        Assert.That(ModularBossCombatPolicy.ResolveBuildingShieldDamageFraction(
                14f),
            Is.EqualTo(0.30f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.ResolveBuildingShieldDamageFraction(
                52f),
            Is.EqualTo(0.40f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.ResolveBridgeShieldDamageFraction(
                8f),
            Is.EqualTo(0.45f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.ResolveBridgeShieldDamageFraction(
                40f),
            Is.EqualTo(0.60f).Within(0.001f));
    }

    [Test]
    public void ModularBossWeaponlessPhaseClosesInsideRamAuthorizationRange()
    {
        for (int tier = 0; tier < 6; tier++)
        {
            ModularBossGenerationProfile profile =
                ModularBossGenerationProfile.ForTier(tier);
            float armed = ModularBossCombatPolicy.
                ResolvePreferredCombatRadius(
                    profile.PreferredCombatRadius,
                    tier,
                    1);
            float weaponless = ModularBossCombatPolicy.
                ResolvePreferredCombatRadius(
                    profile.PreferredCombatRadius,
                    tier,
                    0);
            Assert.That(armed,
                Is.EqualTo(profile.PreferredCombatRadius).Within(0.001f));
            Assert.That(weaponless,
                Is.LessThan(ModularBossCombatPolicy.PlayerRamDistance(tier)),
                $"Tier {tier} must close far enough to authorize a ram.");
        }
    }

    [Test]
    public void ModularBossRecoveryKeepsSelectedDownwardEscapeDirection()
    {
        float downward = ModularBossCombatPolicy.
            ResolveRecoveryEscapeVerticalInput(-1f, 0f, 1f, false);
        float lateral = ModularBossCombatPolicy.
            ResolveRecoveryEscapeVerticalInput(0f, 0f, 1f, false);
        Assert.That(downward, Is.LessThan(0f),
            "A clear downward exit must not be clamped back to lift.");
        Assert.That(lateral, Is.GreaterThan(0f),
            "A side exit still needs ordinary gravity support.");
    }

    [Test]
    public void ModularBossBridgeShieldDamageRequiresANewFastImpact()
    {
        Assert.That(ModularBossCombatPolicy.ShouldApplyBridgeShieldImpact(
            true, 12f), Is.True);
        Assert.That(ModularBossCombatPolicy.ShouldApplyBridgeShieldImpact(
            false, 12f), Is.False,
            "Continuous collision stay must not drain the shield again.");
        Assert.That(ModularBossCombatPolicy.ShouldApplyBridgeShieldImpact(
            true, 2f), Is.False,
            "Resting or sliding contact is not another bridge impact.");
    }

    [Test]
    public void ModularBossFreezePausesStateDeadlinesAndSuppressesWeapons()
    {
        GameObject root = new GameObject("BossFreezeContractTest");
        try
        {
            ModularBossCombatRuntime runtime =
                root.AddComponent<ModularBossCombatRuntime>();
            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            FieldInfo stateEndsAt = typeof(ModularBossCombatRuntime)
                .GetField("stateEndsAt", Flags);
            FieldInfo freezeStartedAt = typeof(ModularBossCombatRuntime)
                .GetField("temporaryFreezeStartedAt", Flags);
            MethodInfo shiftDeadlines = typeof(ModularBossCombatRuntime)
                .GetMethod("ShiftFreezeSensitiveTimes", Flags);
            Assert.That(stateEndsAt, Is.Not.Null);
            Assert.That(freezeStartedAt, Is.Not.Null);
            Assert.That(shiftDeadlines, Is.Not.Null);
            stateEndsAt.SetValue(runtime, Time.time + 2f);
            float before = (float)stateEndsAt.GetValue(runtime);

            runtime.SetTemporarilyFrozen(true);
            Assert.That(runtime.IsTemporarilyFrozen, Is.True);
            Assert.That(runtime.WeaponsSuppressed, Is.True);
            shiftDeadlines.Invoke(runtime, new object[] { 1f });
            freezeStartedAt.SetValue(runtime, -1f);
            runtime.SetTemporarilyFrozen(false);

            Assert.That(runtime.IsTemporarilyFrozen, Is.False);
            Assert.That((float)stateEndsAt.GetValue(runtime),
                Is.GreaterThanOrEqualTo(before + 0.99f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ModularBossThawClearsStoredRamVelocity()
    {
        GameObject root = new GameObject("BossFreezeVelocityTest");
        try
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            ModularBossCombatRuntime runtime =
                root.AddComponent<ModularBossCombatRuntime>();
            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            typeof(ModularBossCombatRuntime)
                .GetField("combatActive", Flags)
                ?.SetValue(runtime, true);
            body.velocity = new Vector3(0f, 0f, 48f);
            TemporaryEnemyFreeze freeze =
                root.AddComponent<TemporaryEnemyFreeze>();
            freeze.FreezeFor(4f);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(runtime.IsTemporarilyFrozen, Is.True);

            MethodInfo restore = typeof(TemporaryEnemyFreeze)
                .GetMethod("Restore", Flags);
            Assert.That(restore, Is.Not.Null);
            restore.Invoke(freeze, null);

            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.velocity.sqrMagnitude, Is.Zero.Within(0.001f),
                "A thawed Boss must rebuild motion through RC3, not restore " +
                "an old charge velocity.");
            Assert.That(runtime.IsTemporarilyFrozen, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void ModularBossRemembersGenericSolidWorldCollision()
    {
        GameObject bossObject = new GameObject("BossGenericCollisionTest");
        GameObject solidObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            ModularBossCombatRuntime runtime =
                bossObject.AddComponent<ModularBossCombatRuntime>();
            const BindingFlags Flags = BindingFlags.Instance |
                                       BindingFlags.NonPublic;
            MethodInfo accepts = typeof(ModularBossCombatRuntime)
                .GetMethod("IsGenericBlockingWorldCollider", Flags);
            Assert.That(accepts, Is.Not.Null);
            Assert.That((bool)accepts.Invoke(
                runtime,
                new object[] { solidObject.GetComponent<Collider>() }),
                Is.True,
                "Combined city/test solids without Urban components still " +
                "need to refresh the escape watchdog.");
        }
        finally
        {
            Object.DestroyImmediate(solidObject);
            Object.DestroyImmediate(bossObject);
        }
    }

    [Test]
    public void ModularBossOnlyBreachesWhenPlayerIsBetweenOpposingBuildings()
    {
        Assert.That(ModularBossCombatPolicy.IsPlayerEntrenched(
            true, false, false, false), Is.False,
            "One roadside building is ordinary cover and must be routed around.");
        Assert.That(ModularBossCombatPolicy.IsPlayerEntrenched(
            true, true, false, false), Is.True);
        Assert.That(ModularBossCombatPolicy.IsPlayerEntrenched(
            false, false, true, true), Is.True);
        Assert.That(ModularBossCombatPolicy.IsPlayerEntrenched(
            true, false, true, false), Is.False,
            "An open corner still has a route and must not authorize demolition.");
    }

    [Test]
    public void ModularBossRoadSpawnPrefersWideMainRoad()
    {
        var roads = new List<AirCombatRoadStrip>
        {
            new AirCombatRoadStrip
            {
                stableId = "narrow-side",
                start = new Vector3(250f, 0f, 0f),
                end = new Vector3(250f, 0f, 600f),
                width = 22.26f,
                laneTiles = 1,
                kind = AirCombatRouteKind.MaskedFlank
            },
            new AirCombatRoadStrip
            {
                stableId = "wide-main",
                start = new Vector3(0f, 0f, 100f),
                end = new Vector3(0f, 0f, 700f),
                width = 66.78f,
                laneTiles = 3,
                kind = AirCombatRouteKind.Main
            }
        };

        Assert.That(ModularBossRoadSpawnPolicy.TryResolve(
            roads,
            Vector3.zero,
            300f,
            out Vector3 spawn,
            out Vector3 direction), Is.True);
        Assert.That(spawn.x, Is.EqualTo(0f).Within(0.001f));
        Assert.That(spawn.z, Is.InRange(250f, 410f));
        Assert.That(Mathf.Abs(Vector3.Dot(direction, Vector3.forward)),
            Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void ModularBossOnlyRamsBridgeBetweenItAndPlayer()
    {
        Vector3 boss = Vector3.zero;
        Vector3 player = Vector3.forward * 100f;
        Assert.That(ModularBossObstaclePolicy.IsBridgeBetweenBossAndPlayer(
            boss,
            player,
            new Bounds(Vector3.forward * 45f, new Vector3(20f, 10f, 4f)),
            2f), Is.True);
        Assert.That(ModularBossObstaclePolicy.IsBridgeBetweenBossAndPlayer(
            boss,
            player,
            new Bounds(new Vector3(40f, 0f, 45f), Vector3.one * 4f),
            2f), Is.False);
        Assert.That(ModularBossObstaclePolicy.IsBridgeBetweenBossAndPlayer(
            boss,
            player,
            new Bounds(Vector3.back * 15f, Vector3.one * 4f),
            2f), Is.False);
    }

    [Test]
    public void ModularBossEmergencyAssistAlwaysRetainsFlightAuthority()
    {
        const float mass = 8000f;
        const float gravity = 9.81f;
        Vector3 emergency =
            ModularBossFlightAuthorityPolicy.ResolveEmergencyCoreForce(
                mass,
                gravity);
        Assert.That(emergency.y, Is.GreaterThan(mass * gravity));
        Assert.That(emergency.x, Is.GreaterThan(0f));
        Assert.That(emergency.z, Is.GreaterThan(0f));
        Assert.That(
            ModularBossFlightAuthorityPolicy.ResolveEmergencyDownForce(mass),
            Is.GreaterThan(0f));
        Assert.That(ModularBossCombatPolicy.ResolveDamagedMobility(0, 24),
            Is.EqualTo(ModularBossCombatPolicy.MinimumDamagedMobility)
                .Within(0.001f));
        Assert.That(ModularBossCombatPolicy.ResolveDamagedMobility(24, 24),
            Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void ModularBossEmergencyAssistDoesNotRequestGravityTwice()
    {
        float healthyHover = ModularBossCombatPolicy.
            ResolveAltitudeVerticalInput(0f, 0f, 1f, false);
        float assistedHover = ModularBossCombatPolicy.
            ResolveAltitudeVerticalInput(0f, 0f, 1f, true);
        float assistedRecovery = ModularBossCombatPolicy.
            ResolveAltitudeVerticalInput(0f, -6f, 1f, true);

        Assert.That(healthyHover,
            Is.EqualTo(ModularBossCombatPolicy.HealthyFlightHoverInput)
                .Within(0.001f));
        Assert.That(assistedHover, Is.Zero.Within(0.001f),
            "Training core authority already supplies gravity support.");
        Assert.That(assistedRecovery, Is.GreaterThan(0f),
            "A damaged Boss must still arrest a real downward velocity.");
        Assert.That(ModularBossCombatPolicy.RequiresAltitudeReturn(-44f),
            Is.False);
        Assert.That(ModularBossCombatPolicy.RequiresAltitudeReturn(-46f),
            Is.True,
            "Special Boss states must not climb indefinitely above a low player.");
        Assert.That(ModularBossCombatPolicy.RequiresAltitudeReturn(-20f, 12f),
            Is.True,
            "Upward momentum must trigger the return before the hull overshoots.");
    }

    [Test]
    public void ModularBossBuildingRamAlwaysTargetsAVisibleFacade()
    {
        Bounds building = new Bounds(
            new Vector3(100f, 75f, 200f),
            new Vector3(80f, 150f, 60f));
        Bounds bossBelow = new Bounds(
            new Vector3(100f, -20f, 200f),
            new Vector3(45f, 35f, 55f));
        Vector3 point = ModularBossCombatPolicy.
            ResolveBuildingFacadeBreachPoint(building, bossBelow);

        Assert.That(point.y,
            Is.GreaterThanOrEqualTo(building.min.y + 4f));
        Assert.That(point.y,
            Is.LessThanOrEqualTo(building.max.y - 4f));
        Assert.That(point.y - bossBelow.extents.y,
            Is.GreaterThanOrEqualTo(building.min.y));
        Assert.That(point.y + bossBelow.extents.y,
            Is.LessThanOrEqualTo(building.max.y));
        bool onSideWall =
            Mathf.Abs(point.x - building.min.x) < 0.001f ||
            Mathf.Abs(point.x - building.max.x) < 0.001f ||
            Mathf.Abs(point.z - building.min.z) < 0.001f ||
            Mathf.Abs(point.z - building.max.z) < 0.001f;
        Assert.That(onSideWall, Is.True,
            "A Boss projected inside the footprint must not target the floor or roof.");
    }

    [Test]
    public void ModularBossBuildingRamStagesOutsideBeforeChargingFacade()
    {
        Bounds building = new Bounds(
            new Vector3(100f, 75f, 200f),
            new Vector3(80f, 150f, 60f));
        Bounds bossAboveRoof = new Bounds(
            new Vector3(100f, 190f, 200f),
            new Vector3(60f, 80f, 70f));
        Vector3 facade = ModularBossCombatPolicy.
            ResolveBuildingFacadeBreachPoint(building, bossAboveRoof);
        float verticalTolerance = ModularBossCombatPolicy.
            ResolveCoverBreachVerticalTolerance(bossAboveRoof);
        Assert.That(
            facade.y + bossAboveRoof.extents.y + verticalTolerance,
            Is.LessThanOrEqualTo(building.max.y - 3.99f),
            "Every charge-authorized height must keep the hull below the roof edge.");
        Vector3 staging = ModularBossCombatPolicy.
            ResolveBuildingFacadeStagingPoint(
                building,
                bossAboveRoof,
                facade);
        Vector3 normal = ModularBossCombatPolicy.
            ResolveBuildingFacadeNormal(building, facade);

        Assert.That(Vector3.Dot(
                staging - facade,
                normal),
            Is.GreaterThanOrEqualTo(
                ModularBossCombatPolicy.CoverBreachStandoff +
                Mathf.Min(bossAboveRoof.extents.x,
                          bossAboveRoof.extents.z)));
        Assert.That(staging.x < building.min.x ||
                    staging.x > building.max.x ||
                    staging.z < building.min.z ||
                    staging.z > building.max.z,
            Is.True,
            "The charge launch point must be outside the roof footprint.");
        Bounds roofHeightAtStaging = new Bounds(
            new Vector3(staging.x, bossAboveRoof.center.y, staging.z),
            bossAboveRoof.size);
        Assert.That(ModularBossCombatPolicy.IsCoverBreachStaged(
                roofHeightAtStaging,
                staging),
            Is.False,
            "A Boss still above the roof is not aligned merely because its planar position is valid.");
        Assert.That(ModularBossCombatPolicy.IsFacadeImpactNormal(
                Vector3.up,
                Vector3.up),
            Is.False,
            "Roof contact must never count as a building ram.");
        Assert.That(ModularBossCombatPolicy.IsFacadeImpactNormal(
                Vector3.right,
                Vector3.up),
            Is.True);
        Vector3 inward = (facade - staging).normalized;
        Assert.That(ModularBossCombatPolicy.
                IsCoverBreachLaunchVelocityReady(
                    inward * 8f,
                    facade,
                    staging),
            Is.True);
        Assert.That(ModularBossCombatPolicy.
                IsCoverBreachLaunchVelocityReady(
                    -inward * 30f,
                    facade,
                    staging),
            Is.False,
            "A hull still drifting away from the facade must brake before charge authorization.");

        Bounds diagonalBoss = new Bounds(
            new Vector3(30f, 190f, 270f),
            bossAboveRoof.size);
        Vector3 diagonalFacade = ModularBossCombatPolicy.
            ResolveBuildingFacadeBreachPoint(building, diagonalBoss);
        int boundaryAxes = 0;
        if (Mathf.Abs(diagonalFacade.x - building.min.x) < 0.001f ||
            Mathf.Abs(diagonalFacade.x - building.max.x) < 0.001f)
            boundaryAxes++;
        if (Mathf.Abs(diagonalFacade.z - building.min.z) < 0.001f ||
            Mathf.Abs(diagonalFacade.z - building.max.z) < 0.001f)
            boundaryAxes++;
        Assert.That(boundaryAxes, Is.EqualTo(1),
            "A diagonal approach must select one facade, never a building corner.");
        bool diagonalOnXFace =
            Mathf.Abs(diagonalFacade.x - building.min.x) < 0.001f ||
            Mathf.Abs(diagonalFacade.x - building.max.x) < 0.001f;
        if (diagonalOnXFace)
        {
            float requiredMargin = Mathf.Min(
                building.extents.z - 4f,
                diagonalBoss.extents.z + 4f);
            Assert.That(diagonalFacade.z,
                Is.InRange(
                    building.min.z + requiredMargin,
                    building.max.z - requiredMargin));
        }
        else
        {
            float requiredMargin = Mathf.Min(
                building.extents.x - 4f,
                diagonalBoss.extents.x + 4f);
            Assert.That(diagonalFacade.x,
                Is.InRange(
                    building.min.x + requiredMargin,
                    building.max.x - requiredMargin));
        }
    }

    [Test]
    public void ModularBossBuildingRamEscapesAStalledStagingApproach()
    {
        float observedAt = 10f;
        float beforeTimeout = observedAt +
            ModularBossCombatPolicy.CoverBreachStallSeconds - 0.01f;
        float atTimeout = observedAt +
            ModularBossCombatPolicy.CoverBreachStallSeconds;

        Assert.That(ModularBossCombatPolicy.
                IsCoverBreachApproachStalled(
                    false,
                    observedAt,
                    beforeTimeout),
            Is.False);
        Assert.That(ModularBossCombatPolicy.
                IsCoverBreachApproachStalled(
                    false,
                    observedAt,
                    atTimeout),
            Is.True);
        Assert.That(ModularBossCombatPolicy.
                IsCoverBreachApproachStalled(
                    true,
                    observedAt,
                    atTimeout + 10f),
            Is.False,
            "A Boss already at the launch point must be allowed to charge.");
        Assert.That(ModularBossCombatPolicy.
                ShouldApplyCoverBreachSeparationAssist(
                    false,
                    false,
                    true,
                    false),
            Is.True,
            "Fresh target-building contact must pull the hull back out before staging.");
        Assert.That(ModularBossCombatPolicy.
                ShouldApplyCoverBreachSeparationAssist(
                    false,
                    true,
                    true,
                    true),
            Is.False,
            "The bounded separation impulse must never be applied every physics tick.");
        Assert.That(ModularBossCombatPolicy.CoverBreachStallSeconds,
            Is.LessThan(ModularBossCombatPolicy.
                CoverBreachApproachTimeoutSeconds));
        Assert.That(ModularBossCombatPolicy.CoverBreachFailedRetrySeconds,
            Is.LessThan(ModularBossCombatPolicy.
                CoverBreachCooldownSeconds));
    }

    [Test]
    public void ModularBossAggressionClosesForReadyRamsWithoutIgnoringCover()
    {
        for (int tier = 0; tier <= 5; tier++)
        {
            float gunRadius = ModularBossGenerationProfile.ForTier(tier).
                PreferredCombatRadius;
            float aggressiveRadius = ModularBossCombatPolicy.
                ResolveTacticalCombatRadius(
                    gunRadius,
                    tier,
                    true,
                    false);
            Assert.That(aggressiveRadius,
                Is.LessThan(ModularBossCombatPolicy.PlayerRamDistance(tier)),
                "A ready Boss must actively cross into ram authorization range.");
            Assert.That(ModularBossCombatPolicy.ResolveTacticalCombatRadius(
                    gunRadius,
                    tier,
                    true,
                    true),
                Is.EqualTo(gunRadius).Within(0.001f),
                "Hard cover must route into demolition instead of a blind player ram.");
        }
    }

    [Test]
    public void ModularBossDemolitionUtilityTargetsThePlayersActualShelter()
    {
        float closeShelter = ModularBossCombatPolicy.ScorePlayerCoverBuilding(
            2,
            12f,
            0.92f,
            95f);
        float broadForegroundTower = ModularBossCombatPolicy.
            ScorePlayerCoverBuilding(
                7,
                150f,
                0.25f,
                25f);
        Assert.That(closeShelter, Is.GreaterThan(broadForegroundTower),
            "The Boss should demolish the cover pinning the player, not an unrelated large foreground tower.");

        Assert.That(ModularBossCombatPolicy.
                ShouldReconsiderCoverBreachTarget(
                    false,
                    false,
                    10f,
                    10f + ModularBossCombatPolicy.
                        CoverBreachTargetReconsiderSeconds + 0.01f),
            Is.True);
        Assert.That(ModularBossCombatPolicy.
                ShouldReconsiderCoverBreachTarget(
                    false,
                    true,
                    10f,
                    20f),
            Is.False,
            "A hull physically touching the target building must finish escaping instead of target-thrashing.");
    }

    [Test]
    public void ModularBossBacktrackCannotBeExtendedBySidewaysMotion()
    {
        Bounds largeHull = new Bounds(
            Vector3.zero,
            new Vector3(42f, 56f, 38f));
        float arrival = ModularBossCombatPolicy.
            ResolveBacktrackArrivalDistance(largeHull, 20f);
        Assert.That(arrival,
            Is.GreaterThan(ModularBossCombatPolicy.BacktrackArrivalDistance),
            "A large fast hull needs a reachable arrival volume instead of a tiny point target.");
        Assert.That(arrival,
            Is.LessThanOrEqualTo(ModularBossCombatPolicy.
                BacktrackMaximumArrivalDistance));

        Assert.That(ModularBossCombatPolicy.HasBacktrackTargetProgress(
                50f,
                49f),
            Is.False,
            "Small drift or sideways motion must not refresh the escape watchdog.");
        Assert.That(ModularBossCombatPolicy.HasBacktrackTargetProgress(
                50f,
                47.9f),
            Is.True,
            "Only meaningful reduction in distance to the safe point counts as progress.");
        Assert.That(ModularBossCombatPolicy.BacktrackStallSeconds,
            Is.LessThan(ModularBossCombatPolicy.BacktrackEscapeSeconds));
    }

    [Test]
    public void PlanetMissionDifficultyAndGalaxyCoinRewardsIncreaseByPlanet()
    {
        int previousClearanceKills = -1;
        int previousClearanceReward = -1;
        int previousAssaultKills = -1;
        int previousAssaultReward = -1;
        int previousBossReward = -1;

        for (int planetIndex = 0;
             planetIndex <
             ProceduralInterstellarGenerator.StarterSystemPlanetCount;
             planetIndex++)
        {
            FinitePlanetMissionRules clearance =
                FinitePlanetMissionRules.Resolve(
                    "abandoned_mine",
                    planetIndex);
            FinitePlanetMissionRules assault =
                FinitePlanetMissionRules.Resolve(
                    "industrial_outpost",
                    planetIndex);
            FinitePlanetMissionRules boss =
                FinitePlanetMissionRules.Resolve(
                    "modular_boss",
                    planetIndex);

            Assert.That(clearance.RequiredKills,
                Is.GreaterThan(previousClearanceKills));
            Assert.That(clearance.GalaxyCoinReward,
                Is.GreaterThan(previousClearanceReward));
            Assert.That(assault.RequiredKills,
                Is.GreaterThan(previousAssaultKills));
            Assert.That(assault.GalaxyCoinReward,
                Is.GreaterThan(previousAssaultReward));
            Assert.That(boss.Kind,
                Is.EqualTo(FinitePlanetMissionObjectiveKind.Boss));
            Assert.That(boss.GalaxyCoinReward,
                Is.GreaterThan(previousBossReward));
            Assert.That(clearance.ObjectiveDescription,
                Does.Contain(clearance.RequiredKills.ToString()));
            Assert.That(assault.ObjectiveDescription,
                Does.Contain(assault.RequiredKills.ToString()));

            previousClearanceKills = clearance.RequiredKills;
            previousClearanceReward = clearance.GalaxyCoinReward;
            previousAssaultKills = assault.RequiredKills;
            previousAssaultReward = assault.GalaxyCoinReward;
            previousBossReward = boss.GalaxyCoinReward;
        }
    }

    [Test]
    public void ObjectiveDrivenHordeKeepsSpawningUntilMissionRequestsFinalClear()
    {
        GameObject host = CreateRoot("ObjectiveDrivenHorde", Vector3.zero);
        HordeCombatDirector director = host.AddComponent<HordeCombatDirector>();
        Rigidbody playerBody = CreateRoot(
            "ObjectiveDrivenPlayer",
            Vector3.zero).AddComponent<Rigidbody>();
        playerBody.isKinematic = true;

        director.ConfigureObjectiveDrivenSession(true);
        director.BeginSession(
            playerBody,
            Vector3.zero,
            500f,
            7319,
            1);
        director.Tick(HordeCombatDirector.SessionDurationSeconds + 10f);

        Assert.That(director.IsRunning, Is.True);
        Assert.That(director.IsFinalClear, Is.False);
        Assert.That(director.IsFinished, Is.False);
        Assert.That(director.CurrentPhase, Is.EqualTo(3));

        director.BeginFinalClear();
        Assert.That(director.IsFinalClear, Is.True);
    }

    [Test]
    public void HordeProfilesSeparateSuicideAndRangedEnemies()
    {
        HordeEnemyProfile suicide =
            HordeEnemyProfile.ForRole(HordeEnemyRole.Interceptor);
        HordeEnemyProfile striker =
            HordeEnemyProfile.ForRole(HordeEnemyRole.Striker);
        HordeEnemyProfile gunship =
            HordeEnemyProfile.ForRole(HordeEnemyRole.Gunship);

        Assert.That(suicide.attackKind,
            Is.EqualTo(HordeEnemyAttackKind.Suicide));
        Assert.That(suicide.gunCount, Is.Zero);
        Assert.That(suicide.detonationDamage, Is.GreaterThan(0f));
        Assert.That(suicide.detonationRadius, Is.GreaterThan(0f));
        Assert.That(striker.attackKind,
            Is.EqualTo(HordeEnemyAttackKind.Ranged));
        Assert.That(striker.gunCount, Is.GreaterThan(0));
        Assert.That(gunship.attackKind,
            Is.EqualTo(HordeEnemyAttackKind.Ranged));
        Assert.That(gunship.gunCount, Is.GreaterThan(0));
    }

    [TestCase(65f)]
    [TestCase(104f)]
    public void HordeRangedInterceptSolvesLongRangeLateralPlayerSpeed(
        float playerSpeed)
    {
        Vector3 origin = Vector3.zero;
        Vector3 targetPosition = Vector3.forward * 420f;
        Vector3 targetVelocity = Vector3.right * playerSpeed;

        bool solved = HordeRangedAimPolicy.TryResolveIntercept(
            origin,
            Vector3.zero,
            targetPosition,
            targetVelocity,
            240f,
            2.5f,
            out Vector3 direction,
            out Vector3 impactPoint,
            out float flightSeconds);

        Assert.That(solved, Is.True);
        Assert.That(flightSeconds, Is.GreaterThan(0.8f),
            "The long-range solution must not regress to the old 0.8-second lead cap.");
        Assert.That(flightSeconds, Is.LessThanOrEqualTo(2.5f));
        Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
        Vector3 projectileAtImpact =
            origin + direction * 240f * flightSeconds;
        Assert.That(Vector3.Distance(projectileAtImpact, impactPoint),
            Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(
                targetPosition + targetVelocity * flightSeconds,
                impactPoint),
            Is.LessThan(0.01f));
    }

    [Test]
    public void HordeRangedInterceptCompensatesInheritedShooterVelocity()
    {
        Vector3 origin = Vector3.zero;
        Vector3 shooterVelocity = Vector3.right * 55f;
        Vector3 targetPosition = Vector3.forward * 300f;

        bool solved = HordeRangedAimPolicy.TryResolveIntercept(
            origin,
            shooterVelocity,
            targetPosition,
            Vector3.zero,
            240f,
            2.5f,
            out Vector3 direction,
            out Vector3 impactPoint,
            out float flightSeconds);

        Assert.That(solved, Is.True);
        Assert.That(direction.x, Is.LessThan(0f),
            "The muzzle must aim against the shooter's inherited lateral velocity.");
        Vector3 projectileAtImpact = origin +
            (direction * 240f + shooterVelocity) * flightSeconds;
        Assert.That(Vector3.Distance(projectileAtImpact, targetPosition),
            Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(impactPoint, targetPosition),
            Is.LessThan(0.01f));
    }

    [Test]
    public void HordeRangedInterceptUnsolvableTargetReturnsFiniteOutputs()
    {
        bool solved = HordeRangedAimPolicy.TryResolveIntercept(
            Vector3.zero,
            Vector3.zero,
            Vector3.forward * 100f,
            Vector3.forward * 300f,
            240f,
            2.5f,
            out Vector3 direction,
            out Vector3 impactPoint,
            out float flightSeconds);

        Assert.That(solved, Is.False);
        Assert.That(float.IsNaN(direction.x) ||
                    float.IsNaN(direction.y) ||
                    float.IsNaN(direction.z), Is.False);
        Assert.That(float.IsInfinity(direction.x) ||
                    float.IsInfinity(direction.y) ||
                    float.IsInfinity(direction.z), Is.False);
        Assert.That(float.IsNaN(impactPoint.x) ||
                    float.IsNaN(impactPoint.y) ||
                    float.IsNaN(impactPoint.z), Is.False);
        Assert.That(float.IsInfinity(impactPoint.x) ||
                    float.IsInfinity(impactPoint.y) ||
                    float.IsInfinity(impactPoint.z), Is.False);
        Assert.That(float.IsNaN(flightSeconds) ||
                    float.IsInfinity(flightSeconds), Is.False);
        Assert.That(flightSeconds, Is.Zero);
    }

    [Test]
    public void HordeReengagementCueIsLoopingSpatialAudioWithOcclusionFilter()
    {
        GameObject host = CreateRoot("ReengagementAudioHost", Vector3.zero);
        HordeCombatDirector director = host.AddComponent<HordeCombatDirector>();
        WeaponVisualPool visuals = host.AddComponent<WeaponVisualPool>();
        WeaponProjectilePool projectiles =
            host.AddComponent<WeaponProjectilePool>();
        projectiles.Initialize(visuals);
        GameObject enemyRoot = CreateRoot(
            "ReengagementAudioEnemy",
            Vector3.zero);
        HordeEnemyVehicle enemy =
            enemyRoot.AddComponent<HordeEnemyVehicle>();

        Assert.That(enemy.Initialize(
            director,
            visuals,
            projectiles,
            out string error), Is.True, error);
        AudioSource audio = enemyRoot.GetComponent<AudioSource>();
        AudioLowPassFilter lowPass =
            enemyRoot.GetComponent<AudioLowPassFilter>();

        Assert.That(audio, Is.Not.Null);
        Assert.That(audio.clip, Is.Not.Null);
        Assert.That(audio.loop, Is.True);
        Assert.That(audio.playOnAwake, Is.False);
        Assert.That(audio.spatialBlend, Is.EqualTo(1f));
        Assert.That(audio.maxDistance, Is.GreaterThanOrEqualTo(250f));
        Assert.That(lowPass, Is.Not.Null);
        Assert.That(lowPass.cutoffFrequency, Is.LessThan(1500f));
    }

    [Test]
    public void HordeSuicideEnemyDetonationDamagesPlayerAndConsumesItself()
    {
        GameObject host = CreateRoot("SuicideDirector", Vector3.zero);
        HordeCombatDirector director = host.AddComponent<HordeCombatDirector>();
        WeaponVisualPool visuals = host.AddComponent<WeaponVisualPool>();
        WeaponProjectilePool projectiles =
            host.AddComponent<WeaponProjectilePool>();
        projectiles.Initialize(visuals);

        GameObject player = CreateRoot(
            "SuicideTarget",
            Vector3.forward * 5f);
        player.AddComponent<BoxCollider>();
        Rigidbody playerBody = player.AddComponent<Rigidbody>();
        playerBody.isKinematic = true;
        VehicleCombatTeamUtility.SetTeam(
            player,
            VehicleCombatTeam.Player);
        TestDamageable damageable = player.AddComponent<TestDamageable>();

        GameObject enemyRoot = CreateRoot("SuicideEnemy", Vector3.zero);
        HordeEnemyVehicle enemy = enemyRoot.AddComponent<HordeEnemyVehicle>();
        Assert.That(enemy.Initialize(
            director,
            visuals,
            projectiles,
            out string error), Is.True, error);
        enemy.Activate(
            HordeEnemyProfile.ForRole(HordeEnemyRole.Interceptor),
            playerBody,
            Vector3.zero,
            Quaternion.identity,
            0,
            1);
        Physics.SyncTransforms();

        MethodInfo detonate = typeof(HordeEnemyVehicle).GetMethod(
            "Detonate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(detonate, Is.Not.Null);
        detonate.Invoke(enemy, null);

        Assert.That(damageable.DamageReceived, Is.GreaterThan(0f));
        Assert.That(enemy.IsDestroyed, Is.True);
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.False);
    }

    [Test]
    public void EnemyFleetVisualsReuseReviewedSpacecraftCatalog()
    {
        Assert.That(EnemyFleetVisualLibrary.HasHull(
            "hull.sf_stealth_fighter"), Is.True);
        Assert.That(EnemyFleetVisualLibrary.HasHull(
            "hull.sf_fighter_gr2"), Is.True);
        Assert.That(EnemyFleetVisualLibrary.HasHull(
            "hull.sf_dropship_r35"), Is.True);
        Assert.That(EnemyFleetVisualLibrary.HasHull(
            "hull.sf_modular_pirate"), Is.True);
    }

    [Test]
    public void HordeSelectsPcgIngressMatchingTacticalSectorThenRotatesRetries()
    {
        var ingresses = new List<Vector3>
        {
            Vector3.back * 500f,
            Vector3.left * 500f,
            Vector3.forward * 500f,
            Vector3.right * 500f
        };
        int preferred = HordeCombatDirector.SelectPlannedIngressIndex(
            ingresses,
            Vector3.zero,
            Vector3.forward,
            2,
            0);
        int retry = HordeCombatDirector.SelectPlannedIngressIndex(
            ingresses,
            Vector3.zero,
            Vector3.forward,
            2,
            1);

        Assert.That(preferred, Is.EqualTo(3));
        Assert.That(retry, Is.EqualTo(0));
    }

    [Test]
    public void HordeWaitsForEveryPcgIngressBeforeUsingSafeAdaptiveFallback()
    {
        Assert.That(
            HordeCombatDirector.ShouldUseAdaptiveSpawnFallback(0, 6),
            Is.False);
        Assert.That(
            HordeCombatDirector.ShouldUseAdaptiveSpawnFallback(5, 6),
            Is.False);
        Assert.That(
            HordeCombatDirector.ShouldUseAdaptiveSpawnFallback(6, 6),
            Is.True);
        Assert.That(
            HordeCombatDirector.ShouldUseAdaptiveSpawnFallback(30, 6),
            Is.True);
        Assert.That(
            HordeCombatDirector.ShouldUseAdaptiveSpawnFallback(0, 0),
            Is.True);

        var directions = new HashSet<int>();
        for (int sample = 0; sample < 8; sample++)
        {
            directions.Add(
                HordeCombatDirector.ResolveAdaptiveSpawnDirectionStep(
                    6,
                    sample));
        }
        CollectionAssert.AreEquivalent(
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            directions,
            "安全兜底必须扫描完整八方向，不能被原始战术扇区锁死。");
    }

    [Test]
    public void LegacyCombatEntryRemainsAndModeOverloadIsAvailable()
    {
        MethodInfo legacy = typeof(CombatTestController).GetMethod(
            "TryBeginCombat",
            new[] { typeof(string).MakeByRefType() });
        MethodInfo modeAware = typeof(CombatTestController).GetMethod(
            "TryBeginCombat",
            new[]
            {
                typeof(CombatTestMode),
                typeof(string).MakeByRefType()
            });

        Assert.That(legacy, Is.Not.Null);
        Assert.That(modeAware, Is.Not.Null);
    }

    [Test]
    public void HordePhaseScheduleUsesTwoMinuteRuleCurve()
    {
        Assert.That(HordeCombatDirector.SessionDurationSeconds, Is.EqualTo(120f));
        Assert.That(HordeCombatDirector.FinalClearStartSeconds, Is.EqualTo(108f));
        Assert.That(HordeCombatDirector.PhaseAt(0f), Is.EqualTo(1));
        Assert.That(HordeCombatDirector.PhaseAt(44.99f), Is.EqualTo(1));
        Assert.That(HordeCombatDirector.PhaseAt(45f), Is.EqualTo(2));
        Assert.That(HordeCombatDirector.PhaseAt(89.99f), Is.EqualTo(2));
        Assert.That(HordeCombatDirector.PhaseAt(90f), Is.EqualTo(3));
        Assert.That(HordeCombatDirector.PhaseAt(108f), Is.EqualTo(4));
        Assert.That(HordeCombatDirector.ActiveCapAt(0f), Is.EqualTo(3));
        Assert.That(HordeCombatDirector.ActiveCapAt(12f), Is.EqualTo(4));
        Assert.That(HordeCombatDirector.ActiveCapAt(45f), Is.EqualTo(7));
        Assert.That(HordeCombatDirector.ActiveCapAt(90f), Is.EqualTo(10));
        Assert.That(HordeCombatDirector.SpawnIntervalAt(0f), Is.EqualTo(7f));
        Assert.That(HordeCombatDirector.SpawnIntervalAt(12f), Is.EqualTo(6f));
        Assert.That(HordeCombatDirector.SpawnIntervalAt(45f), Is.EqualTo(5f));
        Assert.That(HordeCombatDirector.SpawnIntervalAt(90f), Is.EqualTo(4f));
        Assert.That(float.IsPositiveInfinity(
            HordeCombatDirector.SpawnIntervalAt(108f)), Is.True);
        Assert.That(HordeCombatDirector.ThreatBudgetAt(0f), Is.EqualTo(2));
        Assert.That(HordeCombatDirector.ThreatBudgetAt(45f), Is.EqualTo(3));
        Assert.That(HordeCombatDirector.ThreatBudgetAt(90f), Is.EqualTo(4));
        Assert.That(HordeCombatDirector.ThreatBudgetAt(108f), Is.EqualTo(0));
        Assert.That(HordeCombatDirector.ThreatCapAt(12f), Is.EqualTo(5));
        Assert.That(HordeCombatDirector.ThreatCapAt(45f), Is.EqualTo(10));
        Assert.That(HordeCombatDirector.ThreatCapAt(90f), Is.EqualTo(15));
    }

    [Test]
    public void HordeProfilesAreFixedAndNonAdaptive()
    {
        HordeEnemyProfile interceptor =
            HordeEnemyProfile.ForRole(HordeEnemyRole.Interceptor);
        HordeEnemyProfile striker =
            HordeEnemyProfile.ForRole(HordeEnemyRole.Striker);
        HordeEnemyProfile gunship =
            HordeEnemyProfile.ForRole(HordeEnemyRole.Gunship);

        Assert.That(interceptor.maximumHealth, Is.EqualTo(220f));
        Assert.That(interceptor.massKg, Is.EqualTo(850f));
        Assert.That(interceptor.maximumSpeed, Is.EqualTo(70f));
        Assert.That(striker.maximumHealth, Is.EqualTo(420f));
        Assert.That(striker.massKg, Is.EqualTo(1200f));
        Assert.That(striker.maximumSpeed, Is.EqualTo(58f));
        Assert.That(gunship.maximumHealth, Is.EqualTo(700f));
        Assert.That(gunship.massKg, Is.EqualTo(1800f));
        Assert.That(gunship.maximumSpeed, Is.EqualTo(48f));
        Assert.That(gunship.gunCount, Is.EqualTo(2));
        Assert.That(gunship.attackTokenCost, Is.EqualTo(2));
    }

    [Test]
    public void FriendlyColliderDoesNotStopTraceOrReceiveDamage()
    {
        GameObject source = CreateRoot("EnemySource", Vector3.zero);
        VehicleCombatTeamUtility.SetTeam(source, VehicleCombatTeam.Enemy);
        TestDamageable friendly = CreateDamageable(
            "Friendly",
            new Vector3(0f, 0f, 5f),
            VehicleCombatTeam.Enemy);
        TestDamageable hostile = CreateDamageable(
            "Hostile",
            new Vector3(0f, 0f, 10f),
            VehicleCombatTeam.Player);
        Physics.SyncTransforms();
        WeaponProfile profile = WeaponProfileLibrary.Resolve(null);
        profile.damage = 10f;
        profile.explosionRadius = 0f;

        bool hit = WeaponDamageUtility.Trace(
            Vector3.zero,
            Vector3.forward,
            30f,
            source.transform,
            source,
            profile,
            null,
            out RaycastHit selected);

        Assert.That(hit, Is.True);
        Assert.That(selected.collider.GetComponent<TestDamageable>(),
            Is.SameAs(hostile));
        Assert.That(friendly.DamageReceived, Is.Zero);
        Assert.That(hostile.DamageReceived, Is.EqualTo(10f));
    }

    [Test]
    public void FriendlyExplosionDamageIsRejectedButNeutralLegacyRemains()
    {
        GameObject source = CreateRoot("EnemySource", Vector3.zero);
        VehicleCombatTeamUtility.SetTeam(source, VehicleCombatTeam.Enemy);
        TestDamageable friendly = CreateDamageable(
            "Friendly",
            new Vector3(2f, 0f, 0f),
            VehicleCombatTeam.Enemy);
        TestDamageable hostile = CreateDamageable(
            "Hostile",
            new Vector3(4f, 0f, 0f),
            VehicleCombatTeam.Player);
        TestDamageable neutral = CreateDamageable(
            "Neutral",
            new Vector3(6f, 0f, 0f),
            VehicleCombatTeam.Neutral);
        Physics.SyncTransforms();

        WeaponDamageUtility.ApplyExplosion(
            Vector3.zero,
            10f,
            20f,
            source.transform,
            source);

        Assert.That(friendly.DamageReceived, Is.Zero);
        Assert.That(hostile.DamageReceived, Is.GreaterThan(0f));
        Assert.That(neutral.DamageReceived, Is.GreaterThan(0f));
    }

    [Test]
    public void NavigationBuildsBoundedThreeDimensionalGraph()
    {
        Vector3 isolatedCenter = new Vector3(20000f, 0f, 20000f);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        roots.Add(ground);
        ground.name = "NavigationGround";
        ground.transform.position = isolatedCenter;
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();
        var navigation = new HordeAirNavigationService();
        IEnumerator build = navigation.Build(
            isolatedCenter,
            600f,
            null,
            null);
        int steps = 0;
        while (build.MoveNext())
        {
            steps++;
            Assert.That(steps, Is.LessThan(500));
        }

        Assert.That(navigation.IsReady, Is.True, navigation.FailureReason);
        Assert.That(navigation.ValidNodeCount,
            Is.InRange(12, 144));
        var path = new List<Vector3>();
        Assert.That(navigation.FindPath(
            isolatedCenter + new Vector3(-100f, 80f, -100f),
            isolatedCenter + new Vector3(100f, 80f, 100f),
            path),
            Is.True);
        Assert.That(path.Count, Is.GreaterThan(0));
    }

    [Test]
    public void HordeGroundSamplingDoesNotTreatBuildingRoofAsTerrain()
    {
        Vector3 isolatedCenter = new Vector3(22000f, 0f, 22000f);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(ground);
        ground.name = "NavigationGround";
        ground.transform.position = isolatedCenter + Vector3.down;
        ground.transform.localScale = new Vector3(1200f, 2f, 1200f);
        GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(building);
        building.name = "NavigationBuilding";
        building.transform.position = isolatedCenter + Vector3.up * 30f;
        building.transform.localScale = new Vector3(40f, 60f, 40f);
        Physics.SyncTransforms();

        var navigation = new HordeAirNavigationService();
        IEnumerator build = navigation.Build(
            isolatedCenter,
            600f,
            null,
            null);
        while (build.MoveNext())
        {
        }

        Assert.That(
            navigation.TrySampleGround(
                isolatedCenter.x,
                isolatedCenter.z,
                out float sampledGround),
            Is.True);
        Assert.That(sampledGround, Is.EqualTo(0f).Within(0.05f),
            "A roof above the flat ground must not lift the navigation graph.");
    }

    [Test]
    public void GroundedPlayerBallisticLineIsIndependentFromHullCorridor()
    {
        Vector3 isolatedCenter = new Vector3(26000f, 0f, 26000f);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(ground);
        ground.name = "NavigationGround";
        ground.transform.position = isolatedCenter + Vector3.down;
        ground.transform.localScale = new Vector3(200f, 2f, 200f);
        Physics.SyncTransforms();

        var navigation = new HordeAirNavigationService();
        Vector3 muzzle = isolatedCenter + new Vector3(-50f, 6f, 0f);
        Vector3 groundedTarget = isolatedCenter + Vector3.up;
        Assert.That(
            navigation.HasStaticClearCorridor(muzzle, groundedTarget),
            Is.False,
            "The five-metre enemy hull must remain blocked by the ground.");
        Assert.That(
            navigation.HasStaticClearBallisticLine(muzzle, groundedTarget),
            Is.True,
            "A thin projectile line to the landed vehicle must stay valid.");

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(wall);
        wall.name = "NavigationWall";
        wall.transform.position =
            isolatedCenter + new Vector3(-25f, 4f, 0f);
        wall.transform.localScale = new Vector3(2f, 8f, 12f);
        Physics.SyncTransforms();
        Assert.That(
            navigation.HasStaticClearBallisticLine(muzzle, groundedTarget),
            Is.False,
            "Separating projectile LOS must not permit firing through buildings.");
    }

    [Test]
    public void GroundedPlayerGetsReachableOrdinaryEnemyAttackAnchor()
    {
        Vector3 isolatedCenter = new Vector3(24000f, 0f, 24000f);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(ground);
        ground.name = "NavigationGround";
        ground.transform.position = isolatedCenter + Vector3.down;
        ground.transform.localScale = new Vector3(1200f, 2f, 1200f);
        Physics.SyncTransforms();

        var navigation = new HordeAirNavigationService();
        IEnumerator build = navigation.Build(
            isolatedCenter,
            600f,
            null,
            null);
        while (build.MoveNext())
        {
        }
        var path = new List<Vector3>();
        Vector3 player = isolatedCenter + Vector3.up;

        Assert.That(
            navigation.TryFindGroundedCombatApproachPath(
                isolatedCenter + new Vector3(-220f, 70f, 0f),
                player,
                HordeEnemyProfile.ForRole(HordeEnemyRole.Striker),
                0,
                path,
                out Vector3 approach),
            Is.True);
        Assert.That(path, Is.Not.Empty);
        Assert.That(approach.y, Is.GreaterThanOrEqualTo(24f));
        Assert.That(
            Vector3.ProjectOnPlane(approach - player, Vector3.up).magnitude,
            Is.InRange(85f, 120f));
        Assert.That(
            navigation.HasStaticClearBallisticLine(approach, player),
            Is.True);
    }

    [Test]
    public void GroundedPlayerSuicideApproachStaysHullClearOfGround()
    {
        Vector3 isolatedCenter = new Vector3(28000f, 0f, 28000f);
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roots.Add(ground);
        ground.name = "NavigationGround";
        ground.transform.position = isolatedCenter + Vector3.down;
        ground.transform.localScale = new Vector3(300f, 2f, 300f);
        Physics.SyncTransforms();

        var navigation = new HordeAirNavigationService();
        Vector3 player = isolatedCenter + Vector3.up;
        Vector3 safeApproach =
            navigation.ResolveSafePlayerApproachPoint(player);

        Assert.That(safeApproach.y, Is.EqualTo(7f).Within(0.05f));
        Assert.That(
            navigation.HasStaticClearCorridor(
                isolatedCenter + new Vector3(-35f, 18f, 0f),
                safeApproach),
            Is.True);
        Assert.That(
            navigation.HasStaticClearCorridor(
                isolatedCenter + new Vector3(-35f, 18f, 0f),
                player),
            Is.False,
            "The safe intercept point fixes the ground collision without " +
            "allowing the enemy hull to pass through the terrain.");
    }

    [Test]
    public void HordeEnemyUsesSingleHealthAndNoModuleDamageGraph()
    {
        GameObject host = CreateRoot("HordeTestHost", Vector3.zero);
        HordeCombatDirector director = host.AddComponent<HordeCombatDirector>();
        WeaponVisualPool visuals = host.AddComponent<WeaponVisualPool>();
        WeaponProjectilePool projectiles = host.AddComponent<WeaponProjectilePool>();
        projectiles.Initialize(visuals);
        GameObject enemyRoot = CreateRoot("HordeEnemy", Vector3.zero);
        HordeEnemyVehicle enemy = enemyRoot.AddComponent<HordeEnemyVehicle>();

        Assert.That(enemy.Initialize(
            director,
            visuals,
            projectiles,
            out string error),
            Is.True,
            error);
        GameObject player = CreateRoot("Player", new Vector3(0f, 0f, 100f));
        Rigidbody playerBody = player.AddComponent<Rigidbody>();
        playerBody.isKinematic = true;
        enemy.Activate(
            HordeEnemyProfile.ForRole(HordeEnemyRole.Interceptor),
            playerBody,
            Vector3.zero,
            Quaternion.identity,
            0,
            1);

        Assert.That(enemy.MaximumIntegrity, Is.EqualTo(220f));
        Assert.That(
            enemy.GetComponentsInChildren<VehicleModuleDamageReceiver>(true),
            Is.Empty);
        enemy.ApplyDamage(new SpaceDamageInfo(
            999f,
            enemy.transform.position,
            Vector3.forward * 20f,
            SpaceDamageType.Projectile,
            player));
        Assert.That(enemy.IsDestroyed, Is.True);
        Assert.That(enemy.Integrity, Is.Zero);
        Assert.That(enemy.GetComponent<Collider>().enabled, Is.False);
        Assert.That(
            enemy.GetComponentsInChildren<VehicleDetachedDebrisLifetime>(true),
            Is.Empty);
    }

    [Test]
    public void DirectBreakDebrisIsVisualOnlyAndGetsBoundedRecoil()
    {
        GameObject vehicle = CreateRoot("DebrisSourceVehicle", Vector3.zero);
        Rigidbody sourceBody = vehicle.AddComponent<Rigidbody>();
        sourceBody.useGravity = false;
        sourceBody.velocity = new Vector3(12f, 0f, 0f);
        sourceBody.angularVelocity = new Vector3(0f, 0.4f, 0f);
        GameObject module = GameObject.CreatePrimitive(PrimitiveType.Cube);
        module.name = "DebrisSourceModule";
        module.transform.SetParent(vehicle.transform, false);
        module.transform.localPosition = new Vector3(0f, 0f, 3f);
        Physics.SyncTransforms();
        Vector3 center = module.GetComponent<Renderer>().bounds.center;
        Vector3 inheritedVelocity = sourceBody.GetPointVelocity(center);

        GameObject debris = VehicleDetachedDebris.SpawnDirectBreak(
            new[]
            {
                new DetachedDebrisPart("test-module", module, 50f)
            },
            sourceBody,
            Vector3.forward * 120f,
            center - Vector3.forward * 0.5f);

        Assert.That(debris, Is.Not.Null);
        roots.Add(debris);
        Rigidbody debrisBody = debris.GetComponent<Rigidbody>();
        Assert.That(debrisBody, Is.Not.Null);
        Assert.That(debrisBody.detectCollisions, Is.False);
        Assert.That(
            debris.GetComponentsInChildren<Collider>(true),
            Has.All.Matches<Collider>(item => !item.enabled));
        float recoilSpeed =
            (debrisBody.velocity - inheritedVelocity).magnitude;
        Assert.That(recoilSpeed, Is.InRange(4f, 13f));
        Assert.That(debrisBody.angularVelocity.magnitude,
            Is.InRange(0.5f, 7.01f));
        Assert.That(
            debris.GetComponent<VehicleDetachedDebrisLifetime>(),
            Is.Not.Null);
    }

    [Test]
    public void DuelEnemyDirectModuleBreakSpawnsVisiblePhysicalDebris()
    {
        GameObject player = CreateRoot("DuelDebrisPlayer", Vector3.zero);
        Rigidbody playerBody = player.AddComponent<Rigidbody>();
        playerBody.isKinematic = true;
        GameObject enemyRoot = CreateRoot(
            "DuelDebrisEnemy",
            new Vector3(0f, 80f, 120f));
        EnemyAirCombatVehicle enemy =
            enemyRoot.AddComponent<EnemyAirCombatVehicle>();
        enemy.Initialize(null, null, playerBody, null, null);
        VehicleModuleDamageReceiver target = enemyRoot
            .GetComponentsInChildren<VehicleModuleDamageReceiver>(true)
            .OrderByDescending(item =>
                Mathf.Abs(item.transform.localPosition.x))
            .First();
        string runtimeId = target.gameObject.name;
        float health = enemy.MaximumIntegrity(runtimeId);
        Vector3 hitPoint = target.transform.position -
                           enemyRoot.transform.forward * 0.5f;

        enemy.ApplyDamage(
            runtimeId,
            new SpaceDamageInfo(
                health + 1f,
                hitPoint,
                enemyRoot.transform.forward * 120f,
                SpaceDamageType.Projectile,
                player));

        Assert.That(enemy.IsDestroyed(runtimeId), Is.True);
        Assert.That(target.gameObject.activeSelf, Is.False);
        VehicleDetachedDebrisLifetime[] debris =
            CombatTransientRoot.GetOrCreate()
                .GetComponentsInChildren<
                    VehicleDetachedDebrisLifetime>(true);
        Assert.That(debris, Has.Length.EqualTo(1));
        Assert.That(debris[0].gameObject.activeSelf, Is.True);
        Assert.That(
            debris[0].GetComponent<Rigidbody>().detectCollisions,
            Is.False);
        roots.Add(debris[0].gameObject);
    }

    GameObject CreateRoot(string name, Vector3 position)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        roots.Add(root);
        return root;
    }

    static void AssertBalancedBossThrusterPairs(
        ModularBossBuildResult build)
    {
        Dictionary<string, GridModuleRecord> records =
            build.Model.Records.ToDictionary(record => record.RuntimeId);
        foreach (ModularBossThrusterDirection direction in
                 Enum.GetValues(typeof(ModularBossThrusterDirection)))
        {
            Vector3 forceDirection = BossDirectionVector(direction);
            Vector3 centerSum = Vector3.zero;
            int count = 0;
            foreach (KeyValuePair<string, ModularBossThrusterDirection> item in
                     build.ThrusterDirections)
            {
                if (item.Value != direction)
                    continue;
                centerSum += GridAssemblyModel.ModuleCenter(records[item.Key]);
                count++;
            }
            Vector3 averageCenter = centerSum / Mathf.Max(1, count);
            Vector3 translationTorqueLever = Vector3.ProjectOnPlane(
                averageCenter,
                forceDirection);
            Assert.That(translationTorqueLever.magnitude,
                Is.LessThan(0.0001f),
                direction + " thrusters must not create a permanent torque.");
        }
    }

    static void AssertBossThrusterTorqueLeverCoverage(
        ModularBossBuildResult build)
    {
        Dictionary<string, GridModuleRecord> records =
            build.Model.Records.ToDictionary(record => record.RuntimeId);
        foreach (ModularBossThrusterDirection direction in
                 Enum.GetValues(typeof(ModularBossThrusterDirection)))
        {
            Vector3 forceDirection = BossDirectionVector(direction);
            Vector3 firstTangent = Mathf.Abs(forceDirection.x) > 0.5f
                ? Vector3.up
                : Vector3.right;
            Vector3 secondTangent = Vector3.Cross(
                forceDirection,
                firstTangent).normalized;
            float firstLever = 0f;
            float secondLever = 0f;
            foreach (KeyValuePair<string, ModularBossThrusterDirection> item in
                     build.ThrusterDirections)
            {
                if (item.Value != direction)
                    continue;
                Vector3 center =
                    GridAssemblyModel.ModuleCenter(records[item.Key]);
                firstLever = Mathf.Max(
                    firstLever,
                    Mathf.Abs(Vector3.Dot(center, firstTangent)));
                secondLever = Mathf.Max(
                    secondLever,
                    Mathf.Abs(Vector3.Dot(center, secondTangent)));
            }
            Assert.That(firstLever, Is.GreaterThan(0.9f),
                direction + " must retain its first rotation lever arm.");
            Assert.That(secondLever, Is.GreaterThan(0.9f),
                direction + " must retain its second rotation lever arm.");
        }
    }

    static Vector3 BossDirectionVector(
        ModularBossThrusterDirection direction)
    {
        switch (direction)
        {
            case ModularBossThrusterDirection.Right:
                return Vector3.right;
            case ModularBossThrusterDirection.Left:
                return Vector3.left;
            case ModularBossThrusterDirection.Up:
                return Vector3.up;
            case ModularBossThrusterDirection.Down:
                return Vector3.down;
            case ModularBossThrusterDirection.Forward:
                return Vector3.forward;
            default:
                return Vector3.back;
        }
    }

    static HashSet<Vector3Int> ResolveBossStructureCells(
        ModularBossBuildResult build)
    {
        return new HashSet<Vector3Int>(build.Model.Records
            .Where(record => record.Definition.Category ==
                             GridModuleCategory.Structure)
            .SelectMany(record => build.Model.GetCells(record)));
    }

    static Vector3Int ResolveBossHullSpan(ModularBossBuildResult build)
    {
        HashSet<Vector3Int> cells = ResolveBossStructureCells(build);
        return new Vector3Int(
            cells.Max(cell => cell.x) - cells.Min(cell => cell.x) + 1,
            cells.Max(cell => cell.y) - cells.Min(cell => cell.y) + 1,
            cells.Max(cell => cell.z) - cells.Min(cell => cell.z) + 1);
    }

    static void AssertCentrallySymmetricBossHull(
        ModularBossBuildResult build)
    {
        HashSet<Vector3Int> cells = ResolveBossStructureCells(build);
        foreach (Vector3Int cell in cells)
        {
            Assert.That(cells.Contains(new Vector3Int(
                    -1 - cell.x,
                    -1 - cell.y,
                    -1 - cell.z)),
                Is.True,
                $"Missing central mirror for hull cell {cell}.");
        }
    }

    static void AssertNonCubicBossHull(ModularBossBuildResult build)
    {
        Vector3Int span = ResolveBossHullSpan(build);
        int boundingVolume = span.x * span.y * span.z;
        Assert.That(
            boundingVolume,
            Is.GreaterThan(build.StructureModuleCount + 8),
            "A generated hull must not fill its entire bounding cube.");
    }

    static GridModuleDefinition[] CreateBossDefinitions()
    {
        return new[]
        {
            CreateDefinition(
                "core", GridModuleCategory.Core,
                new Vector3Int(2, 2, 2), 100f, 100f, 0f, 1000f),
            CreateDefinition(
                "neox@block:common:block_111",
                GridModuleCategory.Structure,
                Vector3Int.one, 75f, 0f, 0f, 140f),
            CreateDefinition(
                "neox@block:speed:speed_rocketsmall_112",
                GridModuleCategory.MainThruster,
                new Vector3Int(1, 1, 2), 180f, 0f, 0f, 140f, 3000f),
            CreateDefinition(
                "neox@block:common:machinegun_111",
                GridModuleCategory.KineticWeapon,
                Vector3Int.one, 220f, 0f, 5f, 140f)
        };
    }

    static GridModuleDefinition CreateDefinition(
        string id,
        GridModuleCategory category,
        Vector3Int footprint,
        float mass,
        float capacity,
        float cost,
        float integrity,
        float thrust = 0f)
    {
        var definition = ScriptableObject.CreateInstance<GridModuleDefinition>();
        definition.Configure(
            id,
            id,
            category,
            null,
            footprint,
            mass,
            capacity,
            cost,
            integrity,
            thrust,
            null);
        return definition;
    }

    TestDamageable CreateDamageable(
        string name,
        Vector3 position,
        VehicleCombatTeam team)
    {
        GameObject root = CreateRoot(name, position);
        root.AddComponent<BoxCollider>();
        VehicleCombatTeamUtility.SetTeam(root, team);
        return root.AddComponent<TestDamageable>();
    }
}
