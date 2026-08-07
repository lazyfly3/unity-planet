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

    // Add new enhancement families here. The PCG and UI consume this catalog
    // without requiring new scene objects or changes to spacecraft physics.
    public static class EnhancementTypeCatalog
    {
        static readonly EnhancementTypeDefinition[] Definitions =
        {
            new EnhancementTypeDefinition(
                "reinforced_hull_lattice",
                "强化舰体晶格",
                "全船舰体完整度 +{0:0.#}%",
                EnhancementTarget.ShipWide,
                EnhancementStat.HullIntegrity,
                9f,
                2.15f,
                1f,
                90,
                280,
                5),
            new EnhancementTypeDefinition(
                "structure_nanobond",
                "结构纳米键合",
                "结构模块耐久 +{0:0.#}%",
                EnhancementTarget.Structure,
                EnhancementStat.ModuleIntegrity,
                11f,
                2.1f,
                1f,
                85,
                270,
                5),
            new EnhancementTypeDefinition(
                "thruster_vector_calibration",
                "推进矢量校准",
                "推进系统有效输出 +{0:0.#}%",
                EnhancementTarget.Propulsion,
                EnhancementStat.ThrustEfficiency,
                7f,
                2.25f,
                1f,
                100,
                310,
                5),
            new EnhancementTypeDefinition(
                "weapon_capacitor_overdrive",
                "武器电容超频",
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
                "自适应供弹循环",
                "武器射速 +{0:0.#}%",
                EnhancementTarget.Weapon,
                EnhancementStat.FireRate,
                6f,
                2.15f,
                0.9f,
                105,
                320,
                5),
            new EnhancementTypeDefinition(
                "phase_shield_matrix",
                "相位护盾矩阵",
                "护盾容量 +{0:0.#}%",
                EnhancementTarget.Defense,
                EnhancementStat.ShieldCapacity,
                10f,
                2.2f,
                1f,
                105,
                325,
                5),
            new EnhancementTypeDefinition(
                "reactor_storage_loop",
                "反应堆储能回路",
                "能源容量 +{0:0.#}%",
                EnhancementTarget.Energy,
                EnhancementStat.EnergyCapacity,
                12f,
                2.05f,
                1f,
                95,
                295,
                5),
            new EnhancementTypeDefinition(
                "thermal_recirculation",
                "热量回流系统",
                "冷却效率 +{0:0.#}%",
                EnhancementTarget.Energy,
                EnhancementStat.CoolingEfficiency,
                10f,
                2.15f,
                1f,
                90,
                285,
                5),
            new EnhancementTypeDefinition(
                "power_bus_optimization",
                "能源总线优化",
                "能源使用效率 +{0:0.#}%",
                EnhancementTarget.Energy,
                EnhancementStat.EnergyEfficiency,
                8f,
                2.1f,
                0.95f,
                100,
                300,
                5)
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
}
