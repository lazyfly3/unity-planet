using System.Collections.Generic;
using UnityEngine;

namespace CityGeneration
{
    public sealed class CitySelectionController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] Camera selectionCamera;
        [SerializeField] Collider groundCollider;
        [SerializeField] MeshFilter groundMeshFilter;
        [SerializeField] MeshRenderer groundRenderer;
        [SerializeField] CityNoiseTerrain noiseTerrain;
        [SerializeField] Transform boundaryMarkers;
        [SerializeField] LineRenderer boundaryLine;
        [SerializeField] Transform generatedCity;
        [SerializeField] Transform foundationsRoot;
        [SerializeField] Transform roadsRoot;
        [SerializeField] Transform blocksRoot;
        [SerializeField] Transform buildingsRoot;
        [SerializeField] Transform constructionEffectsRoot;
        [SerializeField] CityConstructionAnimator constructionAnimator;

        [Header("Building Models")]
        [SerializeField] bool useBuildingPrefabs = true;
        [SerializeField] bool overridePrefabMaterials;
        [SerializeField] GameObject[] buildingPrefabs = new GameObject[0];

        [Header("Scene")]
        [SerializeField, Min(20f)] float groundSize = 400f;
        [SerializeField] int seed = 12345;
        [SerializeField] CityGenerationSettings generationSettings = new CityGenerationSettings();

        [Header("Elevated Platform")]
        [SerializeField, Min(0.05f)] float platformTopClearance = 0.5f;
        [SerializeField, Min(0.1f)] float platformSlabThickness = 0.4f;
        [SerializeField, Min(2f)] float platformSupportSpacing = 20f;
        [SerializeField, Min(0.2f)] float platformSupportWidth = 1.4f;
        [SerializeField, Min(0f)] float platformMinimumSupportHeight = 0.6f;

        [Header("Colors")]
        [SerializeField] Color groundColor = new Color(0.16f, 0.2f, 0.17f);
        [SerializeField] Color validBoundaryColor = new Color(0.1f, 1f, 0.35f);
        [SerializeField] Color invalidBoundaryColor = new Color(1f, 0.18f, 0.12f);
        [SerializeField] Color roadColor = new Color(0.09f, 0.1f, 0.12f);
        [SerializeField] Color blockColor = new Color(0.36f, 0.4f, 0.34f);
        [SerializeField] Color foundationColor =
            new Color(0.18f, 0.23f, 0.28f);
        [SerializeField] Color waterColor = new Color(0.03f, 0.42f, 0.62f, 0.72f);

        readonly List<Vector2> boundaryPoints = new List<Vector2>();
        readonly List<GameObject> markerObjects = new List<GameObject>();
        readonly List<Material> runtimeMaterials = new List<Material>();

        Material groundMaterial;
        Material boundaryMaterial;
        Material roadMaterial;
        Material blockMaterial;
        Material foundationMaterial;
        Material waterMaterial;
        Material[] buildingMaterials;
        CityGenerationResult lastResult;
        CityPlatformLayout platformLayout;
        string status = "鼠标左键选择边界点；至少 3 点后按 Enter 生成。";
        bool isGenerated;
        bool isGenerating;

        public IReadOnlyList<Vector2> BoundaryPoints => boundaryPoints;
        public bool IsGenerated => isGenerated;
        public bool IsGenerating => isGenerating;
        public float ConstructionProgress =>
            constructionAnimator != null
                ? constructionAnimator.ConstructionProgress
                : 0f;
        public string LastStatus => status;
        public CityGenerationResult LastResult => lastResult;
        public CityPlatformLayout LastPlatformLayout => platformLayout;

        void Awake()
        {
            ResolveReferences();
            constructionAnimator.Configure(constructionEffectsRoot);
            CreateRuntimeMaterials();
            PrepareGround();
            PrepareBoundaryLine();
            ClearGeneratedObjects();
            RefreshBoundaryVisuals();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetCity();
                return;
            }

            if (isGenerating)
            {
                if (Input.GetKeyDown(KeyCode.Return)
                    || Input.GetKeyDown(KeyCode.KeypadEnter)
                    || Input.GetKeyDown(KeyCode.Space))
                {
                    CompleteConstructionImmediately();
                }
                return;
            }

            if (isGenerated)
                return;

            if (Input.GetKeyDown(KeyCode.Backspace))
                UndoLastPoint();

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                TryGenerate();

            if (Input.GetMouseButtonDown(0))
                TryAddPointUnderCursor();
        }

        public bool TryAddBoundaryPoint(Vector3 worldPoint)
        {
            if (isGenerated || isGenerating)
            {
                status = isGenerating
                    ? "城市正在施工；按 Enter 或空格立即完成，按 R 取消。"
                    : "城市已经生成；按 R 重新选择。";
                return false;
            }

            Vector2 point = new Vector2(worldPoint.x, worldPoint.z);
            for (int i = 0; i < boundaryPoints.Count; i++)
            {
                if (Vector2.Distance(boundaryPoints[i], point) < 0.1f)
                {
                    status = "该位置已经有一个边界点。";
                    return false;
                }
            }

            boundaryPoints.Add(point);
            CreateMarker(point, boundaryPoints.Count);
            RefreshBoundaryVisuals();
            return true;
        }

        public bool UndoLastPoint()
        {
            if (isGenerated || isGenerating || boundaryPoints.Count == 0)
                return false;

            boundaryPoints.RemoveAt(boundaryPoints.Count - 1);
            int lastMarker = markerObjects.Count - 1;
            if (lastMarker >= 0)
            {
                DestroySafely(markerObjects[lastMarker]);
                markerObjects.RemoveAt(lastMarker);
            }
            RefreshBoundaryVisuals();
            return true;
        }

        public bool TryGenerate()
        {
            if (isGenerated || isGenerating)
                return false;

            if (!CityPolygonGeometry.ValidateBoundary(
                    boundaryPoints,
                    generationSettings,
                    out string validationError))
            {
                status = validationError;
                RefreshBoundaryVisuals();
                return false;
            }

            status = "正在生成城市平台……";
            var generator = new CityGenerator();
            lastResult = generator.Generate(
                boundaryPoints,
                generationSettings,
                seed);
            if (!lastResult.IsSuccess)
            {
                status = lastResult.Error;
                return false;
            }

            platformLayout = CityElevatedPlatformPlanner.Create(
                lastResult.Regions,
                noiseTerrain,
                platformTopClearance,
                platformSlabThickness);
            lastResult.Diagnostics.MaximumGroundHeight =
                platformLayout.MaximumGroundHeight;
            lastResult.Diagnostics.PlatformTopHeight =
                platformLayout.TopHeight;
            lastResult.Diagnostics.MaximumFoundationClearance =
                platformLayout.MaximumClearance;
            lastResult.Diagnostics.FoundationCount =
                platformLayout.FoundationCount;

            ClearGeneratedObjects();
            CityConstructionJob job = CreateConstructionJob(lastResult);
            isGenerating = true;
            isGenerated = false;
            if (!constructionAnimator.Begin(
                    job,
                    HandleConstructionProgress,
                    HandleConstructionCompleted))
            {
                isGenerating = false;
                status = "无法启动城市施工动画。";
                return false;
            }
            RefreshBoundaryVisuals();
            return true;
        }

        public void CompleteConstructionImmediately()
        {
            if (!isGenerating || constructionAnimator == null)
                return;
            constructionAnimator.CompleteImmediately();
        }

        public void ResetCity()
        {
            if (constructionAnimator != null)
                constructionAnimator.Cancel();
            isGenerated = false;
            isGenerating = false;
            lastResult = null;
            platformLayout = null;
            boundaryPoints.Clear();

            for (int i = markerObjects.Count - 1; i >= 0; i--)
                DestroySafely(markerObjects[i]);
            markerObjects.Clear();

            ClearGeneratedObjects();
            if (noiseTerrain != null)
            {
                noiseTerrain.ClearCityPlan();
                groundCollider = noiseTerrain.GetComponent<MeshCollider>();
            }
            status = "鼠标左键选择边界点；至少 3 点后按 Enter 生成。";
            RefreshBoundaryVisuals();
        }

        void TryAddPointUnderCursor()
        {
            if (selectionCamera == null || groundCollider == null)
            {
                status = "场景缺少相机或地面 Collider。";
                return;
            }

            Ray ray = selectionCamera.ScreenPointToRay(Input.mousePosition);
            if (groundCollider.Raycast(ray, out RaycastHit hit, 2000f))
                TryAddBoundaryPoint(hit.point);
        }

        CityConstructionJob CreateConstructionJob(
            CityGenerationResult result)
        {
            float platformTop = platformLayout?.TopHeight ?? 0f;
            float platformUnderside = platformLayout != null
                ? platformTop - platformLayout.SlabThickness
                : platformTop;
            Vector2 center = Average(result.Boundary);
            var boundaryCopy = new List<Vector2>(result.Boundary);
            var job = new CityConstructionJob(
                boundaryCopy,
                center,
                platformLayout != null
                    ? platformLayout.MinimumGroundHeight - 0.25f
                    : platformUnderside,
                platformUnderside,
                platformTop);

            if (platformLayout != null)
            {
                for (int i = 0; i < platformLayout.Regions.Count; i++)
                {
                    int stableIndex = i;
                    IReadOnlyList<Vector2> region =
                        platformLayout.Regions[i];
                    job.Foundations.Add(new CityConstructionItem(
                        stableIndex,
                        Average(region),
                        () =>
                        {
                            GameObject foundation =
                                CityRuntimeMeshFactory
                                    .CreateElevatedCityPlatform(
                                        "Foundation_" + stableIndex,
                                        region,
                                        platformTop,
                                        platformLayout.SlabThickness,
                                        platformSupportSpacing,
                                        platformSupportWidth,
                                        platformMinimumSupportHeight,
                                        noiseTerrain,
                                        foundationMaterial,
                                        foundationsRoot,
                                        out int supportCount,
                                        out float maximumSupportHeight);
                            result.Diagnostics.FoundationColumnCount +=
                                supportCount;
                            result.Diagnostics.SupportPileCount +=
                                supportCount;
                            result.Diagnostics.MaximumFoundationClearance =
                                Mathf.Max(
                                    result.Diagnostics
                                        .MaximumFoundationClearance,
                                    maximumSupportHeight);
                            return foundation;
                        }));
                }
            }

            for (int i = 0; i < result.Roads.Count; i++)
            {
                int stableIndex = i;
                CityRoadSegment road = result.Roads[i];
                string roadName =
                    (road.IsMajor ? "MajorRoad_" : "MinorRoad_") + i;
                job.Roads.Add(new CityConstructionItem(
                    stableIndex,
                    (road.Start + road.End) * 0.5f,
                    () => CityRuntimeMeshFactory.CreateFlatObject(
                        roadName,
                        road.GetCorners(),
                        platformTop + 0.05f,
                        roadMaterial,
                        roadsRoot)));
            }

            for (int i = 0; i < result.Blocks.Count; i++)
            {
                int stableIndex = i;
                CityBlockData block = result.Blocks[i];
                job.Blocks.Add(new CityConstructionItem(
                    stableIndex,
                    Average(block.Footprint),
                    () => CityRuntimeMeshFactory.CreateExtrudedObject(
                        "Block_" + stableIndex,
                        block.Footprint,
                        platformTop + 0.08f,
                        0.14f,
                        blockMaterial,
                        blocksRoot)));
            }

            for (int i = 0; i < result.Buildings.Count; i++)
            {
                int stableIndex = i;
                CityBuildingData building = result.Buildings[i];
                Material material =
                    buildingMaterials[building.MaterialIndex % buildingMaterials.Length];
                GameObject prefab = buildingPrefabs != null
                    && buildingPrefabs.Length > 0
                    ? buildingPrefabs[
                        building.PrefabIndex % buildingPrefabs.Length]
                    : null;
                job.Buildings.Add(new CityConstructionItem(
                    stableIndex,
                    Average(building.Footprint),
                    () =>
                    {
                        if (useBuildingPrefabs && prefab != null)
                        {
                            return CityRuntimeMeshFactory
                                .CreatePrefabBuildingOnPlane(
                                    "Building_" + stableIndex,
                                    prefab,
                                    building.Footprint,
                                    platformTop,
                                    material,
                                    overridePrefabMaterials,
                                    buildingsRoot);
                        }

                        return CityRuntimeMeshFactory.CreateExtrudedObject(
                            "Building_" + stableIndex,
                            building.Footprint,
                            platformTop + 0.03f,
                            building.Height,
                            material,
                            buildingsRoot);
                    }));
            }

            return job;
        }

        void HandleConstructionProgress(
            CityConstructionPhase phase,
            float progress,
            float secondsRemaining)
        {
            string phaseLabel;
            switch (phase)
            {
                case CityConstructionPhase.Scanning:
                    phaseLabel = "扫描边界";
                    break;
                case CityConstructionPhase.Supports:
                    phaseLabel = "建造平台立柱";
                    break;
                case CityConstructionPhase.Platform:
                    phaseLabel = "铺设平台";
                    break;
                case CityConstructionPhase.Roads:
                    phaseLabel = "铺设道路";
                    break;
                case CityConstructionPhase.Blocks:
                    phaseLabel = "划分街区";
                    break;
                case CityConstructionPhase.Buildings:
                    phaseLabel = "打印建筑";
                    break;
                case CityConstructionPhase.Completion:
                    phaseLabel = "启动城市";
                    break;
                default:
                    phaseLabel = "准备施工";
                    break;
            }

            status =
                $"{phaseLabel} {progress * 100f:F0}% · " +
                $"约 {secondsRemaining:F1} 秒\n" +
                "按 Enter 或空格立即完成，按 R 取消。";
        }

        void HandleConstructionCompleted()
        {
            isGenerating = false;
            isGenerated = true;
            status =
                $"生成完成：{lastResult.Regions.Count} 个平台区域，" +
                $"{lastResult.Roads.Count} 段道路，" +
                $"{lastResult.Blocks.Count} 个街区，" +
                $"{lastResult.Buildings.Count} 栋建筑。\n" +
                $"最高地面 {platformLayout.MaximumGroundHeight:F1}m，" +
                $"平台顶面 {platformLayout.TopHeight:F1}m，" +
                $"最大悬空 {platformLayout.MaximumClearance:F1}m，" +
                $"立柱 {lastResult.Diagnostics.FoundationColumnCount} 根。" +
                "按 R 重置。";
            RefreshBoundaryVisuals();
        }

        void ClearGeneratedObjects()
        {
            ClearChildren(foundationsRoot);
            ClearChildren(roadsRoot);
            ClearChildren(blocksRoot);
            ClearChildren(buildingsRoot);
            ClearChildren(constructionEffectsRoot);
        }

        static void ClearChildren(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    DestroySafely(filter.sharedMesh);
                DestroySafely(child.gameObject);
            }
        }

        void CreateMarker(Vector2 point, int index)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "BoundaryPoint_" + index;
            marker.transform.SetParent(boundaryMarkers, false);
            marker.transform.position = new Vector3(
                point.x,
                SampleTerrainHeight(point) + 0.45f,
                point.y);
            marker.transform.localScale = new Vector3(0.9f, 0.45f, 0.9f);
            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
                DestroySafely(markerCollider);
            marker.GetComponent<MeshRenderer>().sharedMaterial = boundaryMaterial;
            markerObjects.Add(marker);
        }

        void RefreshBoundaryVisuals()
        {
            if (boundaryLine == null)
                return;

            int pointCount = boundaryPoints.Count;
            bool closeLine = pointCount >= 3;
            var sampledLine = new List<Vector3>();
            for (int i = 0; i < pointCount; i++)
            {
                if (i < markerObjects.Count && markerObjects[i] != null)
                {
                    Vector2 markerPoint = boundaryPoints[i];
                    markerObjects[i].transform.position = new Vector3(
                        markerPoint.x,
                        GetBoundaryVisualHeight(markerPoint, 0.45f),
                        markerPoint.y);
                }

                bool hasNext = i + 1 < pointCount || closeLine;
                if (!hasNext)
                {
                    AddTerrainLinePoint(boundaryPoints[i]);
                    continue;
                }

                Vector2 start = boundaryPoints[i];
                Vector2 end = boundaryPoints[(i + 1) % pointCount];
                int sampleCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(Vector2.Distance(start, end) / 3f));
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    AddTerrainLinePoint(
                        Vector2.Lerp(start, end, sample / (float)sampleCount));
                }
            }
            if (closeLine)
                AddTerrainLinePoint(boundaryPoints[0]);

            boundaryLine.positionCount = sampledLine.Count;
            if (sampledLine.Count > 0)
                boundaryLine.SetPositions(sampledLine.ToArray());

            void AddTerrainLinePoint(Vector2 point)
            {
                sampledLine.Add(new Vector3(
                    point.x,
                    GetBoundaryVisualHeight(point, 0.5f),
                    point.y));
            }

            bool valid = CityPolygonGeometry.ValidateBoundary(
                boundaryPoints,
                generationSettings,
                out string validationError);
            Color lineColor = pointCount < 3 || valid
                ? validBoundaryColor
                : invalidBoundaryColor;
            boundaryLine.startColor = lineColor;
            boundaryLine.endColor = lineColor;

            if (!isGenerated && !isGenerating)
            {
                if (pointCount < 3)
                    status = $"已选择 {pointCount} 个点；至少需要 3 个。";
                else if (valid)
                {
                    CityPolygonGeometry.ResolveClosedRegions(
                        boundaryPoints,
                        generationSettings,
                        out List<List<Vector2>> regions,
                        out _);
                    status =
                        $"边界有效（{pointCount} 点，{regions.Count} 个封闭区域）；" +
                        "按 Enter 生成城市。";
                }
                else
                    status = validationError;
            }
        }

        void ResolveReferences()
        {
            if (selectionCamera == null)
                selectionCamera = Camera.main;

            Transform root = transform.parent != null ? transform.parent : transform;
            if (groundCollider == null)
            {
                Transform ground = root.Find("Ground");
                if (ground != null)
                {
                    groundCollider = ground.GetComponent<Collider>();
                    if (groundCollider == null)
                        groundCollider = ground.gameObject.AddComponent<BoxCollider>();
                    groundMeshFilter = ground.GetComponent<MeshFilter>();
                    if (groundMeshFilter == null)
                        groundMeshFilter = ground.gameObject.AddComponent<MeshFilter>();
                    groundRenderer = ground.GetComponent<MeshRenderer>();
                    if (groundRenderer == null)
                        groundRenderer = ground.gameObject.AddComponent<MeshRenderer>();
                    noiseTerrain = ground.GetComponent<CityNoiseTerrain>();
                }
            }
            else if (noiseTerrain == null && groundCollider != null)
            {
                noiseTerrain = groundCollider.GetComponent<CityNoiseTerrain>();
            }

            if (boundaryMarkers == null)
                boundaryMarkers = transform.Find("BoundaryMarkers");
            if (boundaryLine == null)
            {
                Transform line = transform.Find("BoundaryLine");
                if (line != null)
                {
                    boundaryLine = line.GetComponent<LineRenderer>();
                    if (boundaryLine == null)
                        boundaryLine = line.gameObject.AddComponent<LineRenderer>();
                }
            }

            if (generatedCity == null)
                generatedCity = root.Find("GeneratedCity");
            if (generatedCity != null)
            {
                if (foundationsRoot == null)
                    foundationsRoot = generatedCity.Find("Foundations");
                if (foundationsRoot == null)
                {
                    var foundations = new GameObject("Foundations");
                    foundations.transform.SetParent(generatedCity, false);
                    foundationsRoot = foundations.transform;
                }
                if (roadsRoot == null)
                    roadsRoot = generatedCity.Find("Roads");
                if (blocksRoot == null)
                    blocksRoot = generatedCity.Find("Blocks");
                if (buildingsRoot == null)
                    buildingsRoot = generatedCity.Find("Buildings");
                if (constructionEffectsRoot == null)
                {
                    constructionEffectsRoot =
                        generatedCity.Find("ConstructionEffects");
                }
                if (constructionEffectsRoot == null)
                {
                    var effects = new GameObject("ConstructionEffects");
                    effects.transform.SetParent(generatedCity, false);
                    constructionEffectsRoot = effects.transform;
                }
            }
            if (constructionAnimator == null)
                constructionAnimator =
                    GetComponent<CityConstructionAnimator>();
            if (constructionAnimator == null)
                constructionAnimator =
                    gameObject.AddComponent<CityConstructionAnimator>();
        }

        void CreateRuntimeMaterials()
        {
            groundMaterial = TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                "City Ground",
                groundColor,
                0.05f));
            boundaryMaterial = TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                "City Boundary",
                validBoundaryColor,
                0.2f));
            roadMaterial = TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                "City Roads",
                roadColor,
                0.08f));
            blockMaterial = TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                "City Blocks",
                blockColor,
                0.04f));
            foundationMaterial = TrackMaterial(
                CityRuntimeMeshFactory.CreateMaterial(
                    "City Elevated Foundation",
                    foundationColor,
                    0.34f));
            waterMaterial = TrackMaterial(
                CityRuntimeMeshFactory.CreateTransparentMaterial(
                    "City Water",
                    waterColor,
                    0.86f));
            buildingMaterials = new[]
            {
                TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                    "Building Sand",
                    new Color(0.72f, 0.62f, 0.46f),
                    0.16f)),
                TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                    "Building Brick",
                    new Color(0.56f, 0.3f, 0.25f),
                    0.13f)),
                TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                    "Building Concrete",
                    new Color(0.48f, 0.52f, 0.55f),
                    0.11f)),
                TrackMaterial(CityRuntimeMeshFactory.CreateMaterial(
                    "Building Blue",
                    new Color(0.28f, 0.44f, 0.56f),
                    0.2f))
            };
        }

        Material TrackMaterial(Material material)
        {
            runtimeMaterials.Add(material);
            return material;
        }

        void PrepareGround()
        {
            if (noiseTerrain != null)
            {
                noiseTerrain.ConfigureSize(groundSize);
                noiseTerrain.Rebuild(groundMaterial, waterMaterial);
                groundCollider = noiseTerrain.GetComponent<MeshCollider>();
                groundMeshFilter = noiseTerrain.GetComponent<MeshFilter>();
                groundRenderer = noiseTerrain.GetComponent<MeshRenderer>();
                return;
            }

            if (groundMeshFilter != null)
            {
                if (groundMeshFilter.sharedMesh != null)
                    DestroySafely(groundMeshFilter.sharedMesh);
                groundMeshFilter.sharedMesh = CityRuntimeMeshFactory.CreateGroundMesh(groundSize);
            }
            if (groundRenderer != null)
                groundRenderer.sharedMaterial = groundMaterial;
            if (groundCollider is BoxCollider boxCollider)
            {
                boxCollider.center = new Vector3(0f, -0.05f, 0f);
                boxCollider.size = new Vector3(groundSize, 0.1f, groundSize);
            }
        }

        float SampleTerrainHeight(Vector2 point)
        {
            return noiseTerrain != null
                ? noiseTerrain.SampleHeight(point)
                : 0f;
        }

        float GetBoundaryVisualHeight(Vector2 point, float offset)
        {
            return (isGenerated || isGenerating) && platformLayout != null
                ? platformLayout.TopHeight + offset
                : SampleTerrainHeight(point) + offset;
        }

        void PrepareBoundaryLine()
        {
            if (boundaryLine == null)
                return;
            boundaryLine.useWorldSpace = true;
            boundaryLine.loop = false;
            boundaryLine.widthMultiplier = 0.7f;
            boundaryLine.numCapVertices = 4;
            boundaryLine.numCornerVertices = 2;
            boundaryLine.sharedMaterial = boundaryMaterial;
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 18,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            GUI.Box(
                new Rect(16f, 16f, 570f, 94f),
                "城市边界生成测试\n" +
                "左键选点　Backspace 撤销　Enter 生成/跳过　" +
                "空格跳过　R 重置　WASD/滚轮移动视角\n" +
                status,
                style);
        }

        void OnDestroy()
        {
            if (constructionAnimator != null)
                constructionAnimator.Cancel();
            if (noiseTerrain == null
                && groundMeshFilter != null
                && groundMeshFilter.sharedMesh != null)
            {
                DestroySafely(groundMeshFilter.sharedMesh);
            }
            for (int i = 0; i < runtimeMaterials.Count; i++)
                DestroySafely(runtimeMaterials[i]);
            runtimeMaterials.Clear();
        }

        static Vector2 Average(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count == 0)
                return Vector2.zero;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
                sum += points[i];
            return sum / points.Count;
        }

        static void DestroySafely(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
