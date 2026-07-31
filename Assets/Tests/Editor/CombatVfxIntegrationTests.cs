using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityPlanet.ModularAssembly;
using Object = UnityEngine.Object;

public sealed class CombatVfxIntegrationTests
{
    const string HqRoot =
        "Assets/Resources/CombatFeedback/HQExplosions/";
    const string HqPrefabRoot =
        HqRoot + "Realistic explosions/Prefabs/";

    static readonly string[] HqPrefabs =
    {
        "Explosion7",
        "Explosion10",
        "Explosion14",
        "Explosion20",
        "Explosion21",
        "Explosion25"
    };

    static readonly string[] CombatMaterials =
    {
        "CombatFeedback/NeoX/Materials/NeoX_metal_hit_00",
        "CombatFeedback/NeoX/Materials/NeoX_metal_hit_01",
        "CombatFeedback/NeoX/Materials/NeoX_metal_hit_02",
        "CombatFeedback/NeoX/Materials/NeoX_sniper_hit_02",
        "CombatFeedback/NeoX/Materials/NeoX_sniper_hit_03",
        "CombatFeedback/NeoX/Materials/NeoX_energy_impact_03",
        "CombatFeedback/NeoX/Materials/NeoX_energy_impact_10",
        "CombatFeedback/NeoX/Materials/NeoX_missile_explosion_02",
        "CombatFeedback/NeoX/Materials/NeoX_missile_explosion_04",
        "CombatFeedback/NeoX/Materials/NeoX_missile_explosion_05"
    };

    static readonly string[] HqExplosionMaterials =
    {
        HqRoot + "Realistic explosions/Materials/Explosion7.mat",
        HqRoot + "Realistic explosions/Materials/Explosion9.mat",
        HqRoot + "Realistic explosions/Materials/Explosion10.mat",
        HqRoot + "Realistic explosions/Materials/Explosion12.mat",
        HqRoot + "Realistic explosions/Materials/Explosion13.mat",
        HqRoot + "Realistic explosions/Materials/Trail2.mat",
        HqRoot + "Realistic explosions/Materials/Trail4.mat"
    };

    static readonly string[] HeavyLaserMaterials =
    {
        "Assets/Hovl Studio/HSFiles/Materials/Trail21cg.mat",
        "Assets/Hovl Studio/HSFiles/Materials/Point5cg.mat",
        "Assets/Hovl Studio/HSFiles/Materials/Point12cg.mat",
        "Assets/Hovl Studio/HSFiles/Materials/Circle41cg.mat",
        "Assets/Hovl Studio/HSFiles/Materials/Laser3.mat"
    };

    const string HqExplosionShader =
        "UnityPlanet/HQ Explosions/Built-In Explosion";
    const string HqBlendShader =
        "UnityPlanet/HQ Explosions/Built-In Blend";

    [TestCase(
        "sfx/mc/machinegun_bullet_end.sfx",
        0f,
        CombatWeaponEffectKind.Kinetic)]
    [TestCase(
        "sfx/mc/antiair_cannon_end.sfx",
        1.5f,
        CombatWeaponEffectKind.AntiAir)]
    [TestCase(
        "sfx/mc/snipercannon_bullet_end.sfx",
        0f,
        CombatWeaponEffectKind.Sniper)]
    [TestCase(
        "sfx/mc/energy_cannon_end.sfx",
        2f,
        CombatWeaponEffectKind.Energy)]
    [TestCase(
        "HovlLaserRayHit",
        0f,
        CombatWeaponEffectKind.Energy)]
    [TestCase(
        "Forge3DGuidedMissileImpact",
        5f,
        CombatWeaponEffectKind.Missile)]
    public void ProfileResolver_MapsWeaponFamilies(
        string sourceEffect,
        float gameplayRadius,
        CombatWeaponEffectKind expected)
    {
        CombatWeaponEffectProfile profile =
            CombatWeaponEffectProfile.Resolve(
                sourceEffect,
                gameplayRadius);

        Assert.That(profile.Kind, Is.EqualTo(expected));
        Assert.That(profile.MuzzleFlashSize, Is.GreaterThan(0f));
        Assert.That(profile.ImpactCoreRadius, Is.GreaterThan(0f));
        if (gameplayRadius > 0.1f)
            Assert.That(
                profile.ImpactCoreRadius,
                Is.LessThan(gameplayRadius),
                "The bright core must not misrepresent the full damage radius.");
    }

