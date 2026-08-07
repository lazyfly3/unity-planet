using NUnit.Framework;
using UnityPlanet.ModularAssembly;

public sealed class CombatArenaSoftCeilingRuntimeTests
{
    [Test]
    public void BelowSoftBandDoesNotApplyForce()
    {
        float acceleration = CombatArenaSoftCeilingRuntime
            .CalculateSoftCeilingAcceleration(147f, 220f, 72f, 84f);

        Assert.That(acceleration, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void ApproachingCeilingRampsUpAndCrossingItGetsStronger()
    {
        float insideBand = CombatArenaSoftCeilingRuntime
            .CalculateSoftCeilingAcceleration(190f, 220f, 72f, 84f);
        float atCeiling = CombatArenaSoftCeilingRuntime
            .CalculateSoftCeilingAcceleration(220f, 220f, 72f, 84f);
        float aboveCeiling = CombatArenaSoftCeilingRuntime
            .CalculateSoftCeilingAcceleration(240f, 220f, 72f, 84f);

        Assert.That(insideBand, Is.GreaterThan(0f));
        Assert.That(atCeiling, Is.GreaterThan(insideBand));
        Assert.That(aboveCeiling, Is.GreaterThan(atCeiling));
    }
}
