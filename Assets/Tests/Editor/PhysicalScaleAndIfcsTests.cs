using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpacecraftEditor;
using UnityEditor;
using UnityEngine;

public sealed class PhysicalScaleAndIfcsTests
{
    [Test]
    public void EarthReferenceProfile_DerivesExpectedGravityAndOrbitalSpeeds()
    {
        PlanetPhysicalProfile earth = PlanetPhysicalProfile.CreateEarthLike();

        Assert.That(earth.radiusMeters, Is.EqualTo(6_371_000d).Within(1d));
        Assert.That(earth.surfaceGravity, Is.EqualTo(9.81d).Within(0.03d));
        Assert.That(earth.CircularOrbitSpeed(0d), Is.EqualTo(7_900d).Within(40d));
        Assert.That(earth.EscapeVelocity(0d), Is.EqualTo(11_200d).Within(50d));
        Assert.That(
            earth.EscapeVelocity(0d) / earth.CircularOrbitSpeed(0d),
            Is.EqualTo(Math.Sqrt(2d)).Within(0.000001d));
    }

    [Test]
    public void CelestialEphemeris_UsesDoublePrecisionMeters()
    {
        var orbit = new CelestialOrbitDefinition
        {
            semiMajorAxisMeters = PhysicalConstants.AstronomicalUnit,
            eccentricity = 0f,
            inclinationDegrees = 13.25f,
            longitudeAscendingNodeDegrees = 217.5f,
            argumentOfPeriapsisDegrees = 41.75f,
            meanAnomalyAtEpochDegrees = 0f
        };

        CelestialBodyState state = CelestialEphemeris.GetBodyState(
            orbit,
            PhysicalConstants.SolarMass,
            0d);
        double radius = Magnitude(state.positionMeters);
        double speed = Magnitude(state.velocityMetersPerSecond);

        Assert.That(radius, Is.EqualTo(PhysicalConstants.AstronomicalUnit).Within(0.01d));
        Assert.That(speed, Is.EqualTo(29_785d).Within(50d));
    }

    [Test]
    public void HierarchicalCoordinates_PreserveMeterOffsetsAtLightYearDistances()
    {
        var system = new InterstellarCoordinate(900_000_000L, -400_000_000L, 125_000_000L);
        var first = new UniversePosition(system, new DoubleVector3(12.25d, -8.5d, 42d));
        var second = new UniversePosition(system, new DoubleVector3(13.5d, -6.25d, 39d));

        DoubleVector3 delta = UniversePosition.Delta(first, second);

        Assert.That(delta.x, Is.EqualTo(1.25d).Within(0.0000001d));
        Assert.That(delta.y, Is.EqualTo(2.25d).Within(0.0000001d));
        Assert.That(delta.z, Is.EqualTo(-3d).Within(0.0000001d));
        Assert.That(UniversePosition.Distance(first, second), Is.EqualTo(Magnitude(delta)).Within(0.0000001d));
    }

