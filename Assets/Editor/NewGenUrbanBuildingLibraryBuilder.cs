using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityPlanet.CityPcg;

namespace UnityPlanet.CityPcg.Editor
{
    /// <summary>
    /// 把 NewGen Urban 的模块化墙面组合成项目自有的标准化楼房库。
    /// 不修改第三方 FBX、材质或预制体，只在实验场目录内生成适配资产。
    /// </summary>
    public static class NewGenUrbanBuildingLibraryBuilder
    {
        public const string CatalogPath =
            "Assets/CityPcgPrinciplesLab/NewGenUrbanNormalized/NewGenUrbanBuildingCatalog.asset";

        const string GeneratedRoot =
            "Assets/CityPcgPrinciplesLab/NewGenUrbanNormalized";
        const string MeshRoot = GeneratedRoot + "/Meshes";
        const string PrefabRoot = GeneratedRoot + "/Prefabs";
        const string ExistingParts =
            "Assets/ThirdParty/CombatMap/NewGenUrban/Prefabs/Building Parts/";
        const string ImportedParts =
            "Assets/Reversed Interactive/New Gen Urban/Prefabs/Building Parts/";

        static readonly string[] BottomSources =
        {
            ExistingParts + "Build Bottom 1 (1).prefab",
            ExistingParts + "BuildBottom2 (12).prefab",
            ExistingParts + "BuildBottom3.prefab",
            ExistingParts + "BuildBottom4 (12).prefab"
        };

        // BuildTop4/8 是极简平面版本，不放入近距离模型池。
        static readonly string[] FacadeSources =
        {
            ExistingParts + "BuildTop1 2X2.prefab",
            ImportedParts + "BuildTop2 2X2.prefab",
            ExistingParts + "BuildTop3 2X2.prefab",
            ImportedParts + "BuildTop5 2X2.prefab",
            ExistingParts + "BuildTop6 2X2.prefab",
            ImportedParts + "BuildTop7 2X2.prefab"
        };

        static readonly string[] RoofSources =
        {
            ImportedParts + "Roof1.prefab",
            ImportedParts + "Roof2.prefab",
            ImportedParts + "Roof3.prefab",
            ImportedParts + "Roof4.prefab"
        };

        [MenuItem("Tools/城市 PCG/重建 NewGen Urban 标准化楼房库")]
        public static void BuildFromMenu()
        {
            NewGenUrbanBuildingCatalog catalog = BuildLibrary();
            if (catalog != null)
                Selection.activeObject = catalog;
        }

        public static NewGenUrbanBuildingCatalog BuildLibrary()
        {
            if (!ValidateSources())
                return null;

            EnsureFolder("Assets", "CityPcgPrinciplesLab");
            EnsureFolder("Assets/CityPcgPrinciplesLab", "NewGenUrbanNormalized");
            EnsureFolder(GeneratedRoot, "Meshes");
            EnsureFolder(GeneratedRoot, "Prefabs");

            VariantSpec[] specs = CreateVariantSpecs();
            var low = new List<GameObject>();
            var medium = new List<GameObject>();
            var high = new List<GameObject>();
            var facility = new List<GameObject>();

            for (int i = 0; i < specs.Length; i++)
            {
                GameObject prefab = BuildVariantPrefab(specs[i]);
                if (prefab == null)
                    continue;
                switch (specs[i].band)
                {
                    case AirCombatBuildingBand.Medium:
                        medium.Add(prefab);
                        break;
                    case AirCombatBuildingBand.High:
                        high.Add(prefab);
                        break;
                    case AirCombatBuildingBand.Facility:
                        facility.Add(prefab);
                        break;
                    default:
                        low.Add(prefab);
                        break;
                }
            }

            NewGenUrbanBuildingCatalog catalog =
                AssetDatabase.LoadAssetAtPath<NewGenUrbanBuildingCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<NewGenUrbanBuildingCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.low = low.ToArray();
            catalog.medium = medium.ToArray();
            catalog.high = high.ToArray();
            catalog.facility = facility.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "NewGen Urban 标准化楼房库已生成：低层 " + low.Count +
                "，中层 " + medium.Count + "，高层 " + high.Count +
                "，设施 " + facility.Count +
                "。统一方向：底面Y0 / 顶部+Y / 正面+Z / 右侧+X。");
            return catalog;
        }

