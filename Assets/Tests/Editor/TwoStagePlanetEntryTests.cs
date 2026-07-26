using System;
using System.Reflection;
using NUnit.Framework;
using SpacecraftEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class TwoStagePlanetEntryTests
{
    [Test]
    public void LargePlanetNearExitFramesWholePlanet()
    {
        var root = new GameObject("Navigation");
        try
        {
            InterstellarNavigationSystem navigation =
                root.AddComponent<InterstellarNavigationSystem>();
            var planet = new GalaxyPlanetDefinition
            {
                celestial = PlanetCelestialProfile.CreateLargeDefault()
            };

            double distance = navigation.GetNearExitDistance(planet);
            PlanetPhysicalProfile physical = planet.celestial.Physical;
            double expected = Math.Max(
                physical.radiusMeters * 4.2d,
                physical.radiusMeters
                    + Math.Max(
                        planet.celestial.maximumTerrainElevation,
                        physical.atmosphereTopAltitudeMeters)
                    + 2_500d);

            Assert.That(distance, Is.EqualTo(expected).Within(0.01d));
            Assert.That(distance, Is.GreaterThan(
                physical.radiusMeters + physical.atmosphereTopAltitudeMeters));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void IncomingDirectionProducesMatchingExitHemisphere()
    {
        var origin = new DoubleVector3(-64000d, 12000d, 9000d);
        var target = new DoubleVector3(32000d, -4000d, 73000d);
        Vector3 travelDirection =
            InterstellarCruiseController.CalculateTravelDirection(origin, target);
        DoubleVector3 destination =
            InterstellarCruiseController.CalculateWarpDestination(
                origin,
                target,
                8400d);

        Vector3 exitToPlanet = (target - destination).ToVector3().normalized;
        Vector3 landingDirection = (destination - target).ToVector3().normalized;

        Assert.That(Vector3.Dot(exitToPlanet, travelDirection), Is.GreaterThan(0.99999f));
        Assert.That(Vector3.Dot(landingDirection, -travelDirection), Is.GreaterThan(0.99999f));
    }

    [Test]
    public void WarpRelocationPreservesTargetRelativeVelocity()
    {
        Vector3 relativeVelocity = new Vector3(18f, -7f, 122f);
        Vector3 targetReference = new Vector3(-320f, 45f, 88f);

        Vector3 worldVelocity =
            InterstellarCruiseController.CalculateRelocatedWorldVelocity(
                relativeVelocity,
                targetReference);

        Assert.That(worldVelocity - targetReference, Is.EqualTo(relativeVelocity));
    }

    [Test]
    public void PlanetCenteredFramePreservesUniversePositionAndUsesRelativeVelocity()
    {
        var ship = new GameObject("PlanetFrameShip");
        Rigidbody body = ship.AddComponent<Rigidbody>();
        InterstellarFlightRuntime runtime =
            ship.AddComponent<InterstellarFlightRuntime>();
        try
        {
            var origin = new UniversePosition(
                new InterstellarCoordinate(3, -2, 5),
                new DoubleVector3(12000d, -4000d, 9000d));
            Vector3 localPosition = new Vector3(1400f, -250f, 620f);
            Vector3 planetVelocity = new Vector3(-320f, 45f, 88f);
            Vector3 relativeVelocity = new Vector3(18f, -7f, 122f);
            var planet = new GalaxyPlanetDefinition
            {
                planetId = "planet-frame-test",
                coordinate3D = new InterstellarCoordinate(3, -2, 5)
            };
            var planetAddress = new UniversePosition(
                planet.coordinate3D,
                new DoubleVector3(8000d, -5000d, 4000d));

            SetPrivate(runtime, "shipBody", body);
            SetPrivate(runtime, "initialized", true);
            SetPrivate(runtime, "initializeNearFrameAtRest", false);
            SetPrivate(runtime, "universeOrigin", origin);
            body.position = localPosition;
            body.velocity = planetVelocity + relativeVelocity;
            UniversePosition before = runtime.ShipPhysicalUniversePosition;

            runtime.EnterPlanetCenteredFrame(
                planet,
                planetAddress,
                planetVelocity);

            Assert.That(runtime.IsPlanetCenteredFrame, Is.True);
            Assert.That(
                UniversePosition.Distance(
                    before,
                    runtime.ShipPhysicalUniversePosition),
                Is.LessThan(0.001d));
            Assert.That(body.velocity, Is.EqualTo(relativeVelocity));
            Assert.That(
                runtime.ToBarycentricVelocity(body.velocity),
                Is.EqualTo(planetVelocity + relativeVelocity));
            Assert.That(runtime.ActiveRebaseThresholdMeters, Is.EqualTo(2000f));

            Vector3 relativeBeforeRebase =
                runtime.ShipPlanetRelativePositionKilometers;
            InvokePrivate(runtime, "ShiftOrigin", body.position);
            Assert.That(body.position, Is.EqualTo(Vector3.zero));
            Assert.That(
                UniversePosition.Distance(
                    before,
                    runtime.ShipPhysicalUniversePosition),
                Is.LessThan(0.001d));
            Assert.That(
                runtime.ShipPlanetRelativePositionKilometers,
                Is.EqualTo(relativeBeforeRebase));

            runtime.ExitPlanetCenteredFrame();

            Assert.That(runtime.IsPlanetCenteredFrame, Is.False);
            Assert.That(body.velocity, Is.EqualTo(
                planetVelocity + relativeVelocity));
            Assert.That(
                UniversePosition.Distance(
                    before,
                    runtime.ShipPhysicalUniversePosition),
                Is.LessThan(0.001d));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    [Test]
    public void PlanetCenteredProxySurvivesTwoCameraDepthComposition()
    {
        int kilometerLayer = LayerMask.NameToLayer("SpaceKilometerView");
        Assert.That(kilometerLayer, Is.GreaterThanOrEqualTo(0));

        var root = new GameObject("PlanetRenderTestRoot");
        var astronomicalObject = new GameObject("AstronomicalRenderTestCamera");
        var localObject = new GameObject("LocalRenderTestCamera");
        var lightObject = new GameObject("PlanetRenderTestLight");
        var target = new RenderTexture(96, 96, 24);
        Texture2D pixels = null;
        Material terrainMaterial = null;
        Material atmosphereMaterial = null;
        try
        {
            root.layer = kilometerLayer;
            var planet = new GalaxyPlanetDefinition
            {
                planetId = "planet-render-test",
                seed = 137,
                surfaceColor = new Color(0.3f, 0.58f, 0.22f, 1f),
                rockColor = new Color(0.18f, 0.24f, 0.12f, 1f),
                mapColor = new Color(0.3f, 0.58f, 0.22f, 1f),
                celestial = PlanetCelestialProfile.CreateLargeDefault(),
                terrain = new PlanetTerrainSettings()
            };
            planet.celestial.physical.atmosphereSurfaceDensityKgPerCubicMeter = 0d;
            planet.celestial.physical.atmosphereTopAltitudeMeters = 0d;
            planet.celestial.ClampValues();
            terrainMaterial = new Material(
                Shader.Find("VoxelPlanet/SurfaceFarLod"));
            atmosphereMaterial = new Material(
                Shader.Find("VoxelPlanet/AtmosphereShell"));
            SpacePlanetProxy proxy = SpacePlanetProxy.Create(
                root.transform,
                planet,
                1f,
                terrainMaterial,
                atmosphereMaterial);
            proxy.SetKilometerPresentation(
                Vector3.zero,
                (float)SpaceKilometerScale.ToKilometerUnits(
                    planet.celestial.Physical.radiusMeters),
                1f);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

            Camera astronomical = astronomicalObject.AddComponent<Camera>();
            astronomical.transform.position = new Vector3(0f, 0f, -26758f);
            astronomical.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            astronomical.clearFlags = CameraClearFlags.SolidColor;
            astronomical.backgroundColor = Color.black;
            astronomical.cullingMask = 1 << kilometerLayer;
            astronomical.nearClipPlane = 0.05f;
            astronomical.farClipPlane = 120000f;
            astronomical.targetTexture = target;

            Camera local = localObject.AddComponent<Camera>();
            local.transform.SetPositionAndRotation(
                astronomical.transform.position,
                astronomical.transform.rotation);
            local.clearFlags = CameraClearFlags.Depth;
            local.cullingMask = ~(1 << kilometerLayer);
            local.nearClipPlane = 0.01f;
            local.farClipPlane = 5000f;
            local.targetTexture = target;

            astronomical.Render();
            local.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels = new Texture2D(96, 96, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0f, 0f, 96f, 96f), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;

            Color center = pixels.GetPixel(48, 48);
            Assert.That(
                Mathf.Max(center.r, center.g, center.b),
                Is.GreaterThan(0.03f),
                "The local physics camera cleared or hid the astronomical planet.");
        }
        finally
        {
            if (pixels != null)
                UnityEngine.Object.DestroyImmediate(pixels);
            Camera local = localObject.GetComponent<Camera>();
            if (local != null)
                local.targetTexture = null;
            Camera astronomical = astronomicalObject.GetComponent<Camera>();
            if (astronomical != null)
                astronomical.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(localObject);
            UnityEngine.Object.DestroyImmediate(astronomicalObject);
            UnityEngine.Object.DestroyImmediate(root);
            if (terrainMaterial != null)
                UnityEngine.Object.DestroyImmediate(terrainMaterial);
            if (atmosphereMaterial != null)
                UnityEngine.Object.DestroyImmediate(atmosphereMaterial);
        }
    }

    [Test]
    public void WarpCinematicReturnsDirectlyToTacticalAtLowRelativeSpeed()
    {
        var ship = new GameObject("WarpInteractionShip");
        Rigidbody body = ship.AddComponent<Rigidbody>();
        InterstellarFlightRuntime runtime =
            ship.AddComponent<InterstellarFlightRuntime>();
        try
        {
            SetPrivate(runtime, "shipBody", body);
            SetPrivate(runtime, "initialized", true);
            SetPrivate(runtime, "interactionEvaluationStartsAt", 0f);
            SetPrivate(
                runtime,
                "<InteractionMode>k__BackingField",
                SpaceflightInteractionMode.WarpCinematic);
            body.velocity = new Vector3(0f, 0f, 200f);

            InvokePrivate(runtime, "UpdateInteractionMode", true);

            Assert.That(
                runtime.InteractionMode,
                Is.EqualTo(SpaceflightInteractionMode.TacticalPhysics));
            Assert.That(runtime.ActiveRebaseThresholdMeters, Is.EqualTo(2000f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    [Test]
    public void CinematicRestoreUsesTargetPlanetCenteredVelocity()
    {
        var ship = new GameObject("PlanetFrameWarpShip");
        Rigidbody body = ship.AddComponent<Rigidbody>();
        SpacecraftIfcsMotor ifcs = ship.AddComponent<SpacecraftIfcsMotor>();
        InterstellarFlightRuntime runtime =
            ship.AddComponent<InterstellarFlightRuntime>();
        InterstellarCruiseController cruise =
            ship.AddComponent<InterstellarCruiseController>();
        try
        {
            var sourcePlanet = new GalaxyPlanetDefinition
            {
                planetId = "source",
                coordinate3D = new InterstellarCoordinate(1, 2, 3)
            };
            var targetPlanet = new GalaxyPlanetDefinition
            {
                planetId = "target",
                coordinate3D = new InterstellarCoordinate(4, 5, 6)
            };
            Vector3 sourceVelocity = new Vector3(310f, -20f, 75f);
            Vector3 targetVelocity = new Vector3(-140f, 55f, -230f);
            Vector3 pilotRelativeVelocity = new Vector3(12f, -3f, 96f);

            SetPrivate(runtime, "shipBody", body);
            SetPrivate(runtime, "ifcsMotor", ifcs);
            SetPrivate(runtime, "initialized", true);
            SetPrivate(runtime, "initializeNearFrameAtRest", false);
            SetPrivate(cruise, "shipBody", body);
            SetPrivate(cruise, "ifcsMotor", ifcs);
            SetPrivate(cruise, "flightRuntime", runtime);
            body.velocity = sourceVelocity + pilotRelativeVelocity;
            ifcs.ControlsEnabled = true;
            runtime.EnterPlanetCenteredFrame(
                sourcePlanet,
                new UniversePosition(
                    sourcePlanet.coordinate3D,
                    new DoubleVector3(1000d, 2000d, 3000d)),
                sourceVelocity);

            InvokePrivate(cruise, "CaptureAndPausePhysics");
            Assert.That(body.isKinematic, Is.True);

            runtime.ExitPlanetCenteredFrame();
            runtime.EnterPlanetCenteredFrame(
                targetPlanet,
                new UniversePosition(
                    targetPlanet.coordinate3D,
                    new DoubleVector3(-4000d, 5000d, 6000d)),
                targetVelocity);
            SetPrivate(
                cruise,
                "restoredAbsoluteVelocity",
                targetVelocity + pilotRelativeVelocity);

            InvokePrivate(cruise, "RestorePhysicsAfterCinematic");

            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.velocity, Is.EqualTo(pilotRelativeVelocity));
            Assert.That(
                runtime.ToBarycentricVelocity(body.velocity),
                Is.EqualTo(targetVelocity + pilotRelativeVelocity));
            Assert.That(ifcs.ControlsEnabled, Is.True);
            Assert.That(ifcs.VelocityReferenceWorld, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    [Test]
    public void NearStatusRequiresThePlanetReachedByWarp()
    {
        var root = new GameObject("Navigation");
        try
        {
            InterstellarNavigationSystem navigation =
                root.AddComponent<InterstellarNavigationSystem>();
            var reached = new GalaxyPlanetDefinition
            {
                planetId = "reached",
                celestial = PlanetCelestialProfile.CreateLargeDefault()
            };
            var neighbour = new GalaxyPlanetDefinition
            {
                planetId = "neighbour",
                celestial = PlanetCelestialProfile.CreateLargeDefault()
            };

            navigation.MarkNearPlanet(reached);
            double nearDistance = navigation.GetNearExitDistance(reached);

            Assert.That(navigation.IsNearPlanet(reached, nearDistance), Is.True);
            Assert.That(navigation.IsNearPlanet(neighbour, nearDistance), Is.False);
            Assert.That(
                navigation.IsNearPlanet(reached, nearDistance * 1.5d),
                Is.False);

            navigation.ClearNearPlanet();
            Assert.That(navigation.IsNearPlanet(reached, 8200d), Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void WarpRelocationRequiresCrossingTheEllipseAperture()
    {
        var gateSize = new Vector2(28.8f, 18f);

        Assert.That(
            InterstellarWarpGateController.IsInsideEntranceEllipse(
                Vector2.zero,
                gateSize,
                0.82f),
            Is.True);
        Assert.That(
            InterstellarWarpGateController.IsInsideEntranceEllipse(
                new Vector2(10f, 0f),
                gateSize,
                0.82f),
            Is.True);
        Assert.That(
            InterstellarWarpGateController.IsInsideEntranceEllipse(
                new Vector2(13f, 0f),
                gateSize,
                0.82f),
            Is.False);
        Assert.That(
            InterstellarWarpGateController.IsInsideEntranceEllipse(
                new Vector2(0f, 8f),
                gateSize,
                0.82f),
            Is.False);
    }

    [Test]
    public void FixedTransitTimelineCrossesPortalAtAConstantTime()
    {
        Vector3 start = new Vector3(8f, -3f, -45f);
        Vector3 center = new Vector3(8f, -3f, 0f);
        Vector3 forward = Vector3.forward;

        Vector3 beginning =
            InterstellarCruiseController.CalculateFixedTransitPosition(
                start,
                center,
                forward,
                0f);
        Vector3 crossing =
            InterstellarCruiseController.CalculateFixedTransitPosition(
                start,
                center,
                forward,
                1f);

        Assert.That(beginning, Is.EqualTo(start));
        Assert.That(crossing.x, Is.EqualTo(center.x).Within(0.0001f));
        Assert.That(crossing.y, Is.EqualTo(center.y).Within(0.0001f));
        Assert.That(crossing.z, Is.GreaterThan(center.z));
        Assert.That(
            0.9f * InterstellarCruiseController.PortalCrossingProgress,
            Is.EqualTo(0.648f).Within(0.0001f));
    }

    [Test]
    public void CinematicPauseRestoresRigidbodyAndIfcsStateWithoutChangingPose()
    {
        var ship = new GameObject("WarpTestShip");
        Rigidbody body = ship.AddComponent<Rigidbody>();
        SpacecraftIfcsMotor ifcs = ship.AddComponent<SpacecraftIfcsMotor>();
        InterstellarCruiseController cruise =
            ship.AddComponent<InterstellarCruiseController>();
        try
        {
            Vector3 position = new Vector3(18f, -3f, 42f);
            Quaternion rotation = Quaternion.Euler(14f, 37f, -9f);
            Vector3 referenceVelocity = new Vector3(30f, -4f, 12f);
            Vector3 relativeVelocity = new Vector3(7f, 2f, 95f);
            Vector3 angularVelocity = new Vector3(0.2f, -0.4f, 0.1f);
            body.position = position;
            body.rotation = rotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.velocity = referenceVelocity + relativeVelocity;
            body.angularVelocity = angularVelocity;
            ifcs.ControlsEnabled = true;
            ifcs.LinearControlEnabled = false;
            ifcs.AngularControlEnabled = true;
            ifcs.SetVelocityReference(referenceVelocity);
            SetPrivate(cruise, "shipBody", body);
            SetPrivate(cruise, "ifcsMotor", ifcs);

            InvokePrivate(cruise, "CaptureAndPausePhysics");

            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.position, Is.EqualTo(position));
            Assert.That(Quaternion.Angle(body.rotation, rotation), Is.LessThan(0.001f));
            Assert.That(ifcs.ControlsEnabled, Is.False);

            Vector3 newReferenceVelocity = new Vector3(-80f, 6f, 21f);
            SetPrivate(
                cruise,
                "restoredAbsoluteVelocity",
                newReferenceVelocity + relativeVelocity);
            ifcs.SetVelocityReference(newReferenceVelocity);
            InvokePrivate(cruise, "RestorePhysicsAfterCinematic");

            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.position, Is.EqualTo(position));
            Assert.That(Quaternion.Angle(body.rotation, rotation), Is.LessThan(0.001f));
            Assert.That(body.velocity, Is.EqualTo(
                newReferenceVelocity + relativeVelocity));
            Assert.That(body.angularVelocity, Is.EqualTo(angularVelocity));
            Assert.That(
                body.interpolation,
                Is.EqualTo(RigidbodyInterpolation.Interpolate));
            Assert.That(ifcs.ControlsEnabled, Is.True);
            Assert.That(ifcs.LinearControlEnabled, Is.False);
            Assert.That(ifcs.AngularControlEnabled, Is.True);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    [Test]
    public void WarpPresentationCopiesOnlyRenderingAndRestoresOriginalRenderer()
    {
        var ship = new GameObject("WarpPresentationShip");
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.transform.SetParent(ship.transform, false);
        Renderer originalRenderer = visual.GetComponent<Renderer>();
        InterstellarWarpShipPresentation presentation =
            ship.AddComponent<InterstellarWarpShipPresentation>();
        try
        {
            Assert.That(presentation.Begin(ship.transform), Is.True);
            Assert.That(originalRenderer.enabled, Is.False);
            Assert.That(presentation.PresentationTransform, Is.Not.Null);
            Assert.That(
                presentation.PresentationTransform.GetComponentsInChildren<Renderer>(
                    true).Length,
                Is.EqualTo(1));
            Assert.That(
                presentation.PresentationTransform.GetComponentsInChildren<Collider>(
                    true),
                Is.Empty);
            Assert.That(
                presentation.PresentationTransform.GetComponentsInChildren<Rigidbody>(
                    true),
                Is.Empty);
            Assert.That(
                presentation.PresentationTransform.GetComponentsInChildren<MonoBehaviour>(
                    true),
                Is.Empty);

            presentation.Restore();

            Assert.That(originalRenderer.enabled, Is.True);
            Assert.That(presentation.PresentationTransform, Is.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    [Test]
    public void AbortingCinematicRestoresDynamicBodyImmediately()
    {
        var ship = new GameObject("WarpAbortShip");
        Rigidbody body = ship.AddComponent<Rigidbody>();
        InterstellarCruiseController cruise =
            ship.AddComponent<InterstellarCruiseController>();
        try
        {
            Quaternion rotation = Quaternion.Euler(-8f, 72f, 11f);
            Vector3 velocity = new Vector3(14f, -2f, 63f);
            Vector3 angularVelocity = new Vector3(-0.1f, 0.25f, 0.07f);
            body.rotation = rotation;
            body.velocity = velocity;
            body.angularVelocity = angularVelocity;
            SetPrivate(cruise, "shipBody", body);

            InvokePrivate(cruise, "CaptureAndPausePhysics");
            Assert.That(body.isKinematic, Is.True);

            cruise.Abort();

            Assert.That(body.isKinematic, Is.False);
            Assert.That(Quaternion.Angle(body.rotation, rotation), Is.LessThan(0.001f));
            Assert.That(body.velocity, Is.EqualTo(velocity));
            Assert.That(body.angularVelocity, Is.EqualTo(angularVelocity));
            Assert.That(cruise.WarpState, Is.EqualTo(InterstellarWarpState.Unlocked));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    [Test]
    public void CameraPresentationTargetReturnsToPhysicalShip()
    {
        var physicalShip = new GameObject("PhysicalShip");
        var visualProxy = new GameObject("VisualProxy");
        var cameraRoot = new GameObject("CameraRig");
        try
        {
            InterstellarCameraRig rig =
                cameraRoot.AddComponent<InterstellarCameraRig>();
            SetPrivate(rig, "target", physicalShip.transform);

            rig.PushPresentationTarget(visualProxy.transform);
            Assert.That(rig.PresentationTarget, Is.EqualTo(visualProxy.transform));

            rig.PopPresentationTarget();
            Assert.That(rig.PresentationTarget, Is.EqualTo(physicalShip.transform));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cameraRoot);
            UnityEngine.Object.DestroyImmediate(visualProxy);
            UnityEngine.Object.DestroyImmediate(physicalShip);
        }
    }

    [Test]
    public void CinematicDefaultsMatchApprovedTimeline()
    {
        var root = new GameObject("Cruise");
        root.AddComponent<Rigidbody>();
        try
        {
            InterstellarCruiseController controller =
                root.AddComponent<InterstellarCruiseController>();
            Assert.That(ReadFloat(controller, "alignmentDuration"), Is.EqualTo(0.65f));
            Assert.That(ReadFloat(controller, "spoolDuration"), Is.EqualTo(1.15f));
            Assert.That(ReadFloat(controller, "transitDuration"), Is.EqualTo(0.9f));
            Assert.That(ReadFloat(controller, "exitDuration"), Is.EqualTo(0.65f));
            Assert.That(ReadFloat(controller, "exitSpeed"), Is.EqualTo(120f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void InterstellarSceneContainsWarpGateSystemOnlyOnce()
    {
        const string path = "Assets/Scenes/InterstellarFlight.unity";
        bool alreadyLoaded = SceneManager.GetSceneByPath(path).isLoaded;
        Scene scene = alreadyLoaded
            ? SceneManager.GetSceneByPath(path)
            : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            int count = 0;
            int fadeCount = 0;
            InterstellarCruiseController cruise = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<InterstellarWarpGateController>(
                    true).Length;
                fadeCount += root.GetComponentsInChildren<PersistentSpaceflightFade>(
                    true).Length;
                if (cruise == null)
                    cruise = root.GetComponentInChildren<InterstellarCruiseController>(true);
            }
            Assert.That(count, Is.EqualTo(1));
            Assert.That(fadeCount, Is.EqualTo(1));
            Assert.That(cruise, Is.Not.Null);
            Assert.That(ReadFloat(cruise, "alignmentDuration"), Is.EqualTo(0.65f));
            Assert.That(ReadFloat(cruise, "spoolDuration"), Is.EqualTo(1.15f));
            Assert.That(ReadFloat(cruise, "transitDuration"), Is.EqualTo(0.9f));
            Assert.That(ReadFloat(cruise, "exitDuration"), Is.EqualTo(0.65f));
            Assert.That(ReadFloat(cruise, "cooldownDuration"), Is.EqualTo(0.6f));
        }
        finally
        {
            if (!alreadyLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void ApprovedWarpGateShaderIsAvailable()
    {
        Assert.That(
            Shader.Find("VoxelPlanet/InterstellarWarpGate"),
            Is.Not.Null);
    }

    static float ReadFloat(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (float)field.GetValue(target);
    }

    static void SetPrivate(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }

    static void InvokePrivate(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name);
        method.Invoke(target, arguments);
    }
}
