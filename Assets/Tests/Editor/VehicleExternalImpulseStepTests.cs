using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.ModularAssembly;

public sealed class VehicleExternalImpulseStepTests
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance |
        BindingFlags.NonPublic;

    private Scene previousActiveScene;
    private Scene testScene;
    private PhysicsScene physicsScene;

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
        if (previousActiveScene.IsValid())
            SceneManager.SetActiveScene(previousActiveScene);
        if (testScene.IsValid())
            EditorSceneManager.ClosePreviewScene(testScene);
    }

    [Test]
    public void ReactionBeforeTargetStep_QueuesThenAppliesExactlyOnce()
    {
        RobocraftMotionCoordinator coordinator =
            CreateCoordinator(10f, out Rigidbody target);
        VehicleForceLedger ledger = GetLedger(coordinator);
        Vector3 impulse = new Vector3(20f, 0f, 0f);
        Vector3 point = target.worldCenterOfMass + Vector3.up;
        double stepTime = 12d;

        SetField(coordinator, "physicsStepCompleted", true);
        SetField(coordinator, "completedPhysicsStepTime", 11d);
        RouteImpulse(
            coordinator,
            impulse,
            point,
            true,
            stepTime);

        AssertVector(
            GetField<Vector3>(
                coordinator,
                "queuedExternalImpulseWorld"),
            impulse);
        ledger.Begin(target);
        Invoke(coordinator, "ApplyQueuedExternalImpulses");

        float step = Time.fixedDeltaTime;
        AssertVector(
            ledger.TotalForce,
            impulse / step);
        AssertVector(
            ledger.TotalTorque,
            Vector3.Cross(
                point - target.worldCenterOfMass,
                impulse) / step);
        Assert.That(ledger.Apply(), Is.True);
        Assert.That(ledger.Apply(), Is.False);

        physicsScene.Simulate(step);

        AssertVector(
            target.velocity,
            impulse / target.mass,
            0.001f);
    }

    [Test]
    public void ReactionAfterTargetStep_AppliesSameStepAndOnlyRecordsAudit()
    {
        RobocraftMotionCoordinator coordinator =
            CreateCoordinator(10f, out Rigidbody target);
        VehicleForceLedger ledger = GetLedger(coordinator);
        Vector3 impulse = new Vector3(20f, 0f, 0f);
        Vector3 point = target.worldCenterOfMass + Vector3.up;
        double stepTime = 23d;

        ledger.Begin(target);
        Assert.That(ledger.Apply(), Is.True);
        SetField(coordinator, "physicsStepCompleted", true);
        SetField(
            coordinator,
            "completedPhysicsStepTime",
            stepTime);
        RouteImpulse(
            coordinator,
            impulse,
            point,
            true,
            stepTime);

        float step = Time.fixedDeltaTime;
        AssertVector(
            GetField<Vector3>(
                coordinator,
                "queuedExternalImpulseWorld"),
            Vector3.zero);
        AssertVector(
            ledger.TotalForce,
            impulse / step);
        AssertVector(
            ledger.TotalTorque,
            Vector3.Cross(
                point - target.worldCenterOfMass,
                impulse) / step);
        AssertVector(
            coordinator.Telemetry.netForceWorld,
            impulse / step);
        AssertVector(
            coordinator.Telemetry.netTorqueWorld,
            Vector3.Cross(
                point - target.worldCenterOfMass,
                impulse) / step);
        Assert.That(
            ledger.Apply(),
            Is.False,
            "A late audit record must not resubmit the completed ledger.");

        physicsScene.Simulate(step);

        AssertVector(
            target.velocity,
            impulse / target.mass,
            0.001f);
    }

    [Test]
    public void SourceLedger_ToVehicleSink_PredictsAndAppliesOnce()
    {
        RobocraftMotionCoordinator coordinator =
            CreateCoordinator(8f, out Rigidbody target);
        VehicleForceLedger targetLedger = GetLedger(coordinator);
        Rigidbody source = CreateBody("ImpulseSource", 12f);
        source.position = Vector3.right * 10f;
        var sourceLedger = new VehicleForceLedger();
        sourceLedger.Begin(source);
        Vector3 impulse = new Vector3(0f, 0f, 16f);
        Vector3 point = target.worldCenterOfMass;

        sourceLedger.AddExternalImpulse(
            target,
            impulse,
            point);

        AssertVector(
            sourceLedger.PredictedExternalPointVelocity(
                target,
                point),
            impulse / target.mass);
        AssertVector(
            GetField<Vector3>(
                coordinator,
                "queuedExternalImpulseWorld"),
            impulse);

        targetLedger.Begin(target);
        Invoke(coordinator, "ApplyQueuedExternalImpulses");
        Assert.That(targetLedger.Apply(), Is.True);
        Assert.That(targetLedger.Apply(), Is.False);
        physicsScene.Simulate(Time.fixedDeltaTime);

        AssertVector(
            target.velocity,
            impulse / target.mass,
            0.001f);
    }

    [Test]
    public void GeneralExternalImpulse_RemainsQueuedForAuditedNextStep()
    {
        RobocraftMotionCoordinator coordinator =
            CreateCoordinator(10f, out Rigidbody target);
        VehicleForceLedger ledger = GetLedger(coordinator);
        Vector3 impulse = new Vector3(0f, 15f, 0f);
        Vector3 point = target.worldCenterOfMass;

        ledger.Begin(target);
        Assert.That(ledger.Apply(), Is.True);
        SetField(coordinator, "physicsStepCompleted", true);
        SetField(
            coordinator,
            "completedPhysicsStepTime",
            34d);
        VehicleExternalForces.ApplyImpulse(
            target,
            impulse,
            point);

        AssertVector(
            GetField<Vector3>(
                coordinator,
                "queuedExternalImpulseWorld"),
            impulse);
        AssertVector(ledger.TotalForce, Vector3.zero);
        physicsScene.Simulate(Time.fixedDeltaTime);
        AssertVector(target.velocity, Vector3.zero);

        ledger.Begin(target);
        Invoke(coordinator, "ApplyQueuedExternalImpulses");
        Assert.That(ledger.Apply(), Is.True);
        Assert.That(ledger.Apply(), Is.False);
        physicsScene.Simulate(Time.fixedDeltaTime);

        AssertVector(
            target.velocity,
            impulse / target.mass,
            0.001f);
    }

    [Test]
    public void InactiveVehicleSink_FallsBackWithoutDroppingReaction()
    {
        RobocraftMotionCoordinator coordinator =
            CreateCoordinator(10f, out Rigidbody target);
        SetField(coordinator, "active", false);
        Rigidbody source = CreateBody("InactiveSinkSource", 12f);
        source.position = Vector3.right * 10f;
        var sourceLedger = new VehicleForceLedger();
        sourceLedger.Begin(source);
        Vector3 impulse = new Vector3(0f, 0f, 20f);

        sourceLedger.AddExternalImpulse(
            target,
            impulse,
            target.worldCenterOfMass);

        AssertVector(
            GetField<Vector3>(
                coordinator,
                "queuedExternalImpulseWorld"),
            Vector3.zero);
        physicsScene.Simulate(Time.fixedDeltaTime);
        AssertVector(
            target.velocity,
            impulse / target.mass,
            0.001f);
    }

    private RobocraftMotionCoordinator CreateCoordinator(
        float mass,
        out Rigidbody body)
    {
        GameObject root = new GameObject("VehicleImpulseTarget");
        SceneManager.MoveGameObjectToScene(root, testScene);
        root.AddComponent<BoxCollider>();
        body = root.AddComponent<Rigidbody>();
        body.mass = mass;
        body.useGravity = false;
        body.drag = 0f;
        body.angularDrag = 0f;
        body.ResetInertiaTensor();

        RobocraftMotionCoordinator coordinator =
            root.AddComponent<RobocraftMotionCoordinator>();
        SetField(coordinator, "body", body);
        SetField(coordinator, "active", true);
        SetField(
            coordinator,
            "physicsOwnershipExclusive",
            true);
        return coordinator;
    }

    private Rigidbody CreateBody(
        string name,
        float mass)
    {
        GameObject root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, testScene);
        root.AddComponent<BoxCollider>();
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = mass;
        body.useGravity = false;
        body.drag = 0f;
        body.angularDrag = 0f;
        body.ResetInertiaTensor();
        return body;
    }

    private static VehicleForceLedger GetLedger(
        RobocraftMotionCoordinator coordinator)
    {
        return GetField<VehicleForceLedger>(
            coordinator,
            "ledger");
    }

    private static void RouteImpulse(
        RobocraftMotionCoordinator coordinator,
        Vector3 impulse,
        Vector3 point,
        bool inFixedTimeStep,
        double fixedStepTime)
    {
        Invoke(
            coordinator,
            "RouteExternalImpulse",
            impulse,
            point,
            inFixedTimeStep,
            fixedStepTime);
    }

    private static T GetField<T>(
        object target,
        string name)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            InstanceFields);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(target);
    }

    private static void SetField<T>(
        object target,
        string name,
        T value)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            InstanceFields);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }

    private static object Invoke(
        object target,
        string name,
        params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(
            name,
            InstanceFields);
        Assert.That(method, Is.Not.Null, name);
        return method.Invoke(target, arguments);
    }

    private static void AssertVector(
        Vector3 actual,
        Vector3 expected,
        float tolerance = 0.0001f)
    {
        Assert.That(
            Vector3.Distance(actual, expected),
            Is.LessThanOrEqualTo(tolerance),
            $"Expected {expected}, actual {actual}.");
    }
}
