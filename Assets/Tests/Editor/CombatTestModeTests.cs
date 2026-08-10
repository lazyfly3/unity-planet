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
using Object = UnityEngine.Object;

public sealed class CombatTestModeTests
{
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
        }
        finally
        {
            foreach (GridModuleDefinition definition in definitions)
                Object.DestroyImmediate(definition);
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
    public void ModularBossMissionAlwaysRequiresAnUrbanBattlefield()
    {
        FinitePlanetMissionRules boss =
            FinitePlanetMissionRules.Resolve("modular_boss", 5);
        Assert.That(boss.RequiresUrbanEnvironment, Is.True);

        for (int seed = -7; seed <= 7; seed++)
        {
            Assert.That(
                PlanetMissionEnvironmentResolver.Resolve(
                    seed,
                    "planet-" + seed,
                    "modular_boss"),
                Is.EqualTo(PlanetMissionEnvironmentKind.Urban));
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
    public void ModularBossCityHuntPolicyRewardsCoverAndReadableDodges()
    {
        Assert.That(ModularBossCombatPolicy.InitialSpawnDistance(0),
            Is.EqualTo(310f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.InitialSpawnDistance(5),
            Is.EqualTo(270f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.PursuitForceMultiplier,
            Is.EqualTo(1.18f).Within(0.001f));
        Assert.That(ModularBossCombatPolicy.PlayerRamTelegraphSeconds,
            Is.GreaterThanOrEqualTo(0.6f));
        Assert.That(ModularBossCombatPolicy.PlayerRamChargeSeconds,
            Is.GreaterThan(ModularBossCombatPolicy.PlayerRamTelegraphSeconds));
        Assert.That(ModularBossCombatPolicy.CoverBreachDelay(0),
            Is.GreaterThan(ModularBossCombatPolicy.CoverBreachDelay(5)));

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
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        roots.Add(ground);
        ground.name = "NavigationGround";
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        Physics.SyncTransforms();
        var navigation = new HordeAirNavigationService();
        IEnumerator build = navigation.Build(
            Vector3.zero,
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
            new Vector3(-100f, 80f, -100f),
            new Vector3(100f, 80f, 100f),
            path),
            Is.True);
        Assert.That(path.Count, Is.GreaterThan(0));
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
