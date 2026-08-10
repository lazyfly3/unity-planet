using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ModularAssembly;
using NUnit.Framework;
using UnityEngine;
using UnityPlanet.ModularAssembly;
using Object = UnityEngine.Object;

public sealed class ModularAssemblyLabTests
{
    const float AuthoredGeometryTolerance = 0.0001f;
    const float MediumGeometryTolerance = 0.001f;

    [Test]
    public void PresetLibraryDoesNotRetainProceduralDotPreviewRenderer()
    {
        Type removedRenderer = typeof(ModularPresetLibraryUi).Assembly.GetType(
            "UnityPlanet.ModularAssembly.ModularPresetPreviewRenderer");

        Assert.That(
            removedRenderer,
            Is.Null,
            "The dotted blueprint fallback must not return to the preset UI.");
    }

    [Test]
    public void PresetLibraryActualPreviewRendersRealMeshPixels()
    {
        var stage = new GameObject("PresetPreviewTestStage");
        Texture2D preview = null;
        try
        {
            stage.layer = 30;
            GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.transform.SetParent(stage.transform, false);
            mesh.layer = 30;
            mesh.transform.localScale = new Vector3(2f, 1f, 3f);

            MethodInfo render = typeof(ModularPresetLibraryUi).GetMethod(
                "RenderActualPreviewTexture",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(render, Is.Not.Null);
            preview = (Texture2D)render.Invoke(null, new object[] { stage });

            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.name, Is.EqualTo("ModularPresetActualPreview"));
            Assert.That(preview.width, Is.EqualTo(512));
            Assert.That(preview.height, Is.EqualTo(288));
            Color32 background = new Color(0.025f, 0.07f, 0.09f, 1f);
            int meshPixels = preview.GetPixels32().Count(pixel =>
                Math.Abs(pixel.r - background.r) > 2 ||
                Math.Abs(pixel.g - background.g) > 2 ||
                Math.Abs(pixel.b - background.b) > 2);
            Assert.That(
                meshPixels,
                Is.GreaterThan(100),
                "The preview must contain rendered model geometry, not only a background.");
        }
        finally
        {
            if (preview != null)
                Object.DestroyImmediate(preview);
            Object.DestroyImmediate(stage);
        }
    }

    static readonly ExpectedWheelGeometry[] SupportedWheelGeometry =
    {
        new ExpectedWheelGeometry(
            "wheel_basic_111",
            new ExpectedTyreGeometry(
                new Vector3(0.2070303f, -0.1057799f, 0f),
                0.3990774f,
                0.4337587f)),
        new ExpectedWheelGeometry(
            "wheel_m_222",
            new ExpectedTyreGeometry(
                new Vector3(0.3084659f, -0.2324381f, 0f),
                0.7797695f,
                0.8671514f)),
        new ExpectedWheelGeometry(
            "speedwheel_small_l_322",
            new ExpectedTyreGeometry(
                new Vector3(
                    -0.4267096f,
                    -0.1431745f,
                    -0.0014590f),
                0.5419834f,
                0.5624151f)),
        new ExpectedWheelGeometry(
            "speedwheel_small_r_322",
            new ExpectedTyreGeometry(
                new Vector3(
                    0.4267096f,
                    -0.1431745f,
                    -0.0014590f),
                0.5419834f,
                0.5624151f)),
        new ExpectedWheelGeometry(
            "speedwheel_large_l_522",
            new ExpectedTyreGeometry(
                new Vector3(
                    -0.0110487f,
                    -0.2627589f,
                    -0.9597507f),
                0.7343171f,
                1.0092570f),
            new ExpectedTyreGeometry(
                new Vector3(
                    -0.0110488f,
                    -0.2656147f,
                    0.4424138f),
                0.7343853f,
                1.0092570f)),
        new ExpectedWheelGeometry(
            "speedwheel_large_r_522",
            new ExpectedTyreGeometry(
                new Vector3(
                    0.0110487f,
                    -0.2627589f,
                    -0.9597507f),
                0.7343171f,
                1.0092570f),
            new ExpectedTyreGeometry(
                new Vector3(
                    0.0110488f,
                    -0.2656147f,
                    0.4424138f),
                0.7343853f,
                1.0092570f))
    };

    static readonly ExpectedWheelVisual[] SupportedWheelVisuals =
    {
        new ExpectedWheelVisual(
            "wheel_basic_111",
            56,
            new[] { 190 },
            new[]
            {
                new Vector3(
                    -0.028484f,
                    -0.104422f,
                    0.004000f)
            },
            new[] { 0 }),
        new ExpectedWheelVisual(
            "wheel_m_222",
            226,
            new[] { 704 },
            new[]
            {
                new Vector3(
                    0.299884f,
                    -0.236527f,
                    0f)
            },
            new[] { 0 }),
        new ExpectedWheelVisual(
            "speedwheel_small_l_322",
            362,
            new[] { 128 },
            new[]
            {
                new Vector3(
                    -0.419953f,
                    -0.143185f,
                    -0.001460f)
            },
            new[] { 0 }),
        new ExpectedWheelVisual(
            "speedwheel_small_r_322",
            362,
            new[] { 128 },
            new[]
            {
                new Vector3(
                    0.428964f,
                    -0.143185f,
                    -0.001460f)
            },
            new[] { 0 }),
        new ExpectedWheelVisual(
            "speedwheel_large_l_522",
            235,
            new[] { 44, 44 },
            new[]
            {
                new Vector3(
                    -0.045734f,
                    -0.267561f,
                    0.443487f),
                new Vector3(
                    -0.045734f,
                    -0.267561f,
                    -0.959749f)
            },
            new[] { 1, 0 }),
        new ExpectedWheelVisual(
            "speedwheel_large_r_522",
            235,
            new[] { 44, 44 },
            new[]
            {
                new Vector3(
                    0.026669f,
                    -0.264056f,
                    0.443487f),
                new Vector3(
                    0.026669f,
                    -0.264056f,
                    -0.959749f)
            },
            new[] { 1, 0 })
    };

