using System;
using System.Collections.Generic;
using UnityEngine;

public enum NmsAntelopeEditablePart
{
    Body,
    Accessory,
    Ears,
    Horns,
    Tail
}

[Serializable]
public sealed class NmsAntelopeVariantParameters
{
    public int seed;
    public Vector3 visualScale = Vector3.one;
    public string bodyModule = "_Body_Deer";
    public string accessoryModule = string.Empty;
    public string headModule = "_Head_Deer";
    public string eyesModule = "DeerEyes";
    public string earsModule = "_HDEars_1";
    public string hornsModule = string.Empty;
    public string tailModule = "_Tail_Alien0";
    public Color primaryColor = Color.white;
    public Color secondaryColor = Color.gray;
    public Color accentColor = Color.black;

    public NmsAntelopeVariantParameters Clone()
    {
        return (NmsAntelopeVariantParameters)MemberwiseClone();
    }

    public void ClampScale()
    {
        visualScale = new Vector3(
            Mathf.Clamp(visualScale.x, 0.82f, 1.22f),
            Mathf.Clamp(visualScale.y, 0.72f, 1.30f),
            Mathf.Clamp(visualScale.z, 0.96f, 1.04f));
    }

    public string GetModule(NmsAntelopeEditablePart part)
    {
        switch (part)
        {
            case NmsAntelopeEditablePart.Body: return bodyModule;
            case NmsAntelopeEditablePart.Accessory: return accessoryModule;
            case NmsAntelopeEditablePart.Ears: return earsModule;
            case NmsAntelopeEditablePart.Horns: return hornsModule;
            case NmsAntelopeEditablePart.Tail: return tailModule;
            default: return string.Empty;
        }
    }

    public void SetModule(NmsAntelopeEditablePart part, string module)
    {
        string value = module ?? string.Empty;
        switch (part)
        {
            case NmsAntelopeEditablePart.Body:
                bodyModule = value;
                break;
            case NmsAntelopeEditablePart.Accessory:
                accessoryModule = value;
                break;
            case NmsAntelopeEditablePart.Ears:
                earsModule = value;
                break;
            case NmsAntelopeEditablePart.Horns:
                hornsModule = value;
                break;
            case NmsAntelopeEditablePart.Tail:
                tailModule = value;
                break;
        }
    }

    public string[] BuildModuleIds()
    {
        var modules = new List<string>(7);
        AddModule(modules, bodyModule);
        AddModule(modules, accessoryModule);
        AddModule(modules, headModule);
        AddModule(modules, eyesModule);
        AddModule(modules, earsModule);
        AddModule(modules, hornsModule);
        AddModule(modules, tailModule);
        return modules.ToArray();
    }

    public bool HasSameModules(NmsAntelopeVariantParameters other)
    {
        return other != null
            && string.Equals(bodyModule, other.bodyModule, StringComparison.Ordinal)
            && string.Equals(accessoryModule, other.accessoryModule, StringComparison.Ordinal)
            && string.Equals(headModule, other.headModule, StringComparison.Ordinal)
            && string.Equals(eyesModule, other.eyesModule, StringComparison.Ordinal)
            && string.Equals(earsModule, other.earsModule, StringComparison.Ordinal)
            && string.Equals(hornsModule, other.hornsModule, StringComparison.Ordinal)
            && string.Equals(tailModule, other.tailModule, StringComparison.Ordinal);
    }

