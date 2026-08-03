using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityPlanet.CombatMap
{
    /// <summary>
    /// Projects a combat semantic plan into a finite greybox arena.
    /// Generated objects are isolated below GeneratedCombatMap and are
    /// intentionally not persisted into the scene.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CombatMapRuntimeController :
        MonoBehaviour,
        IAirCombatArenaProvider
    {
        public const string GeneratedRootName = "GeneratedCombatMap";

        [Header("地图配方")]
        [SerializeField, InspectorName("配方")] AirCombatMapRecipe recipe;
        [SerializeField, InspectorName("使用配方 Seed")]
        bool useRecipeSeed = true;
        [SerializeField, InspectorName("已应用的基础 Seed")]
        int appliedSeed = 7319;
        [SerializeField, InspectorName("自动选择最佳候选")]
        bool selectBestCandidate = true;
        [SerializeField, InspectorName("启用时自动生成")]
        bool autoGenerateOnEnable = true;

        [Header("外观与调试显示")]
        [SerializeField, InspectorName("地形材质")]
        Material terrainMaterial;
        [SerializeField, InspectorName("遮挡塔材质")]
        Material occluderMaterial;
        [SerializeField, InspectorName("显示战斗语义")]
        bool showSemanticOverlay = false;
        [SerializeField, InspectorName("显示路线")]
        bool showRoutes = true;
        [SerializeField, InspectorName("显示语义锚点")]
        bool showAnchors = true;
        [SerializeField, InspectorName("显示交战焦点")]
        bool showBoundary = true;
        [SerializeField, InspectorName("显示失败采样点")]
        bool showFailureSamples = true;
        [SerializeField, InspectorName("显示运行时面板")]
        bool showRuntimePanel = false;

        [NonSerialized] CombatMapGenerationResult currentResult;
        [NonSerialized] AirCombatMapSettings currentSettings;
        [NonSerialized] GameObject generatedRoot;
        [NonSerialized] GameObject overlayRoot;
        [NonSerialized] readonly List<Mesh> generatedMeshes =
            new List<Mesh>();
        [NonSerialized] readonly List<Material> generatedMaterials =
            new List<Material>();
        [NonSerialized] bool isGenerating;
        [NonSerialized] bool regenerationLocked;
        [NonSerialized] bool terrainCollisionEnabled = true;
        [NonSerialized] readonly List<PhysicMaterial>
            generatedPhysicsMaterials = new List<PhysicMaterial>();
        [NonSerialized] Vector2 panelScroll;

        public event Action<CombatMapGenerationResult> ArenaGenerated;

        public AirCombatMapRecipe Recipe => recipe;
        public CombatMapGenerationResult CurrentResult => currentResult;
        public CombatSemanticPlan CurrentPlan => currentResult?.plan;
        public CombatMapValidationReport CurrentValidation =>
            currentResult?.validation;
        public bool IsReady =>
            currentResult != null
            && currentResult.CanCommit
            && generatedRoot != null;
        public bool IsGenerating => isGenerating;
        public bool RegenerationLocked => regenerationLocked;
        public bool TerrainCollisionEnabled => terrainCollisionEnabled;
        public Transform GeneratedRoot =>
            generatedRoot != null ? generatedRoot.transform : null;
        public AirCombatMapSettings CurrentSettings =>
            (currentSettings ?? GetSettings()).ValidatedCopy();
        public int CurrentBaseSeed =>
            useRecipeSeed && recipe != null
                ? recipe.Seed
                : appliedSeed;
        public int CurrentSeed =>
            currentResult?.derivedSeed
            ?? CurrentBaseSeed;

        public CombatArenaContext CurrentContext
        {
            get
            {
                CombatSemanticPlan plan = CurrentPlan;
                CombatSemanticAnchor player = plan?.FindAnchor(
                    CombatAnchorType.PlayerSpawn);
                CombatSemanticAnchor enemy = plan?.FindAnchor(
                    CombatAnchorType.EnemySpawn);
                AirCombatMapSettings settings = currentSettings
                    ?? GetSettings();
                return new CombatArenaContext
                {
                    seed = currentResult?.derivedSeed
                        ?? settings.seed,
                    checksum = plan?.checksum ?? string.Empty,
                    center = plan?.mapCenter
                        ?? transform.position
                        + settings.mapCenterOffset,
                    size = new Vector2(
                        settings.mapSize,
                        settings.mapSize),
                    warningRadius = settings.warningRadius,
                    forfeitRadius = settings.forfeitRadius,
                    forfeitSeconds = settings.forfeitSeconds,
                    minimumGroundClearance =
                        settings.minimumGroundClearance,
                    maximumGroundClearance =
                        settings.maximumGroundClearance,
                    maximumFlightAltitude = plan?.flightCeiling
                        ?? transform.position.y
                            + settings.maximumGroundClearance,
                    playerSpawnPosition = player?.position
                        ?? Vector3.zero,
                    playerSpawnRotation = RotationFor(player),
                    enemySpawnPosition = enemy?.position
                        ?? Vector3.zero,
                    enemySpawnRotation = RotationFor(enemy)
                };
            }
        }

        void OnEnable()
        {
            if (!autoGenerateOnEnable || isGenerating)
                return;
            Rebuild();
        }

void Update()
        {
            if (!Application.isPlaying)
                return;

            if (Input.GetKeyDown(KeyCode.F7))
                SetSemanticOverlayVisible(!showSemanticOverlay);
            if (Input.GetKeyDown(KeyCode.F8))
                showRuntimePanel = !showRuntimePanel;
        }


        void OnDisable()
        {
            ClearGeneratedMap();
        }

        void OnDestroy()
        {
            ClearGeneratedMap();
        }

public void Configure(AirCombatMapRecipe valueRecipe)
        {
            recipe = valueRecipe;
            showSemanticOverlay = false;
            showRuntimePanel = false;
        }

        public void SetRegenerationLocked(bool value)
        {
            regenerationLocked = value;
        }

        public bool Rebuild()
        {
            if (regenerationLocked || isGenerating)
                return false;
            AirCombatMapSettings settings = GetSettings();
            int seed = useRecipeSeed && recipe != null
                ? recipe.Seed
                : appliedSeed;
            CombatMapGenerationResult result = selectBestCandidate
                ? CombatMapGenerator.GenerateBest(
                    settings,
                    GetMapCenter(settings),
                    seed)
                : CombatMapGenerator.Generate(
                    settings,
                    GetMapCenter(settings),
                    seed);
            return CommitResult(result, settings);
        }

        public CombatMapGenerationResult GeneratePreview(int seed)
        {
            if (regenerationLocked || isGenerating)
                return currentResult;
            AirCombatMapSettings settings = GetSettings(seed);
            CombatMapGenerationResult result = CombatMapGenerator.Generate(
                settings,
                GetMapCenter(settings),
                seed);
            BuildResult(result, settings, false);
            return result;
        }

        public CombatMapGenerationResult GenerateBestPreview(int seed)
        {
            if (regenerationLocked || isGenerating)
                return currentResult;
            AirCombatMapSettings settings = GetSettings(seed);
            CombatMapGenerationResult result =
                CombatMapGenerator.GenerateBest(
                    settings,
                    GetMapCenter(settings),
                    seed);
            BuildResult(result, settings, false);
            return result;
        }

        public bool ApplyCandidate(int seed)
        {
            if (regenerationLocked || isGenerating)
                return false;
            AirCombatMapSettings settings = GetSettings(seed);
            CombatMapGenerationResult result =
                CombatMapGenerator.GenerateBest(
                    settings,
                    GetMapCenter(settings),
                    seed);
            if (result == null || !result.CanCommit)
                return false;
            appliedSeed = seed;
            useRecipeSeed = false;
            return CommitResult(result, settings);
        }

        public CombatMapValidationReport ValidateCurrentCandidate()
        {
            if (currentResult?.plan == null)
                return null;
            currentResult.validation = CombatMapValidator.Validate(
                currentSettings ?? GetSettings(),
                currentResult.plan);
            return currentResult.validation;
        }

        public void SetSemanticOverlayVisible(bool value)
        {
            showSemanticOverlay = value;
            if (overlayRoot != null)
                overlayRoot.SetActive(value);
        }

        public bool Contains(Vector3 worldPosition)
        {
            if (!IsReady)
                return false;
            CombatArenaContext context = CurrentContext;
            Vector2 horizontal = new Vector2(
                worldPosition.x - context.center.x,
                worldPosition.z - context.center.z);
            AirCombatMapSettings settings = CurrentSettings;
            float outerHalf = Mathf.Max(
                settings.mapSize * 0.5f
                    + settings.designCombatSpeed * 120f,
                settings.mapSize * 2f);
            if (Mathf.Max(
                    Mathf.Abs(horizontal.x),
                    Mathf.Abs(horizontal.y)) > outerHalf)
                return false;
            float aboveGround = worldPosition.y
                - SampleGroundHeight(
                    worldPosition.x,
                    worldPosition.z);
            return aboveGround >= context.minimumGroundClearance
                && aboveGround <= context.maximumGroundClearance;
        }

        public float SampleGroundHeight(float worldX, float worldZ)
        {
            CombatSemanticPlan plan = CurrentPlan;
            if (plan == null)
                return transform.position.y;
            return CombatMapGenerator.SampleHeight(
                currentSettings ?? GetSettings(),
                plan,
                worldX,
                worldZ);
        }

        public float SampleGroundHeight(Vector3 worldPosition)
        {
            return SampleGroundHeight(
                worldPosition.x,
                worldPosition.z);
        }

        public bool TryGetSpawnPose(
            bool player,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!IsReady)
                return false;
            CombatSemanticAnchor anchor = CurrentPlan.FindAnchor(
                player
                    ? CombatAnchorType.PlayerSpawn
                    : CombatAnchorType.EnemySpawn);
            if (anchor == null)
                return false;
            position = anchor.position;
            rotation = RotationFor(anchor);
            return true;
        }

        public bool TryGetSpawnPose(
            CombatTeam team,
            out Pose pose)
        {
            bool success = TryGetSpawnPose(
                team == CombatTeam.Player,
                out Vector3 position,
                out Quaternion rotation);
            pose = new Pose(position, rotation);
            return success;
        }

        public bool TryGetAiWaypoint(
            Vector3 currentPosition,
            Vector3 targetPosition,
            out Vector3 waypoint)
        {
            waypoint = targetPosition;
            if (!IsReady)
                return false;
            if (CombatMapGenerator.HasTerrainLineOfSight(
                currentSettings,
                CurrentPlan,
                currentPosition,
                targetPosition))
            {
                return false;
            }

            CombatRouteType preferred;
            float localX = currentPosition.x
                - CurrentPlan.mapCenter.x;
            if (localX < -60f)
            {
                preferred = CombatRouteType.TerrainMaskedFlank;
            }
            else if (localX > 60f)
            {
                preferred = CombatRouteType.LongRange;
            }
            else
            {
                preferred = (CurrentPlan.seed & 1) == 0
                    ? CombatRouteType.TerrainMaskedFlank
                    : CombatRouteType.LongRange;
            }

            CombatSemanticRoute route = CurrentPlan.FindRoute(preferred);
            if (route?.waypoints == null
                || route.waypoints.Length == 0)
            {
                route = CurrentPlan.FindRoute(CombatRouteType.Main);
            }
            if (route?.waypoints == null
                || route.waypoints.Length == 0)
            {
                return false;
            }

            int nearest = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < route.waypoints.Length; i++)
            {
                float distance = (
                    route.waypoints[i] - currentPosition).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }
            int direction = targetPosition.z >= currentPosition.z ? 1 : -1;
            int next = Mathf.Clamp(
                nearest + direction,
                0,
                route.waypoints.Length - 1);
            if (next == nearest)
                next = Mathf.Clamp(
                    nearest - direction,
                    0,
                    route.waypoints.Length - 1);
            waypoint = route.waypoints[next];
            float ground = SampleGroundHeight(
                waypoint.x,
                waypoint.z);
            waypoint.y = Mathf.Max(
                waypoint.y,
                ground
                + currentSettings.minimumGroundClearance
                + 6f);
            return true;
        }

