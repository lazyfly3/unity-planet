using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class NmsSubstanceOfflineMaterialBuilder
{
    const string ExportRoot = "Assets/Creatures/Materials/SubstanceOffline/Exports";
    const string SubstanceImportRoot = "Assets/Creatures/Materials/SubstanceSbsar";
    const string CatalogPath = "Assets/Resources/Creatures/NmsOrganicMaterialCatalog.asset";

    static readonly string[] TextureExtensions =
    {
        ".png", ".tga", ".tif", ".tiff", ".jpg", ".jpeg", ".exr"
    };

    [MenuItem("Tools/Creatures/Substance/Rebuild Offline Texture Catalog")]
    public static void RebuildCatalog()
    {
        EnsureFolders();
        string[] texturePaths = FindCatalogTexturePaths()
            .ToArray();

        BuildCatalog(texturePaths, "offline texture");
    }

    [MenuItem("Tools/Creatures/Substance/Rebuild All Imported Substance Catalog")]
    public static void RebuildAllImportedSubstanceCatalog()
    {
        EnsureFolders();
        string[] texturePaths = FindCatalogTexturePaths(ExportRoot, SubstanceImportRoot)
            .ToArray();

        BuildCatalog(texturePaths, "offline + imported Substance");
    }

    static IEnumerable<string> FindCatalogTexturePaths(params string[] roots)
    {
        string[] searchRoots = roots == null || roots.Length == 0
            ? new[] { ExportRoot }
            : roots.Where(AssetDatabase.IsValidFolder).ToArray();

        if (searchRoots.Length == 0)
            return Array.Empty<string>();

        return AssetDatabase.FindAssets("t:Texture2D", searchRoots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsTexturePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    static void BuildCatalog(string[] texturePaths, string sourceLabel)
    {
        var groups = new Dictionary<string, TextureGroup>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < texturePaths.Length; i++)
        {
            string path = texturePaths[i];
            string baseName = NormalizeBaseName(Path.GetFileNameWithoutExtension(path), out TextureSlot slot);
            if (slot == TextureSlot.Ignore || string.IsNullOrEmpty(baseName))
                continue;
            if (!groups.TryGetValue(baseName, out TextureGroup group))
            {
                group = new TextureGroup { id = SanitizeId(baseName), displayName = baseName };
                groups.Add(baseName, group);
            }
            group.Assign(slot, path);
            ConfigureTextureImporter(path, slot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var textureSets = new List<NmsOrganicTextureSet>(groups.Count);
        foreach (TextureGroup group in groups.Values.OrderBy(value => value.id, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(group.baseColorPath)
                && string.IsNullOrEmpty(group.normalPath)
                && string.IsNullOrEmpty(group.maskPath)
                && string.IsNullOrEmpty(group.emissionPath))
                continue;

            var textureSet = new NmsOrganicTextureSet();
            NmsOrganicMaterialKind kind = InferKind(group.displayName);
            textureSet.Configure(
                group.id,
                group.displayName,
                kind,
                LoadTexture(group.baseColorPath),
                LoadTexture(group.normalPath),
                LoadTexture(group.maskPath),
                LoadTexture(group.emissionPath),
                DefaultPaletteStrength(kind),
                group.normalPath != null ? 1f : 0f,
                group.emissionPath != null ? 1f : 0f);
            textureSets.Add(textureSet);
        }

        NmsOrganicMaterialCatalog catalog =
            AssetDatabase.LoadAssetAtPath<NmsOrganicMaterialCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<NmsOrganicMaterialCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.ConfigureImported(textureSets.ToArray());
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"NMS Substance {sourceLabel} catalog rebuilt. TextureSets={textureSets.Count}, " +
            $"Textures={texturePaths.Length}, " +
            $"SubstancePlugin={(DetectSubstancePlugin() ? "Available" : "Missing; using offline textures")}. " +
            $"ExportRoot={ExportRoot}, SubstanceImportRoot={SubstanceImportRoot}");
    }

    [MenuItem("Tools/Creatures/Substance/Check Plugin And Fallback Status")]
    public static void CheckStatus()
    {
        bool hasPlugin = DetectSubstancePlugin();
        bool hasCatalog = AssetDatabase.LoadAssetAtPath<NmsOrganicMaterialCatalog>(CatalogPath) != null;
        int exportedTextures = AssetDatabase.FindAssets("t:Texture2D", new[] { ExportRoot }).Length;
        int importedSubstanceTextures = AssetDatabase.IsValidFolder(SubstanceImportRoot)
            ? AssetDatabase.FindAssets("t:Texture2D", new[] { SubstanceImportRoot }).Length
            : 0;
        Debug.Log(
            "NMS Substance material status: " +
            $"Plugin={(hasPlugin ? "Available" : "Not installed")}, " +
            $"OfflineCatalog={(hasCatalog ? "Available" : "Missing")}, " +
            $"ExportedTextures={exportedTextures}, " +
            $"ImportedSubstanceTextures={importedSubstanceTextures}, " +
            $"FallbackRoot={ExportRoot}");
    }

    static void EnsureFolders()
    {
        EnsureFolder("Assets/Creatures");
        EnsureFolder("Assets/Creatures/Materials");
        EnsureFolder("Assets/Creatures/Materials/SubstanceOffline");
        EnsureFolder(ExportRoot);
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Creatures");
    }

    static void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    static bool DetectSubstancePlugin()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            string name = assemblies[i].GetName().Name;
            if (string.Equals(name, "UnityEngine.SubstanceModule", StringComparison.Ordinal))
                continue;
            if (name.IndexOf("Adobe.Substance", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Substance.Editor", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Substance.Game", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return Type.GetType("Adobe.Substance.SubstanceGraph, Adobe.Substance") != null
            || Type.GetType("Substance.Game.SubstanceGraph, Substance") != null;
    }

    static bool IsTexturePath(string path)
    {
        string extension = Path.GetExtension(path);
        for (int i = 0; i < TextureExtensions.Length; i++)
            if (string.Equals(extension, TextureExtensions[i], StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string NormalizeBaseName(string fileName, out TextureSlot slot)
    {
        string lower = fileName.ToLowerInvariant();
        slot = TextureSlot.BaseColor;
        string[] normalSuffixes = { "_normal", "_nrm", "_n", "_normalmap" };
        string[] maskSuffixes = { "_mask", "_masks", "_maskmap", "_orm", "_rma", "_ao_rough_metal" };
        string[] emissionSuffixes = { "_emission", "_emissive", "_emit", "_glow" };
        string[] baseSuffixes = { "_basecolor", "_base_color", "_albedo", "_diffuse", "_diffusemap", "_color", "_col" };
        string[] ignoredSuffixes =
        {
            "_height",
            "_heightmap",
            "_metallic",
            "_metalness",
            "_roughness",
            "_ambientocclusion",
            "_ambient_occlusion",
            "_ao"
        };

        if (TryTrim(lower, fileName, ignoredSuffixes, out string result))
        {
            slot = TextureSlot.Ignore;
            return result;
        }
        if (TryTrim(lower, fileName, normalSuffixes, out result))
        {
            slot = TextureSlot.Normal;
            return result;
        }
        if (TryTrim(lower, fileName, maskSuffixes, out result))
        {
            slot = TextureSlot.Mask;
            return result;
        }
        if (TryTrim(lower, fileName, emissionSuffixes, out result))
        {
            slot = TextureSlot.Emission;
            return result;
        }
        if (TryTrim(lower, fileName, baseSuffixes, out result))
        {
            slot = TextureSlot.BaseColor;
            return result;
        }
        return fileName;
    }

    static bool TryTrim(
        string lower,
        string original,
        IReadOnlyList<string> suffixes,
        out string result)
    {
        for (int i = 0; i < suffixes.Count; i++)
        {
            string suffix = suffixes[i];
            if (!lower.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            result = original.Substring(0, original.Length - suffix.Length);
            return true;
        }
        result = null;
        return false;
    }

    static string SanitizeId(string value)
    {
        var chars = value.ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars).Trim('_');
    }

    static void ConfigureTextureImporter(string assetPath, TextureSlot slot)
    {
        if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer))
            return;
        bool changed = false;
        TextureImporterType type = slot == TextureSlot.Normal
            ? TextureImporterType.NormalMap
            : TextureImporterType.Default;
        if (importer.textureType != type)
        {
            importer.textureType = type;
            changed = true;
        }
        bool srgb = slot == TextureSlot.BaseColor;
        if (importer.sRGBTexture != srgb)
        {
            importer.sRGBTexture = srgb;
            changed = true;
        }
        if (importer.wrapMode != TextureWrapMode.Repeat)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            changed = true;
        }
        if (!importer.mipmapEnabled)
        {
            importer.mipmapEnabled = true;
            changed = true;
        }
        if (changed)
            importer.SaveAndReimport();
    }

    static Texture2D LoadTexture(string path)
    {
        return string.IsNullOrEmpty(path)
            ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static NmsOrganicMaterialKind InferKind(string value)
    {
        string lower = value.ToLowerInvariant();
        if (lower.Contains("fur") || lower.Contains("hair"))
            return NmsOrganicMaterialKind.Fur;
        if (lower.Contains("scale") || lower.Contains("reptile") || lower.Contains("lizard")
            || lower.Contains("snake") || lower.Contains("fish"))
            return NmsOrganicMaterialKind.Scale;
        if (lower.Contains("horn") || lower.Contains("claw") || lower.Contains("keratin"))
            return NmsOrganicMaterialKind.Horn;
        if (lower.Contains("bone") || lower.Contains("teeth"))
            return NmsOrganicMaterialKind.Bone;
        if (lower.Contains("eye"))
            return NmsOrganicMaterialKind.Eye;
        if (lower.Contains("mouth") || lower.Contains("jaw") || lower.Contains("tongue")
            || lower.Contains("guts") || lower.Contains("goo"))
            return NmsOrganicMaterialKind.Mouth;
        if (lower.Contains("flesh") || lower.Contains("wound") || lower.Contains("muscle")
            || lower.Contains("brain") || lower.Contains("meat"))
            return NmsOrganicMaterialKind.Flesh;
        if (lower.Contains("armor") || lower.Contains("shell") || lower.Contains("plate")
            || lower.Contains("carapace") || lower.Contains("crust"))
            return NmsOrganicMaterialKind.Armor;
        if (lower.Contains("metal") || lower.Contains("mechanic") || lower.Contains("robot"))
            return NmsOrganicMaterialKind.Mechanical;
        if (lower.Contains("glow") || lower.Contains("emiss"))
            return NmsOrganicMaterialKind.Emissive;
        if (lower.Contains("skin") || lower.Contains("scale") || lower.Contains("lizard")
            || lower.Contains("frog") || lower.Contains("snake") || lower.Contains("cell")
            || lower.Contains("membrane") || lower.Contains("alien"))
            return NmsOrganicMaterialKind.Skin;
        return NmsOrganicMaterialKind.Generic;
    }

    static float DefaultPaletteStrength(NmsOrganicMaterialKind kind)
    {
        switch (kind)
        {
            case NmsOrganicMaterialKind.Bone:
            case NmsOrganicMaterialKind.Horn:
                return 0.2f;
            case NmsOrganicMaterialKind.Eye:
            case NmsOrganicMaterialKind.Mouth:
            case NmsOrganicMaterialKind.Flesh:
                return 0.18f;
            case NmsOrganicMaterialKind.Armor:
            case NmsOrganicMaterialKind.Scale:
                return 0.28f;
            case NmsOrganicMaterialKind.Mechanical:
                return 0.15f;
            case NmsOrganicMaterialKind.Emissive:
                return 0.45f;
            default:
                return 0.35f;
        }
    }

    enum TextureSlot
    {
        BaseColor,
        Normal,
        Mask,
        Emission,
        Ignore
    }

    sealed class TextureGroup
    {
        public string id;
        public string displayName;
        public string baseColorPath;
        public string normalPath;
        public string maskPath;
        public string emissionPath;

        public void Assign(TextureSlot slot, string path)
        {
            switch (slot)
            {
                case TextureSlot.BaseColor:
                    if (baseColorPath == null || IsPreferredBaseColor(path, baseColorPath))
                        baseColorPath = path;
                    break;
                case TextureSlot.Normal:
                    normalPath = path;
                    break;
                case TextureSlot.Mask:
                    maskPath = path;
                    break;
                case TextureSlot.Emission:
                    emissionPath = path;
                    break;
            }
        }

        static bool IsPreferredBaseColor(string candidate, string current)
        {
            string candidateName = Path.GetFileNameWithoutExtension(candidate).ToLowerInvariant();
            string currentName = Path.GetFileNameWithoutExtension(current).ToLowerInvariant();
            bool candidateExplicit = candidateName.EndsWith("_basecolor", StringComparison.Ordinal)
                || candidateName.EndsWith("_base_color", StringComparison.Ordinal)
                || candidateName.EndsWith("_albedo", StringComparison.Ordinal);
            bool currentExplicit = currentName.EndsWith("_basecolor", StringComparison.Ordinal)
                || currentName.EndsWith("_base_color", StringComparison.Ordinal)
                || currentName.EndsWith("_albedo", StringComparison.Ordinal);
            return candidateExplicit && !currentExplicit;
        }
    }
}
