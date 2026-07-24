using System;
using System.Collections.Generic;
using System.Linq;
using KDL.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PlanetLabTerrainPbrLibraryBuilder
{
    public const string SourceMaterialFolder =
        "Assets/terrain_textures_vol2/materials";
    public const string OutputFolder =
        "Assets/PlanetLab/Materials";
    public const string LibraryPath =
        OutputFolder + "/PlanetLabTerrainPbrLibrary.asset";
    public const int ArrayResolution = 512;

    const string AlbedoArrayPath =
        OutputFolder + "/TerrainPbrAlbedoArray.asset";
    const string NormalArrayPath =
        OutputFolder + "/TerrainPbrNormalArray.asset";
    const string MaskArrayPath =
        OutputFolder + "/TerrainPbrMaskArray.asset";

    [MenuItem("Tools/Voxel Planet/Rebuild Planet Lab PBR Library")]
    [AICallable(
        "Rebuild the PlanetLab-only 40-set terrain PBR texture-array library.",
        Category = "PlanetLab.PBR",
        Kind = ToolKind.Write)]
    public static void RebuildLibraryMenu()
    {
        PlanetLabTerrainPbrLibrary library = RebuildLibrary();
        Selection.activeObject = library;
        Debug.Log(
            $"Planet Lab PBR library rebuilt with {library.entries.Count} material sets.");
    }

    [AICallable(
        "Validate the PlanetLab PBR texture arrays, role coverage, and planar shader.",
        Category = "PlanetLab.PBR")]
    public static string ValidateLibraryAndShader()
    {
        PlanetLabTerrainPbrLibrary library =
            AssetDatabase.LoadAssetAtPath<PlanetLabTerrainPbrLibrary>(LibraryPath);
        Shader shader = Shader.Find("VoxelPlanet/PlanetLabPlanarSurface");
        if (library == null || !library.IsReady)
            return "library-not-ready";
        if (shader == null)
            return "planar-shader-missing";
        if (ShaderUtil.ShaderHasError(shader))
            return "planar-shader-has-errors";

        string coverage = string.Join(
            ",",
            new[]
            {
                PlanetLabTerrainPbrRole.Ground,
                PlanetLabTerrainPbrRole.Rock,
                PlanetLabTerrainPbrRole.Shore,
                PlanetLabTerrainPbrRole.Cold
            }.Select(role =>
                role + "=" + library.entries.Count(entry =>
                    entry != null && (entry.roles & role) != 0)));
        return $"ready entries={library.entries.Count} depth={library.albedoArray.depth} "
            + $"resolution={library.albedoArray.width} shaderErrors=false {coverage}";
    }

    public static PlanetLabTerrainPbrLibrary EnsureLibrary()
    {
        PlanetLabTerrainPbrLibrary existing =
            AssetDatabase.LoadAssetAtPath<PlanetLabTerrainPbrLibrary>(LibraryPath);
        return existing != null && existing.IsReady
            ? existing
            : RebuildLibrary();
    }

    public static PlanetLabTerrainPbrLibrary RebuildLibrary()
    {
        EnsureOutputFolder();
        string[] materialPaths = AssetDatabase.FindAssets(
                "t:Material",
                new[] { SourceMaterialFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Material[] materials = materialPaths
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(material => material != null)
            .OrderBy(material => material.name, StringComparer.Ordinal)
            .ToArray();
        if (materials.Length != PlanetLabTerrainPbrLibrary.ExpectedMaterialCount)
        {
            throw new InvalidOperationException(
                $"Expected {PlanetLabTerrainPbrLibrary.ExpectedMaterialCount} terrain materials "
                + $"under {SourceMaterialFolder}, but found {materials.Length}.");
        }
        NormalizeTextureImportSettings(materials);
        materials = materialPaths
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(material => material != null)
            .OrderBy(material => material.name, StringComparer.Ordinal)
            .ToArray();

        Texture2DArray albedoArray = CreateArray(
            AlbedoArrayPath,
            materials.Length,
            false,
            "PlanetLab Terrain PBR Albedo");
        Texture2DArray normalArray = CreateArray(
            NormalArrayPath,
            materials.Length,
            true,
            "PlanetLab Terrain PBR Normal");
        Texture2DArray maskArray = CreateArray(
            MaskArrayPath,
            materials.Length,
            true,
            "PlanetLab Terrain PBR Height Roughness AO");

        Shader maskShader = Shader.Find("Hidden/PlanetLab/PackTerrainMask");
        if (maskShader == null)
            throw new InvalidOperationException(
                "Missing shader: Hidden/PlanetLab/PackTerrainMask");
        var maskMaterial = new Material(maskShader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        var entries = new List<PlanetLabTerrainPbrEntry>(materials.Length);
        try
        {
            for (int slice = 0; slice < materials.Length; slice++)
            {
                Material source = materials[slice];
                CopyTextureToSlice(
                    source.GetTexture("_MainTex"),
                    albedoArray,
                    slice,
                    false,
                    null);
                CopyTextureToSlice(
                    source.GetTexture("_BumpMap"),
                    normalArray,
                    slice,
                    true,
                    null);

                maskMaterial.SetTexture(
                    "_HeightTex",
                    source.GetTexture("_ParallaxMap") ?? Texture2D.blackTexture);
                maskMaterial.SetTexture(
                    "_RoughnessTex",
                    source.GetTexture("_SpecGlossMap") ?? Texture2D.whiteTexture);
                maskMaterial.SetTexture(
                    "_OcclusionTex",
                    source.GetTexture("_OcclusionMap") ?? Texture2D.whiteTexture);
                CopyTextureToSlice(
                    null,
                    maskArray,
                    slice,
                    true,
                    maskMaterial);

                entries.Add(new PlanetLabTerrainPbrEntry
                {
                    id = source.name,
                    slice = slice,
                    roles = Classify(source.name)
                });
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(maskMaterial);
        }

        albedoArray.Apply(true, false);
        normalArray.Apply(true, false);
        maskArray.Apply(true, false);
        EditorUtility.SetDirty(albedoArray);
        EditorUtility.SetDirty(normalArray);
        EditorUtility.SetDirty(maskArray);

        PlanetLabTerrainPbrLibrary library =
            AssetDatabase.LoadAssetAtPath<PlanetLabTerrainPbrLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<PlanetLabTerrainPbrLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.albedoArray = albedoArray;
        library.normalArray = normalArray;
        library.maskArray = maskArray;
        library.entries = entries;
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return library;
    }

    static Texture2DArray CreateArray(
        string path,
        int depth,
        bool linear,
        string name)
    {
        Texture2DArray existing =
            AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
        if (existing != null
            && existing.width == ArrayResolution
            && existing.height == ArrayResolution
            && existing.depth == depth)
        {
            existing.name = name;
            existing.wrapMode = TextureWrapMode.Repeat;
            existing.filterMode = FilterMode.Trilinear;
            existing.anisoLevel = 8;
            return existing;
        }

        if (existing != null)
            AssetDatabase.DeleteAsset(path);
        var array = new Texture2DArray(
            ArrayResolution,
            ArrayResolution,
            depth,
            TextureFormat.RGBA32,
            true,
            linear)
        {
            name = name,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8
        };
        AssetDatabase.CreateAsset(array, path);
        return array;
    }

    static void CopyTextureToSlice(
        Texture source,
        Texture2DArray destination,
        int slice,
        bool linear,
        Material conversionMaterial)
    {
        RenderTextureReadWrite readWrite = linear
            ? RenderTextureReadWrite.Linear
            : RenderTextureReadWrite.sRGB;
        RenderTexture target = RenderTexture.GetTemporary(
            ArrayResolution,
            ArrayResolution,
            0,
            RenderTextureFormat.ARGB32,
            readWrite);
        RenderTexture previous = RenderTexture.active;
        var pixels = new Texture2D(
            ArrayResolution,
            ArrayResolution,
            TextureFormat.RGBA32,
            false,
            linear)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        try
        {
            if (conversionMaterial != null)
                Graphics.Blit(null, target, conversionMaterial);
            else
                Graphics.Blit(source ?? Texture2D.whiteTexture, target);
            RenderTexture.active = target;
            pixels.ReadPixels(
                new Rect(0, 0, ArrayResolution, ArrayResolution),
                0,
                0,
                false);
            pixels.Apply(false, false);
            destination.SetPixels(pixels.GetPixels(), slice, 0);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(pixels);
        }
    }

    static PlanetLabTerrainPbrRole Classify(string materialName)
    {
        string value = materialName.ToLowerInvariant();
        PlanetLabTerrainPbrRole roles = 0;

        if (ContainsAny(value, "grass", "ground", "mud", "foliage", "earth"))
            roles |= PlanetLabTerrainPbrRole.Ground;
        if (ContainsAny(
                value,
                "rock",
                "stone",
                "cliff",
                "crack",
                "break"))
        {
            roles |= PlanetLabTerrainPbrRole.Rock;
        }
        if (ContainsAny(value, "sand", "mud", "ground_01", "ground_02"))
            roles |= PlanetLabTerrainPbrRole.Shore;
        if (ContainsAny(value, "snow", "ice", "frozen"))
            roles |= PlanetLabTerrainPbrRole.Cold;
        if (ContainsAny(value, "ice", "frozen"))
            roles |= PlanetLabTerrainPbrRole.Shore;

        return roles == 0 ? PlanetLabTerrainPbrRole.Ground : roles;
    }

    static bool ContainsAny(string value, params string[] fragments)
        => fragments.Any(value.Contains);

    static void NormalizeTextureImportSettings(Material[] materials)
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (Material material in materials)
            {
                ConfigureTexture(
                    material.GetTexture("_MainTex"),
                    true,
                    TextureImporterType.Default);
                ConfigureTexture(
                    material.GetTexture("_BumpMap"),
                    false,
                    TextureImporterType.NormalMap);
                ConfigureTexture(
                    material.GetTexture("_ParallaxMap"),
                    false,
                    TextureImporterType.Default);
                ConfigureTexture(
                    material.GetTexture("_SpecGlossMap"),
                    false,
                    TextureImporterType.Default);
                ConfigureTexture(
                    material.GetTexture("_OcclusionMap"),
                    false,
                    TextureImporterType.Default);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        AssetDatabase.Refresh();
    }

    static void ConfigureTexture(
        Texture texture,
        bool srgb,
        TextureImporterType textureType)
    {
        if (texture == null)
            return;
        string path = AssetDatabase.GetAssetPath(texture);
        TextureImporter importer =
            AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        bool changed = false;
        if (importer.sRGBTexture != srgb)
        {
            importer.sRGBTexture = srgb;
            changed = true;
        }
        if (importer.textureType != textureType)
        {
            importer.textureType = textureType;
            changed = true;
        }
        if (!importer.mipmapEnabled)
        {
            importer.mipmapEnabled = true;
            changed = true;
        }
        if (importer.wrapMode != TextureWrapMode.Repeat)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            changed = true;
        }
        if (importer.anisoLevel < 4)
        {
            importer.anisoLevel = 4;
            changed = true;
        }
        if (changed)
            importer.SaveAndReimport();
    }

    static void EnsureOutputFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/PlanetLab"))
            AssetDatabase.CreateFolder("Assets", "PlanetLab");
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/PlanetLab", "Materials");
    }
}