    public static NmsAntelopeVariantParameters FromDescriptor(
        NmsAntelopeVariantDescriptor descriptor)
    {
        NmsCreatureSpeciesDefinition species = descriptor.species;
        var result = new NmsAntelopeVariantParameters
        {
            seed = descriptor.seed,
            visualScale = descriptor.visualScale,
            primaryColor = species != null ? species.primaryColor : Color.white,
            secondaryColor = species != null ? species.secondaryColor : Color.gray,
            accentColor = species != null ? species.accentColor : Color.black,
            accessoryModule = string.Empty,
            earsModule = string.Empty,
            hornsModule = string.Empty,
            tailModule = string.Empty
        };
        if (species == null || species.selectedModules == null)
            return result;
        for (int i = 0; i < species.selectedModules.Length; i++)
        {
            string module = species.selectedModules[i];
            if (string.IsNullOrEmpty(module))
                continue;
            if (module.StartsWith("_Body_", StringComparison.Ordinal))
                result.bodyModule = module;
            else if (module.IndexOf("Acc_", StringComparison.Ordinal) >= 0)
                result.accessoryModule = module;
            else if (module.StartsWith("_Head_", StringComparison.Ordinal))
                result.headModule = module;
            else if (module.EndsWith("Eyes", StringComparison.Ordinal))
                result.eyesModule = module;
            else if (module.StartsWith("_HDEars_", StringComparison.Ordinal))
                result.earsModule = module;
            else if (module.StartsWith("_HDHorns_", StringComparison.Ordinal))
                result.hornsModule = module;
            else if (module.StartsWith("_Tail_", StringComparison.Ordinal))
                result.tailModule = module;
        }
        return result;
    }

    static void AddModule(List<string> modules, string module)
    {
        if (!string.IsNullOrEmpty(module))
            modules.Add(module);
    }
}

public sealed class NmsAntelopeEditingOptions
{
    readonly Dictionary<string, string[]> accessoriesByBody =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    public string[] Bodies { get; private set; } = Array.Empty<string>();
    public string[] Ears { get; private set; } = Array.Empty<string>();
    public string[] Horns { get; private set; } = Array.Empty<string>();
    public string[] Tails { get; private set; } = Array.Empty<string>();
    public string Head { get; private set; } = "_Head_Deer";
    public string Eyes { get; private set; } = "DeerEyes";

    public IReadOnlyList<string> GetOptions(
        NmsAntelopeEditablePart part, string bodyModule)
    {
        switch (part)
        {
            case NmsAntelopeEditablePart.Body: return Bodies;
            case NmsAntelopeEditablePart.Accessory:
                return accessoriesByBody.TryGetValue(
                    bodyModule ?? string.Empty, out string[] accessories)
                    ? accessories : Array.Empty<string>();
            case NmsAntelopeEditablePart.Ears: return Ears;
            case NmsAntelopeEditablePart.Horns: return Horns;
            case NmsAntelopeEditablePart.Tail: return Tails;
            default: return Array.Empty<string>();
        }
    }

    public void Constrain(NmsAntelopeVariantParameters parameters)
    {
        if (parameters == null)
            return;
        parameters.ClampScale();
        parameters.bodyModule = ConstrainValue(parameters.bodyModule, Bodies, false);
        parameters.headModule = Head;
        parameters.eyesModule = Eyes;
        parameters.accessoryModule = ConstrainValue(
            parameters.accessoryModule,
            GetOptions(NmsAntelopeEditablePart.Accessory, parameters.bodyModule), true);
        parameters.earsModule = ConstrainValue(parameters.earsModule, Ears, true);
        parameters.hornsModule = ConstrainValue(parameters.hornsModule, Horns, true);
        parameters.tailModule = ConstrainValue(parameters.tailModule, Tails, false);
    }