    GridModuleDefinition core;
    GridModuleDefinition structure;
    GridModuleDefinition battery;
    GridModuleDefinition thruster;

    [SetUp]
    public void SetUp()
    {
        core = Definition("core", GridModuleCategory.Core, new Vector3Int(2, 2, 2), 1000f, 100f, 0f, 0f);
        structure = Definition("structure", GridModuleCategory.Structure, Vector3Int.one, 50f, 0f, 0f, 0f);
        battery = Definition("battery", GridModuleCategory.Battery, Vector3Int.one, 120f, 50f, 0f, 0f);
        thruster = Definition("main_thruster", GridModuleCategory.MainThruster, new Vector3Int(1, 1, 2), 180f, 0f, 20f, 6000f);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(core);
        Object.DestroyImmediate(structure);
        Object.DestroyImmediate(battery);
        Object.DestroyImmediate(thruster);
    }

    [Test]
    public void MultiCellRotationKeepsTwoOccupiedCells()
    {
        for (int index = 0; index < GridOrientation.Count; index++)
        {
            var cells = GridOrientation.NormalizedCells(thruster.Footprint, index);
            Assert.AreEqual(2, cells.Count);
            Assert.AreEqual(2, new System.Collections.Generic.HashSet<Vector3Int>(cells).Count);
        }
    }

    [Test]
    public void PlacementRequiresFaceConnection()
    {
        var model = Model();
        Assert.IsTrue(model.TryPlace(
            "structure",
            new GridModulePose(new Vector3Int(1, 0, 0), 0),
            false,
            out _,
            out _));
        Assert.IsFalse(model.TryPlace(
            "structure",
            new GridModulePose(new Vector3Int(10, 0, 0), 0),
            false,
            out _,
            out _));
    }

    [Test]
    public void DeletingBridgeMarksOrphanDisconnected()
    {
        var model = Model();
        model.TryPlace("structure", new GridModulePose(new Vector3Int(1, 0, 0), 0), false, out string bridge, out _);
        model.TryPlace("structure", new GridModulePose(new Vector3Int(2, 0, 0), 0), false, out _, out _);
        Assert.IsTrue(model.TryRemove(bridge, out _));
        Assert.AreEqual(1, model.Validate().DisconnectedIds.Count);
    }

    [Test]
    public void BlueprintRoundTripPreservesMetrics()
    {
        var model = Model();
        model.TryPlace("battery", new GridModulePose(new Vector3Int(1, 0, 0), 0), false, out _, out _);
        ModularBlueprintData data = model.CaptureBlueprint();
        var restored = Model();
        Assert.IsTrue(restored.RestoreBlueprint(data, out _));
        Assert.AreEqual(model.CalculateMetrics().totalMass, restored.CalculateMetrics().totalMass);
        Assert.AreEqual(model.Records.Count, restored.Records.Count);
    }

    [Test]
    public void MovingModulePreservesBehaviorSettings()
    {
        var model = Model();
        Assert.IsTrue(model.TryPlace(
            "structure",
            new GridModulePose(new Vector3Int(1, 0, 0), 0),
            false,
            out string runtimeId,
            out _));
        Assert.IsTrue(model.TrySetBehaviorSettings(runtimeId, "wheel-tuning", out _));

        Assert.IsTrue(model.TryMove(
            runtimeId,
            new GridModulePose(new Vector3Int(0, 1, 0), 0),
            out _));

        Assert.AreEqual("wheel-tuning", model.Find(runtimeId).BehaviorSettings);
    }

    [Test]
    public void SupportedWheelGeometryMatchesAuthoredTyreMeasurements()
    {
        foreach (ExpectedWheelGeometry expected in SupportedWheelGeometry)
        {
            Assert.That(
                WheelModuleGeometryCatalog.TryResolve(
                    expected.neoXId,
                    out WheelTyreGeometry[] actual),
                Is.True,
                expected.neoXId);
            Assert.That(
                actual.Length,
                Is.EqualTo(expected.tyres.Length),
                expected.neoXId);

            float tolerance = expected.neoXId == "wheel_m_222"
                ? MediumGeometryTolerance
                : AuthoredGeometryTolerance;
            for (int index = 0; index < expected.tyres.Length; index++)
            {
                AssertTyreGeometry(
                    actual[index],
                    expected.tyres[index],
                    tolerance,
                    expected.neoXId + "[" + index + "]");
            }
        }
    }