public void ClearGeneratedMap()
        {
            Transform existing = transform.Find(GeneratedRootName);
            if (generatedRoot == null && existing != null)
                generatedRoot = existing.gameObject;
            if (generatedRoot != null)
            {
                generatedRoot.SetActive(false);
                Collider[] colliders =
                    generatedRoot.GetComponentsInChildren<Collider>(true);
                for (int index = 0; index < colliders.Length; index++)
                {
                    if (colliders[index] != null)
                        colliders[index].enabled = false;
                }
                Physics.SyncTransforms();
                DestroyTransient(generatedRoot);
            }
            generatedRoot = null;
            overlayRoot = null;

            for (int i = 0; i < generatedMeshes.Count; i++)
                DestroyTransient(generatedMeshes[i]);
            generatedMeshes.Clear();
            for (int i = 0; i < generatedMaterials.Count; i++)
                DestroyTransient(generatedMaterials[i]);
            generatedMaterials.Clear();
            for (int i = 0; i < generatedPhysicsMaterials.Count; i++)
                DestroyTransient(generatedPhysicsMaterials[i]);
            generatedPhysicsMaterials.Clear();
        }

        bool CommitResult(
            CombatMapGenerationResult result,
            AirCombatMapSettings settings)
        {
            if (result == null || !result.CanCommit)
            {
                currentResult = result;
                currentSettings = settings ?? GetSettings();
                return false;
            }
            BuildResult(
                result,
                settings ?? GetSettings(),
                true);
            return true;
        }

        void BuildResult(
            CombatMapGenerationResult result,
            AirCombatMapSettings settings,
            bool requireValid)
        {
            if (isGenerating)
                return;
            isGenerating = true;
            try
            {
                ClearGeneratedMap();
                currentResult = result;
                currentSettings = settings;
                if (result?.plan == null
                    || (requireValid && !result.CanCommit))
                {
                    return;
                }

                generatedRoot = new GameObject(GeneratedRootName)
                {
                    hideFlags = HideFlags.DontSaveInEditor
                        | HideFlags.DontSaveInBuild
                };
                generatedRoot.transform.SetParent(transform, false);
                generatedRoot.transform.position =
                    result.plan.mapCenter;
                generatedRoot.transform.rotation = Quaternion.identity;
                generatedRoot.transform.localScale = Vector3.one;

                Material terrain = terrainMaterial != null
                    ? terrainMaterial
                    : CreateTerrainSurfaceMaterial();
                Material towers = occluderMaterial != null
                    ? occluderMaterial
                    : CreateMaterial(
                        "CombatMap Occluders",
                        new Color(0.28f, 0.32f, 0.36f));
                BuildTerrainChunks(settings, result.plan, terrain);
                BuildWorldSkirt(settings, result.plan, terrain);
                BuildUrbanRoadNetwork(settings, result.plan);
                BuildUrbanPlotSurfaces(settings, result.plan);
                BuildOccluders(result.plan, towers);
                BuildFlightContainment(settings, result.plan);
                BuildSemanticOverlay(settings, result);
                SetTerrainCollisionEnabled(terrainCollisionEnabled);
                Physics.SyncTransforms();
                ArenaGenerated?.Invoke(result);
            }
            finally
            {
                isGenerating = false;
            }
        }

