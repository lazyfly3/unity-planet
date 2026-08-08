using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CityPcg;

namespace UnityPlanet.CityPcg.Editor
{
    public static class CityPcgPrinciplesLabBuilder
    {
        public const string ScenePath =
            "Assets/Scenes/CityPcgPrinciplesLab.unity";
        const string AssetRoot = "Assets/CityPcgPrinciplesLab";
        const string MaterialRoot = AssetRoot + "/Materials";
        const string RuntimeTemplatePath =
            "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab";
        const string NewGenRoot =
            "Assets/Reversed Interactive/New Gen Urban";

        [MenuItem("Tools/城市 PCG/重建空战城市实验场")]
        public static void RebuildScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("请先退出 Play 模式再重建空战城市实验场。");
                return;
            }

            EnsureFolder("Assets", "CityPcgPrinciplesLab");
            EnsureFolder(AssetRoot, "Materials");
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "PlanetSurface");
            DarkCity2UrbanCatalog darkCity2Catalog =
                DarkCity2UrbanLibraryBuilder.BuildLibrary();
            NewGenUrbanBuildingCatalog buildingCatalog =
                darkCity2Catalog != null ? darkCity2Catalog.buildings : null;

            Material asphalt = darkCity2Catalog != null &&
                                darkCity2Catalog.asphalt != null
                ? darkCity2Catalog.asphalt
                : EnsureWorldTiledMaterial(
                "Urban_Asphalt_NewGen",
                NewGenRoot + "/Textures/Road.png",
                new Vector4(0.72f, 0.86f, 0.18f, 0.10f),
                11f,
                new Color(0.62f, 0.65f, 0.68f));
            Material cityBlockPaving = darkCity2Catalog != null &&
                                       darkCity2Catalog.cityPaving != null
                ? darkCity2Catalog.cityPaving
                : EnsureWorldTiledMaterial(
                "Urban_BlockPaving_NewGen",
                NewGenRoot + "/Textures/Paving.png",
                new Vector4(0f, 0f, 1f, 1f),
                5.4f,
                new Color(0.54f, 0.57f, 0.60f));
            Material sidewalk = darkCity2Catalog != null &&
                                darkCity2Catalog.sidewalk != null
                ? darkCity2Catalog.sidewalk
                : EnsureWorldTiledMaterial(
                "Urban_Sidewalk_NewGen",
                NewGenRoot + "/Textures/Paving.png",
                new Vector4(0f, 0f, 1f, 1f),
                3.6f,
                new Color(0.78f, 0.80f, 0.82f));
            Material repairCourtyard = EnsureWorldTiledMaterial(
                "Urban_RepairCourtyard",
                NewGenRoot + "/Textures/Paving.png",
                new Vector4(0f, 0f, 1f, 1f),
                4.2f,
                new Color(0.26f, 0.55f, 0.58f));
            Material parkSurface = EnsureWorldTiledMaterial(
                "Urban_TacticalPark",
                NewGenRoot + "/Textures/Grass.png",
                new Vector4(0f, 0f, 1f, 1f),
                7.5f,
                new Color(0.58f, 0.66f, 0.54f));
            Material curb = EnsureMaterial(
                "Urban_Curb",
                new Color(0.52f, 0.54f, 0.56f));
            Material laneMarking = darkCity2Catalog != null &&
                                   darkCity2Catalog.roadLines != null
                ? darkCity2Catalog.roadLines
                : EnsureMaterial(
                    "Urban_LaneMarking",
                    new Color(0.92f, 0.90f, 0.74f));
            Material dangerLaneMarking = EnsureMaterial(
                "Urban_DangerLaneMarking",
                new Color(0.95f, 0.30f, 0.08f),
                true);
            Material ground = cityBlockPaving;
            Material lowBuilding = EnsureMaterial(
                "AirCity_LowBuilding",
                new Color(0.08f, 0.32f, 0.48f));
            Material mediumBuilding = EnsureMaterial(
                "AirCity_MediumBuilding",
                new Color(0.06f, 0.50f, 0.72f));
            Material highBuilding = EnsureMaterial(
                "AirCity_HighBuilding",
                new Color(0.18f, 0.72f, 0.92f));
            Material facility = EnsureMaterial(
                "Facility",
                new Color(1f, 0.30f, 0.04f));

            Material mainRoute = EnsureMaterial(
                "AirCity_MainRoute",
                new Color(0.05f, 0.95f, 1f),
                true);
            Material maskedRoute = EnsureMaterial(
                "AirCity_MaskedRoute",
                new Color(0.18f, 1f, 0.42f),
                true);
            Material longRangeRoute = EnsureMaterial(
                "AirCity_LongRangeRoute",
                new Color(1f, 0.82f, 0.08f),
                true);
            Material suicideRoute = EnsureMaterial(
                "AirCity_SuicideRoute",
                new Color(1f, 0.04f, 0.20f),
                true);
            Material rangedRoute = EnsureMaterial(
                "AirCity_RangedRoute",
                new Color(1f, 0.44f, 0.05f),
                true);
            Material spawn = EnsureMaterial(
                "Spawn",
                new Color(0.10f, 0.95f, 1f),
                true);
            Material objective = EnsureMaterial(
                "Objective",
                new Color(1f, 0.08f, 0.12f),
                true);
            Material maneuver = EnsureTransparentMaterial(
                "AirCity_ManeuverVolume",
                new Color(0.12f, 0.95f, 0.48f, 0.075f));
            Material recovery = EnsureTransparentMaterial(
                "AirCity_RecoveryVolume",
                new Color(0.05f, 0.75f, 1f, 0.065f));
            Material occlusion = EnsureTransparentMaterial(
                "AirCity_OcclusionVolume",
                new Color(0.15f, 1f, 0.30f, 0.085f));
            Material exposure = EnsureTransparentMaterial(
                "AirCity_ExposureVolume",
                new Color(1f, 0.65f, 0.04f, 0.075f));

            var palette = new AirCombatCityPalette
            {
                road = asphalt,
                asphalt = asphalt,
                cityBlockPaving = cityBlockPaving,
                sidewalk = sidewalk,
                curb = curb,
                laneMarking = laneMarking,
                dangerLaneMarking = dangerLaneMarking,
                repairCourtyard = repairCourtyard,
                parkSurface = parkSurface,
                lowBuilding = lowBuilding,
                mediumBuilding = mediumBuilding,
                highBuilding = highBuilding,
                facility = facility,
                mainRoute = mainRoute,
                maskedRoute = maskedRoute,
                longRangeRoute = longRangeRoute,
                suicideRoute = suicideRoute,
                rangedRoute = rangedRoute,
                spawn = spawn,
                objective = objective,
                maneuverVolume = maneuver,
                recoveryVolume = recovery,
                occlusionVolume = occlusion,
                exposureVolume = exposure
            };
            GameObject roadJunctionPrefab = null;
            GameObject parkTreePrefab = null;
            GameObject rooftopMechanicalPrefab = null;
            GameObject[] rooftopBillboardPrefabs =
                System.Array.Empty<GameObject>();
            GameObject streetLightPrefab = null;
            GameObject parkPlanterPrefab = null;

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool reusedOpenScene = scene.IsValid() && scene.isLoaded;
            if (!reusedOpenScene)
            {
                scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
            }
            SceneManager.SetActiveScene(scene);

            try
            {
                if (reusedOpenScene)
                    ClearScene(scene);
                BuildSceneHierarchy(
                    scene,
                    ground,
                    palette,
                    darkCity2Catalog,
                    buildingCatalog,
                    roadJunctionPrefab,
                    parkTreePrefab,
                    rooftopMechanicalPrefab,
                    rooftopBillboardPrefabs,
                    streetLightPrefab,
                    parkPlanterPrefab,
                    mainRoute,
                    maskedRoute,
                    suicideRoute);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                if (!reusedOpenScene)
                    EditorSceneManager.CloseScene(scene, true);
            }

            RemoveFromBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "已在原截图场景中重建空战语义城市 PCG：" + ScenePath +
                "。未修改星球地形、正式战斗或飞船物理结构。");
        }

        [MenuItem("Tools/城市 PCG/打开空战城市实验场")]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath))
                RebuildScene();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void BuildSceneHierarchy(
            Scene scene,
            Material ground,
            AirCombatCityPalette palette,
            DarkCity2UrbanCatalog darkCity2Catalog,
            NewGenUrbanBuildingCatalog buildingCatalog,
            GameObject roadJunctionPrefab,
            GameObject parkTreePrefab,
            GameObject rooftopMechanicalPrefab,
            GameObject[] rooftopBillboardPrefabs,
            GameObject streetLightPrefab,
            GameObject parkPlanterPrefab,
            Material frontAxis,
            Material upAxis,
            Material rightAxis)
        {
            var guide = new GameObject(
                "00_生成顺序_飞机包线→三维空域→道路→建筑遮挡→任务结构→约束验证");
            SceneManager.MoveGameObjectToScene(guide, scene);

            var labObject = new GameObject(
                "AirCombatCityPcgLab_空战语义先行_完全自研");
            SceneManager.MoveGameObjectToScene(labObject, scene);
            AirCombatCityPcgLab lab = labObject.AddComponent<AirCombatCityPcgLab>();
            lab.ConfigureDemo(
                7319,
                palette,
                buildingCatalog,
                roadJunctionPrefab,
                parkTreePrefab,
                rooftopMechanicalPrefab,
                rooftopBillboardPrefabs,
                streetLightPrefab,
                parkPlanterPrefab,
                darkCity2Catalog);
            SaveRuntimeTemplate(lab);
            lab.Rebuild();

            var boundaryObject = new GameObject(
                "08_城市禁飞边界_警告带130m_返航带55m");
            SceneManager.MoveGameObjectToScene(boundaryObject, scene);
            UrbanAirCombatBoundary boundary =
                boundaryObject.AddComponent<UrbanAirCombatBoundary>();
            boundary.Configure(new Vector2(790f, 790f));

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "FlatBattlefield_1664米平坦城市战场";
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(0f, -1.1f, 0f);
            floor.transform.localScale = new Vector3(1792f, 2f, 1792f);
            Renderer floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
                floorRenderer.enabled = false;

            BuildOrientationGallery(
                scene,
                buildingCatalog,
                ground,
                frontAxis,
                upAxis,
                rightAxis);

            var cameraObject = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 1320f, -1460f);
            cameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 75f, 0f) - cameraObject.transform.position,
                Vector3.up);
            camera.fieldOfView = 57f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 5000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.022f, 0.04f);

            var lightObject = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.82f, 0.9f, 1f);
            light.intensity = 1.15f;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            RenderSettings.ambientMode =
                UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.2f, 0.28f, 0.4f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.13f, 0.2f);
            RenderSettings.ambientGroundColor = new Color(0.02f, 0.03f, 0.05f);

            if (!lab.HasValidPlan)
            {
                Debug.LogWarning(
                    "空战城市实验场已生成，但默认候选未通过：" +
                    lab.LastSummary + " / " + lab.Report?.failureReason,
                    lab);
            }
        }

        static void SaveRuntimeTemplate(AirCombatCityPcgLab source)
        {
            var template = new GameObject("UrbanCombatCityTemplate");
            template.SetActive(false);
            AirCombatCityPcgLab templateLab =
                template.AddComponent<AirCombatCityPcgLab>();
            EditorUtility.CopySerialized(source, templateLab);
            PrefabUtility.SaveAsPrefabAsset(template, RuntimeTemplatePath);
            Object.DestroyImmediate(template);
        }

        static void BuildOrientationGallery(
            Scene scene,
            NewGenUrbanBuildingCatalog catalog,
            Material ground,
            Material frontAxis,
            Material upAxis,
            Material rightAxis)
        {
            if (catalog == null)
                return;

            var gallery = new GameObject(
                "07_模型方向检查_底面Y0_顶部+Y_正面+Z_右侧+X");
            SceneManager.MoveGameObjectToScene(gallery, scene);
            gallery.transform.position = new Vector3(-1120f, 0f, 0f);

            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "方向检查平台_不属于战斗区域";
            platform.transform.SetParent(gallery.transform, false);
            platform.transform.localPosition = new Vector3(0f, -0.65f, 0f);
            platform.transform.localScale = new Vector3(260f, 1.2f, 980f);
            Renderer platformRenderer = platform.GetComponent<Renderer>();
            if (platformRenderer != null)
                platformRenderer.sharedMaterial = ground;
            Collider platformCollider = platform.GetComponent<Collider>();
            if (platformCollider != null)
                Object.DestroyImmediate(platformCollider);

            var samples = new System.Collections.Generic.List<GameObject>();
            AddGallerySample(samples, catalog.low, 0);
            AddGallerySample(samples, catalog.low, 2);
            AddGallerySample(samples, catalog.medium, 0);
            AddGallerySample(samples, catalog.medium, 3);
            AddGallerySample(samples, catalog.high, 0);
            AddGallerySample(samples, catalog.high, 3);
            AddGallerySample(samples, catalog.facility, 0);

            float startZ = -360f;
            for (int i = 0; i < samples.Count; i++)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    samples[i]);
                instance.transform.SetParent(gallery.transform, false);
                instance.transform.localPosition =
                    new Vector3(0f, 0f, startZ + i * 120f);
                instance.name = "样本" + (i + 1).ToString("D2") + "_" +
                                samples[i].name + "_正面朝平台蓝色+Z箭头";
                NormalizedBuildingModelInfo info =
                    instance.GetComponent<NormalizedBuildingModelInfo>();
                if (info != null)
                    info.ShowOrientationGizmo = true;
                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < colliders.Length; c++)
                    colliders[c].enabled = false;
            }

            Vector3 axisOrigin = new Vector3(0f, 2f, -455f);
            CreateDirectionAxis(
                gallery.transform,
                "蓝色_正面+Z",
                axisOrigin,
                Vector3.forward,
                62f,
                frontAxis);
            CreateDirectionAxis(
                gallery.transform,
                "绿色_顶部+Y",
                axisOrigin,
                Vector3.up,
                62f,
                upAxis);
            CreateDirectionAxis(
                gallery.transform,
                "红色_右侧+X",
                axisOrigin,
                Vector3.right,
                62f,
                rightAxis);
        }

        static void AddGallerySample(
            System.Collections.Generic.List<GameObject> samples,
            GameObject[] candidates,
            int index)
        {
            if (candidates == null || candidates.Length == 0)
                return;
            samples.Add(candidates[Mathf.Abs(index) % candidates.Length]);
        }

        static void CreateDirectionAxis(
            Transform parent,
            string name,
            Vector3 origin,
            Vector3 direction,
            float length,
            Material material)
        {
            GameObject axis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            axis.name = name;
            axis.transform.SetParent(parent, false);
            axis.transform.localPosition = origin + direction * length * 0.5f;
            axis.transform.localRotation = Quaternion.LookRotation(
                direction,
                direction == Vector3.up ? Vector3.forward : Vector3.up);
            axis.transform.localScale = new Vector3(3.5f, 3.5f, length);
            Renderer renderer = axis.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            Collider collider = axis.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);
        }

        static Material EnsureWorldTiledMaterial(
            string name,
            string texturePath,
            Vector4 atlasRect,
            float worldTileSize,
            Color color)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Shader shader = Shader.Find(
                "UnityPlanet/CityPCG/UrbanAtlasTile");
            if (shader == null)
            {
                Debug.LogError(
                    "城市地面 Shader 尚未导入：" +
                    "UnityPlanet/CityPCG/UrbanAtlasTile");
                return EnsureMaterial(name, color);
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetTexture("_MainTex", texture);
            material.SetVector("_AtlasRect", atlasRect);
            material.SetFloat("_WorldTileSize", Mathf.Max(0.25f, worldTileSize));
            material.SetColor("_Color", color);
            material.SetFloat("_Metallic", 0.02f);
            material.SetFloat("_Glossiness", 0.20f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureMaterial(
            string name,
            Color color,
            bool emission = false)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0.15f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.42f);
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.8f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material EnsureTransparentMaterial(string name, Color color)
        {
            Material material = EnsureMaterial(name, color);
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        static void ClearScene(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = roots.Length - 1; i >= 0; i--)
                Object.DestroyImmediate(roots[i]);
        }

        static void RemoveFromBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].path != ScenePath)
                    scenes.Add(current[i]);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
