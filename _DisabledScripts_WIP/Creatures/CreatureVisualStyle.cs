using System;
using System.Collections.Generic;
using UnityEngine;

public enum CreatureVisualStyle
{
    ClayModular,
    NaturalSciFi
}

[Serializable]
public sealed class CreatureSkinProfile
{
    public Color baseColor = Color.white;
    public Color patternColor = new Color(.24f, .32f, .38f, 1f);
    [Range(0f, 1f)] public float patternStrength = .45f;
    [Range(.25f, 12f)] public float patternScale = 2.5f;
    [Range(0f, 1f)] public float smoothness = .32f;
    [Range(0f, 1f)] public float metallic;
    [Range(0f, 1f)] public float normalStrength = .7f;
    public Color subsurfaceColor = new Color(.42f, .12f, .08f, 1f);
    [Range(0f, 1f)] public float subsurfaceStrength = .12f;
    [Range(0f, 1f)] public float fresnelStrength = .08f;
    public Color emissionColor = Color.black;
    [Range(0f, 3f)] public float emissionStrength;

    public CreatureSkinProfile Clone()
    {
return (CreatureSkinProfile)MemberwiseClone();
    
    
}
}

[Serializable]
public sealed class PlanetCreatureEcology
{
    public bool enabled = true;
    public CreatureVisualStyle primaryStyle = CreatureVisualStyle.ClayModular;
    [Range(0f, 1f)] public float alternateStyleChance = .15f;
    public int populationSeedOffset = 17041;
    public CreatureSkinProfile skin = new CreatureSkinProfile();

    public CreatureVisualStyle ResolveStyle(int planetSeed, int creatureSeed)
    {
uint hash = StableHash(unchecked((uint)(planetSeed ^ populationSeedOffset)), unchecked((uint)creatureSeed));
        float value = (hash >> 8) * (1f / 16777216f);
        if (value >= alternateStyleChance)
            return primaryStyle;
        return primaryStyle == CreatureVisualStyle.ClayModular
            ? CreatureVisualStyle.NaturalSciFi
            : CreatureVisualStyle.ClayModular;
    
    
}

    public static PlanetCreatureEcology CreateProcedural(
        int planetSeed,
        float temperature,
        float moisture,
        float geology,
        float atmosphere,
        float crystal,
        Color surfaceColor)
    {
float naturalScore = atmosphere * .32f + geology * .24f + crystal * .28f + moisture * .16f;
        bool naturalPrimary = naturalScore >= .48f;
        Color pattern = Color.Lerp(surfaceColor, crystal > .58f
            ? new Color(.18f, .72f, .9f, 1f)
            : new Color(.08f, .12f, .1f, 1f), .55f);
        return new PlanetCreatureEcology
        {
            primaryStyle = naturalPrimary ? CreatureVisualStyle.NaturalSciFi : CreatureVisualStyle.ClayModular,
            alternateStyleChance = Mathf.Lerp(.08f, .28f, 1f - Mathf.Abs(naturalScore - .5f) * 2f),
            populationSeedOffset = unchecked(planetSeed * 397) ^ 17041,
            skin = new CreatureSkinProfile
            {
                baseColor = Color.Lerp(surfaceColor, Color.white, .18f),
                patternColor = pattern,
                patternStrength = Mathf.Lerp(.28f, .78f, Mathf.Max(geology, crystal)),
                patternScale = Mathf.Lerp(1.2f, 5.5f, moisture),
                smoothness = Mathf.Lerp(.18f, .58f, moisture * atmosphere),
                metallic = Mathf.Lerp(0f, .32f, crystal * geology),
                normalStrength = Mathf.Lerp(.35f, 1f, geology),
                subsurfaceColor = Color.Lerp(new Color(.36f, .08f, .04f, 1f), surfaceColor, .35f),
                subsurfaceStrength = Mathf.Lerp(.06f, .28f, atmosphere),
                fresnelStrength = Mathf.Lerp(.04f, .24f, moisture),
                emissionColor = crystal > .72f ? Color.Lerp(pattern, Color.white, .25f) : Color.black,
                emissionStrength = crystal > .72f ? Mathf.Lerp(.05f, .55f, crystal) : 0f
            }
        };
    
    
}

    static uint StableHash(uint a, uint b)
    {
        uint value = a + 0x9E3779B9u;
        value ^= b + 0x85EBCA6Bu + (value << 6) + (value >> 2);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }
}

