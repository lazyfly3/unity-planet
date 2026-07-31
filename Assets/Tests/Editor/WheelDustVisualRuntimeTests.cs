using System.Reflection;
using ModularAssembly;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.ModularAssembly;

public sealed class WheelDustVisualRuntimeTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
    }

    [Test]
    public void Activation_RequiresContactLoadAndWheelOrGroundMotion()
    {
        WheelContactTelemetry telemetry =
            GroundedTelemetry();

        Assert.That(
            WheelDustVisualRuntime.ShouldEmit(
                false,
                telemetry,
                0.5f),
            Is.False,
            "A wheel runtime reporting airborne must not emit.");

        telemetry.grounded = false;
        Assert.That(
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                telemetry,
                0.5f,
                1),
            Is.Zero,
            "Airborne telemetry must not emit.");

        telemetry = GroundedTelemetry();
        telemetry.longitudinalSpeed = 0f;
        telemetry.lateralSpeed = 0f;
        telemetry.wheelAngularSpeed = 0f;
        Assert.That(
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                telemetry,
                0.5f,
                1),
            Is.Zero,
            "A stationary tyre must not emit.");

        telemetry.longitudinalSpeed =
            WheelDustVisualRuntime.MinimumEmissionSpeed * 0.9f;
        Assert.That(
            WheelDustVisualRuntime.ShouldEmit(
                true,
                telemetry,
                0.5f),
            Is.False,
            "Low-speed movement stays below the visible dust threshold.");

        telemetry.longitudinalSpeed = 0f;
        telemetry.wheelAngularSpeed = 4f;
        telemetry.slipRatio = 0.8f;
        Assert.That(
            WheelDustVisualRuntime.ShouldEmit(
                true,
                telemetry,
                0.5f),
            Is.True,
            "A grounded spinning tyre can create a burnout cloud.");

        telemetry.normalForce = 0f;
        Assert.That(
            WheelDustVisualRuntime.ShouldEmit(
                true,
                telemetry,
                0.5f),
            Is.False,
            "An unloaded contact must not create dust.");
    }

    [Test]
    public void Rate_IsSparseWhenRollingAndEnhancedByExpectedSlip()
    {
        WheelContactTelemetry rolling =
            GroundedTelemetry();
        rolling.longitudinalSpeed = 8f;
        rolling.wheelAngularSpeed = 16f;
        rolling.slipRatio = 0.01f;
        rolling.slipAngleDegrees = 0.5f;

        float rollingRate =
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                rolling,
                0.5f,
                1);

        WheelContactTelemetry accelerating = rolling;
        accelerating.wheelAngularSpeed = 29f;
        accelerating.slipRatio = 0.65f;
        float accelerationRate =
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                accelerating,
                0.5f,
                1);

        WheelContactTelemetry braking = rolling;
        braking.wheelAngularSpeed = 0f;
        braking.slipRatio = -0.8f;
        float brakingRate =
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                braking,
                0.5f,
                1);

        WheelContactTelemetry sliding = rolling;
        sliding.lateralSpeed = 7f;
        sliding.slipAngleDegrees = 34f;
        float slidingRate =
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                sliding,
                0.5f,
                1);

        Assert.That(rollingRate, Is.GreaterThan(0f));
        Assert.That(rollingRate, Is.LessThan(15f));
        Assert.That(accelerationRate, Is.GreaterThan(rollingRate * 2f));
        Assert.That(brakingRate, Is.GreaterThan(rollingRate * 2f));
        Assert.That(slidingRate, Is.GreaterThan(rollingRate * 2f));
        Assert.That(accelerationRate, Is.LessThanOrEqualTo(30f));
        Assert.That(brakingRate, Is.LessThanOrEqualTo(30f));
        Assert.That(slidingRate, Is.LessThanOrEqualTo(30f));
    }

    [Test]
    public void Rate_DualTyresShareOneModuleWideAmount()
    {
        WheelContactTelemetry telemetry =
            GroundedTelemetry();
        telemetry.longitudinalSpeed = 11f;
        telemetry.wheelAngularSpeed = 28f;
        telemetry.slipRatio = 0.62f;

        float single =
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                telemetry,
                0.5f,
                1);
        float eachDual =
            WheelDustVisualRuntime.ComputeEmissionRate(
                true,
                telemetry,
                0.5f,
                2);

        Assert.That(
            eachDual * 2f,
            Is.EqualTo(single).Within(0.0001f));
    }

    [Test]
    public void Size_IsTyreScaledBoundedAndExpandsForSlip()
    {
        const float radius = 0.5f;
        const float width = 0.44f;
        float rollingSize =
            WheelDustVisualRuntime.ComputeInitialParticleSize(
                radius,
                width,
                0f);
        float slippingSize =
            WheelDustVisualRuntime.ComputeInitialParticleSize(
                radius,
                width,
                1f);
        float largerTyreSize =
            WheelDustVisualRuntime.ComputeInitialParticleSize(
                1f,
                0.86f,
                0f);

        Assert.That(rollingSize, Is.GreaterThan(0f));
        Assert.That(slippingSize, Is.GreaterThan(rollingSize));
        Assert.That(slippingSize, Is.LessThanOrEqualTo(width * 0.48f));
        Assert.That(largerTyreSize, Is.GreaterThan(rollingSize));
    }

    [Test]
    public void Lifetime_FollowsSourceRangeAndTyreScale()
    {
        float smallMinimum =
            WheelDustVisualRuntime.ComputeParticleLifetime(
                0.4f,
                0f);
        float smallMaximum =
            WheelDustVisualRuntime.ComputeParticleLifetime(
                0.4f,
                1f);
        float largeMaximum =
            WheelDustVisualRuntime.ComputeParticleLifetime(
                1.35f,
                1f);

        Assert.That(smallMinimum, Is.InRange(0.75f, 0.8f));
        Assert.That(smallMaximum, Is.InRange(1f, 1.02f));
        Assert.That(largeMaximum, Is.EqualTo(1.2f).Within(0.0001f));
    }

    [Test]
    public void Origin_IsBehindAndAboveTheRealContactPoint()
    {
        Vector3 contact = new Vector3(2f, 0f, 3f);
        Vector3 normal = Vector3.up;
        Vector3 movement = Vector3.forward;

        Vector3 origin =
            WheelDustVisualRuntime.ComputeEmissionOrigin(
                contact,
                normal,
                movement,
                0.5f);

        Assert.That(
            Vector3.Dot(origin - contact, movement),
            Is.LessThan(0f));
        Assert.That(
            Vector3.Dot(origin - contact, normal),
            Is.GreaterThan(0f));
    }

    [Test]
    public void Origin_UsesPlanetaryContactNormalInsteadOfWorldUp()
    {
        Vector3 contact = new Vector3(2f, 0f, 3f);
        Vector3 radialNormal = Vector3.right;
        Vector3 movement = Vector3.forward;

        Vector3 origin =
            WheelDustVisualRuntime.ComputeEmissionOrigin(
                contact,
                radialNormal,
                movement,
                0.5f);
        Vector3 offset = origin - contact;

        Assert.That(
            Vector3.Dot(offset, movement),
            Is.LessThan(0f));
        Assert.That(
            Vector3.Dot(offset, radialNormal),
            Is.GreaterThan(0f));
        Assert.That(
            Vector3.Dot(offset, Vector3.up),
            Is.EqualTo(0f).Within(0.00001f));
    }

    [Test]
    public void Smoothing_RampsIntensityWithoutLingeringEmissionInAir()
    {
        float first =
            WheelDustVisualRuntime.SmoothEmissionRate(
                0f,
                50f,
                0.02f);
        float second =
            WheelDustVisualRuntime.SmoothEmissionRate(
                first,
                50f,
                0.02f);

        Assert.That(first, Is.InRange(0.001f, 49.999f));
        Assert.That(second, Is.GreaterThan(first));
        Assert.That(second, Is.LessThan(50f));
        Assert.That(
            WheelDustVisualRuntime.SmoothEmissionRate(
                second,
                0f,
                0.02f),
            Is.Zero);
    }

    [Test]
    public void SurfaceGate_SuppressesWetIceAndManufacturedHardSurfaces()
    {
        root = new GameObject("QSChunk_PX_0_0_0");
        BoxCollider surface = root.AddComponent<BoxCollider>();
        Assert.That(
            WheelDustVisualRuntime.SupportsDirtVisual(surface),
            Is.True,
            "Voxel and otherwise unknown terrain remains compatible.");

        root.name = "PolishedMetalDeck";
        Assert.That(
            WheelDustVisualRuntime.SupportsDirtVisual(surface),
            Is.False);

        root.name = "TerrainServiceDriver";
        Assert.That(
            WheelDustVisualRuntime.SupportsDirtVisual(surface),
            Is.True,
            "Token matching must not confuse service/driver with ice/river.");

        root.name = "FrozenLake";
        Assert.That(
            WheelDustVisualRuntime.SupportsDirtVisual(surface),
            Is.False);
        root.name = "PlanetTerrain";
        root.layer = LayerMask.NameToLayer("Water");
        Assert.That(
            WheelDustVisualRuntime.SupportsDirtVisual(surface),
            Is.False);

        root.layer = 0;
        root.name = "Tundra_Ice_Floe";
        Assert.That(
            WheelDustVisualRuntime.SupportsDirtVisual(surface),
            Is.False);
    }

    [Test]
    public void Bind_CreatesWorldSpaceVisualOnlyEmitterUsingSharedMaterial()
    {
        root = new GameObject("WheelDustTest");
        root.layer = 8;
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.velocity = new Vector3(3f, 0f, 1f);
        Vector3 originalVelocity = body.velocity;

        ModularWheelRuntime wheel =
            root.AddComponent<ModularWheelRuntime>();
        wheel.ConfigureTyreElement(
            new ModularContentRecord
            {
                neoXId = "wheel_basic_111"
            },
            string.Empty,
            0,
            root.transform);
        WheelDustVisualRuntime dust =
            root.AddComponent<WheelDustVisualRuntime>();

        Assert.That(
            dust.EffectRoot,
            Is.Null,
            "The carrier does not emit before an actual wheel is bound.");
        dust.Bind(null);
        Assert.That(dust.EffectRoot, Is.Null);

        Material shared = Resources.Load<Material>(
            WheelDustVisualRuntime.DustMaterialResourcePath);
        Assert.That(shared, Is.Not.Null);
        Color originalColor = shared.HasProperty("_Color")
            ? shared.GetColor("_Color")
            : Color.white;

        dust.Bind(wheel);

        Assert.That(dust.BoundWheel, Is.SameAs(wheel));
        Assert.That(dust.EffectRoot, Is.Not.Null);
        Assert.That(dust.EffectRoot.gameObject.layer, Is.EqualTo(root.layer));
        Assert.That(dust.Particles, Is.Not.Null);
        Assert.That(dust.DustMaterial, Is.SameAs(shared));
        Assert.That(body.velocity, Is.EqualTo(originalVelocity));
        Assert.That(
            dust.EffectRoot.GetComponentsInChildren<Collider>(true),
            Is.Empty);
        Assert.That(
            dust.EffectRoot.GetComponentsInChildren<Rigidbody>(true),
            Is.Empty);

        ParticleSystem.MainModule main = dust.Particles.main;
        ParticleSystem.EmissionModule emission =
            dust.Particles.emission;
        ParticleSystem.ShapeModule shape = dust.Particles.shape;
        ParticleSystem.CollisionModule collision =
            dust.Particles.collision;
        ParticleSystem.TriggerModule trigger =
            dust.Particles.trigger;
        ParticleSystemRenderer renderer =
            dust.Particles.GetComponent<ParticleSystemRenderer>();

        Assert.That(
            main.simulationSpace,
            Is.EqualTo(ParticleSystemSimulationSpace.World));
        Assert.That(main.playOnAwake, Is.False);
        Assert.That(
            main.gravityModifier.constant,
            Is.Zero,
            "Planetary dust must not inherit world -Y Physics.gravity.");
        Assert.That(emission.enabled, Is.False);
        Assert.That(shape.enabled, Is.False);
        Assert.That(collision.enabled, Is.False);
        Assert.That(trigger.enabled, Is.False);
        Assert.That(renderer.sharedMaterial, Is.SameAs(shared));
        if (shared.HasProperty("_Color"))
        {
            Assert.That(
                shared.GetColor("_Color"),
                Is.EqualTo(originalColor),
                "Binding must not mutate the shared NeoX material.");
        }

        MeshRenderer placeholder =
            root.AddComponent<MeshRenderer>();
        MethodInfo isDustRenderer =
            typeof(NeoXCatalogIntegration).GetMethod(
                "IsWheelDustRenderer",
                BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(isDustRenderer, Is.Not.Null);
        Assert.That(
            isDustRenderer.Invoke(
                null,
                new object[] { placeholder }),
            Is.False,
            "A dust component on the module root must not protect old meshes.");
        Assert.That(
            isDustRenderer.Invoke(
                null,
                new object[] { renderer }),
            Is.True,
            "Only the renderer inside the dust effect root is protected.");
    }

    [Test]
    public void ManualEmission_UsesLocalRandomAndKeepsInitialCloudInsideTyreWidth()
    {
        root = new GameObject("WheelDustRandomIsolationTest");
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.velocity = new Vector3(3f, 0f, 1f);
        body.angularVelocity = new Vector3(0.2f, 0.3f, 0.4f);

        ModularWheelRuntime wheel =
            root.AddComponent<ModularWheelRuntime>();
        wheel.ConfigureTyreElement(
            new ModularContentRecord
            {
                neoXId = "wheel_basic_111"
            },
            string.Empty,
            0,
            root.transform);
        WheelDustVisualRuntime dust =
            root.AddComponent<WheelDustVisualRuntime>();
        dust.Bind(wheel);
        // BuildEffect starts the particle container only in Play Mode.
        // Mirror that runtime precondition before invoking the private,
        // presentation-only emission path in this EditMode test.
        dust.Particles.Play(false);

        Vector3 originalVelocity = body.velocity;
        Vector3 originalAngularVelocity = body.angularVelocity;
        float originalMass = body.mass;
        Random.State originalRandomState = Random.state;
        try
        {
            Random.InitState(1978);
            float expectedFirst = Random.value;
            float expectedSecond = Random.value;
            Random.InitState(1978);
            float actualFirst = Random.value;

            MethodInfo emit = typeof(WheelDustVisualRuntime).GetMethod(
                "EmitParticle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(emit, Is.Not.Null);
            emit.Invoke(
                dust,
                new object[]
                {
                    Vector3.zero,
                    Vector3.up,
                    Vector3.forward,
                    1f,
                    12f
                });

            float actualSecond = Random.value;
            Assert.That(actualFirst, Is.EqualTo(expectedFirst));
            Assert.That(
                actualSecond,
                Is.EqualTo(expectedSecond),
                "Cosmetic dust must not advance Unity's global random state.");
        }
        finally
        {
            Random.state = originalRandomState;
        }

        var emitted = new ParticleSystem.Particle[2];
        int count = dust.Particles.GetParticles(emitted);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(
            emitted[0].startSize,
            Is.LessThanOrEqualTo(wheel.Profile.width * 0.48f));
        Assert.That(body.velocity, Is.EqualTo(originalVelocity));
        Assert.That(
            body.angularVelocity,
            Is.EqualTo(originalAngularVelocity));
        Assert.That(body.mass, Is.EqualTo(originalMass));
    }

    [Test]
    public void CatalogBinding_HappensAfterExactTyreGeometryAndExcludes422()
    {
        root = new GameObject("WheelDustCatalogBindingTest");
        GridModuleView view = root.AddComponent<GridModuleView>();
        view.Initialize(
            new GridModuleRecord
            {
                BehaviorSettings = string.Empty
            });

        MethodInfo configure =
            typeof(NeoXCatalogIntegration).GetMethod(
                "ConfigureWheelElements",
                BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(configure, Is.Not.Null);
        configure.Invoke(
            null,
            new object[]
            {
                view,
                new ModularContentRecord
                {
                    neoXId = "speedwheel_large_l_522"
                }
            });

        ModularWheelRuntime[] tyres =
            root.GetComponentsInChildren<ModularWheelRuntime>(true);
        Assert.That(tyres, Has.Length.EqualTo(2));
        foreach (ModularWheelRuntime tyre in tyres)
        {
            WheelDustVisualRuntime dust =
                tyre.GetComponent<WheelDustVisualRuntime>();
            Assert.That(dust, Is.Not.Null);
            Assert.That(dust.enabled, Is.True);
            Assert.That(dust.BoundWheel, Is.SameAs(tyre));
            Assert.That(
                dust.EffectRoot.GetComponent<ParticleSystem>(),
                Is.Not.Null);
            Assert.That(tyre.HasAuthoredTyreGeometry, Is.True);
            Assert.That(tyre.Profile.radius, Is.GreaterThan(0.7f));
            Assert.That(tyre.Profile.width, Is.GreaterThan(1f));
        }

        configure.Invoke(
            null,
            new object[]
            {
                view,
                new ModularContentRecord
                {
                    neoXId = "wheel_l_422"
                }
            });

        ModularWheelRuntime primary =
            root.GetComponent<ModularWheelRuntime>();
        WheelDustVisualRuntime primaryDust =
            root.GetComponent<WheelDustVisualRuntime>();
        Assert.That(primary.HasAuthoredTyreGeometry, Is.False);
        Assert.That(primaryDust.enabled, Is.False);
        Assert.That(primaryDust.BoundWheel, Is.Null);
        Assert.That(
            root.transform.Find("WheelTyreElement_1").gameObject.activeSelf,
            Is.False);
    }

    private static WheelContactTelemetry GroundedTelemetry()
    {
        return new WheelContactTelemetry
        {
            grounded = true,
            point = Vector3.zero,
            normal = Vector3.up,
            sprungMass = 300f,
            normalForce = 300f * 9.81f,
            surfaceGrip = 1f
        };
    }
}
