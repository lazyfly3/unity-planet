using System;
using System.Reflection;
using NUnit.Framework;
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

            Assert.That(distance, Is.EqualTo(8400d).Within(0.01d));
            Assert.That(distance, Is.GreaterThan(
                planet.celestial.radius + planet.celestial.atmosphereTopAltitude));
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

            Assert.That(navigation.IsNearPlanet(reached, 8200d), Is.True);
            Assert.That(navigation.IsNearPlanet(neighbour, 8200d), Is.False);
            Assert.That(navigation.IsNearPlanet(reached, 12000d), Is.False);

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
    public void TransitTemporarilyUsesKinematicMotionAndRestoresRigidbodyState()
    {
        var ship = new GameObject("WarpTestShip");
        Rigidbody body = ship.AddComponent<Rigidbody>();
        InterstellarCruiseController cruise =
            ship.AddComponent<InterstellarCruiseController>();
        try
        {
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.velocity = Vector3.forward * 850f;
            SetPrivate(cruise, "shipBody", body);

            InvokePrivate(cruise, "BeginTransitKinematicControl");

            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.velocity, Is.EqualTo(Vector3.zero));
            Assert.That(
                body.interpolation,
                Is.EqualTo(RigidbodyInterpolation.Interpolate));

            InvokePrivate(cruise, "EndTransitKinematicControl");

            Assert.That(body.isKinematic, Is.False);
            Assert.That(
                body.interpolation,
                Is.EqualTo(RigidbodyInterpolation.Interpolate));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
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

    static void InvokePrivate(object target, string name)
    {
        MethodInfo method = target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name);
        method.Invoke(target, null);
    }
}