void BuildTerrainChunks(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Material material)
        {
            int terrainLayer = ResolveLayer("CombatTerrain", gameObject.layer);
            var terrainRoot = new GameObject("TerrainChunks")
            {
                hideFlags = generatedRoot.hideFlags,
                layer = terrainLayer
            };
            terrainRoot.transform.SetParent(
                generatedRoot.transform,
                false);

            int chunkCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    settings.mapSize / settings.chunkSize));
            float actualChunkSize = settings.mapSize / chunkCount;
            float half = settings.mapSize * 0.5f;
            for (int z = 0; z < chunkCount; z++)
            for (int x = 0; x < chunkCount; x++)
            {
                float centerX = -half +
                    (x + 0.5f) * actualChunkSize;
                float centerZ = -half +
                    (z + 0.5f) * actualChunkSize;
                var chunk = new GameObject(
                    "Chunk_" + x.ToString("D2") +
                    "_" + z.ToString("D2"))
                {
                    hideFlags = generatedRoot.hideFlags,
                    isStatic = true,
                    layer = terrainLayer
                };
                chunk.transform.SetParent(terrainRoot.transform, false);
                chunk.transform.localPosition =
                    new Vector3(centerX, 0f, centerZ);
                var filter = chunk.AddComponent<MeshFilter>();
                var renderer = chunk.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                Mesh mesh = BuildChunkMesh(
                    settings,
                    plan,
                    centerX,
                    centerZ,
                    actualChunkSize,
                    settings.chunkResolution,
                    x,
                    z);
                generatedMeshes.Add(mesh);
                filter.sharedMesh = mesh;
            }

            BuildCollisionSurface(
                settings,
                plan,
                terrainRoot.transform,
                chunkCount,
                terrainLayer);
        }

        Mesh BuildChunkMesh(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            float chunkCenterX,
            float chunkCenterZ,
            float chunkSize,
            int resolution,
            int chunkX,
            int chunkZ)
        {
            int side = resolution + 1;
            var vertices = new Vector3[side * side];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[resolution * resolution * 6];
            float half = chunkSize * 0.5f;
            float step = chunkSize / resolution;

            for (int z = 0; z <= resolution; z++)
            for (int x = 0; x <= resolution; x++)
            {
                int index = z * side + x;
                float localX = -half + x * step;
                float localZ = -half + z * step;
                float worldX = plan.mapCenter.x
                    + chunkCenterX
                    + localX;
                float worldZ = plan.mapCenter.z
                    + chunkCenterZ
                    + localZ;
                float height = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ);
                vertices[index] = new Vector3(
                    localX,
                    height - plan.mapCenter.y,
                    localZ);

                float left = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX - step,
                    worldZ);
                float right = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX + step,
                    worldZ);
                float down = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ - step);
                float up = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ + step);
                normals[index] = new Vector3(
                    left - right,
                    2f * step,
                    down - up).normalized;
                colors[index] = TerrainSurfaceColor(
                    settings,
                    plan,
                    worldX,
                    worldZ,
                    height,
                    normals[index]);
                uv[index] = new Vector2(
                    (chunkX * resolution + x)
                    / (float)(resolution
                        * Mathf.Max(
                            1,
                            Mathf.CeilToInt(
                                settings.mapSize
                                / settings.chunkSize))),
                    (chunkZ * resolution + z)
                    / (float)(resolution
                        * Mathf.Max(
                            1,
                            Mathf.CeilToInt(
                                settings.mapSize
                                / settings.chunkSize))));
            }

            int triangle = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int a = z * side + x;
                int b = a + 1;
                int c = a + side;
                int d = c + 1;
                triangles[triangle++] = a;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = d;
            }

            var mesh = new Mesh
            {
                name = "CombatMapChunk_"
                    + chunkX.ToString("D2")
                    + "_"
                    + chunkZ.ToString("D2"),
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                normals = normals,
                colors = colors,
                uv = uv,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            return mesh;
        }

