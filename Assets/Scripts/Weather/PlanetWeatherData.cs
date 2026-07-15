using System;
using System.Collections.Generic;
using UnityEngine;

public enum WeatherType
{
    Clear,
    Overcast,
    ThinDust,
    MeteorDust,
    Rain,
    Thunderstorm,
    MorningFog,
    Ashfall,
    Sandstorm,
    DryThunderstorm,
    HeavyRain,
    OceanStorm,
    DenseFog,
    CrystalDust,
    IonStorm,
    VioletFog
}

[Serializable]
public sealed class WeatherPreset
{
    public WeatherType type;
    public string displayName = "晴朗";
    [Min(0.01f)] public float weight = 1f;
    [Min(10f)] public float minimumDuration = 180f;
    [Min(10f)] public float maximumDuration = 420f;
    [Range(0f, 1f)] public float minimumIntensity = 0.55f;
    [Range(0f, 1f)] public float maximumIntensity = 1f;
    [Min(0f)] public float windSpeed = 1f;
    [Min(0f)] public float precipitation;
    [Range(0f, 0.08f)] public float fogDensity;
    [Range(0f, 1f)] public float cloudCoverage;
    [Range(0.05f, 1.5f)] public float lightMultiplier = 1f;
    [Range(0f, 1f)] public float wetness;
    [Range(0f, 1f)] public float dustCoverage;
    [Range(0f, 1f)] public float crystalCoverage;
    [Range(0.35f, 1f)] public float tractionMultiplier = 1f;
    [Min(0f)] public float riverRainfallRate;
    [Range(0, 5000)] public int particleCount;
    public bool lightning;
    public Color fogColor = new Color(0.5f, 0.55f, 0.6f, 1f);
    public Color particleColor = Color.white;
    public Color ambientTint = Color.white;

    public void ClampValues()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(163);}
    try
    {
        weight = Mathf.Max(0.01f, weight);
        minimumDuration = Mathf.Max(10f, minimumDuration);
        maximumDuration = Mathf.Max(minimumDuration, maximumDuration);
        minimumIntensity = Mathf.Clamp01(minimumIntensity);
        maximumIntensity = Mathf.Clamp(maximumIntensity, minimumIntensity, 1f);
        windSpeed = Mathf.Max(0f, windSpeed);
        precipitation = Mathf.Max(0f, precipitation);
        riverRainfallRate = Mathf.Max(0f, riverRainfallRate);
        particleCount = Mathf.Clamp(particleCount, 0, 5000);
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}

[Serializable]
public sealed class PlanetWeatherSettings
{
    public bool enabled = true;
    public int seedOffset = 91573;
    [Min(0f)] public float transitionDuration = 20f;
    public List<WeatherPreset> presets = new List<WeatherPreset>();

    public void ClampValues()
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(164);}
    try
    {
        transitionDuration = Mathf.Max(0f, transitionDuration);
        if (presets == null)
            presets = new List<WeatherPreset>();
        foreach (WeatherPreset preset in presets)
            preset?.ClampValues();
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}
}

public struct WeatherSnapshot
{
    public WeatherType type;
    public WeatherType nextType;
    public string displayName;
    public string nextDisplayName;
    public float intensity;
    public float remainingSeconds;
    public float windSpeed;
    public Vector3 windVelocity;
    public float precipitation;
    public float groundTractionMultiplier;
    public float wetness;
    public float dustCoverage;
    public float crystalCoverage;
}

