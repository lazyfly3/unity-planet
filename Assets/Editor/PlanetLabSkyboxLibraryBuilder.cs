using System;
using System.Collections.Generic;
using System.Linq;
using KDL.Editor;
using UnityEditor;
using UnityEngine;

public static class PlanetLabSkyboxLibraryBuilder
{
    static readonly string[] FaceProperties =
    {
        "_FrontTex",
        "_BackTex",
        "_LeftTex",
        "_RightTex",
        "_UpTex",
        "_DownTex"
    };

    static readonly string[] FaceFileMarkers =
    {
        "Front",
        "Back",
        "Left",
        "Right",
        "Up/Top",
        "Down"
    };

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
        int invalidMaterials = library.entries.Count(entry =>
            entry == null || !IsRenderableSkybox(entry.material));
        return $"ready entries={library.entries.Count} maxTexture="
            + $"{MaximumTextureSize} missingShaders={missingShaders} "
            + $"invalidMaterials={invalidMaterials} {coverage}";
    }

    public static PlanetLabSkyboxLibrary EnsureLibrary()
    {
        PlanetLabSkyboxLibrary existing =
            AssetDatabase.LoadAssetAtPath<PlanetLabSkyboxLibrary>(LibraryPath);
        return existing != null
            && existing.IsReady
            && existing.entries.All(entry =>
                entry != null && IsRenderableSkybox(entry.material))
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
            NormalizeSkyboxMaterial(material, path);

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

    static void NormalizeSkyboxMaterial(Material material, string path)
    {
        Shader shader = Shader.Find("Skybox/6 Sided");
        if (shader == null)
        {
            throw new InvalidOperationException(
                "Unity built-in Skybox/6 Sided shader is unavailable.");
        }

        string folder = System.IO.Path.GetDirectoryName(path)
            ?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder))
            throw new InvalidOperationException(
                "Skybox material folder is invalid: " + path);

        string[] texturePaths = AssetDatabase.FindAssets(
                string.Empty,
                new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsTexturePath)
            .Where(texturePath => string.Equals(
                System.IO.Path.GetDirectoryName(texturePath)
                    ?.Replace('\\', '/'),
                folder,
                StringComparison.Ordinal))
            .ToArray();

        Texture2D[] faces = new Texture2D[FaceProperties.Length];
        for (int index = 0; index < FaceProperties.Length; index++)
        {
            string texturePath = texturePaths.FirstOrDefault(candidate =>
                MatchesFaceFile(candidate, index));
            faces[index] = string.IsNullOrEmpty(texturePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (faces[index] == null)
            {
                throw new InvalidOperationException(
                    $"Skybox face {FaceFileMarkers[index]} is missing beside {path}.");
            }
        }

        bool changed = material.shader != shader;
        if (changed)
            material.shader = shader;
        for (int index = 0; index < FaceProperties.Length; index++)
        {
            if (material.GetTexture(FaceProperties[index]) == faces[index])
                continue;
            material.SetTexture(FaceProperties[index], faces[index]);
            changed = true;
        }
        if (changed)
            EditorUtility.SetDirty(material);
    }

    static bool IsRenderableSkybox(Material material)
    {
        if (material == null
            || material.shader == null
            || !string.Equals(
                material.shader.name,
                "Skybox/6 Sided",
                StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = 0; index < FaceProperties.Length; index++)
        {
            if (!material.HasProperty(FaceProperties[index])
                || material.GetTexture(FaceProperties[index]) == null)
            {
                return false;
            }
        }
        return true;
    }

    static void NormalizeTextureImportSettings()
    {
        string[] texturePaths = AssetDatabase.FindAssets(
                string.Empty,
                new[] { SourceFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsTexturePath)
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
                importer.textureShape,
                TextureImporterShape.Texture2D,
                value => importer.textureShape = value);
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

    static bool IsTexturePath(string path)
    {
        string extension = System.IO.Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase);
    }

    static bool MatchesFaceFile(string path, int faceIndex)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (faceIndex == 4)
        {
            return name.IndexOf("Up", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        return name.IndexOf(
            FaceFileMarkers[faceIndex],
            StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
