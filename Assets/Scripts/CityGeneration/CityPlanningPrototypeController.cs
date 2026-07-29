using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CityGeneration
{
    [DisallowMultipleComponent]
    public sealed class CityPlanningPrototypeController : MonoBehaviour
    {
        [SerializeField] CitySelectionController selectionController;
        [SerializeField] Camera planningCamera;
        [SerializeField] Collider groundCollider;
        [SerializeField, Min(2f)] float anchorMarkerSize = 4f;
        [SerializeField, Min(2.5f)] float zoneBrushRadius = 10f;

        readonly List<CityPlanningAnchorData> anchors =
            new List<CityPlanningAnchorData>();
        readonly List<GameObject> anchorMarkers =
            new List<GameObject>();
        readonly List<Material> runtimeMaterials =
            new List<Material>();

        CityPlanningState state = CityPlanningState.DrawBoundary;
        CityPlanningAnchorType selectedAnchor =
            CityPlanningAnchorType.CityGate;
        CityZoneType selectedZone = CityZoneType.Residential;
        CityZonePaintGrid zoneGrid;
        CityZoneType[] zoneCells;
        CityPreviewResult[] candidates =
            Array.Empty<CityPreviewResult>();
        int selectedCandidate;
        GameObject zonePreviewRoot;
        GameObject planPreviewRoot;
        Material roadPreviewMaterial;
        Material blockPreviewMaterial;
        Material parkPreviewMaterial;
        Material platformPreviewMaterial;
        CancellationTokenSource previewCancellation;
        string status = "请先用鼠标左键绘制统一城市边界。";
        bool previewBlockedBySupportHeight;
        int nextAnchorSequence;

        public CityPlanningState State => state;
        public IReadOnlyList<CityPlanningAnchorData> Anchors => anchors;
        public CityZonePaintGrid ZoneGrid => zoneGrid;
        public IReadOnlyList<CityPreviewResult> Candidates => candidates;
        public int SelectedCandidate => selectedCandidate;

        void Awake()
        {
            ResolveReferences();
            if (selectionController != null)
                selectionController.SetLegacyInputEnabled(false);
            CreatePreviewMaterials();
        }

        void OnEnable()
        {
            ResolveReferences();
            if (selectionController != null)
                selectionController.SetLegacyInputEnabled(false);
        }

        void Update()
        {
            if (selectionController == null)
                return;

            if (state == CityPlanningState.Constructing)
            {
                if (!selectionController.IsGenerating
                    && selectionController.IsGenerated)
                {
                    state = CityPlanningState.Locked;
                    status = "城市施工完成。按 R 回收后重新规划。";
                }
                return;
            }

            if (selectionController.IsDeconstructing)
            {
                if (Input.GetKeyDown(KeyCode.R)
                    || Input.GetKeyDown(KeyCode.Return)
                    || Input.GetKeyDown(KeyCode.Space))
                {
                    selectionController
                        .CompleteDeconstructionImmediately();
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                if (state == CityPlanningState.Locked)
                {
                    selectionController.RequestResetCity();
                    ResetPlanningData(false);
                }
                else
                {
                    ResetPlanningData(true);
                }
                return;
            }

            if (state == CityPlanningState.Locked)
                return;

            if (Input.GetKeyDown(KeyCode.Backspace))
                HandleBack();
            if (Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                HandleConfirm();
            }

            switch (state)
            {
                case CityPlanningState.DrawBoundary:
                    if (Input.GetMouseButtonDown(0)
                        && TryGetGroundPoint(out Vector3 boundaryPoint))
                    {
                        selectionController.TryAddBoundaryPoint(
                            boundaryPoint);
                    }
                    break;
            }
        }

        void HandleModeShortcuts()
        {
            if (state == CityPlanningState.PlaceAnchors)
            {
                if (Input.GetKeyDown(KeyCode.G))
                    selectedAnchor = CityPlanningAnchorType.CityGate;
                if (Input.GetKeyDown(KeyCode.C))
                    selectedAnchor = CityPlanningAnchorType.Cbd;
                if (Input.GetKeyDown(KeyCode.T))
                    selectedAnchor = CityPlanningAnchorType.TransitHub;
                if (Input.GetKeyDown(KeyCode.I))
                    selectedAnchor =
                        CityPlanningAnchorType.IndustrialHub;
                if (Input.GetKeyDown(KeyCode.P))
                    selectedAnchor = CityPlanningAnchorType.Park;
                if (Input.GetKeyDown(KeyCode.V))
                    selectedAnchor = CityPlanningAnchorType.CivicCenter;
                if (Input.GetKeyDown(KeyCode.L))
                    selectedAnchor = CityPlanningAnchorType.Landmark;
            }
            else if (state == CityPlanningState.PaintZones)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1))
                    selectedZone = CityZoneType.Commercial;
                if (Input.GetKeyDown(KeyCode.Alpha2))
                    selectedZone = CityZoneType.MixedUse;
                if (Input.GetKeyDown(KeyCode.Alpha3))
                    selectedZone = CityZoneType.Residential;
                if (Input.GetKeyDown(KeyCode.Alpha4))
                    selectedZone = CityZoneType.Industrial;
                if (Input.GetKeyDown(KeyCode.Alpha5))
                    selectedZone = CityZoneType.Civic;
                if (Input.GetKeyDown(KeyCode.Alpha6))
                    selectedZone = CityZoneType.Park;
                if (Input.GetKeyDown(KeyCode.Q))
                    zoneBrushRadius = Mathf.Max(
                        2.5f,
                        zoneBrushRadius - 2.5f);
                if (Input.GetKeyDown(KeyCode.E))
                    zoneBrushRadius = Mathf.Min(
                        40f,
                        zoneBrushRadius + 2.5f);
            }
        }

        void HandleAnchorInput()
        {
            if (Input.GetMouseButtonDown(0)
                && TryGetGroundPoint(out Vector3 point))
            {
                AddAnchor(new Vector2(point.x, point.z));
            }
            if (Input.GetMouseButtonDown(1)
                && TryGetGroundPoint(out Vector3 removePoint))
            {
                RemoveNearestAnchor(
                    new Vector2(removePoint.x, removePoint.z));
            }
        }

        void HandleZonePainting()
        {
            if ((!Input.GetMouseButton(0)
                 && !Input.GetMouseButton(1))
                || !TryGetGroundPoint(out Vector3 point))
            {
                return;
            }
            PaintZone(
                new Vector2(point.x, point.z),
                Input.GetMouseButton(1)
                    ? CityZoneType.Unassigned
                    : selectedZone);
        }

        void HandleCandidateInput()
        {
            if (candidates == null || candidates.Length == 0)
                return;
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SelectCandidate(0);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                SelectCandidate(1);
            if (Input.GetKeyDown(KeyCode.Alpha3))
                SelectCandidate(2);
        }

        void HandleBack()
        {
            switch (state)
            {
                case CityPlanningState.DrawBoundary:
                    selectionController.UndoLastPoint();
                    break;
                case CityPlanningState.ReviewIssues:
                case CityPlanningState.ConfirmRepairs:
                    ClearPlanPreview();
                    selectionController.ResetCity();
                    state = CityPlanningState.DrawBoundary;
                    status = "已返回边界绘制，请重新框选城市范围。";
                    break;
            }
        }

        void HandleConfirm()
        {
            switch (state)
            {
                case CityPlanningState.DrawBoundary:
                    ConfirmBoundary();
                    break;
                case CityPlanningState.ReviewIssues:
                case CityPlanningState.ConfirmRepairs:
                    ConfirmSelectedCandidate();
                    break;
            }
        }

        void ConfirmBoundary()
        {
            IReadOnlyList<Vector2> boundary =
                selectionController.BoundaryPoints;
            if (boundary.Count < 3)
            {
                status = "边界至少需要3个点。";
                return;
            }
            BeginPreviewGeneration();
        }

        void ConfirmAnchors()
        {
            int gates = 0;
            int cbd = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].type
                    == CityPlanningAnchorType.CityGate)
                {
                    gates++;
                }
                if (anchors[i].type == CityPlanningAnchorType.Cbd)
                    cbd++;
            }
            if (gates < 2 || cbd < 1)
            {
                status = $"需要至少2个入口和1个CBD；当前入口{gates}、CBD{cbd}。";
                return;
            }
            EnsureZoneGrid();
            state = CityPlanningState.PaintZones;
            status =
                "涂功能区：1商业、2混合、3住宅、4工业、5市政、6公园；"
                + "Q/E调画笔，左键涂、右键擦，Enter生成候选。";
        }

        async void BeginPreviewGeneration()
        {
            if (state == CityPlanningState.GeneratePreview)
                return;
            CityPlanData plan = BuildPlan();
            state = CityPlanningState.GeneratePreview;
            status =
                "系统正在分析边界，自动推导入口、CBD、功能分区并筛选最佳路网……";
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            previewCancellation = new CancellationTokenSource();

            try
            {
                var pipeline = new CityPlanningPipeline(
                    selectionController.GenerationSettings,
                    selectionController.BuildingPrefabs.Count);
                CityPreviewResult preview =
                    await pipeline.GenerateAutomaticPreviewAsync(
                    plan,
                    previewCancellation.Token);
                if (preview == null)
                {
                    state = CityPlanningState.DrawBoundary;
                    status = "没有生成出有效方案，请重新绘制边界。";
                    return;
                }
                candidates = new[] { preview };
                selectedCandidate = 0;
                state = CityPlanningState.ReviewIssues;
                ShowSelectedCandidate();
            }
            catch (OperationCanceledException)
            {
                state = CityPlanningState.DrawBoundary;
                status = "已取消自动规划。";
            }
            catch (Exception exception)
            {
                state = CityPlanningState.DrawBoundary;
                status = "自动规划失败：" + exception.Message;
                Debug.LogException(exception, this);
            }
        }

        void ConfirmSelectedCandidate()
        {
            if (candidates == null
                || selectedCandidate < 0
                || selectedCandidate >= candidates.Length)
            {
                return;
            }
            CityPreviewResult candidate = candidates[selectedCandidate];
            if (candidate.Validation == null
                || !candidate.Validation.CanCommit
                || previewBlockedBySupportHeight)
            {
                status =
                    $"自动方案评分 {candidate.Score:F1}，存在阻止施工的问题。"
                    + "请返回并重新绘制边界。";
                state = CityPlanningState.ReviewIssues;
                return;
            }

            ClearPlanPreview();
            ClearZonePreview();
            if (!selectionController.TryBeginPlannedConstruction(
                    candidate.Output))
            {
                status = "无法启动施工，请返回检查规划。";
                state = CityPlanningState.ReviewIssues;
                ShowSelectedCandidate();
                return;
            }
            state = CityPlanningState.Constructing;
            status = "规划已确认，正在分阶段施工。";
        }

        void SelectCandidate(int index)
        {
            if (candidates == null
                || index < 0
                || index >= candidates.Length)
            {
                return;
            }
            selectedCandidate = index;
            ShowSelectedCandidate();
        }

        void ShowSelectedCandidate()
        {
            ClearPlanPreview();
            previewBlockedBySupportHeight = false;
            CityPreviewResult candidate = candidates[selectedCandidate];
            CityGenerationResult result =
                candidate.Output?.legacyResult;
            if (result == null)
            {
                status = FirstIssue(candidate.Validation);
                return;
            }

            planPreviewRoot = new GameObject("CityPlanPreview");
            CityPlatformLayout platform =
                CityElevatedPlatformPlanner.Create(
                    result.Regions,
                    selectionController.TerrainSampler,
                    0.5f,
                    0.4f);
            previewBlockedBySupportHeight =
                platform.MaximumClearance > 40f;
            CityRuntimeMeshFactory.CreateFlatObject(
                "UnifiedPlatformPreview",
                result.Boundary,
                platform.TopHeight,
                platformPreviewMaterial,
                planPreviewRoot.transform);
            for (int i = 0; i < result.Roads.Count; i++)
            {
                CityRoadSegment road = result.Roads[i];
                CityRuntimeMeshFactory.CreateFlatObject(
                    "RoadPreview_" + i,
                    road.GetCorners(),
                    platform.TopHeight + 0.06f,
                    roadPreviewMaterial,
                    planPreviewRoot.transform);
            }
            for (int i = 0; i < result.Blocks.Count; i++)
            {
                Material surfaceMaterial =
                    candidate.Output.blocks != null
                    && i < candidate.Output.blocks.Length
                    && candidate.Output.blocks[i].zoneType
                        == CityZoneType.Park
                        ? parkPreviewMaterial
                        : blockPreviewMaterial;
                CityRuntimeMeshFactory.CreateFlatObject(
                    "BlockPreview_" + i,
                    result.Blocks[i].Footprint,
                    platform.TopHeight + 0.04f,
                    surfaceMaterial,
                    planPreviewRoot.transform);
            }

            int errors = CountIssues(
                candidate.Validation,
                CityPlanningIssueSeverity.Error);
            int warnings = CountIssues(
                candidate.Validation,
                CityPlanningIssueSeverity.Warning);
            status =
                $"系统最佳方案：{candidate.Output.roadPatternId}，"
                + $"评分 {candidate.Score:F1}，"
                + $"{result.Roads.Count}段路、"
                + $"{result.Blocks.Count}街区、"
                + $"{result.Buildings.Count}栋楼；"
                + $"错误{errors}、警告{warnings}。"
                + " Enter确认施工，Backspace返回重画。";
            if (errors > 0)
                status += "\n" + FirstIssue(candidate.Validation);
            if (candidate.Validation != null
                && !candidate.Validation.CanCommit
                && errors == 0)
            {
                status +=
                    "\n自动评分未达到80分，不能施工，请重新框选更规则的边界。";
            }
            if (previewBlockedBySupportHeight)
            {
                status +=
                    $"\n最大支柱高度 {platform.MaximumClearance:F1}m"
                    + "，超过40m安全限制，请缩小或移动边界。";
            }
            state = errors > 0
                || previewBlockedBySupportHeight
                || candidate.Validation == null
                || !candidate.Validation.CanCommit
                ? CityPlanningState.ReviewIssues
                : CityPlanningState.ConfirmRepairs;
        }

        void AddAnchor(Vector2 position)
        {
            IReadOnlyList<Vector2> boundary =
                selectionController.BoundaryPoints;
            if (!CityPolygonGeometry.ContainsPoint(boundary, position))
            {
                status = "锚点必须位于城市边界内。";
                return;
            }
            var anchor = new CityPlanningAnchorData
            {
                stableId =
                    "anchor-" + (nextAnchorSequence++).ToString("D3"),
                type = selectedAnchor,
                position = position,
                influenceRadius = DefaultInfluence(selectedAnchor)
            };
            anchors.Add(anchor);
            anchorMarkers.Add(CreateAnchorMarker(anchor));
            status = $"已放置 {AnchorLabel(selectedAnchor)}。";
        }

        void RemoveNearestAnchor(Vector2 position)
        {
            int best = -1;
            float distance = anchorMarkerSize * anchorMarkerSize * 4f;
            for (int i = 0; i < anchors.Count; i++)
            {
                float candidate = Vector2.SqrMagnitude(
                    anchors[i].position - position);
                if (candidate < distance)
                {
                    distance = candidate;
                    best = i;
                }
            }
            if (best >= 0)
                RemoveAnchorAt(best);
        }

        void RemoveAnchorAt(int index)
        {
            if (index < 0 || index >= anchors.Count)
                return;
            anchors.RemoveAt(index);
            if (index < anchorMarkers.Count)
            {
                Destroy(anchorMarkers[index]);
                anchorMarkers.RemoveAt(index);
            }
            status = "已删除锚点。";
        }

        GameObject CreateAnchorMarker(CityPlanningAnchorData anchor)
        {
            GameObject marker = GameObject.CreatePrimitive(
                PrimitiveType.Cylinder);
            marker.name = "PlanningAnchor_" + anchor.type;
            marker.transform.SetParent(transform, true);
            float ground = SampleGroundHeight(anchor.position);
            marker.transform.position = new Vector3(
                anchor.position.x,
                ground + anchorMarkerSize * 0.5f,
                anchor.position.y);
            marker.transform.localScale = new Vector3(
                anchorMarkerSize,
                anchorMarkerSize,
                anchorMarkerSize);
            Collider markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
                Destroy(markerCollider);
            Material material = CityRuntimeMeshFactory.CreateMaterial(
                "Anchor " + anchor.type,
                AnchorColor(anchor.type),
                0.18f);
            runtimeMaterials.Add(material);
            marker.GetComponent<MeshRenderer>().sharedMaterial = material;
            return marker;
        }

        void EnsureZoneGrid()
        {
            IReadOnlyList<Vector2> boundary =
                selectionController.BoundaryPoints;
            GetBounds(
                boundary,
                out Vector2 minimum,
                out Vector2 maximum);
            float size = CityPlanData.DefaultZoneCellSize;
            int width = Mathf.Max(
                1,
                Mathf.CeilToInt((maximum.x - minimum.x) / size));
            int height = Mathf.Max(
                1,
                Mathf.CeilToInt((maximum.y - minimum.y) / size));
            if (zoneGrid != null
                && zoneGrid.width == width
                && zoneGrid.height == height)
            {
                return;
            }
            zoneGrid = new CityZonePaintGrid
            {
                origin = minimum,
                cellSize = size,
                width = width,
                height = height
            };
            zoneCells = new CityZoneType[width * height];
        }

        void PaintZone(Vector2 position, CityZoneType type)
        {
            EnsureZoneGrid();
            float radiusSquared = zoneBrushRadius * zoneBrushRadius;
            for (int y = 0; y < zoneGrid.height; y++)
            {
                for (int x = 0; x < zoneGrid.width; x++)
                {
                    Vector2 center = zoneGrid.origin + new Vector2(
                        (x + 0.5f) * zoneGrid.cellSize,
                        (y + 0.5f) * zoneGrid.cellSize);
                    if (Vector2.SqrMagnitude(center - position)
                            > radiusSquared
                        || !CityPolygonGeometry.ContainsPoint(
                            selectionController.BoundaryPoints,
                            center))
                    {
                        continue;
                    }
                    int index = y * zoneGrid.width + x;
                    if (zoneCells[index] == type)
                        continue;
                    zoneCells[index] = type;
                }
            }
        }

        void RebuildZonePreview()
        {
            ClearZonePreview();
            if (zoneGrid == null || zoneCells == null)
                return;
            zonePreviewRoot = new GameObject("CityZonePreview");
            for (int zoneIndex = 1;
                 zoneIndex <= (int)CityZoneType.Park;
                 zoneIndex++)
            {
                CityZoneType type = (CityZoneType)zoneIndex;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (int index = 0; index < zoneCells.Length; index++)
                {
                    if (zoneCells[index] != type)
                        continue;
                    int x = index % zoneGrid.width;
                    int y = index / zoneGrid.width;
                    Vector2 first = zoneGrid.origin + new Vector2(
                        x * zoneGrid.cellSize,
                        y * zoneGrid.cellSize);
                    Vector2 last = first
                        + Vector2.one * zoneGrid.cellSize;
                    float height = SampleGroundHeight(
                        (first + last) * 0.5f) + 0.12f;
                    int firstVertex = vertices.Count;
                    vertices.Add(new Vector3(
                        first.x,
                        height,
                        first.y));
                    vertices.Add(new Vector3(
                        last.x,
                        height,
                        first.y));
                    vertices.Add(new Vector3(
                        last.x,
                        height,
                        last.y));
                    vertices.Add(new Vector3(
                        first.x,
                        height,
                        last.y));
                    triangles.Add(firstVertex);
                    triangles.Add(firstVertex + 2);
                    triangles.Add(firstVertex + 1);
                    triangles.Add(firstVertex);
                    triangles.Add(firstVertex + 3);
                    triangles.Add(firstVertex + 2);
                }
                if (vertices.Count == 0)
                    continue;
                var child = new GameObject("Zone_" + type);
                child.transform.SetParent(
                    zonePreviewRoot.transform,
                    false);
                var mesh = new Mesh
                {
                    name = "Zone Preview " + type,
                    vertices = vertices.ToArray(),
                    triangles = triangles.ToArray()
                };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                Material material =
                    CityRuntimeMeshFactory.CreateMaterial(
                        "Zone " + type,
                        ZoneColor(type),
                        0.05f);
                runtimeMaterials.Add(material);
                child.AddComponent<MeshRenderer>()
                    .sharedMaterial = material;
            }
        }

        CityPlanData BuildPlan()
        {
            return new CityPlanData
            {
                cityId = "citygenerate-prototype",
                generatorVersion =
                    CityPlanData.CurrentGeneratorVersion,
                seed = selectionController.Seed,
                boundary = Copy(
                    selectionController.BoundaryPoints),
                planRevision = 1
            };
        }

        void ResetPlanningData(bool resetSelection)
        {
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            previewCancellation = null;
            ClearPlanPreview();
            ClearZonePreview();
            for (int i = anchorMarkers.Count - 1; i >= 0; i--)
                Destroy(anchorMarkers[i]);
            anchorMarkers.Clear();
            anchors.Clear();
            zoneGrid = null;
            zoneCells = null;
            candidates = Array.Empty<CityPreviewResult>();
            selectedCandidate = 0;
            previewBlockedBySupportHeight = false;
            nextAnchorSequence = 0;
            if (resetSelection)
                selectionController.ResetCity();
            state = CityPlanningState.DrawBoundary;
            status = "请先用鼠标左键绘制统一城市边界。";
        }

        void ClearPlanPreview()
        {
            if (planPreviewRoot != null)
            {
                DestroyRuntimeMeshes(planPreviewRoot);
                Destroy(planPreviewRoot);
            }
            planPreviewRoot = null;
        }

        void ClearZonePreview()
        {
            if (zonePreviewRoot != null)
            {
                DestroyRuntimeMeshes(zonePreviewRoot);
                Destroy(zonePreviewRoot);
            }
            zonePreviewRoot = null;
        }

        bool TryGetGroundPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (planningCamera == null || groundCollider == null)
                return false;
            Ray ray = planningCamera.ScreenPointToRay(
                Input.mousePosition);
            if (!groundCollider.Raycast(
                    ray,
                    out RaycastHit hit,
                    3000f))
            {
                return false;
            }
            point = hit.point;
            return true;
        }

        float SampleGroundHeight(Vector2 position)
        {
            ICityTerrainSampler sampler =
                selectionController.TerrainSampler;
            return sampler == null
                ? 0f
                : sampler.SampleTerrain(position).GroundHeight;
        }

        void ResolveReferences()
        {
            if (selectionController == null)
            {
                selectionController =
                    GetComponent<CitySelectionController>();
            }
            if (selectionController == null)
            {
                selectionController =
                    FindObjectOfType<CitySelectionController>();
            }
            if (selectionController != null)
            {
                if (planningCamera == null)
                    planningCamera =
                        selectionController.SelectionCamera;
                if (groundCollider == null)
                    groundCollider =
                        selectionController.GroundCollider;
            }
        }

        void CreatePreviewMaterials()
        {
            roadPreviewMaterial =
                CityRuntimeMeshFactory.CreateMaterial(
                    "Planning Road Preview",
                    new Color(0.06f, 0.08f, 0.11f),
                    0.1f);
            blockPreviewMaterial =
                CityRuntimeMeshFactory.CreateMaterial(
                    "Planning Block Preview",
                    new Color(0.43f, 0.44f, 0.45f),
                    0.16f);
            parkPreviewMaterial =
                CityRuntimeMeshFactory.CreateMaterial(
                    "Planning Park Preview",
                    new Color(0.22f, 0.38f, 0.24f),
                    0.03f);
            platformPreviewMaterial =
                CityRuntimeMeshFactory.CreateMaterial(
                    "Planning Platform Preview",
                    new Color(0.2f, 0.24f, 0.28f),
                    0.08f);
            runtimeMaterials.Add(roadPreviewMaterial);
            runtimeMaterials.Add(blockPreviewMaterial);
            runtimeMaterials.Add(parkPreviewMaterial);
            runtimeMaterials.Add(platformPreviewMaterial);
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            string mode = StateLabel(state);
            GUI.Box(
                new Rect(16f, 16f, 760f, 126f),
                "现代城市 PCG 自动规划　阶段：" + mode
                + "\n" + status
                + "\nR 重置　Backspace 返回/撤销　Enter 确认",
                style);
        }

        void OnDestroy()
        {
            previewCancellation?.Cancel();
            previewCancellation?.Dispose();
            ClearPlanPreview();
            ClearZonePreview();
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] != null)
                    Destroy(runtimeMaterials[i]);
            }
            runtimeMaterials.Clear();
            if (selectionController != null)
                selectionController.SetLegacyInputEnabled(true);
        }

        static void DestroyRuntimeMeshes(GameObject root)
        {
            if (root == null)
                return;
            MeshFilter[] filters =
                root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh != null)
                    Destroy(mesh);
            }
        }

        static int CountIssues(
            CityValidationReport report,
            CityPlanningIssueSeverity severity)
        {
            int count = 0;
            if (report?.issues == null)
                return count;
            for (int i = 0; i < report.issues.Length; i++)
            {
                if (report.issues[i] != null
                    && report.issues[i].severity == severity)
                {
                    count++;
                }
            }
            return count;
        }

        static string FirstIssue(CityValidationReport report)
        {
            if (report?.issues == null || report.issues.Length == 0)
                return "没有可显示的问题。";
            return report.issues[0]?.message ?? "未知规划问题。";
        }

        static float DefaultInfluence(
            CityPlanningAnchorType type)
        {
            switch (type)
            {
                case CityPlanningAnchorType.Cbd: return 85f;
                case CityPlanningAnchorType.IndustrialHub: return 70f;
                case CityPlanningAnchorType.Park: return 55f;
                case CityPlanningAnchorType.CivicCenter: return 50f;
                case CityPlanningAnchorType.TransitHub: return 60f;
                default: return 40f;
            }
        }

        static Color AnchorColor(
            CityPlanningAnchorType type)
        {
            switch (type)
            {
                case CityPlanningAnchorType.CityGate:
                    return new Color(0.1f, 0.9f, 1f);
                case CityPlanningAnchorType.Cbd:
                    return new Color(1f, 0.72f, 0.12f);
                case CityPlanningAnchorType.IndustrialHub:
                    return new Color(0.72f, 0.32f, 0.18f);
                case CityPlanningAnchorType.Park:
                    return new Color(0.18f, 0.8f, 0.25f);
                case CityPlanningAnchorType.CivicCenter:
                    return new Color(0.65f, 0.35f, 0.92f);
                case CityPlanningAnchorType.TransitHub:
                    return new Color(0.2f, 0.55f, 1f);
                default:
                    return Color.white;
            }
        }

        static Color ZoneColor(CityZoneType type)
        {
            switch (type)
            {
                case CityZoneType.Commercial:
                    return new Color(0.95f, 0.7f, 0.16f);
                case CityZoneType.MixedUse:
                    return new Color(0.8f, 0.38f, 0.75f);
                case CityZoneType.Residential:
                    return new Color(0.28f, 0.7f, 0.92f);
                case CityZoneType.Industrial:
                    return new Color(0.7f, 0.34f, 0.22f);
                case CityZoneType.Civic:
                    return new Color(0.55f, 0.42f, 0.9f);
                case CityZoneType.Park:
                    return new Color(0.18f, 0.72f, 0.25f);
                default:
                    return Color.gray;
            }
        }

        static string StateLabel(CityPlanningState value)
        {
            switch (value)
            {
                case CityPlanningState.DrawBoundary: return "绘制边界";
                case CityPlanningState.GeneratePreview: return "自动规划";
                case CityPlanningState.ReviewIssues: return "边界不适用";
                case CityPlanningState.ConfirmRepairs: return "最佳方案预览";
                case CityPlanningState.Constructing: return "施工";
                case CityPlanningState.Locked: return "已锁定";
                case CityPlanningState.Replan: return "重新规划";
                default: return "空闲";
            }
        }

        static string AnchorLabel(
            CityPlanningAnchorType value)
        {
            switch (value)
            {
                case CityPlanningAnchorType.CityGate: return "城市入口";
                case CityPlanningAnchorType.Cbd: return "CBD";
                case CityPlanningAnchorType.TransitHub: return "交通枢纽";
                case CityPlanningAnchorType.IndustrialHub: return "工业中心";
                case CityPlanningAnchorType.Park: return "公园";
                case CityPlanningAnchorType.CivicCenter: return "市政中心";
                case CityPlanningAnchorType.Landmark: return "地标";
                default: return value.ToString();
            }
        }

        static string ZoneLabel(CityZoneType value)
        {
            switch (value)
            {
                case CityZoneType.Commercial: return "商业";
                case CityZoneType.MixedUse: return "混合";
                case CityZoneType.Residential: return "住宅";
                case CityZoneType.Industrial: return "工业";
                case CityZoneType.Civic: return "市政";
                case CityZoneType.Park: return "公园";
                default: return "未分配";
            }
        }

        static Vector2[] Copy(IReadOnlyList<Vector2> source)
        {
            var values = new Vector2[source?.Count ?? 0];
            for (int i = 0; i < values.Length; i++)
                values[i] = source[i];
            return values;
        }

        CityPlanningAnchorData[] CloneAnchors()
        {
            var values = new CityPlanningAnchorData[anchors.Count];
            for (int i = 0; i < anchors.Count; i++)
                values[i] = anchors[i].Clone();
            return values;
        }

        static void GetBounds(
            IReadOnlyList<Vector2> values,
            out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.MaxValue, float.MaxValue);
            maximum = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < values.Count; i++)
            {
                minimum = Vector2.Min(minimum, values[i]);
                maximum = Vector2.Max(maximum, values[i]);
            }
        }
    }
}
