using System;
using System.Collections.Generic;
using UnityEngine;

public enum ProceduralPlantFamily
{
    AlienBroadleaf,
    CanopyTree,
    CoralSucculent
}

public enum PlantLeafMode
{
    SolidLeaf,
    CardLeaf,
    None
}

public enum PlantPaletteMode
{
    Natural,
    Alien,
    Custom
}

public enum PlantMeshQuality
{
    Lod0,
    Lod1,
    Lod2
}

[Serializable]
public sealed class PlantGenerationParameters
{
    [Range(0f, 1f)] public float age = 1f;
    [Min(0.2f)] public float trunkLength = 5.5f;
    [Min(0.02f)] public float trunkRadius = 0.32f;
    [Range(0.35f, 0.95f)] public float taper = 0.72f;
    [Range(0f, 1f)] public float curvature = 0.18f;
    [Range(3, 16)] public int trunkSegments = 8;
    [Range(0, 5)] public int branchDepth = 3;
    [Range(1, 6)] public int budsPerBranch = 3;
    [Range(5f, 80f)] public float branchAngle = 38f;
    [Range(0.35f, 0.9f)] public float lengthDecay = 0.66f;
    [Range(0.25f, 0.85f)] public float radiusDecay = 0.58f;
    [Range(0f, 1f)] public float phototropism = 0.55f;
    [Range(0f, 1f)] public float apicalDominance = 0.62f;
    [Range(0.2f, 2f)] public float crownWidth = 1f;
    [Range(0.3f, 1.8f)] public float crownHeight = 1f;
    public PlantLeafMode leafMode = PlantLeafMode.SolidLeaf;
    [Range(0f, 3f)] public float leafDensity = 1f;
    [Min(0.03f)] public float leafSize = 0.55f;
    [Range(0f, 1f)] public float leafUpwardBias = 0.35f;

    public PlantGenerationParameters Clone()
    {
        return (PlantGenerationParameters)MemberwiseClone();
    }

    public void Clamp()
    {
        age = Mathf.Clamp01(age);
        trunkLength = Mathf.Max(0.2f, trunkLength);
        trunkRadius = Mathf.Max(0.02f, trunkRadius);
        taper = Mathf.Clamp(taper, 0.35f, 0.95f);
        curvature = Mathf.Clamp01(curvature);
        trunkSegments = Mathf.Clamp(trunkSegments, 3, 16);
        branchDepth = Mathf.Clamp(branchDepth, 0, 5);
        budsPerBranch = Mathf.Clamp(budsPerBranch, 1, 6);
        branchAngle = Mathf.Clamp(branchAngle, 5f, 80f);
        lengthDecay = Mathf.Clamp(lengthDecay, 0.35f, 0.9f);
        radiusDecay = Mathf.Clamp(radiusDecay, 0.25f, 0.85f);
        phototropism = Mathf.Clamp01(phototropism);
        apicalDominance = Mathf.Clamp01(apicalDominance);
        crownWidth = Mathf.Clamp(crownWidth, 0.2f, 2f);
        crownHeight = Mathf.Clamp(crownHeight, 0.3f, 1.8f);
        leafDensity = Mathf.Clamp(leafDensity, 0f, 3f);
        leafSize = Mathf.Max(0.03f, leafSize);
        leafUpwardBias = Mathf.Clamp01(leafUpwardBias);
    }

    public int StableHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + age.GetHashCode();
            hash = hash * 31 + trunkLength.GetHashCode();
            hash = hash * 31 + trunkRadius.GetHashCode();
            hash = hash * 31 + taper.GetHashCode();
            hash = hash * 31 + curvature.GetHashCode();
            hash = hash * 31 + trunkSegments;
            hash = hash * 31 + branchDepth;
            hash = hash * 31 + budsPerBranch;
            hash = hash * 31 + branchAngle.GetHashCode();
            hash = hash * 31 + lengthDecay.GetHashCode();
            hash = hash * 31 + radiusDecay.GetHashCode();
            hash = hash * 31 + phototropism.GetHashCode();
            hash = hash * 31 + apicalDominance.GetHashCode();
            hash = hash * 31 + crownWidth.GetHashCode();
            hash = hash * 31 + crownHeight.GetHashCode();
            hash = hash * 31 + (int)leafMode;
            hash = hash * 31 + leafDensity.GetHashCode();
            hash = hash * 31 + leafSize.GetHashCode();
            hash = hash * 31 + leafUpwardBias.GetHashCode();
            return hash;
        }
    }
}

