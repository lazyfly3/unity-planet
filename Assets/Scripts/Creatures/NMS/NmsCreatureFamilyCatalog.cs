using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class NmsCreatureModuleBinding
{
    public string moduleId;
    public string[] rendererPaths = Array.Empty<string>();
}

[Serializable]
public sealed class NmsCreatureLegChain
{
    public string id;
    public string[] bones = Array.Empty<string>();
}

[Serializable]
public sealed class NmsDescriptorCandidateData
{
    public string id;
    public string name;
    public float chance;
    public string[] path = Array.Empty<string>();
    public string[] moduleIds = Array.Empty<string>();
    public NmsDescriptorGroupData[] children = Array.Empty<NmsDescriptorGroupData>();
}

[Serializable]
public sealed class NmsDescriptorGroupData
{
    public string typeId;
    public string[] path = Array.Empty<string>();
    public NmsDescriptorCandidateData[] candidates = Array.Empty<NmsDescriptorCandidateData>();
}

[Serializable]
public sealed class NmsCreatureModuleData
{
    public string name;
    public string[] objects = Array.Empty<string>();
    public string[] materials = Array.Empty<string>();
    public bool compatible;
}

[Serializable]
public sealed class NmsCreatureFamilyManifestData
{
    public int pipelineVersion;
    public string familyId;
    public string skeletonHash;
    public string locomotionType;
    public int legCount;
    public NmsDescriptorGroupData[] descriptorGroups = Array.Empty<NmsDescriptorGroupData>();
    public NmsCreatureModuleData[] modules = Array.Empty<NmsCreatureModuleData>();
    public NmsDescriptorChoiceData[] baselineChoices = Array.Empty<NmsDescriptorChoiceData>();
}

[Serializable]
public sealed class NmsDescriptorChoiceData
{
    public string typeId;
    public string id;
    public string name;
    public string[] path = Array.Empty<string>();
}

[Serializable]
public sealed class NmsCreatureSpeciesDefinition
{
    public int seed;
    public string familyId;
    public string[] selectedModules = Array.Empty<string>();
    public Color primaryColor;
    public Color secondaryColor;
    public Color accentColor;
    public string signature;
}

[CreateAssetMenu(menuName = "Creatures/NMS Family Catalog", fileName = "NmsCreatureFamilyCatalog")]
public sealed class NmsCreatureFamilyCatalog : ScriptableObject
{
    [SerializeField] NmsCreatureFamilyDefinition[] families = Array.Empty<NmsCreatureFamilyDefinition>();
    [SerializeField] int revision;

    public IReadOnlyList<NmsCreatureFamilyDefinition> Families => families;
    public int Revision => revision;

    public bool TryGetFamily(string familyId, out NmsCreatureFamilyDefinition family)
    {
        for (int i = 0; i < families.Length; i++)
        {
            NmsCreatureFamilyDefinition candidate = families[i];
            if (candidate == null
                || !string.Equals(candidate.FamilyId, familyId, StringComparison.Ordinal))
                continue;
            family = candidate;
            return true;
        }
        family = null;
        return false;
    }

#if UNITY_EDITOR
    public void ConfigureImported(NmsCreatureFamilyDefinition[] importedFamilies)
    {
        families = importedFamilies ?? Array.Empty<NmsCreatureFamilyDefinition>();
        revision++;
    }
#endif
}
