using System;
using System.IO;
using System.Linq;
using KDL.Editor;
using UnityEditor;
using UnityEngine;

namespace SpacecraftEditor.Editor
{
    /// <summary>
    /// Rebinds the existing material IDs to physically distinct, texture-driven
    /// Standard materials without changing blueprint or save compatibility.
    /// </summary>
    public static class SpacecraftSurfaceMaterialBuilder
    {
        const string MaterialRoot = "Assets/SpacecraftEditor/Art/Materials/Paints";
        const string PbrTextureRoot = "Assets/SpacecraftEditor/Art/Generated/PBR";
        const string LibraryTextureRoot = MaterialRoot + "/PBRLibrary";

        readonly struct SurfaceSpec
        {
            public readonly string FileName;
            public readonly string DisplayName;
            public readonly Color Tint;
            public readonly float Metallic;
            public readonly float Smoothness;
            public readonly float NormalStrength;
            public readonly float Tiling;

            public SurfaceSpec(
                string fileName,
                string displayName,
                Color tint,
                float metallic,
                float smoothness,
                float normalStrength,
                float tiling)
            {
                FileName = fileName;
                DisplayName = displayName;
                Tint = tint;
                Metallic = metallic;
                Smoothness = smoothness;
                NormalStrength = normalStrength;
                Tiling = tiling;
            }
        }

        static readonly SurfaceSpec[] Specs =
        {
            new SurfaceSpec(
                "deep_space_blue", "深空钛蓝",
                new Color(0.18f, 0.42f, 0.70f), 0.78f, 0.68f, 0.55f, 5f),
            new SurfaceSpec(
                "gunmetal", "喷砂枪灰",
                new Color(0.32f, 0.35f, 0.38f), 0.92f, 0.28f, 0.82f, 7f),
            new SurfaceSpec(
                "ceramic_white", "航空拉丝铝",
                new Color(0.76f, 0.80f, 0.82f), 0.96f, 0.46f, 0.42f, 4f),
            new SurfaceSpec(
                "warning_red", "红色镀锌钢",
                new Color(0.66f, 0.15f, 0.12f), 0.95f, 0.52f, 0.72f, 6f),
            new SurfaceSpec(
                "industrial_copper", "氧化工业铜",
                new Color(0.63f, 0.29f, 0.10f), 1f, 0.62f, 0.50f, 5f),
            new SurfaceSpec(
                "explorer_green", "绿色军用钢",
                new Color(0.18f, 0.42f, 0.28f), 1f, 0.72f, 0.38f, 6f),
            new SurfaceSpec(
                "brushed_brass", "拉丝黄铜",
                new Color(0.67f, 0.48f, 0.14f), 0.98f, 0.58f, 0.48f, 5f),
            new SurfaceSpec(
                "graphite_pitted", "蚀刻石墨金属",
                new Color(0.18f, 0.20f, 0.22f), 0.88f, 0.24f, 0.95f, 7f)
        };

        [MenuItem("Tools/Spacecraft/Rebuild Surface Material Library")]
        [AICallable(
            "重建组装界面的八种真实且可清晰区分的 PBR 表面材质，保留现有 materialId 和存档兼容性。",
            Category = "Spacecraft.Materials",
            Kind = ToolKind.Write)]
        public static string Rebuild()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureLibraryTextureImporters();
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("Built-in Standard shader is unavailable.");

            Texture2D baseColor = LoadPbrTexture("BaseColor");
            Texture2D metallicSmoothness = LoadPbrTexture("MetallicSmoothness");
            Texture2D normal = LoadPbrTexture("Normal");
            Texture2D ao = LoadPbrTexture("AO");
            if (baseColor == null || metallicSmoothness == null || normal == null || ao == null)
            {
                throw new InvalidOperationException(
                    "Generate PCG assets first; BaseColor, Metallic/Roughness, Normal and AO maps are required.");
            }

            foreach (SurfaceSpec spec in Specs)
            {
                Texture2D materialBaseColor =
                    LoadLibraryTexture(spec.FileName, "BaseColor") ?? baseColor;
                Texture2D materialMetallicSmoothness =
                    LoadLibraryTexture(spec.FileName, "MetallicSmoothness") ?? metallicSmoothness;
                Texture2D materialNormal =
                    LoadLibraryTexture(spec.FileName, "Normal") ?? normal;
                Texture2D materialAo =
                    LoadLibraryTexture(spec.FileName, "AO") ?? ao;
                bool usesLibrarySet = materialBaseColor != baseColor;
                string materialPath = MaterialRoot + "/" + spec.FileName + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, materialPath);
                }

                material.name = spec.DisplayName;
                material.shader = shader;
                material.SetColor("_Color", usesLibrarySet ? Color.white : spec.Tint);
                BindTiledTexture(material, "_MainTex", materialBaseColor, spec.Tiling);

                material.SetFloat("_Metallic", spec.Metallic);
                material.SetFloat("_Glossiness", spec.Smoothness);
                material.SetFloat("_GlossMapScale", spec.Smoothness);
                BindTiledTexture(
                    material,
                    "_MetallicGlossMap",
                    materialMetallicSmoothness,
                    spec.Tiling);
                material.EnableKeyword("_METALLICGLOSSMAP");

                BindTiledTexture(material, "_BumpMap", materialNormal, spec.Tiling);
                material.SetFloat("_BumpScale", spec.NormalStrength);
                material.EnableKeyword("_NORMALMAP");

                BindTiledTexture(material, "_OcclusionMap", materialAo, spec.Tiling);
                material.SetFloat("_OcclusionStrength", 1f);
                material.SetFloat("_SpecularHighlights", 1f);
                material.SetFloat("_GlossyReflections", 1f);
                material.doubleSidedGI = true;
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            const string message =
                "Rebuilt 8 texture-driven PBR spacecraft metal surfaces without changing existing material IDs.";
            Debug.Log(message);
            return message;
        }

        static Texture2D LoadPbrTexture(string suffix)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(
                PbrTextureRoot + "/IndustrialHull_" + suffix + ".png");
        }

        static Texture2D LoadLibraryTexture(string materialId, string suffix)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(
                LibraryTextureRoot + "/" + materialId + "_" + suffix + ".png");
        }

        static void ConfigureLibraryTextureImporters()
        {
            foreach (string path in AssetDatabase.FindAssets(
                         "t:Texture2D",
                         new[] { LibraryTextureRoot })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool normal = name.EndsWith("_Normal", StringComparison.Ordinal);
                bool color = name.EndsWith("_BaseColor", StringComparison.Ordinal);
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = color;
                importer.alphaSource = name.EndsWith(
                    "_MetallicSmoothness",
                    StringComparison.Ordinal)
                    ? TextureImporterAlphaSource.FromInput
                    : TextureImporterAlphaSource.None;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 8;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        static void BindTiledTexture(
            Material material,
            string property,
            Texture texture,
            float tiling)
        {
            material.SetTexture(property, texture);
            material.SetTextureScale(property, Vector2.one * tiling);
            material.SetTextureOffset(property, Vector2.zero);
        }
    }
}
