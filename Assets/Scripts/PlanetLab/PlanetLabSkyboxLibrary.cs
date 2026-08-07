using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PlanetLabSkyboxEntry
{
    public string id;
    public ProceduralPlanetLabTemplate template;
    public Material material;
}

[CreateAssetMenu(
    fileName = "PlanetLabSkyboxLibrary",
    menuName = "Voxel Planet/Planet Lab Skybox Library")]
public sealed class PlanetLabSkyboxLibrary : ScriptableObject
{
    public const int CandidatesPerTemplate = 5;
    public const int ExpectedEntryCount = 25;

    public List<PlanetLabSkyboxEntry> entries =
        new List<PlanetLabSkyboxEntry>();

    public bool IsReady
    {
        get
        {
            if (entries == null || entries.Count != ExpectedEntryCount)
                return false;

            foreach (ProceduralPlanetLabTemplate template
                     in Enum.GetValues(typeof(ProceduralPlanetLabTemplate)))
            {
                int count = 0;
                for (int index = 0; index < entries.Count; index++)
                {
                    PlanetLabSkyboxEntry entry = entries[index];
                    if (entry != null
                        && entry.material != null
                        && entry.template == template)
                    {
                        count++;
                    }
                }
                if (count != CandidatesPerTemplate)
                    return false;
            }
            return true;
        }
    }

    public PlanetLabSkyboxEntry SelectEntry(
        int seed,
        ProceduralPlanetLabTemplate template)
    {
        if (entries == null || entries.Count == 0)
            return null;

        int count = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            PlanetLabSkyboxEntry entry = entries[index];
            if (entry != null
                && entry.material != null
                && entry.template == template)
            {
                count++;
            }
        }
        if (count == 0)
            return null;

        int selected = StableHash(seed, 0x534B5942 + (int)template * 977) % count;
        for (int index = 0; index < entries.Count; index++)
        {
            PlanetLabSkyboxEntry entry = entries[index];
            if (entry == null
                || entry.material == null
                || entry.template != template)
            {
                continue;
            }
            if (selected-- == 0)
                return entry;
        }
        return null;
    }

    public Material SelectMaterial(
        int seed,
        ProceduralPlanetLabTemplate template)
        => SelectEntry(seed, template)?.material;

    public PlanetLabSkyboxEntry SelectClimateEntry(PlanetClimate climate)
    {
        string selectedId = GetClimateSkyboxId(climate);
        if (entries == null || string.IsNullOrEmpty(selectedId))
            return null;

        for (int index = 0; index < entries.Count; index++)
        {
            PlanetLabSkyboxEntry entry = entries[index];
            if (entry != null
                && entry.material != null
                && string.Equals(
                    entry.id,
                    selectedId,
                    StringComparison.Ordinal))
            {
                return entry;
            }
        }
        return null;
    }

    public Material SelectClimateMaterial(PlanetClimate climate)
        => SelectClimateEntry(climate)?.material;

    public static string GetClimateSkyboxId(PlanetClimate climate)
    {
        return climate switch
        {
            PlanetClimate.Barren =>
                "Desert/Day Sun Low ClearHazy",
            PlanetClimate.TemperateForest =>
                "TemperateOcean/Day Sun High SummerSky",
            PlanetClimate.Desert =>
                "Desert/Golden Sunset",
            PlanetClimate.Tropical =>
                "TemperateOcean/Sunless_BlueSky_02",
            PlanetClimate.Tundra =>
                "Frozen/Cold Clouds",
            PlanetClimate.Volcanic =>
                "CrimsonOcean/FantasySky_Fire",
            PlanetClimate.Crystal =>
                "Crystal/Space_Nebula_BlueRed",
            _ => string.Empty
        };
    }

    static int StableHash(int seed, int salt)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)salt * 0x9E3779B9u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (int)(value & 0x7FFFFFFF);
        }
    }
}
