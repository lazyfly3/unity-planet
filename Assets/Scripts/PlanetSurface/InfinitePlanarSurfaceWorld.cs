using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-8500)]
[DisallowMultipleComponent]
public sealed class InfinitePlanarSurfaceWorld :
    MonoBehaviour,
    IPlanetSurfaceRuntime,
    IPlanetWaterSampler,
    IPlanetHarvestPersistenceSink
{
    public const float RebaseThreshold = 2048f;
    public const float ChunkSize = PlanetLabPlanarSettings.InfiniteChunkSize;

    readonly HashSet<string> harvestedIds = new HashSet<string>();
    readonly HashSet<Transform> shiftedTransforms = new HashSet<Transform>();

    GalaxyPlanetDefinition definition;
    GalaxyPlanetSaveData loadedSave;
    VoxelPlanetPlayerController player;
    PlanetLabInfiniteTerrainStreamer streamer;
    PlanarSurfaceContentStreamer contentStreamer;
    PlanarDroppedObjectSystem droppedObjectSystem;
    PlanarCityRuntimeSystem cityRuntimeSystem;
    Material terrainMaterial;
    Material oceanMaterial;
    Material previousSkybox;
    Camera skyboxCamera;
    CameraClearFlags previousCameraClearFlags;
    double globalOriginX;
    double globalOriginZ;
    bool configured;
    bool initialPlayerPlaced;
    Transform movementTarget;

    public PlanetSurfaceTopology Topology =>
        PlanetSurfaceTopology.InfinitePlanar;
    public Vector3 AnchorDirection { get; private set; } = Vector3.up;
    public bool IsCenterCollisionReady =>
        streamer != null && streamer.IsCenterChunkReady;
    public bool IsFullyReady =>
        streamer != null
        && streamer.IsFullyReady
        && initialPlayerPlaced
        && IsInitialContentReady;
    public bool IsInitialPlayerPlaced => initialPlayerPlaced;
    public bool IsInitialContentReady =>
        contentStreamer == null || contentStreamer.IsInitialWindowReady;
    public bool OceanEnabled { get; private set; }
    public float SeaHeight =>
        streamer != null ? streamer.SeaHeight : 0f;
    public double GlobalOriginX => globalOriginX;
    public double GlobalOriginZ => globalOriginZ;
    public PlanetLabInfiniteTerrainStreamer Streamer => streamer;
    public GalaxyPlanetDefinition Definition => definition;
    public VoxelPlanetPlayerController Player => player;
    public PlanarDroppedObjectSystem DroppedObjectSystem =>
        droppedObjectSystem;
    public PlanarCityRuntimeSystem CityRuntimeSystem =>
        cityRuntimeSystem;

    public void Configure(
        GalaxyPlanetDefinition valueDefinition,
        GalaxyPlanetSaveData save,
        PendingPlanetLandingContext landing,
        VoxelPlanetPlayerController valuePlayer,
        List<PlanetSurfacePropSpawnSettings> surfacePropPlan)
    {
        definition = valueDefinition;
        loadedSave = save;
        player = valuePlayer;
        movementTarget = player != null ? player.transform : null;
        if (definition == null)
            throw new System.ArgumentNullException(nameof(valueDefinition));

        PlanetLowPolyVisualProfile visual =
            definition.lowPolyVisual ?? new PlanetLowPolyVisualProfile();
        visual.ClampValues();
        OceanEnabled = visual.oceanEnabled;
        AnchorDirection = ResolveAnchor(definition, save, landing);
        RestoreHarvestedIds(save);

        var settings = new PlanetLabPlanarSettings
        {
            autoAnchor = false
        };
        settings.SetAnchorDirection(AnchorDirection);

        PlanetLabPlanarRuntimeAssets assets =
            Resources.Load<PlanetLabPlanarRuntimeAssets>(
                "PlanetSurface/PlanetLabPlanarRuntimeAssets");
        ProceduralPlanetLabTemplate template =
            PlanetLabPlanarMaterialFactory.ResolveTemplate(
                definition.climate,
                OceanEnabled);
        terrainMaterial =
            PlanetLabPlanarMaterialFactory.CreateTerrainMaterial(
                definition,
                assets != null ? assets.terrainPbrLibrary : null,
                template);
        oceanMaterial =
            PlanetLabPlanarMaterialFactory.CreateOceanMaterial(definition);

        previousSkybox = RenderSettings.skybox;
        Material skybox = PlanetLabPlanarMaterialFactory.SelectSkybox(
            definition,
            assets != null ? assets.skyboxLibrary : null,
            template);
        if (skybox != null)
            RenderSettings.skybox = skybox;
        skyboxCamera = Camera.main;
        if (skyboxCamera != null)
        {
            previousCameraClearFlags = skyboxCamera.clearFlags;
            skyboxCamera.clearFlags = CameraClearFlags.Skybox;
        }
        RenderSettings.fog = false;
        RenderSettings.ambientMode =
            UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = visual.groundAmbientColor;

        GameObject terrainRoot = new GameObject("InfiniteTerrain");
        terrainRoot.transform.SetParent(transform, false);
        streamer =
            terrainRoot.AddComponent<PlanetLabInfiniteTerrainStreamer>();
        streamer.ChunkActivated += OnChunkActivated;
        streamer.ChunkRecycled += OnChunkRecycled;
        streamer.Configure(
            definition,
            settings,
            player != null ? player.transform : null,
            terrainMaterial,
            oceanMaterial,
            OceanEnabled,
            PlanetLabPlanarSettings.InfiniteViewRadius,
            PlanetLabPlanarSettings.InfiniteChunkResolution,
            PlanetLabPlanarSettings.InfiniteChunkSize);

        ResolveInitialGlobalPosition(
            save,
            out double playerX,
            out double playerZ);
        globalOriginX =
            System.Math.Floor(playerX / ChunkSize) * ChunkSize;
        globalOriginZ =
            System.Math.Floor(playerZ / ChunkSize) * ChunkSize;
        streamer.SetGlobalOrigin(globalOriginX, globalOriginZ);
        droppedObjectSystem =
            GetComponent<PlanarDroppedObjectSystem>();
        if (droppedObjectSystem == null)
        {
            droppedObjectSystem =
                gameObject.AddComponent<PlanarDroppedObjectSystem>();
        }
        droppedObjectSystem.Configure(
            this,
            streamer,
            definition.celestial,
            save != null ? save.droppedObjects : null);
        cityRuntimeSystem =
            GetComponent<PlanarCityRuntimeSystem>();
        if (cityRuntimeSystem == null)
        {
            cityRuntimeSystem =
                gameObject.AddComponent<
                    PlanarCityRuntimeSystem>();
        }
        cityRuntimeSystem.Configure(
            this,
            streamer,
            droppedObjectSystem,
            player,
            save != null ? save.planarCities : null);
        if (player != null)
        {
            if (player.gameObject
                .GetComponent<PlanetFloatingOriginParticipant>() == null)
            {
                player.gameObject
                    .AddComponent<PlanetFloatingOriginParticipant>();
            }
            player.SetSurfacePhysicsReady(false);
            player.SetGameplayInputBlocked(true);
        }

        // Random surface content is intentionally disabled for the new
        // planar topology. Keep the streamer implementation available for
        // future authored content, but do not generate resources, trees,
        // vegetation, landmarks, or ground cover here.
        contentStreamer = null;
        configured = true;
        PlanetSurfaceRuntimeRegistry.Register(this);
        PlanetWaterRegistry.Register(this);
        StartCoroutine(PlacePlayerWhenCollisionReady(playerX, playerZ));
    }

    public void SetMovementTarget(Transform value)
    {
        movementTarget = value != null
            ? value
            : player != null ? player.transform : null;
        streamer?.SetTarget(movementTarget);
    }

    public bool TryDropPlaceholderCube(
        Rigidbody shipBody,
        Collider[] shipColliders,
        out string failureReason)
    {
        if (droppedObjectSystem == null)
        {
            failureReason = "投放系统尚未就绪";
            return false;
        }
        return droppedObjectSystem.TryDropPlaceholderCube(
            shipBody,
            shipColliders,
            out failureReason);
    }

    public Vector3 GetUp(Vector3 worldPosition)
    {
        return Vector3.up;
    }

    public Vector3 GetGravity(Vector3 worldPosition)
    {
        PlanetCelestialProfile celestial =
            definition != null && definition.celestial != null
                ? definition.celestial
                : PlanetCelestialProfile.CreateCompatibleDefault();
        float gravity = Mathf.Clamp(
            (float)celestial.Physical.surfaceGravity,
            0.1f,
            100f);
        return Vector3.down * gravity;
    }

    public bool TryProjectToSurface(
        Vector3 worldPosition,
        out PlanetSurfaceSample sample)
    {
        sample = default;
        if (streamer == null)
            return false;
        PlanarSurfaceAddress address = ToPersistentAddress(worldPosition);
        if (!streamer.TrySampleSurface(
                address.x,
                address.z,
                out float height,
                out Vector3 normal))
        {
            return false;
        }
        sample.height = height;
        sample.normal = normal;
        sample.point = FromPersistentAddress(
            new PlanarSurfaceAddress(address.x, address.z, height));
        sample.isWater = OceanEnabled && height < SeaHeight;
        return true;
    }

    public bool TryFindLandingPoint(
        Vector3 nearWorldPosition,
        float footprintRadius,
        float maximumSlopeDegrees,
        out PlanetSurfaceSample sample)
    {
        PlanarSurfaceAddress center = ToPersistentAddress(nearWorldPosition);
        float slopeDot = Mathf.Cos(
            Mathf.Clamp(maximumSlopeDegrees, 0f, 89f)
            * Mathf.Deg2Rad);
        float radiusStep = Mathf.Max(footprintRadius * 1.5f, 8f);
        for (int ring = 0; ring <= 12; ring++)
        {
            int count = ring == 0 ? 1 : 12;
            for (int index = 0; index < count; index++)
            {
                float angle = index * Mathf.PI * 2f / count;
                double x = center.x + Mathf.Cos(angle) * ring * radiusStep;
                double z = center.z + Mathf.Sin(angle) * ring * radiusStep;
                Vector3 probe = FromPersistentAddress(
                    new PlanarSurfaceAddress(x, z, 0f));
                if (!TryProjectToSurface(probe, out sample)
                    || Vector3.Dot(sample.normal, Vector3.up) < slopeDot
                    || sample.isWater)
                {
                    continue;
                }
                return true;
            }
        }
        sample = default;
        return false;
    }

    public PlanarSurfaceAddress ToPersistentAddress(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        return new PlanarSurfaceAddress(
            globalOriginX + local.x,
            globalOriginZ + local.z,
            local.y);
    }

    public Vector3 FromPersistentAddress(PlanarSurfaceAddress address)
    {
        return transform.TransformPoint(new Vector3(
            (float)(address.x - globalOriginX),
            address.y,
            (float)(address.z - globalOriginZ)));
    }

    public void MarkHarvested(string stableId)
    {
        if (!string.IsNullOrWhiteSpace(stableId))
            harvestedIds.Add(stableId);
    }

    public bool IsHarvested(string stableId)
    {
        return !string.IsNullOrWhiteSpace(stableId)
            && harvestedIds.Contains(stableId);
    }

    public string[] GetHarvestedIds()
    {
        var values = new string[harvestedIds.Count];
        harvestedIds.CopyTo(values);
        System.Array.Sort(values, System.StringComparer.Ordinal);
        return values;
    }

    public void CapturePlanarState(GalaxyPlanetSaveData data)
    {
        if (data == null)
            return;
        data.surfaceTopology = PlanetSurfaceTopology.InfinitePlanar;
        data.planarAnchorDirection = AnchorDirection;
        data.harvestedResourceIds = GetHarvestedIds();
        data.harvestedSurfacePropIds = GetHarvestedIds();
        data.hasFullResourceSnapshot = true;
        data.hasFullSurfacePropSnapshot = true;
        data.droppedObjects = droppedObjectSystem != null
            ? droppedObjectSystem.CaptureSnapshots()
            : new GalaxyDroppedObjectSaveEntry[0];
        data.planarCities = cityRuntimeSystem != null
            ? cityRuntimeSystem.CaptureSnapshots()
            : new GalaxyPlanarCitySaveEntry[0];
        if (player != null)
        {
            PlanarSurfaceAddress address =
                ToPersistentAddress(player.transform.position);
            data.hasPlanarPlayerPosition = true;
            data.planarPlayerX = address.x;
            data.planarPlayerZ = address.z;
        }
    }

    void Update()
    {
        if (!configured)
            return;
        // Legacy surface camera setup can restore SolidColor after the travel
        // manager has configured the scene. The planar runtime owns the fixed
        // sky presentation, so keep the gameplay camera on the selected
        // deterministic skybox.
        if (skyboxCamera == null)
            skyboxCamera = Camera.main;
        if (skyboxCamera != null
            && skyboxCamera.clearFlags != CameraClearFlags.Skybox)
        {
            skyboxCamera.clearFlags = CameraClearFlags.Skybox;
        }
        if (RenderSettings.fog)
            RenderSettings.fog = false;

        Transform target = movementTarget != null
            ? movementTarget
            : player != null ? player.transform : null;
        if (target == null)
            return;
        Vector3 local = transform.InverseTransformPoint(target.position);
        if (Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.z))
            <= RebaseThreshold)
        {
            return;
        }
        float shiftX =
            Mathf.Floor(local.x / ChunkSize) * ChunkSize;
        float shiftZ =
            Mathf.Floor(local.z / ChunkSize) * ChunkSize;
        Rebase(shiftX, shiftZ);
    }

    void Rebase(float shiftX, float shiftZ)
    {
        if (Mathf.Abs(shiftX) < 0.01f && Mathf.Abs(shiftZ) < 0.01f)
            return;
        Vector3 shift = new Vector3(shiftX, 0f, shiftZ);
        shiftedTransforms.Clear();

        PlanetFloatingOriginParticipant[] participants =
            FindObjectsOfType<PlanetFloatingOriginParticipant>(true);
        for (int index = 0; index < participants.Length; index++)
        {
            if (participants[index]
                .GetComponent<BuildingAnchor>() == null)
            {
                ShiftRoot(participants[index].transform, shift);
            }
        }
        foreach (BuildingAnchor anchor in BuildingAnchor.GetActiveAnchors())
        {
            if (anchor != null)
            {
                if (shiftedTransforms.Add(anchor.transform))
                    anchor.ShiftWorldOffset(-shift);
            }
        }

        globalOriginX += shiftX;
        globalOriginZ += shiftZ;
        streamer.SetGlobalOrigin(globalOriginX, globalOriginZ);
        Physics.SyncTransforms();
    }

    void ShiftRoot(Transform value, Vector3 shift)
    {
        if (value == null)
            return;
        Transform root = value;
        while (root.parent != null
            && root.parent.GetComponent<InfinitePlanarSurfaceWorld>() == null)
        {
            root = root.parent;
        }
        if (root == transform || !shiftedTransforms.Add(root))
            return;
        root.position -= shift;
    }

    IEnumerator PlacePlayerWhenCollisionReady(double x, double z)
    {
        while (streamer != null && !streamer.IsCenterChunkReady)
            yield return null;
        if (streamer == null || player == null)
            yield break;

        streamer.TrySampleSurface(
            x,
            z,
            out float height,
            out Vector3 normal);
        if (OceanEnabled && height <= SeaHeight + 0.5f)
        {
            if (TryFindLandingPoint(
                FromPersistentAddress(
                    new PlanarSurfaceAddress(x, z, height)),
                2f,
                48f,
                out PlanetSurfaceSample safe))
            {
                PlanarSurfaceAddress safeAddress =
                    ToPersistentAddress(safe.point);
                x = safeAddress.x;
                z = safeAddress.z;
                height = safe.height;
                normal = safe.normal;
            }
        }

        Vector3 position = FromPersistentAddress(
            new PlanarSurfaceAddress(x, z, height + 1.2f));
        Vector3 forward = Vector3.ProjectOnPlane(
            player.transform.forward,
            Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        player.TeleportTo(
            position,
            Quaternion.LookRotation(forward.normalized, Vector3.up));
        initialPlayerPlaced = true;
    }

    static Vector3 ResolveAnchor(
        GalaxyPlanetDefinition definition,
        GalaxyPlanetSaveData save,
        PendingPlanetLandingContext landing)
    {
        if (landing != null
            && landing.landingDirection.sqrMagnitude > 0.001f)
        {
            return landing.landingDirection.normalized;
        }
        if (save != null
            && save.planarAnchorDirection.sqrMagnitude > 0.001f)
        {
            return save.planarAnchorDirection.normalized;
        }
        return PlanetLabPlanarPatchMeshBuilder.FindBestLandAnchor(
            definition);
    }

    static void ResolveInitialGlobalPosition(
        GalaxyPlanetSaveData save,
        out double x,
        out double z)
    {
        if (save != null && save.hasPlanarPlayerPosition)
        {
            x = save.planarPlayerX;
            z = save.planarPlayerZ;
            return;
        }
        x = 0d;
        z = 0d;
    }

    void RestoreHarvestedIds(GalaxyPlanetSaveData save)
    {
        harvestedIds.Clear();
        if (save?.harvestedResourceIds != null)
        {
            foreach (string id in save.harvestedResourceIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    harvestedIds.Add(id);
            }
        }
        if (save?.harvestedSurfacePropIds != null)
        {
            foreach (string id in save.harvestedSurfacePropIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    harvestedIds.Add(id);
            }
        }
    }

    void OnChunkActivated(Vector2Int coordinate)
    {
    }

    void OnChunkRecycled(Vector2Int coordinate)
    {
    }

    public bool TrySample(
        Vector3 worldPosition,
        out WaterSample sample)
    {
        sample = default;
        if (!OceanEnabled || streamer == null)
            return false;
        PlanarSurfaceAddress address = ToPersistentAddress(worldPosition);
        streamer.TrySampleSurface(
            address.x,
            address.z,
            out float terrainHeight,
            out _);
        sample.surfacePoint = FromPersistentAddress(
            new PlanarSurfaceAddress(address.x, address.z, SeaHeight));
        sample.surfaceNormal = Vector3.up;
        sample.flowVelocity = Vector3.zero;
        sample.depth = Mathf.Max(0.1f, SeaHeight - terrainHeight);
        sample.signedDistance = worldPosition.y - sample.surfacePoint.y;
        PlanetLowPolyVisualProfile visual =
            definition.lowPolyVisual ?? new PlanetLowPolyVisualProfile();
        sample.tint = Color.Lerp(
            visual.shallowOceanColor,
            visual.deepOceanColor,
            Mathf.Clamp01(sample.depth / 20f));
        sample.kind = PlanetWaterKind.Ocean;
        return true;
    }

    void OnDestroy()
    {
        PlanetSurfaceRuntimeRegistry.Unregister(this);
        PlanetWaterRegistry.Unregister(this);
        if (streamer != null)
        {
            streamer.ChunkActivated -= OnChunkActivated;
            streamer.ChunkRecycled -= OnChunkRecycled;
        }
        if (RenderSettings.skybox != previousSkybox)
            RenderSettings.skybox = previousSkybox;
        if (skyboxCamera != null)
            skyboxCamera.clearFlags = previousCameraClearFlags;
        DestroyTransient(terrainMaterial);
        DestroyTransient(oceanMaterial);
    }

    static void DestroyTransient(Object value)
    {
        if (value == null)
            return;
        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }
}

