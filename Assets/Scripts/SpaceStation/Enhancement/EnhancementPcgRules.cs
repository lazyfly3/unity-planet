using System;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using UnityEngine;
using UnityPlanet.SpaceStation.Skills;

namespace UnityPlanet.SpaceStation.Enhancement
{
    [Serializable]
    public sealed class EnhancementOfferData
    {
        public string offerId;
        public string definitionId;
        public string skillId;
        public EnhancementRarity rarity;
        public float magnitude;
        public int cost;
    }

    public sealed class ShipEnhancementProfile
    {
        readonly Dictionary<EnhancementTarget, int> counts =
            new Dictionary<EnhancementTarget, int>();

        public int ModuleCount { get; private set; }

        public static ShipEnhancementProfile FromBlueprint(
            ModularBlueprintData blueprint)
        {
            var profile = new ShipEnhancementProfile();
            ModularBlueprintModule[] modules = blueprint != null
                ? blueprint.modules
                : null;
            if (modules == null)
            {
                return profile;
            }

            foreach (ModularBlueprintModule module in modules)
            {
                if (module == null)
                {
                    continue;
                }
                profile.ModuleCount++;
                profile.Increment(Classify(module.moduleId));
            }
            return profile;
        }

        public int Count(EnhancementTarget target)
        {
            return counts.TryGetValue(target, out int value)
                ? value
                : 0;
        }

        public float WeightFor(EnhancementTarget target)
        {
            if (target == EnhancementTarget.ShipWide)
            {
                return 1.1f + ModuleCount * 0.015f;
            }
            int targetCount = Count(target);
            if (targetCount <= 0)
            {
                return 0.32f;
            }
            return 1f + Mathf.Min(2.2f, targetCount * 0.18f);
        }

        void Increment(EnhancementTarget target)
        {
            counts[target] = Count(target) + 1;
        }

        static EnhancementTarget Classify(string moduleId)
        {
            string id = (moduleId ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(id,
                    "thruster", "engine", "rcs", "propulsion",
                    "wing", "wheel", "hover"))
            {
                return EnhancementTarget.Propulsion;
            }
            if (ContainsAny(id,
                    "weapon", "gun", "cannon", "laser", "missile",
                    "turret", "blaster"))
            {
                return EnhancementTarget.Weapon;
            }
            if (ContainsAny(id,
                    "shield", "armor", "armour", "defense", "defence"))
            {
                return EnhancementTarget.Defense;
            }
            if (ContainsAny(id,
                    "battery", "energy", "power", "reactor", "capacitor",
                    "core"))
            {
                return EnhancementTarget.Energy;
            }
            return EnhancementTarget.Structure;
        }

        static bool ContainsAny(string value, params string[] tokens)
        {
            foreach (string token in tokens)
            {
                if (value.Contains(token))
                {
                    return true;
                }
            }
            return false;
        }

    }

    public sealed class EnhancementGenerationContext
    {
        public int WorldSeed;
        public int OfferSequence;
        public int DrawsWithoutGold;
        public ShipEnhancementProfile ShipProfile;
        public IReadOnlyDictionary<string, int> OwnedStacks;
    }

    // All draw probabilities, pity rules and strength budgets live here.
    // The generator only combines catalog entries; it never creates scripts,
    // colliders, joints, rigidbodies or arbitrary runtime behaviours.
    public static class EnhancementPcgRules
    {
        public const int CardsPerDraw = 3;
        public const int DrawCost = 120;
        public const float BaseGoldChance = 0.12f;
        public const int GoldPityDraws = 4;
        public const float NormalMagnitudeVariance = 0.12f;
        public const float GoldMagnitudeVariance = 0.08f;

