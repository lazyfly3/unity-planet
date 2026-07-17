using System;
using System.Collections.Generic;
using UnityEngine;

public enum AntelopeFamilyVariant
{
    Random,
    Standard,
    Biped,
    Glow,
    Robot,
    Bone
}

[Serializable]
public sealed class CreatureDescriptorOption
{
    public string stableId;
    public string displayName;
    [Min(0f)] public float weight;
    public bool explicitNone;
    public List<CreatureDescriptorGroup> children = new List<CreatureDescriptorGroup>();
    public List<string> moduleIds = new List<string>();
}

[Serializable]
public sealed class CreatureDescriptorGroup
{
    public string typeId;
    public List<CreatureDescriptorOption> options = new List<CreatureDescriptorOption>();
}

[Serializable]
public sealed class CreatureModuleDefinition
{
    public string stableId;
    public GameObject prefab;
    public bool required;
    public string[] requiredBones;
}

[Serializable]
public sealed class ImportedFootContactProfile
{
    [Min(0.001f)] public float soleOffset = .03f;
    [Min(0.001f)] public float halfWidth = .08f;
    [Min(0.001f)] public float heelDistance = .08f;
    [Min(0.001f)] public float toeDistance = .12f;
}

[Serializable]
public sealed class CreatureSemanticLegDefinition
{
    public string upperBone;
    public string lowerBone;
    public string ankleBone;
    public string footBone;
    public bool isLeft;
    public bool isFront;
    [Range(0f, 1f)] public float walkPhase;
    [Range(0f, 1f)] public float trotPhase;
    public Vector3 upperAxisLocal = Vector3.down;
    public Vector3 lowerAxisLocal = Vector3.down;
    public Vector3 ankleAxisLocal = Vector3.down;
    public Vector3 bendHintRootLocal = Vector3.forward;
    [Min(0.001f)] public float upperLength = .5f;
    [Min(0.001f)] public float lowerLength = .5f;
    [Min(0.001f)] public float ankleLength = .25f;
    public ImportedFootContactProfile footContact = new ImportedFootContactProfile();
}

[Serializable]
public sealed class CreatureRigSemantics
{
    public List<CreatureSemanticLegDefinition> legs = new List<CreatureSemanticLegDefinition>();
    public string pelvisBone = "HipJNT";
    public string neckBone = "Neck1JNT";
    public string headBone = "HeadJNT";
    public string[] longTailBones = { "TailLong1JNT", "TailLong2JNT", "TailLong3JNT", "TailLong4JNT", "TailLong5JNT" };
    public string[] shortTailBones = { "Tail1JNT", "Tail2JNT", "Tail3JNT" };
    public string[] footContactBones = { "LF2FootJNT", "RF2FootJNT", "LBFootJNT", "RBFootJNT" };
}

[Serializable]
public sealed class CreatureBoneMorphChannel
{
    public string channelId;
    public string[] boneNames;
    public Vector3 axis = Vector3.one;
    public Vector2 multiplierRange = new Vector2(.9f, 1.1f);
}

[Serializable]
public sealed class CreatureFamilyVariantDefinition
{
    public string stableId;
    public AntelopeFamilyVariant variant;
    [Min(0f)] public float bioWeight = 1f;
    public CreatureTopology topology = CreatureTopology.Quadruped;
    public GameObject rigPrefab;
    public GameObject safeFallbackPrefab;
    public CreatureRigSemantics semantics = new CreatureRigSemantics();
    public List<CreatureDescriptorGroup> descriptorRoots = new List<CreatureDescriptorGroup>();
    public List<CreatureModuleDefinition> modules = new List<CreatureModuleDefinition>();
    public List<CreatureBoneMorphChannel> morphChannels = new List<CreatureBoneMorphChannel>();
}

[CreateAssetMenu(menuName = "Voxel Planet/Creatures/Descriptor Creature Family")]
public sealed class CreatureRigFamilyDefinition : ScriptableObject
{
    public string stableId = "nms_antelope";
    public int catalogVersion = 1;
    public int maxVisibleModules = 24;
    public int maxRenderers = 32;
    [Min(.0001f)] public float rigImportScale = 1f;
    public Quaternion visualRotation = Quaternion.Euler(0f, 180f, 0f);
    public Vector2 uniformScaleRange = new Vector2(.9f, 1.1f);
    public RuntimeAnimatorController locomotionController;
    public List<CreatureFamilyVariantDefinition> variants = new List<CreatureFamilyVariantDefinition>();

