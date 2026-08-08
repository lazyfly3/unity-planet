using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityPlanet.SpaceStation.Enhancement
{
    public enum EnhancementRarity
    {
        Normal,
        Gold
    }

    public enum EnhancementTarget
    {
        ShipWide,
        Structure,
        Propulsion,
        Weapon,
        Defense,
        Energy
    }

    public enum EnhancementStat
    {
        HullIntegrity,
        ModuleIntegrity,
        ThrustEfficiency,
        WeaponDamage,
        FireRate,
        ShieldCapacity,
        EnergyCapacity,
        CoolingEfficiency,
        EnergyEfficiency
    }

    public sealed class EnhancementTypeDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string DescriptionTemplate { get; }
        public EnhancementTarget Target { get; }
        public EnhancementStat Stat { get; }
        public float BaseMagnitude { get; }
        public float GoldMagnitudeMultiplier { get; }
        public float BaseWeight { get; }
        public int NormalCost { get; }
        public int GoldCost { get; }
        public int MaximumStacks { get; }

        public EnhancementTypeDefinition(
            string id,
            string displayName,
            string descriptionTemplate,
            EnhancementTarget target,
            EnhancementStat stat,
            float baseMagnitude,
            float goldMagnitudeMultiplier,
            float baseWeight,
            int normalCost,
            int goldCost,
            int maximumStacks)
        {
            Id = id;
            DisplayName = displayName;
            DescriptionTemplate = descriptionTemplate;
            Target = target;
            Stat = stat;
            BaseMagnitude = baseMagnitude;
            GoldMagnitudeMultiplier = goldMagnitudeMultiplier;
            BaseWeight = baseWeight;
            NormalCost = normalCost;
            GoldCost = goldCost;
            MaximumStacks = maximumStacks;
        }

        public string FormatDescription(float magnitude)
        {
            return string.Format(
                DescriptionTemplate,
                magnitude);
        }
    }

    // Blue cards are intentionally limited to effects that are connected to
    // the live modular-combat runtime. Additions belong here only after their
    // gameplay stat has a real consumer.
    public static class EnhancementTypeCatalog
    {
        static readonly EnhancementTypeDefinition[] Definitions =
        {
            new EnhancementTypeDefinition(
                "reinforced_hull_lattice",
                "模块耐久提升",
                "全部模块耐久 +{0:0.#}%",
                EnhancementTarget.ShipWide,
                EnhancementStat.ModuleIntegrity,
                9f,
                2.15f,
                1f,
                90,
                280,
                5),
            new EnhancementTypeDefinition(
                "weapon_capacitor_overdrive",
                "武器伤害提升",
                "武器伤害 +{0:0.#}%",
                EnhancementTarget.Weapon,
                EnhancementStat.WeaponDamage,
                8f,
                2.2f,
                1f,
                110,
                330,
                5),
            new EnhancementTypeDefinition(
                "adaptive_feed_cycle",
                "武器射速提升",
                "武器射速 +{0:0.#}%",
                EnhancementTarget.Weapon,
                EnhancementStat.FireRate,
                6f,
                2.15f,
                0.9f,
                105,
                320,
                5),
        };

        static readonly Dictionary<string, EnhancementTypeDefinition> ById =
            Definitions.ToDictionary(
                definition => definition.Id,
                definition => definition,
                StringComparer.Ordinal);

        public static IReadOnlyList<EnhancementTypeDefinition> All =>
            Definitions;

        public static bool TryGet(
            string id,
            out EnhancementTypeDefinition definition)
        {
            return ById.TryGetValue(
                id ?? string.Empty,
                out definition);
        }
    }

    /// <summary>
    /// Immutable percentages from installed blue cards, converted into the
    /// multipliers consumed by the player's live module and weapon systems.
    /// </summary>
    public sealed class EnhancementRuntimeModifiers
    {
        public static EnhancementRuntimeModifiers None { get; } =
            new EnhancementRuntimeModifiers(1f, 1f, 1f);

        public float ModuleIntegrityMultiplier { get; }
        public float WeaponDamageMultiplier { get; }
        public float FireRateMultiplier { get; }

        EnhancementRuntimeModifiers(
            float moduleIntegrityMultiplier,
            float weaponDamageMultiplier,
            float fireRateMultiplier)
        {
            ModuleIntegrityMultiplier = moduleIntegrityMultiplier;
            WeaponDamageMultiplier = weaponDamageMultiplier;
            FireRateMultiplier = fireRateMultiplier;
        }

        public static EnhancementRuntimeModifiers LoadCurrent()
        {
            return FromProgress(GalaxyCurrencyService.LoadOrCreate());
        }

        public static EnhancementRuntimeModifiers FromProgress(
            GalaxyEnhancementProgressData progress)
        {
            float integrityPercent = 0f;
            float damagePercent = 0f;
            float fireRatePercent = 0f;

            AcquiredEnhancementData[] acquired =
                progress?.acquiredEnhancements;
            if (acquired != null)
            {
                foreach (AcquiredEnhancementData entry in acquired)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    float magnitude = Math.Max(0f, entry.totalMagnitude);
                    // Preserve durability already earned from the retired
                    // structure-only card when upgrading old save slots.
                    if (string.Equals(
                            entry.definitionId,
                            "structure_nanobond",
                            StringComparison.Ordinal))
                    {
                        integrityPercent += magnitude;
                        continue;
                    }

                    if (!EnhancementTypeCatalog.TryGet(
                            entry.definitionId,
                            out EnhancementTypeDefinition definition))
                    {
                        continue;
                    }

                    switch (definition.Stat)
                    {
                        case EnhancementStat.ModuleIntegrity:
                            integrityPercent += magnitude;
                            break;
                        case EnhancementStat.WeaponDamage:
                            damagePercent += magnitude;
                            break;
                        case EnhancementStat.FireRate:
                            fireRatePercent += magnitude;
                            break;
                    }
                }
            }

            return new EnhancementRuntimeModifiers(
                PercentToMultiplier(integrityPercent),
                PercentToMultiplier(damagePercent),
                PercentToMultiplier(fireRatePercent));
        }

        public void ApplyToWeapon(
            UnityPlanet.ModularAssembly.WeaponProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            profile.damage = Math.Max(
                0f,
                profile.damage * WeaponDamageMultiplier);
            profile.shotsPerSecond = Math.Max(
                0.01f,
                profile.shotsPerSecond * FireRateMultiplier);
        }

        public void ApplyToStructureGraph(
            UnityPlanet.ModularAssembly.VehicleStructureGraph graph)
        {
            if (graph == null)
            {
                return;
            }

            graph.ConfigureIntegrityMultipliers(
                ModuleIntegrityMultiplier,
                ModuleIntegrityMultiplier,
                ModuleIntegrityMultiplier);
        }

        static float PercentToMultiplier(float percent)
        {
            return 1f + Math.Max(0f, percent) * 0.01f;
        }
    }
}
