using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "PlanetLabPlanarRuntimeAssets",
    menuName = "Voxel Planet/Planet Lab Planar Runtime Assets")]
public sealed class PlanetLabPlanarRuntimeAssets : ScriptableObject
{
    public PlanetLabTerrainPbrLibrary terrainPbrLibrary;
    public PlanetLabSkyboxLibrary skyboxLibrary;
}

public static class PlanetLabPlanarMaterialFactory
{
    public static ProceduralPlanetLabTemplate ResolveTemplate(
        PlanetClimate climate,
        bool oceanEnabled)
    {
        return climate switch
        {
            PlanetClimate.Desert => ProceduralPlanetLabTemplate.Desert,
            PlanetClimate.Tundra => ProceduralPlanetLabTemplate.Frozen,
            PlanetClimate.Volcanic =>
                ProceduralPlanetLabTemplate.CrimsonOcean,
            PlanetClimate.Crystal => ProceduralPlanetLabTemplate.Crystal,
            PlanetClimate.TemperateForest =>
                ProceduralPlanetLabTemplate.TemperateOcean,
            PlanetClimate.Tropical =>
                ProceduralPlanetLabTemplate.TemperateOcean,
            _ => oceanEnabled
                ? ProceduralPlanetLabTemplate.CrimsonOcean
                : ProceduralPlanetLabTemplate.Desert
        };
    }

    public static Material CreateTerrainMaterial(
        GalaxyPlanetDefinition definition,
        PlanetLabTerrainPbrLibrary library,
        ProceduralPlanetLabTemplate template)
    {
        Shader shader = Shader.Find("VoxelPlanet/PlanetLabPlanarSurface");
        if (shader == null)
            throw new InvalidOperationException(
                "Missing shader: VoxelPlanet/PlanetLabPlanarSurface");
        var material = new Material(shader)
        {
            name = "PlanetSurface_PlanarTerrain",
            hideFlags = HideFlags.DontSave
        };
        ApplyTerrainProperties(material, definition, library, template);
        return material;
    }

    public static Material CreateOceanMaterial(
        GalaxyPlanetDefinition definition)
    {
        Shader shader = Shader.Find("VoxelPlanet/PlanetLabPlanarOcean");
        if (shader == null)
            throw new InvalidOperationException(
                "Missing shader: VoxelPlanet/PlanetLabPlanarOcean");
        var material = new Material(shader)
        {
            name = "PlanetSurface_PlanarOcean",
            hideFlags = HideFlags.DontSave
        };
        PlanetLowPolyVisualProfile visual =
            definition.lowPolyVisual ?? new PlanetLowPolyVisualProfile();
        material.SetColor("_DeepColor", visual.deepOceanColor);
        material.SetColor("_ShallowColor", visual.shallowOceanColor);
        material.SetFloat("_WaveStrength", visual.oceanWaveStrength);
        material.SetFloat("_WaveScale", visual.oceanWaveScale);
        material.SetFloat("_WaveSpeed", visual.oceanWaveSpeed);
        material.SetFloat("_Opacity", visual.oceanOpacity);
        return material;
    }

    public static Material SelectSkybox(
        GalaxyPlanetDefinition definition,
        PlanetLabSkyboxLibrary library,
        ProceduralPlanetLabTemplate template)
    {
        return library != null
            ? library.SelectMaterial(definition.seed, template)
            : null;
    }

    static void ApplyTerrainProperties(
        Material material,
        GalaxyPlanetDefinition definition,
        PlanetLabTerrainPbrLibrary library,
        ProceduralPlanetLabTemplate template)
    {
        PlanetLowPolyVisualProfile visual =
            definition.lowPolyVisual ?? new PlanetLowPolyVisualProfile();
        PlanetCelestialProfile celestial =
            definition.celestial
            ?? PlanetCelestialProfile.CreateCompatibleDefault();
        float heightScale = Mathf.Max(
            1f,
            celestial.maximumTerrainElevation);
        material.SetColor("_LowlandColor", visual.lowlandColor);
        material.SetColor("_HighlandColor", visual.highlandColor);
        material.SetColor("_CliffColor", visual.cliffColor);
        material.SetColor("_RockColor", visual.rockColor);
        material.SetColor("_AccentColor", visual.accentColor);
        material.SetColor("_ShoreColor", visual.shoreColor);
        material.SetColor("_SnowColor", visual.snowColor);
        material.SetFloat("_FacetStrength", visual.facetStrength);
        material.SetFloat("_LightingBands", visual.lightingBands);
        material.SetFloat("_MacroColorSize", visual.macroColorSize);
        material.SetFloat("_MacroVariation", visual.macroVariation);
        material.SetFloat("_CliffSlope", visual.cliffSlope);
        material.SetFloat("_HeightScale", heightScale);
        material.SetFloat("_SeaHeight", visual.oceanLevel * heightScale);
        material.SetFloat("_ShoreWidth", visual.shoreWidth * heightScale);
        material.SetFloat("_SnowLine", visual.snowLine);
        material.SetFloat("_SnowAmount", visual.snowAmount);
        material.SetFloat("_MinimumAmbient", 0.16f);

        bool ready = library != null && library.IsReady;
        material.SetFloat("_UsePbrLibrary", ready ? 1f : 0f);
        if (!ready)
            return;
        material.SetTexture("_PbrAlbedoArray", library.albedoArray);
        material.SetTexture("_PbrNormalArray", library.normalArray);
        material.SetTexture("_PbrMaskArray", library.maskArray);
        material.SetFloat(
            "_GroundSlice",
            library.SelectSlice(
                definition.seed,
                PlanetLabTerrainPbrRole.Ground,
                101));
        material.SetFloat(
            "_RockSlice",
            library.SelectSlice(
                definition.seed,
                PlanetLabTerrainPbrRole.Rock,
                211));
        material.SetFloat(
            "_ShoreSlice",
            library.SelectSlice(
                definition.seed,
                PlanetLabTerrainPbrRole.Shore,
                307));
        material.SetFloat(
            "_ColdSlice",
            library.SelectSlice(
                definition.seed,
                PlanetLabTerrainPbrRole.Cold,
                401));
        material.SetFloat("_PbrTiling", 0.22f);
        material.SetFloat("_PbrNormalStrength", 0.52f);
        material.SetFloat(
            "_PbrColorStrength",
            template == ProceduralPlanetLabTemplate.Frozen ? 0.72f
                : template == ProceduralPlanetLabTemplate.Crystal ? 0.66f
                : template == ProceduralPlanetLabTemplate.CrimsonOcean ? 0.58f
                : 0.46f);
    }
}