    [Test]
    public void RacingWheelGeometryIsMirroredAndRearUsesTwoTyres()
    {
        Assert.That(
            WheelModuleGeometryCatalog.TryResolve(
                "speedwheel_small_l_322",
                out WheelTyreGeometry[] frontLeft),
            Is.True);
        Assert.That(
            WheelModuleGeometryCatalog.TryResolve(
                "speedwheel_small_r_322",
                out WheelTyreGeometry[] frontRight),
            Is.True);
        AssertMirroredTyres(frontLeft, frontRight, "racing front");

        Assert.That(
            WheelModuleGeometryCatalog.TryResolve(
                "speedwheel_large_l_522",
                out WheelTyreGeometry[] rearLeft),
            Is.True);
        Assert.That(
            WheelModuleGeometryCatalog.TryResolve(
                "speedwheel_large_r_522",
                out WheelTyreGeometry[] rearRight),
            Is.True);
        Assert.That(rearLeft.Length, Is.EqualTo(2));
        Assert.That(rearRight.Length, Is.EqualTo(2));
        AssertMirroredTyres(rearLeft, rearRight, "racing rear");
        Assert.That(
            Mathf.Abs(
                rearLeft[1].centerLocal.z -
                rearLeft[0].centerLocal.z),
            Is.GreaterThan(1.3f),
            "The authored rear module is a tandem two-tyre assembly.");
    }

    [Test]
    public void FourByTwoByTwoLargeWheelIsNotInAuthoredGeometryCatalog()
    {
        Assert.That(
            WheelModuleGeometryCatalog.TryResolve(
                "wheel_l_422",
                out WheelTyreGeometry[] tyres),
            Is.False);
        Assert.That(tyres, Is.Empty);
    }

    [Test]
    public void ConfigureTyreElementPreservesGeometryAndSharesModuleBudget()
    {
        foreach (ExpectedWheelGeometry expected in SupportedWheelGeometry)
        {
            GameObject module = new GameObject(
                "TyreElementTest_" + expected.neoXId);
            try
            {
                ModularContentRecord record = WheelRecord(expected.neoXId);
                WheelModuleProfile moduleProfile =
                    WheelModuleProfile.ForNeoXId(expected.neoXId);
                float configuredMass = 0f;
                float configuredLoad = 0f;
                float actuationShare = 0f;

                for (int index = 0;
                     index < expected.tyres.Length;
                     index++)
                {
                    GameObject element = new GameObject(
                        "TyreElement_" + index);
                    element.transform.SetParent(module.transform, false);
                    ModularWheelRuntime wheel =
                        element.AddComponent<ModularWheelRuntime>();

                    wheel.ConfigureTyreElement(
                        record,
                        WheelRoleSettings.Serialize(
                            WheelRoleOverride.Auto),
                        index,
                        module.transform);
                    // Reconfiguration happens during modular rebuilds and must
                    // not fall back to the legacy root-centred envelope.
                    wheel.ConfigureTyreElement(
                        record,
                        WheelRoleSettings.Serialize(
                            WheelRoleOverride.Auto),
                        index,
                        module.transform);

                    ExpectedTyreGeometry authored =
                        expected.tyres[index];
                    Assert.That(
                        wheel.HasAuthoredTyreGeometry,
                        Is.True,
                        expected.neoXId);
                    Assert.That(
                        wheel.TyreElementIndex,
                        Is.EqualTo(index),
                        expected.neoXId);
                    Assert.That(
                        wheel.TyreElementCount,
                        Is.EqualTo(expected.tyres.Length),
                        expected.neoXId);
                    Assert.That(
                        wheel.ModuleRoot,
                        Is.SameAs(module.transform),
                        expected.neoXId);
                    AssertVector3(
                        wheel.TyreCenterLocal,
                        authored.centerLocal,
                        MediumGeometryTolerance,
                        expected.neoXId + " center");
                    Assert.That(
                        wheel.Profile.radius,
                        Is.EqualTo(authored.radius)
                            .Within(MediumGeometryTolerance),
                        expected.neoXId + " radius");
                    Assert.That(
                        wheel.Profile.width,
                        Is.EqualTo(authored.width)
                            .Within(MediumGeometryTolerance),
                        expected.neoXId + " width");
                    Assert.That(
                        wheel.Profile.massKg,
                        Is.EqualTo(
                                moduleProfile.massKg /
                                expected.tyres.Length)
                            .Within(0.0001f),
                        expected.neoXId + " mass");
                    Assert.That(
                        wheel.Profile.loadCapacityKg,
                        Is.EqualTo(
                                moduleProfile.loadCapacityKg /
                                expected.tyres.Length)
                            .Within(0.0001f),
                        expected.neoXId + " load capacity");
                    Assert.That(
                        wheel.ModuleActuationShare,
                        Is.EqualTo(
                                1f /
                                expected.tyres.Length)
                            .Within(0.0001f),
                        expected.neoXId + " actuation");

                    configuredMass += wheel.Profile.massKg;
                    configuredLoad += wheel.Profile.loadCapacityKg;
                    actuationShare += wheel.ModuleActuationShare;
                }

                Assert.That(
                    configuredMass,
                    Is.EqualTo(moduleProfile.massKg).Within(0.0001f),
                    expected.neoXId);
                Assert.That(
                    configuredLoad,
                    Is.EqualTo(moduleProfile.loadCapacityKg)
                        .Within(0.0001f),
                    expected.neoXId);
                Assert.That(
                    actuationShare,
                    Is.EqualTo(1f).Within(0.0001f),
                    expected.neoXId);
            }
            finally
            {
                Object.DestroyImmediate(module);
            }
        }
    }

