#if UNITY_EDITOR
using NUnit.Framework;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.Tests.Editor
{
    public sealed class CombatBalanceTests
    {
        [Test]
        public void RawDpsBelowThresholdIsUnchanged()
        {
            Assert.That(CombatBalanceRuntime.EffectiveDps(216f), Is.EqualTo(216f).Within(0.001f));
        }

        [Test]
        public void FourMachineGunsRespectHardCap()
        {
            float rawDps = 4f * 18f * 12f;
            float effective = CombatBalanceRuntime.EffectiveDps(rawDps);
            Assert.That(effective, Is.LessThanOrEqualTo(450f));
            Assert.That(effective, Is.GreaterThan(220f));
        }

        [Test]
        public void MoreWeaponsHaveDiminishingButPositiveReturn()
        {
            float two = CombatBalanceRuntime.EffectiveDps(2f * 216f);
            float four = CombatBalanceRuntime.EffectiveDps(4f * 216f);
            Assert.That(four, Is.GreaterThan(two));
            Assert.That(four - two, Is.LessThan(two));
        }

        [Test]
        public void StandardTargetProducesThirtySecondMedianAtExpectedAccuracy()
        {
            float effectiveDps = CombatBalanceRuntime.EffectiveDps(4f * 216f);
            const float hitRate = 0.55f;
            float targetEffectiveDurability = effectiveDps * hitRate * 30f;
            float ttk = targetEffectiveDurability / (effectiveDps * hitRate);
            Assert.That(ttk, Is.InRange(25f, 35f));
        }
    }
}
#endif
