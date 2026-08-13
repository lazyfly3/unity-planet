using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityPlanet.CityPcg;

namespace UnityPlanet.CityPcg.Editor
{
    /// <summary>
    /// Audits Dark City 2 and creates project-owned, canonical adapters. The
    /// third-party package is never used as a fixed city layout and its source
    /// prefabs are never modified. Runtime PCG consumes only this semantic
    /// catalog, so replacing the art pack does not replace the city rules.
    /// </summary>
    public static class DarkCity2UrbanLibraryBuilder
    {
        public const string CatalogPath =
            "Assets/CityPcgPrinciplesLab/DarkCity2Derived/DarkCity2UrbanCatalog.asset";

        const string SourceRoot = "Assets/Dark City 2/Assets/Prefabs";
        const string SourceMaterialRoot = "Assets/Dark City 2/Assets/Source";
        const string GeneratedRoot =
            "Assets/CityPcgPrinciplesLab/DarkCity2Derived";
        const string MeshRoot = GeneratedRoot + "/Meshes";
        const string PrefabRoot = GeneratedRoot + "/Prefabs";
        const string MaterialRoot = GeneratedRoot + "/Materials";
        const string BuildingCatalogPath =
            GeneratedRoot + "/DarkCity2BuildingCatalog.asset";

        static readonly Regex BuildingLodPattern = new Regex(
            @"^Building_\d{2}_L1$",
            RegexOptions.CultureInvariant);

        public static void BuildFromMenu()
        {
            DarkCity2UrbanCatalog catalog = BuildLibrary();
            if (catalog != null)
                Selection.activeObject = catalog;
        }