[CreateAssetMenu(menuName = "Procedural Plants/Species", fileName = "ProceduralPlantSpecies")]
public sealed class ProceduralPlantSpecies : ScriptableObject
{
    public const int CurrentSchema = 1;

    public string speciesId = "plant";
    public int schemaVersion = CurrentSchema;
    public int baseSeed = 1000;
    public ProceduralPlantFamily family;
    public PlantGenerationParameters parameters = new PlantGenerationParameters();

    [Header("Natural palette")]
    public Color naturalBark = new Color(0.22f, 0.12f, 0.06f);
    public Color naturalLeaf = new Color(0.16f, 0.48f, 0.18f);
    public Color naturalAccent = new Color(0.48f, 0.72f, 0.22f);

    [Header("Alien palette")]
    public Color alienBark = new Color(0.08f, 0.24f, 0.3f);
    public Color alienLeaf = new Color(0.06f, 0.78f, 0.72f);
    public Color alienAccent = new Color(0.72f, 0.18f, 0.95f);

    [Header("Runtime")]
    [Range(1, 24)] public int variantPoolSize = 12;
    public Material barkMaterial;
    public Material foliageMaterial;

    public PlantSpeciesSnapshot CreateSnapshot(
        int seed,
        PlantGenerationParameters parameterOverride = null,
        PlantPaletteMode palette = PlantPaletteMode.Natural,
        Color? customBark = null,
        Color? customLeaf = null,
        Color? customAccent = null)
    {
        PlantGenerationParameters value = (parameterOverride ?? parameters ?? new PlantGenerationParameters()).Clone();
        value.Clamp();
        Color bark = palette == PlantPaletteMode.Alien ? alienBark : naturalBark;
        Color leaf = palette == PlantPaletteMode.Alien ? alienLeaf : naturalLeaf;
        Color accent = palette == PlantPaletteMode.Alien ? alienAccent : naturalAccent;
        if (palette == PlantPaletteMode.Custom)
        {
            bark = customBark ?? naturalBark;
            leaf = customLeaf ?? naturalLeaf;
            accent = customAccent ?? naturalAccent;
        }
        return new PlantSpeciesSnapshot(
            speciesId,
            schemaVersion,
            family,
            unchecked(baseSeed ^ seed),
            value,
            bark,
            leaf,
            accent);
    }

    public int StableRecipeHash()
    {
        unchecked
        {
            int hash = PlanetDecorationCatalog.StableHash(speciesId ?? string.Empty);
            hash = hash * 31 + schemaVersion;
            hash = hash * 31 + baseSeed;
            hash = hash * 31 + (int)family;
            hash = hash * 31 + (parameters != null ? parameters.StableHash() : 0);
            hash = hash * 31 + variantPoolSize;
            hash = hash * 31 + naturalBark.GetHashCode();
            hash = hash * 31 + naturalLeaf.GetHashCode();
            hash = hash * 31 + alienBark.GetHashCode();
            hash = hash * 31 + alienLeaf.GetHashCode();
            return hash;
        }
    }
}

public sealed class PlantSpeciesSnapshot
{
    public readonly string SpeciesId;
    public readonly int SchemaVersion;
    public readonly ProceduralPlantFamily Family;
    public readonly int Seed;
    public readonly PlantGenerationParameters Parameters;
    public readonly Color BarkColor;
    public readonly Color LeafColor;
    public readonly Color AccentColor;

