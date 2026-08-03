using System;
using System.Collections.Generic;
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

    static readonly string[] ForgeSelectedEffectResources =
    {
        "ExplosionSmall",
        "FighterMissileExplosion",
        "MissileExplosion",
        "MissileFlame",
        "MissileSmokeTrail",
        "PlasmaImpact",
        "PlasmaMuzzle",
        "SeekerFlare",
        "SeekerMuzzle",
        "SniperBeam",
        "SniperImpact",
        "SoloMuzzle",
        "VulcanImpact",
        "VulcanMuzzle"
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
    public void CameraDepthRequest_PreservesExistingFlags()
    {
        var root = new GameObject("CombatVfxDepthCameraTest");
        try
        {
            Camera camera = root.AddComponent<Camera>();
            camera.depthTextureMode = DepthTextureMode.DepthNormals;
            DepthTextureMode before = camera.depthTextureMode;
            Assert.That(
                before & DepthTextureMode.Depth,
                Is.EqualTo(DepthTextureMode.None));

            CombatTransientRoot.EnsureCameraDepthTexture(camera);

            Assert.That(
                camera.depthTextureMode,
                Is.EqualTo(before | DepthTextureMode.Depth));
            Assert.That(
                camera.depthTextureMode & before,
                Is.EqualTo(before));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
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
    public void HqParticleAtlasUv_RemainsSingleMapped()
    {
        foreach (string path in HqExplosionMaterials)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null, path);
            Assert.That(material.HasProperty("_TilingXY"), Is.True, path);
            Vector4 tiling = material.GetVector("_TilingXY");
            Assert.That(
                tiling.x,
                Is.EqualTo(1f).Within(0.0001f),
                path + " would slice an already-atlased particle UV.");
            Assert.That(
                tiling.y,
                Is.EqualTo(1f).Within(0.0001f),
                path + " would slice an already-atlased particle UV.");
        }

        var usedMaterials =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string prefabName in HqPrefabs)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    HqPrefabRoot + prefabName + ".prefab");
            Assert.That(prefab, Is.Not.Null, prefabName);
            foreach (ParticleSystemRenderer renderer in
                     prefab.GetComponentsInChildren<
                         ParticleSystemRenderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material == null ||
                    material.shader == null ||
                    material.shader.name != HqExplosionShader)
                    continue;

                string materialPath =
                    AssetDatabase.GetAssetPath(material);
                usedMaterials.Add(materialPath);
                ParticleSystem particle =
                    renderer.GetComponent<ParticleSystem>();
                Assert.That(particle, Is.Not.Null, materialPath);
                ParticleSystem.TextureSheetAnimationModule sheet =
                    particle.textureSheetAnimation;
                Assert.That(
                    sheet.enabled,
                    Is.True,
                    materialPath + " must receive its atlas frame from " +
                    "the Particle System.");
                Assert.That(
                    sheet.numTilesX * sheet.numTilesY,
                    Is.GreaterThan(1),
                    materialPath + " is expected to be an animated atlas.");

                var streams =
                    new List<ParticleSystemVertexStream>();
                renderer.GetActiveVertexStreams(streams);
                Assert.That(
                    streams,
                    Does.Contain(ParticleSystemVertexStream.UV),
                    materialPath);
                Assert.That(
                    streams,
                    Does.Contain(ParticleSystemVertexStream.Custom1XY),
                    materialPath);
                Assert.That(
                    streams.Contains(
                        ParticleSystemVertexStream.UV2),
                    Is.False,
                    materialPath);
                Assert.That(
                    streams.Contains(
                        ParticleSystemVertexStream.AnimBlend),
                    Is.False,
                    materialPath);
            }
        }

        Assert.That(
            usedMaterials,
            Is.EquivalentTo(HqExplosionMaterials),
            "Every replacement material must be exercised by a selected " +
            "HQ prefab without a second atlas transform.");
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
        Assert.That(
            prefab.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "Hit"),
            Is.True,
            "A renderable beam must retain its authored hit endpoint cue.");
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
    public void FallbackTracer_IsLayeredReadableAndPresentationOnly()
    {
        DestroyTransientRoot();
        var host = new GameObject("ReadableTracerTestHost");
        WeaponVisualPool pool = null;
        try
        {
            pool = host.AddComponent<WeaponVisualPool>();
            InitializeWeaponVisualPool(pool);
            pool.SpawnTracer(
                Vector3.zero,
                Vector3.forward * 160f,
                new Color(0.2f, 0.75f, 1f),
                0.11f,
                "ReadabilityFallback");

            Transform transientRoot = CombatTransientRoot.GetOrCreate();
            LineRenderer core = transientRoot
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.gameObject.name ==
                    "Tracer_ReadabilityFallback");
            LineRenderer halo = core.transform
                .Find("TracerHalo")
                ?.GetComponent<LineRenderer>();

            Assert.That(halo, Is.Not.Null);
            Assert.That(core.startWidth, Is.GreaterThanOrEqualTo(0.17f));
            Assert.That(
                halo.startWidth,
                Is.GreaterThan(core.startWidth * 2.5f));
            Assert.That(core.startColor.r, Is.GreaterThan(0.7f));
            AssertUsable(core.sharedMaterial, "tracer core");
            AssertUsable(halo.sharedMaterial, "tracer halo");
            Assert.That(
                core.GetComponentsInParent<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                core.GetComponentsInChildren<Collider>(true),
                Is.Empty);
        }
        finally
        {
            DisposeWeaponVisualPool(pool);
            Object.DestroyImmediate(host);
            DestroyTransientRoot();
        }
    }

    [Test]
    public void ReceivedHitPresentation_DoesNotDrawOrangeModuleFrame()
    {
        MethodInfo removedFrame =
            typeof(VehicleDamageFeedbackPresenter).GetMethod(
                "DrawModuleMarker",
                BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo vignette =
            typeof(VehicleDamageFeedbackPresenter).GetMethod(
                "DrawDamageVignette",
                BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(
            removedFrame,
            Is.Null,
            "The world-space orange module frame was reintroduced.");
        Assert.That(vignette, Is.Not.Null);
    }

    [Test]
    public void DamageImpulse_IsBoundedAndDoesNotTouchVehiclePhysics()
    {
        var cameraRoot = new GameObject("DamageImpulseCamera");
        var vehicle = new GameObject("DamageImpulseVehicle");
        try
        {
            Camera camera = cameraRoot.AddComponent<Camera>();
            Rigidbody body = vehicle.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = new Vector3(12f, -3f, 45f);
            GridLabCameraController controller =
                vehicle.AddComponent<GridLabCameraController>();
            controller.Initialize(camera, vehicle.transform);
            Vector3 velocityBefore = body.velocity;

            controller.AddDamageImpulse(Vector3.right, 1f);
            MethodInfo resolver = typeof(GridLabCameraController).GetMethod(
                "ResolveDamageKickRotation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(resolver, Is.Not.Null);
            Quaternion kick = (Quaternion)resolver.Invoke(controller, null);

            Assert.That(Quaternion.Angle(Quaternion.identity, kick),
                Is.GreaterThan(0.15f));
            Assert.That(Quaternion.Angle(Quaternion.identity, kick),
                Is.LessThan(3.5f));
            Assert.That(body.velocity, Is.EqualTo(velocityBefore));
        }
        finally
        {
            Object.DestroyImmediate(cameraRoot);
            Object.DestroyImmediate(vehicle);
        }
    }

    [Test]
    public void AppliedEnemyDamage_EmitsOnePlayerHitConfirmation()
    {
        var source = new GameObject("PlayerDamageSource");
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        int feedbackCount = 0;
        CombatDamageAppliedFeedback observed = default;
        Action<CombatDamageAppliedFeedback> handler = feedback =>
        {
            feedbackCount++;
            observed = feedback;
        };
        try
        {
            VehicleCombatTeamUtility.SetTeam(
                source,
                VehicleCombatTeam.Player);
            VehicleCombatTeamUtility.SetTeam(
                target,
                VehicleCombatTeam.Enemy);
            CombatFeedbackTestDamageable damageable =
                target.AddComponent<CombatFeedbackTestDamageable>();
            CombatDamageFeedbackBus.DamageApplied += handler;

            WeaponDamageUtility.ApplyDirect(
                target.GetComponent<Collider>(),
                25f,
                target.transform.position,
                Vector3.forward,
                source);

            Assert.That(feedbackCount, Is.EqualTo(1));
            Assert.That(
                observed.SourceTeam,
                Is.EqualTo(VehicleCombatTeam.Player));
            Assert.That(
                observed.TargetTeam,
                Is.EqualTo(VehicleCombatTeam.Enemy));
            Assert.That(observed.DamageAmount, Is.EqualTo(25f));
            Assert.That(observed.Destroyed, Is.False);
            Assert.That(damageable.Integrity, Is.EqualTo(75f));
        }
        finally
        {
            CombatDamageFeedbackBus.DamageApplied -= handler;
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void WeaponFallback_UsesSupportedBuiltInShader()
    {
        MethodInfo resolver = typeof(WeaponVisualPool).GetMethod(
            "ResolveRuntimeUnlitShader",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(resolver, Is.Not.Null);

        Shader shader = resolver.Invoke(null, null) as Shader;
        Assert.That(shader, Is.Not.Null);
        Assert.That(shader.isSupported, Is.True);
        Assert.That(
            shader.name,
            Does.Not.Contain("Universal Render Pipeline")
                .IgnoreCase);
        Assert.That(
            shader.name,
            Does.Not.Contain("InternalErrorShader")
                .IgnoreCase);
    }

    [Test]
    public void WeaponVisualPool_ClearReusesAndDestroyRemovesOwnedVisuals()
    {
        DestroyTransientRoot();
        var host = new GameObject("WeaponVisualPoolLifecycleTest");
        Transform root = CombatTransientRoot.GetOrCreate();
        WeaponVisualPool pool = null;
        try
        {
            pool = host.AddComponent<WeaponVisualPool>();
            InitializeWeaponVisualPool(pool);
            pool.Prewarm();
            MethodInfo acquireParticles =
                typeof(WeaponVisualPool).GetMethod(
                    "AcquireParticles",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(acquireParticles, Is.Not.Null);
            Assert.That(
                acquireParticles.Invoke(pool, null),
                Is.Not.Null);

            pool.SpawnTracer(
                Vector3.zero,
                Vector3.forward * 12f,
                Color.white,
                0.2f,
                "LifecycleFallback");
            LineRenderer tracer = root
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.gameObject.name ==
                    "Tracer_LifecycleFallback");
            int tracerId = tracer.gameObject.GetInstanceID();
            Assert.That(tracer.gameObject.activeSelf, Is.True);
            Assert.That(tracer.enabled, Is.True);

            CombatTransientRoot.ClearVisuals();

            Assert.That(tracer.gameObject.activeSelf, Is.False);
            Assert.That(tracer.enabled, Is.False);
            pool.SpawnTracer(
                Vector3.right,
                Vector3.right + Vector3.forward * 12f,
                Color.white,
                0.2f,
                "LifecycleFallback");
            LineRenderer reused = root
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.gameObject.name ==
                    "Tracer_LifecycleFallback");
            Assert.That(
                reused.gameObject.GetInstanceID(),
                Is.EqualTo(tracerId));
            Assert.That(reused.gameObject.activeSelf, Is.True);
            Assert.That(reused.enabled, Is.True);

            MethodInfo destroyCallback =
                typeof(WeaponVisualPool).GetMethod(
                    "OnDestroy",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(destroyCallback, Is.Not.Null);
            destroyCallback.Invoke(pool, null);
            Object.DestroyImmediate(host);
            host = null;

            Assert.That(
                root.Cast<Transform>()
                    .Any(item =>
                        item.name.StartsWith(
                            "Tracer_",
                            StringComparison.Ordinal) ||
                        item.name == "WeaponTracer" ||
                        item.name == "WeaponParticles" ||
                        item.name == "HeavyLaser_HovlRay"),
                Is.False,
                "Destroying the owning pool left global visual children.");
        }
        finally
        {
            DisposeWeaponVisualPool(pool);
            if (host != null)
                Object.DestroyImmediate(host);
            DestroyTransientRoot();
        }
    }

    [Test]
    public void HeavyLaser_DistanceScaleDoesNotInstantiateMaterial()
    {
        DestroyTransientRoot();
        var host = new GameObject("HeavyLaserMaterialOwnershipTest");
        WeaponVisualPool pool = null;
        try
        {
            GameObject prefab =
                Resources.Load<GameObject>("WeaponEffects/HovlLaserRay");
            Assert.That(prefab, Is.Not.Null);
            Material assetMaterial =
                prefab.GetComponent<LineRenderer>().sharedMaterial;
            AssertUsable(assetMaterial, "HovlLaserRay shared beam");

            pool = host.AddComponent<WeaponVisualPool>();
            InitializeWeaponVisualPool(pool);
            pool.Prewarm();
            const float distance = 37f;
            pool.SpawnContinuousLaser(
                Vector3.zero,
                Vector3.forward * distance,
                Vector3.back,
                true,
                0.2f);

            LineRenderer line = GameObject
                .Find("HeavyLaser_HovlRay")
                .GetComponent<LineRenderer>();
            Assert.That(line.sharedMaterial, Is.SameAs(assetMaterial));
            var properties = new MaterialPropertyBlock();
            line.GetPropertyBlock(properties);
            Assert.That(
                properties.GetVector("_MainTex_ST").x,
                Is.EqualTo(distance).Within(0.001f));
            Assert.That(
                properties.GetVector("_Noise_ST").x,
                Is.EqualTo(distance).Within(0.001f));
        }
        finally
        {
            DisposeWeaponVisualPool(pool);
            Object.DestroyImmediate(host);
            DestroyTransientRoot();
        }
    }

    [Test]
    public void WeaponVisualPool_RecreatesAfterTransientRootDestroyed()
    {
        DestroyTransientRoot();
        var host = new GameObject("WeaponVisualPoolRootRecoveryTest");
        WeaponVisualPool pool = null;
        try
        {
            pool = host.AddComponent<WeaponVisualPool>();
            InitializeWeaponVisualPool(pool);
            pool.Prewarm();
            Transform oldRoot = CombatTransientRoot.GetOrCreate();
            Object.DestroyImmediate(oldRoot.gameObject);

            Assert.DoesNotThrow(() =>
                pool.SpawnTracer(
                    Vector3.zero,
                    Vector3.forward * 9f,
                    Color.white,
                    0.2f,
                    "RecoveredRoot"));
            Transform newRoot = CombatTransientRoot.GetOrCreate();
            Assert.That(newRoot, Is.Not.SameAs(oldRoot));
            LineRenderer tracer = newRoot
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.gameObject.name == "Tracer_RecoveredRoot");
            Assert.That(tracer.gameObject.activeSelf, Is.True);
            Assert.That(tracer.enabled, Is.True);
        }
        finally
        {
            DisposeWeaponVisualPool(pool);
            Object.DestroyImmediate(host);
            DestroyTransientRoot();
        }
    }

    [Test]
    public void ForgeAnimator_RejectsGeneratedLodAndCollisionRenderers()
    {
        MethodInfo shouldClone =
            typeof(Forge3DWeaponAnimator).GetMethod(
                "ShouldClone",
                BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(shouldClone, Is.Not.Null);

        const string prefabPath =
            "Assets/ModularAssemblyLab/Generated/KineticWeapon.prefab";
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, prefabPath);
        MeshRenderer[] generated =
            prefab.GetComponentsInChildren<MeshRenderer>(true);
        Assert.That(
            generated.Select(item => item.name),
            Is.EquivalentTo(
                new[]
                {
                    "Collision",
                    "Render_LOD0",
                    "Render_LOD1",
                    "Render_LOD2"
                }));
        foreach (MeshRenderer renderer in generated)
        {
            Assert.That(
                (bool)shouldClone.Invoke(
                    null,
                    new object[] { renderer }),
                Is.False,
                renderer.name +
                " must not be cloned outside the generated LOD model.");
        }

        var synthetic = new GameObject("ForgeAnimatorFilterTest");
        try
        {
            MeshRenderer barrel =
                new GameObject("MainBarrel")
                    .AddComponent<MeshRenderer>();
            barrel.transform.SetParent(synthetic.transform, false);
            Assert.That(
                (bool)shouldClone.Invoke(
                    null,
                    new object[] { barrel }),
                Is.True,
                "An explicit non-LOD recoil pivot remains eligible.");

            GameObject lodRoot =
                new GameObject("ExplicitLodContainer");
            lodRoot.transform.SetParent(synthetic.transform, false);
            lodRoot.AddComponent<LODGroup>();
            MeshRenderer lodBarrel =
                new GameObject("MainBarrel_LOD0")
                    .AddComponent<MeshRenderer>();
            lodBarrel.transform.SetParent(lodRoot.transform, false);
            Assert.That(
                (bool)shouldClone.Invoke(
                    null,
                    new object[] { lodBarrel }),
                Is.False,
                "Even an explicitly named barrel must stay owned by its " +
                "LODGroup.");
        }
        finally
        {
            Object.DestroyImmediate(synthetic);
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
    public void ForgePool_BurstIsBoundedReusedAndCleared()
    {
        DestroyTransientRoot();
        Transform root = CombatTransientRoot.GetOrCreate();
        try
        {
            Forge3DEffectPool pool =
                root.gameObject.AddComponent<Forge3DEffectPool>();
            const string resource =
                "WeaponEffects/Forge3D/VulcanMuzzle";
            for (int index = 0;
                 index < Forge3DEffectPool.MaximumSlots;
                 index++)
            {
                Assert.That(
                    pool.SpawnOneShot(
                        resource,
                        new Vector3(index * 0.1f, 0f, 4f),
                        Vector3.forward,
                        10f,
                        0.2f),
                    Is.True,
                    "Forge pool rejected slot " + index + ".");
            }

            Assert.That(
                pool.SlotCount,
                Is.EqualTo(Forge3DEffectPool.MaximumSlots));
            int[] originalIds = root
                .Cast<Transform>()
                .Where(item =>
                    item.name.StartsWith(
                        "Forge3D_",
                        StringComparison.Ordinal))
                .Select(item => item.GetInstanceID())
                .OrderBy(item => item)
                .ToArray();
            Assert.That(
                originalIds.Length,
                Is.EqualTo(Forge3DEffectPool.MaximumSlots));

            Assert.That(
                pool.SpawnOneShot(
                    resource,
                    Vector3.forward * 8f,
                    Vector3.forward,
                    10f,
                    0.2f),
                Is.True,
                "The oldest same-resource slot should be reused.");
            Assert.That(
                pool.SlotCount,
                Is.EqualTo(Forge3DEffectPool.MaximumSlots));
            Assert.That(
                root.Cast<Transform>()
                    .Where(item =>
                        item.name.StartsWith(
                            "Forge3D_",
                            StringComparison.Ordinal))
                    .Select(item => item.GetInstanceID()),
                Is.EquivalentTo(originalIds));

            CombatTransientRoot.ClearVisuals();
            Assert.That(
                root.Cast<Transform>()
                    .Where(item =>
                        item.name.StartsWith(
                            "Forge3D_",
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
    public void ForgeSelectedResources_AllActiveRenderersUseSupportedMaterials()
    {
        DestroyTransientRoot();
        Transform root = CombatTransientRoot.GetOrCreate();
        try
        {
            Forge3DEffectPool pool =
                root.gameObject.AddComponent<Forge3DEffectPool>();
            foreach (string name in ForgeSelectedEffectResources)
            {
                Assert.That(
                    pool.SpawnOneShot(
                        "WeaponEffects/Forge3D/" + name,
                        Vector3.zero,
                        Vector3.forward,
                        0.1f,
                        1f),
                    Is.True,
                    name +
                    " contains no fully valid active renderer set.");
            }
            Assert.That(
                pool.SpawnBeam(
                    "WeaponEffects/Forge3D/SniperBeam",
                    Vector3.left * 4f,
                    Vector3.right * 4f,
                    0.1f),
                Is.True,
                "The selected beam must pass the same all-renderer contract.");
        }
        finally
        {
            DestroyTransientRoot();
        }
    }

    [Test]
    public void PlasmaProjectile_ValidatedVisualOwnsBodyAndInvalidSetFallsBack()
    {
        const string plasmaResource =
            "WeaponEffects/Forge3D/PlasmaProjectile";
        DestroyTransientRoot();
        GameObject projectileRoot =
            GameObject.CreatePrimitive(PrimitiveType.Cube);
        HashSet<string> loggedResources = null;
        try
        {
            Collider collider = projectileRoot.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);
            WeaponProjectile projectile =
                projectileRoot.AddComponent<WeaponProjectile>();
            projectile.Initialize(null);
            var profile = new WeaponProfile
            {
                displayName = "Plasma Contract Test",
                delivery = WeaponDeliveryKind.PhysicalProjectile,
                projectileSpeed = 40f,
                range = 120f,
                effectColor = Color.cyan,
                projectileEffect = "energy_cannon_projectile"
            };
            projectile.Launch(
                Vector3.zero,
                Vector3.forward,
                Vector3.zero,
                profile,
                null,
                null,
                null);

            Renderer body = projectileRoot.GetComponent<Renderer>();
            GameObject validVisual = projectileRoot
                .GetComponentsInChildren<Transform>(true)
                .Single(item =>
                    item.name == "Forge3D_ProjectileVisual")
                .gameObject;
            Assert.That(validVisual.activeInHierarchy, Is.True);
            Assert.That(
                Forge3DEffectPool.HasRenderableRendererSet(validVisual),
                Is.True);
            Assert.That(body.enabled, Is.False);
            int validVisualId = validVisual.GetInstanceID();

            Assert.That(
                Forge3DWeaponPresentation.TryGetProjectileVisual(
                    profile.projectileEffect,
                    out string resource,
                    out _),
                Is.True);
            Assert.That(
                resource,
                Is.EqualTo(plasmaResource));

            var plainProfile = new WeaponProfile
            {
                displayName = "Plain Projectile Contract Test",
                delivery = WeaponDeliveryKind.PhysicalProjectile,
                projectileSpeed = 40f,
                range = 120f,
                effectColor = Color.white,
                projectileEffect = "plain_projectile"
            };
            projectile.Launch(
                Vector3.zero,
                Vector3.forward,
                Vector3.zero,
                plainProfile,
                null,
                null,
                null);
            Assert.That(validVisual.activeSelf, Is.False);
            Assert.That(body.enabled, Is.True);

            projectile.Launch(
                Vector3.zero,
                Vector3.forward,
                Vector3.zero,
                profile,
                null,
                null,
                null);
            GameObject reusedVisual = projectileRoot
                .GetComponentsInChildren<Transform>(true)
                .Single(item =>
                    item.name == "Forge3D_ProjectileVisual")
                .gameObject;
            Assert.That(
                reusedVisual.GetInstanceID(),
                Is.EqualTo(validVisualId));
            Assert.That(reusedVisual.activeInHierarchy, Is.True);
            Assert.That(
                Forge3DEffectPool.HasRenderableRendererSet(reusedVisual),
                Is.True);
            Assert.That(body.enabled, Is.False);

            Object.DestroyImmediate(reusedVisual);
            var invalidVisual =
                new GameObject("Forge3D_ProjectileVisual");
            invalidVisual.transform.SetParent(
                projectileRoot.transform,
                false);
            invalidVisual.AddComponent<MeshRenderer>();
            Type projectileType = typeof(WeaponProjectile);
            projectileType.GetField(
                    "flightVisual",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(projectile, invalidVisual);
            projectileType.GetField(
                    "flightVisualResource",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(projectile, resource);
            projectileType.GetField(
                    "flightVisualParticles",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(
                    projectile,
                    Array.Empty<ParticleSystem>());
            body.enabled = false;

            loggedResources =
                (HashSet<string>)projectileType.GetField(
                        "InvalidFlightVisualErrors",
                        BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            loggedResources.Remove(resource);
            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    "\\[Combat VFX\\] Projectile visual .*" +
                    "contains an active renderer"));
            projectileType.GetMethod(
                    "ConfigureFlightVisual",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(projectile, null);

            Assert.That(body.enabled, Is.True);
            Assert.That(invalidVisual == null, Is.True);
            Assert.That(
                projectileType.GetField(
                        "flightVisual",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(projectile),
                Is.Null);
        }
        finally
        {
            loggedResources?.Remove(plasmaResource);
            if (projectileRoot != null)
                Object.DestroyImmediate(projectileRoot);
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
    public void FloatingOrigin_CachedLineGeometrySurvivesNextUpdate()
    {
        DestroyTransientRoot();
        Transform root = CombatTransientRoot.GetOrCreate();
        root.position = Vector3.zero;
        try
        {
            CombatWeaponEffectPool weaponPool =
                root.gameObject.AddComponent<CombatWeaponEffectPool>();
            weaponPool.Prewarm();
            Assert.That(
                weaponPool.SpawnImpact(
                    new Vector3(3f, 2f, 5f),
                    Vector3.up,
                    new Color(1f, 0.45f, 0.12f),
                    "Forge3DGuidedMissileImpact",
                    5f),
                Is.True);

            Type destructionPoolType =
                typeof(CombatFeedbackController).Assembly.GetType(
                    "UnityPlanet.ModularAssembly." +
                    "ModuleDestructionEffectPool",
                    true);
            Component destructionPool =
                root.gameObject.AddComponent(destructionPoolType);
            MethodInfo spawnSever =
                destructionPoolType.GetMethod(
                    "SpawnSeverFlash",
                    BindingFlags.Instance | BindingFlags.Public);
            Assert.That(spawnSever, Is.Not.Null);
            spawnSever.Invoke(
                destructionPool,
                new object[]
                {
                    new Vector3(-4f, 1f, 7f),
                    Vector3.forward,
                    0f
                });

            LineRenderer pressure = root
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.enabled &&
                    item.gameObject.name ==
                    "CombatVfx_PressureRing");
            LineRenderer sever = root
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.enabled &&
                    item.gameObject.name == "SeverFlash");
            Vector3 pressureBefore = AverageLinePosition(pressure);
            Vector3 severBefore = AverageLinePosition(sever);

            MonoBehaviour driver = root
                .GetComponents<MonoBehaviour>()
                .Single(item =>
                    item.GetType().Name ==
                    "CombatTransientRootDriver");
            Type driverType = driver.GetType();
            driverType.GetField(
                    "previousPosition",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(driver, root.position);
            MethodInfo lateUpdate = driverType.GetMethod(
                "LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(lateUpdate, Is.Not.Null);

            Vector3 delta = new Vector3(137f, -19f, 83f);
            root.position += delta;
            lateUpdate.Invoke(driver, null);
            AssertVectorNear(
                AverageLinePosition(pressure),
                pressureBefore + delta,
                "Pressure ring did not follow the origin shift.");
            AssertVectorNear(
                AverageLinePosition(sever),
                severBefore + delta,
                "Sever flash did not follow the origin shift.");

            typeof(CombatWeaponEffectPool).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(weaponPool, null);
            destructionPoolType.GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(destructionPool, null);
            AssertVectorNear(
                AverageLinePosition(pressure),
                pressureBefore + delta,
                "Pressure-ring cache restored its pre-shift center.");
            AssertVectorNear(
                AverageLinePosition(sever),
                severBefore + delta,
                "Sever-flash cache restored its pre-shift center.");
        }
        finally
        {
            Object.DestroyImmediate(root.gameObject);
        }
    }

    [Test]
    public void FloatingOrigin_SameFramePostRebaseSpawnIsNotDoubleShifted()
    {
        DestroyTransientRoot();
        Transform root = CombatTransientRoot.GetOrCreate();
        var worldHost = new GameObject("CombatVfxRebaseWorld");
        var visualHost = new GameObject("CombatVfxRebaseVisualPool");
        Material skyboxBefore = RenderSettings.skybox;
        WeaponVisualPool pool = null;
        try
        {
            pool = visualHost.AddComponent<WeaponVisualPool>();
            InitializeWeaponVisualPool(pool);
            pool.Prewarm();
            Vector3 oldStart = new Vector3(640f, 2f, 30f);
            Vector3 oldEnd = oldStart + Vector3.forward * 20f;
            pool.SpawnTracer(
                oldStart,
                oldEnd,
                Color.white,
                1f,
                "PreRebase");
            LineRenderer oldTracer = root
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.gameObject.name == "Tracer_PreRebase");
            Vector3 oldCenter = AverageLinePosition(oldTracer);

            InfinitePlanarSurfaceWorld world =
                worldHost.AddComponent<InfinitePlanarSurfaceWorld>();
            MethodInfo shiftRoot =
                typeof(InfinitePlanarSurfaceWorld).GetMethod(
                    "ShiftRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(shiftRoot, Is.Not.Null);
            Vector3 shift = new Vector3(512f, 0f, 256f);
            shiftRoot.Invoke(world, new object[] { root, shift });
            AssertVectorNear(
                AverageLinePosition(oldTracer),
                oldCenter - shift,
                "Pre-rebase tracer was not shifted synchronously.");

            Vector3 newStart = new Vector3(32f, 3f, 18f);
            Vector3 newEnd = newStart + Vector3.forward * 14f;
            pool.SpawnTracer(
                newStart,
                newEnd,
                Color.cyan,
                1f,
                "PostRebase");
            LineRenderer newTracer = root
                .GetComponentsInChildren<LineRenderer>(true)
                .Single(item =>
                    item.gameObject.name == "Tracer_PostRebase");
            Vector3 newCenter = AverageLinePosition(newTracer);

            MonoBehaviour driver = root
                .GetComponents<MonoBehaviour>()
                .Single(item =>
                    item.GetType().Name ==
                    "CombatTransientRootDriver");
            MethodInfo lateUpdate = driver.GetType().GetMethod(
                "LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(lateUpdate, Is.Not.Null);
            lateUpdate.Invoke(driver, null);

            AssertVectorNear(
                AverageLinePosition(oldTracer),
                oldCenter - shift,
                "LateUpdate shifted the old tracer twice.");
            AssertVectorNear(
                AverageLinePosition(newTracer),
                newCenter,
                "A same-frame post-rebase tracer was shifted twice.");
        }
        finally
        {
            DisposeWeaponVisualPool(pool);
            Object.DestroyImmediate(visualHost);
            Object.DestroyImmediate(worldHost);
            RenderSettings.skybox = skyboxBefore;
            DestroyTransientRoot();
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
                            StringComparison.Ordinal) ||
                        item.name.StartsWith(
                            "Forge3D_",
                            StringComparison.Ordinal) ||
                        item.name == "HeavyLaser_HovlRay" ||
                        item.name == "WeaponTracer" ||
                        item.name.StartsWith(
                            "Tracer_",
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

    static void InitializeWeaponVisualPool(WeaponVisualPool pool)
    {
        MethodInfo awake = typeof(WeaponVisualPool).GetMethod(
            "Awake",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(awake, Is.Not.Null);
        awake.Invoke(pool, null);
    }

    static void DisposeWeaponVisualPool(WeaponVisualPool pool)
    {
        if (pool == null)
            return;
        MethodInfo onDestroy = typeof(WeaponVisualPool).GetMethod(
            "OnDestroy",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(onDestroy, Is.Not.Null);
        onDestroy.Invoke(pool, null);
    }

    static Vector3 AverageLinePosition(LineRenderer line)
    {
        var positions = new Vector3[line.positionCount];
        int count = line.GetPositions(positions);
        Assert.That(count, Is.GreaterThan(0), line.name);
        Vector3 sum = Vector3.zero;
        for (int index = 0; index < count; index++)
            sum += positions[index];
        return sum / count;
    }

    static void AssertVectorNear(
        Vector3 actual,
        Vector3 expected,
        string message)
    {
        Assert.That(
            Vector3.Distance(actual, expected),
            Is.LessThan(0.001f),
            message + " Expected " + expected + ", got " + actual + ".");
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

public sealed class CombatFeedbackTestDamageable : MonoBehaviour,
    ISpaceDamageable
{
    public float Integrity { get; private set; } = 100f;
    public float MaximumIntegrity => 100f;
    public bool IsDestroyed => Integrity <= 0f;

    public void ApplyDamage(SpaceDamageInfo damage)
    {
        Integrity = Mathf.Max(0f, Integrity - damage.amount);
    }
}
