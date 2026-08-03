using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.ModularAssembly;
using Object = UnityEngine.Object;

public sealed class CombatTestModeTests
{
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
    public void HordePhaseScheduleUsesLockedCapsAndIntervals()
    {
        Assert.That(HordeCombatDirector.PhaseAt(0f), Is.EqualTo(1));
        Assert.That(HordeCombatDirector.PhaseAt(44.99f), Is.EqualTo(1));
        Assert.That(HordeCombatDirector.PhaseAt(45f), Is.EqualTo(2));
        Assert.That(HordeCombatDirector.PhaseAt(179.99f), Is.EqualTo(2));
        Assert.That(HordeCombatDirector.PhaseAt(180f), Is.EqualTo(3));
        Assert.That(HordeCombatDirector.ActiveCapAt(0f), Is.EqualTo(4));
        Assert.That(HordeCombatDirector.ActiveCapAt(45f), Is.EqualTo(8));
        Assert.That(HordeCombatDirector.ActiveCapAt(180f), Is.EqualTo(10));
        Assert.That(HordeCombatDirector.SpawnIntervalAt(0f), Is.EqualTo(8f));
        Assert.That(HordeCombatDirector.SpawnIntervalAt(45f), Is.EqualTo(6f));
        Assert.That(HordeCombatDirector.SpawnIntervalAt(180f), Is.EqualTo(5f));
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

    GameObject CreateRoot(string name, Vector3 position)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        roots.Add(root);
        return root;
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