    public PlantSpeciesSnapshot(
        string speciesId,
        int schemaVersion,
        ProceduralPlantFamily family,
        int seed,
        PlantGenerationParameters parameters,
        Color barkColor,
        Color leafColor,
        Color accentColor)
    {
        SpeciesId = speciesId ?? string.Empty;
        SchemaVersion = schemaVersion;
        Family = family;
        Seed = seed;
        Parameters = parameters.Clone();
        BarkColor = barkColor;
        LeafColor = leafColor;
        AccentColor = accentColor;
    }
}

public sealed class PlantBranch
{
    public readonly int ParentIndex;
    public readonly int Depth;
    public readonly float StartRadius;
    public readonly float EndRadius;
    public readonly Vector3[] Points;

    public PlantBranch(int parentIndex, int depth, float startRadius, float endRadius, Vector3[] points)
    {
        ParentIndex = parentIndex;
        Depth = depth;
        StartRadius = startRadius;
        EndRadius = endRadius;
        Points = points;
    }
}

public struct PlantLeafAnchor
{
    public Vector3 position;
    public Vector3 direction;
    public Vector3 normal;
    public float scale;
    public int branchIndex;
}

public sealed class PlantSkeleton
{
    public readonly List<PlantBranch> Branches = new List<PlantBranch>();
    public readonly List<PlantLeafAnchor> Leaves = new List<PlantLeafAnchor>();
    public Bounds Bounds;

    public int StableHash()
    {
        unchecked
        {
            int hash = 19;
            foreach (PlantBranch branch in Branches)
            {
                hash = hash * 31 + branch.ParentIndex;
                hash = hash * 31 + branch.Depth;
                hash = hash * 31 + branch.StartRadius.GetHashCode();
                hash = hash * 31 + branch.EndRadius.GetHashCode();
                foreach (Vector3 point in branch.Points)
                    hash = hash * 31 + point.GetHashCode();
            }
            foreach (PlantLeafAnchor leaf in Leaves)
            {
                hash = hash * 31 + leaf.position.GetHashCode();
                hash = hash * 31 + leaf.direction.GetHashCode();
                hash = hash * 31 + leaf.scale.GetHashCode();
            }
            return hash;
        }
    }
}

public sealed class ProceduralPlantMeshData
{
    public Vector3[] vertices = Array.Empty<Vector3>();
    public Vector3[] normals = Array.Empty<Vector3>();
    public Vector2[] uv = Array.Empty<Vector2>();
    public Color[] colors = Array.Empty<Color>();
    public int[] barkTriangles = Array.Empty<int>();
    public int[] foliageTriangles = Array.Empty<int>();
    public Bounds bounds;
    public int sourceHash;

    public int VertexCount => vertices != null ? vertices.Length : 0;

    public bool IsValid(out string error)
    {
        error = null;
        if (vertices == null || vertices.Length == 0)
        {
            error = "Plant mesh has no vertices.";
            return false;
        }
        if (normals == null || normals.Length != vertices.Length
            || uv == null || uv.Length != vertices.Length
            || colors == null || colors.Length != vertices.Length)
        {
            error = "Plant mesh vertex channels have inconsistent lengths.";
            return false;
        }
        if (!ValidateTriangles(barkTriangles, vertices.Length)
            || !ValidateTriangles(foliageTriangles, vertices.Length))
        {
            error = "Plant mesh contains invalid triangle indices.";
            return false;
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            Vector3 normal = normals[i];
            if (!IsFinite(vertex) || !IsFinite(normal))
            {
                error = "Plant mesh contains NaN or infinite data.";
                return false;
            }
        }
        return true;
    }

    static bool ValidateTriangles(int[] triangles, int vertexCount)
    {
        if (triangles == null || triangles.Length % 3 != 0)
            return false;
        for (int i = 0; i < triangles.Length; i++)
            if (triangles[i] < 0 || triangles[i] >= vertexCount)
                return false;
        return true;
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
