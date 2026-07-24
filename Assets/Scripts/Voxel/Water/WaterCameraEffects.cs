using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class WaterCameraEffects : MonoBehaviour
{
    const float EnterThreshold = -0.15f;
    const float ExitThreshold = 0.15f;
    const float UnderwaterCutoffFrequency = 650f;
    const float WadingBlend = 0.2f;
    const float FogDistance = 36f;

    Camera targetCamera;
    VoxelPlanetPlayerController player;
    AudioLowPassFilter lowPass;
    Material effectMaterial;
    Material particleMaterial;
    Texture2D particleTexture;
    ParticleSystem underwaterParticles;
    float blend;
    float entryPulse;
    float waterDepth;
    bool isUnderwater;
    Color waterTint = new Color(0.02f, 0.23f, 0.31f, 1f);

    public bool IsUnderwater => isUnderwater;
    public float Blend => blend;
    public float WaterDepth => waterDepth;

    public void Configure(VoxelPlanetPlayerController valuePlayer)
    {
        player = valuePlayer;
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();
        targetCamera.depthTextureMode |= DepthTextureMode.Depth;
    }

    public static bool ResolveUnderwaterState(
        bool previousState,
        bool hasWater,
        float signedDistance)
    {
        if (!hasWater)
            return false;
        return previousState
            ? signedDistance <= ExitThreshold
            : signedDistance < EnterThreshold;
    }

    void Awake()
    {
        targetCamera = GetComponent<Camera>();
        targetCamera.depthTextureMode |= DepthTextureMode.Depth;
        lowPass = GetComponent<AudioLowPassFilter>();
        if (lowPass == null)
            lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        lowPass.enabled = false;

        Shader shader = Shader.Find("Hidden/Voxel Planet/Underwater Screen");
        if (shader != null)
            effectMaterial = new Material(shader);
        BuildParticles();
    }

    void Update()
    {
        bool hasWater = PlanetWaterRegistry.TrySampleAny(
            transform.position,
            out WaterSample water);
        bool previousUnderwater = isUnderwater;
        isUnderwater = ResolveUnderwaterState(
            isUnderwater,
            hasWater,
            hasWater ? water.signedDistance : float.PositiveInfinity);
        if (hasWater)
        {
            waterTint = water.tint;
            waterDepth = Mathf.Max(0f, -water.signedDistance);
        }
        else
        {
            isUnderwater = false;
            waterDepth = 0f;
        }

        if (!previousUnderwater && isUnderwater)
            entryPulse = 1f;
        entryPulse = Mathf.MoveTowards(
            entryPulse,
            0f,
            2.4f * Time.unscaledDeltaTime);

        bool wading = !isUnderwater
            && player != null
            && player.IsSwimming;
        float targetBlend = isUnderwater ? 1f : wading ? WadingBlend : 0f;
        float transitionSpeed = targetBlend > blend ? 3.8f : 2.2f;
        blend = Mathf.MoveTowards(
            blend,
            targetBlend,
            transitionSpeed * Time.unscaledDeltaTime);

        float audioSubmersion = isUnderwater ? blend : blend * 0.35f;
        lowPass.cutoffFrequency = Mathf.Lerp(
            22000f,
            UnderwaterCutoffFrequency,
            audioSubmersion);
        lowPass.enabled = blend > 0.001f;
        UpdateParticles();
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (effectMaterial == null || blend <= 0.001f)
        {
            Graphics.Blit(source, destination);
            return;
        }

        effectMaterial.SetFloat("_Blend", blend);
        effectMaterial.SetFloat("_Submerged", isUnderwater ? 1f : 0f);
        effectMaterial.SetFloat("_WaterDepth", waterDepth);
        effectMaterial.SetFloat("_EntryPulse", entryPulse);
        effectMaterial.SetFloat("_FogDistance", FogDistance);
        effectMaterial.SetFloat("_TimeValue", Time.unscaledTime);
        effectMaterial.SetColor("_Tint", waterTint);
        Graphics.Blit(source, destination, effectMaterial);
    }

    void BuildParticles()
    {
        GameObject particleObject = new GameObject("UnderwaterParticles");
        particleObject.transform.SetParent(transform, false);
        underwaterParticles = particleObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = underwaterParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 72;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.16f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.075f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.45f, 0.9f, 1f, 0.12f),
            new Color(0.8f, 1f, 1f, 0.42f));

        ParticleSystem.EmissionModule emission = underwaterParticles.emission;
        emission.enabled = false;
        emission.rateOverTime = 20f;
        ParticleSystem.ShapeModule shape = underwaterParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 7f;
        shape.radiusThickness = 1f;

        ParticleSystemRenderer renderer =
            particleObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader particleShader =
            Shader.Find("Legacy Shaders/Particles/Alpha Blended")
            ?? Shader.Find("Particles/Standard Unlit");
        if (particleShader == null)
            return;

        particleTexture = CreateParticleTexture();
        particleMaterial = new Material(particleShader);
        particleMaterial.name = "Runtime Underwater Particle";
        particleMaterial.SetTexture("_MainTex", particleTexture);
        renderer.sharedMaterial = particleMaterial;
    }

    void UpdateParticles()
    {
        if (underwaterParticles == null)
            return;

        ParticleSystem.EmissionModule emission = underwaterParticles.emission;
        emission.enabled = isUnderwater && blend > 0.05f;
        if (emission.enabled && !underwaterParticles.isPlaying)
            underwaterParticles.Play();
        else if (!emission.enabled && underwaterParticles.isPlaying)
            underwaterParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        ParticleSystem.MainModule main = underwaterParticles.main;
        Color shallow = Color.Lerp(waterTint, Color.white, 0.48f);
        shallow.a = 0.35f;
        Color deep = Color.Lerp(waterTint, Color.cyan, 0.25f);
        deep.a = 0.1f;
        main.startColor = new ParticleSystem.MinMaxGradient(deep, shallow);
    }

    static Texture2D CreateParticleTexture()
    {
        const int size = 32;
        var texture = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false,
            true);
        texture.name = "Runtime Underwater Particle Mask";
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 uv = new Vector2(
                (x + 0.5f) / size,
                (y + 0.5f) / size) * 2f - Vector2.one;
            float alpha = Mathf.Pow(
                Mathf.Clamp01(1f - uv.sqrMagnitude),
                2.2f);
            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    void OnDisable()
    {
        if (lowPass != null)
            lowPass.enabled = false;
        if (underwaterParticles != null)
            underwaterParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        blend = 0f;
        entryPulse = 0f;
        isUnderwater = false;
        waterDepth = 0f;
    }

    void OnDestroy()
    {
        if (effectMaterial != null)
            Destroy(effectMaterial);
        if (particleMaterial != null)
            Destroy(particleMaterial);
        if (particleTexture != null)
            Destroy(particleTexture);
    }
}