    [Test]
    public void SupportedWheelCarrierCollisionRemainsEnabledInFlightMode()
    {
        foreach (ExpectedWheelGeometry expected in SupportedWheelGeometry)
        {
            GameObject module = new GameObject(
                "CarrierCollisionTest_" + expected.neoXId);
            try
            {
                WheelCarrierCollisionRuntime carrier =
                    module.AddComponent<
                        WheelCarrierCollisionRuntime>();
                carrier.Configure(expected.neoXId);

                Assert.That(
                    carrier.ConfiguredNeoXId,
                    Is.EqualTo(expected.neoXId));
                Assert.That(
                    carrier.GeneratedColliders.Count,
                    Is.GreaterThan(0),
                    expected.neoXId);
                foreach (BoxCollider collider in
                         carrier.GeneratedColliders)
                {
                    Assert.That(
                        collider,
                        Is.Not.Null,
                        expected.neoXId);
                    Assert.That(
                        collider.transform.IsChildOf(
                            module.transform),
                        Is.True,
                        expected.neoXId);
                    Assert.That(
                        collider.isTrigger,
                        Is.False,
                        expected.neoXId);
                    Assert.That(
                        collider.enabled,
                        Is.True,
                        expected.neoXId);
                }

                GameObject element = new GameObject("TyreElement");
                element.transform.SetParent(module.transform, false);
                ModularWheelRuntime wheel =
                    element.AddComponent<ModularWheelRuntime>();
                wheel.ConfigureTyreElement(
                    WheelRecord(expected.neoXId),
                    WheelRoleSettings.Serialize(
                        WheelRoleOverride.Auto),
                    0,
                    module.transform);
                wheel.SetFlightMode(true);

                foreach (BoxCollider collider in
                         carrier.GeneratedColliders)
                {
                    Assert.That(
                        collider.enabled,
                        Is.True,
                        expected.neoXId +
                        " carrier must remain solid in flight");
                }
            }
            finally
            {
                Object.DestroyImmediate(module);
            }
        }
    }

