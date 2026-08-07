using NUnit.Framework;
using UnityPlanet.SpaceStation.Skills;

public sealed class PlayerSkillSelfRepairTests
{
    [Test]
    public void SelfRepair_LevelOneTakesTenSecondsAndHasSixtySecondCooldown()
    {
        Assert.That(
            PlayerSkillCatalog.SelfRepairDurationSeconds,
            Is.EqualTo(10f));
        Assert.That(
            PlayerSkillCatalog.TryGet(
                PlayerSkillCatalog.SelfRepairId,
                out PlayerSkillDefinition definition),
            Is.True);
        Assert.That(definition.CooldownForLevel(1), Is.EqualTo(60f));
    }

    [Test]
    public void SelfRepair_LevelsOnlyReduceCooldown()
    {
        PlayerSkillCatalog.TryGet(
            PlayerSkillCatalog.SelfRepairId,
            out PlayerSkillDefinition definition);

        float[] expected = { 60f, 54f, 48f, 42f, 36f };
        for (int level = 1; level <= expected.Length; level++)
        {
            Assert.That(
                definition.CooldownForLevel(level),
                Is.EqualTo(expected[level - 1]));
            StringAssert.Contains(
                "修复时间  10 秒",
                PlayerSkillCatalog.EffectForLevel(
                    PlayerSkillCatalog.SelfRepairId,
                    level));
        }
    }

    [TestCase(50f)]
    [TestCase(500f)]
    [TestCase(5000f)]
    public void SelfRepair_AnyStartingDamageCompletesInTenSeconds(
        float startingIntegrity)
    {
        const float step = 0.02f;
        float remaining = startingIntegrity;
        float elapsed = 0f;
        for (int index = 0; index < 500; index++)
        {
            float budget =
                PlayerSkillCatalog.SelfRepairIntegrityBudgetForStep(
                    remaining,
                    elapsed,
                    step);
            remaining = System.Math.Max(0f, remaining - budget);
            elapsed += step;
        }

        Assert.That(remaining, Is.LessThan(0.001f));
        Assert.That(elapsed, Is.EqualTo(10f).Within(0.001f));
    }

    [Test]
    public void SelfRepair_DamageAddedDuringRepairStillFinishesAtTenSeconds()
    {
        const float step = 0.02f;
        float remaining = 1000f;
        float elapsed = 0f;
        for (int index = 0; index < 500; index++)
        {
            if (index == 250)
                remaining += 1000f;
            float budget =
                PlayerSkillCatalog.SelfRepairIntegrityBudgetForStep(
                    remaining,
                    elapsed,
                    step);
            remaining = System.Math.Max(0f, remaining - budget);
            elapsed += step;
        }

        Assert.That(remaining, Is.LessThan(0.001f));
    }
}
