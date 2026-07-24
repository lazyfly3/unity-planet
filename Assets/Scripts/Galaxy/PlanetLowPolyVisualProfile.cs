using System;
using UnityEngine;

[Serializable]
public sealed class PlanetLowPolyVisualProfile
{
    public const int CurrentVersion = 3;

    public int visualVersion = CurrentVersion;

    [Header("Terrain Palette")]
    public Color lowlandColor = new Color(0.22f, 0.42f, 0.24f, 1f);
    public Color highlandColor = new Color(0.42f, 0.5f, 0.3f, 1f);
    public Color cliffColor = new Color(0.24f, 0.24f, 0.22f, 1f);
    public Color rockColor = new Color(0.32f, 0.3f, 0.28f, 1f);
    public Color accentColor = new Color(0.55f, 0.68f, 0.34f, 1f);
    [Range(0f, 1f)] public float facetStrength = 0.72f;
    [Range(3, 5)] public int lightingBands = 4;
    [Min(1f)] public float macroColorSize = 18f;
    [Range(0.05f, 0.95f)] public float cliffSlope = 0.58f;
    [Range(0f, 1f)] public float macroVariation = 0.16f;

    [Header("Ocean And Height Biomes")]
    public bool oceanEnabled = true;
    public Color deepOceanColor = new Color(0.015f, 0.12f, 0.22f, 1f);
    public Color shallowOceanColor = new Color(0.03f, 0.52f, 0.62f, 1f);
    public Color shoreColor = new Color(0.72f, 0.68f, 0.42f, 1f);
    public Color snowColor = new Color(0.9f, 0.92f, 0.84f, 1f);
    [Range(-0.08f, 0.08f)] public float oceanLevel;
    [Range(0.002f, 0.08f)] public float shoreWidth = 0.018f;
    [Range(0.15f, 0.95f)] public float snowLine = 0.68f;
    [Range(0f, 1f)] public float snowAmount = 0.55f;
    [Range(0f, 1f)] public float oceanSmoothness = 0.86f;
    [Range(0f, 1f)] public float oceanWaveStrength = 0.32f;
    [Range(0.1f, 3f)] public float oceanWaveScale = 1f;
    [Range(0f, 3f)] public float oceanWaveSpeed = 1f;
    [Range(0f, 2f)] public float oceanNormalStrength = 0.85f;
    [Range(0f, 1f)] public float oceanFoamStrength = 0.62f;
    [Range(0f, 0.04f)] public float oceanRefractionStrength = 0.012f;
    [Range(0.35f, 1f)] public float oceanOpacity = 0.82f;

    [Header("Continuous Ecosystem Weights")]
    [Range(0f, 1f)] public float forestWeight;
    [Range(0f, 1f)] public float desertWeight;
    [Range(0f, 1f)] public float tundraWeight;
    [Range(0f, 1f)] public float volcanicWeight;
    [Range(0f, 1f)] public float crystalWeight;
    [Range(0f, 1f)] public float tropicalWeight;

    [Header("Decoration")]
    public int decorationSeed;
    [Range(0.35f, 1.8f)] public float decorationDensity = 1f;
    [Range(0.7f, 1.5f)] public float decorationScale = 1f;
    [Range(0f, 1f)] public float clusterStrength = 0.5f;
    [Range(0, 4)] public int landmarkStyle;

    [Header("Surface Sky And Clouds")]
    public Color horizonColor = new Color(0.42f, 0.68f, 0.9f, 1f);
    public Color zenithColor = new Color(0.08f, 0.22f, 0.42f, 1f);
    public Color sunsetColor = new Color(1f, 0.35f, 0.12f, 1f);
    public Color groundAmbientColor = new Color(0.08f, 0.09f, 0.08f, 1f);
    [Range(0f, 1f)] public float atmosphereThickness = 0.65f;
    [Range(0f, 1f)] public float cloudCoverage = 0.35f;
    [Range(0f, 1f)] public float hazeStrength = 0.22f;
    public int cloudSeed;

    public PlanetLowPolyVisualProfile Clone()
        => (PlanetLowPolyVisualProfile)MemberwiseClone();

