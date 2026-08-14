using NUnit.Framework;
using UnityPlanet.SpaceStation;

public sealed class SpaceStationPauseMenuPolicyTests
{
    [Test]
    public void InstallsOnlyInPlayableSpaceStationScene()
    {
        Assert.That(
            SpaceStationPauseMenu.ShouldInstall(
                "SpaceStationUpgradeTest"),
            Is.True);
        Assert.That(
            SpaceStationPauseMenu.ShouldInstall("StartMenu"),
            Is.False);
        Assert.That(
            SpaceStationPauseMenu.ShouldInstall("ModularAssemblyLab"),
            Is.False);
    }

    [Test]
    public void EscapeRequiresAvailableFirstPersonInput()
    {
        Assert.That(
            SpaceStationPauseMenu.ShouldOpenForEscape(true, true),
            Is.True);
        Assert.That(
            SpaceStationPauseMenu.ShouldOpenForEscape(false, true),
            Is.False);
        Assert.That(
            SpaceStationPauseMenu.ShouldOpenForEscape(true, false),
            Is.False);
    }
}