    public static bool TryCreate(
        NmsCreatureFamilyDefinition family,
        out NmsAntelopeEditingOptions options,
        out string error)
    {
        options = null;
        error = null;
        if (family == null || !family.TryGetManifest(out NmsCreatureFamilyManifestData manifest))
        {
            error = "The antelope family manifest is unavailable.";
            return false;
        }
        var result = new NmsAntelopeEditingOptions();
        for (int groupIndex = 0; groupIndex < manifest.descriptorGroups.Length; groupIndex++)
        {
            NmsDescriptorGroupData group = manifest.descriptorGroups[groupIndex];
            if (group == null)
                continue;
            if (group.typeId == "_BODY_")
            {
                result.Bodies = CandidateModules(group, false);
                for (int candidateIndex = 0; candidateIndex < group.candidates.Length; candidateIndex++)
                {
                    NmsDescriptorCandidateData body = group.candidates[candidateIndex];
                    string bodyModule = CandidateModule(body);
                    if (string.IsNullOrEmpty(bodyModule) || body.children == null)
                        continue;
                    for (int childIndex = 0; childIndex < body.children.Length; childIndex++)
                        if (body.children[childIndex] != null
                            && body.children[childIndex].typeId == "_ACCESSORY_")
                            result.accessoriesByBody[bodyModule] =
                                CandidateModules(body.children[childIndex], true);
                }
            }
            else if (group.typeId == "_HEAD_")
                result.Head = FirstCandidateModule(group, result.Head);
            else if (group.typeId == "_EYES_")
                result.Eyes = FirstCandidateModule(group, result.Eyes);
            else if (group.typeId == "_EARS_")
                result.Ears = CandidateModules(group, true);
            else if (group.typeId == "_HORNS_")
                result.Horns = CandidateModules(group, true);
            else if (group.typeId == "_TAIL_")
                result.Tails = CandidateModules(group, false);
        }
        if (result.Bodies.Length == 0 || result.Tails.Length == 0)
        {
            error = "The antelope manifest has no safe body or tail options.";
            return false;
        }
        options = result;
        return true;
    }

    public static string DisplayName(string module)
    {
        if (string.IsNullOrEmpty(module))
            return "无";
        if (module == "_Body_Deer") return "标准身体";
        if (module == "_Body_Fat") return "厚重身体";
        if (module.StartsWith("_DeerAcc_", StringComparison.Ordinal))
            return "标准装饰 " + module.Substring("_DeerAcc_".Length);
        if (module.StartsWith("_FatAcc_", StringComparison.Ordinal))
            return "厚重装饰 " + module.Substring("_FatAcc_".Length);
        if (module.StartsWith("_HDEars_", StringComparison.Ordinal))
            return "耳型 " + module.Substring("_HDEars_".Length);
        if (module.StartsWith("_HDHorns_", StringComparison.Ordinal))
            return "角型 " + module.Substring("_HDHorns_".Length);
        if (module.StartsWith("_Tail_Alien", StringComparison.Ordinal))
            return "尾型 " + module.Substring("_Tail_Alien".Length);
        return module.TrimStart('_').Replace('_', ' ');
    }

    static string[] CandidateModules(NmsDescriptorGroupData group, bool includeNone)
    {
        var result = new List<string>();
        if (includeNone)
            result.Add(string.Empty);
        if (group?.candidates == null)
            return result.ToArray();
        for (int i = 0; i < group.candidates.Length; i++)
        {
            string module = CandidateModule(group.candidates[i]);
            if (!string.IsNullOrEmpty(module) && !result.Contains(module))
                result.Add(module);
        }
        return result.ToArray();
    }

    static string CandidateModule(NmsDescriptorCandidateData candidate)
    {
        if (candidate == null || candidate.moduleIds == null)
            return string.Empty;
        for (int i = 0; i < candidate.moduleIds.Length; i++)
            if (!string.IsNullOrEmpty(candidate.moduleIds[i]))
                return candidate.moduleIds[i];
        return string.Empty;
    }

    static string FirstCandidateModule(
        NmsDescriptorGroupData group, string fallback)
    {
        if (group?.candidates == null)
            return fallback;
        for (int i = 0; i < group.candidates.Length; i++)
        {
            string module = CandidateModule(group.candidates[i]);
            if (!string.IsNullOrEmpty(module))
                return module;
        }
        return fallback;
    }

    static string ConstrainValue(
        string value, IReadOnlyList<string> options, bool allowNone)
    {
        if (options != null)
            for (int i = 0; i < options.Count; i++)
                if (string.Equals(options[i], value, StringComparison.Ordinal))
                    return value ?? string.Empty;
        if (allowNone)
            return string.Empty;
        return options != null && options.Count > 0 ? options[0] : string.Empty;
    }
}
