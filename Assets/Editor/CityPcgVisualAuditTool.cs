using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CityPcg;

/// <summary>
/// 在独立 PreviewScene 中还原正式星球城市关卡并生成多机位截图。
/// 不保存场景、不修改玩家当前 Hierarchy，也不触碰自然地形和飞船物理结构。
/// </summary>
public static class CityPcgVisualAuditTool
{
    const string TemplatePath =
        "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab";
    const int Width = 1280;
    const int Height = 720;

    [MenuItem("Tools/城市 PCG/生成正式城市多角度自检截图")]
    public static void GenerateFromMenu()
    {
        GenerateAudit();
    }

    [MenuItem("Tools/城市 PCG/只运行城市 PCG 的 9 个局部测试")]
    public static void RunTargetedCityTests()
    {
        string outputDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../Artifacts/CityPcgVisualAudit"));
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, "局部测试报告.txt");
        var callback = new TargetedTestCallback(outputPath);
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(callback);
        try
        {
            var settings = new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = new[]
                {
                    "AirCombatCityPcgTests.DarkCityCatalogContainsTheExpandedSemanticFamilies",
                    "AirCombatCityPcgTests.FormalCityActuallyInstantiatesDistrictArtWithoutDecorationColliders",
                    "AirCombatCityPcgTests.RepresentativeSeedsKeepMixedSkylineAndNoVerticalBypass",
                    "AirCombatCityPcgTests.SameSeedIsDeterministicAndDifferentSeedChangesLayout",
                    "AirCombatCityPcgTests.ClearanceMixesLowAndMediumCoverUnderTheManeuverBowl",
                    "AirCombatCityPcgTests.FacilityAssaultBuildsThreeReachableCores",
                    "AirCombatCityPcgTests.FormalCityHasCombatHeightCoverAndSpawnCompatibleIngresses",
                    "UrbanDestructionRuntimeTests.SkybridgeIgnoresOrdinaryFireAndSmallBlastButDemolitionSeversIt",
                    "UrbanDestructionRuntimeTests.SkybridgeAcceptsOnlyAGenuinelyLargeExplosion"
                }
            })
            {
                runSynchronously = true
            };
            api.Execute(settings);
        }
        finally
        {
            api.UnregisterCallbacks(callback);
            UnityEngine.Object.DestroyImmediate(api);
        }
    }

    static void GenerateAudit()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            TemplatePath);
        if (template == null)
            throw new FileNotFoundException("找不到正式城市模板", TemplatePath);

        string outputDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../Artifacts/CityPcgVisualAudit"));
        Directory.CreateDirectory(outputDirectory);

        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(preview);
            GameObject city = UnityEngine.Object.Instantiate(template);
            city.name = "FormalPlanetUrbanCombat_AuditOnly";
            SceneManager.MoveGameObjectToScene(city, preview);
            city.SetActive(true);

            AirCombatCityPcgLab lab =
                city.GetComponent<AirCombatCityPcgLab>();
            if (lab == null)
                throw new InvalidOperationException("正式城市模板缺少 AirCombatCityPcgLab");

            Camera camera = CreateAuditCamera(preview);
            CreateAuditLighting(preview);
            int[] auditSeeds = { 7319, 15437, 29881 };
            int totalViews = 0;
            for (int seedIndex = 0; seedIndex < auditSeeds.Length; seedIndex++)
            {
                int seed = auditSeeds[seedIndex];
                // 使用和正式星球战斗相同的入口，而不是单独拼一个审计假场景。
                lab.ConfigureRuntimeMission(seed, AirCombatCityMission.Clearance);
                if (lab.Plan == null || lab.Report == null)
                    throw new InvalidOperationException(
                        "城市 PCG 没有生成可审计的规划数据，Seed=" + seed);
                string seedDirectory = Path.Combine(
                    outputDirectory,
                    "Seed_" + seed);
                Directory.CreateDirectory(seedDirectory);
                List<AuditView> views = BuildViews(lab);
                totalViews += views.Count;
                for (int i = 0; i < views.Count; i++)
                    RenderView(camera, views[i], seedDirectory);

                string report = BuildReport(city, lab, views);
                File.WriteAllText(
                    Path.Combine(seedDirectory, "自检报告.txt"),
                    report,
                    new UTF8Encoding(true));
            }
            File.WriteAllText(
                Path.Combine(outputDirectory, "完成标记.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                new UTF8Encoding(false));

            Debug.Log(
                "[城市PCG自检] 已对 3 个 Seed 生成 " + totalViews +
                " 张正式城市多角度截图与中文报告：" + outputDirectory);
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                SceneManager.SetActiveScene(previousActiveScene);
            EditorSceneManager.CloseScene(preview, true);
        }
    }

    static Camera CreateAuditCamera(Scene preview)
    {
        var gameObject = new GameObject("AuditCamera");
        SceneManager.MoveGameObjectToScene(gameObject, preview);
        Camera camera = gameObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.14f, 0.18f, 1f);
        camera.fieldOfView = 67f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 4200f;
        camera.allowHDR = true;
        UrbanBloomImageEffect bloom =
            gameObject.AddComponent<UrbanBloomImageEffect>();
        bloom.Configure(1.02f, 0.78f, 2, 2);
        return camera;
    }

    static void CreateAuditLighting(Scene preview)
    {
        CreateDirectionalLight(
            preview,
            "AuditKeyLight",
            new Vector3(48f, -32f, 0f),
            new Color(1.00f, 0.94f, 0.84f),
            1.25f);
        CreateDirectionalLight(
            preview,
            "AuditFillLight",
            new Vector3(32f, 142f, 0f),
            new Color(0.42f, 0.58f, 0.82f),
            0.48f);
    }

    static void CreateDirectionalLight(
        Scene preview,
        string objectName,
        Vector3 euler,
        Color color,
        float intensity)
    {
        var gameObject = new GameObject(objectName);
        SceneManager.MoveGameObjectToScene(gameObject, preview);
        gameObject.transform.rotation = Quaternion.Euler(euler);
        Light light = gameObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.Soft;
    }

    static List<AuditView> BuildViews(AirCombatCityPcgLab lab)
    {
        AirCombatCityPlan plan = lab.Plan;
        AirCombatCitySettings settings = lab.Settings;
        AirCombatRoadStrip mainRoad = plan.roads
            .Where(road => road.kind == AirCombatRouteKind.Main)
            .OrderByDescending(road => (road.end - road.start).sqrMagnitude)
            .FirstOrDefault() ?? plan.roads.First();
        Vector3 mainDirection = (mainRoad.end - mainRoad.start).normalized;
        Vector3 mainStart = Vector3.Lerp(mainRoad.start, mainRoad.end, 0.10f);
        Vector3 mainTarget = Vector3.Lerp(mainRoad.start, mainRoad.end, 0.68f);

        AirCombatBuildingLot tallest = plan.buildings
            .OrderByDescending(lot => lot.size.y)
            .First();
        Quaternion tallestRotation = Quaternion.Euler(0f, tallest.yaw, 0f);
        Vector3 tallestFront = tallestRotation * Vector3.forward;

        AirCombatTacticalVolume recovery = plan.volumes
            .FirstOrDefault(volume =>
                volume.kind == AirCombatVolumeKind.RecoveryPocket);
        Vector3 recoveryCenter = recovery != null
            ? recovery.center
            : new Vector3(-settings.mapSize * 0.25f, settings.lowAltitude, 0f);
        Vector3 recoveryOpenDirection = new Vector3(
            -Mathf.Sign(recoveryCenter.x),
            0f,
            0f);
        if (recoveryOpenDirection.sqrMagnitude < 0.1f)
            recoveryOpenDirection = Vector3.right;

        AirCombatTacticalVolume danger = plan.volumes
            .FirstOrDefault(volume =>
                volume.kind == AirCombatVolumeKind.DangerPlaza);
        Vector3 dangerCenter = danger != null
            ? danger.center
            : plan.objective;

        UrbanDestructibleBridge[] allBridges = lab
            .GetComponentsInChildren<UrbanDestructibleBridge>(true);
        IGrouping<string, UrbanDestructibleBridge> auditedStack = allBridges
            .GroupBy(BridgePairKey)
            .Where(group => group.Count() >= 3)
            .OrderBy(group => group.Average(bridge => new Vector2(
                bridge.DestructionBounds.center.x,
                bridge.DestructionBounds.center.z).magnitude))
            .FirstOrDefault() ?? allBridges
                .GroupBy(BridgePairKey)
                .Where(group => group.Count() >= 2)
                .OrderBy(group => group.Average(bridge => new Vector2(
                    bridge.DestructionBounds.center.x,
                    bridge.DestructionBounds.center.z).magnitude))
                .FirstOrDefault();
        UrbanDestructibleBridge auditedBridge = auditedStack != null
            ? auditedStack.OrderBy(bridge => bridge.DestructionBounds.center.y)
                .FirstOrDefault()
            : allBridges.OrderBy(bridge => new Vector2(
                    bridge.DestructionBounds.center.x,
                    bridge.DestructionBounds.center.z).magnitude)
                .FirstOrDefault();
        Bounds bridgeBounds = auditedBridge != null
            ? auditedBridge.DestructionBounds
            : new Bounds(new Vector3(0f, 95f, 0f), new Vector3(60f, 4f, 9f));
        if (auditedStack != null)
        {
            foreach (UrbanDestructibleBridge bridge in auditedStack)
                bridgeBounds.Encapsulate(bridge.DestructionBounds);
        }
        Vector3 bridgeForward = auditedBridge != null
            ? auditedBridge.transform.forward
            : Vector3.forward;
        Vector3 bridgeRight = auditedBridge != null
            ? auditedBridge.transform.right
            : Vector3.right;
        float half = settings.mapSize * 0.5f;

        Transform[] generated = lab.GetComponentsInChildren<Transform>(true);
        Transform industrialAnchor = generated.FirstOrDefault(item =>
            item.name.IndexOf("Industrial", StringComparison.Ordinal) >= 0 &&
            (item.name.StartsWith("RoofEquipment_", StringComparison.Ordinal) ||
             item.name.StartsWith("FacadeAttachment_", StringComparison.Ordinal)));
        Transform commercialAnchor = generated.FirstOrDefault(item =>
            item.name.IndexOf("Commercial", StringComparison.Ordinal) >= 0 &&
            (item.name.StartsWith("FacadeBillboard_", StringComparison.Ordinal) ||
             item.name.StartsWith("FacadeNeon_", StringComparison.Ordinal)));
        Transform transitAnchor = generated.FirstOrDefault(item =>
            item.name.IndexOf("Transit", StringComparison.Ordinal) >= 0 &&
            item.name.StartsWith("StreetUtility_", StringComparison.Ordinal));
        Transform mixedAnchor = generated.FirstOrDefault(item =>
            item.name.IndexOf("Mixed", StringComparison.Ordinal) >= 0 &&
            item.name.StartsWith("FacadeAttachment_", StringComparison.Ordinal));
        Transform serviceAnchor = generated.FirstOrDefault(item =>
            item.name.IndexOf("Service", StringComparison.Ordinal) >= 0 &&
            (item.name.StartsWith("RoofEquipment_", StringComparison.Ordinal) ||
             item.name.StartsWith("StreetUtility_", StringComparison.Ordinal)));
        Transform cableAnchor = generated.FirstOrDefault(item =>
            item.name.StartsWith("AerialCableLink_", StringComparison.Ordinal));
        AerialCableCurve cableCurve = cableAnchor != null
            ? cableAnchor.GetComponentsInChildren<AerialCableCurve>(true)
                .FirstOrDefault(item => item.name == "Cable_Dark_Center")
            : null;
        Vector3 cableStart = cableCurve != null
            ? cableCurve.GetWorldPoint(0)
            : new Vector3(-60f, 90f, 0f);
        Vector3 cableEnd = cableCurve != null
            ? cableCurve.GetWorldPoint(cableCurve.PointCount - 1)
            : new Vector3(60f, 90f, 0f);
        Vector3 cableCenter = (cableStart + cableEnd) * 0.5f -
                              Vector3.up * (cableCurve != null ? 8f : 0f);
        Vector3 cableDirection = cableEnd - cableStart;
        cableDirection.y = 0f;
        cableDirection = cableDirection.sqrMagnitude > 0.01f
            ? cableDirection.normalized
            : Vector3.forward;
        Vector3 cableSide = Vector3.Cross(Vector3.up, cableDirection).normalized;
        UrbanDestructibleBuilding closeCompanion = lab
            .GetComponentsInChildren<UrbanDestructibleBuilding>(true)
            .FirstOrDefault(item => item.name.StartsWith(
                "TacticalClosePair_",
                StringComparison.Ordinal));
        UrbanDestructibleBuilding closeOwner = closeCompanion != null
            ? lab.GetComponentsInChildren<UrbanDestructibleBuilding>(true)
                .Where(item => !ReferenceEquals(item, closeCompanion))
                .OrderBy(item => (item.DestructionBounds.center -
                                  closeCompanion.DestructionBounds.center)
                    .sqrMagnitude)
                .FirstOrDefault()
            : null;
        Vector3 closeGapCenter = new Vector3(0f, settings.lowAltitude, 0f);
        Vector3 closeGapForward = Vector3.forward;
        if (closeCompanion != null && closeOwner != null)
        {
            Bounds companionBounds = closeCompanion.DestructionBounds;
            Bounds ownerBounds = closeOwner.DestructionBounds;
            Vector3 separation = companionBounds.center - ownerBounds.center;
            bool separatedOnX = Mathf.Abs(separation.x) > Mathf.Abs(separation.z);
            if (separatedOnX)
            {
                float leftEdge = separation.x > 0f
                    ? ownerBounds.max.x
                    : companionBounds.max.x;
                float rightEdge = separation.x > 0f
                    ? companionBounds.min.x
                    : ownerBounds.min.x;
                closeGapCenter = new Vector3(
                    (leftEdge + rightEdge) * 0.5f,
                    Mathf.Min(ownerBounds.max.y, companionBounds.max.y) * 0.52f,
                    (ownerBounds.center.z + companionBounds.center.z) * 0.5f);
                closeGapForward = Vector3.forward;
            }
            else
            {
                float lowerEdge = separation.z > 0f
                    ? ownerBounds.max.z
                    : companionBounds.max.z;
                float upperEdge = separation.z > 0f
                    ? companionBounds.min.z
                    : ownerBounds.min.z;
                closeGapCenter = new Vector3(
                    (ownerBounds.center.x + companionBounds.center.x) * 0.5f,
                    Mathf.Min(ownerBounds.max.y, companionBounds.max.y) * 0.52f,
                    (lowerEdge + upperEdge) * 0.5f);
                closeGapForward = Vector3.right;
            }
        }
        Vector3 industrialCenter = industrialAnchor != null
            ? industrialAnchor.position
            : new Vector3(-half * 0.35f, 45f, half * 0.20f);
        Vector3 commercialCenter = commercialAnchor != null
            ? commercialAnchor.position
            : new Vector3(half * 0.22f, 65f, -half * 0.18f);
        Vector3 transitCenter = transitAnchor != null
            ? transitAnchor.position
            : new Vector3(0f, 20f, half * 0.30f);
        Vector3 mixedCenter = mixedAnchor != null
            ? mixedAnchor.position
            : new Vector3(-half * 0.20f, 42f, -half * 0.24f);
        Vector3 serviceCenter = serviceAnchor != null
            ? serviceAnchor.position
            : recoveryCenter;

        return new List<AuditView>
        {
            new AuditView(
                "01_低空主干道街谷",
                mainStart + Vector3.up * 74f - mainDirection * 45f,
                mainTarget + Vector3.up * 98f,
                66f),
            new AuditView(
                "02_中央斜向战术掩体",
                new Vector3(-360f, 96f, -330f),
                new Vector3(80f, 108f, 70f),
                70f),
            new AuditView(
                "03_高楼屋顶设备与临街广告",
                tallest.center + tallestFront * 92f +
                Vector3.up * (tallest.size.y * 0.50f + 13f),
                tallest.center + tallestFront *
                Mathf.Max(0f, tallest.size.z * 0.5f - 4f) +
                Vector3.up * (tallest.size.y * 0.50f + 7f),
                52f),
            new AuditView(
                "04_脱离维修区",
                recoveryCenter + recoveryOpenDirection * 24f +
                Vector3.up * 72f,
                recoveryCenter - recoveryOpenDirection * 38f +
                Vector3.up * 70f,
                65f),
            new AuditView(
                "05_危险广场攻防入口",
                dangerCenter + new Vector3(-220f, 92f, -190f),
                dangerCenter + Vector3.up * 78f,
                68f),
            new AuditView(
                "06_同楼对多层不等距连廊与桥头接缝",
                bridgeBounds.center + bridgeRight * Mathf.Max(
                    78f,
                    bridgeBounds.size.y * 1.15f) - bridgeForward * 10f +
                Vector3.up * Mathf.Max(14f, bridgeBounds.size.y * 0.12f),
                bridgeBounds.center,
                54f),
            new AuditView(
                "07_连廊下方低空穿越路线",
                bridgeBounds.center + bridgeRight * 48f + bridgeForward * 8f -
                Vector3.up * 18f,
                bridgeBounds.center - Vector3.up * 5f,
                62f),
            new AuditView(
                "08_全城高空密度与高度错落",
                new Vector3(-half * 0.72f, 780f, -half * 0.88f),
                new Vector3(0f, 92f, 0f),
                64f),
            new AuditView(
                "09_工业区管线设备与立面轮廓",
                industrialAnchor != null
                    ? industrialCenter + industrialAnchor.forward * 62f +
                      industrialAnchor.right * 38f + Vector3.up * 28f
                    : industrialCenter + new Vector3(-92f, 58f, -78f),
                industrialCenter + Vector3.up * 16f,
                60f),
            new AuditView(
                "10_商业区广告霓虹与街谷",
                commercialAnchor != null
                    ? commercialCenter + commercialAnchor.forward * 70f +
                      commercialAnchor.right * 10f + Vector3.up * 18f
                    : commercialCenter + new Vector3(78f, 48f, -86f),
                commercialCenter + Vector3.up * 10f,
                58f),
            new AuditView(
                "11_交通区街道设施与飞行净空",
                transitAnchor != null
                    ? transitCenter + transitAnchor.right * 54f +
                      transitAnchor.forward * 28f + Vector3.up * 24f
                    : transitCenter + new Vector3(-76f, 42f, -72f),
                transitCenter + Vector3.up * 7f,
                62f),
            new AuditView(
                "12_混合区消防梯阳台与外墙线缆",
                mixedAnchor != null
                    ? mixedCenter + mixedAnchor.forward * 52f +
                      mixedAnchor.right * 12f + Vector3.up * 14f
                    : mixedCenter + new Vector3(58f, 35f, -54f),
                mixedCenter + Vector3.up * 9f,
                55f),
            new AuditView(
                "13_维修区设备与脱战识别",
                serviceAnchor != null
                    ? serviceCenter + serviceAnchor.forward * 48f +
                      serviceAnchor.right * 26f + Vector3.up * 24f
                    : serviceCenter + new Vector3(-52f, 38f, -46f),
                serviceCenter + Vector3.up * 8f,
                58f),
            new AuditView(
                "14_跨楼三色缆线端点与自然下垂",
                cableCenter + cableSide * 62f - cableDirection * 18f +
                Vector3.up * 18f,
                cableCenter,
                52f),
            new AuditView(
                "15_船体尺寸门与楼间技术窄缝",
                closeGapCenter - closeGapForward * 76f + Vector3.up * 4f,
                closeGapCenter + closeGapForward * 34f,
                58f)
        };
    }

    static void RenderView(
        Camera camera,
        AuditView view,
        string outputDirectory)
    {
        camera.transform.position = view.position;
        camera.transform.LookAt(view.target, Vector3.up);
        camera.fieldOfView = view.fieldOfView;

        RenderTexture texture = RenderTexture.GetTemporary(
            Width,
            Height,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = texture;
            RenderTexture.active = texture;
            camera.Render();
            // OnRenderImage bloom uses temporary render targets internally.
            // Rebind the camera's final HDR target before ReadPixels; otherwise
            // the editor may still point at a released blur buffer.
            RenderTexture.active = texture;
            image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(
                Path.Combine(outputDirectory, view.fileName + ".png"),
                image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            if (image != null)
                UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(texture);
        }
    }

    static string BuildReport(
        GameObject city,
        AirCombatCityPcgLab lab,
        IReadOnlyCollection<AuditView> views)
    {
        AirCombatCityPlan plan = lab.Plan;
        AirCombatCityReport generation = lab.Report;
        AirCombatCitySettings settings = lab.Settings;
        Transform[] objects = city.GetComponentsInChildren<Transform>(true);
        Transform[] mechanical = objects.Where(item =>
            item.name.StartsWith("RoofEquipment_", StringComparison.Ordinal)).ToArray();
        Transform[] billboards = objects.Where(item =>
            item.name.StartsWith("FacadeBillboard_", StringComparison.Ordinal) ||
            item.name.StartsWith("FacadeNeon_", StringComparison.Ordinal)).ToArray();
        Transform[] facadeAttachments = objects.Where(item =>
            item.name.StartsWith("FacadeAttachment_", StringComparison.Ordinal)).ToArray();
        Transform[] streetLights = objects.Where(item =>
            item.name.StartsWith("StreetLight_", StringComparison.Ordinal)).ToArray();
        Transform[] streetUtilities = objects.Where(item =>
            item.name.StartsWith("StreetUtility_", StringComparison.Ordinal) ||
            item.name.StartsWith("StreetBarrier_", StringComparison.Ordinal)).ToArray();
        Transform[] infillBuildings = objects.Where(item =>
            item.name.StartsWith("InfillBuilding_", StringComparison.Ordinal)).ToArray();
        Transform[] aerialCableLinks = objects.Where(item =>
            item.name.StartsWith("AerialCableLink_", StringComparison.Ordinal)).ToArray();
        Transform[] tacticalClosePairs = objects.Where(item =>
            item.name.StartsWith("TacticalClosePair_", StringComparison.Ordinal)).ToArray();
        int compactHullGates = tacticalClosePairs.Count(item =>
            item.name.IndexOf("CompactHullGate", StringComparison.Ordinal) >= 0);
        int standardSkillGates = tacticalClosePairs.Count(item =>
            item.name.IndexOf("StandardSkillGate", StringComparison.Ordinal) >= 0);
        int heavyHullGates = tacticalClosePairs.Count(item =>
            item.name.IndexOf("HeavyHullGate", StringComparison.Ordinal) >= 0);
        UrbanDestructibleBridge[] bridges = city
            .GetComponentsInChildren<UrbanDestructibleBridge>(true);
        IGrouping<string, UrbanDestructibleBridge>[] bridgePairGroups = bridges
            .GroupBy(BridgePairKey)
            .ToArray();
        int multiLevelBridgePairs = bridgePairGroups.Count(group =>
            group.Count() >= 2);
        int irregularBridgeStacks = bridgePairGroups.Count(group =>
            HasIrregularVerticalSpacing(group));
        float minimumStackGap = bridgePairGroups
            .SelectMany(BridgeVerticalGaps)
            .DefaultIfEmpty(0f)
            .Min();
        UrbanDestructibleBuilding[] destructibleBuildings = city
            .GetComponentsInChildren<UrbanDestructibleBuilding>(true);
        int largeParcelCount = destructibleBuildings.Count(building =>
            building.StableId.StartsWith("building.large.", StringComparison.Ordinal) ||
            building.StableId.StartsWith(
                "infill-LargeCompositeParcel",
                StringComparison.Ordinal));
        int standardParcelCount = destructibleBuildings.Count(building =>
            building.StableId.StartsWith("building.standard.", StringComparison.Ordinal) ||
            building.StableId.StartsWith(
                "infill-StandardParcel",
                StringComparison.Ordinal));
        int smallParcelCount = destructibleBuildings.Count(building =>
            building.StableId.StartsWith(
                "building.small-gapfill.",
                StringComparison.Ordinal) ||
            building.StableId.StartsWith(
                "infill-SmallClusterParcel",
                StringComparison.Ordinal));
        int brightEmissiveMaterialCount = CountBrightEmissiveMaterials(city);
        int cityGlowRuntimeCount = city
            .GetComponentsInChildren<UrbanCityGlowRuntime>(true).Length;
        Transform[] bridgeHeads = objects.Where(item =>
            item.name.StartsWith("BridgeHead_", StringComparison.Ordinal)).ToArray();
        int invalidBridgeEndpointCount;
        int sameBuildingBridgeCount;
        int crossRoadBlockBridgeCount;
        CountInvalidBridgeConnections(
            bridges,
            destructibleBuildings,
            settings.buildingSpacing * 3f,
            out invalidBridgeEndpointCount,
            out sameBuildingBridgeCount,
            out crossRoadBlockBridgeCount);
        int invalidCableEndpointCount;
        int sameBuildingCableCount;
        int cableThirdBuildingIntersections;
        int invalidCableLineCount;
        CountInvalidCableConnections(
            aerialCableLinks,
            destructibleBuildings,
            out invalidCableEndpointCount,
            out sameBuildingCableCount,
            out cableThirdBuildingIntersections,
            out invalidCableLineCount);
        int bridgesWithoutCollider = bridges.Count(bridge =>
            bridge.GetComponentsInChildren<Collider>(true).Length == 0);
        Collider[] decorationColliders = objects
            .Where(item => item.name.StartsWith("04B_", StringComparison.Ordinal))
            .SelectMany(item => item.GetComponentsInChildren<Collider>(true))
            .ToArray();
        int cableColliderCount = aerialCableLinks.Sum(item =>
            item.GetComponentsInChildren<Collider>(true).Length);

        int low = plan.buildings.Count(lot =>
            lot.band == AirCombatBuildingBand.Low);
        int medium = plan.buildings.Count(lot =>
            lot.band == AirCombatBuildingBand.Medium);
        int high = plan.buildings.Count(lot =>
            lot.band == AirCombatBuildingBand.High);
        float centralRadius = settings.ManeuverDiameter * 0.5f;
        int centralUsefulCover = plan.buildings.Count(lot =>
            new Vector2(lot.center.x, lot.center.z).magnitude < centralRadius &&
            lot.size.y >= settings.lowAltitude + 12f);
        int centralLowCover = plan.buildings.Count(lot =>
            new Vector2(lot.center.x, lot.center.z).magnitude < centralRadius &&
            lot.band == AirCombatBuildingBand.Low);
        int centralMediumCover = plan.buildings.Count(lot =>
            new Vector2(lot.center.x, lot.center.z).magnitude < centralRadius &&
            lot.band == AirCombatBuildingBand.Medium);
        float maximumIngressRadius = plan.ingresses.Count == 0
            ? 0f
            : plan.ingresses.Max(ingress =>
                new Vector2(ingress.position.x, ingress.position.z).magnitude);

        int billboardFacingFailures = billboards.Count(billboard =>
            !BillboardFacesNearestRoad(billboard, plan));
        int facadeAttachmentFailures = facadeAttachments.Count(item =>
            !FacadeAnchorFitsOwner(item, plan));
        int rooftopPlacementFailures = mechanical.Count(item =>
            !RooftopAnchorFitsOwner(item, plan));
        int streetLightBuildingOverlaps = streetLights.Count(item =>
            PointInsideAnyBuilding(item.position, plan, 0.6f));
        int streetUtilityBuildingOverlaps = streetUtilities.Count(item =>
            PointInsideAnyDestructibleBuilding(
                item.position,
                destructibleBuildings,
                0.35f));
        int macroDecorationCount = mechanical.Length + billboards.Length +
                                   facadeAttachments.Length +
                                   streetUtilities.Length + bridges.Length +
                                   aerialCableLinks.Length;
        float macroPerBuilding = destructibleBuildings.Length > 0
            ? macroDecorationCount / (float)destructibleBuildings.Length
            : 0f;
        int representedDistricts = CountRepresentedDistricts(objects);
        int representedMacroFamilies = CountRepresentedMacroFamilies(objects);
        float maximumCentralCoverGap = CalculateMaximumCentralCoverGap(
            lab,
            destructibleBuildings);
        float minimumRouteClearance = CalculateMinimumRouteClearance(
            lab,
            destructibleBuildings);

        var checks = new List<AuditCheck>
        {
            new AuditCheck("大地块构成主体，小地块只补缝",
                largeParcelCount >= 100 &&
                largeParcelCount >= smallParcelCount * 2,
                "大/中/小地块=" + largeParcelCount + "/" +
                standardParcelCount + "/" + smallParcelCount),
            new AuditCheck("城市总密度足够且空白街区有规则补楼",
                destructibleBuildings.Length >= 240 &&
                infillBuildings.Length >= 30,
                "总战术楼=" + destructibleBuildings.Length +
                "，补缝楼=" + infillBuildings.Length),
            new AuditCheck("Dark City 自发光材质与城市专用 Bloom 均已生效",
                brightEmissiveMaterialCount >= 8 && cityGlowRuntimeCount == 1,
                "HDR 自发光材质=" + brightEmissiveMaterialCount +
                "，城市 Bloom 控制器=" + cityGlowRuntimeCount),
            new AuditCheck("PCG 约束报告通过", generation.valid,
                generation.Summary +
                (string.IsNullOrEmpty(generation.failureReason)
                    ? string.Empty
                    : "；" + generation.failureReason)),
            new AuditCheck("中心不是无掩体平地", centralUsefulCover >= 8,
                "中心有效中高遮挡=" + centralUsefulCover + "（要求≥8）"),
            new AuditCheck("中央低/中楼均能形成高低拉扯", centralLowCover >= 4 && centralMediumCover >= 4,
                "中央低/中=" + centralLowCover + "/" + centralMediumCover + "（各要求≥4）"),
            new AuditCheck("高楼真正影响 220m 战斗层", generation.maximumTowerHeight >= 330f,
                "最高楼=" + generation.maximumTowerHeight.ToString("0.0") + "m"),
            new AuditCheck("建筑高低混合而非成片单高", low > 0 && medium > 0 && high > 0,
                "低/中/高=" + low + "/" + medium + "/" + high),
            new AuditCheck("空白街区补入可形成路线选择的战术楼体", infillBuildings.Length >= 30,
                "规则填充楼=" + infillBuildings.Length + "（大地块主导后要求≥30）"),
            new AuditCheck("内部刷新兼容采样点位于允许半径", maximumIngressRadius <= 510f,
                "最远入口=" + maximumIngressRadius.ToString("0.0") + "m"),
            new AuditCheck("屋顶设备存在且都在所属屋顶范围", mechanical.Length > 0 && rooftopPlacementFailures == 0,
                "设备=" + mechanical.Length + "，越界=" + rooftopPlacementFailures),
            new AuditCheck("广告存在且正面朝最近道路", billboards.Length > 0 && billboardFacingFailures == 0,
                "广告=" + billboards.Length + "，朝向失败=" + billboardFacingFailures),
            new AuditCheck("消防梯、阳台、管线和商店都贴在所属建筑立面",
                facadeAttachments.Length > 0 && facadeAttachmentFailures == 0,
                "立面附属=" + facadeAttachments.Length +
                "，挂点失败=" + facadeAttachmentFailures),
            new AuditCheck("路灯存在且没有落入建筑体", streetLights.Length > 0 && streetLightBuildingOverlaps == 0,
                "路灯=" + streetLights.Length + "，建筑重叠=" + streetLightBuildingOverlaps),
            new AuditCheck("街边设施只放在人行道后缘且没有落入楼体",
                streetUtilities.Length > 0 && streetUtilityBuildingOverlaps == 0,
                "街边终端/交通/工业设施=" + streetUtilities.Length +
                "，楼体重叠=" + streetUtilityBuildingOverlaps),
            new AuditCheck("城市至少形成四种可辨认功能区",
                representedDistricts >= 4,
                "可辨认分区=" + representedDistricts + "/5"),
            new AuditCheck("大型轮廓资源不是单一广告模板",
                representedMacroFamilies >= 6,
                "已出现大型装饰族=" + representedMacroFamilies + "/10"),
            new AuditCheck("宏观装饰密度不过空也不过载",
                macroPerBuilding >= 0.55f && macroPerBuilding <= 2.15f,
                "宏观装饰/建筑=" + macroPerBuilding.ToString("0.00") +
                "（大地块表面积增加后允许0.55～2.15）"),
            new AuditCheck("中央交战区不存在长距离无掩体空洞",
                maximumCentralCoverGap <= 190f,
                "最大最近掩体距离=" + maximumCentralCoverGap.ToString("0.0") +
                "m（要求≤190m）"),
            new AuditCheck("主要飞行路线保留飞船安全净空",
                minimumRouteClearance >= settings.wingspan * 0.55f,
                "最小航路侧向净空=" + minimumRouteClearance.ToString("0.0") +
                "m（要求≥" + (settings.wingspan * 0.55f).ToString("0.0") + "m）"),
            new AuditCheck("楼间窄缝反向影响玩家船体尺寸选择",
                compactHullGates >= 2 && standardSkillGates >= 2 &&
                heavyHullGates >= 2,
                "紧凑船体门/标准技术门/重型船体门=" +
                compactHullGates + "/" + standardSkillGates + "/" +
                heavyHullGates + "（各要求≥2）"),
            new AuditCheck("楼间实体连廊达到城市级多路线网络", bridges.Length >= 150,
                "有效实体连廊=" + bridges.Length + "（要求≥150）"),
            new AuditCheck("同一楼对允许形成多层连廊而不是只能连接一次",
                multiLevelBridgePairs >= 12,
                "多层连接楼对=" + multiLevelBridgePairs + "（要求≥12）"),
            new AuditCheck("多层连廊采用不等距高度并保留纵向飞行净空",
                irregularBridgeStacks >= 3 && minimumStackGap >= 18f,
                "不等距三层以上楼对=" + irregularBridgeStacks +
                "，最小层间距=" + minimumStackGap.ToString("0.0") +
                "m（要求≥18m）"),
            new AuditCheck("至少二分之一实体连廊跨越道路分隔的真实街区",
                crossRoadBlockBridgeCount >= Mathf.CeilToInt(bridges.Length * 0.5f),
                "跨道路街区连廊=" + crossRoadBlockBridgeCount + "/" + bridges.Length +
                "（要求至少二分之一）"),
            new AuditCheck("每条连廊有两个桥头遮住立面接缝",
                bridgeHeads.Length == bridges.Length * 2,
                "连廊/桥头=" + bridges.Length + "/" + bridgeHeads.Length),
            new AuditCheck("连廊两端分别插入两栋不同建筑的实际边界",
                invalidBridgeEndpointCount == 0 && sameBuildingBridgeCount == 0,
                "悬空端点=" + invalidBridgeEndpointCount +
                "，同楼误连=" + sameBuildingBridgeCount),
            new AuditCheck("连廊保留独立碰撞代理",
                bridgesWithoutCollider == 0,
                "缺少碰撞代理=" + bridgesWithoutCollider),
            new AuditCheck("跨楼三色缆线形成独立于实体连廊的视觉网络",
                aerialCableLinks.Length >= 18 && invalidCableLineCount == 0,
                "缆线组=" + aerialCableLinks.Length +
                "，非三线组=" + invalidCableLineCount),
            new AuditCheck("每组缆线两端嵌入两栋不同楼且不穿过第三栋楼",
                invalidCableEndpointCount == 0 && sameBuildingCableCount == 0 &&
                cableThirdBuildingIntersections == 0,
                "悬空端点=" + invalidCableEndpointCount +
                "，同楼误连=" + sameBuildingCableCount +
                "，穿楼=" + cableThirdBuildingIntersections),
            new AuditCheck("楼间缆线仅作视觉连接，不改变飞船速度或物理结构",
                cableColliderCount == 0,
                "缆线碰撞体=" + cableColliderCount),
            new AuditCheck("装饰不加入飞船碰撞结构", decorationColliders.Length == 0,
                "装饰碰撞体=" + decorationColliders.Length),
            new AuditCheck("多角度视觉样本完整", views.Count >= 8,
                "截图=" + views.Count)
        };

        var builder = new StringBuilder(2048);
        builder.AppendLine("正式星球城市 PCG 多角度自检报告");
        builder.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("范围：仅城市地形表现；未修改自然地形、任务波次和飞船模块物理结构。");
        builder.AppendLine();
        foreach (AuditCheck check in checks)
        {
            builder.Append(check.passed ? "[通过] " : "[失败] ");
            builder.Append(check.name);
            builder.Append(" —— ");
            builder.AppendLine(check.evidence);
        }
        builder.AppendLine();
        int failed = checks.Count(check => !check.passed);
        builder.AppendLine("自动结论：" + (failed == 0
            ? "全部量化规则通过，仍需逐张查看截图确认尺度感和视觉穿帮。"
            : "存在 " + failed + " 项失败，不能把当前结果视为完成。"));
        builder.AppendLine("人工自问：低空航线是否真的被楼体遮断视线？维修区是否能脱战？广告、设备、路灯是否像人摆放而不是随机撒点？");
        return builder.ToString();
    }

    static int CountBrightEmissiveMaterials(GameObject city)
    {
        var materials = new HashSet<Material>();
        Renderer[] renderers = city.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0;
             rendererIndex < renderers.Length;
             rendererIndex++)
        {
            Material[] shared = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0;
                 materialIndex < shared.Length;
                 materialIndex++)
            {
                if (shared[materialIndex] != null)
                    materials.Add(shared[materialIndex]);
            }
        }

        int bright = 0;
        foreach (Material material in materials)
        {
            if (!material.HasProperty("_EmissionColor"))
                continue;
            if (material.GetColor("_EmissionColor").maxColorComponent > 1.01f)
                bright++;
        }
        return bright;
    }

    static int CountRepresentedDistricts(IEnumerable<Transform> objects)
    {
        string[] districts =
        {
            "Commercial", "Industrial", "Transit", "Mixed", "Service"
        };
        return districts.Count(district => objects.Any(item =>
            item.name.IndexOf(district, StringComparison.Ordinal) >= 0));
    }

    static int CountRepresentedMacroFamilies(IEnumerable<Transform> objects)
    {
        string[] families =
        {
            "RoofGenerator", "RoofPipeFrame", "FacadeFireEscape",
            "FacadeBalcony", "FacadePipe", "FacadeCable", "FacadeShop",
            "StreetIndustrial", "StreetTransit", "FacadeBillboard"
        };
        return families.Count(family => objects.Any(item =>
            item.name.IndexOf(family, StringComparison.Ordinal) >= 0));
    }

    static float CalculateMaximumCentralCoverGap(
        AirCombatCityPcgLab lab,
        IReadOnlyCollection<UrbanDestructibleBuilding> buildings)
    {
        float radius = lab.Settings.ManeuverDiameter * 0.5f;
        float maximum = 0f;
        const int SamplesPerAxis = 11;
        for (int x = 0; x < SamplesPerAxis; x++)
        for (int z = 0; z < SamplesPerAxis; z++)
        {
            Vector2 point = new Vector2(
                Mathf.Lerp(-radius, radius, x / (SamplesPerAxis - 1f)),
                Mathf.Lerp(-radius, radius, z / (SamplesPerAxis - 1f)));
            if (point.magnitude > radius)
                continue;
            float nearest = float.PositiveInfinity;
            foreach (UrbanDestructibleBuilding building in buildings)
            {
                Bounds bounds = building.DestructionBounds;
                if (bounds.max.y < lab.Settings.lowAltitude + 10f)
                    continue;
                float dx = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
                float dz = Mathf.Max(bounds.min.z - point.y, 0f, point.y - bounds.max.z);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
            }
            if (!float.IsPositiveInfinity(nearest))
                maximum = Mathf.Max(maximum, nearest);
        }
        return maximum;
    }

    static float CalculateMinimumRouteClearance(
        AirCombatCityPcgLab lab,
        IReadOnlyCollection<UrbanDestructibleBuilding> buildings)
    {
        float minimum = float.PositiveInfinity;
        foreach (AirCombatFlightRoute route in lab.Plan.routes)
        {
            if (route.points == null || route.points.Length < 2)
                continue;
            // Enemy ingress is a pursuit/spawn intent line and is allowed to
            // approach occluders. Only player-readable main/flank/long-range
            // routes are required to preserve full modular-ship clearance.
            if (route.kind == AirCombatRouteKind.EnemyIngress)
                continue;
            for (int segment = 0; segment < route.points.Length - 1; segment++)
            {
                Vector3 start = route.points[segment];
                Vector3 end = route.points[segment + 1];
                int samples = Mathf.Max(2, Mathf.CeilToInt(
                    Vector3.Distance(start, end) / 32f));
                for (int sample = 0; sample <= samples; sample++)
                {
                    Vector3 point = Vector3.Lerp(start, end, sample / (float)samples);
                    foreach (UrbanDestructibleBuilding building in buildings)
                    {
                        Bounds bounds = building.DestructionBounds;
                        if (point.y < bounds.min.y - lab.Settings.wingspan * 0.5f ||
                            point.y > bounds.max.y + lab.Settings.wingspan * 0.5f)
                            continue;
                        float dx = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
                        float dz = Mathf.Max(bounds.min.z - point.z, 0f, point.z - bounds.max.z);
                        minimum = Mathf.Min(minimum, Mathf.Sqrt(dx * dx + dz * dz));
                    }
                }
            }
        }
        return float.IsPositiveInfinity(minimum) ? 9999f : minimum;
    }

    static bool FacadeAnchorFitsOwner(
        Transform anchor,
        AirCombatCityPlan plan)
    {
        AirCombatBuildingLot owner = plan.buildings.FirstOrDefault(lot =>
            anchor.name.EndsWith(lot.stableId, StringComparison.Ordinal));
        if (owner == null)
            return false;
        Quaternion inverse = Quaternion.Inverse(
            Quaternion.Euler(0f, owner.yaw, 0f));
        Vector3 local = inverse * (anchor.position - owner.center);
        bool onX = Mathf.Abs(Mathf.Abs(local.x) - owner.size.x * 0.5f) <= 4.5f &&
                   Mathf.Abs(local.z) <= owner.size.z * 0.5f + 2f;
        bool onZ = Mathf.Abs(Mathf.Abs(local.z) - owner.size.z * 0.5f) <= 4.5f &&
                   Mathf.Abs(local.x) <= owner.size.x * 0.5f + 2f;
        float bottom = owner.center.y - owner.size.y * 0.5f;
        float top = owner.center.y + owner.size.y * 0.5f;
        return (onX || onZ) && anchor.position.y >= bottom - 0.5f &&
               anchor.position.y <= top - 1f;
    }

    static bool PointInsideAnyDestructibleBuilding(
        Vector3 point,
        IReadOnlyCollection<UrbanDestructibleBuilding> buildings,
        float margin)
    {
        foreach (UrbanDestructibleBuilding building in buildings)
        {
            Bounds bounds = building.DestructionBounds;
            if (point.x > bounds.min.x - margin && point.x < bounds.max.x + margin &&
                point.z > bounds.min.z - margin && point.z < bounds.max.z + margin)
                return true;
        }
        return false;
    }

    static string BridgePairKey(UrbanDestructibleBridge bridge)
    {
        string value = bridge != null ? bridge.name : string.Empty;
        int pairStartMarker = value.IndexOf(
            '_',
            "Skybridge_".Length);
        int layerMarker = value.IndexOf(
            "_Layer",
            StringComparison.Ordinal);
        return pairStartMarker >= 0 && layerMarker > pairStartMarker
            ? value.Substring(
                pairStartMarker + 1,
                layerMarker - pairStartMarker - 1)
            : value;
    }

    static IEnumerable<float> BridgeVerticalGaps(
        IEnumerable<UrbanDestructibleBridge> bridges)
    {
        float[] heights = bridges
            .Select(bridge => bridge.DestructionBounds.center.y)
            .OrderBy(height => height)
            .ToArray();
        for (int index = 1; index < heights.Length; index++)
            yield return heights[index] - heights[index - 1];
    }

    static bool HasIrregularVerticalSpacing(
        IEnumerable<UrbanDestructibleBridge> bridges)
    {
        float[] gaps = BridgeVerticalGaps(bridges).ToArray();
        return gaps.Length >= 2 && gaps.Max() - gaps.Min() >= 1.5f;
    }

    static void CountInvalidBridgeConnections(
        IReadOnlyCollection<UrbanDestructibleBridge> bridges,
        IReadOnlyCollection<UrbanDestructibleBuilding> buildings,
        float streetPitch,
        out int invalidEndpointCount,
        out int sameBuildingCount,
        out int crossRoadBlockCount)
    {
        invalidEndpointCount = 0;
        sameBuildingCount = 0;
        crossRoadBlockCount = 0;
        streetPitch = Mathf.Max(1f, streetPitch);
        foreach (UrbanDestructibleBridge bridge in bridges)
        {
            Transform span = bridge.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name.StartsWith(
                    "BridgeSpan_",
                    StringComparison.Ordinal));
            DarkCity2AssetDescriptor descriptor = span != null
                ? span.GetComponent<DarkCity2AssetDescriptor>()
                : null;
            if (span == null || descriptor == null)
            {
                invalidEndpointCount += 2;
                continue;
            }

            Vector3 size = descriptor.AuthoredSize;
            Vector3 negativeEndpoint = span.TransformPoint(new Vector3(
                0f,
                size.y * 0.5f,
                -size.z * 0.5f));
            Vector3 positiveEndpoint = span.TransformPoint(new Vector3(
                0f,
                size.y * 0.5f,
                size.z * 0.5f));
            UrbanDestructibleBuilding negativeOwner = FindEndpointOwner(
                negativeEndpoint,
                buildings);
            UrbanDestructibleBuilding positiveOwner = FindEndpointOwner(
                positiveEndpoint,
                buildings);
            if (negativeOwner == null)
                invalidEndpointCount++;
            if (positiveOwner == null)
                invalidEndpointCount++;
            if (negativeOwner != null && ReferenceEquals(negativeOwner, positiveOwner))
                sameBuildingCount++;
            if (negativeOwner != null && positiveOwner != null &&
                !ReferenceEquals(negativeOwner, positiveOwner) &&
                RoadBlockKey(negativeOwner.DestructionBounds.center, streetPitch) !=
                RoadBlockKey(positiveOwner.DestructionBounds.center, streetPitch))
            {
                crossRoadBlockCount++;
            }
        }
    }

    static string RoadBlockKey(Vector3 center, float streetPitch)
    {
        int x = Mathf.FloorToInt(center.x / streetPitch);
        int z = Mathf.FloorToInt(center.z / streetPitch);
        return x + ":" + z;
    }

    static void CountInvalidCableConnections(
        IReadOnlyCollection<Transform> cableLinks,
        IReadOnlyCollection<UrbanDestructibleBuilding> buildings,
        out int invalidEndpointCount,
        out int sameBuildingCount,
        out int thirdBuildingIntersections,
        out int invalidLineCount)
    {
        invalidEndpointCount = 0;
        sameBuildingCount = 0;
        thirdBuildingIntersections = 0;
        invalidLineCount = 0;
        foreach (Transform link in cableLinks)
        {
            AerialCableCurve[] curves =
                link.GetComponentsInChildren<AerialCableCurve>(true);
            if (curves.Length != 3)
            {
                invalidLineCount++;
                continue;
            }

            foreach (AerialCableCurve curve in curves)
            {
                if (curve == null || curve.PointCount < 2)
                {
                    invalidLineCount++;
                    continue;
                }
                Vector3 start = curve.GetWorldPoint(0);
                Vector3 end = curve.GetWorldPoint(curve.PointCount - 1);
                UrbanDestructibleBuilding startOwner = FindEndpointOwner(
                    start,
                    buildings);
                UrbanDestructibleBuilding endOwner = FindEndpointOwner(
                    end,
                    buildings);
                if (startOwner == null)
                    invalidEndpointCount++;
                if (endOwner == null)
                    invalidEndpointCount++;
                if (startOwner != null && ReferenceEquals(startOwner, endOwner))
                    sameBuildingCount++;
                if (startOwner == null || endOwner == null)
                    continue;

                if (LineIntersectsThirdBuilding(
                        curve,
                        startOwner,
                        endOwner,
                        buildings))
                {
                    thirdBuildingIntersections++;
                }
            }
        }
    }

    static bool LineIntersectsThirdBuilding(
        AerialCableCurve curve,
        UrbanDestructibleBuilding startOwner,
        UrbanDestructibleBuilding endOwner,
        IReadOnlyCollection<UrbanDestructibleBuilding> buildings)
    {
        Vector3 previous = curve.GetWorldPoint(0);
        for (int pointIndex = 1;
             pointIndex < curve.PointCount;
             pointIndex++)
        {
            Vector3 current = curve.GetWorldPoint(pointIndex);
            foreach (UrbanDestructibleBuilding building in buildings)
            {
                if (building == null || ReferenceEquals(building, startOwner) ||
                    ReferenceEquals(building, endOwner))
                {
                    continue;
                }
                Bounds bounds = building.DestructionBounds;
                float minimumY = Mathf.Min(previous.y, current.y);
                float maximumY = Mathf.Max(previous.y, current.y);
                if (maximumY < bounds.min.y - 1f ||
                    minimumY > bounds.max.y + 1f)
                {
                    continue;
                }
                Rect rectangle = new Rect(
                    bounds.min.x - 1f,
                    bounds.min.z - 1f,
                    bounds.size.x + 2f,
                    bounds.size.z + 2f);
                if (SegmentIntersectsRectangle(
                        new Vector2(previous.x, previous.z),
                        new Vector2(current.x, current.z),
                        rectangle))
                {
                    return true;
                }
            }
            previous = current;
        }
        return false;
    }

    static bool SegmentIntersectsRectangle(
        Vector2 start,
        Vector2 end,
        Rect rectangle)
    {
        if (rectangle.Contains(start) || rectangle.Contains(end))
            return true;
        Vector2 bottomLeft = new Vector2(rectangle.xMin, rectangle.yMin);
        Vector2 bottomRight = new Vector2(rectangle.xMax, rectangle.yMin);
        Vector2 topRight = new Vector2(rectangle.xMax, rectangle.yMax);
        Vector2 topLeft = new Vector2(rectangle.xMin, rectangle.yMax);
        return SegmentCrosses(start, end, bottomLeft, bottomRight) ||
               SegmentCrosses(start, end, bottomRight, topRight) ||
               SegmentCrosses(start, end, topRight, topLeft) ||
               SegmentCrosses(start, end, topLeft, bottomLeft);
    }

    static bool SegmentCrosses(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d)
    {
        float first = Cross2D(b - a, c - a);
        float second = Cross2D(b - a, d - a);
        float third = Cross2D(d - c, a - c);
        float fourth = Cross2D(d - c, b - c);
        return first * second < -0.001f && third * fourth < -0.001f;
    }

    static float Cross2D(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    static string ConnectionZoneKey(UrbanDestructibleBuilding building)
    {
        if (building == null)
            return string.Empty;
        string objectName = building.name;
        int cluster = objectName.IndexOf("_Cluster", StringComparison.Ordinal);
        if (cluster < 0)
            return building.StableId;
        int end = objectName.IndexOf('_', cluster + 1);
        return end < 0
            ? objectName.Substring(cluster)
            : objectName.Substring(cluster, end - cluster);
    }

    static UrbanDestructibleBuilding FindEndpointOwner(
        Vector3 endpoint,
        IReadOnlyCollection<UrbanDestructibleBuilding> buildings)
    {
        foreach (UrbanDestructibleBuilding building in buildings)
        {
            if (building != null && building.DestructionBounds.Contains(endpoint))
                return building;
        }
        return null;
    }

    static bool RooftopAnchorFitsOwner(
        Transform anchor,
        AirCombatCityPlan plan)
    {
        AirCombatBuildingLot owner = plan.buildings.FirstOrDefault(lot =>
            anchor.name.EndsWith(lot.stableId, StringComparison.Ordinal));
        if (owner == null)
            return false;
        Quaternion inverse = Quaternion.Inverse(
            Quaternion.Euler(0f, owner.yaw, 0f));
        Vector3 local = inverse * (anchor.position - owner.center);
        float roof = owner.center.y + owner.size.y * 0.5f;
        return Mathf.Abs(local.x) <= owner.size.x * 0.42f &&
               Mathf.Abs(local.z) <= owner.size.z * 0.42f &&
               Mathf.Abs(anchor.position.y - roof) <= 0.25f;
    }

    static bool BillboardFacesNearestRoad(
        Transform billboard,
        AirCombatCityPlan plan)
    {
        AirCombatBuildingLot owner = plan.buildings.FirstOrDefault(lot =>
            billboard.name.EndsWith(lot.stableId, StringComparison.Ordinal));
        if (owner == null)
            return false;
        Vector2 point = new Vector2(owner.center.x, owner.center.z);
        Vector3 forward3 = billboard.forward;
        Vector2 forward = new Vector2(forward3.x, forward3.z).normalized;
        bool roadAhead = false;
        foreach (AirCombatRoadStrip road in plan.roads)
        {
            Vector2 start = new Vector2(road.start.x, road.start.z);
            Vector2 end = new Vector2(road.end.x, road.end.z);
            Vector2 segment = end - start;
            float denominator = Mathf.Max(0.0001f, segment.sqrMagnitude);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / denominator);
            Vector2 candidate = start + segment * t - point;
            if (candidate.sqrMagnitude < 0.0001f ||
                candidate.sqrMagnitude > 150f * 150f)
                continue;
            if (Vector2.Dot(forward, candidate.normalized) >= 0.70f)
            {
                roadAhead = true;
                break;
            }
        }
        return roadAhead;
    }

    static bool PointInsideAnyBuilding(
        Vector3 point,
        AirCombatCityPlan plan,
        float margin)
    {
        foreach (AirCombatBuildingLot lot in plan.buildings)
        {
            Quaternion inverse = Quaternion.Inverse(
                Quaternion.Euler(0f, lot.yaw, 0f));
            Vector3 local = inverse * (point - lot.center);
            if (Mathf.Abs(local.x) < lot.size.x * 0.5f + margin &&
                Mathf.Abs(local.z) < lot.size.z * 0.5f + margin)
            {
                return true;
            }
        }
        return false;
    }

    readonly struct AuditView
    {
        public readonly string fileName;
        public readonly Vector3 position;
        public readonly Vector3 target;
        public readonly float fieldOfView;

        public AuditView(
            string fileName,
            Vector3 position,
            Vector3 target,
            float fieldOfView)
        {
            this.fileName = fileName;
            this.position = position;
            this.target = target;
            this.fieldOfView = fieldOfView;
        }
    }

    readonly struct AuditCheck
    {
        public readonly string name;
        public readonly bool passed;
        public readonly string evidence;

        public AuditCheck(string name, bool passed, string evidence)
        {
            this.name = name;
            this.passed = passed;
            this.evidence = evidence;
        }
    }

    sealed class TargetedTestCallback : ICallbacks
    {
        readonly string outputPath;

        public TargetedTestCallback(string outputPath)
        {
            this.outputPath = outputPath;
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("AirCombatCityPcgTests 局部测试报告");
            builder.AppendLine("仅运行城市 PCG 的 9 个 EditMode 测试，未运行全量测试。");
            builder.AppendLine("通过=" + result.PassCount +
                               "，失败=" + result.FailCount +
                               "，跳过=" + result.SkipCount +
                               "，耗时=" + result.Duration.ToString("0.000") + "s");
            AppendFailures(result, builder);
            File.WriteAllText(outputPath, builder.ToString(), new UTF8Encoding(true));
            if (result.FailCount == 0)
                Debug.Log("[城市PCG局部测试] 9 个目标测试全部通过。");
            else
                Debug.LogError("[城市PCG局部测试] 有 " + result.FailCount + " 个失败，详见 " + outputPath);
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }

        static void AppendFailures(
            ITestResultAdaptor result,
            StringBuilder builder)
        {
            if (!result.HasChildren && result.FailCount > 0)
            {
                builder.AppendLine();
                builder.AppendLine("[失败] " + result.FullName);
                builder.AppendLine(result.Message);
                return;
            }
            if (!result.HasChildren)
                return;
            foreach (ITestResultAdaptor child in result.Children)
                AppendFailures(child, builder);
        }
    }
}
