using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ProceduralPlanetLabController : MonoBehaviour
{
    const double RebuildDelaySeconds = 0.2d;

    [Header("Scene References")]
    [SerializeField] Transform planetRoot;
    [SerializeField] MeshFilter terrainFilter;
    [SerializeField] MeshRenderer terrainRenderer;
    [SerializeField] MeshFilter oceanFilter;
    [SerializeField] MeshRenderer oceanRenderer;
    [SerializeField] Camera previewCamera;
    [SerializeField] Light sunLight;
    [SerializeField] Light fillLight;

    [Header("Active Preset")]
    [SerializeField] ProceduralPlanetPreset preset;

    Mesh terrainMesh;
    Mesh oceanMesh;
    Material terrainMaterial;
    Material oceanMaterial;
    RenderTexture previewTexture;
    bool rebuildPending;
    double rebuildAt;
    int terrainBuildRevision;
    float builtOceanRadius = float.NaN;
    int builtOceanResolution;
    double lastEditorUpdate;

    public ProceduralPlanetPreset Preset => preset;
    public Camera PreviewCamera => previewCamera;
    public Mesh TerrainMesh => terrainMesh;
    public Mesh OceanMesh => oceanMesh;
    public int TerrainBuildRevision => terrainBuildRevision;
    public float OceanRadius => builtOceanRadius;
    public int GeneratedSceneObjectCount
        => (terrainFilter != null ? 1 : 0) + (oceanFilter != null ? 1 : 0);

    public void ConfigureSceneReferences(
        Transform valuePlanetRoot,
        MeshFilter valueTerrainFilter,
        MeshRenderer valueTerrainRenderer,
        MeshFilter valueOceanFilter,
        MeshRenderer valueOceanRenderer,
        Camera valuePreviewCamera,
        Light valueSunLight,
        Light valueFillLight)
    {
        planetRoot = valuePlanetRoot;
        terrainFilter = valueTerrainFilter;
        terrainRenderer = valueTerrainRenderer;
        oceanFilter = valueOceanFilter;
        oceanRenderer = valueOceanRenderer;
        previewCamera = valuePreviewCamera;
        sunLight = valueSunLight;
        fillLight = valueFillLight;
    }

    public void ApplyPreset(ProceduralPlanetPreset value, bool rebuildShape)
    {
        preset = value;
        if (preset == null)
        {
            SetRenderersEnabled(false);
            return;
        }

        preset.ClampValues();
        EnsureRuntimeMaterials();
        ApplyMaterialProperties();
        ApplyPreviewSettings();

        if (rebuildShape || terrainMesh == null)
            RequestRebuild();
        else
            RebuildOceanIfNeeded(preset.previewResolution);
    }

    public void RequestRebuild()
    {
        if (preset == null)
            return;

        rebuildPending = true;
#if UNITY_EDITOR
        rebuildAt = EditorApplication.timeSinceStartup + RebuildDelaySeconds;
#else
        rebuildAt = Time.realtimeSinceStartupAsDouble + RebuildDelaySeconds;
#endif
    }

    public void RebuildNow(int resolution)
    {
        if (preset == null || terrainFilter == null || terrainRenderer == null)
            return;

        preset.ClampValues();
        resolution = Mathf.Clamp(resolution, 6, 128);
        rebuildPending = false;
        EnsureRuntimeMaterials();

        GalaxyPlanetDefinition definition = preset.CloneDefinition();
        Mesh replacement = PlanetLodMeshBuilder.Build(definition, resolution);
        replacement.name = "PlanetLabTerrain_" + preset.seed + "_" + resolution;
        replacement.hideFlags = HideFlags.HideAndDontSave;
        terrainFilter.sharedMesh = replacement;
        DestroyTransient(terrainMesh);
        terrainMesh = replacement;
        terrainBuildRevision++;

        RebuildOcean(resolution);
        ApplyMaterialProperties();
        ApplyPreviewSettings();
        SetRenderersEnabled(true);
    }

    public RenderTexture RenderPreview(int width, int height)
    {
        if (previewCamera == null)
            return null;

        width = Mathf.Clamp(width, 64, 4096);
        height = Mathf.Clamp(height, 64, 4096);
        if (previewTexture == null
            || previewTexture.width != width
            || previewTexture.height != height)
        {
            DestroyPreviewTexture();
            previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "PlanetLabWindowPreview",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false
            };
            previewTexture.Create();
        }

        ApplyRuntimeCenters();
        RenderTexture previous = previewCamera.targetTexture;
        previewCamera.targetTexture = previewTexture;
        previewCamera.Render();
        previewCamera.targetTexture = previous;
        return previewTexture;
    }

    public void CapturePreview(string path, int width, int height)
    {
        if (previewCamera == null)
            throw new InvalidOperationException("Planet Lab preview camera is missing.");

        width = Mathf.Clamp(width, 64, 7680);
        height = Mathf.Clamp(height, 64, 4320);
        string fullPath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "PlanetLabCapture",
            hideFlags = HideFlags.HideAndDontSave,
            antiAliasing = 4
        };
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        {
            name = "PlanetLabCapturePixels",
            hideFlags = HideFlags.HideAndDontSave
        };
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = previewCamera.targetTexture;
        try
        {
            target.Create();
            ApplyRuntimeCenters();
            previewCamera.targetTexture = target;
            previewCamera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            image.Apply(false, false);
            File.WriteAllBytes(fullPath, image.EncodeToPNG());
        }
        finally
        {
            previewCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            DestroyTransient(target);
            DestroyTransient(image);
        }
    }

    public void OrbitPreview(Vector2 delta)
    {
        if (preset == null)
            return;
        preset.preview.cameraYaw =
            Mathf.Repeat(preset.preview.cameraYaw + delta.x * 0.35f + 180f, 360f) - 180f;
        preset.preview.cameraPitch = Mathf.Clamp(
            preset.preview.cameraPitch - delta.y * 0.3f,
            -85f,
            85f);
        ApplyPreviewSettings();
    }

    public void ZoomPreview(float scrollDelta)
    {
        if (preset == null)
            return;
        preset.preview.cameraDistance = Mathf.Clamp(
            preset.preview.cameraDistance * Mathf.Exp(scrollDelta * 0.08f),
            1.35f,
            8f);
        ApplyPreviewSettings();
    }

    public void ResetPreviewOrbit()
    {
        if (preset == null)
            return;
        preset.preview.cameraYaw = 35f;
        preset.preview.cameraPitch = 20f;
        preset.preview.cameraDistance = 3.45f;
        ApplyPreviewSettings();
    }

    public void RefreshAppearance()
    {
        if (preset == null)
            return;
        preset.ClampValues();
        EnsureRuntimeMaterials();
        RebuildOceanIfNeeded(preset.previewResolution);
        ApplyMaterialProperties();
        ApplyPreviewSettings();
    }

    void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
        lastEditorUpdate = EditorApplication.timeSinceStartup;
