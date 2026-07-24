using System;
using System.Collections.Generic;
using System.Linq;
using KDL.Editor;
using UnityEditor;
using UnityEngine;

public static class PlanetLabSkyboxLibraryBuilder
{
    public const string SourceFolder =
        "Assets/PlanetLab/Skyboxes/AllSkySelected";
    public const string LibraryPath =
        "Assets/PlanetLab/Skyboxes/PlanetLabSkyboxLibrary.asset";
    public const int MaximumTextureSize = 1024;

    [MenuItem("Tools/Voxel Planet/Rebuild Planet Lab Skybox Library")]
    [AICallable(
        "Rebuild the PlanetLab-only seeded skybox library.",
        Category = "PlanetLab.Skybox",
        Kind = ToolKind.Write)]
    public static void RebuildLibraryMenu()
    {
        PlanetLabSkyboxLibrary library = RebuildLibrary();
        Selection.activeObject = library;
        Debug.Log(
            $"Planet Lab skybox library rebuilt with {library.entries.Count} materials.");
    }

    [AICallable(
        "Validate the PlanetLab skybox candidates and template coverage.",
        Category = "PlanetLab.Skybox")]
    public static string ValidateLibraryAndMaterials()
    {
        PlanetLabSkyboxLibrary library =
            AssetDatabase.LoadAssetAtPath<PlanetLabSkyboxLibrary>(LibraryPath);
        if (library == null || !library.IsReady)
            return "library-not-ready";

        string coverage = string.Join(
            ",",
            Enum.GetValues(typeof(ProceduralPlanetLabTemplate))
                .Cast<ProceduralPlanetLabTemplate>()
                .Select(template => template + "="
                    + library.entries.Count(entry =>
                        entry != null
                        && entry.material != null
                        && entry.template == template)));
        int missingShaders = library.entries.Count(entry =>
            entry == null
            || entry.material == null
            || entry.material.shader == null);
        return $"ready entries={library.entries.Count} maxTexture="
            + $"{MaximumTextureSize} missingShaders={missingShaders} {coverage}";
    }

    public static PlanetLabSkyboxLibrary EnsureLibrary()
    {
        PlanetLabSkyboxLibrary existing =
            AssetDatabase.LoadAssetAtPath<PlanetLabSkyboxLibrary>(LibraryPath);
        return existing != null && existing.IsReady
            ? existing
            : RebuildLibrary();
    }

    public static PlanetLabSkyboxLibrary RebuildLibrary()
    {
        NormalizeTextureImportSettings();
        string[] materialPaths = AssetDatabase.FindAssets(
                "t:Material",
                new[] { SourceFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (materialPaths.Length != PlanetLabSkyboxLibrary.ExpectedEntryCount)
        {
            throw new InvalidOperationException(
                $"Expected {PlanetLabSkyboxLibrary.ExpectedEntryCount} selected skyboxes "
                + $"under {SourceFolder}, but found {materialPaths.Length}.");
        }

        var entries =
            new List<PlanetLabSkyboxEntry>(materialPaths.Length);
        for (int index = 0; index < materialPaths.Length; index++)
        {
            string path = materialPaths[index];
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null)
            {
                throw new InvalidOperationException(
                    "Skybox material or shader is missing: " + path);
            }

            string relative = path.Substring(SourceFolder.Length + 1);
            string templateName = relative.Split('/')[0];
            if (!Enum.TryParse(
                    templateName,
                    false,
                    out ProceduralPlanetLabTemplate template))
            {
                throw new InvalidOperationException(
                    "Unknown PlanetLab skybox template folder: " + templateName);
            }

            entries.Add(new PlanetLabSkyboxEntry
            {
                id = template + "/" + material.name,
                template = template,
                material = material
            });
        }

        foreach (ProceduralPlanetLabTemplate template
                 in Enum.GetValues(typeof(ProceduralPlanetLabTemplate)))
        {
            int count = entries.Count(entry => entry.template == template);
            if (count != PlanetLabSkyboxLibrary.CandidatesPerTemplate)
            {
                throw new InvalidOperationException(
                    $"Expected {PlanetLabSkyboxLibrary.CandidatesPerTemplate} "
                    + $"{template} skyboxes, but found {count}.");
            }
        }

        PlanetLabSkyboxLibrary library =
            AssetDatabase.LoadAssetAtPath<PlanetLabSkyboxLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<PlanetLabSkyboxLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.entries = entries;
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        return library;
    }

    static void NormalizeTextureImportSettings()
    {
        string[] texturePaths = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { SourceFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (string path in texturePaths)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                continue;

            bool changed = false;
            changed |= SetIfDifferent(
                importer.textureType,
                TextureImporterType.Default,
                value => importer.textureType = value);
            changed |= SetIfDifferent(
                importer.sRGBTexture,
                true,
                value => importer.sRGBTexture = value);
            changed |= SetIfDifferent(
                importer.maxTextureSize,
                MaximumTextureSize,
                value => importer.maxTextureSize = value);
            changed |= SetIfDifferent(
                importer.mipmapEnabled,
                true,
                value => importer.mipmapEnabled = value);
            changed |= SetIfDifferent(
                importer.wrapMode,
                TextureWrapMode.Clamp,
                value => importer.wrapMode = value);
            changed |= SetIfDifferent(
                importer.filterMode,
                FilterMode.Trilinear,
                value => importer.filterMode = value);
            changed |= SetIfDifferent(
                importer.textureCompression,
                TextureImporterCompression.Compressed,
                value => importer.textureCompression = value);
            if (changed)
                importer.SaveAndReimport();
        }
    }

    static bool SetIfDifferent<T>(T current, T requested, Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, requested))
            return false;
        setter(requested);
        return true;
    }
}
