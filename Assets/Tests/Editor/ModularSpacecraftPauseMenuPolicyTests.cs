using NUnit.Framework;
using UnityPlanet.ModularAssembly;

public sealed class ModularSpacecraftPauseMenuPolicyTests
{
    [Test]
    public void EscapeOnlyOpensPauseDuringUnblockedFlight()
    {
        Assert.That(
            ModularSpacecraftPauseMenu.ShouldOpenForEscape(
                true,
                true,
                false,
                false),
            Is.True);
        Assert.That(
            ModularSpacecraftPauseMenu.ShouldOpenForEscape(
                false,
                true,
                false,
                false),
            Is.False);
        Assert.That(
            ModularSpacecraftPauseMenu.ShouldOpenForEscape(
                true,
                false,
                false,
                false),
            Is.False);
        Assert.That(
            ModularSpacecraftPauseMenu.ShouldOpenForEscape(
                true,
                true,
                true,
                false),
            Is.False);
        Assert.That(
            ModularSpacecraftPauseMenu.ShouldOpenForEscape(
                true,
                true,
                false,
                true),
            Is.False);
    }
}
