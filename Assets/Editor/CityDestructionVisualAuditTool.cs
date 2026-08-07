using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityPlanet.CityPcg;

/// <summary>
/// 只在临时附加场景中实例化正式城市模板，生成建筑破坏前后截图。
/// 不保存场景，不进入 StartMenu 流程，也不实例化玩家模块飞船。
/// </summary>
public static class CityDestructionVisualAuditTool
{
    const string TemplatePath =
        "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab";
    const int Width = 1280;
    const int Height = 720;

    sealed class BuildingAuditSample
    {
        public string label;
        public UrbanDestructibleBuilding building;
    }

    [MenuItem("Tools/城市 PCG/生成城市建筑破坏专项截图")]
    public static void GenerateAudit()
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(
            TemplatePath);
        if (template == null)
            throw new FileNotFoundException("找不到正式城市模板", TemplatePath);

        string outputDirectory = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "../Artifacts/CityDestructionAudit"));
        Directory.CreateDirectory(outputDirectory);

        Scene previous = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(preview);
            GameObject city = UnityEngine.Object.Instantiate(template);
            city.name = "FormalUrbanDestruction_AuditOnly";
            SceneManager.MoveGameObjectToScene(city, preview);
            city.SetActive(true);

            AirCombatCityPcgLab lab = city.GetComponent<AirCombatCityPcgLab>();
            if (lab == null)
                throw new InvalidOperationException("正式城市模板缺少 AirCombatCityPcgLab");
            lab.ConfigureRuntimeMission(7319, AirCombatCityMission.Clearance);
            if (!lab.HasValidPlan)
                throw new InvalidOperationException("正式城市 PCG 规划未通过约束校验");

            UrbanDestructibleBuilding[] buildings =
                city.GetComponentsInChildren<UrbanDestructibleBuilding>(true);
            UrbanDestructibleBuilding target = buildings
                .Where(item => item.DesignSize.y >= 120f)
                .OrderBy(item => new Vector2(
                    item.transform.position.x,
                    item.transform.position.z).sqrMagnitude)
                .ThenByDescending(item => item.DesignSize.y)
                .FirstOrDefault();
            if (target == null)
                throw new InvalidOperationException("没有找到可破坏的中高层建筑");

            UrbanDestructionCoordinator coordinator =
                city.GetComponentInChildren<UrbanDestructionCoordinator>(true);
            if (coordinator == null)
                throw new InvalidOperationException("正式城市没有建筑破坏预算协调器");

            CreateLighting(preview);
            Camera camera = CreateCamera(preview);
            Physics.SyncTransforms();
            Bounds intactBounds = target.DestructionBounds;
            Vector3 front = target.transform.forward;
            Vector3 intendedPoint = intactBounds.center +
                                    Vector3.up * (target.DesignSize.y * 0.04f);
            Vector3 viewPosition = intendedPoint + front *
                                   Mathf.Max(105f, target.DesignSize.z * 2.4f) +
                                   Vector3.up * 18f;
            Vector3 facadePoint = intendedPoint;
            Vector3 facadeNormal = front;
            Collider targetCollider = target
                .GetComponentsInChildren<Collider>(true)
                .FirstOrDefault(item => item.enabled);
            if (targetCollider != null)
            {
                Ray ray = new Ray(
                    viewPosition,
                    (intendedPoint - viewPosition).normalized);
                if (targetCollider.Raycast(ray, out RaycastHit hit, 1000f))
                {
                    facadePoint = hit.point;
                    facadeNormal = hit.normal;
                }
            }
            Vector3 viewTarget = facadePoint + Vector3.up * 2f;

            Render(
                camera,
                viewPosition,
                viewTarget,
                55f,
                outputDirectory,
                "01_正式城市完整高楼_破坏前");

            target.DebugApplyDemolition(facadePoint, facadeNormal);
            coordinator.DebugAdvanceForAudit(0.16f);
            Render(
                camera,
                viewPosition,
                viewTarget,
                55f,
                outputDirectory,
                "02_第一发破坏弹_局部炮击缺口与粉尘");
            Render(
                camera,
                Vector3.Lerp(viewPosition, facadePoint, 0.48f),
                facadePoint,
                44f,
                outputDirectory,
                "03_炮击缺口近景_焦痕冲击波与有限碎片");

            target.DebugApplyDemolition(facadePoint, facadeNormal);
            target.DebugApplyDemolition(facadePoint, facadeNormal);
            target.DebugApplyDemolition(facadePoint, facadeNormal);
            Vector3 supportPoint = facadePoint;
            supportPoint.y = intactBounds.min.y + target.DesignSize.y * 0.15f;
            for (int column = -1; column <= 1; column++)
            {
                Vector3 columnPoint = supportPoint +
                                      target.transform.right *
                                      (column * target.DesignSize.x * 0.28f);
                target.DebugApplyDemolition(columnPoint, facadeNormal);
                target.DebugApplyDemolition(columnPoint, facadeNormal);
                target.DebugApplyDemolition(columnPoint, facadeNormal);
            }
            coordinator.DebugAdvanceForAudit(1.15f);
            Render(
                camera,
                viewPosition + front * 30f + Vector3.up * 22f,
                intactBounds.center + Vector3.up * 5f,
                62f,
                outputDirectory,
                "04_承重单元失效_仅无支撑区域脱落");
            coordinator.DebugAdvanceForAudit(0.85f);
            Render(
                camera,
                intactBounds.center + new Vector3(-210f, 132f, -190f),
                intactBounds.center + Vector3.up * 28f,
                66f,
                outputDirectory,
                "05_局部破坏后全景_主体继续站立");

            int localBrokenCells = target.BrokenStructuralCells;
            int localActiveCells = target.ActiveStructuralCells;
            int localDetachedClusters = target.DetachedClusterCount;
            bool localHadDamageVisual = target.HasLocalizedDamageVisual;
            bool localHadCavityInterior = target.HasDamageCavityInterior;
            int localCavityPieces = target.DamageCavityPieceCount;
            Renderer[] localCavityRenderers = city
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.StartsWith(
                    "DamageCavityOriginalShell_",
                    StringComparison.Ordinal))
                .ToArray();
            int localLegacyFillers = city
                .GetComponentsInChildren<Renderer>(true)
                .Count(renderer =>
                    renderer.name.StartsWith("CavityLiner") ||
                    renderer.name.StartsWith("CavityOcclusionBacking"));
            bool localCavityInsideOriginalBounds =
                localCavityRenderers.Length > 0;
            foreach (Renderer renderer in localCavityRenderers)
            {
                Bounds inner = renderer.bounds;
                localCavityInsideOriginalBounds &=
                    inner.min.x > intactBounds.min.x &&
                    inner.max.x < intactBounds.max.x &&
                    inner.min.z > intactBounds.min.z &&
                    inner.max.z < intactBounds.max.z;
            }
            bool ordinaryFireCollapsedWholeBuilding = target.IsCollapsed;

            GenerateRepresentativeBuildingAudits(
                city,
                buildings,
                target,
                coordinator,
                camera,
                outputDirectory);

            Vector3 bladePoint = facadePoint;
            bladePoint.y = intactBounds.center.y + target.DesignSize.y * 0.12f;
            target.DebugApplyEnergyBlade(bladePoint, facadeNormal);
            coordinator.DebugAdvanceForAudit(1.1f);
            Render(
                camera,
                viewPosition + front * 18f + Vector3.up * 18f,
                bladePoint,
                58f,
                outputDirectory,
                "06_月牙光刃_按命中高度切开并倾倒");

            UrbanDestructibleRuinSection fallen = city
                .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
                .FirstOrDefault(section =>
                    section.name.Contains("FallenTower"));
            if (fallen != null && fallen.PersistentCollider != null)
            {
                Collider fallenCollider = fallen.PersistentCollider;
                Vector3 secondaryPoint = fallenCollider.bounds.center +
                                         facadeNormal *
                                         fallenCollider.bounds.extents.magnitude * 0.25f;
                for (int index = 0; index < 2; index++)
                {
                    UrbanDestructionWorld.TryApplyDemolition(
                        fallenCollider,
                        secondaryPoint,
                        facadeNormal,
                        -facadeNormal,
                        150f,
                        20f,
                        null);
                }
                Physics.SyncTransforms();
                Render(
                    camera,
                    secondaryPoint + facadeNormal * 86f + Vector3.up * 28f,
                    secondaryPoint,
                    52f,
                    outputDirectory,
                    "07_光刃切落楼体再次受击_生成可命中大碎块");

                UrbanDestructibleRuinSection largeChunk = city
                    .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
                    .FirstOrDefault(section =>
                        section.FractureGeneration == 1);
                if (largeChunk != null && largeChunk.PersistentCollider != null)
                {
                    for (int index = 0; index < 2; index++)
                    {
                        UrbanDestructionWorld.TryApplyDemolition(
                            largeChunk.PersistentCollider,
                            largeChunk.DestructionBounds.center,
                            facadeNormal,
                            -facadeNormal,
                            150f,
                            20f,
                            null);
                    }
                    Physics.SyncTransforms();
                }
            }

            // EditMode 不会像运行时每个 FixedUpdate 自动同步动态创建的
            // Collider；审计读取 bounds 前显式同步，避免把原点旧缓存误判为越界。
            Physics.SyncTransforms();

            string report = BuildReport(
                city,
                lab,
                target,
                coordinator,
                buildings.Length,
                intactBounds,
                facadePoint,
                facadeNormal,
                viewPosition,
                localBrokenCells,
                localActiveCells,
                localDetachedClusters,
                localHadDamageVisual,
                localHadCavityInterior,
                localCavityPieces,
                localLegacyFillers,
                localCavityInsideOriginalBounds,
                ordinaryFireCollapsedWholeBuilding);
            File.WriteAllText(
                Path.Combine(outputDirectory, "城市建筑破坏专项自检报告.txt"),
                report,
                new UTF8Encoding(true));
            AssetDatabase.Refresh();
            Debug.Log("[城市建筑破坏自检] 已生成详细破坏与多建筑对比截图：" +
                      outputDirectory);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(preview, true);
        }
    }

    static Camera CreateCamera(Scene scene)
    {
        var gameObject = new GameObject("UrbanDestructionAuditCamera");
        SceneManager.MoveGameObjectToScene(gameObject, scene);
        Camera camera = gameObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.075f, 0.095f, 0.12f, 1f);
        camera.nearClipPlane = 0.2f;
        camera.farClipPlane = 4200f;
        camera.allowHDR = true;
        return camera;
    }

    static void GenerateRepresentativeBuildingAudits(
        GameObject city,
        UrbanDestructibleBuilding[] buildings,
        UrbanDestructibleBuilding detailedTarget,
        UrbanDestructionCoordinator coordinator,
        Camera camera,
        string outputDirectory)
    {
        var samples = new System.Collections.Generic.List<BuildingAuditSample>(4);
        AddDistinctSample(
            samples,
            "低矮宽体",
            buildings
                .Where(item => item != detailedTarget && item.DesignSize.y <= 90f)
                .OrderByDescending(item =>
                    item.DesignSize.x * item.DesignSize.z /
                    Mathf.Max(1f, item.DesignSize.y))
                .FirstOrDefault());
        AddDistinctSample(
            samples,
            "中层多部件",
            buildings
                .Where(item => item != detailedTarget &&
                               item.DesignSize.y > 70f &&
                               item.DesignSize.y < 145f)
                .OrderByDescending(item =>
                    item.GetComponentsInChildren<MeshRenderer>(true).Length)
                .ThenByDescending(item => item.DesignSize.y)
                .FirstOrDefault());
        AddDistinctSample(
            samples,
            "高层细塔",
            buildings
                .Where(item => item != detailedTarget && item.DesignSize.y >= 130f)
                .OrderByDescending(item =>
                    item.DesignSize.y /
                    Mathf.Max(1f, Mathf.Min(
                        item.DesignSize.x,
                        item.DesignSize.z)))
                .FirstOrDefault());
        AddDistinctSample(
            samples,
            "高层宽塔",
            buildings
                .Where(item => item != detailedTarget && item.DesignSize.y >= 105f)
                .OrderByDescending(item =>
                    item.DesignSize.x * item.DesignSize.z)
                .FirstOrDefault());

        for (int index = 0; index < samples.Count; index++)
        {
            BuildingAuditSample sample = samples[index];
            UrbanDestructibleBuilding building = sample.building;
            Bounds bounds = building.DestructionBounds;
            Vector3 viewAxis = index % 2 == 0
                ? building.transform.forward
                : building.transform.right;
            if (viewAxis.sqrMagnitude < 0.001f)
                viewAxis = Vector3.forward;
            viewAxis.Normalize();
            Vector3 intendedPoint = bounds.center +
                                    Vector3.up * bounds.size.y * 0.035f;
            float distance = Mathf.Max(
                72f,
                Mathf.Max(bounds.size.x, bounds.size.y) * 1.18f);
            Vector3 viewPosition = intendedPoint + viewAxis * distance +
                                   Vector3.up * Mathf.Clamp(
                                       bounds.size.y * 0.08f,
                                       5f,
                                       18f);
            Vector3 hitPoint = intendedPoint;
            Vector3 hitNormal = viewAxis;
            Collider collider = building
                .GetComponentsInChildren<Collider>(true)
                .FirstOrDefault(item => item.enabled);
            if (collider != null)
            {
                Ray ray = new Ray(
                    viewPosition,
                    (intendedPoint - viewPosition).normalized);
                if (collider.Raycast(ray, out RaycastHit hit, distance * 2f))
                {
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                }
            }

            string prefix = (8 + index).ToString("D2") + "_" + sample.label;
            Render(
                camera,
                viewPosition,
                bounds.center,
                48f,
                outputDirectory,
                prefix + "_破坏前");
            building.DebugApplyDemolition(hitPoint, hitNormal);
            coordinator.DebugAdvanceForAudit(0.14f);
            Render(
                camera,
                Vector3.Lerp(viewPosition, hitPoint, 0.43f),
                hitPoint,
                44f,
                outputDirectory,
                prefix + "_单次命中后");
        }
    }

    static void AddDistinctSample(
        System.Collections.Generic.List<BuildingAuditSample> samples,
        string label,
        UrbanDestructibleBuilding candidate)
    {
        if (candidate == null || samples.Any(item => item.building == candidate))
            return;
        samples.Add(new BuildingAuditSample
        {
            label = label,
            building = candidate
        });
    }

    static void CreateLighting(Scene scene)
    {
        CreateDirectional(
            scene,
            "DestructionKey",
            new Vector3(43f, -34f, 0f),
            new Color(1f, 0.88f, 0.72f),
            1.45f);
        CreateDirectional(
            scene,
            "DestructionFill",
            new Vector3(24f, 148f, 8f),
            new Color(0.32f, 0.54f, 0.82f),
            0.62f);
    }

    static void CreateDirectional(
        Scene scene,
        string name,
        Vector3 euler,
        Color color,
        float intensity)
    {
        var gameObject = new GameObject(name);
        SceneManager.MoveGameObjectToScene(gameObject, scene);
        gameObject.transform.rotation = Quaternion.Euler(euler);
        Light light = gameObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.Soft;
    }

    static void Render(
        Camera camera,
        Vector3 position,
        Vector3 target,
        float fieldOfView,
        string outputDirectory,
        string fileName)
    {
        camera.transform.position = position;
        camera.transform.LookAt(target, Vector3.up);
        camera.fieldOfView = fieldOfView;
        RenderTexture renderTexture = RenderTexture.GetTemporary(
            Width,
            Height,
            24,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(
                Path.Combine(outputDirectory, fileName + ".png"),
                image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            if (image != null)
                UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    static string BuildReport(
        GameObject city,
        AirCombatCityPcgLab lab,
        UrbanDestructibleBuilding target,
        UrbanDestructionCoordinator coordinator,
        int destructibleCount,
        Bounds originalBounds,
        Vector3 facadePoint,
        Vector3 facadeNormal,
        Vector3 viewPosition,
        int localBrokenCells,
        int localActiveCells,
        int localDetachedClusters,
        bool localHadDamageVisual,
        bool localHadCavityInterior,
        int localCavityPieces,
        int localLegacyFillers,
        bool localCavityInsideOriginalBounds,
        bool ordinaryFireCollapsedWholeBuilding)
    {
        MeshCollider[] runtimeMeshColliders = city
            .GetComponentsInChildren<MeshCollider>(true)
            .Where(item => item.GetComponentInParent<UrbanDestructibleBuilding>() != null ||
                           item.GetComponentInParent<UrbanDebrisPiece>() != null)
            .ToArray();
        UrbanDebrisPiece[] debris =
            city.GetComponentsInChildren<UrbanDebrisPiece>(true);
        Renderer[] detachedOriginalFacades = city
            .GetComponentsInChildren<Renderer>(true)
            .Where(item => item.name.StartsWith(
                "DetachedOriginalFacade_",
                StringComparison.Ordinal))
            .ToArray();
        Renderer[] obsoleteCellBlocks = city
            .GetComponentsInChildren<Renderer>(true)
            .Where(item => item.name.StartsWith(
                "ConnectedCell_",
                StringComparison.Ordinal))
            .ToArray();
        UrbanDestructibleRuinSection[] structuralClusters = city
            .GetComponentsInChildren<UrbanDestructibleRuinSection>(true)
            .Where(item => item.name.Contains("UnsupportedStructuralCluster"))
            .ToArray();
        int maximumClusterColliderCount = structuralClusters.Length == 0
            ? 0
            : structuralClusters.Max(item =>
                item.GetComponents<BoxCollider>().Length);
        Transform ruin = city.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item =>
                item.name.StartsWith("Ruin_" + target.StableId,
                    StringComparison.Ordinal));
        UrbanTopplingSection toppling = ruin == null
            ? null
            : ruin.GetComponentInChildren<UrbanTopplingSection>(true);
        BoxCollider[] retainedColliders = ruin == null
            ? Array.Empty<BoxCollider>()
            : ruin.GetComponentsInChildren<BoxCollider>(true)
                .Where(item => item.enabled)
                .ToArray();
        UrbanDestructibleRuinSection[] ruinSections = ruin == null
            ? Array.Empty<UrbanDestructibleRuinSection>()
            : ruin.GetComponentsInChildren<UrbanDestructibleRuinSection>(true);
        int largeRecursiveChunks = ruinSections.Count(section =>
            section.FractureGeneration == 1 &&
            section.PersistentCollider != null &&
            section.PersistentCollider.enabled);
        int mediumRecursiveChunks = ruinSections.Count(section =>
            section.FractureGeneration == 2 &&
            section.PersistentCollider != null &&
            section.PersistentCollider.enabled);
        float maximumLocalX = 0f;
        float maximumLocalZ = 0f;
        bool retainedInsideFootprint = true;
        foreach (BoxCollider collider in retainedColliders)
        {
            Vector3 local = Quaternion.Inverse(target.transform.rotation) *
                            (collider.bounds.center - originalBounds.center);
            maximumLocalX = Mathf.Max(maximumLocalX, Mathf.Abs(local.x));
            maximumLocalZ = Mathf.Max(maximumLocalZ, Mathf.Abs(local.z));
            retainedInsideFootprint &=
                Mathf.Abs(local.x) <= target.DesignSize.x * 0.5f +
                                      target.DesignSize.y + 4f &&
                Mathf.Abs(local.z) <= target.DesignSize.z * 0.5f +
                                      target.DesignSize.y + 4f;
        }
        bool originalHidden = target.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.GetComponentInParent<UrbanDebrisPiece>() == null)
            .All(renderer => !renderer.enabled);

        var builder = new StringBuilder(1600);
        builder.AppendLine("正式城市建筑破坏专项自检报告");
        builder.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("范围：仅城市建筑与装饰；未运行 StartMenu、自然地形或玩家模块全量测试。");
        builder.AppendLine("受击审计：点=" + facadePoint.ToString("F2") +
                           "，法线=" + facadeNormal.ToString("F3") +
                           "，面向相机点积=" +
                           Vector3.Dot(
                               facadeNormal.normalized,
                               (viewPosition - facadePoint).normalized).ToString("0.000"));
        Renderer[] impactVisuals = coordinator
            .GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.name.Contains("UrbanBreach") ||
                               renderer.name.Contains("UrbanDustVolume") ||
                               renderer.name.Contains("PooledUrbanDebris"))
            .Take(12)
            .ToArray();
        builder.AppendLine("受击可视对象=" + impactVisuals.Length);
        foreach (Renderer visual in impactVisuals)
        {
            builder.AppendLine("  " + visual.name +
                               " enabled=" + visual.enabled +
                               " active=" + visual.gameObject.activeInHierarchy +
                               " center=" + visual.bounds.center.ToString("F1") +
                               " size=" + visual.bounds.size.ToString("F1"));
        }
        builder.AppendLine();
        AppendCheck(builder, "正式城市 PCG 约束仍通过", lab.HasValidPlan,
            lab.LastSummary);
        AppendCheck(builder, "所有正式楼体均获得可破坏组件",
            destructibleCount >= lab.Plan.buildings.Count,
            destructibleCount + "/" + lab.Plan.buildings.Count);
        AppendCheck(builder, "光刃切落后完整楼体已隐藏，无新旧模型穿插",
            originalHidden,
            "原楼渲染器全部关闭=" + originalHidden);
        AppendCheck(builder, "普通炮击只破坏局部结构单元，不触发整楼倾倒",
            !ordinaryFireCollapsedWholeBuilding &&
            localBrokenCells > 0 &&
            localActiveCells > 0,
            "普通炮击后整楼倒塌=" + ordinaryFireCollapsedWholeBuilding +
            "，破坏单元=" + localBrokenCells +
            "，剩余单元=" + localActiveCells);
        AppendCheck(builder, "承重缺失后仅生成无支撑连通块",
            localDetachedClusters > 0,
            "局部脱落连通块=" + localDetachedClusters);
        AppendCheck(builder, "局部缺口材质已生效",
            localHadDamageVisual,
            "DamageMode 副本=" + localHadDamageVisual);
        AppendCheck(builder, "破口内层来自受击建筑自身，不使用统一黄棕色柱体",
            localHadCavityInterior &&
            localCavityPieces > 0 &&
            localLegacyFillers == 0,
            "自身内缩 Mesh=" + localCavityPieces +
            "，旧柱体/衬层=" + localLegacyFillers);
        AppendCheck(builder, "连续破坏后建筑可见内层仍完全位于原包围盒内",
            localCavityInsideOriginalBounds,
            "内缩层未扩大建筑=" + localCavityInsideOriginalBounds);
        AppendCheck(builder, "失稳块继承原楼外壳，不再出现纯色大方块",
            detachedOriginalFacades.Length > 0 && obsoleteCellBlocks.Length == 0,
            "原楼裁片=" + detachedOriginalFacades.Length +
            "，旧纯色单元=" + obsoleteCellBlocks.Length);
        AppendCheck(builder, "失稳块碰撞使用受限复合轮廓",
            structuralClusters.Length > 0 &&
            maximumClusterColliderCount >= 1 &&
            maximumClusterColliderCount <= 8,
            "失稳块=" + structuralClusters.Length +
            "，单块最大盒数=" + maximumClusterColliderCount);
        AppendCheck(builder, "月牙光刃才按实际命中高度切开楼体",
            toppling != null && target.LastCollapseWasEnergyBlade,
            "记录切口Y/物理切口Y=" +
            target.LastCollapseCutHeight.ToString("0.00") + "/" +
            (toppling != null ? toppling.CutHeight.ToString("0.00") : "无"));
        AppendCheck(builder, "光刃切落部分保留可读倾倒阶段",
            toppling != null && toppling.CurrentAngle >= 12f,
            "专项推进后的倾角=" +
            (toppling != null ? toppling.CurrentAngle.ToString("0.0") : "无"));
        AppendCheck(builder, "没有运行时 MeshCollider 烹饪",
            runtimeMeshColliders.Length == 0,
            "可破坏层 MeshCollider=" + runtimeMeshColliders.Length);
        AppendCheck(builder, "物理碎片未超过全局预算",
            coordinator.ActivePhysicalDebris <= coordinator.MaximumPhysicalDebris,
            coordinator.ActivePhysicalDebris + "/" + coordinator.MaximumPhysicalDebris);
        AppendCheck(builder, "残骸碰撞保持在受控倒塌范围",
            retainedColliders.Length > 0 && retainedInsideFootprint,
            "残骸碰撞=" + retainedColliders.Length +
            "，中心最大本地X/Z=" + maximumLocalX.ToString("0.00") + "/" +
            maximumLocalZ.ToString("0.00") +
            "，受控倒塌包络=" +
            (target.DesignSize.x * 0.5f + target.DesignSize.y + 4f)
                .ToString("0.00") + "/" +
            (target.DesignSize.z * 0.5f + target.DesignSize.y + 4f)
                .ToString("0.00") +
            "，楼旋转=" + target.transform.eulerAngles.ToString("F1") +
            "，原中心=" + originalBounds.center.ToString("F1") +
            "，残骸中心=" + (retainedColliders.Length > 0
                ? retainedColliders[0].bounds.center.ToString("F1")
                : "无"));
        AppendCheck(builder, "碎片数量为受控规模",
            debris.Length <= 96,
            "碎片池对象=" + debris.Length);
        AppendCheck(builder, "倒地楼体可继续破裂为带碰撞的大碎块",
            largeRecursiveChunks >= 3,
            "第一代可破坏碎块=" + largeRecursiveChunks);
        AppendCheck(builder, "大碎块可再次破裂且递归层级有上限",
            mediumRecursiveChunks >= 2 && mediumRecursiveChunks <= 6,
            "第二代可破坏碎块=" + mediumRecursiveChunks + "，允许=2..6");
        builder.AppendLine();
        builder.AppendLine("规则结论：普通火炮只在命中处破坏外墙、立柱和核心单元；上方单元只有在显式支撑图判定失稳后才按连通块脱落，整楼不会被一发或数发炮弹当作单刚体推倒。月牙光刃保留为特殊规则，才按实际命中高度切开楼体。所有倒地块保留低成本盒碰撞并可继续破裂，递归止于第二代，避免碎片指数增长和 MeshCollider 重烹饪。");
        return builder.ToString();
    }

    static void AppendCheck(
        StringBuilder builder,
        string name,
        bool passed,
        string evidence)
    {
        builder.Append(passed ? "[通过] " : "[失败] ");
        builder.Append(name);
        builder.Append(" —— ");
        builder.AppendLine(evidence);
    }
}
