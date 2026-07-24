using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LowPolyPlanetKitAssetBuilder
{
    const int BuildVersion = 3;
    const string ModelRoot = "Assets/Models/PlanetLowPolyKit";
    const string PrefabRoot = "Assets/PlanetDecoration/LowPolyPlanetKit";
    const string CloudPrefabRoot = "Assets/Resources/PlanetLowPolyKit";
    const string BuildKeyPrefix = "LowPolyPlanetKit.Build.";

    static bool buildScheduled;
    static bool building;

    static LowPolyPlanetKitAssetBuilder()
    {
        EditorApplication.delayCall += EnsureBuilt;
    }

    public static void ScheduleBuild()
    {
        if (buildScheduled)
            return;
        buildScheduled = true;
        EditorApplication.delayCall += EnsureBuilt;
    }

    public static void AppendCatalogEntries(List<PlanetDecorationEntry> entries)
    {
        if (entries == null)
            return;

        AddCatalogEntries(entries, "Rock", PlanetClimateMask.All, PlanetDecorationRole.Rock, 1.15f);
        AddCatalogEntries(entries, "Plant",
            PlanetClimateMask.TemperateForest | PlanetClimateMask.Tropical | PlanetClimateMask.Desert
            | PlanetClimateMask.Tundra | PlanetClimateMask.Crystal,
            PlanetDecorationRole.Vegetation, 1f);
        AddCatalogEntries(entries, "Crystal",
            PlanetClimateMask.Crystal | PlanetClimateMask.Volcanic | PlanetClimateMask.Barren
            | PlanetClimateMask.Tundra,
            PlanetDecorationRole.Rock, 0.9f);
        AddCatalogEntries(entries, "Landmark", PlanetClimateMask.All, PlanetDecorationRole.Landmark, 0.42f);
    }

    static void EnsureBuilt()
    {
        buildScheduled = false;
        if (building || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/Rock_01_LOD0.fbx") == null)
            return;

        string buildKey = BuildKeyPrefix + Application.dataPath.GetHashCode();
        bool outputsMissing =
            AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Rock_01.prefab") == null
            || AssetDatabase.LoadAssetAtPath<GameObject>($"{CloudPrefabRoot}/Cloud_01.prefab") == null;
        if (!outputsMissing && EditorPrefs.GetInt(buildKey, 0) == BuildVersion)
        {
            EnsureCatalogContainsKit();
            return;
        }

        building = true;
        try
        {
            EnsureFolder(PrefabRoot);
            EnsureFolder(CloudPrefabRoot);
            for (int i = 1; i <= 4; i++)
            {
                BuildLodPrefab("Rock", i, PrefabRoot, true);
                BuildLodPrefab("Plant", i, PrefabRoot, true);
                BuildLodPrefab("Crystal", i, PrefabRoot, true);
                BuildLodPrefab("Landmark", i, PrefabRoot, true);
                BuildLodPrefab("Cloud", i, CloudPrefabRoot, false);
            }
            EnsureCatalogContainsKit();
            EditorPrefs.SetInt(buildKey, BuildVersion);
            AssetDatabase.SaveAssets();
            Debug.Log("LowPolyPlanetKit: built 16 decoration prefabs, 4 cloud prefabs, and updated the catalog.");
        }
        finally
        {
            building = false;
        }
    }

    static void EnsureCatalogContainsKit()
    {
        PlanetDecorationCatalog catalog = PlanetDecorationToolBootstrap.EnsureCatalog();
        if (catalog == null)
            return;

        var profiles = new List<PlanetClimateProfile>(catalog.Profiles.Count);
        foreach (PlanetClimateProfile profile in catalog.Profiles)
            profiles.Add(profile);
        var entries = new List<PlanetDecorationEntry>(catalog.Entries.Count + 16);
        foreach (PlanetDecorationEntry entry in catalog.Entries)
            entries.Add(entry);
        AppendCatalogEntries(entries);
        catalog.ReplaceContents(profiles, entries);
        EditorUtility.SetDirty(catalog);
    }

    static void AddCatalogEntries(
        List<PlanetDecorationEntry> entries,
        string category,
        PlanetClimateMask climates,
        PlanetDecorationRole role,
        float weight)
    {
        for (int i = 1; i <= 4; i++)
        {
            string name = $"{category}_{i:00}";
            string stableId = $"lowpoly-kit-{category.ToLowerInvariant()}-{i:00}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{name}.prefab");
            if (prefab == null)
                continue;

            PlanetDecorationEntry entry = PlanetDecorationToolBootstrap.CreateEntry(
                stableId, prefab, climates, role, 1f, 0f, true);
            entry.weight = weight;
            if (role == PlanetDecorationRole.Landmark)
            {
                entry.minimumScale = 1.4f;
                entry.maximumScale = 3.2f;
                entry.minimumSpacing = 26f;
                entry.clusterSize = 1;
                entry.clusterRadius = 0f;
            }
            else if (category == "Crystal")
            {
                entry.minimumScale = 0.75f;
                entry.maximumScale = 1.8f;
                entry.minimumSpacing = 3.5f;
            }
            else if (category == "Plant")
            {
                entry.minimumScale = 0.75f;
                entry.maximumScale = 1.65f;
                entry.clusterRadius = 13f;
                entry.clusterSize = 9;
            }

            int existing = entries.FindIndex(value =>
                value != null && string.Equals(value.stableId, stableId, StringComparison.Ordinal));
            if (existing >= 0)
                entries[existing] = entry;
            else
                entries.Add(entry);
        }
    }

    static void BuildLodPrefab(string category, int index, string outputRoot, bool decoration)
    {
        string baseName = $"{category}_{index:00}";
        GameObject[] sources = new GameObject[3];
        for (int lod = 0; lod < sources.Length; lod++)
        {
            sources[lod] = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{ModelRoot}/{baseName}_LOD{lod}.fbx");
            if (sources[lod] == null)
                throw new InvalidOperationException($"Missing Low Poly kit source: {baseName}_LOD{lod}.fbx");
        }

        GameObject root = new GameObject(baseName);
        try
        {
            var lods = new LOD[3];
            float[] thresholds = { 0.48f, 0.18f, 0.035f };
            for (int lod = 0; lod < sources.Length; lod++)
            {
                GameObject child = PrefabUtility.InstantiatePrefab(sources[lod]) as GameObject;
                if (child == null)
                    child = UnityEngine.Object.Instantiate(sources[lod]);
                child.name = $"LOD{lod}";
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = Vector3.one;
                Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                lods[lod] = new LOD(thresholds[lod], renderers);
            }

            LODGroup group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.SetLODs(lods);
            group.RecalculateBounds();

            if (decoration)
            {
                Bounds bounds = CalculateLocalBounds(root.transform, lods[0].renderers);
                PlanetDecorationAnchor anchor = root.AddComponent<PlanetDecorationAnchor>();
                anchor.sourcePrefab = sources[0];
                anchor.role = CategoryRole(category);
                anchor.orientationVerified = true;
                anchor.localBounds = bounds;
            }

            string outputPath = $"{outputRoot}/{baseName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, outputPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static Bounds CalculateLocalBounds(Transform root, Renderer[] renderers)
    {
        bool initialized = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        foreach (Renderer rendererValue in renderers)
        {
            Bounds world = rendererValue.bounds;
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                Vector3 local = toLocal.MultiplyPoint3x4(corner);
                if (!initialized)
                {
                    result = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                    result.Encapsulate(local);
            }
        }
        return initialized ? result : new Bounds(Vector3.up * 0.5f, Vector3.one);
    }

    static PlanetDecorationRole CategoryRole(string category)
    {
        if (category == "Plant")
            return PlanetDecorationRole.Vegetation;
        if (category == "Landmark")
            return PlanetDecorationRole.Landmark;
        return PlanetDecorationRole.Rock;
    }

    static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

public sealed class LowPolyPlanetKitAssetPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        foreach (string path in importedAssets)
        {
            if (path.StartsWith("Assets/Models/PlanetLowPolyKit/", StringComparison.Ordinal))
            {
                LowPolyPlanetKitAssetBuilder.ScheduleBuild();
                return;
            }
        }
    }
}
