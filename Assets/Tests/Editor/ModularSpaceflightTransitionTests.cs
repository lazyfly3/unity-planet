using System.Linq;
using ModularAssembly;
using NUnit.Framework;
using SpacecraftEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;

public sealed class ModularSpaceflightTransitionTests
{
    GridModuleDefinition core;
    GridModuleDefinition structure;

    [SetUp]
    public void SetUp()
    {
        core = Definition(
            "core",
            GridModuleCategory.Core,
            new Vector3Int(2, 2, 2));
        structure = Definition(
            "structure",
            GridModuleCategory.Structure,
            Vector3Int.one);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(core);
        Object.DestroyImmediate(structure);
    }

    [TestCase(false, false, false, SpacecraftBlueprintRoute.ModularAssembly)]
    [TestCase(false, true, false, SpacecraftBlueprintRoute.LegacyWorkshop)]
    [TestCase(true, true, false, SpacecraftBlueprintRoute.ModularAssembly)]
    [TestCase(false, true, true, SpacecraftBlueprintRoute.ModularAssembly)]
    public void RouteResolverPreservesLegacyOnlySlots(
        bool hasModular,
        bool hasLegacy,
        bool forceModular,
        SpacecraftBlueprintRoute expected)
    {
        Assert.That(
            SpacecraftBlueprintRouteResolver.Resolve(
                hasModular,
                hasLegacy,
                forceModular),
            Is.EqualTo(expected));
    }

    [Test]
    public void StrictRestoreRejectsUnknownModulesWithoutChangingDesign()
    {
        GridAssemblyModel model = CreateModelWithStructure();
        ModularBlueprintData before = model.CaptureBlueprint();
        ModularBlueprintData invalid = model.CaptureBlueprint();
        invalid.modules = invalid.modules.Concat(new[]
        {
            new ModularBlueprintModule
            {
                runtimeId = "missing-runtime",
                moduleId = "missing-module",
                pose = new GridModulePose(new Vector3Int(2, 0, 0), 0)
            }
        }).ToArray();

        Assert.That(model.RestoreBlueprint(invalid, out string error), Is.False);
        Assert.That(error, Does.Contain("missing-module"));
        AssertBlueprintEqual(before, model.CaptureBlueprint());
    }