    public CreatureFamilyVariantDefinition FindVariant(string id)
    {
for (int i = 0; i < variants.Count; i++)
            if (variants[i] != null && string.Equals(variants[i].stableId, id, StringComparison.Ordinal))
                return variants[i];
        return null;
}

    public CreatureFamilyVariantDefinition FindVariant(AntelopeFamilyVariant variant)
    {
for (int i = 0; i < variants.Count; i++)
            if (variants[i] != null && variants[i].variant == variant)
                return variants[i];
        return null;
}
}

[Serializable]
public sealed class CreatureMorphValue
{
    public string channelId;
    public float multiplier = 1f;
}

[Serializable]
public sealed class GeneratedCreatureSpeciesDefinition
{
    public int generatorVersion = 7;
    public int seed;
    public int catalogVersion;
    public string familyId;
    public string variantId;
    public CreatureTopology topology;
    public float uniformScale = 1f;
    public Color primaryColor = Color.white;
    public Color secondaryColor = Color.gray;
    public List<string> selectedModuleIds = new List<string>();
    public List<CreatureMorphValue> morphValues = new List<CreatureMorphValue>();

    public string BuildSignature()
    {
return familyId + ":" + variantId + ":" + string.Join(",", selectedModuleIds);
}
}

public static class DescriptorCreatureSpeciesGenerator
{
    const int CurrentVersion = 7;
    static readonly Dictionary<string, GeneratedCreatureSpeciesDefinition> Cache =
        new Dictionary<string, GeneratedCreatureSpeciesDefinition>(StringComparer.Ordinal);

    public static GeneratedCreatureSpeciesDefinition Generate(
        CreatureRigFamilyDefinition family,
        int seed,
        AntelopeFamilyVariant forcedVariant = AntelopeFamilyVariant.Random)
    {
if (family == null)
            return null;

        string cacheKey = family.stableId + ":" + family.catalogVersion + ":" + seed + ":" + (int)forcedVariant;
        if (Cache.TryGetValue(cacheKey, out GeneratedCreatureSpeciesDefinition cached))
            return Clone(cached);

        var random = new StableRandom(seed, family.catalogVersion);
        CreatureFamilyVariantDefinition variant = forcedVariant == AntelopeFamilyVariant.Random
            ? SelectWeightedVariant(family.variants, ref random)
            : family.FindVariant(forcedVariant);
        if (variant == null)
            variant = FirstUsableVariant(family.variants);
        if (variant == null)
            return null;

        var result = new GeneratedCreatureSpeciesDefinition
        {
            generatorVersion = CurrentVersion,
            seed = seed,
            catalogVersion = family.catalogVersion,
            familyId = family.stableId,
            variantId = variant.stableId,
            topology = variant.topology,
            uniformScale = random.Range(family.uniformScaleRange.x, family.uniformScaleRange.y),
            primaryColor = BuildColor(ref random, .48f, .88f, .58f, .94f),
            secondaryColor = BuildColor(ref random, .35f, .78f, .55f, 1f)
        };

        var selected = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < variant.descriptorRoots.Count; i++)
            ResolveGroup(variant.descriptorRoots[i], ref random, selected, result.selectedModuleIds);

        for (int i = 0; i < variant.modules.Count; i++)
        {
            CreatureModuleDefinition module = variant.modules[i];
            if (module != null && module.required && selected.Add(module.stableId))
                result.selectedModuleIds.Add(module.stableId);
        }

        for (int i = 0; i < variant.morphChannels.Count; i++)
        {
            CreatureBoneMorphChannel channel = variant.morphChannels[i];
            if (channel == null || string.IsNullOrEmpty(channel.channelId))
                continue;
            result.morphValues.Add(new CreatureMorphValue
            {
                channelId = channel.channelId,
                multiplier = random.Range(channel.multiplierRange.x, channel.multiplierRange.y)
            });
        }