void BuildOccluders(
            CombatSemanticPlan plan,
            Material material)
        {
            int obstacleLayer = ResolveLayer(
                "CombatObstacle",
                gameObject.layer);
            var root = new GameObject("OccluderTowers")
            {
                hideFlags = generatedRoot.hideFlags,
                layer = obstacleLayer
            };
            root.transform.SetParent(generatedRoot.transform, false);
            if (plan.occluders == null)
                return;
            for (int i = 0; i < plan.occluders.Length; i++)
            {
                CombatOccluderData value = plan.occluders[i];
                if (value == null ||
                    value.type != CombatOccluderType.Tower)
                {
                    continue;
                }
                var tower = new GameObject(value.stableId)
                {
                    hideFlags = generatedRoot.hideFlags,
                    isStatic = true,
                    layer = obstacleLayer
                };
                tower.transform.SetParent(root.transform, false);
                tower.transform.localPosition =
                    value.position - plan.mapCenter;
                tower.transform.localRotation =
                    Quaternion.Euler(0f, value.yaw, 0f);

                GameObject visual = GameObject.CreatePrimitive(
                    PrimitiveFor(value.decorationKind));
                visual.name = "PlaceholderVisual_"
                    + value.decorationKind;
                visual.hideFlags = generatedRoot.hideFlags;
                visual.layer = obstacleLayer;
                visual.transform.SetParent(tower.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = PrimitiveScale(
                    value.decorationKind,
                    value.size);
                Collider primitiveCollider = visual.GetComponent<Collider>();
                if (primitiveCollider != null)
                    DestroyTransient(primitiveCollider);

                MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                var collider = tower.AddComponent<BoxCollider>();
                collider.size = value.size;
                collider.sharedMaterial =
                    generatedPhysicsMaterials.Count > 0
                        ? generatedPhysicsMaterials[0]
                        : CreateFlightPhysicsMaterial();
                collider.enabled = terrainCollisionEnabled;
            }
        }

        void BuildUrbanRoadNetwork(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            if (plan.theme != CombatMapTheme.Urban
                || plan.urbanRoads == null
                || plan.urbanRoads.Length == 0)
            {
                return;
            }

            int terrainLayer = ResolveLayer(
                "CombatTerrain",
                gameObject.layer);
            var root = new GameObject("CityRoadNetwork")
            {
                hideFlags = generatedRoot.hideFlags,
                isStatic = true,
                layer = terrainLayer
            };
            root.transform.SetParent(generatedRoot.transform, false);
            Material roadMaterial = CreateRoadSurfaceMaterial();
            int roadIndex = 0;
            for (int index = 0; index < plan.urbanRoads.Length; index++)
            {
                CombatUrbanRoadData road = plan.urbanRoads[index];
                if (road == null)
                    continue;
                BuildRoadStrip(
                    settings,
                    plan,
                    root.transform,
                    roadMaterial,
                    new Vector2(road.start.x, road.start.z),
                    new Vector2(road.end.x, road.end.z),
                    road.width,
                    roadIndex++);
            }
            if (roadIndex == 0)
                DestroyTransient(root);
        }

        void BuildUrbanPlotSurfaces(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            if (plan.theme != CombatMapTheme.Urban
                || plan.urbanPlots == null
                || plan.urbanPlots.Length == 0)
            {
                return;
            }
            int terrainLayer = ResolveLayer(
                "CombatTerrain",
                gameObject.layer);
            var root = new GameObject("UrbanGroundLots")
            {
                hideFlags = generatedRoot.hideFlags,
                isStatic = true,
                layer = terrainLayer
            };
            root.transform.SetParent(generatedRoot.transform, false);
            Material material = CreateUrbanPlotMaterial();
            for (int index = 0; index < plan.urbanPlots.Length; index++)
            {
                CombatUrbanPlotData plot = plan.urbanPlots[index];
                if (plot == null)
                    continue;
                CombatUrbanRoadData road = FindUrbanRoad(
                    plan,
                    plot.roadStableId);
                if (road == null)
                    continue;
                Vector2 center = new Vector2(
                    plot.position.x,
                    plot.position.z);
                Vector2 roadPoint = ClosestPointOnSegment(
                    center,
                    new Vector2(road.start.x, road.start.z),
                    new Vector2(road.end.x, road.end.z));
                Vector2 direction = roadPoint - center;
                float distance = direction.magnitude;
                if (distance <= 1f)
                    continue;
                direction /= distance;
                float plotExtent = Mathf.Min(plot.size.x, plot.size.y)
                    * 0.32f;
                Vector2 drivewayStart = center
                    + direction * Mathf.Min(plotExtent, distance * 0.55f);
                if (Vector2.Distance(drivewayStart, roadPoint) <= 3f)
                    continue;
                BuildRoadStrip(
                    settings,
                    plan,
                    root.transform,
                    material,
                    drivewayStart,
                    roadPoint,
                    Mathf.Clamp(
                        settings.vehicleWingspan * 0.62f,
                        9f,
                        14f),
                    100 + index);
            }
        }

        static CombatUrbanRoadData FindUrbanRoad(
            CombatSemanticPlan plan,
            string stableId)
        {
            if (plan.urbanRoads == null)
                return null;
            for (int i = 0; i < plan.urbanRoads.Length; i++)
            {
                CombatUrbanRoadData value = plan.urbanRoads[i];
                if (value != null && value.stableId == stableId)
                    return value;
            }
            return null;
        }

        static Vector2 ClosestPointOnSegment(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.0001f)
                return start;
            float along = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / denominator);
            return start + segment * along;
        }

        void BuildRoadStrip(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Transform parent,
            Material material,
            Vector2 start,
            Vector2 end,
            float width,
            int index)
        {
            int segments = Mathf.Clamp(
                Mathf.CeilToInt(Vector2.Distance(start, end) / 28f),
                4,
                32);
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            Vector2 direction = (end - start).normalized;
            Vector2 side = new Vector2(-direction.y, direction.x) * width * 0.5f;
            for (int step = 0; step <= segments; step++)
            {
                float t = step / (float)segments;
                Vector2 center = Vector2.Lerp(start, end, t);
                for (int edge = 0; edge < 2; edge++)
                {
                    Vector2 point = center + (edge == 0 ? -side : side);
                    float height = CombatMapGenerator.SampleHeight(
                        settings,
                        plan,
                        point.x,
                        point.y) - plan.mapCenter.y + 0.18f;
                    int vertex = step * 2 + edge;
                    vertices[vertex] = new Vector3(
                        point.x - plan.mapCenter.x,
                        height,
                        point.y - plan.mapCenter.z);
                    uv[vertex] = new Vector2(
                        edge * width / 8f,
                        t * Vector2.Distance(start, end) / 8f);
                }
                if (step == segments)
                    continue;
                int triangle = step * 6;
                int current = step * 2;
                triangles[triangle] = current;
                triangles[triangle + 1] = current + 2;
                triangles[triangle + 2] = current + 1;
                triangles[triangle + 3] = current + 2;
                triangles[triangle + 4] = current + 3;
                triangles[triangle + 5] = current + 1;
            }

            var mesh = new Mesh
            {
                name = "CombatMapRoad_" + index.ToString("D2"),
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                triangles = triangles,
                uv = uv
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            generatedMeshes.Add(mesh);

            var road = new GameObject(mesh.name)
            {
                hideFlags = generatedRoot.hideFlags,
                isStatic = true,
                layer = parent.gameObject.layer
            };
            road.transform.SetParent(parent, false);
            MeshFilter filter = road.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = road.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        void BuildFlightContainment(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            int obstacleLayer = ResolveLayer(
                "CombatObstacle",
                gameObject.layer);
            var root = new GameObject("FlightContainmentAirWalls")
            {
                hideFlags = generatedRoot.hideFlags,
                layer = obstacleLayer
            };
            root.transform.SetParent(generatedRoot.transform, false);

            float half = settings.mapSize * 0.5f;
            float thickness = Mathf.Max(
                12f,
                settings.vehicleWingspan * 0.8f);
            float ceiling = plan.flightCeiling;
            float bottom = plan.mapCenter.y
                - Mathf.Max(80f, settings.mountainHeight * 0.35f);
            float wallHeight = ceiling - bottom + thickness;
            float localY = (bottom + ceiling) * 0.5f
                - plan.mapCenter.y;
            PhysicMaterial physicsMaterial =
                generatedPhysicsMaterials.Count > 0
                    ? generatedPhysicsMaterials[0]
                    : CreateFlightPhysicsMaterial();

            AddAirWall(
                root.transform,
                "WestAirWall",
                new Vector3(-half - thickness * 0.5f, localY, 0f),
                new Vector3(thickness, wallHeight, settings.mapSize),
                obstacleLayer,
                physicsMaterial);
            AddAirWall(
                root.transform,
                "EastAirWall",
                new Vector3(half + thickness * 0.5f, localY, 0f),
                new Vector3(thickness, wallHeight, settings.mapSize),
                obstacleLayer,
                physicsMaterial);
            AddAirWall(
                root.transform,
                "SouthAirWall",
                new Vector3(0f, localY, -half - thickness * 0.5f),
                new Vector3(
                    settings.mapSize + thickness * 2f,
                    wallHeight,
                    thickness),
                obstacleLayer,
                physicsMaterial);
            AddAirWall(
                root.transform,
                "NorthAirWall",
                new Vector3(0f, localY, half + thickness * 0.5f),
                new Vector3(
                    settings.mapSize + thickness * 2f,
                    wallHeight,
                    thickness),
                obstacleLayer,
                physicsMaterial);
            AddAirWall(
                root.transform,
                "CeilingAirWall",
                new Vector3(
                    0f,
                    ceiling - plan.mapCenter.y + thickness * 0.5f,
                    0f),
                new Vector3(
                    settings.mapSize + thickness * 2f,
                    thickness,
                    settings.mapSize + thickness * 2f),
                obstacleLayer,
                physicsMaterial);
        }

        void AddAirWall(
            Transform parent,
            string wallName,
            Vector3 localPosition,
            Vector3 size,
            int layer,
            PhysicMaterial physicsMaterial)
        {
            var wall = new GameObject(wallName)
            {
                hideFlags = generatedRoot.hideFlags,
                isStatic = true,
                layer = layer
            };
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = size;
            collider.sharedMaterial = physicsMaterial;
            collider.enabled = terrainCollisionEnabled;
        }

        static PrimitiveType PrimitiveFor(CombatDecorationKind kind)
        {
            switch (kind)
            {
                case CombatDecorationKind.RockSpire:
                case CombatDecorationKind.Beacon:
                    return PrimitiveType.Cylinder;
                case CombatDecorationKind.Crystal:
                    return PrimitiveType.Capsule;
                default:
                    return PrimitiveType.Cube;
            }
        }

        static Vector3 PrimitiveScale(
            CombatDecorationKind kind,
            Vector3 size)
        {
            switch (kind)
            {
                case CombatDecorationKind.RockSpire:
                case CombatDecorationKind.Beacon:
                case CombatDecorationKind.Crystal:
                    return new Vector3(
                        size.x,
                        size.y * 0.5f,
                        size.z);
                default:
                    return size;
            }
        }

        void BuildWorldSkirt(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Material material)
        {
            int terrainLayer = ResolveLayer(
                "CombatTerrain",
                gameObject.layer);
            var skirt = new GameObject("OutskirtsTerrainSkirt")
            {
                hideFlags = generatedRoot.hideFlags,
                isStatic = true,
                layer = terrainLayer
            };
            skirt.transform.SetParent(generatedRoot.transform, false);

            const int segmentsPerSide = 40;
            const int ringCount = 6;
            int perimeterCount = segmentsPerSide * 4;
            float innerHalf = settings.mapSize * 0.5f;
            float outerHalf = Mathf.Max(
                innerHalf + settings.designCombatSpeed * 120f,
                settings.mapSize * 1.7f);
            var vertices = new Vector3[perimeterCount * ringCount];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[
                perimeterCount * (ringCount - 1) * 6];
            for (int ring = 0; ring < ringCount; ring++)
            for (int index = 0; index < perimeterCount; index++)
            {
                Vector2 inner = SquarePerimeterPoint(
                    innerHalf,
                    index,
                    segmentsPerSide);
                Vector2 outer = inner.sqrMagnitude > 0.001f
                    ? inner.normalized * outerHalf
                    : inner;
                float t = ring / (float)(ringCount - 1);
                float blend = Mathf.SmoothStep(0f, 1f, t);
                Vector2 point = Vector2.Lerp(inner, outer, blend);
                float worldX = plan.mapCenter.x + point.x;
                float worldZ = plan.mapCenter.z + point.y;
                float height = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    worldX,
                    worldZ)
                    - plan.mapCenter.y
                    - t * t * settings.mountainHeight * 0.16f;
                int vertex = ring * perimeterCount + index;
                vertices[vertex] = new Vector3(point.x, height, point.y);
                uv[vertex] = new Vector2(
                    point.x / 15f,
                    point.y / 15f);
                if (ring == ringCount - 1)
                    continue;
                int next = (index + 1) % perimeterCount;
                int triangle = (ring * perimeterCount + index) * 6;
                int innerA = ring * perimeterCount + index;
                int innerB = ring * perimeterCount + next;
                int outerA = (ring + 1) * perimeterCount + index;
                int outerB = (ring + 1) * perimeterCount + next;
                triangles[triangle] = innerA;
                triangles[triangle + 1] = innerB;
                triangles[triangle + 2] = outerA;
                triangles[triangle + 3] = innerB;
                triangles[triangle + 4] = outerB;
                triangles[triangle + 5] = outerA;
            }

            var mesh = new Mesh
            {
                name = "CombatMapOutskirtsSkirt",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                triangles = triangles,
                uv = uv
            };
            mesh.RecalculateNormals();
            Vector3[] normals = mesh.normals;
            var colors = new Color[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 point = vertices[index];
                colors[index] = TerrainSurfaceColor(
                    settings,
                    plan,
                    plan.mapCenter.x + point.x,
                    plan.mapCenter.z + point.z,
                    plan.mapCenter.y + point.y,
                    normals[index]);
            }
            mesh.colors = colors;
            mesh.RecalculateBounds();
            generatedMeshes.Add(mesh);

            MeshFilter filter = skirt.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = skirt.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            MeshCollider collider = skirt.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            collider.isTrigger = false;
            collider.sharedMaterial =
                generatedPhysicsMaterials.Count > 0
                    ? generatedPhysicsMaterials[0]
                    : CreateFlightPhysicsMaterial();
        }

        static Vector2 SquarePerimeterPoint(
            float half,
            int index,
            int segmentsPerSide)
        {
            int perimeter = segmentsPerSide * 4;
            int wrapped = (index % perimeter + perimeter) % perimeter;
            int side = wrapped / segmentsPerSide;
            float t = (wrapped % segmentsPerSide)
                / (float)segmentsPerSide;
            switch (side)
            {
                case 0:
                    return new Vector2(
                        Mathf.Lerp(-half, half, t),
                        -half);
                case 1:
                    return new Vector2(
                        half,
                        Mathf.Lerp(-half, half, t));
                case 2:
                    return new Vector2(
                        Mathf.Lerp(half, -half, t),
                        half);
                default:
                    return new Vector2(
                        -half,
                        Mathf.Lerp(half, -half, t));
            }
        }

void BuildSemanticOverlay(
            AirCombatMapSettings settings,
            CombatMapGenerationResult result)
        {
            int debugLayer = ResolveLayer("CombatDebug", gameObject.layer);
            overlayRoot = new GameObject("SemanticOverlay")
            {
                hideFlags = generatedRoot.hideFlags,
                layer = debugLayer
            };
            overlayRoot.transform.SetParent(
                generatedRoot.transform,
                false);
            overlayRoot.SetActive(showSemanticOverlay);
            if (showRoutes)
                BuildRouteOverlay(result.plan);
            if (showAnchors)
            {
                BuildTacticalVolumeOverlay(result.plan);
                BuildAnchorOverlay(result.plan);
            }
            if (showBoundary)
                BuildBoundaryOverlay(settings, result.plan);
            if (showFailureSamples)
                BuildViolationOverlay(result);
            SetLayerRecursively(overlayRoot.transform, debugLayer);
        }

        void BuildRouteOverlay(CombatSemanticPlan plan)
        {
            if (plan.routes == null)
                return;
            var routeRoot = new GameObject("Routes")
            {
                hideFlags = generatedRoot.hideFlags
            };
            routeRoot.transform.SetParent(overlayRoot.transform, false);
            for (int i = 0; i < plan.routes.Length; i++)
            {
                CombatSemanticRoute route = plan.routes[i];
                if (route?.waypoints == null
                    || route.waypoints.Length < 2)
                {
                    continue;
                }
                Color color = RouteColor(route.type);
                Material material = CreateLineMaterial(
                    "CombatMap " + route.type + " Route",
                    color);
                var lineObject = new GameObject(route.stableId)
                {
                    hideFlags = generatedRoot.hideFlags
                };
                lineObject.transform.SetParent(routeRoot.transform, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.sharedMaterial = material;
                line.positionCount = route.waypoints.Length;
                line.widthMultiplier = Mathf.Clamp(
                    route.width * 0.055f,
                    3f,
                    12f);
                line.numCapVertices = 4;
                line.numCornerVertices = 3;
                line.startColor = color;
                line.endColor = color;
                for (int point = 0;
                     point < route.waypoints.Length;
                     point++)
                {
                    line.SetPosition(
                        point,
                        route.waypoints[point] - plan.mapCenter
                        + Vector3.up * 2f);
                }
            }
        }

        void BuildAnchorOverlay(CombatSemanticPlan plan)
        {
            if (plan.anchors == null)
                return;
            var anchorRoot = new GameObject("Anchors")
            {
                hideFlags = generatedRoot.hideFlags
            };
            anchorRoot.transform.SetParent(overlayRoot.transform, false);
            for (int i = 0; i < plan.anchors.Length; i++)
            {
                CombatSemanticAnchor anchor = plan.anchors[i];
                if (anchor == null)
                    continue;
                Color color = AnchorColor(anchor.type);
                Material material = CreateMaterial(
                    "CombatMap " + anchor.type + " Anchor",
                    color,
                    true);
                GameObject marker = GameObject.CreatePrimitive(
                    anchor.type == CombatAnchorType.Landmark
                        ? PrimitiveType.Cylinder
                        : PrimitiveType.Sphere);
                marker.name = anchor.stableId;
                marker.hideFlags = generatedRoot.hideFlags;
                marker.transform.SetParent(anchorRoot.transform, false);
                marker.transform.localPosition =
                    anchor.position - plan.mapCenter;
                float scale = Mathf.Clamp(
                    anchor.radius * 0.18f,
                    5f,
                    18f);
                marker.transform.localScale = Vector3.one * scale;
                MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = material;
                Collider collider = marker.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                    DestroyTransient(collider);
                }
            }
        }

        void BuildTacticalVolumeOverlay(CombatSemanticPlan plan)
        {
            if (plan.tacticalVolumes == null)
                return;
            const int segmentCount = 48;
            var root = new GameObject("TacticalVolumes")
            {
                hideFlags = generatedRoot.hideFlags
            };
            root.transform.SetParent(overlayRoot.transform, false);
            for (int i = 0; i < plan.tacticalVolumes.Length; i++)
            {
                CombatTacticalVolume volume =
                    plan.tacticalVolumes[i];
                if (volume == null)
                    continue;
                Color color = VolumeColor(volume.type);
                var marker = new GameObject(volume.stableId)
                {
                    hideFlags = generatedRoot.hideFlags
                };
                marker.transform.SetParent(root.transform, false);
                LineRenderer line =
                    marker.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = segmentCount;
                line.widthMultiplier = 2.5f;
                line.sharedMaterial = CreateLineMaterial(
                    "CombatMap " + volume.type + " Volume",
                    color);
                line.startColor = color;
                line.endColor = color;
                Vector3 localCenter =
                    volume.position - plan.mapCenter;
                float radiusX = volume.size.x * 0.5f;
                float radiusZ = volume.size.z * 0.5f;
                for (int point = 0;
                     point < segmentCount;
                     point++)
                {
                    float angle = point
                        / (float)segmentCount
                        * Mathf.PI * 2f;
                    line.SetPosition(
                        point,
                        localCenter
                        + new Vector3(
                            Mathf.Cos(angle) * radiusX,
                            0f,
                            Mathf.Sin(angle) * radiusZ));
                }
            }
        }

        void BuildBoundaryOverlay(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan)
        {
            const int segmentCount = 96;
            var boundary = new GameObject("CombatFocusBoundary")
            {
                hideFlags = generatedRoot.hideFlags
            };
            boundary.transform.SetParent(overlayRoot.transform, false);
            LineRenderer line = boundary.AddComponent<LineRenderer>();
            Color color = new Color(0.18f, 0.7f, 1f, 0.75f);
            line.sharedMaterial = CreateLineMaterial(
                "CombatMap Focus Guide",
                color);
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segmentCount;
            line.widthMultiplier = 5f;
            line.startColor = color;
            line.endColor = color;
            for (int i = 0; i < segmentCount; i++)
            {
                float angle = i / (float)segmentCount
                    * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * settings.forfeitRadius;
                float z = Mathf.Sin(angle) * settings.forfeitRadius;
                float y = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    plan.mapCenter.x + x,
                    plan.mapCenter.z + z)
                    - plan.mapCenter.y
                    + 6f;
                line.SetPosition(i, new Vector3(x, y, z));
            }
        }

        void BuildViolationOverlay(CombatMapGenerationResult result)
        {
            CombatMapViolation[] violations =
                result.validation?.violations;
            if (violations == null || violations.Length == 0)
                return;
            Material warning = CreateMaterial(
                "CombatMap Warning",
                new Color(1f, 0.12f, 0.06f),
                true);
            var root = new GameObject("ValidationFailures")
            {
                hideFlags = generatedRoot.hideFlags
            };
            root.transform.SetParent(overlayRoot.transform, false);
            for (int i = 0; i < violations.Length; i++)
            {
                CombatMapViolation violation = violations[i];
                if (violation == null)
                    continue;
                GameObject marker = GameObject.CreatePrimitive(
                    PrimitiveType.Sphere);
                marker.name = violation.code;
                marker.hideFlags = generatedRoot.hideFlags;
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition =
                    violation.position
                    - result.plan.mapCenter
                    + Vector3.up * 8f;
                marker.transform.localScale = Vector3.one * 9f;
                MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = warning;
                Collider collider = marker.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = false;
                    DestroyTransient(collider);
                }
            }
        }

        static Color TerrainSurfaceColor(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            float worldX,
            float worldZ,
            float height,
            Vector3 normal)
        {
            float slope = Mathf.Clamp01(1f - normal.y);
            float rock = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.12f, 0.52f, slope));
            float dirt = Mathf.Clamp01(slope * 1.8f) * (1f - rock);
            float mud = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    plan.mapCenter.y + 10f,
                    plan.mapCenter.y + 1f,
                    height)) * (1f - rock) * 0.62f;
            Vector2 point = new Vector2(worldX, worldZ);

            if (plan.urbanRoads != null)
            {
                for (int i = 0; i < plan.urbanRoads.Length; i++)
                {
                    CombatUrbanRoadData road = plan.urbanRoads[i];
                    if (road == null)
                        continue;
                    float distance = DistanceToSegment2D(
                        point,
                        new Vector2(road.start.x, road.start.z),
                        new Vector2(road.end.x, road.end.z));
                    float shoulder = 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            road.width * 0.5f,
                            road.width * 0.5f + road.shoulder * 1.65f,
                            distance));
                    dirt = Mathf.Max(dirt, shoulder * 0.92f);
                    mud *= 1f - shoulder;
                }
            }
            if (plan.urbanPlots != null)
            {
                for (int i = 0; i < plan.urbanPlots.Length; i++)
                {
                    CombatUrbanPlotData plot = plan.urbanPlots[i];
                    if (plot == null)
                        continue;
                    float radius = plot.size.magnitude * 0.5f;
                    float distance = Vector2.Distance(
                        point,
                        new Vector2(plot.position.x, plot.position.z));
                    float transition = 1f - Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.InverseLerp(
                            radius,
                            radius + settings.vehicleWingspan * 1.4f,
                            distance));
                    dirt = Mathf.Max(dirt, transition * 0.78f);
                }
            }
            return new Color(rock, dirt, mud, 1f);
        }

        static float DistanceToSegment2D(
            Vector2 point,
            Vector2 start,
            Vector2 end)
        {
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            if (denominator <= 0.0001f)
                return Vector2.Distance(point, start);
            float along = Mathf.Clamp01(
                Vector2.Dot(point - start, segment) / denominator);
            return Vector2.Distance(point, start + segment * along);
        }

        Material CreateTerrainSurfaceMaterial()
        {
            Shader shader = Shader.Find(
                "UnityPlanet/CombatMap/TerrainBlend");
            if (shader == null)
            {
                return CreateMaterial(
                    "CombatMap Terrain Fallback",
                    new Color(0.26f, 0.32f, 0.2f));
            }
            var material = new Material(shader)
            {
                name = "CombatMap Layered Terrain",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
            material.SetTexture(
                "_GrassTex",
                Resources.Load<Texture2D>(
                    "CombatMap/Surface/T_Ground_03_Grass_BC_SM"));
            material.SetTexture(
                "_MudTex",
                Resources.Load<Texture2D>(
                    "CombatMap/Surface/T_Ground_10_Mud_BC_SM"));
            material.SetTexture(
                "_DirtTex",
                Resources.Load<Texture2D>(
                    "CombatMap/Surface/T_Ground_34_Dirt_A_Sm"));
            material.SetTexture(
                "_RockTex",
                Resources.Load<Texture2D>(
                    "CombatMap/Surface/T_Ground_24_Rock_A_Sm"));
            material.SetFloat("_TextureScale", 15f);
            generatedMaterials.Add(material);
            return material;
        }

        Material CreateRoadSurfaceMaterial()
        {
            Material material = CreateMaterial(
                "CombatMap Asphalt Roads",
                new Color(0.34f, 0.34f, 0.34f));
            Texture2D road = Resources.Load<Texture2D>(
                "CombatMap/Surface/T_Ground_22_Road_BC_SM");
            if (material != null && road != null)
            {
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", road);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", road);
                material.mainTextureScale = Vector2.one;
            }
            return material;
        }

        Material CreateUrbanPlotMaterial()
        {
            Material material = CreateMaterial(
                "CombatMap Urban Lots",
                new Color(0.46f, 0.43f, 0.38f));
            Texture2D dirt = Resources.Load<Texture2D>(
                "CombatMap/Surface/T_Ground_34_Dirt_A_Sm");
            if (material != null && dirt != null)
            {
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", dirt);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", dirt);
                material.mainTextureScale = Vector2.one;
            }
            return material;
        }

        Material CreateMaterial(
            string materialName,
            Color color,
            bool unlit = false)
        {
            Shader shader;
            if (unlit)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Sprites/Default");
            }
            else
            {
                shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Legacy Shaders/Diffuse")
                    ?? Shader.Find("Unlit/Color");
            }
            if (shader == null)
                return null;
            var material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
                color = color,
                enableInstancing = true
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            generatedMaterials.Add(material);
            return material;
        }

        Material CreateLineMaterial(string name, Color color)
        {
            Material material = CreateMaterial(name, color, true);
            if (material != null)
                material.renderQueue = 3100;
            return material;
        }

        AirCombatMapSettings GetSettings(int? seed = null)
        {
            AirCombatMapSettings settings = recipe != null
                ? recipe.CreateValidatedSettings(seed)
                : AirCombatMapSettings.CreateDefault();
            if (recipe == null && seed.HasValue)
                settings.seed = seed.Value;
            if (!seed.HasValue && !useRecipeSeed)
                settings.seed = appliedSeed;
            settings.Clamp();
            return settings;
        }

        Vector3 GetMapCenter(AirCombatMapSettings settings)
        {
            return transform.position + settings.mapCenterOffset;
        }

        static Quaternion RotationFor(CombatSemanticAnchor anchor)
        {
            if (anchor == null)
                return Quaternion.identity;
            Vector3 forward = anchor.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
        }

        static Color RouteColor(CombatRouteType type)
        {
            switch (type)
            {
                case CombatRouteType.Main:
                    return new Color(1f, 0.72f, 0.12f, 0.9f);
                case CombatRouteType.TerrainMaskedFlank:
                    return new Color(0.15f, 0.9f, 0.48f, 0.9f);
                case CombatRouteType.LongRange:
                    return new Color(0.22f, 0.65f, 1f, 0.9f);
                default:
                    return new Color(0.78f, 0.35f, 0.95f, 0.8f);
            }
        }

        static Color AnchorColor(CombatAnchorType type)
        {
            switch (type)
            {
                case CombatAnchorType.PlayerSpawn:
                    return new Color(0.1f, 0.85f, 1f);
                case CombatAnchorType.EnemySpawn:
                    return new Color(1f, 0.22f, 0.16f);
                case CombatAnchorType.CentralConflict:
                    return new Color(1f, 0.8f, 0.08f);
                case CombatAnchorType.PowerPosition:
                    return new Color(0.88f, 0.22f, 1f);
                case CombatAnchorType.Landmark:
                    return new Color(1f, 1f, 1f);
                default:
                    return new Color(0.25f, 1f, 0.42f);
            }
        }

        static Color VolumeColor(CombatTacticalVolumeType type)
        {
            switch (type)
            {
                case CombatTacticalVolumeType.SpawnBasin:
                    return new Color(0.2f, 0.8f, 1f, 0.85f);
                case CombatTacticalVolumeType.ManeuverBowl:
                    return new Color(1f, 0.76f, 0.12f, 0.9f);
                case CombatTacticalVolumeType.OcclusionGate:
                    return new Color(0.18f, 0.95f, 0.48f, 0.9f);
                case CombatTacticalVolumeType.ExposureLane:
                    return new Color(0.3f, 0.58f, 1f, 0.9f);
                default:
                    return new Color(0.82f, 0.35f, 1f, 0.85f);
            }
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

        void OnGUI()
        {
            if (!Application.isPlaying || !showRuntimePanel)
                return;
            const float width = 330f;
            float height = Mathf.Min(430f, Screen.height - 32f);
            var area = new Rect(
                Mathf.Max(16f, Screen.width - width - 16f),
                16f,
                width,
                height);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("战斗地图实验室（F8 关闭，F7 切换语义）");
            GUILayout.Label(
                "基础 Seed  "
                + CurrentBaseSeed
                + "    候选 Seed  "
                + CurrentSeed
                + "\n"
                + (IsReady ? "状态：可用" : "状态：未通过"));
            GUILayout.Label(
                "校验码  "
                + (CurrentPlan?.checksum ?? "—"));
            GUILayout.Label(
                "总分  "
                + (CurrentValidation != null
                    ? CurrentValidation.score.ToString("0.0")
                    : "—"));

            GUILayout.BeginHorizontal();
            GUI.enabled = !regenerationLocked && !isGenerating;
            if (GUILayout.Button("上一个 Seed"))
            {
                appliedSeed = CurrentBaseSeed - 1;
                useRecipeSeed = false;
                Rebuild();
            }
            if (GUILayout.Button("下一个 Seed"))
            {
                appliedSeed = CurrentBaseSeed + 1;
                useRecipeSeed = false;
                Rebuild();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重新生成"))
                Rebuild();
            if (GUILayout.Button("重新验证"))
                ValidateCurrentCandidate();
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            bool overlay = GUILayout.Toggle(
                showSemanticOverlay,
                "显示战斗语义");
            if (overlay != showSemanticOverlay)
                SetSemanticOverlayVisible(overlay);
            if (regenerationLocked)
                GUILayout.Label("当前状态已锁定地图重建。");

            panelScroll = GUILayout.BeginScrollView(panelScroll);
            CombatMapViolation[] violations =
                CurrentValidation?.violations;
            if (violations == null || violations.Length == 0)
            {
                GUILayout.Label("没有验证问题。");
            }
            else
            {
                for (int i = 0; i < violations.Length; i++)
                {
                    CombatMapViolation violation = violations[i];
                    if (violation == null)
                        continue;
                    GUILayout.Label(
                        "["
                        + (violation.severity
                           == CombatMapViolationSeverity.HardError
                            ? "硬错误"
                            : "警告")
                        + "] "
                        + violation.code
                        + "\n"
                        + violation.message);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    

public float SampleCollisionHeight(float worldX, float worldZ)
        {
            CombatSemanticPlan plan = CurrentPlan;
            AirCombatMapSettings settings = currentSettings ?? GetSettings();
            if (plan == null)
                return transform.position.y;

            int chunkCount = Mathf.Max(
                1,
                Mathf.CeilToInt(settings.mapSize / settings.chunkSize));
            int resolution = Mathf.Max(
                1,
                chunkCount * settings.chunkResolution);
            float step = settings.mapSize / resolution;
            float originX = plan.mapCenter.x - settings.mapSize * 0.5f;
            float originZ = plan.mapCenter.z - settings.mapSize * 0.5f;
            float gx = Mathf.Clamp(
                (worldX - originX) / step,
                0f,
                resolution - 0.0001f);
            float gz = Mathf.Clamp(
                (worldZ - originZ) / step,
                0f,
                resolution - 0.0001f);
            int ix = Mathf.Clamp(
                Mathf.FloorToInt(gx), 0, resolution - 1);
            int iz = Mathf.Clamp(
                Mathf.FloorToInt(gz), 0, resolution - 1);
            float fx = Mathf.Clamp01(gx - ix);
            float fz = Mathf.Clamp01(gz - iz);

            float x0 = originX + ix * step;
            float z0 = originZ + iz * step;
            float h00 = CombatMapGenerator.SampleHeight(
                settings, plan, x0, z0);
            float h10 = CombatMapGenerator.SampleHeight(
                settings, plan, x0 + step, z0);
            float h01 = CombatMapGenerator.SampleHeight(
                settings, plan, x0, z0 + step);
            float h11 = CombatMapGenerator.SampleHeight(
                settings, plan, x0 + step, z0 + step);

            if (fx + fz <= 1f)
            {
                return h00 + (h10 - h00) * fx +
                       (h01 - h00) * fz;
            }
            return h10 * (1f - fz) +
                   h01 * (1f - fx) +
                   h11 * (fx + fz - 1f);
        }

        public float SampleCollisionHeight(Vector3 worldPosition)
        {
            return SampleCollisionHeight(
                worldPosition.x,
                worldPosition.z);
        }


public bool RebuildRuntime(
            AirCombatMapSettings settings,
            int seed)
        {
            if (settings == null || regenerationLocked || isGenerating)
                return false;
            AirCombatMapSettings runtime = settings.ValidatedCopy(seed);
            CombatMapGenerationResult result = selectBestCandidate
                ? CombatMapGenerator.GenerateBest(
                    runtime,
                    GetMapCenter(runtime),
                    seed)
                : CombatMapGenerator.Generate(
                    runtime,
                    GetMapCenter(runtime),
                    seed);
            return CommitResult(result, runtime);
        }

        public void SetTerrainCollisionEnabled(bool value)
        {
            terrainCollisionEnabled = value;
            if (generatedRoot == null)
                return;
            Collider[] colliders =
                generatedRoot.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider != null && !collider.isTrigger)
                    collider.enabled = value;
            }
            Physics.SyncTransforms();
        }


void BuildCollisionSurface(
            AirCombatMapSettings settings,
            CombatSemanticPlan plan,
            Transform terrainRoot,
            int chunkCount,
            int terrainLayer)
        {
            int resolution = Mathf.Max(
                1,
                chunkCount * settings.chunkResolution);
            int side = resolution + 1;
            var vertices = new Vector3[side * side];
            var triangles = new int[resolution * resolution * 6];
            float step = settings.mapSize / resolution;
            float half = settings.mapSize * 0.5f;

            for (int z = 0; z <= resolution; z++)
            for (int x = 0; x <= resolution; x++)
            {
                float localX = -half + x * step;
                float localZ = -half + z * step;
                float height = CombatMapGenerator.SampleHeight(
                    settings,
                    plan,
                    plan.mapCenter.x + localX,
                    plan.mapCenter.z + localZ);
                vertices[z * side + x] = new Vector3(
                    localX,
                    height - plan.mapCenter.y,
                    localZ);
            }

            int triangle = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int a = z * side + x;
                int b = a + 1;
                int c = a + side;
                int d = c + 1;
                triangles[triangle++] = a;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = d;
            }

            var mesh = new Mesh
            {
                name = "CombatMapCollisionSurface",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Length > 65535
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            generatedMeshes.Add(mesh);

            var collisionObject = new GameObject("CollisionSurface")
            {
                hideFlags = generatedRoot.hideFlags,
                isStatic = true,
                layer = terrainLayer
            };
            collisionObject.transform.SetParent(terrainRoot, false);
            var collider = collisionObject.AddComponent<MeshCollider>();
            collider.cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices |
                MeshColliderCookingOptions.UseFastMidphase;
            collider.sharedMesh = mesh;
            collider.sharedMaterial = CreateFlightPhysicsMaterial();
            collider.enabled = terrainCollisionEnabled;
        }

        PhysicMaterial CreateFlightPhysicsMaterial()
        {
            var material = new PhysicMaterial("CombatMap Zero Friction")
            {
                hideFlags = HideFlags.HideAndDontSave,
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicMaterialCombine.Minimum,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
            generatedPhysicsMaterials.Add(material);
            return material;
        }

        static int ResolveLayer(string layerName, int fallback)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? layer : fallback;
        }


static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
                SetLayerRecursively(root.GetChild(index), layer);
        }
}
}