public static class PlanetWeatherDefaults
{
    public static PlanetWeatherSettings Create(string planetId)
    {bool __logTrackDepthEntered = FSPDebuger.EnableLogTrackInternal;
    if(__logTrackDepthEntered){FSPDebuger.PushDepth();FSPDebuger.LogTrack(165);}
    try
    {
        var settings = new PlanetWeatherSettings();
        switch (planetId)
        {
            case "verdant":
                settings.presets.Add(Preset(WeatherType.Clear, "晴朗", 3f, 240f, 420f, 2f));
                settings.presets.Add(Preset(WeatherType.Overcast, "阴天", 2f, 180f, 360f, 4f, fog: 0.006f, cloud: 0.75f, light: 0.72f));
                settings.presets.Add(Preset(WeatherType.Rain, "温带降雨", 3f, 180f, 360f, 6f, 0.65f, 0.012f, 0.88f, 0.62f, 0.85f, 1.8f, 1800));
                settings.presets.Add(Preset(WeatherType.Thunderstorm, "雷暴", 1f, 60f, 180f, 12f, 1f, 0.025f, 1f, 0.38f, 0.72f, 4.2f, 3200, true));
                settings.presets.Add(Preset(WeatherType.MorningFog, "晨雾", 1f, 120f, 240f, 1.5f, fog: 0.035f, cloud: 0.45f, light: 0.68f));
                break;
            case "crimson":
                settings.presets.Add(Preset(WeatherType.Clear, "灼热晴空", 3f, 240f, 420f, 3f));
                settings.presets.Add(Preset(WeatherType.Ashfall, "火山灰", 2.5f, 150f, 300f, 7f, cloud: 0.65f, fog: 0.018f, light: 0.6f, particles: 1600, dust: 0.45f));
                settings.presets.Add(Preset(WeatherType.Sandstorm, "赤砂风暴", 3.5f, 90f, 210f, 16f, fog: 0.045f, cloud: 0.85f, light: 0.4f, traction: 0.7f, particles: 3600, dust: 0.85f));
                settings.presets.Add(Preset(WeatherType.DryThunderstorm, "干雷暴", 1f, 60f, 150f, 14f, fog: 0.018f, cloud: 0.92f, light: 0.45f, particles: 800, lightning: true));
                break;
            case "azure":
                settings.presets.Add(Preset(WeatherType.Overcast, "海洋阴云", 2f, 180f, 360f, 5f, fog: 0.008f, cloud: 0.8f, light: 0.72f));
                settings.presets.Add(Preset(WeatherType.HeavyRain, "强降雨", 3.5f, 150f, 330f, 9f, 1f, 0.02f, 0.95f, 0.52f, 0.75f, 4.5f, 3000));
                settings.presets.Add(Preset(WeatherType.OceanStorm, "海洋风暴", 3f, 70f, 180f, 18f, 1f, 0.035f, 1f, 0.32f, 0.62f, 8f, 4400, true));
                settings.presets.Add(Preset(WeatherType.DenseFog, "海雾", 1.5f, 120f, 260f, 3f, fog: 0.05f, cloud: 0.7f, light: 0.58f));
                break;
            case "violet":
                settings.presets.Add(Preset(WeatherType.Clear, "寂静星空", 3f, 240f, 420f, 2f));
                settings.presets.Add(Preset(WeatherType.CrystalDust, "紫晶尘", 3.5f, 150f, 330f, 8f, fog: 0.014f, cloud: 0.55f, light: 0.72f, particles: 2200, crystal: 0.7f));
                settings.presets.Add(Preset(WeatherType.IonStorm, "离子风暴", 2.5f, 70f, 180f, 15f, fog: 0.025f, cloud: 1f, light: 0.45f, traction: 0.78f, particles: 3600, crystal: 0.4f, lightning: true));
                settings.presets.Add(Preset(WeatherType.VioletFog, "紫雾", 1f, 120f, 260f, 2f, fog: 0.048f, cloud: 0.5f, light: 0.55f));
                break;
            default:
                settings.presets.Add(Preset(WeatherType.Clear, "晴朗", 4.5f, 240f, 420f, 2f));
                settings.presets.Add(Preset(WeatherType.ThinDust, "薄尘", 3.5f, 180f, 360f, 6f, fog: 0.009f, cloud: 0.25f, light: 0.82f, particles: 900, dust: 0.25f));
                settings.presets.Add(Preset(WeatherType.MeteorDust, "陨尘风", 2f, 80f, 190f, 13f, fog: 0.028f, cloud: 0.55f, light: 0.58f, traction: 0.8f, particles: 2800, dust: 0.65f));
                break;
        }
        settings.ClampValues();
        return settings;
    }
    finally
    {
        if(__logTrackDepthEntered)FSPDebuger.PopDepth();
    }}

    static WeatherPreset Preset(WeatherType type, string name, float weight, float minDuration,
        float maxDuration, float wind, float precipitation = 0f, float fog = 0f,
        float cloud = 0f, float light = 1f, float traction = 1f, float riverRain = 0f,
        int particles = 0, bool lightning = false, float dust = 0f, float crystal = 0f)
    {
        return new WeatherPreset
        {
            type = type, displayName = name, weight = weight,
            minimumDuration = minDuration, maximumDuration = maxDuration,
            windSpeed = wind, precipitation = precipitation, fogDensity = fog,
            cloudCoverage = cloud, lightMultiplier = light, tractionMultiplier = traction,
            riverRainfallRate = riverRain, particleCount = particles, lightning = lightning,
            wetness = precipitation > 0f ? Mathf.Clamp01(precipitation) : 0f,
            dustCoverage = dust, crystalCoverage = crystal,
            fogColor = FogColor(type), particleColor = ParticleColor(type), ambientTint = AmbientColor(type)
        };
    }

    static Color FogColor(WeatherType type)
    {
        if (type == WeatherType.Sandstorm || type == WeatherType.Ashfall || type == WeatherType.MeteorDust)
            return new Color(0.42f, 0.2f, 0.09f, 1f);
        if (type == WeatherType.CrystalDust || type == WeatherType.IonStorm || type == WeatherType.VioletFog)
            return new Color(0.22f, 0.08f, 0.34f, 1f);
        return new Color(0.3f, 0.42f, 0.5f, 1f);
    }

    static Color ParticleColor(WeatherType type)
    {
        if (type == WeatherType.Ashfall) return new Color(0.25f, 0.2f, 0.18f, 1f);
        if (type == WeatherType.Sandstorm || type == WeatherType.MeteorDust || type == WeatherType.ThinDust) return new Color(0.9f, 0.43f, 0.16f, 1f);
        if (type == WeatherType.CrystalDust || type == WeatherType.IonStorm) return new Color(0.78f, 0.3f, 1f, 1f);
        return new Color(0.72f, 0.9f, 1f, 1f);
    }

    static Color AmbientColor(WeatherType type)
    {
        if (type == WeatherType.Sandstorm || type == WeatherType.Ashfall) return new Color(0.75f, 0.42f, 0.25f, 1f);
        if (type == WeatherType.CrystalDust || type == WeatherType.IonStorm || type == WeatherType.VioletFog) return new Color(0.55f, 0.3f, 0.8f, 1f);
        return Color.white;
    }
}