        static VariantSpec[] CreateVariantSpecs()
        {
            return new[]
            {
                V("L01_CornerShop", AirCombatBuildingBand.Low, 34f, 32f, 28f, 0, 0, 3, 0),
                V("L02_WideBlock", AirCombatBuildingBand.Low, 46f, 42f, 28f, 1, 1, 4, 1),
                V("L03_SquareBlock", AirCombatBuildingBand.Low, 31f, 50f, 31f, 2, 2, 5, 2),
                V("L04_LongWorkshop", AirCombatBuildingBand.Low, 52f, 38f, 25f, 3, 3, 0, 3),
                V("L05_RaisedBlock", AirCombatBuildingBand.Low, 38f, 58f, 34f, 0, 4, 1, 1),

                V("M01_OfficeSlab", AirCombatBuildingBand.Medium, 38f, 78f, 31f, 1, 0, 2, 0),
                V("M02_WideResidential", AirCombatBuildingBand.Medium, 50f, 88f, 29f, 2, 1, 3, 1),
                V("M03_DeepAtrium", AirCombatBuildingBand.Medium, 33f, 98f, 44f, 3, 2, 4, 2),
                V("M04_TwinFacade", AirCombatBuildingBand.Medium, 47f, 108f, 36f, 0, 3, 5, 3),
                V("M05_SquareMidrise", AirCombatBuildingBand.Medium, 35f, 116f, 35f, 1, 4, 0, 0),
                V("M06_LongSlab", AirCombatBuildingBand.Medium, 58f, 84f, 27f, 2, 5, 1, 1),
                V("M07_CompactMidrise", AirCombatBuildingBand.Medium, 31f, 124f, 33f, 3, 0, 4, 2),

                V("H01_CombatTower", AirCombatBuildingBand.High, 35f, 146f, 35f, 0, 0, 3, 0),
                V("H02_OffsetTower", AirCombatBuildingBand.High, 44f, 158f, 31f, 1, 1, 4, 1),
                V("H03_DeepTower", AirCombatBuildingBand.High, 31f, 172f, 46f, 2, 2, 5, 2),
                V("H04_SkylineSquare", AirCombatBuildingBand.High, 40f, 186f, 40f, 3, 3, 0, 3),
                V("H05_WideLandmark", AirCombatBuildingBand.High, 54f, 151f, 33f, 0, 4, 1, 1),
                V("H06_Needle", AirCombatBuildingBand.High, 30f, 196f, 31f, 1, 5, 2, 2),
                V("H07_CrosswindTower", AirCombatBuildingBand.High, 47f, 181f, 35f, 2, 0, 5, 0),

                V("F01_AssaultCore", AirCombatBuildingBand.Facility, 52f, 82f, 52f, 3, 1, 4, 3),
                V("F02_CommandCore", AirCombatBuildingBand.Facility, 48f, 98f, 48f, 0, 3, 0, 2),
                V("F03_ReactorCore", AirCombatBuildingBand.Facility, 56f, 112f, 50f, 2, 5, 2, 1)
            };
        }

        static VariantSpec V(
            string name,
            AirCombatBuildingBand band,
            float width,
            float height,
            float depth,
            int bottom,
            int facade,
            int accent,
            int roof)
        {
            return new VariantSpec
            {
                name = name,
                band = band,
                size = new Vector3(width, height, depth),
                bottom = bottom,
                facade = facade,
                accent = accent,
                roof = roof
            };
        }