    [Test]
    public void CombatMaterials_AreLoadedAndShaderClean()
    {
        foreach (string path in CombatMaterials)
        {
            Material material = Resources.Load<Material>(path);
            AssertUsable(material, path);
        }
    }

    [Test]
    public void BuiltInDistortion_UsesPackageLegacyShader()
    {
        Shader shader = Shader.Find("Hovl/Particles/Distortion");
        Assert.That(shader, Is.Not.Null);
        Assert.That(shader.isSupported, Is.True);
        Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        Assert.That(ShaderUtil.GetShaderMessages(shader), Is.Empty);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(
            HqRoot +
            "HSFiles/Materials/DistortionNormal12.mat");
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader, Is.SameAs(shader));
    }

    [Test]
    public void HqMaterials_UseSelfContainedBuiltInShaders()
    {
        Shader explosionShader = Shader.Find(HqExplosionShader);
        Shader blendShader = Shader.Find(HqBlendShader);
        AssertShaderClean(explosionShader, HqExplosionShader);
        AssertShaderClean(blendShader, HqBlendShader);

        foreach (string path in HqExplosionMaterials)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null, path);
            Assert.That(
                material.shader,
                Is.SameAs(explosionShader),
                path);
            Assert.That(
                material.GetTexture("_MainTex"),
                Is.Not.Null,
                path + " has no explosion flipbook.");
        }

        Material explosion13 =
            AssetDatabase.LoadAssetAtPath<Material>(
                HqRoot +
                "Realistic explosions/Materials/Explosion13.mat");
        Assert.That(
            explosion13.GetTexture("_MainTex").name,
            Is.EqualTo("Explosion13"),
            "The legacy _Maintexture slot points at Explosion12; " +
            "the Built-in shader must use the correct _MainTex slot.");

        Material point =
            AssetDatabase.LoadAssetAtPath<Material>(
                HqRoot + "HSFiles/Materials/Point5cg.mat");
        Material snow =
            AssetDatabase.LoadAssetAtPath<Material>(
                HqRoot + "HSFiles/Materials/Snow4bcg.mat");
        Assert.That(point.shader, Is.SameAs(blendShader));
        Assert.That(snow.shader, Is.SameAs(blendShader));
        Assert.That(point.GetFloat("_SrcBlend"), Is.EqualTo(5f));
        Assert.That(point.GetFloat("_DstBlend"), Is.EqualTo(1f));
        Assert.That(snow.GetFloat("_SrcBlend"), Is.EqualTo(5f));
        Assert.That(snow.GetFloat("_DstBlend"), Is.EqualTo(10f));
        Assert.That(
            point.GetFloat("_BUILTIN_SrcBlend"),
            Is.EqualTo(5f));
        Assert.That(
            point.GetFloat("_BUILTIN_DstBlend"),
            Is.EqualTo(1f));
        Assert.That(
            snow.GetFloat("_BUILTIN_SrcBlend"),
            Is.EqualTo(5f));
        Assert.That(
            snow.GetFloat("_BUILTIN_DstBlend"),
            Is.EqualTo(10f));
    }

    [Test]
    public void HeavyLaserPrefab_UsesRenderableBuiltInMaterials()
    {
        Shader blendShader = Shader.Find(HqBlendShader);
        AssertShaderClean(blendShader, HqBlendShader);
        foreach (string path in HeavyLaserMaterials)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null, path);
            Assert.That(material.shader, Is.SameAs(blendShader), path);
            Assert.That(material.GetTexture("_MainTex"), Is.Not.Null, path);
        }

        const string prefabPath =
            "Assets/Resources/WeaponEffects/HovlLaserRay.prefab";
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, prefabPath);
        Renderer[] renderers =
            prefab.GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers, Is.Not.Empty);
        foreach (Renderer renderer in renderers)
        {
            ParticleSystemRenderer particleRenderer =
                renderer as ParticleSystemRenderer;
            if (particleRenderer == null)
            {
                AssertUsable(renderer.sharedMaterial, renderer.name);
                Assert.That(
                    renderer.sharedMaterial.shader,
                    Is.SameAs(blendShader));
                continue;
            }

            ParticleSystem particle =
                particleRenderer.GetComponent<ParticleSystem>();
            bool hasMain = particleRenderer.sharedMaterial != null;
            bool hasTrail =
                particle != null &&
                particle.trails.enabled &&
                particleRenderer.trailMaterial != null;
            Assert.That(
                hasMain || hasTrail,
                Is.True,
                renderer.name + " has no renderable particle layer.");
            if (hasMain)
            {
                AssertUsable(
                    particleRenderer.sharedMaterial,
                    renderer.name);
                Assert.That(
                    particleRenderer.sharedMaterial.shader,
                    Is.SameAs(blendShader));
            }
            if (hasTrail)
            {
                AssertUsable(
                    particleRenderer.trailMaterial,
                    renderer.name + " trail");
                Assert.That(
                    particleRenderer.trailMaterial.shader,
                    Is.SameAs(blendShader));
            }
        }

        Assert.That(
            AssetDatabase.GetDependencies(prefabPath, true)
                .Any(path =>
                    path.EndsWith(
                        "HS_Blend_CG.shadergraph",
                        StringComparison.OrdinalIgnoreCase)),
            Is.False,
            "The heavy laser still depends on the broken depth graph.");
        Assert.That(
            prefab.GetComponentsInChildren<Rigidbody>(true),
            Is.Empty);
        Assert.That(
            prefab.GetComponentsInChildren<Collider>(true),
            Is.Empty);
    }

    [TestCaseSource(nameof(HqPrefabs))]
    public void HqPrefab_HasOnlyUsableActiveMaterials(
        string prefabName)
    {
        string path = HqPrefabRoot + prefabName + ".prefab";
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);

        ParticleSystemRenderer[] renderers =
            prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
        Assert.That(renderers, Is.Not.Empty, path);
        foreach (ParticleSystemRenderer renderer in renderers)
        {
            AssertUsable(renderer.sharedMaterial, renderer.name);
            ParticleSystem particle =
                renderer.GetComponent<ParticleSystem>();
            Assert.That(particle, Is.Not.Null, renderer.name);
            if (particle.trails.enabled)
                AssertUsable(
                    renderer.trailMaterial,
                    renderer.name + " trail");
        }

        Assert.That(
            prefab.GetComponentsInChildren<Rigidbody>(true),
            Is.Empty,
            "VFX prefab must not add rigidbody physics.");
        Assert.That(
            prefab.GetComponentsInChildren<Collider>(true),
            Is.Empty,
            "VFX prefab must not add collision physics.");
        Assert.That(
            prefab.GetComponentsInChildren<Component>(true)
                .Any(component => component == null),
            Is.False,
            "VFX prefab contains a missing script.");
    }

    [TestCaseSource(nameof(HqPrefabs))]
    public void HqPrefab_IsSelfContained(string prefabName)
    {
        string path = HqPrefabRoot + prefabName + ".prefab";
        string[] dependencies =
            AssetDatabase.GetDependencies(path, true);

        Assert.That(
            dependencies.Any(dependency =>
                dependency.StartsWith(
                    "Assets/Hovl Studio/",
                    StringComparison.OrdinalIgnoreCase)),
            Is.False,
            prefabName + " still depends on the ignored source package.");
    }

    [Test]
    public void HitscanTracer_DoesNotUseStaticForgeProjectile()
    {
        var host = new GameObject("TracerTestHost");
        try
        {
            bool handled = Forge3DWeaponPresentation.TrySpawnTracer(
                host.transform,
                Vector3.zero,
                Vector3.forward * 30f,
                "sfx/mc/machinegun_bullet_shoot.sfx",
                0.07f);

            Assert.That(handled, Is.False);
            Assert.That(
                host.GetComponent<Forge3DEffectPool>(),
                Is.Null,
                "The static Forge projectile must not swallow the line tracer.");
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void ForgePool_RejectsMissingAndNonBeamResources()
    {
        var host = new GameObject("ForgeVisibilityContractTest");
        try
        {
            Forge3DEffectPool pool =
                host.AddComponent<Forge3DEffectPool>();
            Assert.That(
                pool.SpawnOneShot(
                    "WeaponEffects/Forge3D/__DefinitelyMissing__",
                    Vector3.zero,
                    Vector3.forward,
                    0.1f,
                    1f),
                Is.False);
            Assert.That(
                pool.SpawnBeam(
                    "WeaponEffects/Forge3D/VulcanImpact",
                    Vector3.zero,
                    Vector3.forward,
                    0.1f),
                Is.False,
                "An impact prefab without LineRenderer cannot suppress " +
                "the reliable tracer fallback.");
            Assert.That(
                pool.SpawnBeam(
                    "WeaponEffects/Forge3D/SniperBeam",
                    Vector3.zero,
                    Vector3.zero,
                    0.1f),
                Is.False,
                "A zero-length beam is not a visible effect.");
        }
        finally
        {
            Object.DestroyImmediate(host);
            DestroyTransientRoot();
        }
    }

    [Test]
    public void RuntimeWeaponFeedback_IsVisibleAndPhysicsFree()
    {
        var root = new GameObject("CombatWeaponEffectPoolTest");
        try
        {
            CombatWeaponEffectPool pool =
                root.AddComponent<CombatWeaponEffectPool>();
            pool.Prewarm();
            Assert.That(pool.IsReady, Is.True);

            Assert.That(
                pool.SpawnMuzzle(
                    Vector3.zero,
                    Vector3.forward,
                    new Color(1f, 0.72f, 0.2f),
                    "sfx/mc/machinegun_bullet_emit.sfx",
                    null),
                Is.True);
            Assert.That(
                pool.SpawnImpact(
                    Vector3.forward * 2f,
                    Vector3.back,
                    new Color(0.2f, 0.9f, 1f),
                    "sfx/mc/energy_cannon_end.sfx",
                    2f),
                Is.True);

            Assert.That(
                root.GetComponentsInChildren<ParticleSystem>(true)
                    .Sum(system => system.particleCount),
                Is.GreaterThan(0));
            Assert.That(
                root.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer =>
                        renderer.enabled &&
                        renderer.gameObject.activeInHierarchy),
                Is.True);
            Assert.That(
                root.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<Collider>(true),
                Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void RuntimeWeaponFeedback_BurstIsBoundedAndReusesSlots()
    {
        var root = new GameObject("CombatWeaponBurstPoolTest");
        try
        {
            CombatWeaponEffectPool pool =
                root.AddComponent<CombatWeaponEffectPool>();
            pool.Prewarm();
            Assert.That(pool.IsReady, Is.True);

            SpawnMissileBurst(pool, 64, 0);
            int particleCount = pool.ParticleSlotCount;
            int lineCount = pool.LineSlotCount;
            int[] particleIds =
                root.GetComponentsInChildren<ParticleSystem>(true)
                    .Select(item => item.GetInstanceID())
                    .OrderBy(item => item)
                    .ToArray();
            int[] lineIds =
                root.GetComponentsInChildren<LineRenderer>(true)
                    .Select(item => item.GetInstanceID())
                    .OrderBy(item => item)
                    .ToArray();

            Assert.That(
                particleCount,
                Is.LessThanOrEqualTo(
                    CombatWeaponEffectPool.MaximumParticleSlots));
            Assert.That(
                lineCount,
                Is.LessThanOrEqualTo(
                    CombatWeaponEffectPool.MaximumLineSlots));

            SpawnMissileBurst(pool, 64, 1000);
            Assert.That(pool.ParticleSlotCount, Is.EqualTo(particleCount));
            Assert.That(pool.LineSlotCount, Is.EqualTo(lineCount));
            Assert.That(
                root.GetComponentsInChildren<ParticleSystem>(true)
                    .Select(item => item.GetInstanceID()),
                Is.EquivalentTo(particleIds));
            Assert.That(
                root.GetComponentsInChildren<LineRenderer>(true)
                    .Select(item => item.GetInstanceID()),
                Is.EquivalentTo(lineIds));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void CombatTransientRoot_ClearVisuals_StopsAllVisualLayers()
    {
        Transform root = CombatTransientRoot.GetOrCreate();
        try
        {
            CombatWeaponEffectPool weaponPool =
                root.GetComponent<CombatWeaponEffectPool>() ??
                root.gameObject.AddComponent<CombatWeaponEffectPool>();
            weaponPool.Prewarm();
            Assert.That(
                weaponPool.SpawnImpact(
                    Vector3.zero,
                    Vector3.up,
                    new Color(1f, 0.45f, 0.12f),
                    "Forge3DGuidedMissileImpact",
                    5f),
                Is.True);

            CombatFeedbackController.GetOrCreate().PlayDestruction(
                new ModuleDestructionFeedbackContext(
                    "cleanup-core",
                    Vector3.forward * 2f,
                    Vector3.up,
                    ModularAssembly.GridModuleCategory.Core,
                    true,
                    new Bounds(Vector3.forward * 2f, Vector3.one * 6f),
                    2));
            Light sentinel = new GameObject("TransientLightSentinel")
                .AddComponent<Light>();
            sentinel.transform.SetParent(root, false);
            sentinel.enabled = true;

            Assert.That(
                root.GetComponentsInChildren<ParticleSystem>(true)
                    .Sum(system => system.particleCount),
                Is.GreaterThan(0));
            Assert.That(
                root.GetComponentsInChildren<LineRenderer>(true)
                    .Any(line => line.enabled),
                Is.True);
            Assert.That(
                root.GetComponentsInChildren<Light>(true)
                    .Any(light => light.enabled),
                Is.True);

            CombatTransientRoot.ClearVisuals();

            Assert.That(
                root.GetComponentsInChildren<ParticleSystem>(true)
                    .All(system =>
                        system.particleCount == 0 &&
                        !system.isPlaying &&
                        !system.IsAlive(true)),
                Is.True);
            Assert.That(
                root.GetComponentsInChildren<LineRenderer>(true)
                    .Any(line => line.enabled),
                Is.False);
            Assert.That(
                root.GetComponentsInChildren<Light>(true)
                    .Any(light => light.enabled),
                Is.False);
            Assert.That(
                root.GetComponentsInChildren<Transform>(true)
                    .Where(item =>
                        item.name.StartsWith(
                            "CombatVfx_",
                            StringComparison.Ordinal) ||
                        item.name.StartsWith(
                            "PooledModuleExplosion_",
                            StringComparison.Ordinal))
                    .Any(item => item.gameObject.activeSelf),
                Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root.gameObject);
        }
    }

    [Test]
    public void ConnectivityCollapse_EmitsVehicleScaleCoreFeedback()
    {
        Transform transientRoot = CombatTransientRoot.GetOrCreate();
        var graphRoot = new GameObject("ConnectivityCollapseGraph");
        try
        {
            VehicleStructureGraph graph =
                graphRoot.AddComponent<VehicleStructureGraph>();
            graph.SetAutomaticReturnToBuild(false);
            CombatFeedbackController controller =
                CombatFeedbackController.GetOrCreate();
            controller.Observe(graph);
            var delta = new VehicleStructureDelta
            {
                DirectHitRuntimeId = "detached-armour",
                HitPoint = graphRoot.transform.position,
                Impulse = Vector3.down
            };
            MethodInfo destroyVehicle =
                typeof(VehicleStructureGraph).GetMethod(
                    "DestroyVehicle",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(destroyVehicle, Is.Not.Null);

            destroyVehicle.Invoke(graph, new object[] { delta });

            Assert.That(graph.LastDestructionDelta, Is.SameAs(delta));
            Assert.That(
                transientRoot.GetComponentsInChildren<Transform>(true)
                    .Any(item =>
                        item.gameObject.activeSelf &&
                        item.name ==
                        "PooledModuleExplosion_Core"),
                Is.True,
                "Connectivity collapse must have a vehicle-scale core cue.");
        }
        finally
        {
            Object.DestroyImmediate(transientRoot.gameObject);
            Object.DestroyImmediate(graphRoot);
        }
    }

    [Test]
    public void VisualTargetRadius_UsesBoundsAndRemainsClamped()
    {
        var catalog = new ModuleDestructionEffectCatalog();
        foreach (ModuleDestructionEffectTier tier in
                 Enum.GetValues(typeof(ModuleDestructionEffectTier)))
        {
            ModuleDestructionEffectDefinition definition =
                catalog.Resolve(tier);
            float minimum = definition.TargetRadius * 0.75f;
            float maximum = definition.TargetRadius *
                            (tier == ModuleDestructionEffectTier.Core
                                ? 1.65f
                                : 1.5f);
            float fallback = definition.ResolveVisualTargetRadius(
                new Bounds(Vector3.zero, Vector3.zero),
                0);
            float tiny = definition.ResolveVisualTargetRadius(
                new Bounds(Vector3.zero, Vector3.one * 0.001f),
                0);
            float medium = definition.ResolveVisualTargetRadius(
                new Bounds(Vector3.zero, Vector3.one),
                0);
            float detached = definition.ResolveVisualTargetRadius(
                new Bounds(Vector3.zero, Vector3.one),
                25);
            float huge = definition.ResolveVisualTargetRadius(
                new Bounds(Vector3.zero, Vector3.one * 1000f),
                100);

            Assert.That(fallback, Is.EqualTo(definition.TargetRadius));
            Assert.That(tiny, Is.InRange(minimum, maximum));
            Assert.That(medium, Is.InRange(minimum, maximum));
            Assert.That(detached, Is.InRange(minimum, maximum));
            Assert.That(detached, Is.GreaterThanOrEqualTo(medium));
            Assert.That(huge, Is.EqualTo(maximum).Within(0.0001f));
            Assert.That(float.IsNaN(huge), Is.False);
            Assert.That(float.IsInfinity(huge), Is.False);
        }
    }

    [Test]
    public void HqRuntimePool_LeavesAtLeastOneRendererVisible()
    {
        var root = new GameObject("CombatFeedbackControllerTest");
        try
        {
            CombatFeedbackController controller =
                root.AddComponent<CombatFeedbackController>();
            controller.PlayDestruction(
                new ModuleDestructionFeedbackContext(
                    "test-functional-module",
                    Vector3.zero,
                    Vector3.up,
                    ModularAssembly.GridModuleCategory.KineticWeapon,
                    false,
                    new Bounds(Vector3.zero, Vector3.one),
                    0));

            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            Assert.That(
                renderers.Any(renderer =>
                    renderer.enabled &&
                    renderer.gameObject.activeInHierarchy),
                Is.True,
                "HQ effect played but every renderer was disabled.");
            foreach (Renderer renderer in renderers.Where(
                         item => item.enabled &&
                                 item.gameObject.activeInHierarchy))
            {
                Material primary =
                    renderer is ParticleSystemRenderer particleRenderer
                        ? particleRenderer.sharedMaterial
                        : renderer.sharedMaterials.FirstOrDefault(
                            item => item != null);
                AssertUsable(primary, renderer.name);
            }
            Assert.That(
                root.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<Collider>(true),
                Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void HqPool_InvalidPrimaryUsesFallbackAndBothInvalidRejects()
    {
        string folderName =
            "__CombatVfxTest_" + Guid.NewGuid().ToString("N");
        string assetFolder = "Assets/Resources/" + folderName;
        GameObject invalidSource = null;
        GameObject fallbackSource = null;
        GameObject fallbackHost = null;
        GameObject rejectionHost = null;
        try
        {
            Assert.That(
                AssetDatabase.CreateFolder(
                    "Assets/Resources",
                    folderName),
                Is.Not.Empty);

            invalidSource = new GameObject("InvalidPrimary");
            ParticleSystem invalidParticle =
                invalidSource.AddComponent<ParticleSystem>();
            invalidParticle.GetComponent<ParticleSystemRenderer>()
                .sharedMaterial = null;
            PrefabUtility.SaveAsPrefabAsset(
                invalidSource,
                assetFolder + "/InvalidPrimary.prefab");

            fallbackSource = new GameObject("ValidFallback");
            ParticleSystem fallbackParticle =
                fallbackSource.AddComponent<ParticleSystem>();
            fallbackParticle.GetComponent<ParticleSystemRenderer>()
                .sharedMaterial =
                Resources.Load<Material>(CombatMaterials[1]);
            PrefabUtility.SaveAsPrefabAsset(
                fallbackSource,
                assetFolder + "/ValidFallback.prefab");
            Object.DestroyImmediate(invalidSource);
            invalidSource = null;
            Object.DestroyImmediate(fallbackSource);
            fallbackSource = null;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);

            Type poolType =
                typeof(CombatFeedbackController).Assembly.GetType(
                    "UnityPlanet.ModularAssembly." +
                    "ModuleDestructionEffectPool",
                    true);
            MethodInfo createSlot = poolType.GetMethod(
                "CreateSlot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(createSlot, Is.Not.Null);

            fallbackHost = new GameObject("HqFallbackPoolTest");
            Component fallbackPool =
                fallbackHost.AddComponent(poolType);
            var fallbackDefinition =
                new ModuleDestructionEffectDefinition(
                    ModuleDestructionEffectTier.Structural,
                    folderName + "/InvalidPrimary",
                    folderName + "/ValidFallback",
                    1.25f,
                    4.5f,
                    1f,
                    1f,
                    0.2f,
                    1);
            object fallbackSlot = createSlot.Invoke(
                fallbackPool,
                new object[] { fallbackDefinition });
            Assert.That(fallbackSlot, Is.Not.Null);
            FieldInfo sourceRadius =
                fallbackSlot.GetType().GetField("SourceRadius");
            Assert.That(sourceRadius, Is.Not.Null);
            Assert.That(
                (float)sourceRadius.GetValue(fallbackSlot),
                Is.EqualTo(4.5f),
                "The valid fallback source radius proves the invalid " +
                "primary did not masquerade as a successful spawn.");

            rejectionHost = new GameObject("HqRejectionPoolTest");
            Component rejectionPool =
                rejectionHost.AddComponent(poolType);
            var rejectionDefinition =
                new ModuleDestructionEffectDefinition(
                    ModuleDestructionEffectTier.Functional,
                    folderName + "/InvalidPrimary",
                    folderName + "/InvalidPrimary",
                    1f,
                    1f,
                    1f,
                    1f,
                    0.2f,
                    1);
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    "\\[Combat VFX\\] No usable Functional " +
                    "module-destruction prefab\\."));
            object rejectedSlot = createSlot.Invoke(
                rejectionPool,
                new object[] { rejectionDefinition });
            Assert.That(rejectedSlot, Is.Null);
        }
        finally
        {
            if (invalidSource != null)
                Object.DestroyImmediate(invalidSource);
            if (fallbackSource != null)
                Object.DestroyImmediate(fallbackSource);
            if (fallbackHost != null)
                Object.DestroyImmediate(fallbackHost);
            if (rejectionHost != null)
                Object.DestroyImmediate(rejectionHost);
            AssetDatabase.DeleteAsset(assetFolder);
            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport);
        }
    }

    static void SpawnMissileBurst(
        CombatWeaponEffectPool pool,
        int count,
        int positionOffset)
    {
        for (int index = 0; index < count; index++)
        {
            Vector3 position = new Vector3(
                positionOffset + index * 0.1f,
                0f,
                0f);
            Assert.That(
                pool.SpawnMuzzle(
                    position,
                    Vector3.forward,
                    new Color(1f, 0.45f, 0.12f),
                    "Forge3DGuidedMissileMuzzle",
                    null),
                Is.True);
            Assert.That(
                pool.SpawnImpact(
                    position + Vector3.forward,
                    Vector3.back,
                    new Color(1f, 0.45f, 0.12f),
                    "Forge3DGuidedMissileImpact",
                    5f),
                Is.True);
        }
    }

    static void DestroyTransientRoot()
    {
        Transform root = GameObject.Find("CombatTransientRoot")?.transform;
        if (root != null)
            Object.DestroyImmediate(root.gameObject);
    }

    static void AssertUsable(Material material, string context)
    {
        Assert.That(material, Is.Not.Null, context);
        AssertShaderClean(material.shader, context);
    }

    static void AssertShaderClean(Shader shader, string context)
    {
        Assert.That(shader, Is.Not.Null, context);
        Assert.That(shader.isSupported, Is.True, context);
        Assert.That(
            shader.name,
            Does.Not.Contain("InternalErrorShader")
                .IgnoreCase,
            context);
        Assert.That(
            ShaderUtil.ShaderHasError(shader),
            Is.False,
            context);
        Assert.That(
            ShaderUtil.GetShaderMessages(shader),
            Is.Empty,
            context);
    }
}