    [Test]
    public void SameModuleRearTyresShareOneActuationBudget()
    {
        const string rearId = "speedwheel_large_l_522";
        Assert.That(
            WheelModuleGeometryCatalog.TryResolve(
                rearId,
                out WheelTyreGeometry[] geometry),
            Is.True);
        Assert.That(geometry.Length, Is.EqualTo(2));

        GameObject module = new GameObject(
            "RearActuationShareTest");
        try
        {
            Rigidbody body = module.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = 1000f;
            ModularContentRecord record = WheelRecord(rearId);
            var tyres =
                new ModularWheelRuntime[geometry.Length];
            for (int index = 0; index < tyres.Length; index++)
            {
                GameObject element = new GameObject(
                    "RearTyre_" + index);
                element.transform.SetParent(module.transform, false);
                ModularWheelRuntime wheel =
                    element.AddComponent<ModularWheelRuntime>();
                wheel.ConfigureTyreElement(
                    record,
                    WheelRoleSettings.Serialize(
                        WheelRoleOverride.Auto),
                    index,
                    module.transform);
                tyres[index] = wheel;
            }

            var mobility = new VehicleGroundMobility();
            mobility.Rebuild(body, module.transform, tyres);
            foreach (ModularWheelRuntime tyre in tyres)
                tyre.SetModuleActuationShare(0f);

            var ledger = new VehicleForceLedger();
            ledger.Begin(body);
            mobility.ApplyControl(
                Vector3.up,
                9.81f,
                Vector2.zero,
                false,
                false,
                ledger,
                0.02f);

            float totalShare = 0f;
            foreach (ModularWheelRuntime tyre in tyres)
            {
                Assert.That(
                    tyre.ModuleRoot,
                    Is.SameAs(module.transform));
                Assert.That(
                    tyre.ModuleActuationShare,
                    Is.EqualTo(0.5f).Within(0.0001f));
                totalShare += tyre.ModuleActuationShare;
            }
            Assert.That(
                totalShare,
                Is.EqualTo(1f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(module);
        }
    }

    [Test]
    public void RearTandemVisualUsesBoundedBogieWithoutTyreClipping()
    {
        const string rearId = "speedwheel_large_l_522";
        Assert.That(
            WheelModuleGeometryCatalog.TryResolve(
                rearId,
                out WheelTyreGeometry[] geometry),
            Is.True);
        Assert.That(geometry.Length, Is.EqualTo(2));

        GameObject module = new GameObject("RearVisualBogieTest");
        try
        {
            Rigidbody body = module.AddComponent<Rigidbody>();
            body.useGravity = false;
            ModularContentRecord record = WheelRecord(rearId);
            ModularWheelRuntime[] tyres =
                new ModularWheelRuntime[geometry.Length];
            for (int index = 0; index < tyres.Length; index++)
            {
                GameObject target = index == 0
                    ? module
                    : new GameObject("RearTyre_" + index);
                if (index > 0)
                    target.transform.SetParent(module.transform, false);
                tyres[index] =
                    target.AddComponent<ModularWheelRuntime>();
                tyres[index].ConfigureTyreElement(
                    record,
                    WheelRoleSettings.Serialize(
                        WheelRoleOverride.Auto),
                    index,
                    module.transform);
                tyres[index].BindVehicle(
                    body,
                    module.transform);
            }

            GameObject visual = new GameObject("RearVisual");
            visual.transform.SetParent(module.transform, false);
            tyres[0].BindVisual(visual.transform);
            float[] solvedDroops = { 0.12f, 0.32f };
            BindingFlags fields =
                BindingFlags.Instance |
                BindingFlags.NonPublic;
            FieldInfo groundedField =
                typeof(ModularWheelRuntime).GetField(
                    "grounded",
                    fields);
            FieldInfo contactNormalField =
                typeof(ModularWheelRuntime).GetField(
                    "contactNormal",
                    fields);
            FieldInfo probeAnchorField =
                typeof(ModularWheelRuntime).GetField(
                    "probeAnchorWorld",
                    fields);
            FieldInfo droopField =
                typeof(ModularWheelRuntime).GetField(
                    "suspensionDroop",
                    fields);
            Assert.That(groundedField, Is.Not.Null);
            Assert.That(contactNormalField, Is.Not.Null);
            Assert.That(probeAnchorField, Is.Not.Null);
            Assert.That(droopField, Is.Not.Null);
            for (int index = 0; index < tyres.Length; index++)
            {
                groundedField.SetValue(tyres[index], true);
                contactNormalField.SetValue(
                    tyres[index],
                    Vector3.up);
                probeAnchorField.SetValue(
                    tyres[index],
                    tyres[index].NeutralWheelCenterWorld);
                droopField.SetValue(
                    tyres[index],
                    solvedDroops[index]);
            }

            MethodInfo lateUpdate =
                typeof(ModularWheelRuntime).GetMethod(
                    "LateUpdate",
                    fields);
            Assert.That(lateUpdate, Is.Not.Null);
            lateUpdate.Invoke(tyres[0], null);

            for (int index = 0; index < tyres.Length; index++)
            {
                Vector3 visibleCenter =
                    tyres[0].VisualMotionRoot.TransformPoint(
                        geometry[index].centerLocal);
                Vector3 solvedCenter =
                    module.transform.TransformPoint(
                        geometry[index].centerLocal) -
                    Vector3.up * solvedDroops[index];
                Assert.That(
                    visibleCenter.y,
                    Is.EqualTo(solvedCenter.y).Within(0.002f),
                    "rear tyre " + index);
            }
            float bogieAngle = Quaternion.Angle(
                Quaternion.identity,
                tyres[0].VisualMotionRoot.parent.localRotation);
            Assert.That(bogieAngle, Is.LessThanOrEqualTo(12.01f));
        }
        finally
        {
            Object.DestroyImmediate(module);
        }
    }

    [Test]
    public void RearRollingVisualsRemapWhenConfiguredAfterBinding()
    {
        const string rearId = "speedwheel_large_l_522";
        GameObject module =
            new GameObject("RearVisualBindOrderTest");
        try
        {
            ModularWheelRuntime primary =
                module.AddComponent<ModularWheelRuntime>();
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(module.transform, false);
            Transform wheel = new GameObject("wheel").transform;
            wheel.SetParent(visual.transform, false);
            wheel.localPosition =
                SupportedWheelVisuals[4].rollingPivots[0];
            Transform wheel2 =
                new GameObject("wheel2").transform;
            wheel2.SetParent(visual.transform, false);
            wheel2.localPosition =
                SupportedWheelVisuals[4].rollingPivots[1];

            primary.BindVisual(visual.transform);

            GameObject secondaryObject =
                new GameObject("WheelTyreElement_1");
            secondaryObject.transform.SetParent(
                module.transform,
                false);
            ModularWheelRuntime secondary =
                secondaryObject.AddComponent<ModularWheelRuntime>();
            ModularContentRecord record = WheelRecord(rearId);
            primary.ConfigureTyreElement(
                record,
                WheelRoleSettings.Serialize(
                    WheelRoleOverride.Auto),
                0,
                module.transform);
            secondary.ConfigureTyreElement(
                record,
                WheelRoleSettings.Serialize(
                    WheelRoleOverride.Auto),
                1,
                module.transform);

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
            wheelAngularSpeed.SetValue(primary, 2f);
            wheelAngularSpeed.SetValue(secondary, 5f);

            const float step = 0.25f;
            advanceRollingVisuals.Invoke(
                primary,
                new object[] { step });
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    wheel.localRotation),
                Is.EqualTo(5f * Mathf.Rad2Deg * step)
                    .Within(0.05f),
                "front visual belongs to tyre element 1");
            Assert.That(
                Quaternion.Angle(
                    Quaternion.identity,
                    wheel2.localRotation),
                Is.EqualTo(2f * Mathf.Rad2Deg * step)
                    .Within(0.05f),
                "rear visual belongs to tyre element 0");
        }
        finally
        {
            Object.DestroyImmediate(module);
        }
    }