    [Test]
    public void StrictRestoreRollsBackDisconnectedBlueprint()
    {
        GridAssemblyModel model = CreateModelWithStructure();
        ModularBlueprintData before = model.CaptureBlueprint();
        ModularBlueprintData invalid = model.CaptureBlueprint();
        ModularBlueprintModule moved = invalid.modules.First(item =>
            item.runtimeId != GridAssemblyModel.CoreRuntimeId);
        moved.pose = new GridModulePose(new Vector3Int(20, 0, 0), 0);

        Assert.That(model.RestoreBlueprint(invalid, out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
        AssertBlueprintEqual(before, model.CaptureBlueprint());
    }

    [Test]
    public void VacuumProviderHasNoContinuousEnvironmentalForces()
    {
        GameObject root = new GameObject("VacuumProviderTest");
        try
        {
            VacuumEnvironmentProvider provider =
                root.AddComponent<VacuumEnvironmentProvider>();
            PlanetEnvironmentSample sample = provider.Sample(
                new Vector3(120f, -4f, 900f),
                1234d);

            Assert.That(sample.gravityAcceleration, Is.EqualTo(Vector3.zero));
            Assert.That(sample.airDensity, Is.Zero);
            Assert.That(sample.ambientPressure, Is.Zero);
            Assert.That(sample.meanWindVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(sample.gustVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(sample.atmosphereVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(sample.hasAtmosphere, Is.False);
            Assert.That(sample.hasSurface, Is.False);
            Assert.That(float.IsPositiveInfinity(sample.surfaceDistance), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InterstellarModularLoaderStartsWithTrialPhysicsInVacuum()
    {
        GameObject runtimeRoot =
            new GameObject("InterstellarModularLoaderTest");
        GameObject vehicleRoot = null;
        try
        {
            InterstellarModularVehicleLoader loader =
                runtimeRoot.AddComponent<
                    InterstellarModularVehicleLoader>();
            loader.Prepare(null);
            Rigidbody body = loader.Body;
            vehicleRoot = body.gameObject;

            Assert.That(loader.ShipController.IsModular, Is.True);
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.drag, Is.Zero);
            Assert.That(body.angularDrag, Is.Zero);
            Assert.That(
                vehicleRoot.GetComponent<SpacecraftIfcsMotor>(),
                Is.Null);
            Assert.That(
                vehicleRoot.GetComponent<KeyboardMouseFlightInput>(),
                Is.Null);
            Assert.That(
                vehicleRoot.GetComponent<IRobocraftPilotAimSource>(),
                Is.SameAs(
                    vehicleRoot.GetComponent<
                        ModularInterstellarControlAdapter>()));
        }
        finally
        {
            if (vehicleRoot != null)
                Object.DestroyImmediate(vehicleRoot);
            Object.DestroyImmediate(runtimeRoot);
        }
    }

    [Test]
    public void PlanarSurfaceModularLoaderStartsWithExclusiveRc3Physics()
    {
        GameObject runtimeRoot =
            new GameObject("PlanarSurfaceModularLoaderTest");
        GameObject vehicleRoot = null;
        try
        {
            PlanarSurfaceModularVehicleLoader loader =
                runtimeRoot.AddComponent<
                    PlanarSurfaceModularVehicleLoader>();
            loader.Prepare(null, null);
            Rigidbody body = loader.Body;
            vehicleRoot = body.gameObject;

            Assert.That(body.useGravity, Is.False);
            Assert.That(body.drag, Is.Zero);
            Assert.That(body.angularDrag, Is.Zero);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(
                vehicleRoot.GetComponent<SpacecraftIfcsMotor>(),
                Is.Null);
            Assert.That(
                vehicleRoot.GetComponent<KeyboardMouseFlightInput>(),
                Is.Null);
            Assert.That(
                vehicleRoot.GetComponent<PlanetSurfaceFlightEnvironment>(),
                Is.Null);
            Assert.That(
                vehicleRoot.GetComponent<SurfaceSpacecraftController>(),
                Is.Null,
                "Vehicle-only planet entry must not add the legacy F disembark controller.");
            Assert.That(
                vehicleRoot.GetComponent<IRobocraftPilotAimSource>(),
                Is.TypeOf<PlanarSurfacePilotAimSource>());
        }
        finally
        {
            if (vehicleRoot != null)
                Object.DestroyImmediate(vehicleRoot);
            Object.DestroyImmediate(runtimeRoot);
        }
    }

    [Test]
    public void ModularSpaceAimUsesWorldHeadingAndYieldsToExternalControl()
    {
        GameObject root = new GameObject("ModularSpaceAimTest");
        try
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.rotation = Quaternion.Euler(-18f, 73f, 0f);
            ModularInterstellarControlAdapter controls =
                root.AddComponent<ModularInterstellarControlAdapter>();
            controls.Configure(body, null);
            controls.ControlsEnabled = true;

            Assert.That(
                controls.TryGetPilotAim(out Vector3 initialAim),
                Is.True);
            Assert.That(
                Vector3.Angle(initialAim, body.transform.forward),
                Is.LessThan(0.01f));

            controls.SetExternalWorldAttitudeTarget(
                Quaternion.LookRotation(Vector3.left, Vector3.up));
            Assert.That(
                controls.TryGetPilotAim(out _),
                Is.False,
                "Cruise and landing targets must own attitude control.");

            body.rotation = Quaternion.Euler(11f, -42f, 0f);
            controls.ClearExternalTargets();
            Assert.That(
                controls.TryGetPilotAim(out Vector3 restoredAim),
                Is.True);
            Assert.That(
                Vector3.Angle(restoredAim, body.transform.forward),
                Is.LessThan(0.01f));

            controls.ControlsEnabled = false;
            Assert.That(controls.TryGetPilotAim(out _), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    GridAssemblyModel CreateModelWithStructure()
    {
        var model = new GridAssemblyModel(new[] { core, structure });
        Assert.That(model.TryPlace(
            structure.ModuleId,
            new GridModulePose(new Vector3Int(1, 0, 0), 0),
            false,
            out _,
            out string error), Is.True, error);
        return model;
    }

    static void AssertBlueprintEqual(
        ModularBlueprintData expected,
        ModularBlueprintData actual)
    {
        Assert.That(actual.modules.Length, Is.EqualTo(expected.modules.Length));
        for (int index = 0; index < expected.modules.Length; index++)
        {
            Assert.That(actual.modules[index].runtimeId,
                Is.EqualTo(expected.modules[index].runtimeId));
            Assert.That(actual.modules[index].moduleId,
                Is.EqualTo(expected.modules[index].moduleId));
            Assert.That(actual.modules[index].pose.Origin,
                Is.EqualTo(expected.modules[index].pose.Origin));
        }
    }

    static GridModuleDefinition Definition(
        string id,
        GridModuleCategory category,
        Vector3Int footprint)
    {
        GridModuleDefinition definition =
            ScriptableObject.CreateInstance<GridModuleDefinition>();
        definition.Configure(
            id,
            id,
            category,
            null,
            footprint,
            100f,
            100f,
            0f,
            100f,
            0f,
            null);
        return definition;
    }
}