        static GameObject BuildVariantPrefab(VariantSpec spec)
        {
            var root = new GameObject(
                "NGU_" + spec.name + "_BottomY0_Top+Y_Front+Z_Right+X");
            NormalizedBuildingModelInfo info =
                root.AddComponent<NormalizedBuildingModelInfo>();
            info.Configure(
                spec.size,
                "NewGen Urban 模块组合；正面法线经审计为本地 +Z");

            var groups = new Dictionary<Material, List<CombineInstance>>();
            float roofHeight = 1.25f;
            float wallHeight = spec.size.y - roofHeight;
            float groundCourseHeight = Mathf.Min(7.2f, wallHeight * 0.22f);

            AddWallCourse(
                BottomSources[spec.bottom % BottomSources.Length],
                spec.size,
                0f,
                groundCourseHeight,
                MeshChoice.All,
                groups);

            float upperHeight = Mathf.Max(1f, wallHeight - groundCourseHeight);
            int rows = Mathf.Clamp(Mathf.CeilToInt(upperHeight / 11.5f), 1, 18);
            float courseHeight = upperHeight / rows;
            for (int row = 0; row < rows; row++)
            {
                int style = row > 0 && row % 4 == 3
                    ? spec.accent
                    : spec.facade;
                AddWallCourse(
                    FacadeSources[style % FacadeSources.Length],
                    spec.size,
                    groundCourseHeight + row * courseHeight,
                    courseHeight,
                    MeshChoice.LowestDetail,
                    groups);
            }

            AddRoof(
                RoofSources[spec.roof % RoofSources.Length],
                spec.size,
                wallHeight,
                roofHeight,
                groups);

            int materialIndex = 0;
            foreach (KeyValuePair<Material, List<CombineInstance>> pair in groups)
            {
                if (pair.Key == null || pair.Value.Count == 0)
                    continue;
                var mesh = new Mesh
                {
                    name = spec.name + "_Surface_" + materialIndex,
                    indexFormat = IndexFormat.UInt32
                };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                string meshPath = MeshRoot + "/" + spec.name + "_" +
                                  materialIndex.ToString("D2") + ".asset";
                Mesh savedMesh = SaveOrReplaceMesh(mesh, meshPath);

                var visual = new GameObject(
                    "Visual_" + materialIndex.ToString("D2") + "_" + pair.Key.name);
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = savedMesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = pair.Key;
                materialIndex++;
            }

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, spec.size.y * 0.5f, 0f);
            collider.size = spec.size;

