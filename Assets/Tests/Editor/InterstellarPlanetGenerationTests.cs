using System;
using NUnit.Framework;
using UnityEngine;

public sealed class InterstellarPlanetGenerationTests
{
    [Test]
    public void GeneratedPlanetsAreDeterministicAndDoNotGenerateRivers()
    {
        var firstGenerator = new ProceduralInterstellarGenerator(
            7319,
            Array.Empty<GalaxyResourceCatalogEntry>());
        var secondGenerator = new ProceduralInterstellarGenerator(
            7319,
            Array.Empty<GalaxyResourceCatalogEntry>());

        GalaxyPlanetDefinition first = firstGenerator.GeneratePlanet(InterstellarCoordinate.Zero);
        GalaxyPlanetDefinition second = secondGenerator.GeneratePlanet(InterstellarCoordinate.Zero);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(first.planetId, Is.EqualTo(second.planetId));
        Assert.That(first.seed, Is.EqualTo(second.seed));
        Assert.That(first.terrain.continentScale, Is.EqualTo(second.terrain.continentScale));
        Assert.That(first.celestial.surfaceGravity, Is.EqualTo(second.celestial.surfaceGravity));
        Assert.That(first.celestial.rotationPeriod, Is.EqualTo(second.celestial.rotationPeriod));
        Assert.That(first.rivers, Is.Not.Null);
        Assert.That(first.rivers.enabled, Is.False);
        Assert.That(first.rivers.riverCount, Is.Zero);
    }

    [Test]
    public void CircularOrbitProducesBoundNearCircularState()
    {
        GameObject root = new GameObject("CelestialRoot");
        GameObject fieldObject = new GameObject("CelestialField");
        try
        {
            var profile = PlanetCelestialProfile.CreateCompatibleDefault();
            profile.surfaceGravity = 6f;
            profile.radius = 100f;
            profile.atmosphereSurfaceDensity = 0f;
            profile.ClampValues();

            CelestialGravityField field = fieldObject.AddComponent<CelestialGravityField>();
            field.Configure(root.transform, profile);
            float orbitalRadius = 140f;
            float circularSpeed = Mathf.Sqrt(profile.gravitationalParameter / orbitalRadius);
            OrbitalState state = field.CalculateOrbit(
                root.transform.position + Vector3.right * orbitalRadius,
                Vector3.forward * circularSpeed);

            Assert.That(state.isBound, Is.True);
            Assert.That(state.eccentricity, Is.LessThan(0.001f));
            Assert.That(state.periapsisAltitude, Is.EqualTo(40f).Within(0.02f));
            Assert.That(state.apoapsisAltitude, Is.EqualTo(40f).Within(0.02f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fieldObject);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
