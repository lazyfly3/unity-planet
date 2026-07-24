using NUnit.Framework;

public sealed class GalaxyVisitedPlanetNameTests
{
    [Test]
    public void NormalizePlanetName_TrimsAndAcceptsChineseName()
    {
        bool valid = GalaxyTravelManager.TryNormalizePlanetDisplayName(
            "  苍蓝港湾  ",
            out string normalized,
            out string error);

        Assert.That(valid, Is.True, error);
        Assert.That(normalized, Is.EqualTo("苍蓝港湾"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("第一行\n第二行")]
    [TestCase("星球\t名称")]
    public void NormalizePlanetName_RejectsInvalidInput(string value)
    {
        bool valid = GalaxyTravelManager.TryNormalizePlanetDisplayName(
            value,
            out _,
            out string error);

        Assert.That(valid, Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void NormalizePlanetName_RejectsMoreThanTwentyFourVisibleCharacters()
    {
        string tooLong = new string('星', 25);

        bool valid = GalaxyTravelManager.TryNormalizePlanetDisplayName(
            tooLong,
            out _,
            out string error);

        Assert.That(valid, Is.False);
        Assert.That(error, Does.Contain("24"));
    }
}