            string prefabPath = PrefabRoot + "/" + spec.name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static void AddWallCourse(
            string sourcePath,
            Vector3 buildingSize,
            float bottom,
            float height,
            MeshChoice choice,
            Dictionary<Material, List<CombineInstance>> groups)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Bounds bounds = CalculateSourceBounds(source, choice);
            AddWallSide(source, bounds, buildingSize.x, buildingSize.z * 0.5f,
                bottom, height, 0f, choice, groups);
            AddWallSide(source, bounds, buildingSize.x, buildingSize.z * 0.5f,
                bottom, height, 180f, choice, groups);
            AddWallSide(source, bounds, buildingSize.z, buildingSize.x * 0.5f,
                bottom, height, 90f, choice, groups);
            AddWallSide(source, bounds, buildingSize.z, buildingSize.x * 0.5f,
                bottom, height, -90f, choice, groups);
        }

        static void AddWallSide(
            GameObject source,
            Bounds sourceBounds,
            float wallLength,
            float outwardDistance,
            float bottom,
            float height,
            float yaw,
            MeshChoice choice,
            Dictionary<Material, List<CombineInstance>> groups)
        {
            int columns = Mathf.Clamp(Mathf.CeilToInt(wallLength / 28f), 1, 3);
            float cellWidth = wallLength / columns;
            float scaleX = cellWidth / Mathf.Max(0.01f, sourceBounds.size.x);
            float scaleY = height / Mathf.Max(0.01f, sourceBounds.size.y);
            Matrix4x4 wallFrame = Matrix4x4.TRS(
                Quaternion.Euler(0f, yaw, 0f) *
                new Vector3(0f, 0f, outwardDistance),
                Quaternion.Euler(0f, yaw, 0f),
                Vector3.one);

            for (int column = 0; column < columns; column++)
            {
                float center = -wallLength * 0.5f +
                               (column + 0.5f) * cellWidth;
                Matrix4x4 panel = Matrix4x4.TRS(
                    new Vector3(
                        center - sourceBounds.center.x * scaleX,
                        bottom - sourceBounds.min.y * scaleY,
                        -sourceBounds.max.z),
                    Quaternion.identity,
                    new Vector3(scaleX, scaleY, 1f));
                AddSource(source, wallFrame * panel, choice, groups);
            }
        }

        static void AddRoof(
            string sourcePath,
            Vector3 buildingSize,
            float bottom,
            float height,
            Dictionary<Material, List<CombineInstance>> groups)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Bounds bounds = CalculateSourceBounds(source, MeshChoice.All);
            float scaleX = buildingSize.x / Mathf.Max(0.01f, bounds.size.x);
            float scaleY = height / Mathf.Max(0.01f, bounds.size.y);
            float scaleZ = buildingSize.z / Mathf.Max(0.01f, bounds.size.z);
            Matrix4x4 matrix = Matrix4x4.TRS(
                new Vector3(
                    -bounds.center.x * scaleX,
                    bottom - bounds.min.y * scaleY,
                    -bounds.center.z * scaleZ),
                Quaternion.identity,
                new Vector3(scaleX, scaleY, scaleZ));
            AddSource(source, matrix, MeshChoice.All, groups);
        }

        static void AddSource(
            GameObject source,
            Matrix4x4 placement,
            MeshChoice choice,
            Dictionary<Material, List<CombineInstance>> groups)
        {
            MeshFilter[] filters = SelectedFilters(source, choice);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (mesh == null || renderer == null)
                    continue;
                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 matrix = placement *
                                   RelativeMatrix(source.transform, filter.transform);
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    Material material = materials.Length == 0
                        ? null
                        : materials[Mathf.Min(subMesh, materials.Length - 1)];
                    if (material == null)
                        continue;
                    List<CombineInstance> instances;
                    if (!groups.TryGetValue(material, out instances))
                    {
                        instances = new List<CombineInstance>();
                        groups.Add(material, instances);
                    }
                    instances.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = subMesh,
                        transform = matrix
                    });
                }
            }
        }

        static Bounds CalculateSourceBounds(GameObject source, MeshChoice choice)
        {
            MeshFilter[] filters = SelectedFilters(source, choice);
            Bounds result = new Bounds();
            bool hasBounds = false;
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                    continue;
                Matrix4x4 matrix = RelativeMatrix(source.transform, filters[i].transform);
                Bounds bounds = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    point = matrix.MultiplyPoint3x4(point);
                    if (!hasBounds)
                    {
                        result = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }
            return result;
        }

        static MeshFilter[] SelectedFilters(GameObject source, MeshChoice choice)
        {
            MeshFilter[] all = source.GetComponentsInChildren<MeshFilter>(true);
            if (choice == MeshChoice.All || all.Length <= 1)
                return all;

            int minimumVertices = int.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].sharedMesh != null)
                    minimumVertices = Mathf.Min(
                        minimumVertices,
                        all[i].sharedMesh.vertexCount);
            }
            var result = new List<MeshFilter>();
            for (int i = 0; i < all.Length; i++)
            {
                Mesh mesh = all[i].sharedMesh;
                if (mesh != null && mesh.vertexCount <= minimumVertices + 8)
                    result.Add(all[i]);
            }
            return result.ToArray();
        }

        static Matrix4x4 RelativeMatrix(Transform root, Transform child)
        {
            Matrix4x4 result = Matrix4x4.identity;
            Transform current = child;
            while (current != null && current != root)
            {
                result = Matrix4x4.TRS(
                             current.localPosition,
                             current.localRotation,
                             current.localScale) * result;
                current = current.parent;
            }
            return result;
        }

        static Mesh SaveOrReplaceMesh(Mesh mesh, string path)
        {
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

        static bool ValidateSources()
        {
            bool valid = true;
            foreach (string path in BottomSources)
                valid &= ValidateSource(path);
            foreach (string path in FacadeSources)
                valid &= ValidateSource(path);
            foreach (string path in RoofSources)
                valid &= ValidateSource(path);
            return valid;
        }

        static bool ValidateSource(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                return true;
            Debug.LogError("缺少 NewGen Urban 建筑模块：" + path);
            return false;
        }

        static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        enum MeshChoice
        {
            All,
            LowestDetail
        }

        sealed class VariantSpec
        {
            public string name;
            public AirCombatBuildingBand band;
            public Vector3 size;
            public int bottom;
            public int facade;
            public int accent;
            public int roof;
        }
    }
}
