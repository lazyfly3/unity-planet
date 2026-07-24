using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ProceduralPlantFactory : IDisposable
{
    sealed class Variant
    {
        public readonly Mesh[] meshes = new Mesh[3];
        public Color barkColor;
        public Color leafColor;
        public Color accentColor;
    }

    sealed class Pool
    {
        public string catalogId;
        public ProceduralPlantSpecies species;
        public Material barkMaterial;
        public Material foliageMaterial;
        public Variant[] variants;
        public int recipeHash;
    }

    readonly Dictionary<string, Pool> pools = new Dictionary<string, Pool>(StringComparer.Ordinal);
    readonly List<Material> ownedMaterials = new List<Material>();
    bool disposed;

    public int PoolCount => pools.Count;

    public void RegisterPool(
        ProceduralPlantSpecies species,
        int worldSeed,
        string catalogId,
        int poolSize,
        PlantPaletteMode palette = PlantPaletteMode.Natural,
        PlantGenerationParameters parameterOverride = null,
        Color? customBark = null,
        Color? customLeaf = null,
        Color? customAccent = null,
        int? fixedSeed = null)
    {
        if (species == null)
            throw new ArgumentNullException(nameof(species));
        if (string.IsNullOrWhiteSpace(catalogId))
            throw new ArgumentException("Catalog ID is required.", nameof(catalogId));
        if (disposed)
            throw new ObjectDisposedException(nameof(ProceduralPlantFactory));

        poolSize = Mathf.Clamp(poolSize, 1, 24);
        var pool = new Pool
        {
            catalogId = catalogId,
            species = species,
            barkMaterial = ResolveMaterial(species.barkMaterial, "Plant/ProceduralBark"),
            foliageMaterial = ResolveMaterial(species.foliageMaterial, "Plant/ProceduralFoliage"),
            variants = new Variant[poolSize],
            recipeHash = species.StableRecipeHash()
        };

        for (int i = 0; i < poolSize; i++)
        {
            int seed = fixedSeed.HasValue
                ? unchecked(fixedSeed.Value + i * 7919)
                : StableMix(worldSeed, species.baseSeed, PlanetDecorationCatalog.StableHash(catalogId), i);
            PlantSpeciesSnapshot snapshot = species.CreateSnapshot(
                seed,
                parameterOverride,
                palette,
                customBark,
                customLeaf,
                customAccent);
            var variant = new Variant
            {
                barkColor = snapshot.BarkColor,
                leafColor = snapshot.LeafColor,
                accentColor = snapshot.AccentColor
            };
            variant.meshes[0] = CreateMesh(
                ProceduralPlantMeshGenerator.Build(snapshot, PlantMeshQuality.Lod0),
                catalogId + "_V" + i + "_LOD0");
            variant.meshes[1] = CreateMesh(
                ProceduralPlantMeshGenerator.Build(snapshot, PlantMeshQuality.Lod1),
                catalogId + "_V" + i + "_LOD1");
            variant.meshes[2] = CreateMesh(
                ProceduralPlantMeshGenerator.Build(snapshot, PlantMeshQuality.Lod2),
                catalogId + "_V" + i + "_LOD2");
            pool.variants[i] = variant;
        }

        if (pools.TryGetValue(catalogId, out Pool old))
            DestroyPool(old);
        pools[catalogId] = pool;
    }

    public GameObject CreateInstance(
        string catalogId,
        int worldSeed,
        string instanceId,
        Transform parent)
    {
        if (!pools.TryGetValue(catalogId, out Pool pool))
            throw new InvalidOperationException("No procedural plant pool registered for " + catalogId + ".");
        int variantIndex = GetVariantIndex(
            pool.species,
            worldSeed,
            catalogId,
            instanceId,
            pool.variants.Length);
        Variant variant = pool.variants[variantIndex];
        var root = new GameObject(catalogId + "_" + instanceId + "_V" + variantIndex);
        root.transform.SetParent(parent, false);
        var lodGroup = root.AddComponent<LODGroup>();
        var lods = new LOD[3];
        float[] transitions = { 0.55f, 0.18f, 0.035f };
        for (int i = 0; i < 3; i++)
        {
            var child = new GameObject("LOD" + i);
            child.transform.SetParent(root.transform, false);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = variant.meshes[i];
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { pool.barkMaterial, pool.foliageMaterial };
            renderer.shadowCastingMode = i == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = i != 2;
            ApplyProperties(renderer, variant, instanceId);
            lods[i] = new LOD(transitions[i], new Renderer[] { renderer });
        }
        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();
        return root;
    }

    public bool ContainsPool(string catalogId)
    {
        return !string.IsNullOrEmpty(catalogId) && pools.ContainsKey(catalogId);
    }

    public static int GetVariantIndex(
        ProceduralPlantSpecies species,
        int worldSeed,
        string catalogId,
        string instanceId,
        int poolSize)
    {
        if (species == null || poolSize <= 0)
            return 0;
        int hash = StableMix(
            species.schemaVersion,
            species.StableRecipeHash(),
            worldSeed,
            PlanetDecorationCatalog.StableHash(catalogId),
            PlanetDecorationCatalog.StableHash(instanceId));
        return (hash & int.MaxValue) % poolSize;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        foreach (Pool pool in pools.Values)
            DestroyPool(pool);
        pools.Clear();
        foreach (Material material in ownedMaterials)
            DestroyObject(material);
        ownedMaterials.Clear();
        disposed = true;
    }

    Material ResolveMaterial(Material assigned, string shaderName)
    {
        if (assigned != null)
            return assigned;
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            shader = Shader.Find("Standard");
        var material = new Material(shader)
        {
            name = shaderName.Replace('/', '_') + "_Runtime",
            enableInstancing = true
        };
        ownedMaterials.Add(material);
        return material;
    }

    static Mesh CreateMesh(ProceduralPlantMeshData data, string name)
    {
        var mesh = new Mesh
        {
            name = name,
            indexFormat = data.VertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.vertices = data.vertices;
        mesh.normals = data.normals;
        mesh.uv = data.uv;
        mesh.colors = data.colors;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(data.barkTriangles, 0, false);
        mesh.SetTriangles(data.foliageTriangles, 1, false);
        mesh.bounds = data.bounds;
        mesh.UploadMeshData(true);
        return mesh;
    }

    static void ApplyProperties(Renderer renderer, Variant variant, string instanceId)
    {
        float variation = ((PlanetDecorationCatalog.StableHash(instanceId) & 255) / 255f - 0.5f) * 0.12f;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", OffsetColor(variant.barkColor, variation));
        properties.SetColor("_LeafColor", OffsetColor(variant.leafColor, -variation * 0.7f));
        properties.SetColor("_AccentColor", variant.accentColor);
        properties.SetFloat("_HueVariation", variation);
        renderer.SetPropertyBlock(properties);
    }

    static Color OffsetColor(Color color, float amount)
    {
        Color.RGBToHSV(color, out float h, out float s, out float v);
        h = Mathf.Repeat(h + amount * 0.2f, 1f);
        v = Mathf.Clamp01(v + amount);
        return Color.HSVToRGB(h, s, v);
    }

    static int StableMix(params int[] values)
    {
        unchecked
        {
            int hash = 486187739;
            for (int i = 0; i < values.Length; i++)
            {
                uint value = (uint)values[i];
                value ^= value >> 16;
                value *= 0x7feb352d;
                value ^= value >> 15;
                value *= 0x846ca68b;
                value ^= value >> 16;
                hash = hash * 31 + (int)value;
            }
            return hash;
        }
    }

    static void DestroyPool(Pool pool)
    {
        if (pool?.variants == null)
            return;
        foreach (Variant variant in pool.variants)
        {
            if (variant?.meshes == null)
                continue;
            foreach (Mesh mesh in variant.meshes)
                DestroyObject(mesh);
        }
    }

    static void DestroyObject(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(value);
        else
            UnityEngine.Object.DestroyImmediate(value);
    }
}
