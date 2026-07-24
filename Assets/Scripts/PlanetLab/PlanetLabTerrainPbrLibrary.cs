using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum PlanetLabTerrainPbrRole
{
    Ground = 1 << 0,
    Rock = 1 << 1,
    Shore = 1 << 2,
    Cold = 1 << 3
}

[Serializable]
public sealed class PlanetLabTerrainPbrEntry
{
    public string id;
    public int slice;
    public PlanetLabTerrainPbrRole roles;
}

[CreateAssetMenu(
    fileName = "PlanetLabTerrainPbrLibrary",
    menuName = "Voxel Planet/Planet Lab Terrain PBR Library")]
public sealed class PlanetLabTerrainPbrLibrary : ScriptableObject
{
    public const int ExpectedMaterialCount = 40;

    public Texture2DArray albedoArray;
    public Texture2DArray normalArray;
    public Texture2DArray maskArray;
    public List<PlanetLabTerrainPbrEntry> entries =
        new List<PlanetLabTerrainPbrEntry>();

    public bool IsReady
        => albedoArray != null
        && normalArray != null
        && maskArray != null
        && entries != null
        && entries.Count == ExpectedMaterialCount;

    public int SelectSlice(int seed, PlanetLabTerrainPbrRole role, int salt)
        => SelectSlice(seed, role, salt, null);

    public int SelectSlice(
        int seed,
        PlanetLabTerrainPbrRole role,
        int salt,
        string[] preferredIds)
    {
        if (entries == null || entries.Count == 0)
            return 0;

        bool usePreferred = preferredIds != null && preferredIds.Length > 0;
        int candidateCount = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            PlanetLabTerrainPbrEntry entry = entries[index];
            if (IsCandidate(entry, role, preferredIds, usePreferred))
                candidateCount++;
        }

        if (candidateCount == 0 && usePreferred)
            return SelectSlice(seed, role, salt, null);
        if (candidateCount == 0)
            return Mathf.Abs(StableHash(seed, salt)) % entries.Count;

        int selected = Mathf.Abs(StableHash(seed, salt)) % candidateCount;
        for (int index = 0; index < entries.Count; index++)
        {
            PlanetLabTerrainPbrEntry entry = entries[index];
            if (!IsCandidate(entry, role, preferredIds, usePreferred))
                continue;
            if (selected-- == 0)
                return Mathf.Clamp(entry.slice, 0, entries.Count - 1);
        }

        return 0;
    }

    static bool IsCandidate(
        PlanetLabTerrainPbrEntry entry,
        PlanetLabTerrainPbrRole role,
        string[] preferredIds,
        bool usePreferred)
    {
        if (entry == null || (entry.roles & role) == 0)
            return false;
        if (!usePreferred)
            return true;
        for (int index = 0; index < preferredIds.Length; index++)
        {
            if (string.Equals(
                    entry.id,
                    preferredIds[index],
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
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
