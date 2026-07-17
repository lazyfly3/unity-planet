using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PlanetWeatherSystem : MonoBehaviour
{
    struct WeatherSegment
    {
        public int index;
        public double start;
        public double end;
        public float intensity;
        public WeatherPreset preset;
    }

    public static PlanetWeatherSystem Active { get; private set; }

    [Header("World References")]
    [SerializeField] Transform effectsRoot;
    [SerializeField] MeshRenderer cloudShell;
    [SerializeField] Light sunLight;
    [SerializeField] Light lightningLight;
    [SerializeField] LineRenderer lightningRenderer;

    [Header("Precipitation")]
    [SerializeField] ParticleSystem rainParticles;
    [SerializeField] ParticleSystem snowParticles;
    [SerializeField] ParticleSystem ashParticles;
    [SerializeField] ParticleSystem sandParticles;
    [SerializeField] ParticleSystem crystalParticles;
    [SerializeField] ParticleSystem groundMistParticles;

    [Header("Audio")]
    [SerializeField] AudioSource windAudio;
    [SerializeField] AudioSource rainAudio;
    [SerializeField] AudioSource stormAudio;

    [Header("Weather Panel")]
    [SerializeField] GameObject weatherPanel;
    [SerializeField] Image weatherIcon;
    [SerializeField] Text weatherName;
    [SerializeField] Text intensityText;
    [SerializeField] Text windText;
    [SerializeField] Text remainingTimeText;
    [SerializeField] Text nextWeatherText;
    [SerializeField] Sprite[] weatherIcons;

    [Header("Tuning")]
    [SerializeField, Min(1f)] float precipitationHeight = 16f;
    [SerializeField, Min(1f)] float cloudAltitude = 28f;
    [SerializeField, Range(0f, 1f)] float fogBlend = 1f;

    MaterialPropertyBlock cloudProperties;
    VoxelQuadSphereWorld world;
    PlanetWeatherSettings settings;
    Transform player;
    int planetSeed;
    Vector3 windAxis;
    WeatherSegment current;
    WeatherSegment next;
    WeatherSnapshot snapshot;
    bool configured;
    int lastUiSecond = -1;
    int lastThunderCycle = -1;

    bool baselineFog;
    Color baselineFogColor;
    float baselineFogDensity;
    Color baselineAmbient;
    float baselineSunIntensity;
    Color baselineSunColor;

    public WeatherSnapshot Snapshot => snapshot;

    void Awake()
    {
        cloudProperties = new MaterialPropertyBlock();
        Active = this;
        CaptureEnvironmentBaseline();
        SetAllEffectsStopped();
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;
        RestoreEnvironmentBaseline();
        Shader.SetGlobalFloat("_WeatherWetness", 0f);
        Shader.SetGlobalFloat("_WeatherDust", 0f);
        Shader.SetGlobalFloat("_WeatherCrystal", 0f);
    }

    public void Configure(VoxelQuadSphereWorld targetWorld, PlanetWeatherSettings planetSettings,
        int targetPlanetSeed, string planetId)
    {
world = targetWorld;
        planetSeed = targetPlanetSeed;
        settings = planetSettings;
        if (settings == null || settings.presets == null || settings.presets.Count == 0)
            settings = PlanetWeatherDefaults.Create(planetId);
        settings.ClampValues();
        player = FindObjectOfType<VoxelPlanetPlayerController>()?.transform;
        windAxis = CreateWindAxis(planetSeed + settings.seedOffset);
        double weatherTime = GetWeatherTime();
        ResolveFromStart(weatherTime);
        configured = settings.enabled && current.preset != null;
        PositionCloudShell();
        ApplyAtTime(weatherTime, true);
        if (weatherPanel != null)
            weatherPanel.SetActive(configured);
    
}

    void Update()
    {
        if (!configured || world == null)
            return;
        double weatherTime = GetWeatherTime();
        if (weatherTime < current.start || weatherTime >= current.end)
            ResolveFromCurrent(weatherTime);
        ApplyAtTime(weatherTime, false);
    }

    public static bool TrySample(Vector3 worldPosition, out WeatherSnapshot value)
    {
if (Active == null || !Active.configured)
        {
            value = default;
            return false;
        }
        value = Active.snapshot;
        value.windVelocity = Active.SampleWind(worldPosition);
        return true;
    
}

    public Vector3 SampleWind(Vector3 worldPosition)
    {
if (!configured || world == null)
            return Vector3.zero;
        Vector3 up = worldPosition - world.GetPlanetCenterWorld();
        if (up.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        up.Normalize();
        Vector3 tangent = Vector3.Cross(windAxis, up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.Cross(Vector3.Cross(windAxis, Vector3.right).normalized, up);
        return tangent.normalized * snapshot.windSpeed;
    
}

    void ResolveFromStart(double weatherTime)
    {
        current = CreateSegment(0, 0d);
        next = CreateSegment(1, current.end);
        int guard = 0;
        while (weatherTime >= current.end && guard++ < 100000)
        {
            current = next;
            next = CreateSegment(current.index + 1, current.end);
        }
    }

    void ResolveFromCurrent(double weatherTime)
    {
        if (weatherTime < current.start)
        {
            ResolveFromStart(weatherTime);
            return;
        }
        int guard = 0;
        while (weatherTime >= current.end && guard++ < 10000)
        {
            current = next;
            next = CreateSegment(current.index + 1, current.end);
        }
    }

    WeatherSegment CreateSegment(int index, double start)
    {
        int seed = CombineHash(planetSeed + settings.seedOffset, index);
        var random = new System.Random(seed);
        float totalWeight = 0f;
        foreach (WeatherPreset preset in settings.presets)
            if (preset != null) totalWeight += preset.weight;
        float selection = (float)random.NextDouble() * Mathf.Max(0.01f, totalWeight);
        WeatherPreset selected = settings.presets[0];
        foreach (WeatherPreset preset in settings.presets)
        {
            if (preset == null)
                continue;
            selection -= preset.weight;
            selected = preset;
            if (selection <= 0f)
                break;
        }
        float duration = Mathf.Lerp(selected.minimumDuration, selected.maximumDuration,
            (float)random.NextDouble());
        float intensity = Mathf.Lerp(selected.minimumIntensity, selected.maximumIntensity,
            (float)random.NextDouble());
        return new WeatherSegment
        {
            index = index,
            start = start,
            end = start + duration,
            intensity = intensity,
            preset = selected
        };
    }

    void ApplyAtTime(double weatherTime, bool forceUi)
    {
        float remaining = Mathf.Max(0f, (float)(current.end - weatherTime));
        float transition = settings.transitionDuration > 0f
            ? Mathf.Clamp01(1f - remaining / settings.transitionDuration)
            : 0f;
        transition = transition * transition * (3f - 2f * transition);
        WeatherPreset a = current.preset;
        WeatherPreset b = next.preset;
        float currentStrength = current.intensity * (1f - transition);
        float nextStrength = next.intensity * transition;

        float windSpeed = Mathf.Lerp(a.windSpeed * current.intensity, b.windSpeed * next.intensity, transition);
        float precipitation = Mathf.Lerp(a.precipitation * current.intensity, b.precipitation * next.intensity, transition);
        float fogDensity = Mathf.Lerp(a.fogDensity * current.intensity, b.fogDensity * next.intensity, transition) * fogBlend;
        float cloud = Mathf.Lerp(a.cloudCoverage * current.intensity, b.cloudCoverage * next.intensity, transition);
        float wetness = Mathf.Lerp(a.wetness * current.intensity, b.wetness * next.intensity, transition);
        float dust = Mathf.Lerp(a.dustCoverage * current.intensity, b.dustCoverage * next.intensity, transition);
        float crystal = Mathf.Lerp(a.crystalCoverage * current.intensity, b.crystalCoverage * next.intensity, transition);
        float traction = Mathf.Lerp(a.tractionMultiplier, b.tractionMultiplier, transition);

        snapshot = new WeatherSnapshot
        {
            type = a.type,
            nextType = b.type,
            displayName = a.displayName,
            nextDisplayName = b.displayName,
            intensity = Mathf.Lerp(current.intensity, next.intensity, transition),
            remainingSeconds = remaining,
            windSpeed = windSpeed,
            precipitation = precipitation,
            groundTractionMultiplier = traction,
            wetness = wetness,
            dustCoverage = dust,
            crystalCoverage = crystal
        };
        if (player != null)
            snapshot.windVelocity = SampleWind(player.position);

        ApplyEnvironment(a, b, transition, fogDensity, cloud, wetness, dust, crystal);
        ApplyParticle(rainParticles, RainWeight(a.type) * currentStrength + RainWeight(b.type) * nextStrength,
            a.particleColor, b.particleColor, transition, a.particleCount, b.particleCount);
        ApplyParticle(snowParticles, 0f, Color.white, Color.white, 0f, 0, 0);
        ApplyParticle(ashParticles, AshWeight(a.type) * currentStrength + AshWeight(b.type) * nextStrength,
            a.particleColor, b.particleColor, transition, a.particleCount, b.particleCount);
        ApplyParticle(sandParticles, DustWeight(a.type) * currentStrength + DustWeight(b.type) * nextStrength,
            a.particleColor, b.particleColor, transition, a.particleCount, b.particleCount);
        ApplyParticle(crystalParticles, CrystalWeight(a.type) * currentStrength + CrystalWeight(b.type) * nextStrength,
            a.particleColor, b.particleColor, transition, a.particleCount, b.particleCount);
        ApplyParticle(groundMistParticles, FogWeight(a.type) * currentStrength + FogWeight(b.type) * nextStrength,
            a.fogColor, b.fogColor, transition, a.particleCount + 500, b.particleCount + 500);
        PositionEffects();
        ApplyAudio(precipitation, windSpeed, a.lightning || b.lightning);
        ApplyLightning(weatherTime, a.lightning || (transition > 0.5f && b.lightning));

        float riverRain = Mathf.Lerp(a.riverRainfallRate * current.intensity,
            b.riverRainfallRate * next.intensity, transition);
        world.RiverSystem?.SetWeatherRainfall(riverRain);
        UpdatePanel(forceUi);
    }

    void ApplyEnvironment(WeatherPreset a, WeatherPreset b, float transition, float fogDensity,
        float cloud, float wetness, float dust, float crystal)
    {
        Color fogColor = Color.Lerp(a.fogColor, b.fogColor, transition);
        RenderSettings.fog = fogDensity > 0.0001f || baselineFog;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = Mathf.Max(baselineFogDensity, fogDensity);
        RenderSettings.fogColor = Color.Lerp(baselineFogColor, fogColor, Mathf.Clamp01(fogDensity * 28f));
        Color ambientTint = Color.Lerp(a.ambientTint, b.ambientTint, transition);
        RenderSettings.ambientLight = Color.Lerp(baselineAmbient,
            baselineAmbient * ambientTint, Mathf.Clamp01(cloud));
        if (sunLight != null)
        {
            sunLight.intensity = baselineSunIntensity * Mathf.Lerp(a.lightMultiplier, b.lightMultiplier, transition);
            sunLight.color = Color.Lerp(baselineSunColor, ambientTint, Mathf.Clamp01(cloud * 0.35f));
        }
        if (cloudShell != null)
        {
            cloudShell.GetPropertyBlock(cloudProperties);
            cloudProperties.SetFloat("_Coverage", cloud);
            cloudProperties.SetColor("_CloudColor", Color.Lerp(a.fogColor, b.fogColor, transition));
            cloudProperties.SetVector("_Wind", new Vector4(windAxis.x, windAxis.y, windAxis.z, snapshot.windSpeed));
            cloudShell.SetPropertyBlock(cloudProperties);
            cloudShell.enabled = cloud > 0.01f;
        }
        Shader.SetGlobalVector("_PlanetCenter", world.GetPlanetCenterWorld());
        Shader.SetGlobalFloat("_WeatherWetness", wetness);
        Shader.SetGlobalFloat("_WeatherDust", dust);
        Shader.SetGlobalFloat("_WeatherCrystal", crystal);
    }

    void PositionEffects()
    {
        if (player == null)
        {
            player = FindObjectOfType<VoxelPlanetPlayerController>()?.transform;
            if (player == null)
                return;
        }
        Vector3 up = (player.position - world.GetPlanetCenterWorld()).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.Cross(up, Vector3.right).normalized;
        if (effectsRoot != null)
        {
            effectsRoot.position = player.position + up * precipitationHeight;
            effectsRoot.rotation = Quaternion.LookRotation(-up, forward);
        }
    }

    void PositionCloudShell()
    {
        if (cloudShell == null || world == null)
            return;
        cloudShell.transform.position = world.GetPlanetCenterWorld();
        float diameter = (world.PlanetRadius + cloudAltitude) * 2f;
        cloudShell.transform.localScale = Vector3.one * diameter;
    }

    void ApplyParticle(ParticleSystem system, float strength, Color from, Color to, float blend,
        int fromCount, int toCount)
    {
        if (system == null)
            return;
        float normalized = Mathf.Clamp01(strength);
        var main = system.main;
        main.startColor = Color.Lerp(from, to, blend);
        main.maxParticles = Mathf.Max(64, Mathf.RoundToInt(Mathf.Lerp(fromCount, toCount, blend)));
        var emission = system.emission;
        emission.rateOverTime = main.maxParticles * normalized * 0.45f;
        if (normalized > 0.005f)
        {
            if (!system.isPlaying) system.Play();
        }
        else if (system.isPlaying)
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        Vector3 wind = player != null ? SampleWind(player.position) : Vector3.zero;
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = wind.x;
        velocity.y = wind.y;
        velocity.z = wind.z;
    }

    void ApplyAudio(float precipitation, float windSpeed, bool storm)
    {
        SetAudio(windAudio, Mathf.Clamp01(windSpeed / 18f) * 0.7f);
        SetAudio(rainAudio, Mathf.Clamp01(precipitation) * 0.8f);
        if (!storm)
            SetAudio(stormAudio, 0f);
    }

    static void SetAudio(AudioSource source, float volume)
    {
        if (source == null)
            return;
        source.volume = volume;
        if (volume > 0.005f && source.clip != null && !source.isPlaying)
            source.Play();
        else if (volume <= 0.005f && source.isPlaying)
            source.Stop();
    }

    void ApplyLightning(double weatherTime, bool enabled)
    {
        if (lightningLight == null || lightningRenderer == null)
            return;
        if (!enabled || player == null)
        {
            lightningLight.enabled = false;
            lightningRenderer.enabled = false;
            return;
        }
        const float cycleLength = 7f;
        int cycle = Mathf.FloorToInt((float)weatherTime / cycleLength);
        float phase = Mathf.Repeat((float)weatherTime, cycleLength);
        bool flash = phase < 0.16f || (phase > 0.25f && phase < 0.31f);
        lightningLight.enabled = flash;
        lightningRenderer.enabled = flash;
        if (!flash)
            return;
        Vector3 up = (player.position - world.GetPlanetCenterWorld()).normalized;
        Vector3 tangent = SampleWind(player.position).normalized;
        if (tangent.sqrMagnitude < 0.001f)
            tangent = Vector3.Cross(up, Vector3.forward).normalized;
        float offset = Hash01(CombineHash(planetSeed, cycle)) * 24f - 12f;
        Vector3 ground = player.position + tangent * offset + Vector3.Cross(up, tangent) * (offset * 0.4f);
        Vector3 sky = ground + up * 32f;
        lightningRenderer.SetPosition(0, sky);
        lightningRenderer.SetPosition(1, Vector3.Lerp(sky, ground, 0.48f) + tangent * 2f);
        lightningRenderer.SetPosition(2, ground);
        lightningLight.transform.position = ground + up * 6f;
        lightningLight.intensity = phase < 0.1f ? 5f : 2.2f;
        if (cycle != lastThunderCycle)
        {
            lastThunderCycle = cycle;
            if (stormAudio != null && stormAudio.clip != null)
                stormAudio.PlayOneShot(stormAudio.clip, 0.8f);
        }
    }

    void UpdatePanel(bool force)
    {
        int seconds = Mathf.CeilToInt(snapshot.remainingSeconds);
        if (!force && seconds == lastUiSecond)
            return;
        lastUiSecond = seconds;
        if (weatherName != null) weatherName.text = snapshot.displayName;
        if (intensityText != null) intensityText.text = $"强度 {snapshot.intensity * 100f:0}%";
        if (windText != null) windText.text = $"风速 {snapshot.windSpeed:0.0} m/s";
        if (remainingTimeText != null) remainingTimeText.text = $"剩余 {seconds / 60:00}:{seconds % 60:00}";
        if (nextWeatherText != null) nextWeatherText.text = $"下一天气  {snapshot.nextDisplayName}";
        if (weatherIcon != null && weatherIcons != null && (int)snapshot.type < weatherIcons.Length)
            weatherIcon.sprite = weatherIcons[(int)snapshot.type];
    }

    void CaptureEnvironmentBaseline()
    {
        baselineFog = RenderSettings.fog;
        baselineFogColor = RenderSettings.fogColor;
        baselineFogDensity = RenderSettings.fogDensity;
        baselineAmbient = RenderSettings.ambientLight;
        if (sunLight != null)
        {
            baselineSunIntensity = sunLight.intensity;
            baselineSunColor = sunLight.color;
        }
        else
        {
            baselineSunIntensity = 1f;
            baselineSunColor = Color.white;
        }
    }

    void RestoreEnvironmentBaseline()
    {
        RenderSettings.fog = baselineFog;
        RenderSettings.fogColor = baselineFogColor;
        RenderSettings.fogDensity = baselineFogDensity;
        RenderSettings.ambientLight = baselineAmbient;
        if (sunLight != null)
        {
            sunLight.intensity = baselineSunIntensity;
            sunLight.color = baselineSunColor;
        }
    }

    void SetAllEffectsStopped()
    {
        ParticleSystem[] systems = { rainParticles, snowParticles, ashParticles, sandParticles, crystalParticles, groundMistParticles };
        foreach (ParticleSystem system in systems)
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (lightningLight != null) lightningLight.enabled = false;
        if (lightningRenderer != null) lightningRenderer.enabled = false;
    }

    double GetWeatherTime()
    {
        return GalaxyTravelManager.Instance != null ? GalaxyTravelManager.Instance.WeatherTimeSeconds : Time.timeAsDouble;
    }

    static int CombineHash(int seed, int index)
    {
        unchecked
        {
            uint value = (uint)seed;
            value ^= (uint)index + 0x9e3779b9u + (value << 6) + (value >> 2);
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            return (int)value;
        }
    }

    static float Hash01(int value)
    {
        return (uint)value / (float)uint.MaxValue;
    }

    static Vector3 CreateWindAxis(int seed)
    {
        var random = new System.Random(seed);
        Vector3 axis = new Vector3((float)random.NextDouble() * 2f - 1f,
            (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
        return axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.up;
    }

    static float RainWeight(WeatherType type) => type == WeatherType.Rain || type == WeatherType.Thunderstorm
        || type == WeatherType.HeavyRain || type == WeatherType.OceanStorm ? 1f : 0f;
    static float AshWeight(WeatherType type) => type == WeatherType.Ashfall ? 1f : 0f;
    static float DustWeight(WeatherType type) => type == WeatherType.ThinDust || type == WeatherType.MeteorDust
        || type == WeatherType.Sandstorm || type == WeatherType.DryThunderstorm ? 1f : 0f;
    static float CrystalWeight(WeatherType type) => type == WeatherType.CrystalDust || type == WeatherType.IonStorm ? 1f : 0f;
    static float FogWeight(WeatherType type) => type == WeatherType.MorningFog || type == WeatherType.DenseFog
        || type == WeatherType.VioletFog ? 1f : 0f;
}