        Cache[cacheKey] = Clone(result);
        return result;
}

    public static void ClearCache()
    {
Cache.Clear();
}

    static void ResolveGroup(
        CreatureDescriptorGroup group,
        ref StableRandom random,
        HashSet<string> selected,
        List<string> moduleIds)
    {
        if (group == null || group.options == null || group.options.Count == 0)
            return;

        CreatureDescriptorOption option = SelectWeightedOption(group.options, ref random);
        if (option == null)
            return;

        if (!option.explicitNone)
        {
            for (int i = 0; i < option.moduleIds.Count; i++)
            {
                string moduleId = option.moduleIds[i];
                if (!string.IsNullOrEmpty(moduleId) && selected.Add(moduleId))
                    moduleIds.Add(moduleId);
            }
        }

        for (int i = 0; i < option.children.Count; i++)
            ResolveGroup(option.children[i], ref random, selected, moduleIds);
    }

    static CreatureFamilyVariantDefinition SelectWeightedVariant(
        List<CreatureFamilyVariantDefinition> variants,
        ref StableRandom random)
    {
        float total = 0f;
        for (int i = 0; i < variants.Count; i++)
            if (variants[i] != null && variants[i].rigPrefab != null)
                total += Mathf.Max(0f, variants[i].bioWeight);
        if (total <= 0f)
            return FirstUsableVariant(variants);

        float choice = random.Range(0f, total);
        for (int i = 0; i < variants.Count; i++)
        {
            CreatureFamilyVariantDefinition variant = variants[i];
            if (variant == null || variant.rigPrefab == null)
                continue;
            choice -= Mathf.Max(0f, variant.bioWeight);
            if (choice <= 0f)
                return variant;
        }
        return FirstUsableVariant(variants);
    }

    static CreatureFamilyVariantDefinition FirstUsableVariant(List<CreatureFamilyVariantDefinition> variants)
    {
        for (int i = 0; i < variants.Count; i++)
            if (variants[i] != null && variants[i].rigPrefab != null)
                return variants[i];
        return null;
    }

    static CreatureDescriptorOption SelectWeightedOption(
        List<CreatureDescriptorOption> options,
        ref StableRandom random)
    {
        float total = 0f;
        bool hasPositiveWeight = false;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] == null)
                continue;
            float weight = Mathf.Max(0f, options[i].weight);
            total += weight;
            hasPositiveWeight |= weight > 0f;
        }

        if (!hasPositiveWeight)
        {
            int count = 0;
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null)
                    count++;
            if (count == 0)
                return null;
            int target = random.Range(0, count);
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i] == null)
                    continue;
                if (target-- == 0)
                    return options[i];
            }
        }

        float choice = random.Range(0f, total);
        for (int i = 0; i < options.Count; i++)
        {
            CreatureDescriptorOption option = options[i];
            if (option == null)
                continue;
            choice -= Mathf.Max(0f, option.weight);
            if (choice <= 0f)
                return option;
        }
        return null;
    }

    static Color BuildColor(ref StableRandom random, float minSaturation, float maxSaturation, float minValue, float maxValue)
    {
        return Color.HSVToRGB(random.Value(), random.Range(minSaturation, maxSaturation), random.Range(minValue, maxValue));
    }

    static GeneratedCreatureSpeciesDefinition Clone(GeneratedCreatureSpeciesDefinition source)
    {
        var clone = new GeneratedCreatureSpeciesDefinition
        {
            generatorVersion = source.generatorVersion,
            seed = source.seed,
            catalogVersion = source.catalogVersion,
            familyId = source.familyId,
            variantId = source.variantId,
            topology = source.topology,
            uniformScale = source.uniformScale,
            primaryColor = source.primaryColor,
            secondaryColor = source.secondaryColor
        };
        clone.selectedModuleIds.AddRange(source.selectedModuleIds);
        for (int i = 0; i < source.morphValues.Count; i++)
            clone.morphValues.Add(new CreatureMorphValue
            {
                channelId = source.morphValues[i].channelId,
                multiplier = source.morphValues[i].multiplier
            });
        return clone;
    }

    struct StableRandom
    {
        uint state;

        public StableRandom(int seed, int salt)
        {
            state = Mix(unchecked((uint)seed) ^ unchecked((uint)salt * 0x9E3779B9u));
            if (state == 0u)
                state = 0xA341316Cu;
        }

        public float Value()
        {
uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return (value & 0x00FFFFFFu) / 16777216f;
}

        public int Range(int minimum, int maximum)
        {
return minimum + Mathf.FloorToInt(Value() * (maximum - minimum));
}

        public float Range(float minimum, float maximum)
        {
return Mathf.Lerp(minimum, maximum, Value());
}

        static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }
    }
}