[DisallowMultipleComponent]
public sealed class InfinitePlanarSurfaceEntryCoordinator : MonoBehaviour
{
    InfinitePlanarSurfaceWorld world;
    PlanetLoadingUI loadingUI;
    PlanarSurfaceLandedSpacecraftRestorer restorer;
    Coroutine routine;

    public bool IsReadyForReveal { get; private set; }

    public void Initialize(
        InfinitePlanarSurfaceWorld targetWorld,
        PlanetLoadingUI targetLoadingUI)
    {
        world = targetWorld;
        loadingUI = targetLoadingUI
            ?? FindObjectOfType<PlanetLoadingUI>(true);
        loadingUI?.Show("正在准备无限平面地表");
        if (routine != null)
            StopCoroutine(routine);
        routine = StartCoroutine(WaitForReady());
    }

    public void BindRestorer(
        PlanarSurfaceLandedSpacecraftRestorer value)
    {
        restorer = value;
    }

    IEnumerator WaitForReady()
    {
        while (world != null && !world.IsCenterCollisionReady)
        {
            loadingUI?.SetProgress(0.25f, "正在生成着陆区碰撞");
            yield return null;
        }
        while (world != null && !world.IsInitialPlayerPlaced)
        {
            loadingUI?.SetProgress(0.55f, "正在校准安全出生点");
            yield return null;
        }
        while (world != null && !world.IsFullyReady)
        {
            float progress = world.Streamer != null
                ? Mathf.Lerp(
                    0.55f,
                    0.95f,
                    world.Streamer.ActiveChunkCount / 25f)
                : 0.55f;
            loadingUI?.SetProgress(progress, "正在生成 5×5 活动区块");
            yield return null;
        }
        while (restorer != null && !restorer.IsRestoreComplete)
        {
            if (restorer.HasFailed)
            {
                loadingUI?.ShowFailure(restorer.FailureReason);
                yield break;
            }
            loadingUI?.SetProgress(
                0.97f,
                "正在恢复飞船和着陆平台");
            yield return null;
        }
        if (world == null)
            yield break;

        Physics.SyncTransforms();
        yield return null;
        Physics.SyncTransforms();
        IsReadyForReveal = true;
        VoxelPlanetPlayerController player = world.Player;
        if (loadingUI != null)
            yield return loadingUI.CompleteAndFade(0.6f);
        if (player != null)
        {
            player.SetSurfacePhysicsReady(true);
            player.SetGameplayInputBlocked(false);
            player.GetComponent<SurfaceMultifunctionController>()
                ?.SetInputBlocked(false);
        }
        routine = null;
    }
}
