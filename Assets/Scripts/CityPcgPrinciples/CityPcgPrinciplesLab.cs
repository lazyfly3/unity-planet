using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityPlanet.CityPcg
{
    [Serializable]
    public sealed class CityPcgTheme
    {
        [Header("道路模型（留空时使用灰盒）")]
        public GameObject roadStraight;
        public GameObject roadCorner;
        public GameObject roadTee;
        public GameObject roadCross;
        public GameObject roadEnd;

        [Header("城市模型（留空时使用灰盒）")]
        public GameObject[] buildings = Array.Empty<GameObject>();
        public GameObject park;
        public GameObject facility;
    }

    /// <summary>
    /// Visual teaching scene for a dependency-free city PCG pipeline.
    /// All generated data remains valid when Dungeon Architect is absent.
    /// </summary>
    [ExecuteAlways]
    public sealed class CityPcgPrinciplesLab : MonoBehaviour
    {
        const string GeneratedRootName = "GeneratedCityPcg";

        [Header("一、确定性布局参数")]
        [SerializeField] CityPcgSettings settings = new CityPcgSettings();

        [Header("二、主题模型，可完全替换")]
        [SerializeField] CityPcgTheme theme = new CityPcgTheme();

        [Header("三、原理可视化")]
        [SerializeField] bool rebuildOnPlay = true;
        [SerializeField] bool showRoadGraph = true;
        [SerializeField] bool showMarkerLabels = true;
        [SerializeField] bool showRuntimePanel = true;

        [Header("实验场材质")]
        [SerializeField] Material roadMaterial;
        [SerializeField] Material buildingMaterial;
        [SerializeField] Material parkMaterial;
        [SerializeField] Material facilityMaterial;
        [SerializeField] Material spawnMaterial;
        [SerializeField] Material objectiveMaterial;
        [SerializeField] Material primaryRouteMaterial;
        [SerializeField] Material alternateRouteMaterial;

        CityPcgLayout layout;
        CityPcgReport report;
        Transform roadRoot;
        Transform buildingRoot;
        Transform markerRoot;

        public CityPcgSettings Settings => settings;
        public CityPcgReport Report => report;
        public bool HasValidLayout => report != null && report.valid;
        public string LastSummary => report?.Summary ?? "尚未生成";

        void OnEnable()
        {
            // Layout data is intentionally not serialized into the scene.
            // Reconstruct it from the saved settings so Scene Gizmos and the
            // validation report remain inspectable after reopening the scene.
            if (!Application.isPlaying)
                layout = CityPcgGenerator.Generate(settings, out report);
        }

        void Start()
        {
            if (Application.isPlaying && rebuildOnPlay)
                Rebuild();
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            if (Input.GetKeyDown(KeyCode.N))
            {
                settings.seed++;
                Rebuild();
            }
            else if (Input.GetKeyDown(KeyCode.P))
            {
                settings.seed--;
                Rebuild();
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                Rebuild();
            }
            else if (Input.GetKeyDown(KeyCode.G))
            {
                showRoadGraph = !showRoadGraph;
            }
        }

        public void ConfigureDemo(
            int seed,
            Material road,
            Material building,
            Material park,
            Material facility,
            Material spawn,
            Material objective,
            Material primaryRoute,
            Material alternateRoute)
        {
            settings.seed = seed;
            roadMaterial = road;
            buildingMaterial = building;
            parkMaterial = park;
            facilityMaterial = facility;
            spawnMaterial = spawn;
            objectiveMaterial = objective;
            primaryRouteMaterial = primaryRoute;
            alternateRouteMaterial = alternateRoute;
        }

        [ContextMenu("重新生成城市（当前 Seed）")]
        public void Rebuild()
        {
            ClearGenerated();
            layout = CityPcgGenerator.Generate(settings, out report);
            if (layout == null)
            {
                Debug.LogError("城市 PCG 没有生成布局。", this);
                return;
            }

            CreateLayerRoots();
            BuildRoadThemeLayer();
            BuildBuildingThemeLayer();
            BuildConstraintLayer();

            if (!report.valid)
            {
                Debug.LogWarning(
                    "城市 PCG 候选没有通过："
                    + report.failureReason,
                    this);
            }
        }

        [ContextMenu("下一个 Seed")]
        public void NextSeed()
        {
            settings.seed++;
            Rebuild();
        }

        [ContextMenu("上一个 Seed")]
        public void PreviousSeed()
        {
            settings.seed--;
            Rebuild();
        }

        void CreateLayerRoots()
        {
            var generated = new GameObject(GeneratedRootName);
            generated.transform.SetParent(transform, false);

            roadRoot = CreateRoot(
                generated.transform,
                "02_道路主题层_模型可替换");
            buildingRoot = CreateRoot(
                generated.transform,
                "03_建筑主题层_模型可替换");
            markerRoot = CreateRoot(
                generated.transform,
                "04_任务与验证层");

            CreateRoot(
                generated.transform,
                "01_布局数据层_由Scene_Gizmos显示");
        }

        void BuildRoadThemeLayer()
        {
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!layout.IsRoad(cell))
                        continue;

                    float yaw;
                    CityRoadShape shape = layout.GetRoadShape(
                        cell,
                        out yaw);
                    GameObject prefab = GetRoadPrefab(shape);
                    GameObject road = SpawnThemedObject(
                        prefab,
                        PrimitiveType.Cube,
                        roadRoot,
                        "Road_" + shape + "_" + x + "_" + y);
                    road.transform.localPosition = CellToLocal(cell, 0.18f);
                    road.transform.localRotation = Quaternion.Euler(
                        0f,
                        yaw,
                        0f);
                    if (prefab == null)
                    {
                        road.transform.localScale = new Vector3(
                            settings.cellSize * 0.94f,
                            0.35f,
                            settings.cellSize * 0.94f);
                    }
                    AssignMaterial(road, roadMaterial);
                }
            }
        }

        void BuildBuildingThemeLayer()
        {
            int buildingIndex = 0;
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    CityCellKind kind = layout.Cells[x, y];
                    Vector2Int cell = new Vector2Int(x, y);
                    if (kind == CityCellKind.Road)
                        continue;

                    if (kind == CityCellKind.Park)
                    {
                        SpawnPark(cell);
                        continue;
                    }

                    if (kind == CityCellKind.Facility)
                    {
                        SpawnFacility(cell);
                        continue;
                    }

                    GameObject prefab = GetBuildingPrefab(buildingIndex++);
                    float height = Mathf.Max(
                        4f,
                        layout.BuildingHeights[x, y]);
                    GameObject building = SpawnThemedObject(
                        prefab,
                        PrimitiveType.Cube,
                        buildingRoot,
                        "BuildingSlot_" + x + "_" + y);
                    building.transform.localPosition = CellToLocal(
                        cell,
                        height * 0.5f);
                    if (prefab == null)
                    {
                        building.transform.localScale = new Vector3(
                            settings.cellSize * 0.72f,
                            height,
                            settings.cellSize * 0.72f);
                    }
                    AssignMaterial(building, buildingMaterial);
                }
            }
        }

        void BuildConstraintLayer()
        {
            SpawnMarker(
                "PlayerSpawn_出生安全区",
                layout.playerSpawn,
                PrimitiveType.Cylinder,
                spawnMaterial,
                3.2f);
            SpawnMarker(
                "MissionObjective_设施突袭目标",
                layout.objective,
                PrimitiveType.Sphere,
                objectiveMaterial,
                4.1f);

            CreateRouteLine(
                "PrimaryRoute_主路线",
                layout.primaryRoute,
                primaryRouteMaterial,
                2.6f);
            CreateRouteLine(
                "AlternateRoute_备用路线",
                layout.alternateRoute,
                alternateRouteMaterial,
                1.8f);
        }

        void SpawnPark(Vector2Int cell)
        {
            GameObject parkObject = SpawnThemedObject(
                theme.park,
                PrimitiveType.Cylinder,
                buildingRoot,
                "ParkSlot_" + cell.x + "_" + cell.y);
            parkObject.transform.localPosition = CellToLocal(cell, 0.4f);
            if (theme.park == null)
            {
                parkObject.transform.localScale = new Vector3(
                    settings.cellSize * 0.72f,
                    0.8f,
                    settings.cellSize * 0.72f);
            }
            AssignMaterial(parkObject, parkMaterial);
        }

        void SpawnFacility(Vector2Int cell)
        {
            float height = Mathf.Max(
                18f,
                layout.BuildingHeights[cell.x, cell.y]);
            GameObject facilityObject = SpawnThemedObject(
                theme.facility,
                PrimitiveType.Cube,
                buildingRoot,
                "FacilitySlot_设施模型槽");
            facilityObject.transform.localPosition = CellToLocal(
                cell,
                height * 0.5f);
            if (theme.facility == null)
            {
                facilityObject.transform.localScale = new Vector3(
                    settings.cellSize * 0.82f,
                    height,
                    settings.cellSize * 0.82f);
            }
            AssignMaterial(facilityObject, facilityMaterial);
        }

        void SpawnMarker(
            string objectName,
            Vector2Int cell,
            PrimitiveType primitive,
            Material material,
            float height)
        {
            GameObject marker = SpawnThemedObject(
                null,
                primitive,
                markerRoot,
                objectName);
            marker.transform.localPosition = CellToLocal(cell, height * 0.5f + 1f);
            marker.transform.localScale = new Vector3(
                settings.cellSize * 0.42f,
                height,
                settings.cellSize * 0.42f);
            AssignMaterial(marker, material);
        }

        void CreateRouteLine(
            string lineName,
            List<Vector2Int> cells,
            Material material,
            float width)
        {
            if (cells == null || cells.Count < 2)
                return;

            var lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(markerRoot, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = false;
            line.widthMultiplier = width;
            line.positionCount = cells.Count;
            line.sharedMaterial = material;
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            for (int i = 0; i < cells.Count; i++)
                line.SetPosition(i, CellToLocal(cells[i], 2.2f));
        }

        GameObject GetRoadPrefab(CityRoadShape shape)
        {
            switch (shape)
            {
                case CityRoadShape.Straight:
                    return theme.roadStraight;
                case CityRoadShape.Corner:
                    return theme.roadCorner;
                case CityRoadShape.Tee:
                    return theme.roadTee;
                case CityRoadShape.Cross:
                    return theme.roadCross;
                case CityRoadShape.End:
                    return theme.roadEnd;
                default:
                    return theme.roadStraight;
            }
        }

        GameObject GetBuildingPrefab(int index)
        {
            if (theme.buildings == null || theme.buildings.Length == 0)
                return null;
            return theme.buildings[Mathf.Abs(index) % theme.buildings.Length];
        }

        GameObject SpawnThemedObject(
            GameObject prefab,
            PrimitiveType fallback,
            Transform parent,
            string objectName)
        {
            GameObject result;
            if (prefab != null)
            {
                result = Instantiate(prefab, parent);
            }
            else
            {
                result = GameObject.CreatePrimitive(fallback);
                result.transform.SetParent(parent, false);
                Collider collider = result.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying)
                        Destroy(collider);
                    else
                        DestroyImmediate(collider);
                }
            }
            result.name = objectName;
            return result;
        }

        static void AssignMaterial(GameObject target, Material material)
        {
            if (target == null || material == null)
                return;
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = material;
        }

        Vector3 CellToLocal(Vector2Int cell, float y)
        {
            float halfX = (layout.Width - 1) * 0.5f;
            float halfY = (layout.Height - 1) * 0.5f;
            return new Vector3(
                (cell.x - halfX) * settings.cellSize,
                y,
                (cell.y - halfY) * settings.cellSize);
        }

        static Transform CreateRoot(Transform parent, string objectName)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        void ClearGenerated()
        {
            Transform generated = transform.Find(GeneratedRootName);
            if (generated == null)
                return;
            if (Application.isPlaying)
                Destroy(generated.gameObject);
            else
                DestroyImmediate(generated.gameObject);
        }

        void OnGUI()
        {
            if (!Application.isPlaying || !showRuntimePanel)
                return;

            GUILayout.BeginArea(
                new Rect(18f, 18f, 560f, 258f),
                GUI.skin.box);
            GUILayout.Label("城市 PCG 原理实验场（完全不依赖 Dungeon Architect）");
            GUILayout.Label("1 Seed → 2 道路图 → 3 街区 → 4 标记 → 5 模型主题 → 6 规则验证");
            GUILayout.Space(5f);
            GUILayout.Label(LastSummary);
            if (report != null && !report.valid)
                GUILayout.Label("失败原因：" + report.failureReason);
            GUILayout.Label(
                "道路覆盖率："
                + (report != null ? report.roadCoverage.ToString("P1") : "-")
                + "    主路线："
                + (report != null ? report.primaryRouteLength.ToString() : "-")
                + "    备用路线："
                + (report != null ? report.alternateRouteLength.ToString() : "-"));
            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一个 Seed（P）")) PreviousSeed();
            if (GUILayout.Button("重新生成（R）")) Rebuild();
            if (GUILayout.Button("下一个 Seed（N）")) NextSeed();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("显示/隐藏道路骨架（G）"))
                showRoadGraph = !showRoadGraph;
            GUILayout.Label("蓝绿线=主路线，橙线=破坏部分主路线后仍可用的备用路线。");
            GUILayout.Label("Inspector 的 Theme 模型槽可以换成任意道路/建筑预制体，布局算法不变。");
            GUILayout.EndArea();
        }

        void OnDrawGizmos()
        {
            if (!showRoadGraph || layout == null)
                return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.05f, 0.85f, 1f, 0.78f);
            for (int x = 0; x < layout.Width; x++)
            {
                for (int y = 0; y < layout.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!layout.IsRoad(cell))
                        continue;
                    Vector3 point = CellToLocal(cell, 1.1f);
                    if (layout.IsRoad(cell + Vector2Int.right))
                    {
                        Gizmos.DrawLine(
                            point,
                            CellToLocal(cell + Vector2Int.right, 1.1f));
                    }
                    if (layout.IsRoad(cell + Vector2Int.up))
                    {
                        Gizmos.DrawLine(
                            point,
                            CellToLocal(cell + Vector2Int.up, 1.1f));
                    }

#if UNITY_EDITOR
                    if (showMarkerLabels)
                    {
                        float yaw;
                        CityRoadShape shape = layout.GetRoadShape(
                            cell,
                            out yaw);
                        if (shape == CityRoadShape.Cross
                            || shape == CityRoadShape.Tee)
                        {
                            UnityEditor.Handles.Label(
                                transform.TransformPoint(point + Vector3.up * 1.2f),
                                shape.ToString());
                        }
                    }
#endif
                }
            }
        }
    }
}