        public static DarkCity2UrbanCatalog BuildLibrary()
        {
            if (!AssetDatabase.IsValidFolder(SourceRoot))
            {
                Debug.LogError("Dark City 2 尚未导入：" + SourceRoot);
                return null;
            }

            EnsureFolder("Assets", "CityPcgPrinciplesLab");
            EnsureFolder("Assets/CityPcgPrinciplesLab", "DarkCity2Derived");
            EnsureFolder(GeneratedRoot, "Meshes");
            EnsureFolder(GeneratedRoot, "Prefabs");
            EnsureFolder(GeneratedRoot, "Materials");

            var low = new List<GameObject>();
            var medium = new List<GameObject>();
            var high = new List<GameObject>();
            var facility = new List<GameObject>();
            var background = new List<GameObject>();
            BuildBuildingLibrary(low, medium, high, facility, background);

            var straightBridges = new List<GameObject>();
            var curvedBridges = new List<GameObject>();
            var supportedOverpasses = new List<GameObject>();
            var bridgeHeads = new List<GameObject>();
            BuildBridgeLibrary(
                straightBridges,
                curvedBridges,
                supportedOverpasses,
                bridgeHeads);

            List<GameObject> districtBuildings = BuildDistrictBuildingLibrary();

            List<GameObject> rooftop = BuildDecorationLibrary(
                DarkCity2AssetCategory.RooftopDecoration,
                new[]
                {
                    "Street Props/Antenna_1.prefab",
                    "Street Props/Antenna_3.prefab",
                    "Street Props/Antenna_5.prefab",
                    "Street Props/Antenna_7.prefab",
                    "Street Props/Antenna_8.prefab",
                    "Street Props/Vents1.prefab",
                    "Street Props/Vents3.prefab",
                    "Street Props/Vents4.prefab",
                    "Street Props/Solar Panel1.prefab",
                    "Street Props/Solar Panel2.prefab",
                    "Street Props/Generator1.prefab",
                    "Street Props/Radiator1.prefab",
                    "Prefabs buildings/Building_RoofConstr_01.prefab",
                    "Prefabs buildings/PipelineFrame_01_A.prefab",
                    "Prefabs buildings/Pipe_group1.prefab"
                });
            List<GameObject> facade = BuildDecorationLibrary(
                DarkCity2AssetCategory.FacadeDecoration,
                new[]
                {
                    "Billboards/Billboard_01.prefab",
                    "Billboards/Billboard_03.prefab",
                    "Billboards/Billboard_06.prefab",
                    "Billboards/Billboard_08a.prefab",
                    "Billboards/Billboard_10A.prefab",
                    "Billboards/Billboard_11.prefab",
                    "Billboards/Banner_Moving_01.prefab",
                    "Billboards/Banner_Moving_02.prefab",
                    "Prefabs buildings/Neon_01.prefab",
                    "Prefabs buildings/Neon_04.prefab",
                    "Prefabs buildings/Neon_07.prefab",
                    "Prefabs buildings/Neon_10.prefab",
                    "Prefabs buildings/Neon_13.prefab",
                    "Prefabs buildings/Neon_Window_A.prefab",
                    "Prefabs buildings/Building_Fasade_prop1.prefab",
                    "Prefabs buildings/Building_Fasade_prop2.prefab",
                    "Prefabs buildings/Building_Fasade_prop3.prefab",
                    "Prefabs buildings/FireEscape_01.prefab",
                    "Prefabs buildings/Wall_Balcony_01.prefab",
                    "Prefabs buildings/Wall_Balcony_02.prefab",
                    "Prefabs buildings/Wall_Pipe_01A.prefab",
                    "Prefabs buildings/Wall_Pipe_02A.prefab",
                    "Prefabs buildings/Wall_Pipe_03A.prefab",
                    "Prefabs buildings/Wall_Pipe_Valve_01.prefab",
                    "Prefabs buildings/Cables_01.prefab",
                    "Prefabs buildings/Cables_04.prefab",
                    "Prefabs buildings/Cables_07.prefab",
                    "Prefabs buildings/Shop_01.prefab",
                    "Prefabs buildings/Shop_02.prefab",
                    "Prefabs buildings/Shop_03.prefab",
                    "Prefabs buildings/Shop_04.prefab",
                    "Prefabs buildings/Shop_05.prefab"
                });
            List<GameObject> street = BuildDecorationLibrary(
                DarkCity2AssetCategory.StreetDecoration,
                new[]
                {
                    "Street Lights/Street_Lamp1.prefab",
                    "Street Lights/Street_Lamp2.prefab",
                    "Street Lights/Street_Lamp3.prefab",
                    "Street Lights/Street_Lamp4.prefab",
                    "Street Lights/Street_Lamp_Traffic_01.prefab",
                    "Street Lights/StreetLight_Camera1.prefab",
                    "Street Lights/Traffic_light1.prefab",
                    "Street Props/Street_Server.prefab",
                    "Street Props/Terminal_01.prefab",
                    "Street Props/Terminal_02.prefab",
                    "Street Props/Street_Barrier1.prefab",
                    "Street Props/Street_Barrier3.prefab",
                    "Street Props/Street_Barrier7_Corner.prefab",
                    "Street Props/Bus_stop.prefab",
                    "Street Props/Toll_Booth_01.prefab",
                    "Street Props/SectorSign_01.prefab",
                    "Street Props/SectorSign_07.prefab",
                    "Street Props/Container.prefab",
                    "Street Props/Generator1.prefab",
                    "Street Props/Scaffolding_EL1.prefab",
                    "Street Props/Street_Frame1.prefab",
                    "Street Props/Pylon_Big.prefab",
                    "Warnings/Warning (03).prefab",
                    "Warnings/Warning (11).prefab",
                    "Warnings/Warning (24).prefab"
                });

            NewGenUrbanBuildingCatalog buildingCatalog =
                LoadOrCreate<NewGenUrbanBuildingCatalog>(BuildingCatalogPath);
            buildingCatalog.low = low.ToArray();
            buildingCatalog.medium = medium.ToArray();
            buildingCatalog.high = high.ToArray();
            buildingCatalog.facility = facility.ToArray();
            EditorUtility.SetDirty(buildingCatalog);

            DarkCity2UrbanCatalog catalog =
                LoadOrCreate<DarkCity2UrbanCatalog>(CatalogPath);
            catalog.buildings = buildingCatalog;
            catalog.backgroundBuildings = background.ToArray();
            catalog.straightSkybridges = straightBridges.ToArray();
            catalog.curvedSkybridges = curvedBridges.ToArray();
            catalog.supportedOverpasses = supportedOverpasses.ToArray();
            catalog.bridgeHeads = bridgeHeads.ToArray();
            catalog.districtBuildings = districtBuildings.ToArray();
            catalog.rooftopDecorations = rooftop.ToArray();
            catalog.facadeDecorations = facade.ToArray();
            catalog.streetDecorations = street.ToArray();
            catalog.asphalt = LoadMaterial(
                "Streets/Models/Materials/Street_el1_LP_a.mat");
            catalog.sidewalk = LoadMaterial(
                "Streets/Models/Materials/Street_pavement1_a.mat");
            catalog.cityPaving = LoadMaterial(
                "Materials/Rough/Concrete_Panels8_a_tiled.mat");
            catalog.roadLines = LoadMaterial(
                "Streets/Models/Materials/Road_Lines_a.mat");
            catalog.bridgeSleeve = LoadMaterial(
                "Materials/Metallic/Overpass_a.mat");
            catalog.cableRed = CreateOrUpdateCableMaterial(
                "Cable_Red_Emissive",
                new Color(0.92f, 0.035f, 0.025f, 1f),
                new Color(1.8f, 0.035f, 0.02f, 1f));
            catalog.cableDark = CreateOrUpdateCableMaterial(
                "Cable_Dark_Rubber",
                new Color(0.018f, 0.022f, 0.027f, 1f),
                Color.black);
            catalog.cableBlue = CreateOrUpdateCableMaterial(
                "Cable_Blue_Emissive",
                new Color(0.02f, 0.22f, 0.72f, 1f),
                new Color(0.02f, 0.45f, 2.1f, 1f));
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Dark City 2 规则素材目录完成：低层 " + low.Count +
                "，中层 " + medium.Count +
                "，高层 " + high.Count +
                "，设施 " + facility.Count +
                "，背景 " + background.Count +
                "，直连廊 " + straightBridges.Count +
                "，转角连廊 " + curvedBridges.Count +
                "，桥头 " + bridgeHeads.Count +
                "，分区建筑 " + districtBuildings.Count +
                "。所有派生资源统一为 +Y 向上、建筑正面 +Z、连廊跨度 +Z。原包未改写。");
            return catalog;
        }

