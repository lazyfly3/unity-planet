using System;
using System.Collections.Generic;
using UnityEngine;

public enum NmsOrganicMaterialKind
{
    Skin,
    Scale,
    Fur,
    Horn,
    Bone,
    Eye,
    Mouth,
    Flesh,
    Armor,
    Mechanical,
    Emissive,
    Generic
}

[Serializable]
public sealed class NmsOrganicTextureSet
{
    [SerializeField] string textureSetId;
    [SerializeField] string displayName;
    [SerializeField] NmsOrganicMaterialKind kind = NmsOrganicMaterialKind.Generic;
    [SerializeField] Texture2D baseColor;
    [SerializeField] Texture2D normal;
    [SerializeField] Texture2D mask;
    [SerializeField] Texture2D emission;
    [SerializeField, Range(0f, 1f)] float paletteStrength = 0.35f;
    [SerializeField, Range(0f, 2f)] float normalStrength = 1f;
    [SerializeField, Range(0f, 3f)] float emissionStrength = 0f;

    public string TextureSetId => textureSetId;
    public string DisplayName => displayName;
    public NmsOrganicMaterialKind Kind => kind;
    public Texture2D BaseColor => baseColor;
    public Texture2D Normal => normal;
    public Texture2D Mask => mask;
    public Texture2D Emission => emission;
    public float PaletteStrength => paletteStrength;
    public float NormalStrength => normalStrength;
    public float EmissionStrength => emissionStrength;

#if UNITY_EDITOR
    public void Configure(
        string id,
        string label,
        NmsOrganicMaterialKind materialKind,
        Texture2D baseColorTexture,
        Texture2D normalTexture,
        Texture2D maskTexture,
        Texture2D emissionTexture,
        float palette,
        float normalPower,
        float emissionPower)
    {
        textureSetId = id;
        displayName = label;
        kind = materialKind;
        baseColor = baseColorTexture;
        normal = normalTexture;
        mask = maskTexture;
        emission = emissionTexture;
        paletteStrength = Mathf.Clamp01(palette);
        normalStrength = Mathf.Max(0f, normalPower);
        emissionStrength = Mathf.Max(0f, emissionPower);
    }
#endif
}

[CreateAssetMenu(menuName = "Creatures/NMS Organic Material Catalog", fileName = "NmsOrganicMaterialCatalog")]
public sealed class NmsOrganicMaterialCatalog : ScriptableObject
{
    [SerializeField] NmsOrganicTextureSet[] textureSets = Array.Empty<NmsOrganicTextureSet>();
    [SerializeField] int revision;

    public IReadOnlyList<NmsOrganicTextureSet> TextureSets => textureSets;
    public int Revision => revision;

    public bool TrySelect(
        string materialPreset,
        int seed,
        int salt,
        out NmsOrganicTextureSet textureSet)
    {
        NmsOrganicMaterialKind preferredKind = ResolveKind(materialPreset);
        int preferredCount = 0;
        for (int i = 0; i < textureSets.Length; i++)
            if (textureSets[i] != null && textureSets[i].Kind == preferredKind)
                preferredCount++;

        if (preferredCount > 0)
        {
            int index = PositiveHash(seed, salt, (int)preferredKind) % preferredCount;
            for (int i = 0; i < textureSets.Length; i++)
            {
                if (textureSets[i] == null || textureSets[i].Kind != preferredKind)
                    continue;
                if (index-- == 0)
                {
                    textureSet = textureSets[i];
                    return true;
                }
            }
        }

        int usableCount = 0;
        for (int i = 0; i < textureSets.Length; i++)
            if (textureSets[i] != null)
                usableCount++;
        if (usableCount == 0)
        {
            textureSet = null;
            return false;
        }

        int fallbackIndex = PositiveHash(seed, salt, 17) % usableCount;
        for (int i = 0; i < textureSets.Length; i++)
        {
            if (textureSets[i] == null)
                continue;
            if (fallbackIndex-- == 0)
            {
                textureSet = textureSets[i];
                return true;
            }
        }

        textureSet = null;
        return false;
    }

    static NmsOrganicMaterialKind ResolveKind(string materialPreset)
    {
        if (string.IsNullOrEmpty(materialPreset))
            return NmsOrganicMaterialKind.Generic;
        if (Contains(materialPreset, "fur") || Contains(materialPreset, "hair"))
            return NmsOrganicMaterialKind.Fur;
        if (Contains(materialPreset, "scale") || Contains(materialPreset, "reptile")
            || Contains(materialPreset, "lizard") || Contains(materialPreset, "snake")
            || Contains(materialPreset, "fish"))
            return NmsOrganicMaterialKind.Scale;
        if (Contains(materialPreset, "horn") || Contains(materialPreset, "keratin"))
            return NmsOrganicMaterialKind.Horn;
        if (Contains(materialPreset, "bone") || Contains(materialPreset, "teeth"))
            return NmsOrganicMaterialKind.Bone;
        if (Contains(materialPreset, "eye"))
            return NmsOrganicMaterialKind.Eye;
        if (Contains(materialPreset, "mouth") || Contains(materialPreset, "jaw")
            || Contains(materialPreset, "tongue") || Contains(materialPreset, "guts"))
            return NmsOrganicMaterialKind.Mouth;
        if (Contains(materialPreset, "flesh") || Contains(materialPreset, "wound")
            || Contains(materialPreset, "muscle"))
            return NmsOrganicMaterialKind.Flesh;
        if (Contains(materialPreset, "armor") || Contains(materialPreset, "shell")
            || Contains(materialPreset, "plate") || Contains(materialPreset, "carapace"))
            return NmsOrganicMaterialKind.Armor;
        if (Contains(materialPreset, "robot") || Contains(materialPreset, "metal")
            || Contains(materialPreset, "mechanical"))
            return NmsOrganicMaterialKind.Mechanical;
        if (Contains(materialPreset, "glow") || Contains(materialPreset, "emissive"))
            return NmsOrganicMaterialKind.Emissive;
        if (Contains(materialPreset, "skin"))
            return NmsOrganicMaterialKind.Skin;
        return NmsOrganicMaterialKind.Generic;
    }

    static bool Contains(string value, string token)
    {
        return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static int PositiveHash(int a, int b, int c)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)a) * 16777619u;
            hash = (hash ^ (uint)b) * 16777619u;
            hash = (hash ^ (uint)c) * 16777619u;
            return (int)(hash & 0x7fffffffu);
        }
    }

#if UNITY_EDITOR
    public void ConfigureImported(NmsOrganicTextureSet[] importedTextureSets)
    {
        textureSets = importedTextureSets ?? Array.Empty<NmsOrganicTextureSet>();
        revision++;
    }
#endif
}