    [Test]
    public void ProductionWheelBundleMatchesCalibratedGeometryAndCarrier()
    {
        string catalogPath = Path.Combine(
            Application.streamingAssetsPath,
            "ModularContent",
            "modular_content_catalog.json");
        Assert.That(File.Exists(catalogPath), Is.True);
        ModularContentCatalog catalog =
            ModularContentCatalog.FromJson(
                File.ReadAllText(catalogPath));
        MethodInfo normalizeVisual =
            typeof(ModularContentService).GetMethod(
                "NormalizeVisual",
                BindingFlags.Static |
                BindingFlags.NonPublic);
        Assert.That(normalizeVisual, Is.Not.Null);
        MethodInfo advanceRollingVisuals =
            typeof(ModularWheelRuntime).GetMethod(
                "AdvanceRollingVisuals",
                BindingFlags.Instance |
                BindingFlags.NonPublic);
        FieldInfo wheelAngularSpeed =
            typeof(ModularWheelRuntime).GetField(
                "wheelAngularSpeed",
                BindingFlags.Instance |
                BindingFlags.NonPublic);
        Assert.That(advanceRollingVisuals, Is.Not.Null);
        Assert.That(wheelAngularSpeed, Is.Not.Null);

        Vector3[] expectedBounds =
        {
            new Vector3(0.8478193f, 1f, 0.7838529f),
            new Vector3(1.4840830f, 2f, 1.5578000f),
            new Vector3(1.4384430f, 1.3703150f, 3f),
            new Vector3(1.4384430f, 1.3703150f, 3f),
            new Vector3(1.5973090f, 2f, 4.8472420f),
            new Vector3(1.5973090f, 2f, 4.8472420f)
        };
        ModularContentRecord bundleRecord = catalog.Items
            .First(item =>
                item != null &&
                item.IsBase &&
                string.Equals(
                    item.neoXId,
                    SupportedWheelGeometry[0].neoXId,
                    StringComparison.OrdinalIgnoreCase));
        string bundleAddress =
            bundleRecord.bundleAddress.Replace('\\', '/');
        foreach (AssetBundle staleBundle in
                 AssetBundle.GetAllLoadedAssetBundles()
                     .Where(candidate =>
                         candidate != null &&
                         string.Equals(
                             candidate.name.Replace('\\', '/'),
                             bundleAddress,
                             StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            staleBundle.Unload(true);
        }
        string bundlePath = Path.Combine(
            Application.streamingAssetsPath,
            "ModularContent",
            "Windows",
            bundleAddress.Replace(
                '/',
                Path.DirectorySeparatorChar));
        Assert.That(File.Exists(bundlePath), Is.True);
        AssetBundle bundle =
            AssetBundle.LoadFromFile(bundlePath);
        Assert.That(bundle, Is.Not.Null);
        try
        {
            for (int idIndex = 0;
                 idIndex < SupportedWheelGeometry.Length;
                 idIndex++)
            {
                ExpectedWheelGeometry expected =
                    SupportedWheelGeometry[idIndex];
                ExpectedWheelVisual expectedVisual =
                    SupportedWheelVisuals.Single(item =>
                        string.Equals(
                            item.neoXId,
                            expected.neoXId,
                            StringComparison.OrdinalIgnoreCase));
                ModularContentRecord record = catalog.Items
                    .FirstOrDefault(item =>
                        item != null &&
                        item.IsBase &&
                        string.Equals(
                            item.neoXId,
                            expected.neoXId,
                            StringComparison.OrdinalIgnoreCase));
                Assert.That(
                    record,
                    Is.Not.Null,
                    expected.neoXId +
                    " production catalog record");
                Assert.That(
                    record.bundleAddress.Replace('\\', '/'),
                    Is.EqualTo(bundleAddress)
                        .IgnoreCase,
                    expected.neoXId +
                    " production bundle address");

                GameObject prefab =
                    bundle.LoadAsset<GameObject>(
                        record.assetAddress);
                Assert.That(
                    prefab,
                    Is.Not.Null,
                    expected.neoXId + " production asset");
                GameObject module =
                    new GameObject(
                        "ProductionWheel_" +
                        expected.neoXId);
                try
                {
                GameObject visual =
                    Object.Instantiate(prefab, module.transform);
                normalizeVisual.Invoke(
                    null,
                    new object[] { visual, record });
                Renderer[] renderers =
                    visual.GetComponentsInChildren<Renderer>(true);
                Assert.That(
                    renderers.Length,
                    Is.EqualTo(
                        1 + expectedVisual.rollingFaces.Length),
                    expected.neoXId + " renderer count");
                Transform visualCarrier = visual
                    .GetComponentsInChildren<Transform>(true)
                    .Single(item => item.name == "Carrier");
                Transform[] rollingVisuals = visual
                    .GetComponentsInChildren<Transform>(true)
                    .Where(item =>
                        item.name == "wheel" ||
                        item.name == "wheel2")
                    .OrderBy(item =>
                        item.name == "wheel2" ? 1 : 0)
                    .ToArray();
                Assert.That(
                    rollingVisuals.Length,
                    Is.EqualTo(expectedVisual.rollingFaces.Length),
                    expected.neoXId + " semantic rolling parts");
                Assert.That(
                    TriangleCount(visualCarrier),
                    Is.EqualTo(expectedVisual.carrierFaces),
                    expected.neoXId + " fixed carrier faces");
                int semanticFaceTotal =
                    TriangleCount(visualCarrier);
                for (int visualIndex = 0;
                     visualIndex < rollingVisuals.Length;
                     visualIndex++)
                {
                    Assert.That(
                        TriangleCount(rollingVisuals[visualIndex]),
                        Is.EqualTo(
                            expectedVisual.rollingFaces[visualIndex]),
                        expected.neoXId +
                        " rolling faces " + visualIndex);
                    semanticFaceTotal +=
                        TriangleCount(rollingVisuals[visualIndex]);
                    AssertVector3(
                        module.transform.InverseTransformPoint(
                            rollingVisuals[visualIndex].position),
                        expectedVisual.rollingPivots[visualIndex],
                        0.006f,
                        expected.neoXId +
                        " rolling pivot " + visualIndex);
                    Assert.That(
                        rollingVisuals[visualIndex]
                            .GetComponentsInChildren<Collider>(true),
                        Is.Empty,
                        expected.neoXId +
                        " rolling visuals must not own colliders");
                }
                Assert.That(
                    semanticFaceTotal,
                    Is.EqualTo(
                        expectedVisual.carrierFaces +
                        expectedVisual.rollingFaces.Sum()),
                    expected.neoXId +
                    " semantic faces must be complete and disjoint");
                Bounds visualBounds = renderers[0].bounds;
                for (int rendererIndex = 1;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    visualBounds.Encapsulate(
                        renderers[rendererIndex].bounds);
                }
                AssertVector3(
                    visualBounds.center,
                    module.transform.position,
                    0.001f,
                    expected.neoXId + " normalized center");
                AssertVector3(
                    visualBounds.size,
                    expectedBounds[idIndex],
                    0.001f,
                    expected.neoXId + " normalized bounds");

                var wheels =
                    new ModularWheelRuntime[expected.tyres.Length];
                wheels[0] =
                    module.AddComponent<ModularWheelRuntime>();
                for (int tyreIndex = 1;
                     tyreIndex < wheels.Length;
                     tyreIndex++)
                {
                    GameObject tyreObject = new GameObject(
                        "WheelTyreElement_" + tyreIndex);
                    tyreObject.transform.SetParent(
                        module.transform,
                        false);
                    wheels[tyreIndex] =
                        tyreObject.AddComponent<
                            ModularWheelRuntime>();
                }
                for (int tyreIndex = 0;
                     tyreIndex < wheels.Length;
                     tyreIndex++)
                {
                    wheels[tyreIndex].ConfigureTyreElement(
                        record,
                        WheelRoleSettings.Serialize(
                            WheelRoleOverride.Auto),
                        tyreIndex,
                        module.transform);
                }
                ModularWheelRuntime wheel = wheels[0];
                wheel.BindVisual(visual.transform);
                Assert.That(
                    wheel.RollingVisualCount,
                    Is.EqualTo(expectedVisual.rollingFaces.Length),
                    expected.neoXId +
                    " runtime rolling target count");
                Assert.That(
                    wheel.Profile.radius,
                    Is.EqualTo(expected.tyres[0].radius)
                        .Within(AuthoredGeometryTolerance),
                    expected.neoXId + " runtime radius");
                Assert.That(
                    wheel.Profile.width,
                    Is.EqualTo(expected.tyres[0].width)
                        .Within(AuthoredGeometryTolerance),
                    expected.neoXId + " runtime width");

                Quaternion fixedCarrierRotation =
                    visualCarrier.localRotation;
                Quaternion[] rollingRotations =
                    rollingVisuals
                        .Select(item => item.localRotation)
                        .ToArray();
                float[] elementSpeeds =
                    { 2f, 5f };
                for (int tyreIndex = 0;
                     tyreIndex < wheels.Length;
                     tyreIndex++)
                {
                    wheelAngularSpeed.SetValue(
                        wheels[tyreIndex],
                        elementSpeeds[tyreIndex]);
                }
                const float visualStep = 0.25f;
                advanceRollingVisuals.Invoke(
                    wheel,
                    new object[] { visualStep });
                Assert.That(
                    Quaternion.Angle(
                        fixedCarrierRotation,
                        visualCarrier.localRotation),
                    Is.LessThan(0.001f),
                    expected.neoXId +
                    " carrier must not roll with the tyre");
                for (int visualIndex = 0;
                     visualIndex < rollingVisuals.Length;
                     visualIndex++)
                {
                    int ownerIndex =
                        expectedVisual
                            .tyreElementIndices[visualIndex];
                    float expectedAngle =
                        elementSpeeds[ownerIndex] *
                        Mathf.Rad2Deg *
                        visualStep;
                    Assert.That(
                        Quaternion.Angle(
                            rollingRotations[visualIndex],
                            rollingVisuals[visualIndex]
                                .localRotation),
                        Is.EqualTo(expectedAngle).Within(0.05f),
                        expected.neoXId +
                        " independent rolling speed " +
                        visualIndex);
                }

                WheelCarrierCollisionRuntime carrierCollision =
                    module.AddComponent<
                        WheelCarrierCollisionRuntime>();
                carrierCollision.BindMotionRoot(
                    wheel.VisualMotionRoot);
                carrierCollision.Configure(expected.neoXId);
                Assert.That(
                    carrierCollision.GeneratedColliders.Count,
                    Is.GreaterThan(0),
                    expected.neoXId + " carrier count");
                float lowestTyre = expected.tyres
                    .Min(item =>
                        item.centerLocal.y - item.radius);
                Bounds carrierLimit = visualBounds;
                carrierLimit.Expand(0.02f);
                foreach (BoxCollider collider in
                         carrierCollision.GeneratedColliders)
                {
                    Assert.That(
                        carrierLimit.Contains(collider.bounds.min) &&
                        carrierLimit.Contains(collider.bounds.max),
                        Is.True,
                        expected.neoXId +
                        " carrier must stay inside visible mount");
                    Assert.That(
                        collider.bounds.min.y,
                        Is.GreaterThan(lowestTyre + 0.025f),
                        expected.neoXId +
                        " carrier must not become a phantom tyre");
                }
                }
                finally
                {
                    Object.DestroyImmediate(module);
                }
            }
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    GridAssemblyModel Model() => new GridAssemblyModel(new[] { core, structure, battery, thruster });

    static ModularContentRecord WheelRecord(string neoXId)
    {
        return new ModularContentRecord
        {
            neoXId = neoXId,
            wheelAxleLocal = new[] { 1f, 0f, 0f },
            wheelRollingForwardLocal = new[] { 0f, 0f, 1f }
        };
    }

    static void AssertMirroredTyres(
        WheelTyreGeometry[] left,
        WheelTyreGeometry[] right,
        string message)
    {
        Assert.That(right.Length, Is.EqualTo(left.Length), message);
        for (int index = 0; index < left.Length; index++)
        {
            Assert.That(
                right[index].centerLocal.x,
                Is.EqualTo(-left[index].centerLocal.x)
                    .Within(AuthoredGeometryTolerance),
                message + "[" + index + "] x");
            Assert.That(
                right[index].centerLocal.y,
                Is.EqualTo(left[index].centerLocal.y)
                    .Within(AuthoredGeometryTolerance),
                message + "[" + index + "] y");
            Assert.That(
                right[index].centerLocal.z,
                Is.EqualTo(left[index].centerLocal.z)
                    .Within(AuthoredGeometryTolerance),
                message + "[" + index + "] z");
            Assert.That(
                right[index].radius,
                Is.EqualTo(left[index].radius)
                    .Within(AuthoredGeometryTolerance),
                message + "[" + index + "] radius");
            Assert.That(
                right[index].width,
                Is.EqualTo(left[index].width)
                    .Within(AuthoredGeometryTolerance),
                message + "[" + index + "] width");
        }
    }

    static void AssertTyreGeometry(
        WheelTyreGeometry actual,
        ExpectedTyreGeometry expected,
        float tolerance,
        string message)
    {
        AssertVector3(
            actual.centerLocal,
            expected.centerLocal,
            tolerance,
            message + " center");
        Assert.That(
            actual.radius,
            Is.EqualTo(expected.radius).Within(tolerance),
            message + " radius");
        Assert.That(
            actual.width,
            Is.EqualTo(expected.width).Within(tolerance),
            message + " width");
    }

    static int TriangleCount(Transform root)
    {
        int result = 0;
        foreach (MeshFilter filter in
                 root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
                continue;
            for (int subMesh = 0;
                 subMesh < mesh.subMeshCount;
                 subMesh++)
            {
                result +=
                    (int)(mesh.GetIndexCount(subMesh) / 3);
            }
        }
        return result;
    }

    static void AssertVector3(
        Vector3 actual,
        Vector3 expected,
        float tolerance,
        string message)
    {
        Assert.That(
            actual.x,
            Is.EqualTo(expected.x).Within(tolerance),
            message + " x");
        Assert.That(
            actual.y,
            Is.EqualTo(expected.y).Within(tolerance),
            message + " y");
        Assert.That(
            actual.z,
            Is.EqualTo(expected.z).Within(tolerance),
            message + " z");
    }

    static GridModuleDefinition Definition(
        string id,
        GridModuleCategory category,
        Vector3Int footprint,
        float mass,
        float capacity,
        float cost,
        float thrust)
    {
        var definition = ScriptableObject.CreateInstance<GridModuleDefinition>();
        definition.Configure(id, id, category, null, footprint, mass, capacity, cost, 100f, thrust, null);
        return definition;
    }

    readonly struct ExpectedWheelGeometry
    {
        public readonly string neoXId;
        public readonly ExpectedTyreGeometry[] tyres;

        public ExpectedWheelGeometry(
            string id,
            params ExpectedTyreGeometry[] authoredTyres)
        {
            neoXId = id;
            tyres = authoredTyres;
        }
    }

    readonly struct ExpectedTyreGeometry
    {
        public readonly Vector3 centerLocal;
        public readonly float radius;
        public readonly float width;

        public ExpectedTyreGeometry(
            Vector3 center,
            float authoredRadius,
            float authoredWidth)
        {
            centerLocal = center;
            radius = authoredRadius;
            width = authoredWidth;
        }
    }

    readonly struct ExpectedWheelVisual
    {
        public readonly string neoXId;
        public readonly int carrierFaces;
        public readonly int[] rollingFaces;
        public readonly Vector3[] rollingPivots;
        public readonly int[] tyreElementIndices;

        public ExpectedWheelVisual(
            string id,
            int expectedCarrierFaces,
            int[] expectedRollingFaces,
            Vector3[] expectedRollingPivots,
            int[] expectedTyreElementIndices)
        {
            neoXId = id;
            carrierFaces = expectedCarrierFaces;
            rollingFaces = expectedRollingFaces;
            rollingPivots = expectedRollingPivots;
            tyreElementIndices = expectedTyreElementIndices;
        }
    }
}
