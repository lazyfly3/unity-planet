using System;
using UnityEngine;

public static class ProceduralPlantMeshGenerator
{
    public static ProceduralPlantMeshData Build(PlantSpeciesSnapshot species, PlantMeshQuality quality)
    {
        if (species == null)
            throw new ArgumentNullException(nameof(species));

        PlantSkeleton skeleton = ProceduralPlantSkeletonBuilder.Build(species);
        GetQualitySettings(quality, out int sides, out int depthLimit, out int leafStride);
        var builder = new ProceduralTubeMeshBuilder();
        for (int i = 0; i < skeleton.Branches.Count; i++)
        {
            PlantBranch branch = skeleton.Branches[i];
            if (branch.Depth > depthLimit)
                continue;
            builder.AddBranch(branch, sides, species.BarkColor);
        }

        PlantLeafMode leafMode = species.Parameters.leafMode;
        if (quality == PlantMeshQuality.Lod2 && leafMode == PlantLeafMode.SolidLeaf)
            leafMode = PlantLeafMode.CardLeaf;
        for (int i = 0; i < skeleton.Leaves.Count; i += leafStride)
        {
            PlantLeafAnchor leaf = skeleton.Leaves[i];
            if (leafMode == PlantLeafMode.SolidLeaf)
                builder.AddSolidLeaf(leaf, species.LeafColor, species.AccentColor);
            else if (leafMode == PlantLeafMode.CardLeaf)
                builder.AddCardLeaf(leaf, species.LeafColor, species.AccentColor, true);
        }

        int sourceHash = CalculateSourceHash(species, quality, skeleton.StableHash());
        ProceduralPlantMeshData result = builder.ToMeshData(sourceHash);
        if (!result.IsValid(out string error))
            throw new InvalidOperationException(error);
        return result;
    }

    public static int CalculateSourceHash(
        PlantSpeciesSnapshot species,
        PlantMeshQuality quality,
        int skeletonHash)
    {
        unchecked
        {
            int hash = PlanetDecorationCatalog.StableHash(species.SpeciesId);
            hash = hash * 31 + species.SchemaVersion;
            hash = hash * 31 + species.Seed;
            hash = hash * 31 + species.Parameters.StableHash();
            hash = hash * 31 + (int)quality;
            hash = hash * 31 + skeletonHash;
            return hash;
        }
    }

    static void GetQualitySettings(
        PlantMeshQuality quality,
        out int sides,
        out int depthLimit,
        out int leafStride)
    {
        switch (quality)
        {
            case PlantMeshQuality.Lod0:
                sides = 8;
                depthLimit = 5;
                leafStride = 1;
                break;
            case PlantMeshQuality.Lod1:
                sides = 5;
                depthLimit = 3;
                leafStride = 2;
                break;
            default:
                sides = 4;
                depthLimit = 2;
                leafStride = 6;
                break;
        }
    }
}
