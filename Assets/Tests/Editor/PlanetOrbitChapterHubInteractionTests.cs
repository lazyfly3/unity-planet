using NUnit.Framework;
using UnityEngine;

public sealed class PlanetOrbitChapterHubInteractionTests
{
    [Test]
    public void MissionDirections_AreDeterministicAndCoverTheWholeSphere()
    {
        bool positiveX = false;
        bool negativeX = false;
        bool positiveY = false;
        bool negativeY = false;
        bool positiveZ = false;
        bool negativeZ = false;

        for (int seed = -50; seed <= 50; seed++)
        {
            Vector3 first =
                PlanetOrbitChapterHubGeometry.DirectionFromSeed(seed);
            Vector3 second =
                PlanetOrbitChapterHubGeometry.DirectionFromSeed(seed);
            Assert.That(Vector3.Distance(first, second), Is.LessThan(0.0001f));
            Assert.That(first.magnitude, Is.EqualTo(1f).Within(0.0001f));
            positiveX |= first.x > 0.2f;
            negativeX |= first.x < -0.2f;
            positiveY |= first.y > 0.2f;
            negativeY |= first.y < -0.2f;
            positiveZ |= first.z > 0.2f;
            negativeZ |= first.z < -0.2f;
        }

        Assert.That(
            positiveX && negativeX && positiveY && negativeY &&
            positiveZ && negativeZ,
            Is.True);
    }

    [Test]
    public void BacksideMission_StaysHiddenUntilPlanetRotatesItForward()
    {
        Vector3 center = Vector3.zero;
        Vector3 camera = new Vector3(0f, 0f, -100f);
        Vector3 backside = Vector3.forward;
        const float radius = 30f;

        Assert.That(
            PlanetOrbitChapterHubGeometry.IsSurfaceVisible(
                backside,
                center,
                camera,
                radius),
            Is.False);

        Vector3 rotated = Quaternion.AngleAxis(180f, Vector3.up) * backside;
        Assert.That(
            PlanetOrbitChapterHubGeometry.IsSurfaceVisible(
                rotated,
                center,
                camera,
                radius),
            Is.True);
    }

    [Test]
    public void NearHorizonMission_HidesWhenPlanetOccludesItsSurface()
    {
        Vector3 center = Vector3.zero;
        Vector3 camera = new Vector3(0f, 0f, -300f);
        const float radius = 130f;
        Vector3 occludedNearLimb = new Vector3(
            Mathf.Sqrt(1f - 0.35f * 0.35f),
            0f,
            -0.35f);
        Vector3 clearlyVisible = new Vector3(
            Mathf.Sqrt(1f - 0.60f * 0.60f),
            0f,
            -0.60f);

        Assert.That(
            Vector3.Dot(occludedNearLimb, Vector3.back),
            Is.GreaterThan(0f),
            "The old hemisphere-only check would have shown this marker.");
        Assert.That(
            PlanetOrbitChapterHubGeometry.IsSurfaceVisible(
                occludedNearLimb,
                center,
                camera,
                radius),
            Is.False);
        Assert.That(
            PlanetOrbitChapterHubGeometry.IsSurfaceVisible(
                clearlyVisible,
                center,
                camera,
                radius),
            Is.True);
    }

    [Test]
    public void BacksideLandingPath_AlwaysStaysOutsideThePlanet()
    {
        Vector3 center = new Vector3(20f, -5f, 80f);
        Vector3 start = center + Vector3.back * 300f;
        Vector3 targetDirection = Vector3.forward;
        const float radius = 130f;

        for (int index = 0; index <= 400; index++)
        {
            Vector3 point =
                PlanetOrbitChapterHubGeometry.EvaluateSafeLandingPath(
                    start,
                    center,
                    targetDirection,
                    radius,
                    52f,
                    24f,
                    index / 400f,
                    Vector3.up);
            Assert.That(
                Vector3.Distance(point, center),
                Is.GreaterThanOrEqualTo(radius + 1.49f));
        }

        Vector3 end = PlanetOrbitChapterHubGeometry.EvaluateSafeLandingPath(
            start,
            center,
            targetDirection,
            radius,
            52f,
            24f,
            1f,
            Vector3.up);
        Assert.That(
            Vector3.Distance(
                end,
                center + targetDirection * (radius + 1.5f)),
            Is.LessThan(0.001f));
    }
}
