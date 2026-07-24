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

    [Header("Planar Experiment")]
    [SerializeField] PlanetLabSurfaceMode surfaceMode;
    [SerializeField] GameObject globeRoot;
    [SerializeField] GameObject planarRoot;
    [SerializeField] MeshFilter planarTerrainFilter;
    [SerializeField] MeshRenderer planarTerrainRenderer;
    [SerializeField] MeshCollider planarTerrainCollider;
    [SerializeField] MeshFilter planarOceanFilter;
    [SerializeField] MeshRenderer planarOceanRenderer;
    [SerializeField] GameObject planarPlayer;
    [SerializeField] PlanetLabFirstPersonController planarPlayerController;
    [SerializeField] Camera planarPlayerCamera;
    [SerializeField] PlanetLabTerrainPbrLibrary planarPbrLibrary;
    [SerializeField] PlanetLabSkyboxLibrary planarSkyboxLibrary;
    [SerializeField] GameObject infiniteTerrainRoot;
    [SerializeField] PlanetLabInfiniteTerrainStreamer infiniteTerrainStreamer;

    [Header("Active Preset")]
    [SerializeField] ProceduralPlanetPreset preset;

    Mesh terrainMesh;
    Mesh oceanMesh;
    Mesh planarTerrainMesh;
    Mesh planarOceanMesh;
    Material terrainMaterial;
    Material oceanMaterial;
    Material planarTerrainMaterial;
    Material planarOceanMaterial;
    RenderTexture previewTexture;
    bool rebuildPending;
    double rebuildAt;
    int terrainBuildRevision;
    float builtOceanRadius = float.NaN;
    int builtOceanResolution;
    int planarBuildRevision;
    Vector3 builtPlanarAnchor = Vector3.up;
    Vector3 planarSpawnPosition;
    float builtPlanarSeaHeight;
    double lastEditorUpdate;
    PlanetLabSkyboxEntry selectedPlanarSkybox;
    Material originalSkybox;
    bool originalSkyboxCaptured;

    public ProceduralPlanetPreset Preset => preset;
    public Camera PreviewCamera => previewCamera;
    public Mesh TerrainMesh => terrainMesh;
    public Mesh OceanMesh => oceanMesh;
    public int TerrainBuildRevision => terrainBuildRevision;
    public int PlanarBuildRevision => planarBuildRevision;
    public float OceanRadius => builtOceanRadius;
    public PlanetLabSurfaceMode SurfaceMode => surfaceMode;
    public Mesh PlanarTerrainMesh => planarTerrainMesh;
    public Mesh PlanarOceanMesh => planarOceanMesh;
    public Vector3 PlanarAnchorDirection => builtPlanarAnchor;
    public Vector3 PlanarSpawnPosition => planarSpawnPosition;
    public float PlanarSeaHeight => builtPlanarSeaHeight;
    public PlanetLabTerrainPbrLibrary PlanarPbrLibrary => planarPbrLibrary;
    public PlanetLabSkyboxLibrary PlanarSkyboxLibrary => planarSkyboxLibrary;
    public Material CurrentPlanarSkybox
        => selectedPlanarSkybox != null
            ? selectedPlanarSkybox.material
            : null;
    public string CurrentPlanarSkyboxName
        => selectedPlanarSkybox != null
            ? selectedPlanarSkybox.id
            : "None";
    public PlanetLabInfiniteTerrainStreamer InfiniteTerrainStreamer
        => infiniteTerrainStreamer;
    public int InfiniteActiveChunkCount
        => infiniteTerrainStreamer != null
            ? infiniteTerrainStreamer.ActiveChunkCount
            : 0;
    public int GeneratedSceneObjectCount
        => (terrainFilter != null ? 1 : 0)
        + (oceanFilter != null ? 1 : 0)
        + (planarTerrainFilter != null ? 1 : 0)
        + (planarOceanFilter != null ? 1 : 0);

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
        if (globeRoot == null && valuePlanetRoot != null)
            globeRoot = valuePlanetRoot.gameObject;
    }

    public void ConfigurePlanarReferences(
        GameObject valuePlanarRoot,
        MeshFilter valueTerrainFilter,
        MeshRenderer valueTerrainRenderer,
        MeshCollider valueTerrainCollider,
        MeshFilter valueOceanFilter,
        MeshRenderer valueOceanRenderer,
        GameObject valuePlayer,
        PlanetLabFirstPersonController valuePlayerController,
        Camera valuePlayerCamera,
        GameObject valueInfiniteTerrainRoot = null,
        PlanetLabInfiniteTerrainStreamer valueInfiniteTerrainStreamer = null)
    {
        planarRoot = valuePlanarRoot;
        planarTerrainFilter = valueTerrainFilter;
        planarTerrainRenderer = valueTerrainRenderer;
        planarTerrainCollider = valueTerrainCollider;
        planarOceanFilter = valueOceanFilter;
        planarOceanRenderer = valueOceanRenderer;
        planarPlayer = valuePlayer;
        planarPlayerController = valuePlayerController;
        planarPlayerCamera = valuePlayerCamera;
        infiniteTerrainRoot = valueInfiniteTerrainRoot;
        infiniteTerrainStreamer = valueInfiniteTerrainStreamer;
        ApplySurfaceModeState();
    }

    public void ConfigurePbrLibrary(PlanetLabTerrainPbrLibrary value)
    {
        planarPbrLibrary = value;
        ApplyPlanarMaterialProperties();
    }

    public void ConfigureSkyboxLibrary(PlanetLabSkyboxLibrary value)
    {
        planarSkyboxLibrary = value;
        SelectPlanarSkybox();
        ApplySurfaceModeState();
        ApplyPreviewSettings();
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
        SelectPlanarSkybox();
        ApplyMaterialProperties();
        ApplyPreviewSettings();

        if (rebuildShape || terrainMesh == null)
            RequestRebuild();
        else
        {
            RebuildOceanIfNeeded(preset.previewResolution);
            if (planarTerrainMesh != null)
                ApplyPlanarMaterialProperties();
        }
        ApplySurfaceModeState();
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
        if (surfaceMode == PlanetLabSurfaceMode.Planar
            || planarTerrainMesh != null)
        {
            RebuildPlanarNow();
        }
        if (surfaceMode == PlanetLabSurfaceMode.InfinitePlanar
            || InfiniteActiveChunkCount > 0)
        {
            RebuildInfinitePlanarNow();
        }
        ApplyMaterialProperties();
        ApplyPreviewSettings();
        SetRenderersEnabled(true);
        ApplySurfaceModeState();
    }

    public void SetSurfaceMode(PlanetLabSurfaceMode value)
    {
        if (surfaceMode == value)
        {
            ApplySurfaceModeState();
            return;
        }

        surfaceMode = value;
        if (surfaceMode == PlanetLabSurfaceMode.Planar
            && planarTerrainMesh == null
            && preset != null)
        {
            RebuildPlanarNow();
        }
        if (surfaceMode == PlanetLabSurfaceMode.InfinitePlanar
            && InfiniteActiveChunkCount == 0
            && preset != null)
        {
            RebuildInfinitePlanarNow();
        }
        ApplySurfaceModeState();
        ApplyPreviewSettings();
    }

    public void RebuildPlanarNow()
    {
        if (preset == null
            || planarTerrainFilter == null
            || planarTerrainRenderer == null)
        {
            return;
        }

        preset.ClampValues();
        EnsurePlanarRuntimeMaterials();
        GalaxyPlanetDefinition definition = preset.CloneDefinition();
        PlanetLabPlanarPatchBuildResult result =
            PlanetLabPlanarPatchMeshBuilder.Build(
                definition,
                preset.planar,
                PlanetLabPlanarSettings.PatchResolution);

        Mesh previousTerrain = planarTerrainMesh;
        Mesh previousOcean = planarOceanMesh;
        planarTerrainMesh = result.terrainMesh;
        planarOceanMesh = result.oceanMesh;
        planarTerrainFilter.sharedMesh = planarTerrainMesh;
        if (planarOceanFilter != null)
            planarOceanFilter.sharedMesh = planarOceanMesh;
        if (planarTerrainCollider != null)
        {
            planarTerrainCollider.sharedMesh = null;
            planarTerrainCollider.sharedMesh = planarTerrainMesh;
        }

        builtPlanarAnchor = result.anchorDirection;
        planarSpawnPosition = result.spawnPosition;
        builtPlanarSeaHeight = result.seaHeight;
        if (preset.planar.autoAnchor)
            preset.planar.SetAnchorDirection(result.anchorDirection);
        planarPlayerController?.SetSpawnPosition(planarSpawnPosition, false);
        if (Application.isPlaying
            && surfaceMode == PlanetLabSurfaceMode.Planar
            && planarPlayerController != null)
        {
            planarPlayerController.ResetToSpawn();
        }

        DestroyTransient(previousTerrain);
        DestroyTransient(previousOcean);
        planarBuildRevision++;
        ApplyPlanarMaterialProperties();
        ApplySurfaceModeState();
        ApplyPreviewSettings();
    }

    public void RebuildInfinitePlanarNow()
    {
        if (preset == null || infiniteTerrainStreamer == null)
            return;

        preset.ClampValues();
        EnsurePlanarRuntimeMaterials();
        GalaxyPlanetDefinition definition = preset.CloneDefinition();
        Vector3 anchor = preset.planar.autoAnchor
            ? PlanetLabPlanarPatchMeshBuilder.FindBestLandAnchor(definition)
            : preset.planar.AnchorDirection;
        if (preset.planar.autoAnchor)
            preset.planar.SetAnchorDirection(anchor);

        PlanetLabPlanarPatchMeshBuilder.BuildTangentBasis(
            anchor,
            out Vector3 east,
            out Vector3 north);
        float spawnHeight = PlanetLabInfiniteTerrainStreamer.SampleInfiniteHeight(
            definition,
            anchor,
            east,
            north,
            0f,
            0f);
        builtPlanarAnchor = anchor;
        builtPlanarSeaHeight = preset.visual.oceanLevel
            * Mathf.Max(1f, preset.maximumTerrainElevation);
        planarSpawnPosition = new Vector3(0f, spawnHeight + 1.15f, 0f);
        planarPlayerController?.SetSpawnPosition(planarSpawnPosition, false);

        infiniteTerrainStreamer.Configure(
            definition,
            preset.planar,
            planarPlayer != null ? planarPlayer.transform : null,
            planarTerrainMaterial,
            planarOceanMaterial,
            preset.visual.oceanEnabled);
        if (Application.isPlaying
            && surfaceMode == PlanetLabSurfaceMode.InfinitePlanar
            && planarPlayerController != null)
        {
            planarPlayerController.ResetToSpawn();
        }

        planarBuildRevision++;
        ApplyPlanarMaterialProperties();
        ApplySurfaceModeState();
        ApplyPreviewSettings();
    }

    public void AutoLocatePlanarAnchor()
    {
        if (preset == null)
            return;

        preset.planar = preset.planar ?? new PlanetLabPlanarSettings();
        preset.planar.autoAnchor = true;
        Vector3 anchor = PlanetLabPlanarPatchMeshBuilder.FindBestLandAnchor(
            preset.CloneDefinition());
        preset.planar.SetAnchorDirection(anchor);
        if (surfaceMode == PlanetLabSurfaceMode.InfinitePlanar)
            RebuildInfinitePlanarNow();
        else
            RebuildPlanarNow();
    }

    public void ResetPlanarPlayerToSpawn()
    {
        planarPlayerController?.ResetToSpawn();
        if (surfaceMode == PlanetLabSurfaceMode.InfinitePlanar)
            infiniteTerrainStreamer?.RebuildImmediate();
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
        SelectPlanarSkybox();
        RebuildOceanIfNeeded(preset.previewResolution);
        ApplyMaterialProperties();
        ApplyPreviewSettings();
    }

    void OnEnable()
    {
        CaptureOriginalSkybox();
        SelectPlanarSkybox();
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
        lastEditorUpdate = EditorApplication.timeSinceStartup;
#endif
        if (preset != null && terrainFilter != null)
            RebuildNow(preset.previewResolution);
        ApplySurfaceModeState();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
        RestoreOriginalSkybox();
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
        if (surfaceMode != PlanetLabSurfaceMode.Globe
            || preset == null
            || planetRoot == null
            || !preset.preview.autoRotate)
        {
            return;
        }
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

    void EnsurePlanarRuntimeMaterials()
    {
        if (planarTerrainMaterial == null)
        {
            Shader shader = Shader.Find("VoxelPlanet/PlanetLabPlanarSurface");
            if (shader == null)
                throw new InvalidOperationException(
                    "Missing shader: VoxelPlanet/PlanetLabPlanarSurface");
            planarTerrainMaterial = new Material(shader)
            {
                name = "PlanetLabPlanarTerrainMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        if (planarTerrainRenderer != null)
            planarTerrainRenderer.sharedMaterial = planarTerrainMaterial;

        if (planarOceanMaterial == null)
        {
            Shader shader = Shader.Find("VoxelPlanet/PlanetLabPlanarOcean");
            if (shader == null)
                throw new InvalidOperationException(
                    "Missing shader: VoxelPlanet/PlanetLabPlanarOcean");
            planarOceanMaterial = new Material(shader)
            {
                name = "PlanetLabPlanarOceanMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
        }
        if (planarOceanRenderer != null)
            planarOceanRenderer.sharedMaterial = planarOceanMaterial;
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
        ApplyPlanarMaterialProperties();
        ApplyRuntimeCenters();
    }

    void ApplyPlanarMaterialProperties()
    {
        if (preset == null)
            return;

        PlanetLowPolyVisualProfile visual = preset.visual;
        if (planarTerrainMaterial != null)
        {
            planarTerrainMaterial.SetColor("_LowlandColor", visual.lowlandColor);
            planarTerrainMaterial.SetColor("_HighlandColor", visual.highlandColor);
            planarTerrainMaterial.SetColor("_CliffColor", visual.cliffColor);
            planarTerrainMaterial.SetColor("_RockColor", visual.rockColor);
            planarTerrainMaterial.SetColor("_AccentColor", visual.accentColor);
            planarTerrainMaterial.SetColor("_ShoreColor", visual.shoreColor);
            planarTerrainMaterial.SetColor("_SnowColor", visual.snowColor);
            planarTerrainMaterial.SetFloat("_FacetStrength", visual.facetStrength);
            planarTerrainMaterial.SetFloat("_LightingBands", visual.lightingBands);
            planarTerrainMaterial.SetFloat("_MacroColorSize", visual.macroColorSize);
            planarTerrainMaterial.SetFloat("_MacroVariation", visual.macroVariation);
            planarTerrainMaterial.SetFloat("_CliffSlope", visual.cliffSlope);
            planarTerrainMaterial.SetFloat(
                "_HeightScale",
                Mathf.Max(1f, preset.maximumTerrainElevation));
            planarTerrainMaterial.SetFloat(
                "_SeaHeight",
                visual.oceanLevel * Mathf.Max(1f, preset.maximumTerrainElevation));
            planarTerrainMaterial.SetFloat(
                "_ShoreWidth",
                visual.shoreWidth * Mathf.Max(1f, preset.maximumTerrainElevation));
            planarTerrainMaterial.SetFloat("_SnowLine", visual.snowLine);
            planarTerrainMaterial.SetFloat("_SnowAmount", visual.snowAmount);
            planarTerrainMaterial.SetFloat("_MinimumAmbient", 0.16f);
            ApplyPlanarPbrProperties();
        }

        if (planarOceanMaterial != null)
        {
            planarOceanMaterial.SetColor("_DeepColor", visual.deepOceanColor);
            planarOceanMaterial.SetColor("_ShallowColor", visual.shallowOceanColor);
            planarOceanMaterial.SetFloat("_WaveStrength", visual.oceanWaveStrength);
            planarOceanMaterial.SetFloat("_WaveScale", visual.oceanWaveScale);
            planarOceanMaterial.SetFloat("_WaveSpeed", visual.oceanWaveSpeed);
            planarOceanMaterial.SetFloat("_Opacity", visual.oceanOpacity);
        }
        if (planarOceanRenderer != null)
        {
            planarOceanRenderer.enabled = visual.oceanEnabled
                && planarOceanMesh != null
                && surfaceMode == PlanetLabSurfaceMode.Planar;
        }
        infiniteTerrainStreamer?.RefreshAppearance(
            planarTerrainMaterial,
            planarOceanMaterial,
            visual.oceanEnabled);
    }

    void ApplyPlanarPbrProperties()
    {
        if (planarTerrainMaterial == null)
            return;

        bool ready = planarPbrLibrary != null && planarPbrLibrary.IsReady;
        planarTerrainMaterial.SetFloat("_UsePbrLibrary", ready ? 1f : 0f);
        if (!ready || preset == null)
            return;

        planarTerrainMaterial.SetTexture(
            "_PbrAlbedoArray",
            planarPbrLibrary.albedoArray);
        planarTerrainMaterial.SetTexture(
            "_PbrNormalArray",
            planarPbrLibrary.normalArray);
        planarTerrainMaterial.SetTexture(
            "_PbrMaskArray",
            planarPbrLibrary.maskArray);
        planarTerrainMaterial.SetFloat(
            "_GroundSlice",
            planarPbrLibrary.SelectSlice(
                preset.seed,
                PlanetLabTerrainPbrRole.Ground,
                101,
                GetPreferredPbrIds(PlanetLabTerrainPbrRole.Ground)));
        planarTerrainMaterial.SetFloat(
            "_RockSlice",
            planarPbrLibrary.SelectSlice(
                preset.seed,
                PlanetLabTerrainPbrRole.Rock,
                211,
                GetPreferredPbrIds(PlanetLabTerrainPbrRole.Rock)));
        planarTerrainMaterial.SetFloat(
            "_ShoreSlice",
            planarPbrLibrary.SelectSlice(
                preset.seed,
                PlanetLabTerrainPbrRole.Shore,
                307,
                GetPreferredPbrIds(PlanetLabTerrainPbrRole.Shore)));
        planarTerrainMaterial.SetFloat(
            "_ColdSlice",
            planarPbrLibrary.SelectSlice(
                preset.seed,
                PlanetLabTerrainPbrRole.Cold,
                401,
                GetPreferredPbrIds(PlanetLabTerrainPbrRole.Cold)));
        planarTerrainMaterial.SetFloat("_PbrTiling", 0.22f);
        planarTerrainMaterial.SetFloat("_PbrNormalStrength", 0.52f);
        float colorStrength = preset.template switch
        {
            ProceduralPlanetLabTemplate.Frozen => 0.72f,
            ProceduralPlanetLabTemplate.Crystal => 0.66f,
            ProceduralPlanetLabTemplate.CrimsonOcean => 0.58f,
            _ => 0.46f
        };
        planarTerrainMaterial.SetFloat("_PbrColorStrength", colorStrength);
    }

    string[] GetPreferredPbrIds(PlanetLabTerrainPbrRole role)
    {
        switch (preset.template)
        {
            case ProceduralPlanetLabTemplate.Frozen:
                return role switch
                {
                    PlanetLabTerrainPbrRole.Ground => new[]
                    {
                        "frozen_earth",
                        "grass_snow",
                        "stone_grass_snow"
                    },
                    PlanetLabTerrainPbrRole.Rock => new[]
                    {
                        "ice_crack",
                        "cliff_stone_01",
                        "cliff_stone_02",
                        "rock_stone"
                    },
                    PlanetLabTerrainPbrRole.Shore => new[]
                    {
                        "ice",
                        "ice_crack",
                        "frozen_earth"
                    },
                    _ => new[]
                    {
                        "snow",
                        "snow_drift",
                        "ice",
                        "ice_crack",
                        "grass_snow",
                        "stone_grass_snow"
                    }
                };
            case ProceduralPlanetLabTemplate.Desert:
                return role switch
                {
                    PlanetLabTerrainPbrRole.Ground => new[]
                    {
                        "sand_01",
                        "sand_02",
                        "sand_03",
                        "dry_grass",
                        "ground_crack_01",
                        "ground_crack_02"
                    },
                    PlanetLabTerrainPbrRole.Rock => new[]
                    {
                        "rock_sand",
                        "cliff_01",
                        "cliff_02",
                        "breakstone",
                        "cracks"
                    },
                    PlanetLabTerrainPbrRole.Shore => new[]
                    {
                        "sand_01",
                        "sand_02",
                        "sand_03",
                        "rock_sand"
                    },
                    _ => new[] { "snow_drift", "snow" }
                };
            case ProceduralPlanetLabTemplate.CrimsonOcean:
                return role switch
                {
                    PlanetLabTerrainPbrRole.Ground => new[]
                    {
                        "mud_01",
                        "mud_02",
                        "mud_03",
                        "ground_crack_01",
                        "ground_crack_02"
                    },
                    PlanetLabTerrainPbrRole.Rock => new[]
                    {
                        "cracks",
                        "breakstone",
                        "broken_stones",
                        "cliff_02",
                        "rock"
                    },
                    PlanetLabTerrainPbrRole.Shore => new[]
                    {
                        "mud_01",
                        "mud_02",
                        "rock_sand",
                        "sand_03"
                    },
                    _ => new[] { "snow", "snow_drift" }
                };
            case ProceduralPlanetLabTemplate.Crystal:
                return role switch
                {
                    PlanetLabTerrainPbrRole.Ground => new[]
                    {
                        "ground_stone_01",
                        "ground_stone_02",
                        "frozen_earth",
                        "ground_rock"
                    },
                    PlanetLabTerrainPbrRole.Rock => new[]
                    {
                        "ice_crack",
                        "cracks",
                        "cliff_stone_01",
                        "cliff_stone_02",
                        "stones_02"
                    },
                    PlanetLabTerrainPbrRole.Shore => new[]
                    {
                        "ice",
                        "ice_crack",
                        "sand_02"
                    },
                    _ => new[] { "ice", "ice_crack", "snow" }
                };
            default:
                return role switch
                {
                    PlanetLabTerrainPbrRole.Ground => new[]
                    {
                        "grass_01",
                        "grass_02",
                        "ground_grass_01",
                        "ground_grass_02",
                        "ground_foliage_01",
                        "ground_foliage_02",
                        "grass_stones"
                    },
                    PlanetLabTerrainPbrRole.Rock => new[]
                    {
                        "ground_rock",
                        "ground_stone_01",
                        "ground_stone_02",
                        "cliff_stone_01",
                        "cliff_stone_02",
                        "rock_stone"
                    },
                    PlanetLabTerrainPbrRole.Shore => new[]
                    {
                        "sand_01",
                        "sand_02",
                        "mud_01",
                        "rock_sand"
                    },
                    _ => new[]
                    {
                        "snow",
                        "snow_drift",
                        "grass_snow",
                        "stone_grass_snow"
                    }
                };
        }
    }

    void ApplyPreviewSettings()
    {
        if (preset == null)
            return;

        ProceduralPlanetLabPreviewSettings settings = preset.preview;
        if (previewCamera != null)
        {
            bool useSkybox =
                surfaceMode != PlanetLabSurfaceMode.Globe
                && CurrentPlanarSkybox != null;
            previewCamera.clearFlags = useSkybox
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = settings.backgroundColor;
            previewCamera.fieldOfView = 40f;
            Quaternion orbit = Quaternion.Euler(
                settings.cameraPitch,
                settings.cameraYaw,
                0f);
            bool planar = surfaceMode != PlanetLabSurfaceMode.Globe;
            Vector3 center = planar
                ? transform.TransformPoint(new Vector3(0f, builtPlanarSeaHeight, 0f))
                : transform.position;
            float distance = planar
                ? PlanetLabPlanarSettings.PatchSize
                    * Mathf.Lerp(0.55f, 1.1f, settings.cameraDistance / 8f)
                : preset.radius * settings.cameraDistance;
            previewCamera.nearClipPlane = planar
                ? 0.1f
                : Mathf.Max(0.03f, preset.radius * 0.01f);
            previewCamera.farClipPlane = planar
                ? 3000f
                : Mathf.Max(1000f, preset.radius * 20f);
            previewCamera.transform.position = center + orbit * (Vector3.back * distance);
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

    void ApplySurfaceModeState()
    {
        bool planar = surfaceMode != PlanetLabSurfaceMode.Globe;
        bool fixedPlanar = surfaceMode == PlanetLabSurfaceMode.Planar;
        bool infinitePlanar = surfaceMode == PlanetLabSurfaceMode.InfinitePlanar;
        ApplyPlanarSkyboxState(planar);
        if (globeRoot == null && planetRoot != null)
            globeRoot = planetRoot.gameObject;
        if (globeRoot != null)
            globeRoot.SetActive(!planar);
        if (planarRoot != null)
            planarRoot.SetActive(planar);
        if (planarTerrainFilter != null)
            planarTerrainFilter.gameObject.SetActive(fixedPlanar);
        if (planarOceanFilter != null)
            planarOceanFilter.gameObject.SetActive(fixedPlanar);
        if (infiniteTerrainRoot != null)
            infiniteTerrainRoot.SetActive(infinitePlanar);

        bool planarPlay = planar && Application.isPlaying;
        if (previewCamera != null)
            previewCamera.enabled = !planarPlay;
        if (planarPlayer != null)
            planarPlayer.SetActive(planar);
        if (planarPlayerCamera != null)
        {
            planarPlayerCamera.clearFlags =
                planar && CurrentPlanarSkybox != null
                    ? CameraClearFlags.Skybox
                    : CameraClearFlags.SolidColor;
            planarPlayerCamera.enabled = planarPlay;
        }
        if (planarPlayerController != null)
        {
            planarPlayerController.SetInfiniteWorld(infinitePlanar);
            planarPlayerController.enabled = planarPlay;
        }
        if (planarTerrainRenderer != null)
            planarTerrainRenderer.enabled = fixedPlanar
                && planarTerrainMesh != null;
        if (planarOceanRenderer != null)
        {
            planarOceanRenderer.enabled = fixedPlanar
                && planarOceanMesh != null
                && preset != null
                && preset.visual.oceanEnabled;
        }
    }

    void SelectPlanarSkybox()
    {
        selectedPlanarSkybox =
            preset != null && planarSkyboxLibrary != null
                ? planarSkyboxLibrary.SelectEntry(preset.seed, preset.template)
                : null;
    }

    void ApplyPlanarSkyboxState(bool planar)
    {
        CaptureOriginalSkybox();
        RenderSettings.skybox =
            planar && CurrentPlanarSkybox != null
                ? CurrentPlanarSkybox
                : null;
    }

    void CaptureOriginalSkybox()
    {
        if (originalSkyboxCaptured)
            return;
        originalSkybox = RenderSettings.skybox;
        originalSkyboxCaptured = true;
    }

    void RestoreOriginalSkybox()
    {
        if (!originalSkyboxCaptured)
            return;
        RenderSettings.skybox = originalSkybox;
        originalSkybox = null;
        originalSkyboxCaptured = false;
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
        if (planarTerrainFilter != null
            && planarTerrainFilter.sharedMesh == planarTerrainMesh)
        {
            planarTerrainFilter.sharedMesh = null;
        }
        if (planarOceanFilter != null
            && planarOceanFilter.sharedMesh == planarOceanMesh)
        {
            planarOceanFilter.sharedMesh = null;
        }
        if (planarTerrainCollider != null
            && planarTerrainCollider.sharedMesh == planarTerrainMesh)
        {
            planarTerrainCollider.sharedMesh = null;
        }
        if (planarTerrainRenderer != null
            && planarTerrainRenderer.sharedMaterial == planarTerrainMaterial)
        {
            planarTerrainRenderer.sharedMaterial = null;
        }
        if (planarOceanRenderer != null
            && planarOceanRenderer.sharedMaterial == planarOceanMaterial)
        {
            planarOceanRenderer.sharedMaterial = null;
        }

        DestroyTransient(terrainMesh);
        DestroyTransient(oceanMesh);
        DestroyTransient(planarTerrainMesh);
        DestroyTransient(planarOceanMesh);
        DestroyTransient(terrainMaterial);
        DestroyTransient(oceanMaterial);
        DestroyTransient(planarTerrainMaterial);
        DestroyTransient(planarOceanMaterial);
        DestroyPreviewTexture();
        terrainMesh = null;
        oceanMesh = null;
        planarTerrainMesh = null;
        planarOceanMesh = null;
        terrainMaterial = null;
        oceanMaterial = null;
        planarTerrainMaterial = null;
        planarOceanMaterial = null;
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