#endif
        if (preset != null && terrainFilter != null)
            RebuildNow(preset.previewResolution);
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
        CleanupTransientResources();
    }

    void OnDestroy()
    {
        CleanupTransientResources();
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;
        if (rebuildPending && Time.realtimeSinceStartupAsDouble >= rebuildAt)
            RebuildNow(preset != null ? preset.previewResolution : 64);
        TickAutoRotation(Time.unscaledDeltaTime);
        ApplyRuntimeCenters();
    }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (this == null || Application.isPlaying)
            return;

        double now = EditorApplication.timeSinceStartup;
        float delta = (float)Math.Max(0d, Math.Min(0.1d, now - lastEditorUpdate));
        lastEditorUpdate = now;
        if (rebuildPending && now >= rebuildAt)
            RebuildNow(preset != null ? preset.previewResolution : 64);
        TickAutoRotation(delta);
        ApplyRuntimeCenters();
    }
#endif

    void TickAutoRotation(float deltaTime)
    {
        if (preset == null || planetRoot == null || !preset.preview.autoRotate)
            return;
        planetRoot.Rotate(Vector3.up, preset.preview.rotationSpeed * deltaTime, Space.Self);
    }

    void EnsureRuntimeMaterials()
    {
        if (terrainMaterial == null)
        {
            Shader terrainShader = Shader.Find("VoxelPlanet/SurfaceFarLod");
            if (terrainShader == null)
                throw new InvalidOperationException(
                    "Missing shader: VoxelPlanet/SurfaceFarLod");
            terrainMaterial = new Material(terrainShader)
            {
                name = "PlanetLabTerrainMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        if (terrainRenderer != null)
            terrainRenderer.sharedMaterial = terrainMaterial;

        if (oceanMaterial == null)
        {
            Shader oceanShader = Shader.Find("VoxelPlanet/ProceduralOcean");
            if (oceanShader == null)
                throw new InvalidOperationException(
                    "Missing shader: VoxelPlanet/ProceduralOcean");
            oceanMaterial = new Material(oceanShader)
            {
                name = "PlanetLabOceanMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        if (oceanRenderer != null)
            oceanRenderer.sharedMaterial = oceanMaterial;
    }

    void ApplyMaterialProperties()
    {
        if (preset == null)
            return;

        PlanetLowPolyVisualProfile visual = preset.visual;
        if (terrainMaterial != null)
        {
            terrainMaterial.SetFloat("_UseLowPolyVisual", 1f);
            terrainMaterial.SetColor("_LowlandColor", visual.lowlandColor);
            terrainMaterial.SetColor("_HighlandColor", visual.highlandColor);
            terrainMaterial.SetColor("_CliffColor", visual.cliffColor);
            terrainMaterial.SetColor("_RockColor", visual.rockColor);
            terrainMaterial.SetColor("_AccentColor", visual.accentColor);
            terrainMaterial.SetColor("_ShoreColor", visual.shoreColor);
            terrainMaterial.SetColor("_SnowColor", visual.snowColor);
            terrainMaterial.SetFloat("_FacetStrength", visual.facetStrength);
            terrainMaterial.SetFloat("_LightingBands", visual.lightingBands);
            terrainMaterial.SetFloat("_MacroColorSize", visual.macroColorSize);
            terrainMaterial.SetFloat("_MacroVariation", visual.macroVariation);
            terrainMaterial.SetFloat("_CliffSlope", visual.cliffSlope);
            terrainMaterial.SetFloat("_PlanetRadius", preset.radius);
            terrainMaterial.SetFloat("_HeightScale", preset.maximumTerrainElevation);
            terrainMaterial.SetFloat("_SeaLevel", visual.oceanLevel);
            terrainMaterial.SetFloat("_ShoreWidth", visual.shoreWidth);
            terrainMaterial.SetFloat("_SnowLine", visual.snowLine);
            terrainMaterial.SetFloat("_SnowAmount", visual.snowAmount);
            terrainMaterial.SetFloat("_EnableNearTerrainCutout", 0f);
            terrainMaterial.SetFloat("_HideRadius", 0f);
            terrainMaterial.SetFloat("_TransitionWidth", 1f);
            terrainMaterial.SetFloat("_RadialInset", 0f);
            terrainMaterial.SetFloat("_MinimumAmbient", 0.16f);
        }

        if (oceanMaterial != null)
        {
            oceanMaterial.SetColor("_DeepColor", visual.deepOceanColor);
            oceanMaterial.SetColor("_ShallowColor", visual.shallowOceanColor);
            oceanMaterial.SetFloat("_Smoothness", visual.oceanSmoothness);
            oceanMaterial.SetFloat("_WaveStrength", visual.oceanWaveStrength);
            oceanMaterial.SetFloat("_WaveScale", visual.oceanWaveScale);
            oceanMaterial.SetFloat("_WaveSpeed", visual.oceanWaveSpeed);
            oceanMaterial.SetFloat("_WaveHeight", Mathf.Lerp(
                0.04f,
                Mathf.Max(0.12f, preset.maximumTerrainElevation * 0.028f),
                visual.oceanWaveStrength));
            oceanMaterial.SetFloat("_NormalStrength", visual.oceanNormalStrength);
            oceanMaterial.SetFloat("_FoamStrength", visual.oceanFoamStrength);
            oceanMaterial.SetFloat("_RefractionStrength", visual.oceanRefractionStrength);
            oceanMaterial.SetFloat("_Opacity", visual.oceanOpacity);
            oceanMaterial.SetFloat("_OceanRadius", CalculateOceanRadius());
        }

        if (oceanRenderer != null)
            oceanRenderer.enabled = visual.oceanEnabled && oceanMesh != null;
        ApplyRuntimeCenters();
    }

    void ApplyPreviewSettings()
    {
        if (preset == null)
            return;

        ProceduralPlanetLabPreviewSettings settings = preset.preview;
        if (previewCamera != null)
        {
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = settings.backgroundColor;
            previewCamera.fieldOfView = 40f;
            previewCamera.nearClipPlane = Mathf.Max(0.03f, preset.radius * 0.01f);
            previewCamera.farClipPlane = Mathf.Max(1000f, preset.radius * 20f);
            Quaternion orbit = Quaternion.Euler(
                settings.cameraPitch,
                settings.cameraYaw,
                0f);
            Vector3 center = transform.position;
            previewCamera.transform.position = center
                + orbit * (Vector3.back * preset.radius * settings.cameraDistance);
            previewCamera.transform.rotation = Quaternion.LookRotation(
                center - previewCamera.transform.position,
                Vector3.up);
        }

        if (sunLight != null)
        {
            sunLight.type = LightType.Directional;
            sunLight.color = settings.sunColor;
            sunLight.intensity = settings.sunIntensity;
            sunLight.transform.rotation = Quaternion.Euler(settings.sunEuler);
        }

        if (fillLight != null)
        {
            fillLight.type = LightType.Directional;
            fillLight.color = settings.fillColor;
            fillLight.intensity = settings.fillIntensity;
            fillLight.transform.rotation = Quaternion.Euler(settings.fillEuler);
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = settings.ambientColor;
        RenderSettings.fog = false;
    }

    void RebuildOceanIfNeeded(int resolution)
    {
        if (preset == null)
            return;
        float requestedRadius = CalculateOceanRadius();
        int oceanResolution = Mathf.Clamp(resolution, 8, 128);
        if (oceanMesh == null
            || !Mathf.Approximately(requestedRadius, builtOceanRadius)
            || builtOceanResolution != oceanResolution)
        {
            RebuildOcean(oceanResolution);
        }
    }

    void RebuildOcean(int resolution)
    {
        if (preset == null || oceanFilter == null)
            return;

        builtOceanRadius = CalculateOceanRadius();
        builtOceanResolution = Mathf.Clamp(resolution, 8, 128);
        Mesh replacement = PlanetLodMeshBuilder.BuildOcean(
            builtOceanRadius,
            builtOceanResolution);
        replacement.name = "PlanetLabOcean_" + builtOceanResolution;
        replacement.hideFlags = HideFlags.HideAndDontSave;
        oceanFilter.sharedMesh = replacement;
        DestroyTransient(oceanMesh);
        oceanMesh = replacement;
    }

    float CalculateOceanRadius()
    {
        return preset.radius
            + preset.visual.oceanLevel * Mathf.Max(1f, preset.maximumTerrainElevation);
    }

    void ApplyRuntimeCenters()
    {
        Vector3 center = transform.position;
        Vector4 shaderCenter = new Vector4(center.x, center.y, center.z, 1f);
        if (terrainMaterial != null)
        {
            terrainMaterial.SetVector("_PlanetCenter", shaderCenter);
            terrainMaterial.SetVector("_HideCenter", shaderCenter);
        }
        if (oceanMaterial != null)
            oceanMaterial.SetVector("_PlanetCenter", shaderCenter);
    }

    void SetRenderersEnabled(bool value)
    {
        if (terrainRenderer != null)
            terrainRenderer.enabled = value;
        if (oceanRenderer != null)
            oceanRenderer.enabled = value
                && preset != null
                && preset.visual.oceanEnabled;
    }

    void CleanupTransientResources()
    {
        if (terrainFilter != null && terrainFilter.sharedMesh == terrainMesh)
            terrainFilter.sharedMesh = null;
        if (oceanFilter != null && oceanFilter.sharedMesh == oceanMesh)
            oceanFilter.sharedMesh = null;
        if (terrainRenderer != null && terrainRenderer.sharedMaterial == terrainMaterial)
            terrainRenderer.sharedMaterial = null;
        if (oceanRenderer != null && oceanRenderer.sharedMaterial == oceanMaterial)
            oceanRenderer.sharedMaterial = null;

        DestroyTransient(terrainMesh);
        DestroyTransient(oceanMesh);
        DestroyTransient(terrainMaterial);
        DestroyTransient(oceanMaterial);
        DestroyPreviewTexture();
        terrainMesh = null;
        oceanMesh = null;
        terrainMaterial = null;
        oceanMaterial = null;
        builtOceanRadius = float.NaN;
        builtOceanResolution = 0;
    }

    void DestroyPreviewTexture()
    {
        if (previewTexture == null)
            return;
        previewTexture.Release();
        DestroyTransient(previewTexture);
        previewTexture = null;
    }

    static void DestroyTransient(UnityEngine.Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }
}
