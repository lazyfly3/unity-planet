using System.Linq;
using System.Reflection;
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
    public void SelfRepair_EffectPreviewShowsConcreteValuesAtEveryLevel()
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

    [Test]
    public void SkillDescriptions_DoNotUseOnlyCooldownUpgradeCopy()
    {
        foreach (PlayerSkillDefinition definition in PlayerSkillCatalog.All)
        {
            StringAssert.DoesNotContain(
                "只降低",
                definition.Description);
        }
    }

    [Test]
    public void EveryUpgradeableSkill_HasADifferentConcreteNextLevelPreview()
    {
        foreach (PlayerSkillDefinition definition in PlayerSkillCatalog.All)
        {
            string current = PlayerSkillCatalog.EffectForLevel(
                definition.Id,
                1);
            string next = PlayerSkillCatalog.EffectForLevel(
                definition.Id,
                2);

            Assert.That(current, Is.Not.Empty, definition.Id);
            Assert.That(next, Is.Not.Empty, definition.Id);
            Assert.That(next, Is.Not.EqualTo(current), definition.Id);
        }
    }

    [Test]
    public void Normalize_AllowsEveryLoadoutSlotToRemainEmpty()
    {
        var data = new PlayerSkillProgressData
        {
            skills = new[]
            {
                new PlayerSkillProgressEntry
                {
                    skillId = PlayerSkillCatalog.SelfRepairId,
                    level = 1
                }
            },
            equippedSkillIds = new string[
                PlayerSkillProgressService.SlotCount]
        };
        MethodInfo normalize = typeof(PlayerSkillProgressService).GetMethod(
            "Normalize",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(normalize, Is.Not.Null);
        normalize.Invoke(null, new object[] { data });

        Assert.That(
            data.equippedSkillIds.All(string.IsNullOrWhiteSpace),
            Is.True);
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
