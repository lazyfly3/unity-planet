using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class NmsCreatureVariantSampler
{
    const ulong DescriptorSalt = 0xE7037ED1A0B428DBUL;

    public static bool TrySample(
        NmsCreatureFamilyDefinition family,
        int seed,
        out NmsCreatureSpeciesDefinition species,
        out string error,
        bool forceStandardAntelope = false)
    {
        species = null;
        error = null;
        if (family == null || !family.TryGetManifest(out NmsCreatureFamilyManifestData manifest))
        {
            error = "The requested NMS family has no valid manifest.";
            return false;
        }

        var descriptorRandom = new NmsStableRandom(
            unchecked((ulong)(uint)seed) ^ DescriptorSalt);
        var colorRandom = new NmsStableRandom(
            unchecked((ulong)(uint)seed) ^ StableHash(family.FamilyId));
        HashSet<string> selected = SelectModules(
            manifest.descriptorGroups,
            family.AllowRareModules,
            family.ExcludedModulePrefixes,
            BuildIncompatibleModuleSet(manifest.modules),
            ref descriptorRandom);
        if (forceStandardAntelope)
        {
            selected.Clear();
            selected.Add("_Body_Deer");
            selected.Add("_Head_Deer");
            selected.Add("DeerEyes");
            selected.Add("_HDEars_1");
        }
        EnsureAntelopeTailSocketCovered(family.FamilyId, seed, selected);

        CreatePalette(
            ref colorRandom,
            out Color primary,
            out Color secondary,
            out Color accent);
        species = new NmsCreatureSpeciesDefinition
        {
            seed = seed,
            familyId = family.FamilyId,
            selectedModules = Sorted(selected),
            primaryColor = primary,
            secondaryColor = secondary,
            accentColor = accent,
            signature = BuildSignature(
                family.FamilyId, seed, selected, primary, accent)
        };
        return true;
    }

    public static NmsCreatureSpeciesDefinition CreateManualSpecies(
        string familyId,
        int seed,
        IEnumerable<string> moduleIds,
        Color primary,
        Color secondary,
        Color accent)
    {
        var modules = new HashSet<string>(StringComparer.Ordinal);
        if (moduleIds != null)
            foreach (string module in moduleIds)
                if (!string.IsNullOrEmpty(module))
                    modules.Add(module);
        EnsureAntelopeTailSocketCovered(familyId, seed, modules);
        return new NmsCreatureSpeciesDefinition
        {
            seed = seed,
            familyId = familyId,
            selectedModules = Sorted(modules),
            primaryColor = primary,
            secondaryColor = secondary,
            accentColor = accent,
            signature = BuildSignature(familyId, seed, modules, primary, accent)
        };
    }

    static void EnsureAntelopeTailSocketCovered(
        string familyId, int seed, HashSet<string> selected)
    {
        if (!string.Equals(
            familyId, "AntelopeQuadruped", StringComparison.Ordinal))
            return;
        foreach (string module in selected)
            if (module.StartsWith("_Tail_", StringComparison.Ordinal))
                return;

        string[] safeTails =
        {
            "_Tail_Alien0",
            "_Tail_Alien1",
            "_Tail_Alien4"
        };
        var tailRandom = new NmsStableRandom(
            unchecked((ulong)(uint)seed) ^ 0x94D049BB133111EBUL);
        selected.Add(safeTails[tailRandom.Next(safeTails.Length)]);
    }

    static HashSet<string> SelectModules(
        NmsDescriptorGroupData[] groups,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules,
        ref NmsStableRandom random)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (groups == null)
            return selected;
        for (int i = 0; i < groups.Length; i++)
        {
            NmsDescriptorGroupData group = groups[i];
            if (group != null && (group.path == null || group.path.Length == 0))
                VisitGroup(
                    group, selected, allowRareModules,
                    excludedModulePrefixes, incompatibleModules, ref random);
        }
        return selected;
    }

    static void VisitGroup(
        NmsDescriptorGroupData group,
        HashSet<string> selected,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules,
        ref NmsStableRandom random)
    {
        if (group.candidates == null || group.candidates.Length == 0)
            return;
        NmsDescriptorCandidateData chosen = WeightedChoice(
            group.candidates, allowRareModules, excludedModulePrefixes,
            incompatibleModules, ref random);
        if (chosen == null)
            return;
        if (!string.IsNullOrEmpty(chosen.name))
            selected.Add(chosen.name);
        if (chosen.moduleIds != null)
            for (int i = 0; i < chosen.moduleIds.Length; i++)
                if (!string.IsNullOrEmpty(chosen.moduleIds[i]))
                    selected.Add(chosen.moduleIds[i]);
        if (chosen.children == null)
            return;
        for (int i = 0; i < chosen.children.Length; i++)
            if (chosen.children[i] != null)
                VisitGroup(
                    chosen.children[i], selected, allowRareModules,
                    excludedModulePrefixes, incompatibleModules, ref random);
    }

    static NmsDescriptorCandidateData WeightedChoice(
        NmsDescriptorCandidateData[] candidates,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules,
        ref NmsStableRandom random)
    {
        float total = 0f;
        int allowedCount = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            NmsDescriptorCandidateData candidate = candidates[i];
            if (!IsAllowedCandidate(
                candidate, allowRareModules, excludedModulePrefixes,
                incompatibleModules))
                continue;
            allowedCount++;
            if (candidate.chance > 0f)
                total += candidate.chance;
        }
        if (allowedCount == 0)
            return null;
        if (total <= 0f)
        {
            int selectedIndex = random.Next(allowedCount);
            for (int i = 0; i < candidates.Length; i++)
            {
                NmsDescriptorCandidateData candidate = candidates[i];
                if (!IsAllowedCandidate(
                    candidate, allowRareModules, excludedModulePrefixes,
                    incompatibleModules))
                    continue;
                if (selectedIndex-- == 0)
                    return candidate;
            }
            return null;
        }

        float cursor = random.NextFloat() * total;
        NmsDescriptorCandidateData fallback = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            NmsDescriptorCandidateData candidate = candidates[i];
            if (!IsAllowedCandidate(
                    candidate, allowRareModules, excludedModulePrefixes,
                    incompatibleModules)
                || candidate.chance <= 0f)
                continue;
            fallback = candidate;
            cursor -= candidate.chance;
            if (cursor <= 0f)
                return candidate;
        }
        return fallback;
    }

    static bool IsAllowedCandidate(
        NmsDescriptorCandidateData candidate,
        bool allowRareModules,
        IReadOnlyList<string> excludedModulePrefixes,
        HashSet<string> incompatibleModules)
    {
        if (candidate == null)
            return false;
        if (MatchesExcludedPrefix(candidate.id, excludedModulePrefixes)
            || MatchesExcludedPrefix(candidate.name, excludedModulePrefixes)
            || IsIncompatible(candidate.id, incompatibleModules)
            || IsIncompatible(candidate.name, incompatibleModules))
            return false;
        if (!allowRareModules
            && (IsRareId(candidate.id) || IsRareId(candidate.name)))
            return false;
        if (candidate.moduleIds != null)
            for (int i = 0; i < candidate.moduleIds.Length; i++)
                if (MatchesExcludedPrefix(
                        candidate.moduleIds[i], excludedModulePrefixes)
                    || IsIncompatible(candidate.moduleIds[i], incompatibleModules)
                    || (!allowRareModules && IsRareId(candidate.moduleIds[i])))
                    return false;
        return true;
    }

    static HashSet<string> BuildIncompatibleModuleSet(NmsCreatureModuleData[] modules)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (modules == null)
            return result;
        for (int i = 0; i < modules.Length; i++)
            if (modules[i] != null
                && !modules[i].compatible
                && !string.IsNullOrEmpty(modules[i].name))
                result.Add(modules[i].name);
        return result;
    }

    static bool IsIncompatible(
        string value, HashSet<string> incompatibleModules)
    {
        return !string.IsNullOrEmpty(value)
            && incompatibleModules != null
            && incompatibleModules.Contains(value);
    }

    static bool MatchesExcludedPrefix(
        string value, IReadOnlyList<string> excludedModulePrefixes)
    {
        if (string.IsNullOrEmpty(value) || excludedModulePrefixes == null)
            return false;
        for (int i = 0; i < excludedModulePrefixes.Count; i++)
        {
            string prefix = excludedModulePrefixes[i];
            if (!string.IsNullOrEmpty(prefix)
                && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static bool IsRareId(string value)
    {
        return !string.IsNullOrEmpty(value)
            && value.IndexOf("RARE", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void CreatePalette(
        ref NmsStableRandom random,
        out Color primary,
        out Color secondary,
        out Color accent)
    {
        float hue = random.NextFloat();
        primary = Color.HSVToRGB(
            hue, random.Range(0.38f, 0.68f), random.Range(0.5f, 0.82f));
        secondary = Color.HSVToRGB(
            Mathf.Repeat(hue + random.Range(-0.12f, 0.12f), 1f),
            random.Range(0.28f, 0.58f),
            random.Range(0.58f, 0.9f));
        accent = Color.HSVToRGB(
            Mathf.Repeat(hue + random.Range(0.38f, 0.62f), 1f),
            random.Range(0.45f, 0.78f),
            random.Range(0.62f, 0.95f));
    }

    static string[] Sorted(HashSet<string> values)
    {
        var result = new string[values.Count];
        values.CopyTo(result);
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    static string BuildSignature(
        string familyId,
        int seed,
        HashSet<string> modules,
        Color primary,
        Color accent)
    {
        string[] sorted = Sorted(modules);
        var builder = new StringBuilder(familyId).Append('|').Append(seed);
        for (int i = 0; i < sorted.Length; i++)
            builder.Append('|').Append(sorted[i]);
        builder.Append('|').Append(ColorUtility.ToHtmlStringRGB(primary));
        builder.Append('|').Append(ColorUtility.ToHtmlStringRGB(accent));
        return builder.ToString();
    }

    public static ulong StableHash(string value)
    {
        ulong hash = 1469598103934665603UL;
        if (value == null)
            return hash;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }
}

public struct NmsStableRandom
{
    ulong state;

    public NmsStableRandom(ulong seed)
    {
        state = seed != 0UL ? seed : 0x9E3779B97F4A7C15UL;
    }

    public int Next(int maximum)
    {
        return maximum <= 1 ? 0 : (int)(NextUInt64() % (uint)maximum);
    }

    public float NextFloat()
    {
        return (NextUInt64() >> 40) * (1f / 16777216f);
    }

    public float Range(float minimum, float maximum)
    {
        return Mathf.Lerp(minimum, maximum, NextFloat());
    }

    ulong NextUInt64()
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
