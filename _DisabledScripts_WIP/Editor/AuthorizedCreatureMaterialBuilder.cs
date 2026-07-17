using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

static class AuthorizedCreatureMaterialBuilder
{
    const int BuildRevision = 2;
    const string PrefabPath = "Assets/Creatures/Authorized/NMS/Prefabs/AntelopeVariant_0042.prefab";
    const string TextureDirectory = "Assets/Creatures/Authorized/NMS/Textures";
    const string MaterialDirectory = "Assets/Creatures/Authorized/NMS/Materials";

    sealed class Definition
    {
        public string materialName;
        public string albedo;
        public string normal;
        public bool cutout;
        public bool emission;
        public float smoothness;
    }

    [DidReloadScripts]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Definition[] definitions =
        {
            New("Eye_mat", "SPIDEREYE.BASE.1.DDS", "SPIDEREYE.BASE.1.NORMAL.DDS", false, false, .72f),
            New("AntelopeBodyMat", "ANTELOPEBODY.BASE.FUR.DDS", "ANTELOPEBODY.BASE.FUR.NORMAL.DDS", false, false, .24f),
            New("SpikeFinNew_mat", "SPIKEFINNEW.BASE.DDS", "SPIKEFINNEW.BASE.NORMAL.DDS", true, false, .32f),
            New("Spores_mat", "SPORES.BASE.DDS", "SPORES.BASE.NORMAL.DDS", true, false, .36f),
            New("SporesGlow_mat", "SPORES.BASE.DDS", "SPORES.BASE.NORMAL.DDS", true, true, .42f),
            New("AntelopeHeadMat", "ANTELOPEHEAD.BASE.FUR.DDS", "ANTELOPEHEAD.BASE.FUR.NORMAL.DDS", false, false, .26f),
            New("Gums_mat", "GUMS.BASEG.1.DDS", "GUMS.BASEG.1.NORMAL.DDS", false, false, .38f),
            New("FatTailMat", "FATTAIL.BASE.FUR.DDS", "FATTAIL.BASE.FUR.NORMAL.DDS", false, false, .24f),
            New("PetHarnessMat3", "PETHARNESS.BASE.DDS", "PETHARNESS.BASE.NORMAL.DDS", false, false, .56f),
            New("SpikeFinSolid_mat", "SPIKEFINSOLID.BASE.DDS", "SUPPORTSPIKES.BASE.BONE.NORMAL.DDS", false, false, .38f)
        };

        Directory.CreateDirectory(MaterialDirectory);
        var replacements = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < definitions.Length; i++)
        {
            Definition definition = definitions[i];
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureDirectory}/{definition.albedo}");
            Texture2D normal = ConfigureNormalTexture(definition.normal);
            if (albedo == null)
            {
                Debug.LogError($"Authorized creature albedo texture is missing: {definition.albedo}");
                continue;
            }

            string materialPath = $"{MaterialDirectory}/{definition.materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard")) { name = definition.materialName };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = Shader.Find("Standard");
            material.SetColor("_Color", Color.white);
            material.SetTexture("_MainTex", albedo);
            material.SetFloat("_Glossiness", definition.smoothness);
            material.SetFloat("_Metallic", definition.materialName == "PetHarnessMat3" ? .28f : 0f);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            ConfigureCutout(material, definition.cutout);
            if (definition.emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(.08f, .7f, .85f, 1f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(material);
            replacements[definition.materialName] = material;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        int replacedSlots = 0;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].sharedMaterials;
            bool changed = false;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                string sourceName = materials[slot] != null ? materials[slot].name : string.Empty;
                if (!replacements.TryGetValue(sourceName, out Material replacement))
                    continue;
                materials[slot] = replacement;
                replacedSlots++;
                changed = true;
            }
            if (changed)
                renderers[i].sharedMaterials = materials;
        }
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
        Debug.Log($"Authorized creature materials revision {BuildRevision} created; replaced {replacedSlots} renderer slots.");
    }

    static Definition New(string name, string albedo, string normal, bool cutout, bool emission, float smoothness)
    {
        return new Definition
        {
            materialName = name,
            albedo = albedo,
            normal = normal,
            cutout = cutout,
            emission = emission,
            smoothness = smoothness
        };
    }

    static Texture2D ConfigureNormalTexture(string fileName)
    {
        string path = $"{TextureDirectory}/{fileName}";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void ConfigureCutout(Material material, bool cutout)
    {
        if (!cutout)
        {
            material.SetFloat("_Mode", 0f);
            material.SetOverrideTag("RenderType", string.Empty);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = -1;
            return;
        }

        material.SetFloat("_Mode", 1f);
        material.SetFloat("_Cutoff", .35f);
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }
}