    [TestCase(0L, 0L, 0L)]
    [TestCase(19L, -27L, 381L)]
    [TestCase(-900000000L, 400000000L, -125000000L)]
    public void InterstellarPlanetIds_RoundTripCoordinates(long x, long y, long z)
    {
        var expected = new InterstellarCoordinate(x, y, z);
        string planetId = ProceduralInterstellarGenerator.EncodePlanetId(expected);

        Assert.That(
            ProceduralInterstellarGenerator.TryDecodePlanetId(
                planetId,
                out InterstellarCoordinate actual),
            Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void GeneratedSystem_HasStableSeparatedKeplerOrbits()
    {
        var generator = new ProceduralInterstellarGenerator(
            24681357,
            Array.Empty<GalaxyResourceCatalogEntry>());
        var planets = new List<GalaxyPlanetDefinition>();
        for (long z = 0; z < ProceduralInterstellarGenerator.MacroCellSize; z++)
        for (long y = 0; y < ProceduralInterstellarGenerator.MacroCellSize; y++)
        for (long x = 0; x < ProceduralInterstellarGenerator.MacroCellSize; x++)
        {
            var coordinate = new InterstellarCoordinate(x, y, z);
            if (generator.HasPlanet(coordinate))
                planets.Add(generator.GeneratePlanet(coordinate));
        }

        GalaxyPlanetDefinition[] ordered = planets.OrderBy(planet => planet.orbitIndex).ToArray();
        Assert.That(ordered.Length, Is.InRange(2, 6));
        Assert.That(ordered.Select(planet => planet.orbitIndex), Is.EqualTo(Enumerable.Range(0, ordered.Length)));
        for (int index = 0; index < ordered.Length; index++)
        {
            double axisAu = ordered[index].orbit.semiMajorAxisMeters / PhysicalConstants.AstronomicalUnit;
            Assert.That(axisAu, Is.InRange(0.1d, 30d));
            if (index > 0)
            {
                double previous = ordered[index - 1].orbit.semiMajorAxisMeters;
                Assert.That(ordered[index].orbit.semiMajorAxisMeters / previous, Is.GreaterThan(1.5d));
            }
        }
    }

    [Test]
    public void GeneratedPlanets_HaveConsistentPhysicalGravity()
    {
        var generator = new ProceduralInterstellarGenerator(
            987654,
            Array.Empty<GalaxyResourceCatalogEntry>());
        int checkedPlanets = 0;
        for (long z = -4; z <= 4 && checkedPlanets < 20; z++)
        for (long y = -4; y <= 4 && checkedPlanets < 20; y++)
        for (long x = -4; x <= 4 && checkedPlanets < 20; x++)
        {
            var coordinate = new InterstellarCoordinate(x, y, z);
            if (!generator.HasPlanet(coordinate))
                continue;
            PlanetPhysicalProfile physical = generator.GeneratePlanet(coordinate).celestial.Physical;
            double derivedGravity = physical.gravitationalParameter
                / (physical.radiusMeters * physical.radiusMeters);
            Assert.That(physical.radiusMeters, Is.InRange(1_500_000d, 10_000_000d));
            Assert.That(physical.surfaceGravity / PhysicalConstants.StandardGravity, Is.InRange(0.15d, 1.8d));
            Assert.That(physical.surfaceGravity, Is.EqualTo(derivedGravity).Within(0.000001d));
            checkedPlanets++;
        }
        Assert.That(checkedPlanets, Is.EqualTo(20));
    }

    [Test]
    public void UnitFormatter_UsesPhysicalScaleUnits()
    {
        Assert.That(SpaceflightUnitFormatter.FormatSpeed(120d), Is.EqualTo("120 m/s"));
        Assert.That(SpaceflightUnitFormatter.FormatSpeed(7_900d), Does.Contain("km/s"));
        Assert.That(SpaceflightUnitFormatter.FormatDistance(384_400_000d), Is.EqualTo("384400 km"));
        Assert.That(SpaceflightUnitFormatter.FormatDistance(PhysicalConstants.AstronomicalUnit), Is.EqualTo("1 AU"));
        Assert.That(SpaceflightUnitFormatter.FormatDistance(PhysicalConstants.LightYear * 4.2d), Does.Contain("ly"));
        Assert.That(SpaceflightUnitFormatter.FormatMass(12_000d), Is.EqualTo("12 t"));
        Assert.That(SpaceflightUnitFormatter.FormatForce(300_000d), Is.EqualTo("300 kN"));
    }

    [Test]
    public void KilometerPresentationScale_ConvertsDistanceVelocityForceAndTorque()
    {
        Assert.That(SpaceKilometerScale.ToKilometerUnits(1000d), Is.EqualTo(1d));
        Assert.That(
            SpaceKilometerScale.ToKilometerUnitsPerSecond(250d),
            Is.EqualTo(0.25d));
        Assert.That(
            SpaceKilometerScale.ToKilometerUnitsPerSecondSquared(6d),
            Is.EqualTo(0.006d));
        Assert.That(
            SpaceKilometerScale.ToKilometerForceUnits(72_000d),
            Is.EqualTo(72d));
        Assert.That(
            SpaceKilometerScale.ToKilometerTorqueUnits(1_000_000d),
            Is.EqualTo(1d));
    }

    [Test]
    public void KilometerPresentationScale_PreservesSiForceAcceleration()
    {
        const double massKilograms = 12_000d;
        const double forceNewtons = 72_000d;
        double accelerationMetersPerSecondSquared = forceNewtons / massKilograms;
        double presentationAcceleration =
            SpaceKilometerScale.ToKilometerUnitsPerSecondSquared(
                accelerationMetersPerSecondSquared);
        double presentationDistanceAfterTenSeconds =
            SpaceKilometerScale.ToKilometerUnits(250d * 10d);

        Assert.That(accelerationMetersPerSecondSquared, Is.EqualTo(6d));
        Assert.That(presentationAcceleration, Is.EqualTo(0.006d));
        Assert.That(presentationDistanceAfterTenSeconds, Is.EqualTo(2.5d));
    }

    [Test]
    public void EarthNearObservationPose_UsesKilometerUnits()
    {
        PlanetPhysicalProfile earth = PlanetPhysicalProfile.CreateEarthLike();
        double nearDistanceMeters = earth.radiusMeters * 4.2d;

        Assert.That(
            SpaceKilometerScale.ToKilometerUnits(earth.radiusMeters),
            Is.EqualTo(6371d).Within(0.001d));
        Assert.That(
            SpaceKilometerScale.ToKilometerUnits(nearDistanceMeters),
            Is.EqualTo(26758.2d).Within(0.01d));
    }

    [Test]
    public void HighSpeedInteractionThresholds_UseHysteresis()
    {
        Assert.That(
            InterstellarFlightRuntime.ShouldEnterHighSpeed(999f),
            Is.False);
        Assert.That(
            InterstellarFlightRuntime.ShouldEnterHighSpeed(1000f),
            Is.True);
        Assert.That(
            InterstellarFlightRuntime.CanReturnToTactical(601f),
            Is.False);
        Assert.That(
            InterstellarFlightRuntime.CanReturnToTactical(600f),
            Is.True);
    }

    [Test]
    public void DualScaleRenderingLayers_AreConfigured()
    {
        Assert.That(
            LayerMask.NameToLayer("SpaceKilometerView"),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(
            LayerMask.NameToLayer("SpacePhysicsBubble"),
            Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ShipAssets_UseRealMassAndForceForAcceleration()
    {
        ShipHullDefinition hull = AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(
            "Assets/SpacecraftEditor/Data/hull_balanced.asset");
        ShipPartDefinition thruster = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(
            "Assets/SpacecraftEditor/Data/thruster_large.asset");

        Assert.That(hull.BaseMass, Is.EqualTo(12_000f).Within(0.01f));
        Assert.That(thruster.BaseThrust, Is.EqualTo(300_000f).Within(0.01f));
        Assert.That(thruster.BaseThrust / hull.BaseMass, Is.EqualTo(25f).Within(0.01f));
        Assert.That(hull.FlightProfile.DefaultTargetSpeed, Is.EqualTo(250f));
        Assert.That(hull.FlightProfile.MaximumTargetSpeed, Is.EqualTo(10_000f));
    }

    [Test]
    public void RuntimeSpacecraft_UsesMassScaledInertiaTensor()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab");
        ShipHullDefinition hull = AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(
            "Assets/SpacecraftEditor/Data/hull_balanced.asset");
        GameObject instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            Rigidbody body = instance.GetComponentInChildren<Rigidbody>(true);
            ShipAssembly assembly = instance.GetComponentInChildren<ShipAssembly>(true);
            ShipHullController hullController =
                instance.GetComponentInChildren<ShipHullController>(true);

            Assert.That(hullController.ApplyHull(hull), Is.True);
            assembly.SetHullMass(hull.BaseMass);

            Assert.That(body.inertiaTensor.x, Is.EqualTo(40_840f).Within(1f));
            Assert.That(body.inertiaTensor.y, Is.EqualTo(45_000f).Within(1f));
            Assert.That(body.inertiaTensor.z, Is.EqualTo(13_840f).Within(1f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void PureTranslationRcsSpool_DoesNotInjectLargeTorque()
    {
        var ship = new GameObject("TranslationAllocatorTest");
        try
        {
            Rigidbody body = ship.AddComponent<Rigidbody>();
            body.mass = 12_000f;
            ShipHullDefinition hull = AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(
                "Assets/SpacecraftEditor/Data/hull_balanced.asset");
            var allocator = new SpacecraftThrusterAllocator();
            allocator.Rebuild(null, hull);

            allocator.SolveAndApply(
                body,
                ship.transform,
                Vector3.back * 72_000f,
                Vector3.zero,
                1.35f,
                1f,
                0.02f);

            Assert.That(allocator.AppliedLocalTorque.magnitude, Is.LessThan(100f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }
    }

    static double Magnitude(DoubleVector3 value)
        => Math.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z);
}
