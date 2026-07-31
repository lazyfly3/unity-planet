using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

public sealed class ModularWheelPhysicsTests
{
    private const float Step = 0.02f;
    private static readonly Vector3 TestOrigin = Vector3.zero;
    private Scene testScene;
    private PhysicsScene physicsScene;
    private Scene previousActiveScene;
    private readonly List<PhysicMaterial> materials =
        new List<PhysicMaterial>();

    [SetUp]
    public void SetUp()
    {
        previousActiveScene = SceneManager.GetActiveScene();
        testScene = EditorSceneManager.NewPreviewScene();
        physicsScene = testScene.GetPhysicsScene();
        Assert.That(physicsScene.IsValid(), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (PhysicMaterial material in materials)
        {
            if (material != null)
                UnityEngine.Object.DestroyImmediate(material);
        }
        materials.Clear();
        if (previousActiveScene.IsValid())
            SceneManager.SetActiveScene(previousActiveScene);
        if (testScene.IsValid())
            EditorSceneManager.ClosePreviewScene(testScene);
    }

    [Test]
    public void SprungMassSolver_PreservesMassAndPlanarMoments()
    {
        Vector2[] positions =
        {
            new Vector2(-1.5f, -1f),
            new Vector2(1.5f, -1f),
            new Vector2(-1.5f, 2f),
            new Vector2(1.5f, 2f)
        };

        float[] masses =
            VehicleGroundMobility.SolveSprungMasses(
                positions,
                1200f);

        float total = 0f;
        Vector2 moment = Vector2.zero;
        for (int index = 0; index < masses.Length; index++)
        {
            Assert.That(masses[index], Is.GreaterThanOrEqualTo(0f));
            total += masses[index];
            moment += positions[index] * masses[index];
        }
        Assert.That(total, Is.EqualTo(1200f).Within(0.01f));
        Assert.That(moment.x, Is.EqualTo(0f).Within(0.1f));
        Assert.That(moment.y, Is.EqualTo(0f).Within(0.1f));
        Assert.That(masses[0], Is.GreaterThan(masses[2]));
    }

    [Test]
    public void ContactPatch_UsesBodyPhysicsSceneOnUnevenMesh()
    {
        CreateWaveTerrain(18f, 18f, 8, 8, 0.08f);
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.61f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        bool grounded = wheel.ProbeContact(Vector3.up, Step);
        float load = wheel.PrepareSuspension(
            Vector3.up,
            9.81f,
            Step);

        Assert.That(grounded, Is.True);
        Assert.That(wheel.Telemetry.validSamples, Is.GreaterThanOrEqualTo(6));
        Assert.That(wheel.Telemetry.patchSamples, Is.GreaterThanOrEqualTo(1));
        Assert.That(wheel.ContactNormal.magnitude, Is.EqualTo(1f).Within(0.001f));
        Assert.That(Vector3.Dot(wheel.ContactNormal, Vector3.up),
            Is.GreaterThan(0.9f));
        Assert.That(wheel.Compression, Is.InRange(0f, 1f));
        Assert.That(load, Is.GreaterThan(0f));
    }

    [Test]
    public void ContactRecovery_ShallowPenetrationCreatesSeparatingLoad()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.4f, 0f));
        body.velocity = Vector3.down * 4f;
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        float load = wheel.PrepareSuspension(
            Vector3.up,
            9.81f,
            Step);

        Assert.That(wheel.SuspensionDroop, Is.LessThan(0f));
        Assert.That(wheel.Telemetry.penetrationDepth,
            Is.GreaterThan(0.05f));
        Assert.That(load, Is.GreaterThan(500f * 9.81f));
    }

    [Test]
    public void ZeroFrictionSurface_DoesNotCreateHiddenTireGrip()
    {
        PhysicMaterial ice = new PhysicMaterial("ZeroGrip")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum
        };
        materials.Add(ice);
        CreateFlatGround(ice);
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.6f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetAutomaticRole(WheelRoleOverride.SteerDrive);
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();
        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);

        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        wheel.ApplyForces(
            Vector3.up,
            1f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);

        Vector3 planarForce =
            Vector3.ProjectOnPlane(ledger.TotalForce, Vector3.up);
        Assert.That(wheel.Telemetry.surfaceGrip, Is.EqualTo(0f));
        Assert.That(planarForce.magnitude, Is.LessThan(0.001f));
    }

    [Test]
    public void ZeroAssignedSprungMass_DoesNotBecomeWholeVehicleLoad()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(900f, new Vector3(0f, 0.6f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(0f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        float load = wheel.PrepareSuspension(
            Vector3.up,
            9.81f,
            Step);

        Assert.That(load, Is.LessThan(25f));
    }

    [Test]
    public void ConstantDroopOnSlope_DoesNotCreateFalseDamping()
    {
        const float slope = 0.18f;
        CreateSlopeTerrain(20f, 30f, slope);
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.6f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();
        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);

        Vector3 slopeDirection =
            new Vector3(0f, slope, 1f).normalized;
        body.velocity = slopeDirection * 10f;
        body.position += body.velocity * Step;
        Physics.SyncTransforms();
        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);

        Assert.That(Mathf.Abs(wheel.SuspensionSpeed), Is.LessThan(0.2f));
    }

    [Test]
    public void TireImpulse_DoesNotReverseLongitudinalSlipInOneStep()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.6f, 0f));
        body.velocity = Vector3.forward;
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetAutomaticRole(WheelRoleOverride.FreeRolling);
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();
        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);
        float initialPointSpeed = Vector3.Dot(
            body.GetPointVelocity(wheel.ContactPoint),
            wheel.BaseForwardWorld);
        float initialSlip =
            wheel.WheelAngularSpeed * wheel.Profile.radius -
            initialPointSpeed;

        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        wheel.ApplyForces(
            Vector3.up,
            0f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);
        IntegrateBody(body, ledger);
        float pointSpeed = Vector3.Dot(
            body.GetPointVelocity(wheel.ContactPoint),
            wheel.BaseForwardWorld);
        float finalSlip =
            wheel.WheelAngularSpeed * wheel.Profile.radius -
            pointSpeed;

        Assert.That(initialSlip, Is.LessThan(0f));
        Assert.That(finalSlip, Is.LessThanOrEqualTo(0.02f));
        Assert.That(initialSlip * finalSlip, Is.GreaterThanOrEqualTo(-0.02f));
    }

    [Test]
    public void ForwardSphereSweep_BlocksVerticalStepFace()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.6f, 0f));
        body.velocity = Vector3.forward * 8f;
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetAutomaticRole(WheelRoleOverride.FreeRolling);
        wheel.SetFlightMode(true);
        float stepFront =
            wheel.NeutralWheelCenterWorld.z +
            wheel.Profile.radius +
            body.velocity.z * Step * 0.55f;
        CreateBox(
            "VerticalStep",
            new Vector3(0f, 0.5f, stepFront + 0.1f),
            new Vector3(4f, 1f, 0.2f));
        Physics.SyncTransforms();

        wheel.ProbeContact(Vector3.up, Step);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);
        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        wheel.ApplyForces(
            Vector3.up,
            0f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);

        Assert.That(wheel.Telemetry.obstacleContact, Is.True);
        Assert.That(wheel.Telemetry.obstacleNormal.z, Is.LessThan(-0.8f));
        Assert.That(wheel.Telemetry.obstacleForce, Is.GreaterThan(0f));
        Assert.That(ledger.TotalForce.z, Is.LessThan(0f));
    }

    [Test]
    public void RecoveryRay_DoesNotMistakeOverheadPlatformForGround()
    {
        CreateFlatGround();
        CreateBox(
            "OverheadPlatform",
            new Vector3(0f, 1.35f, 0f),
            new Vector3(6f, 0.15f, 6f));
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.6f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactPoint.y, Is.EqualTo(0f).Within(0.01f));
        Assert.That(wheel.Telemetry.penetrationDepth, Is.LessThan(0.01f));
    }

    [Test]
    public void StaticLoad_SettlesIntoSuspensionInsteadOfHoveringAtFullDroop()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(1200f, new Vector3(0f, 0.68f, 0f));
        VehicleGroundMobility mobility = CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();

        for (int frame = 0; frame < 220; frame++)
            SimulateVehicleStep(body, mobility, ledger, Vector2.zero);

        float averageCompression = 0f;
        foreach (ModularWheelRuntime wheel in mobility.Wheels)
            averageCompression += wheel.Compression;
        averageCompression /= mobility.WheelCount;

        Assert.That(averageCompression, Is.InRange(0.35f, 0.68f));
        Assert.That(mobility.SupportRatio, Is.InRange(0.88f, 1.12f));
        Assert.That(Mathf.Abs(body.velocity.y), Is.LessThan(0.15f));
    }

    [Test]
    public void ContinuousVerticalProbe_StopsExtremeLandingBeforePenetration()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(1200f, new Vector3(0f, 2.4f, 0f));
        body.velocity = Vector3.down * 120f;
        VehicleGroundMobility mobility = CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();
        float maximumPenetration = 0f;

        for (int frame = 0; frame < 100; frame++)
        {
            SimulateVehicleStep(body, mobility, ledger, Vector2.zero);
            maximumPenetration = Mathf.Max(
                maximumPenetration,
                MaximumWheelPenetration(mobility));
        }

        Assert.That(maximumPenetration, Is.LessThan(0.04f));
        Assert.That(body.position.y, Is.GreaterThan(0.48f));
        Assert.That(Mathf.Abs(body.velocity.y), Is.LessThan(0.75f));
    }

    [Test]
    public void MirroredWheelFrame_PreservesVehicleForwardDriveDirection()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(600f, new Vector3(0f, 0.6f, 0f));
        ModularWheelRuntime regular =
            CreateWheel(
                body,
                new Vector3(-0.8f, 0f, 0f),
                "wheel_basic_111");
        ModularWheelRuntime mirrored =
            CreateWheel(
                body,
                new Vector3(0.8f, 0f, 0f),
                "wheel_basic_111");
        mirrored.transform.localRotation =
            Quaternion.AngleAxis(180f, Vector3.up);
        Transform regularRolling =
            BindRollingVisual(regular, "Regular");
        Transform mirroredRolling =
            BindRollingVisual(mirrored, "Mirrored");
        regular.SetSprungMass(300f);
        mirrored.SetSprungMass(300f);
        regular.SetAutomaticRole(WheelRoleOverride.DriveOnly);
        mirrored.SetAutomaticRole(WheelRoleOverride.DriveOnly);
        regular.SetFlightMode(true);
        mirrored.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(regular.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(mirrored.ProbeContact(Vector3.up, Step), Is.True);
        regular.PrepareSuspension(Vector3.up, 9.81f, Step);
        mirrored.PrepareSuspension(Vector3.up, 9.81f, Step);
        Assert.That(
            Vector3.Dot(regular.BaseForwardWorld, body.transform.forward),
            Is.GreaterThan(0.95f));
        Assert.That(
            Vector3.Dot(mirrored.BaseForwardWorld, body.transform.forward),
            Is.GreaterThan(0.95f));
        Assert.That(
            Vector3.Dot(mirrored.SuspensionUpWorld, Vector3.up),
            Is.GreaterThan(0.95f));

        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        regular.ApplyForces(
            Vector3.up,
            0.8f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);
        mirrored.ApplyForces(
            Vector3.up,
            0.8f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);

        Assert.That(
            Vector3.Dot(ledger.TotalForce, body.transform.forward),
            Is.GreaterThan(0f));

        BindingFlags fields =
            BindingFlags.Instance |
            BindingFlags.NonPublic;
        FieldInfo wheelAngularSpeed =
            typeof(ModularWheelRuntime).GetField(
                "wheelAngularSpeed",
                fields);
        MethodInfo advanceRollingVisuals =
            typeof(ModularWheelRuntime).GetMethod(
                "AdvanceRollingVisuals",
                fields);
        Assert.That(wheelAngularSpeed, Is.Not.Null);
        Assert.That(advanceRollingVisuals, Is.Not.Null);
        const float angularSpeed = 2f;
        const float visualStep = 0.25f;
        wheelAngularSpeed.SetValue(regular, angularSpeed);
        wheelAngularSpeed.SetValue(mirrored, angularSpeed);
        Vector3 regularBefore =
            regularRolling.TransformDirection(Vector3.up);
        Vector3 mirroredBefore =
            mirroredRolling.TransformDirection(Vector3.up);
        advanceRollingVisuals.Invoke(
            regular,
            new object[] { visualStep });
        advanceRollingVisuals.Invoke(
            mirrored,
            new object[] { visualStep });
        float expectedVisualAngle =
            angularSpeed * Mathf.Rad2Deg * visualStep;
        Assert.That(
            Vector3.SignedAngle(
                regularBefore,
                regularRolling.TransformDirection(Vector3.up),
                regular.BaseAxleWorld),
            Is.EqualTo(expectedVisualAngle).Within(0.05f),
            "regular visual rotation follows the physical axle");
        Assert.That(
            Vector3.SignedAngle(
                mirroredBefore,
                mirroredRolling.TransformDirection(Vector3.up),
                mirrored.BaseAxleWorld),
            Is.EqualTo(expectedVisualAngle).Within(0.05f),
            "mirrored visual rotation follows the canonical physical axle");
    }

    [Test]
    public void VolumeEnvelope_DetectsNarrowRidgeBetweenRaySamples()
    {
        CreateFlatGround();
        CreateBox(
            "NarrowRidge",
            new Vector3(0f, 0.1f, 0.16f),
            new Vector3(0.05f, 0.2f, 0.05f));
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.68f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactPoint.y, Is.GreaterThan(0.18f));
        Assert.That(wheel.Telemetry.penetrationDepth, Is.LessThan(0.02f));
    }

    [Test]
    public void StaticColliderSeam_DoesNotAverageSeparateChunks()
    {
        CreateBox(
            "LowChunk",
            new Vector3(0f, -0.5f, -2.5f),
            new Vector3(10f, 1f, 5f));
        CreateBox(
            "HighChunk",
            new Vector3(0f, -0.4f, 2.5f),
            new Vector3(10f, 1f, 5f));
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.65f, 0.02f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactPoint.y, Is.EqualTo(0.1f).Within(0.015f));
    }

    [Test]
    public void ThinMesh_StopsHundredMetrePerSecondLanding()
    {
        CreateWaveTerrain(20f, 20f, 10, 10, 0f);
        Rigidbody body = CreateBody(1200f, new Vector3(0f, 2f, 0f));
        body.velocity = Vector3.down * 100f;
        VehicleGroundMobility mobility = CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();
        float maximumPenetration = 0f;

        for (int frame = 0; frame < 100; frame++)
        {
            SimulateVehicleStep(body, mobility, ledger, Vector2.zero);
            maximumPenetration = Mathf.Max(
                maximumPenetration,
                MaximumWheelPenetration(mobility));
        }

        Assert.That(maximumPenetration, Is.LessThan(0.04f));
        Assert.That(body.position.y, Is.GreaterThan(0.48f));
    }

    [Test]
    public void FourWheelLateralImpulse_DoesNotReverseWholeVehicleSlip()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(1200f, new Vector3(0f, 0.6f, 0f));
        body.velocity = Vector3.right;
        VehicleGroundMobility mobility = CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();

        for (int frame = 0; frame < 8; frame++)
        {
            SimulateVehicleStep(body, mobility, ledger, Vector2.zero);
            float lateralSpeed = Vector3.Dot(
                body.velocity,
                body.transform.right);
            Assert.That(lateralSpeed, Is.GreaterThanOrEqualTo(-0.03f));
        }

        Assert.That(
            Mathf.Abs(Vector3.Dot(body.velocity, body.transform.right)),
            Is.LessThan(0.35f));
    }

    [Test]
    public void TwoWheelsOnSameSide_AreNotStableSupportPolygon()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(600f, new Vector3(0f, 0.6f, 0f));
        ModularWheelRuntime[] wheels =
        {
            CreateWheel(
                body,
                new Vector3(-1.1f, 0f, -1.4f),
                "wheel_basic_111"),
            CreateWheel(
                body,
                new Vector3(-1.1f, 0f, 1.4f),
                "wheel_basic_111")
        };
        var mobility = new VehicleGroundMobility();
        mobility.Rebuild(body, body.transform, wheels);
        mobility.SetFlightMode(true);
        Physics.SyncTransforms();
        mobility.BeginPhysicsStep(Vector3.up, 9.81f, Step);

        Assert.That(mobility.GroundedWheelCount, Is.EqualTo(2));
        Assert.That(mobility.SupportMargin, Is.LessThanOrEqualTo(0f));
        Assert.That(mobility.HasStableGroundSupport, Is.False);
    }

    [Test]
    public void ClimbableSlope_IsNotAlsoClassifiedAsObstacle()
    {
        float slope = Mathf.Tan(50f * Mathf.Deg2Rad);
        CreateSlopeTerrain(20f, 30f, slope);
        Rigidbody body = CreateBody(500f, new Vector3(0f, 0.7f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.Telemetry.obstacleContact, Is.False);
        Assert.That(
            Vector3.Dot(wheel.ContactNormal, Vector3.up),
            Is.GreaterThan(0.6f));
    }

    [Test]
    public void OverloadedWheelBuild_UsesMoreTravelWithoutSinking()
    {
        CreateFlatGround();
        Rigidbody supportedBody = CreateBody(
            1200f,
            new Vector3(-8f, 0.68f, 0f));
        Rigidbody overloadedBody = CreateBody(
            6000f,
            new Vector3(8f, 0.68f, 0f));
        VehicleGroundMobility supported =
            CreateFourWheelMobility(supportedBody);
        VehicleGroundMobility overloaded =
            CreateFourWheelMobility(overloadedBody);
        var supportedLedger = new VehicleForceLedger();
        var overloadedLedger = new VehicleForceLedger();
        float overloadedMaximumPenetration = 0f;

        for (int frame = 0; frame < 300; frame++)
        {
            SimulateVehicleStep(
                supportedBody,
                supported,
                supportedLedger,
                Vector2.zero);
            SimulateVehicleStep(
                overloadedBody,
                overloaded,
                overloadedLedger,
                Vector2.zero);
            overloadedMaximumPenetration = Mathf.Max(
                overloadedMaximumPenetration,
                MaximumWheelPenetration(overloaded));
        }

        float supportedCompression = AverageCompression(supported);
        float overloadedCompression = AverageCompression(overloaded);
        Assert.That(supportedCompression, Is.InRange(0.42f, 0.58f));
        Assert.That(
            overloadedCompression,
            Is.GreaterThan(supportedCompression + 0.22f));
        Assert.That(overloadedCompression, Is.LessThan(0.95f));
        Assert.That(overloadedMaximumPenetration, Is.LessThan(0.02f));
    }

    [Test]
    public void HighSpeedSteeringEnvelope_TightensWithoutRemovingLowSpeedControl()
    {
        CreateFlatGround();
        Rigidbody lowSpeedBody = CreateBody(
            1200f,
            new Vector3(-8f, 0.6f, 0f));
        Rigidbody highSpeedBody = CreateBody(
            1200f,
            new Vector3(8f, 0.6f, 0f));
        lowSpeedBody.velocity = Vector3.forward * 5f;
        highSpeedBody.velocity = Vector3.forward * 60f;
        VehicleGroundMobility lowSpeed =
            CreateFourWheelMobility(lowSpeedBody);
        VehicleGroundMobility highSpeed =
            CreateFourWheelMobility(highSpeedBody);
        var lowLedger = new VehicleForceLedger();
        var highLedger = new VehicleForceLedger();

        for (int frame = 0; frame < 20; frame++)
        {
            ApplyControlWithoutIntegration(
                lowSpeedBody,
                lowSpeed,
                lowLedger,
                new Vector2(1f, 0f));
            ApplyControlWithoutIntegration(
                highSpeedBody,
                highSpeed,
                highLedger,
                new Vector2(1f, 0f));
        }

        float lowMaximumSteer = MaximumSteerAngle(lowSpeed);
        float highMaximumSteer = MaximumSteerAngle(highSpeed);
        Assert.That(lowMaximumSteer, Is.GreaterThan(25f));
        Assert.That(highMaximumSteer, Is.InRange(0.1f, 1.5f));
        Assert.That(highMaximumSteer, Is.LessThan(lowMaximumSteer * 0.08f));
    }

    [Test]
    public void FourWheelRig_HighSpeedLandingRecoversWithoutSinking()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(1200f, new Vector3(0f, 1.4f, 0f));
        body.velocity = Vector3.down * 35f;
        VehicleGroundMobility mobility = CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();
        float maximumPenetration = 0f;

        for (int frame = 0; frame < 140; frame++)
        {
            ledger.Begin(body);
            ledger.AddAcceleration(Vector3.down * 9.81f);
            mobility.BeginPhysicsStep(
                Vector3.up,
                9.81f,
                Step,
                ledger);
            mobility.ApplyPassive(Vector3.up, ledger, Step);
            mobility.FinalizePhysicsStep(
                Vector3.up,
                9.81f,
                ledger,
                Step);
            foreach (ModularWheelRuntime wheel in mobility.Wheels)
            {
                maximumPenetration = Mathf.Max(
                    maximumPenetration,
                    wheel.Telemetry.penetrationDepth);
            }
            IntegrateBody(body, ledger);
        }

        Assert.That(maximumPenetration, Is.LessThan(0.04f));
        Assert.That(body.position.y - TestOrigin.y, Is.GreaterThan(0.48f));
        Assert.That(Mathf.Abs(body.velocity.y), Is.LessThan(0.75f));
        Assert.That(Vector3.Angle(body.transform.up, Vector3.up),
            Is.LessThan(5f));
    }

    [Test]
    public void FourWheelRig_DrivesAcrossWaveMeshWithoutDriftOrRollover()
    {
        // Keep the original two-metre triangle spacing, but leave enough
        // runway for the complete acceleration phase. The shorter mesh let
        // the rig drive off its end and measured free-fall pitch as rollover.
        CreateWaveTerrain(24f, 180f, 12, 90, 0.09f);
        Rigidbody body = CreateBody(
            1200f,
            new Vector3(0f, 0.66f, -32f));
        VehicleGroundMobility mobility = CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();
        float maximumPenetration = 0f;
        float maximumTilt = 0f;
        float maximumLateralOffset = 0f;
        float maximumTailVerticalSpeed = 0f;
        float maximumStraightLineAssist = 0f;
        float tailSupportSum = 0f;
        int tailSupportSamples = 0;

        for (int frame = 0; frame < 100; frame++)
        {
            SimulateVehicleStep(body, mobility, ledger, Vector2.zero);
            maximumPenetration = Mathf.Max(
                maximumPenetration,
                MaximumWheelPenetration(mobility));
            maximumTilt = Mathf.Max(
                maximumTilt,
                Vector3.Angle(body.transform.up, Vector3.up));
            maximumStraightLineAssist = Mathf.Max(
                maximumStraightLineAssist,
                Mathf.Abs(mobility.StraightLineAssistAngle));
        }
        float startZ = body.position.z;
        for (int frame = 0; frame < 350; frame++)
        {
            SimulateVehicleStep(
                body,
                mobility,
                ledger,
                new Vector2(0f, 0.72f));
            maximumPenetration = Mathf.Max(
                maximumPenetration,
                MaximumWheelPenetration(mobility));
            maximumTilt = Mathf.Max(
                maximumTilt,
                Vector3.Angle(body.transform.up, Vector3.up));
            maximumLateralOffset = Mathf.Max(
                maximumLateralOffset,
                Mathf.Abs(body.position.x - TestOrigin.x));
            maximumStraightLineAssist = Mathf.Max(
                maximumStraightLineAssist,
                Mathf.Abs(mobility.StraightLineAssistAngle));
            if (frame >= 250)
            {
                tailSupportSum += mobility.SupportRatio;
                tailSupportSamples++;
                maximumTailVerticalSpeed = Mathf.Max(
                    maximumTailVerticalSpeed,
                    Mathf.Abs(body.velocity.y));
            }
        }

        Assert.That(body.position.z - startZ, Is.GreaterThan(8f));
        Assert.That(maximumLateralOffset, Is.LessThan(0.6f));
        Assert.That(maximumStraightLineAssist,
            Is.InRange(0.01f, 1f));
        Assert.That(maximumTilt, Is.LessThan(14f));
        Assert.That(mobility.GroundedWheelCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(
            tailSupportSum / Mathf.Max(1, tailSupportSamples),
            Is.InRange(0.8f, 1.2f));
        Assert.That(maximumTailVerticalSpeed, Is.LessThan(1.6f));
        Assert.That(maximumPenetration, Is.LessThan(0.08f));
    }

    [Test]
    public void FinalLedgerSweep_BlocksLateDownwardForceOnThinMesh()
    {
        CreateWaveTerrain(20f, 20f, 10, 10, 0f);
        Rigidbody body = CreateBody(
            1200f,
            new Vector3(0f, 2f, 0f));
        VehicleGroundMobility mobility =
            CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();

        ledger.Begin(body);
        ledger.AddAcceleration(Vector3.down * 9.81f);
        mobility.BeginPhysicsStep(
            Vector3.up,
            9.81f,
            Step,
            ledger);
        mobility.ApplyControl(
            Vector3.up,
            9.81f,
            Vector2.zero,
            false,
            false,
            ledger,
            Step);
        // This force is deliberately added after the ordinary wheel pass,
        // matching a late RCS/aerodynamic contribution in the real pipeline.
        ledger.AddAcceleration(Vector3.down * 5000f);
        mobility.FinalizePhysicsStep(
            Vector3.up,
            9.81f,
            ledger,
            Step);
        IntegrateBody(body, ledger);

        Assert.That(body.position.y, Is.GreaterThan(0.46f));
        foreach (ModularWheelRuntime wheel in mobility.Wheels)
        {
            Vector3 tyreCenter =
                wheel.NeutralWheelCenterWorld -
                wheel.SuspensionUpWorld *
                wheel.CurrentVisualDroop;
            float tyreBottom =
                tyreCenter.y - wheel.Profile.radius;
            Assert.That(tyreBottom, Is.GreaterThanOrEqualTo(-0.01f));
        }
    }

    [Test]
    public void DeepOverlap_SpawnInsideGroundRecoversUpward()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, -0.3f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(
            wheel.ProbeContact(Vector3.up, Step),
            Is.True);
        Assert.That(wheel.ContactNormal.y, Is.GreaterThan(0.9f));
        Assert.That(wheel.ContactPoint.y, Is.EqualTo(0f).Within(0.02f));
        Assert.That(wheel.Telemetry.penetrationDepth, Is.GreaterThan(0.5f));
        Vector3 recoveredTyreCenter =
            wheel.NeutralWheelCenterWorld -
            wheel.SuspensionUpWorld *
            wheel.CurrentVisualDroop;
        Assert.That(
            recoveredTyreCenter.y -
            wheel.Profile.radius,
            Is.GreaterThanOrEqualTo(-0.02f));
    }

    [Test]
    public void FinalSweep_ConstrainsClimbableThinRampAhead()
    {
        CreateFlatGround();
        CreateThinRamp(
            0.62f,
            0.36f,
            0.2f,
            4f);
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.66f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetAutomaticRole(WheelRoleOverride.FreeRolling);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        ledger.AddAcceleration(Vector3.down * 9.81f);
        Assert.That(
            wheel.ProbeContact(Vector3.up, Step, ledger),
            Is.True);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);
        wheel.ApplyForces(
            Vector3.up,
            0f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);
        ledger.AddAcceleration(Vector3.forward * 2400f);
        wheel.PrepareFinalConstraints(
            Vector3.up,
            9.81f,
            Step,
            ledger);
        for (int iteration = 0; iteration < 6; iteration++)
            wheel.ApplyConstraintCorrection(Step, ledger);

        Assert.That(wheel.Telemetry.obstacleContact, Is.True);
        Assert.That(
            wheel.Telemetry.obstacleNormal.y,
            Is.GreaterThan(0.28f));
        Assert.That(wheel.Telemetry.obstacleForce, Is.GreaterThan(0f));
    }

    [Test]
    public void TreadCapsule_DetectsNarrowRidgeUnderShoulder()
    {
        CreateFlatGround();
        CreateBox(
            "ShoulderRidge",
            new Vector3(0.15f, 0.1f, 0.16f),
            new Vector3(0.025f, 0.2f, 0.04f));
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.66f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactPoint.y, Is.GreaterThan(0.18f));
    }

    [Test]
    public void DynamicPlatformConstraint_UsesReducedMassPrediction()
    {
        Rigidbody platform = CreateDynamicGround(
            500f,
            new Vector3(0f, -0.5f, 0f),
            new Vector3(20f, 1f, 20f));
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.48f, 0f));
        body.velocity = Vector3.down * 10f;
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(0f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);
        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        wheel.ApplyForces(
            Vector3.up,
            0f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);
        for (int iteration = 0; iteration < 8; iteration++)
            wheel.ApplyConstraintCorrection(Step, ledger);

        Vector3 predictedBodyVelocity =
            body.GetPointVelocity(wheel.ContactPoint) +
            ledger.TotalForce / body.mass * Step;
        Vector3 predictedGroundVelocity =
            ledger.PredictedExternalPointVelocity(
                platform,
                wheel.ContactPoint);
        float relativeNormalSpeed = Vector3.Dot(
            predictedBodyVelocity - predictedGroundVelocity,
            wheel.ContactNormal);
        Assert.That(Mathf.Abs(relativeNormalSpeed), Is.LessThan(0.35f));
        Assert.That(predictedGroundVelocity.y, Is.LessThan(0f));
    }

    [Test]
    public void TriangularSupport_UsesHullCrossSectionNotVertexWidth()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(
            1200f,
            new Vector3(0f, 0.58f, 0f));
        ModularWheelRuntime[] wheels =
        {
            CreateWheel(
                body,
                new Vector3(-2f, 0f, 2f),
                "legacy_test_wheel"),
            CreateWheel(
                body,
                new Vector3(2f, 0f, 2f),
                "legacy_test_wheel"),
            CreateWheel(
                body,
                new Vector3(0f, 0f, -2f),
                "legacy_test_wheel")
        };
        var mobility = new VehicleGroundMobility();
        mobility.Rebuild(body, body.transform, wheels);
        mobility.SetFlightMode(true);
        Physics.SyncTransforms();
        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        ledger.AddAcceleration(Vector3.down * 9.81f);
        mobility.BeginPhysicsStep(
            Vector3.up,
            9.81f,
            Step,
            ledger);
        mobility.ApplyControl(
            Vector3.up,
            9.81f,
            Vector2.zero,
            false,
            false,
            ledger,
            Step);

        Assert.That(mobility.HasStableGroundSupport, Is.True);
        Assert.That(mobility.LeftSupportMargin, Is.InRange(0.9f, 1.1f));
        Assert.That(mobility.RightSupportMargin, Is.InRange(0.9f, 1.1f));
        Assert.That(mobility.LateralSupportMargin, Is.LessThan(1.1f));
    }

    [Test]
    public void VisualEnvelope_CalibratesRadiusAndFullAxleWidth()
    {
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 2f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "legacy_test_wheel");
        GameObject visual =
            GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(visual, testScene);
        UnityEngine.Object.DestroyImmediate(
            visual.GetComponent<Collider>());
        visual.transform.SetParent(wheel.transform, false);
        visual.transform.localScale =
            new Vector3(0.9f, 1.1f, 0.8f);

        wheel.BindVisual(visual.transform);

        Assert.That(wheel.Profile.radius,
            Is.GreaterThanOrEqualTo(0.559f));
        Assert.That(wheel.Profile.width,
            Is.GreaterThanOrEqualTo(0.919f));
        float calibratedRadius = wheel.Profile.radius;
        float calibratedWidth = wheel.Profile.width;

        wheel.Configure(
            new ModularContentRecord
            {
                neoXId = "legacy_test_wheel",
                wheelAxleLocal = new[] { 1f, 0f, 0f },
                wheelRollingForwardLocal =
                    new[] { 0f, 0f, 1f }
            },
            WheelRoleSettings.Serialize(
                WheelRoleOverride.Auto));

        Assert.That(wheel.Profile.radius,
            Is.EqualTo(calibratedRadius).Within(0.001f));
        Assert.That(wheel.Profile.width,
            Is.EqualTo(calibratedWidth).Within(0.001f));
    }

    [Test]
    public void VisualDroop_PreservesContactPlaneOnSteepSlope()
    {
        const float slopeDegrees = 50f;
        Vector3 normal =
            Quaternion.AngleAxis(slopeDegrees, Vector3.right) *
            Vector3.up;
        CreateSlopedGround(slopeDegrees);
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.72f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        float originalDroop = wheel.CurrentVisualDroop;
        Vector3 anchorDelta = -normal * 0.2f;
        body.transform.position += anchorDelta;
        float expected =
            originalDroop +
            Vector3.Dot(anchorDelta, wheel.ContactNormal) /
            Vector3.Dot(
                wheel.SuspensionUpWorld,
                wheel.ContactNormal);

        Assert.That(
            wheel.CurrentVisualDroop,
            Is.EqualTo(expected).Within(0.01f));
    }

    [Test]
    public void ConservativeEnvelope_AllWheelProfilesCoverOuterShoulder()
    {
        CreateFlatGround();
        string[] ids =
        {
            "wheel_basic_111",
            "wheel_m_222",
            "speedwheel_small_l_322",
            "speedwheel_small_r_322",
            "speedwheel_large_l_522",
            "speedwheel_large_r_522"
        };
        int laneIndex = 0;
        foreach (string id in ids)
        {
            Assert.That(
                WheelModuleGeometryCatalog.TryResolve(
                    id,
                    out WheelTyreGeometry[] authoredTyres),
                Is.True,
                id);
            for (int tyreIndex = 0;
                 tyreIndex < authoredTyres.Length;
                 tyreIndex++, laneIndex++)
            {
                WheelTyreGeometry geometry =
                    authoredTyres[tyreIndex];
                float lane = laneIndex * 8f;
                float probeRadius = Mathf.Max(
                    0.04f,
                    Mathf.Min(
                        geometry.radius * 0.2f,
                        Mathf.Max(
                            geometry.width * 0.12f,
                            geometry.radius *
                            Mathf.Sin(Mathf.PI / 64f) *
                            1.05f)));
                float betweenSamples =
                    0.7916667f *
                    (geometry.radius - probeRadius);
                float circleDepth = Mathf.Sqrt(
                    geometry.radius * geometry.radius -
                    betweenSamples * betweenSamples);
                float ridgeTop =
                    geometry.radius - circleDepth + 0.08f;
                CreateBox(
                    "OuterShoulder_" + id + "_" + tyreIndex,
                    new Vector3(
                        lane + geometry.centerLocal.x +
                        geometry.width * 0.5f - 0.002f,
                        ridgeTop * 0.5f,
                        geometry.centerLocal.z + betweenSamples),
                    new Vector3(
                        0.012f,
                        ridgeTop,
                        0.012f));
                Rigidbody body = CreateBody(
                    500f,
                    new Vector3(
                        lane,
                        geometry.radius + 0.18f -
                        geometry.centerLocal.y,
                        0f));
                ModularContentRecord record =
                    new ModularContentRecord
                    {
                        neoXId = id,
                        wheelAxleLocal =
                            new[] { 1f, 0f, 0f },
                        wheelRollingForwardLocal =
                            new[] { 0f, 0f, 1f }
                    };
                ModularWheelRuntime wheel =
                    CreateWheel(body, Vector3.zero, id);
                wheel.ConfigureTyreElement(
                    record,
                    WheelRoleSettings.Serialize(
                        WheelRoleOverride.Auto),
                    tyreIndex,
                    wheel.transform);
                wheel.SetSprungMass(500f);
                wheel.SetFlightMode(true);
                Physics.SyncTransforms();

                string label = id + "[" + tyreIndex + "]";
                Assert.That(
                    wheel.ProbeContact(Vector3.up, Step),
                    Is.True,
                    label);
                Assert.That(
                    wheel.ContactPoint.y,
                    Is.GreaterThan(ridgeTop - 0.02f),
                    label);
                Assert.That(
                    wheel.Telemetry.penetrationDepth,
                    Is.LessThan(0.02f),
                    label);
            }
        }
    }

    [Test]
    public void FinalSweep_GroundedWheelBlocksLateUpwardThinCeiling()
    {
        CreateFlatGround();
        const float ceilingBottom = 1.08f;
        CreateBox(
            "ThinCeiling",
            new Vector3(0f, ceilingBottom + 0.01f, 0f),
            new Vector3(8f, 0.02f, 8f));
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.68f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetAutomaticRole(WheelRoleOverride.FreeRolling);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        ledger.AddAcceleration(Vector3.down * 9.81f);
        Assert.That(
            wheel.ProbeContact(Vector3.up, Step, ledger),
            Is.True);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);
        wheel.ApplyForces(
            Vector3.up,
            0f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);
        ledger.AddAcceleration(Vector3.up * 3000f);
        wheel.PrepareFinalConstraints(
            Vector3.up,
            9.81f,
            Step,
            ledger);
        for (int iteration = 0; iteration < 8; iteration++)
            wheel.ApplyConstraintCorrection(Step, ledger);

        Assert.That(wheel.Telemetry.obstacleContact, Is.True);
        Assert.That(wheel.Telemetry.obstacleNormal.y,
            Is.LessThan(-0.9f));
        Assert.That(wheel.Telemetry.obstacleForce,
            Is.GreaterThan(0f));
        IntegrateBody(body, ledger);
        Vector3 constrainedTyreCenter =
            wheel.NeutralWheelCenterWorld -
            wheel.SuspensionUpWorld *
            wheel.CurrentVisualDroop;
        float tyreTop =
            constrainedTyreCenter.y +
            wheel.Profile.radius;
        Assert.That(
            tyreTop,
            Is.LessThanOrEqualTo(ceilingBottom + 0.015f));
    }

    [Test]
    public void ContinuousMesh_LowRampIsVisibleToFinalSweep()
    {
        CreateContinuousGroundWithLowRamp(
            0.62f,
            0.04f,
            0.012f);
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.68f, 0f));
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetAutomaticRole(WheelRoleOverride.FreeRolling);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        var ledger = new VehicleForceLedger();
        ledger.Begin(body);
        ledger.AddAcceleration(Vector3.down * 9.81f);
        Assert.That(
            wheel.ProbeContact(Vector3.up, Step, ledger),
            Is.True);
        wheel.PrepareSuspension(Vector3.up, 9.81f, Step);
        wheel.ApplyForces(
            Vector3.up,
            0f,
            0f,
            false,
            false,
            1f,
            Step,
            ledger);
        ledger.AddAcceleration(Vector3.forward * 3000f);
        wheel.PrepareFinalConstraints(
            Vector3.up,
            9.81f,
            Step,
            ledger);
        for (int iteration = 0; iteration < 8; iteration++)
            wheel.ApplyConstraintCorrection(Step, ledger);

        Assert.That(wheel.Telemetry.obstacleContact, Is.True);
        Assert.That(wheel.Telemetry.obstacleForce,
            Is.GreaterThan(0f));
    }

    [Test]
    public void QueryBuffers_DenseSelfCollidersDoNotHideGround()
    {
        CreateFlatGround();
        Rigidbody body = CreateBody(
            500f,
            new Vector3(0f, 0.72f, 0f));
        for (int index = 0; index < 320; index++)
        {
            GameObject self =
                new GameObject("DenseSelf_" + index);
            self.transform.SetParent(body.transform, false);
            self.transform.localPosition =
                new Vector3(
                    0f,
                    0.12f + index * 0.0015f,
                    0f);
            BoxCollider collider =
                self.AddComponent<BoxCollider>();
            collider.size =
                new Vector3(0.3f, 0.001f, 0.3f);
        }
        ModularWheelRuntime wheel =
            CreateWheel(body, Vector3.zero, "wheel_basic_111");
        wheel.SetSprungMass(500f);
        wheel.SetFlightMode(true);
        Physics.SyncTransforms();

        Assert.That(wheel.ProbeContact(Vector3.up, Step), Is.True);
        Assert.That(wheel.ContactPoint.y,
            Is.EqualTo(0f).Within(0.02f));
        RaycastHit[] buffer =
            (RaycastHit[])typeof(ModularWheelRuntime)
                .GetField(
                    "hits",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .GetValue(wheel);
        Assert.That(buffer.Length, Is.GreaterThan(256));
    }

    [Test]
    public void PhysicsSceneSimulation_HighSpeedLandingKeepsTyresAboveMesh()
    {
        CreateWaveTerrain(20f, 20f, 12, 12, 0f);
        Rigidbody body = CreateBody(
            1200f,
            new Vector3(0f, 3f, 0f));
        body.rotation =
            Quaternion.AngleAxis(6f, Vector3.forward);
        body.velocity = Vector3.down * 80f;
        VehicleGroundMobility mobility =
            CreateFourWheelMobility(body);
        var ledger = new VehicleForceLedger();
        float minimumTyreBottom = float.PositiveInfinity;
        float maximumTilt = 0f;

        for (int frame = 0; frame < 180; frame++)
        {
            ledger.Begin(body);
            ledger.AddAcceleration(Vector3.down * 9.81f);
            mobility.BeginPhysicsStep(
                Vector3.up,
                9.81f,
                Step,
                ledger);
            mobility.ApplyControl(
                Vector3.up,
                9.81f,
                Vector2.zero,
                false,
                false,
                ledger,
                Step);
            mobility.FinalizePhysicsStep(
                Vector3.up,
                9.81f,
                ledger,
                Step);
            Assert.That(ledger.Apply(), Is.True);
            physicsScene.Simulate(Step);
            Physics.SyncTransforms();

            maximumTilt = Mathf.Max(
                maximumTilt,
                Vector3.Angle(body.transform.up, Vector3.up));
            foreach (ModularWheelRuntime wheel in mobility.Wheels)
            {
                float probeRadius = Mathf.Max(
                    0.04f,
                    Mathf.Min(
                        wheel.Profile.radius * 0.2f,
                        Mathf.Max(
                            wheel.Profile.width * 0.12f,
                            wheel.Profile.radius *
                            Mathf.Sin(Mathf.PI / 64f) *
                            1.05f)));
                Vector3 tyreCenter =
                    wheel.NeutralWheelCenterWorld -
                    wheel.SuspensionUpWorld *
                    wheel.CurrentVisualDroop;
                float radialVerticalExtent =
                    wheel.Profile.radius *
                    Mathf.Sqrt(
                        Mathf.Pow(Vector3.Dot(
                            Vector3.up,
                            wheel.SuspensionUpWorld), 2f) +
                        Mathf.Pow(Vector3.Dot(
                            Vector3.up,
                            wheel.BaseForwardWorld), 2f));
                float axialVerticalExtent =
                    (wheel.Profile.width * 0.5f + probeRadius) *
                    Mathf.Abs(Vector3.Dot(
                        Vector3.up,
                        wheel.BaseAxleWorld));
                minimumTyreBottom = Mathf.Min(
                    minimumTyreBottom,
                    tyreCenter.y -
                    radialVerticalExtent -
                    axialVerticalExtent);
            }
        }

        Assert.That(minimumTyreBottom,
            Is.GreaterThanOrEqualTo(-0.025f));
        Assert.That(maximumTilt, Is.LessThan(22f));
        Assert.That(body.position.y, Is.GreaterThan(0.45f));
    }

    private static float MaximumWheelPenetration(
        VehicleGroundMobility mobility)
    {
        float result = 0f;
        foreach (ModularWheelRuntime wheel in mobility.Wheels)
        {
            result = Mathf.Max(
                result,
                wheel.Telemetry.penetrationDepth);
        }
        return result;
    }

    private static float AverageCompression(
        VehicleGroundMobility mobility)
    {
        float result = 0f;
        foreach (ModularWheelRuntime wheel in mobility.Wheels)
            result += wheel.Compression;
        return result / Mathf.Max(1, mobility.WheelCount);
    }

    private static float MaximumSteerAngle(
        VehicleGroundMobility mobility)
    {
        float result = 0f;
        foreach (ModularWheelRuntime wheel in mobility.Wheels)
            result = Mathf.Max(result, Mathf.Abs(wheel.SteerAngle));
        return result;
    }

    private static void ApplyControlWithoutIntegration(
        Rigidbody body,
        VehicleGroundMobility mobility,
        VehicleForceLedger ledger,
        Vector2 move)
    {
        ledger.Begin(body);
        ledger.AddAcceleration(Vector3.down * 9.81f);
        mobility.BeginPhysicsStep(
            Vector3.up,
            9.81f,
            Step,
            ledger);
        mobility.ApplyControl(
            Vector3.up,
            9.81f,
            move,
            false,
            false,
            ledger,
            Step);
        mobility.FinalizePhysicsStep(
            Vector3.up,
            9.81f,
            ledger,
            Step);
    }

    private void SimulateVehicleStep(
        Rigidbody body,
        VehicleGroundMobility mobility,
        VehicleForceLedger ledger,
        Vector2 move)
    {
        ledger.Begin(body);
        ledger.AddAcceleration(Vector3.down * 9.81f);
        mobility.BeginPhysicsStep(
            Vector3.up,
            9.81f,
            Step,
            ledger);
        mobility.ApplyControl(
            Vector3.up,
            9.81f,
            move,
            false,
            false,
            ledger,
            Step);
        mobility.FinalizePhysicsStep(
            Vector3.up,
            9.81f,
            ledger,
            Step);
        IntegrateBody(body, ledger);
    }

    private VehicleGroundMobility CreateFourWheelMobility(
        Rigidbody body)
    {
        ModularWheelRuntime[] wheels =
        {
            CreateWheel(
                body,
                new Vector3(-1.2f, 0f, -1.5f),
                "wheel_basic_111"),
            CreateWheel(
                body,
                new Vector3(1.2f, 0f, -1.5f),
                "wheel_basic_111"),
            CreateWheel(
                body,
                new Vector3(-1.2f, 0f, 1.5f),
                "wheel_basic_111"),
            CreateWheel(
                body,
                new Vector3(1.2f, 0f, 1.5f),
                "wheel_basic_111")
        };
        wheels[1].transform.localRotation =
            Quaternion.AngleAxis(180f, Vector3.up);
        wheels[3].transform.localRotation =
            Quaternion.AngleAxis(180f, Vector3.up);
        float authoredLateralOffset =
            Mathf.Abs(wheels[0].TyreCenterLocal.x);
        wheels[0].transform.localPosition +=
            Vector3.left * authoredLateralOffset;
        wheels[2].transform.localPosition +=
            Vector3.left * authoredLateralOffset;
        wheels[1].transform.localPosition +=
            Vector3.right * authoredLateralOffset;
        wheels[3].transform.localPosition +=
            Vector3.right * authoredLateralOffset;
        var mobility = new VehicleGroundMobility();
        mobility.Rebuild(body, body.transform, wheels);
        mobility.SetFlightMode(true);
        Physics.SyncTransforms();
        return mobility;
    }

    private Rigidbody CreateBody(float mass, Vector3 position)
    {
        GameObject target = new GameObject("WheelTestBody");
        SceneManager.MoveGameObjectToScene(target, testScene);
        target.transform.position = TestOrigin + position;
        Rigidbody body = target.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = mass;
        body.drag = 0f;
        body.angularDrag = 0f;
        body.maxAngularVelocity = 12f;
        body.inertiaTensorRotation = Quaternion.identity;
        body.inertiaTensor = new Vector3(1800f, 2200f, 2500f);
        return body;
    }

    private ModularWheelRuntime CreateWheel(
        Rigidbody body,
        Vector3 localPosition,
        string neoXId)
    {
        GameObject target = new GameObject("Wheel_" + neoXId);
        target.transform.SetParent(body.transform, false);
        target.transform.localPosition = localPosition;
        ModularWheelRuntime wheel =
            target.AddComponent<ModularWheelRuntime>();
        wheel.Configure(
            new ModularContentRecord
            {
                neoXId = neoXId,
                wheelAxleLocal = new[] { 1f, 0f, 0f },
                wheelRollingForwardLocal = new[] { 0f, 0f, 1f }
            },
            WheelRoleSettings.Serialize(WheelRoleOverride.Auto));
        wheel.BindVehicle(body, body.transform);
        return wheel;
    }

    private static Transform BindRollingVisual(
        ModularWheelRuntime wheel,
        string suffix)
    {
        GameObject visual =
            new GameObject("WheelVisual_" + suffix);
        visual.transform.SetParent(
            wheel.ModuleRoot,
            false);
        Transform rolling =
            new GameObject("wheel").transform;
        rolling.SetParent(visual.transform, false);
        wheel.BindVisual(visual.transform);
        return rolling;
    }

    private void CreateFlatGround(PhysicMaterial material = null)
    {
        GameObject ground = new GameObject("FlatGround");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        ground.transform.position =
            TestOrigin + new Vector3(0f, -0.5f, 0f);
        BoxCollider collider = ground.AddComponent<BoxCollider>();
        collider.size = new Vector3(200f, 1f, 200f);
        collider.sharedMaterial = material;
    }

    private void CreateSlopedGround(float slopeDegrees)
    {
        GameObject ground = new GameObject("SlopedGround");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        ground.transform.rotation =
            Quaternion.AngleAxis(slopeDegrees, Vector3.right);
        Vector3 normal = ground.transform.up;
        ground.transform.position =
            TestOrigin - normal * 0.5f;
        BoxCollider collider = ground.AddComponent<BoxCollider>();
        collider.size = new Vector3(20f, 1f, 20f);
    }

    private void CreateContinuousGroundWithLowRamp(
        float startZ,
        float length,
        float height)
    {
        GameObject ground =
            new GameObject("ContinuousGroundWithLowRamp");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        const float halfWidth = 6f;
        const float back = -6f;
        const float front = 6f;
        float endZ = startZ + length;
        var mesh = new Mesh
        {
            name = "ContinuousGroundWithLowRampMesh",
            vertices = new[]
            {
                new Vector3(-halfWidth, 0f, back),
                new Vector3(halfWidth, 0f, back),
                new Vector3(-halfWidth, 0f, startZ),
                new Vector3(halfWidth, 0f, startZ),
                new Vector3(-halfWidth, height, endZ),
                new Vector3(halfWidth, height, endZ),
                new Vector3(-halfWidth, 0f, front),
                new Vector3(halfWidth, 0f, front)
            },
            triangles = new[]
            {
                0, 2, 1, 1, 2, 3,
                2, 4, 3, 3, 4, 5,
                4, 6, 5, 5, 6, 7
            }
        };
        mesh.RecalculateNormals();
        MeshCollider collider =
            ground.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
    }

    private void CreateBox(
        string name,
        Vector3 position,
        Vector3 size)
    {
        GameObject target = new GameObject(name);
        SceneManager.MoveGameObjectToScene(target, testScene);
        target.transform.position = TestOrigin + position;
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.size = size;
    }

    private Rigidbody CreateDynamicGround(
        float mass,
        Vector3 position,
        Vector3 size)
    {
        GameObject target = new GameObject("DynamicGround");
        SceneManager.MoveGameObjectToScene(target, testScene);
        target.transform.position = TestOrigin + position;
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.size = size;
        Rigidbody result = target.AddComponent<Rigidbody>();
        result.useGravity = false;
        result.mass = mass;
        result.drag = 0f;
        result.angularDrag = 0f;
        return result;
    }

    private void CreateThinRamp(
        float startZ,
        float length,
        float height,
        float width)
    {
        GameObject ground = new GameObject("ThinClimbableRamp");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        float halfWidth = width * 0.5f;
        var mesh = new Mesh
        {
            name = "WheelPhysicsThinRamp",
            vertices = new[]
            {
                new Vector3(-halfWidth, 0f, startZ),
                new Vector3(halfWidth, 0f, startZ),
                new Vector3(-halfWidth, height, startZ + length),
                new Vector3(halfWidth, height, startZ + length)
            },
            triangles = new[] { 0, 2, 1, 1, 2, 3 }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        MeshCollider collider = ground.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
    }

    private void CreateSlopeTerrain(
        float width,
        float length,
        float slope)
    {
        GameObject ground = new GameObject("SlopeMeshGround");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        ground.transform.position = TestOrigin;
        float halfWidth = width * 0.5f;
        float halfLength = length * 0.5f;
        var mesh = new Mesh
        {
            name = "WheelPhysicsSlopeMesh",
            vertices = new[]
            {
                new Vector3(-halfWidth, -halfLength * slope, -halfLength),
                new Vector3(halfWidth, -halfLength * slope, -halfLength),
                new Vector3(-halfWidth, halfLength * slope, halfLength),
                new Vector3(halfWidth, halfLength * slope, halfLength)
            },
            triangles = new[] { 0, 2, 1, 1, 2, 3 }
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        MeshCollider collider = ground.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
    }

    private void CreateWaveTerrain(
        float width,
        float length,
        int widthSegments,
        int lengthSegments,
        float amplitude)
    {
        GameObject ground = new GameObject("WaveMeshGround");
        SceneManager.MoveGameObjectToScene(ground, testScene);
        ground.transform.position = TestOrigin;
        int row = widthSegments + 1;
        var vertices = new Vector3[
            (widthSegments + 1) * (lengthSegments + 1)];
        for (int z = 0; z <= lengthSegments; z++)
        {
            float zPosition =
                (z / (float)lengthSegments - 0.5f) * length;
            for (int x = 0; x <= widthSegments; x++)
            {
                float xPosition =
                    (x / (float)widthSegments - 0.5f) * width;
                float height =
                    Mathf.Sin(zPosition * 0.55f) * amplitude +
                    Mathf.Sin(
                        zPosition * 0.21f +
                        xPosition * 0.35f) *
                    amplitude * 0.35f;
                vertices[z * row + x] =
                    new Vector3(xPosition, height, zPosition);
            }
        }

        var triangles =
            new int[widthSegments * lengthSegments * 6];
        int triangle = 0;
        for (int z = 0; z < lengthSegments; z++)
        {
            for (int x = 0; x < widthSegments; x++)
            {
                int a = z * row + x;
                int b = a + 1;
                int c = a + row;
                int d = c + 1;
                triangles[triangle++] = a;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = d;
            }
        }

        var mesh = new Mesh
        {
            name = "WheelPhysicsWaveMesh",
            vertices = vertices,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        MeshCollider collider = ground.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
    }

    private static void IntegrateBody(
        Rigidbody body,
        VehicleForceLedger ledger)
    {
        Vector3 acceleration =
            ledger.TotalForce / Mathf.Max(0.01f, body.mass);
        body.velocity += acceleration * Step;
        // Preview-scene EditMode tests do not run a PhysX simulation step.
        // Update the Transform explicitly so child wheel anchors and collider
        // queries observe the same integrated pose as the Rigidbody.
        body.transform.position += body.velocity * Step;

        Vector3 localTorque =
            body.transform.InverseTransformDirection(
                ledger.TotalTorque);
        Vector3 inertia = body.inertiaTensor;
        Vector3 localAngularAcceleration = new Vector3(
            localTorque.x / Mathf.Max(0.01f, inertia.x),
            localTorque.y / Mathf.Max(0.01f, inertia.y),
            localTorque.z / Mathf.Max(0.01f, inertia.z));
        Vector3 worldAngularAcceleration =
            body.transform.TransformDirection(
                localAngularAcceleration);
        body.angularVelocity +=
            worldAngularAcceleration * Step;
        float angularStep =
            body.angularVelocity.magnitude * Step;
        if (angularStep > 0.000001f)
        {
            body.transform.rotation =
                Quaternion.AngleAxis(
                    angularStep * Mathf.Rad2Deg,
                    body.angularVelocity.normalized) *
                body.transform.rotation;
        }
        Physics.SyncTransforms();
    }
}