        static void BuildBuildingLibrary(
            List<GameObject> low,
            List<GameObject> medium,
            List<GameObject> high,
            List<GameObject> facility,
            List<GameObject> background)
        {
            string buildingRoot = SourceRoot + "/Prefabs buildings";
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { buildingRoot });
            Array.Sort(guids, StringComparer.Ordinal);
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!BuildingLodPattern.IsMatch(name))
                    continue;
                SourceAudit audit = AuditSource(path, 0f);
                if (!audit.valid || audit.triangleCount > 50000)
                    continue;

                DarkCity2AssetCategory category = audit.bounds.size.y <= 15f
                    ? DarkCity2AssetCategory.LowBuilding
                    : DarkCity2AssetCategory.MediumBuilding;
                GameObject prefab = BuildCanonicalPrefab(
                    path,
                    "DC2_" + name,
                    category,
                    0f,
                    true,
                    true,
                    DarkCity2StretchAxis.None,
                    Vector2.one);
                if (prefab == null)
                    continue;
                if (category == DarkCity2AssetCategory.LowBuilding)
                    low.Add(prefab);
                else
                    medium.Add(prefab);
            }

            for (int number = 1; number <= 5; number++)
            {
                string name = "Skyscraper_" + number.ToString("00");
                string path = SourceRoot + "/Background/" + name + ".prefab";
                GameObject prefab = BuildCanonicalPrefab(
                    path,
                    "DC2_" + name,
                    number == 5
                        ? DarkCity2AssetCategory.HighBuilding
                        : DarkCity2AssetCategory.MediumBuilding,
                    0f,
                    true,
                    true,
                    DarkCity2StretchAxis.None,
                    Vector2.one);
                if (prefab == null)
                    continue;
                if (number <= 4)
                    medium.Add(prefab);
                high.Add(prefab);
            }

            // Facilities use broad, readable silhouettes selected from the
            // already audited pools; no additional source orientation exists.
            if (medium.Count > 0)
                facility.Add(medium[Mathf.Min(1, medium.Count - 1)]);
            if (high.Count > 0)
                facility.Add(high[0]);
            if (high.Count > 2)
                facility.Add(high[2]);

            for (int number = 1; number <= 8; number++)
            {
                string name = "Skyscraper_distance_" + number;
                string path = SourceRoot + "/Background/" + name + ".prefab";
                GameObject prefab = BuildCanonicalPrefab(
                    path,
                    "DC2_" + name,
                    DarkCity2AssetCategory.BackgroundBuilding,
                    0f,
                    true,
                    false,
                    DarkCity2StretchAxis.None,
                    Vector2.one);
                if (prefab != null)
                    background.Add(prefab);
            }
        }

        static void BuildBridgeLibrary(
            List<GameObject> straight,
            List<GameObject> curved,
            List<GameObject> supported,
            List<GameObject> heads)
        {
            AddBridge(straight, "Overpass_03_A", 0f,
                DarkCity2AssetCategory.StraightSkybridge, new Vector2(0.72f, 1.78f));
            AddBridge(straight, "Footbridge_01", -90f,
                DarkCity2AssetCategory.StraightSkybridge, new Vector2(0.82f, 1.7f));
            AddBridge(straight, "Footbridge_02", -90f,
                DarkCity2AssetCategory.StraightSkybridge, new Vector2(0.82f, 1.7f));
            AddBridge(curved, "Overpass_03_B", 0f,
                DarkCity2AssetCategory.CurvedSkybridge, Vector2.one);
            AddBridge(supported, "Overpass_01", 0f,
                DarkCity2AssetCategory.SupportedOverpass, new Vector2(0.82f, 1.25f));
            AddBridge(supported, "Overpass_02", 0f,
                DarkCity2AssetCategory.SupportedOverpass, new Vector2(0.82f, 1.25f));
            AddBridge(heads, "Footbridge_03A", 0f,
                DarkCity2AssetCategory.BridgeHead, Vector2.one);
            AddBridge(heads, "Footbridge_03B", 0f,
                DarkCity2AssetCategory.BridgeHead, Vector2.one);
            AddBridge(heads, "Footbridge_04A", 0f,
                DarkCity2AssetCategory.BridgeHead, Vector2.one);
            AddBridge(heads, "Footbridge_04B", 0f,
                DarkCity2AssetCategory.BridgeHead, Vector2.one);
        }

        static void AddBridge(
            List<GameObject> target,
            string sourceName,
            float correctionYaw,
            DarkCity2AssetCategory category,
            Vector2 stretch)
        {
            string path = SourceRoot + "/Prefabs buildings/" +
                          sourceName + ".prefab";
            GameObject prefab = BuildCanonicalPrefab(
                path,
                "DC2_" + sourceName,
                category,
                correctionYaw,
                true,
                true,
                category == DarkCity2AssetCategory.BridgeHead
                    ? DarkCity2StretchAxis.None
                    : DarkCity2StretchAxis.Z,
                stretch);
            if (prefab != null)
                target.Add(prefab);
        }

        static List<GameObject> BuildDecorationLibrary(
            DarkCity2AssetCategory category,
            string[] relativePaths)
        {
            var result = new List<GameObject>();
            for (int index = 0; index < relativePaths.Length; index++)
            {
                string path = SourceRoot + "/" + relativePaths[index];
                string name = Path.GetFileNameWithoutExtension(path);
                GameObject prefab = BuildCanonicalPrefab(
                    path,
                    "DC2_" + category + "_" + name,
                    category,
                    0f,
                    true,
                    false,
                    DarkCity2StretchAxis.None,
                    Vector2.one);
                if (prefab != null)
                    result.Add(prefab);
            }
            return result;
        }

        static List<GameObject> BuildDistrictBuildingLibrary()
        {
            var result = new List<GameObject>();
            AddDistrictBuilding(
                result,
                "Warehouse_01",
                "IndustrialWarehouse",
                DarkCity2AssetCategory.LowBuilding);
            AddDistrictBuilding(
                result,
                "Warehouse_03",
                "IndustrialWarehouse",
                DarkCity2AssetCategory.LowBuilding);
            AddDistrictBuilding(
                result,
                "Silo_01",
                "IndustrialSilo",
                DarkCity2AssetCategory.FacilityBuilding);
            AddDistrictBuilding(
                result,
                "Silo_02",
                "IndustrialSilo",
                DarkCity2AssetCategory.FacilityBuilding);
            AddDistrictBuilding(
                result,
                "Garage_01",
                "TransitGarage",
                DarkCity2AssetCategory.LowBuilding);
            return result;
        }

        static void AddDistrictBuilding(
            List<GameObject> result,
            string sourceName,
            string semanticName,
            DarkCity2AssetCategory category)
        {
            string path = SourceRoot + "/Prefabs buildings/" + sourceName + ".prefab";
            GameObject prefab = BuildCanonicalPrefab(
                path,
                "DC2_" + semanticName + "_" + sourceName,
                category,
                0f,
                true,
                true,
                DarkCity2StretchAxis.None,
                Vector2.one);
            if (prefab != null)
                result.Add(prefab);
        }

        static GameObject BuildCanonicalPrefab(
            string sourcePath,
            string outputName,
            DarkCity2AssetCategory category,
            float correctionYaw,
            bool combineMeshes,
            bool addCollider,
            DarkCity2StretchAxis stretchAxis,
            Vector2 stretchRange)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                Debug.LogWarning("Dark City 2 分类源缺失：" + sourcePath);
                return null;
            }

            var root = new GameObject(outputName + "_UpY_FrontOrSpanZ_RightX");
            var axis = new GameObject("AxisCorrection_SourcePreserved");
            axis.transform.SetParent(root.transform, false);
            axis.transform.localRotation = Quaternion.Euler(0f, correctionYaw, 0f);
            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                return null;
            }
            instance.name = "AuditedSource_" + source.name;
            instance.transform.SetParent(axis.transform, false);

            SourceAudit initial = AuditHierarchy(root, sourcePath);
            if (!initial.valid)
            {
                UnityEngine.Object.DestroyImmediate(root);
                return null;
            }
            axis.transform.localPosition = new Vector3(
                -initial.bounds.center.x,
                -initial.bounds.min.y,
                -initial.bounds.center.z);
            SourceAudit normalized = AuditHierarchy(root, sourcePath);

            RemoveColliders(root);
            if (combineMeshes)
                CombineHierarchy(root, outputName);

            Vector3 size = normalized.bounds.size;
            if (IsBuildingCategory(category))
            {
                NormalizedBuildingModelInfo info =
                    root.AddComponent<NormalizedBuildingModelInfo>();
                info.Configure(
                    size,
                    "Dark City 2 自动分类派生；底面 Y=0，顶部 +Y，街道正面 +Z，右侧 +X；源资源未修改。");
            }

            DarkCity2ConnectionSocket[] sockets =
                IsSpanningBridge(category)
                    ? CreateBridgeSockets(size)
                    : Array.Empty<DarkCity2ConnectionSocket>();
            DarkCity2AssetDescriptor descriptor =
                root.AddComponent<DarkCity2AssetDescriptor>();
            descriptor.Configure(
                category,
                size,
                stretchAxis,
                stretchRange,
                sourcePath,
                sockets,
                ResolvePlacementRole(category, outputName));

            if (addCollider)
            {
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, size.y * 0.5f, 0f);
                if (IsSpanningBridge(category))
                {
                    collider.size = new Vector3(
                        Mathf.Max(1.2f, size.x * 0.84f),
                        Mathf.Max(1f, size.y * 0.68f),
                        Mathf.Max(2f, size.z * 0.94f));
                }
                else
                {
                    collider.size = size;
                }
            }

            string prefabPath = PrefabRoot + "/" + outputName + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static void CombineHierarchy(GameObject root, string outputName)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            var groups = new Dictionary<Material, List<CombineInstance>>();
            for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
            {
                MeshFilter filter = filters[filterIndex];
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (mesh == null || renderer == null || !renderer.enabled)
                    continue;
                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 matrix = root.transform.worldToLocalMatrix *
                                   filter.transform.localToWorldMatrix;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    Material material = materials.Length == 0
                        ? null
                        : materials[Mathf.Min(subMesh, materials.Length - 1)];
                    if (material == null)
                        continue;
                    if (!groups.TryGetValue(material, out List<CombineInstance> list))
                    {
                        list = new List<CombineInstance>();
                        groups.Add(material, list);
                    }
                    list.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = subMesh,
                        transform = matrix
                    });
                }
            }

            Transform source = root.transform.Find("AxisCorrection_SourcePreserved");
            if (source != null)
                UnityEngine.Object.DestroyImmediate(source.gameObject);

            int materialIndex = 0;
            foreach (KeyValuePair<Material, List<CombineInstance>> pair in groups)
            {
                if (pair.Value.Count == 0)
                    continue;
                var mesh = new Mesh
                {
                    name = outputName + "_Combined_" + materialIndex,
                    indexFormat = IndexFormat.UInt32
                };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                string meshPath = MeshRoot + "/" + outputName + "_" +
                                  materialIndex.ToString("D2") + ".asset";
                Mesh saved = SaveOrReplaceMesh(mesh, meshPath);
                var visual = new GameObject(
                    "Visual_" + materialIndex.ToString("D2") + "_" + pair.Key.name);
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = saved;
                visual.AddComponent<MeshRenderer>().sharedMaterial = pair.Key;
                materialIndex++;
            }
        }

        static SourceAudit AuditSource(string path, float correctionYaw)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
                return default;
            var root = new GameObject("AuditRoot");
            var axis = new GameObject("Axis");
            axis.transform.SetParent(root.transform, false);
            axis.transform.localRotation = Quaternion.Euler(0f, correctionYaw, 0f);
            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            instance.transform.SetParent(axis.transform, false);
            SourceAudit audit = AuditHierarchy(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return audit;
        }

        static SourceAudit AuditHierarchy(GameObject root, string path)
        {
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            Bounds bounds = new Bounds();
            bool initialized = false;
            int triangles = 0;
            for (int index = 0; index < filters.Length; index++)
            {
                Mesh mesh = filters[index].sharedMesh;
                if (mesh == null)
                    continue;
                triangles += CountTriangles(mesh);
                Matrix4x4 matrix = root.transform.worldToLocalMatrix *
                                   filters[index].transform.localToWorldMatrix;
                EncapsulateTransformedBounds(
                    mesh.bounds,
                    matrix,
                    ref bounds,
                    ref initialized);
            }
            return new SourceAudit
            {
                valid = initialized,
                path = path,
                bounds = bounds,
                meshCount = filters.Length,
                triangleCount = triangles
            };
        }

        static int CountTriangles(Mesh mesh)
        {
            int triangles = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                    triangles += (int)mesh.GetIndexCount(subMesh) / 3;
            }
            return triangles;
        }

        static void EncapsulateTransformedBounds(
            Bounds source,
            Matrix4x4 matrix,
            ref Bounds result,
            ref bool initialized)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3(
                    (corner & 1) == 0 ? source.min.x : source.max.x,
                    (corner & 2) == 0 ? source.min.y : source.max.y,
                    (corner & 4) == 0 ? source.min.z : source.max.z);
                point = matrix.MultiplyPoint3x4(point);
                if (!initialized)
                {
                    result = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(point);
                }
            }
        }

        static DarkCity2ConnectionSocket[] CreateBridgeSockets(Vector3 size)
        {
            Vector2 opening = new Vector2(
                Mathf.Max(1f, size.x * 0.78f),
                Mathf.Max(1f, size.y * 0.62f));
            return new[]
            {
                new DarkCity2ConnectionSocket
                {
                    name = "Socket_A_-Z",
                    localPosition = new Vector3(0f, size.y * 0.5f, -size.z * 0.5f),
                    localForward = Vector3.back,
                    openingSize = opening
                },
                new DarkCity2ConnectionSocket
                {
                    name = "Socket_B_+Z",
                    localPosition = new Vector3(0f, size.y * 0.5f, size.z * 0.5f),
                    localForward = Vector3.forward,
                    openingSize = opening
                }
            };
        }

        static bool IsBuildingCategory(DarkCity2AssetCategory category)
        {
            return category == DarkCity2AssetCategory.LowBuilding ||
                   category == DarkCity2AssetCategory.MediumBuilding ||
                   category == DarkCity2AssetCategory.HighBuilding ||
                   category == DarkCity2AssetCategory.FacilityBuilding ||
                   category == DarkCity2AssetCategory.BackgroundBuilding;
        }

        static bool IsSpanningBridge(DarkCity2AssetCategory category)
        {
            return category == DarkCity2AssetCategory.StraightSkybridge ||
                   category == DarkCity2AssetCategory.CurvedSkybridge ||
                   category == DarkCity2AssetCategory.SupportedOverpass;
        }

        static DarkCity2PlacementRole ResolvePlacementRole(
            DarkCity2AssetCategory category,
            string name)
        {
            if (name.IndexOf("IndustrialWarehouse", StringComparison.OrdinalIgnoreCase) >= 0)
                return DarkCity2PlacementRole.IndustrialWarehouse;
            if (name.IndexOf("IndustrialSilo", StringComparison.OrdinalIgnoreCase) >= 0)
                return DarkCity2PlacementRole.IndustrialSilo;
            if (name.IndexOf("TransitGarage", StringComparison.OrdinalIgnoreCase) >= 0)
                return DarkCity2PlacementRole.TransitGarage;
            switch (category)
            {
                case DarkCity2AssetCategory.LowBuilding:
                    return DarkCity2PlacementRole.LowBlock;
                case DarkCity2AssetCategory.MediumBuilding:
                    return DarkCity2PlacementRole.MidBlock;
                case DarkCity2AssetCategory.HighBuilding:
                    return name.EndsWith("05", StringComparison.Ordinal)
                        ? DarkCity2PlacementRole.Landmark
                        : DarkCity2PlacementRole.Tower;
                case DarkCity2AssetCategory.FacilityBuilding:
                    return DarkCity2PlacementRole.Facility;
                case DarkCity2AssetCategory.BackgroundBuilding:
                    return DarkCity2PlacementRole.DistantSkyline;
                case DarkCity2AssetCategory.StraightSkybridge:
                    return DarkCity2PlacementRole.BridgeStraight;
                case DarkCity2AssetCategory.CurvedSkybridge:
                    return DarkCity2PlacementRole.BridgeCorner;
                case DarkCity2AssetCategory.SupportedOverpass:
                    return DarkCity2PlacementRole.BridgeSupported;
                case DarkCity2AssetCategory.BridgeHead:
                    return name.EndsWith("B", StringComparison.Ordinal)
                        ? DarkCity2PlacementRole.BridgeHeadRight
                        : DarkCity2PlacementRole.BridgeHeadLeft;
                case DarkCity2AssetCategory.RooftopDecoration:
                    if (name.IndexOf("Antenna", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.RoofAntenna;
                    if (name.IndexOf("Solar", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.RoofEnergy;
                    if (name.IndexOf("Generator", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.RoofGenerator;
                    if (name.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("RoofConstr", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.RoofPipeFrame;
                    return DarkCity2PlacementRole.RoofMechanical;
                case DarkCity2AssetCategory.FacadeDecoration:
                    if (name.IndexOf("Neon", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.FacadeNeon;
                    if (name.IndexOf("Billboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Banner", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.FacadeBillboard;
                    if (name.IndexOf("FireEscape", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.FacadeFireEscape;
                    if (name.IndexOf("Balcony", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.FacadeBalcony;
                    if (name.IndexOf("Wall_Pipe", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.FacadePipe;
                    if (name.IndexOf("Cables", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.FacadeCable;
                    if (name.IndexOf("Shop", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.FacadeShop;
                    return DarkCity2PlacementRole.FacadeMechanical;
                case DarkCity2AssetCategory.StreetDecoration:
                    if (name.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Traffic_light", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.StreetLight;
                    if (name.IndexOf("Barrier", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.StreetBarrier;
                    if (name.IndexOf("Bus_stop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Toll_Booth", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.StreetTransit;
                    if (name.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("SectorSign", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.StreetWarning;
                    if (name.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Generator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Scaffolding", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Street_Frame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Pylon", StringComparison.OrdinalIgnoreCase) >= 0)
                        return DarkCity2PlacementRole.StreetIndustrial;
                    return DarkCity2PlacementRole.StreetUtility;
                default:
                    return DarkCity2PlacementRole.None;
            }
        }

        static void RemoveColliders(GameObject root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
                UnityEngine.Object.DestroyImmediate(colliders[index]);
        }

        static Mesh SaveOrReplaceMesh(Mesh mesh, string path)
        {
            mesh.name = Path.GetFileNameWithoutExtension(path);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Material LoadMaterial(string relativePath)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(
                SourceMaterialRoot + "/" + relativePath);
        }

        static Material CreateOrUpdateCableMaterial(
            string materialName,
            Color baseColor,
            Color emissionColor)
        {
            string path = MaterialRoot + "/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Color") ??
                            Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", baseColor);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emissionColor);
                if (emissionColor.maxColorComponent > 0.001f)
                    material.EnableKeyword("_EMISSION");
                else
                    material.DisableKeyword("_EMISSION");
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        struct SourceAudit
        {
            public bool valid;
            public string path;
            public Bounds bounds;
            public int meshCount;
            public int triangleCount;
        }
    }
}