        public static EnhancementOfferData[] Generate(
            EnhancementGenerationContext context,
            out bool containsGold)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ShipEnhancementProfile profile = context.ShipProfile ??
                                             new ShipEnhancementProfile();
            var random = new System.Random(StableSeed(
                context.WorldSeed,
                context.OfferSequence));
            bool forceGold = context.DrawsWithoutGold >= GoldPityDraws;
            containsGold = forceGold ||
                           random.NextDouble() < BaseGoldChance;
            int goldIndex = containsGold
                ? random.Next(0, CardsPerDraw)
                : -1;

            var available = EnhancementTypeCatalog.All
                .Where(definition => !AtMaximumStacks(
                    definition,
                    context.OwnedStacks))
                .ToList();
            if (available.Count < CardsPerDraw)
            {
                available = EnhancementTypeCatalog.All.ToList();
            }

            var offers = new EnhancementOfferData[CardsPerDraw];
            var selectedStats = new HashSet<EnhancementStat>();
            for (int index = 0; index < offers.Length; index++)
            {
                if (index == goldIndex)
                {
                    IReadOnlyList<PlayerSkillDefinition> skills =
                        PlayerSkillCatalog.All;
                    PlayerSkillDefinition skill = skills[
                        random.Next(0, skills.Count)];
                    offers[index] = new EnhancementOfferData
                    {
                        offerId = context.OfferSequence + "-" + index +
                                  "-" + skill.Id + "-Gold",
                        definitionId = string.Empty,
                        skillId = skill.Id,
                        rarity = EnhancementRarity.Gold,
                        magnitude = 1f,
                        cost = 0
                    };
                    continue;
                }

                EnhancementTypeDefinition definition = PickWeighted(
                    available,
                    selectedStats,
                    profile,
                    random);
                available.Remove(definition);
                selectedStats.Add(definition.Stat);

                EnhancementRarity rarity = EnhancementRarity.Normal;
                float variance = NormalMagnitudeVariance;
                float multiplier = 1f +
                    Mathf.Lerp(-variance, variance, (float)random.NextDouble());

                float magnitude = Mathf.Round(
                    definition.BaseMagnitude * multiplier * 10f) / 10f;

                offers[index] = new EnhancementOfferData
                {
                    offerId = context.OfferSequence + "-" + index + "-" +
                              definition.Id + "-" + rarity,
                    definitionId = definition.Id,
                    skillId = string.Empty,
                    rarity = rarity,
                    magnitude = magnitude,
                    cost = 0
                };
            }
            return offers;
        }

        static EnhancementTypeDefinition PickWeighted(
            List<EnhancementTypeDefinition> candidates,
            HashSet<EnhancementStat> selectedStats,
            ShipEnhancementProfile profile,
            System.Random random)
        {
            List<EnhancementTypeDefinition> diverse = candidates
                .Where(item => !selectedStats.Contains(item.Stat))
                .ToList();
            if (diverse.Count > 0)
            {
                candidates = diverse;
            }

            float total = 0f;
            foreach (EnhancementTypeDefinition candidate in candidates)
            {
                total += candidate.BaseWeight *
                         profile.WeightFor(candidate.Target);
            }

            float roll = (float)random.NextDouble() * total;
            foreach (EnhancementTypeDefinition candidate in candidates)
            {
                roll -= candidate.BaseWeight *
                        profile.WeightFor(candidate.Target);
                if (roll <= 0f)
                {
                    return candidate;
                }
            }
            return candidates[candidates.Count - 1];
        }

        static bool AtMaximumStacks(
            EnhancementTypeDefinition definition,
            IReadOnlyDictionary<string, int> ownedStacks)
        {
            if (ownedStacks == null ||
                !ownedStacks.TryGetValue(definition.Id, out int stacks))
            {
                return false;
            }
            return stacks >= definition.MaximumStacks;
        }

        static int StableSeed(int worldSeed, int sequence)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)worldSeed) * 16777619u;
                hash = (hash ^ (uint)sequence) * 16777619u;
                hash = (hash ^ 0x47434F49u) * 16777619u;
                return (int)hash;
            }
        }
    }
}