[Serializable]
public sealed class CreatureVisualAssetEntry
{
    public string stableId;
    public CreatureVisualStyle style;
    public GameObject prefab;
    public CreatureTopology[] compatibleTopologies;
    public Vector2 scaleRange = new Vector2(.9f, 1.1f);
    public string sourceManifestId;

    public bool Supports(CreatureTopology topology)
    {
if (compatibleTopologies == null || compatibleTopologies.Length == 0)
            return true;
        for (int i = 0; i < compatibleTopologies.Length; i++)
            if (compatibleTopologies[i] == topology)
                return true;
        return false;
    
    
}
}

[CreateAssetMenu(menuName = "Voxel Planet/Creatures/Visual Asset Catalog")]
public sealed class CreatureVisualAssetCatalog : ScriptableObject
{
    [SerializeField] List<CreatureVisualAssetEntry> entries = new List<CreatureVisualAssetEntry>();

    public CreatureVisualAssetEntry Find(string stableId)
    {
if (string.IsNullOrWhiteSpace(stableId))
            return null;
        for (int i = 0; i < entries.Count; i++)
        {
            CreatureVisualAssetEntry entry = entries[i];
            if (entry != null && string.Equals(entry.stableId, stableId, StringComparison.Ordinal))
                return entry;
        }
        return null;
    
    
}
}

public static class CreatureVisualStyleResolver
{
    public static void Apply(CreatureGenome genome, GalaxyPlanetDefinition planet)
    {
if (genome == null || planet == null || planet.creatureEcology == null || !planet.creatureEcology.enabled)
            return;
        genome.visualStyle = planet.creatureEcology.ResolveStyle(planet.seed, genome.seed);
        genome.skinProfile = planet.creatureEcology.skin != null
            ? planet.creatureEcology.skin.Clone()
            : new CreatureSkinProfile();
    
    
}
}

public static class CreatureSkinMaterialBinder
{
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int PatternColorId = Shader.PropertyToID("_PatternColor");
    static readonly int PatternStrengthId = Shader.PropertyToID("_PatternStrength");
    static readonly int PatternScaleId = Shader.PropertyToID("_PatternScale");
    static readonly int SmoothnessId = Shader.PropertyToID("_Glossiness");
    static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    static readonly int NormalStrengthId = Shader.PropertyToID("_NormalStrength");
    static readonly int SubsurfaceColorId = Shader.PropertyToID("_SubsurfaceColor");
    static readonly int SubsurfaceStrengthId = Shader.PropertyToID("_SubsurfaceStrength");
    static readonly int FresnelStrengthId = Shader.PropertyToID("_FresnelStrength");
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
    static readonly int UseTriplanarId = Shader.PropertyToID("_UseTriplanar");

    public static void Apply(GameObject root, CreatureGenome genome)
    {
if (root == null || genome == null)
            return;
        CreatureSkinProfile profile = genome.skinProfile ?? new CreatureSkinProfile
        {
            baseColor = genome.primaryColor,
            patternColor = genome.secondaryColor
        };
        if (profile.baseColor == default)
            profile.baseColor = genome.primaryColor;

        var block = new MaterialPropertyBlock();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.GetPropertyBlock(block);
            block.SetColor(ColorId, profile.baseColor);
            block.SetColor(PatternColorId, profile.patternColor);
            block.SetFloat(PatternStrengthId, profile.patternStrength);
            block.SetFloat(PatternScaleId, profile.patternScale);
            block.SetFloat(SmoothnessId, profile.smoothness);
            block.SetFloat(MetallicId, profile.metallic);
            block.SetFloat(NormalStrengthId, profile.normalStrength);
            block.SetColor(SubsurfaceColorId, profile.subsurfaceColor);
            block.SetFloat(SubsurfaceStrengthId, profile.subsurfaceStrength);
            block.SetFloat(FresnelStrengthId, profile.fresnelStrength);
            block.SetColor(EmissionColorId, profile.emissionColor);
            block.SetFloat(EmissionStrengthId, profile.emissionStrength);
            block.SetFloat(UseTriplanarId, renderer.gameObject.name == "SkinnedTorso" ? 1f : 0f);
            renderer.SetPropertyBlock(block);
            block.Clear();
        }
    
    
}
}