    public void ClampValues()
    {
        if (visualVersion < 2)
            UpgradeHeightBiomes();
        if (visualVersion < 3)
            UpgradeAnimatedOcean();
        visualVersion = CurrentVersion;
        facetStrength = Mathf.Clamp01(facetStrength);
        lightingBands = Mathf.Clamp(lightingBands, 3, 5);
        macroColorSize = Mathf.Max(1f, macroColorSize);
        cliffSlope = Mathf.Clamp(cliffSlope, 0.05f, 0.95f);
        macroVariation = Mathf.Clamp01(macroVariation);
        oceanLevel = Mathf.Clamp(oceanLevel, -0.08f, 0.08f);
        shoreWidth = Mathf.Clamp(shoreWidth, 0.002f, 0.08f);
        snowLine = Mathf.Clamp(snowLine, 0.15f, 0.95f);
        snowAmount = Mathf.Clamp01(snowAmount);
        oceanSmoothness = Mathf.Clamp01(oceanSmoothness);
        oceanWaveStrength = Mathf.Clamp01(oceanWaveStrength);
        oceanWaveScale = Mathf.Clamp(oceanWaveScale, 0.1f, 3f);
        oceanWaveSpeed = Mathf.Clamp(oceanWaveSpeed, 0f, 3f);
        oceanNormalStrength = Mathf.Clamp(oceanNormalStrength, 0f, 2f);
        oceanFoamStrength = Mathf.Clamp01(oceanFoamStrength);
        oceanRefractionStrength = Mathf.Clamp(
            oceanRefractionStrength,
            0f,
            0.04f);
        oceanOpacity = Mathf.Clamp(oceanOpacity, 0.35f, 1f);
        forestWeight = Mathf.Clamp01(forestWeight);
        desertWeight = Mathf.Clamp01(desertWeight);
        tundraWeight = Mathf.Clamp01(tundraWeight);
        volcanicWeight = Mathf.Clamp01(volcanicWeight);
        crystalWeight = Mathf.Clamp01(crystalWeight);
        tropicalWeight = Mathf.Clamp01(tropicalWeight);
        decorationDensity = Mathf.Clamp(decorationDensity, 0.35f, 1.8f);
        decorationScale = Mathf.Clamp(decorationScale, 0.7f, 1.5f);
        clusterStrength = Mathf.Clamp01(clusterStrength);
        landmarkStyle = Mathf.Clamp(landmarkStyle, 0, 4);
        atmosphereThickness = Mathf.Clamp01(atmosphereThickness);
        cloudCoverage = Mathf.Clamp01(cloudCoverage);
        hazeStrength = Mathf.Clamp01(hazeStrength);
    }

    void UpgradeHeightBiomes()
    {
        oceanEnabled = atmosphereThickness > 0.14f
            && volcanicWeight < 0.92f;
        deepOceanColor = Color.Lerp(zenithColor, Color.black, 0.52f);
        shallowOceanColor = Color.Lerp(horizonColor, zenithColor, 0.35f);
        shoreColor = Color.Lerp(lowlandColor, new Color(0.82f, 0.72f, 0.47f, 1f), 0.58f);
        snowColor = Color.Lerp(Color.white, highlandColor, 0.12f);
        snowLine = Mathf.Lerp(0.42f, 0.84f, Mathf.Clamp01(
            tropicalWeight + desertWeight * 0.65f + volcanicWeight * 0.45f));
        snowAmount = Mathf.Clamp01(tundraWeight * 0.75f + (1f - snowLine) * 0.45f);
        oceanSmoothness = 0.86f;
        oceanWaveStrength = 0.32f;
        visualVersion = 2;
    }

    void UpgradeAnimatedOcean()
    {
        oceanWaveScale = 1f;
        oceanWaveSpeed = 1f;
        oceanNormalStrength = 0.85f;
        oceanFoamStrength = 0.62f;
        oceanRefractionStrength = 0.012f;
        oceanOpacity = 0.82f;
        visualVersion = 3;
    }
}
