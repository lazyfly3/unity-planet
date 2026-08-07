using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityPlanet.CombatMap;

/// <summary>
/// Play-mode authoring window for the finite planet combat terrain.
/// It only mutates the active runtime plan and can restore the exact plan that
/// was captured when the window attached to the battle scene.
/// </summary>
public sealed class FinitePlanetPcgTerrainTuningWindow : EditorWindow
{
    const string MenuPath = "工具/星球战斗/PCG 地形实时调试器";
    const string DebugStampPrefix = "$pcg-tool.center.";
    const string PreferencePrefix = "UnityPlanet.PcgTerrainTool.";

    sealed class Baseline
    {
        public int worldInstanceId;
        public InfinitePlanarSurfaceWorld world;
        public FinitePlanetCombatTerrainPlan terrain;
        public AirCombatMapSettings settings;
        public CombatSemanticPlan plan;
        public CombatTerrainStamp[] originalStamps;
        public float[] stampRadii;
        public float[] stampFalloffs;
        public float[] stampHeights;
        public CombatSemanticRoute[] routes;
        public float[] routeWidths;
        public float mountainHeight;
        public float mainRouteWidth;
        public float canyonRouteWidth;
        public float longRangeRouteWidth;
        public float microNoiseStrength;
        public float microNoiseScale;
    }

    struct TerrainMetrics
    {
        public float centerCoverRatio;
        public float centerMaximumHeight;
        public float coverThreshold;
    }

    Baseline baseline;
    CombatMapValidationReport validation;
    TerrainMetrics metrics;
    Vector2 scroll;
    double nextLookupAt;

    float heightMultiplier = 1.05f;
    float featureWidthMultiplier = 1f;
    float routeWidthMultiplier = 1f;
    float noiseStrength = 5f;
    float noiseScale = 80f;

    bool addCenterLandmark = true;
    float centerHeight = 230f;
    float centerWidth = 90f;
    float centerLength = 520f;
    float centerGap = 150f;
    float centerAngle = 25f;

    [MenuItem(MenuPath)]
    static void Open()
    {
        GetWindow<FinitePlanetPcgTerrainTuningWindow>(
            "PCG 地形调试器");
    }

    void OnEnable()
    {
        minSize = new Vector2(430f, 620f);
        LoadPreferences();
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.update += HandleEditorUpdate;
        TryAttachToWorld(true);
    }

    void OnDisable()
    {
        SavePreferences();
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.update -= HandleEditorUpdate;
    }

    void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            baseline = null;
            validation = null;
        }
        Repaint();
    }

    void HandleEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextLookupAt)
            return;
        nextLookupAt = EditorApplication.timeSinceStartup + 0.5d;
        TryAttachToWorld(false);
        Repaint();
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawHeader();
        EditorGUILayout.Space(6f);

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "请先进入星球战斗场景并保持 Play 模式。工具只修改当前运行中的测试地形，不会写入正式 PCG 算法。",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        TryAttachToWorld(false);
        if (!HasLiveTerrain())
        {
            EditorGUILayout.HelpBox(
                "尚未找到有限星球战斗地形。请从近轨道选择“清剿”或“设施突袭”并完成降落。",
                MessageType.Warning);
            if (GUILayout.Button("重新查找战斗地形", GUILayout.Height(30f)))
                TryAttachToWorld(true);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawLiveStatus();
        DrawPresets();
        DrawTerrainParameters();
        DrawCenterParameters();
        DrawApplyButtons();
        DrawValidation();
        DrawTestGuide();
        EditorGUILayout.EndScrollView();
    }

    static void DrawHeader()
    {
        EditorGUILayout.LabelField(
            "星球战斗 PCG 地形实时调试器",
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "用途：在战斗中比较遮挡、中心密度和飞行通道，不改正式生成文件。",
            EditorStyles.wordWrappedMiniLabel);
    }

    void DrawLiveStatus()
    {
        FinitePlanetCombatTerrainPlan terrain = baseline.terrain;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("当前战场", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("任务", terrain.MissionKind.ToString());
            EditorGUILayout.LabelField("气候", terrain.Climate.ToString());
            EditorGUILayout.LabelField("地形 Seed", terrain.Seed.ToString());
            EditorGUILayout.LabelField(
                "战斗半径 / 飞行上限",
                terrain.CombatRadius.ToString("F0") + " m / "
                + baseline.world.FiniteCombatFlightCeilingHeight.ToString("F0")
                + " m");
        }
    }

    void DrawPresets()
    {
        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("对比预设", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("推荐平衡"))
                SetPreset(0);
            if (GUILayout.Button("加强遮挡"))
                SetPreset(1);
            if (GUILayout.Button("宽阔追逐"))
                SetPreset(2);
            if (GUILayout.Button("极限校验"))
                SetPreset(3);
        }
        EditorGUILayout.HelpBox(
            "建议先用“推荐平衡”，再分别与“加强遮挡”和“宽阔追逐”各打一局。预设只填参数，点击下方“应用并重建”才会生效。",
            MessageType.None);
    }

    void DrawTerrainParameters()
    {
        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("整体地形", EditorStyles.boldLabel);
        heightMultiplier = LabeledSlider(
            "山体高度倍率",
            heightMultiplier,
            0.70f,
            1.45f,
            "推荐 0.95～1.20。过高会压缩飞行空间。");
        featureWidthMultiplier = LabeledSlider(
            "山脊宽度倍率",
            featureWidthMultiplier,
            0.75f,
            1.35f,
            "推荐 0.90～1.15。越宽，中心遮挡占地越大。");
        routeWidthMultiplier = LabeledSlider(
            "航道宽度倍率",
            routeWidthMultiplier,
            0.80f,
            1.35f,
            "推荐 0.95～1.15。必须至少容纳两倍转弯半径。");
        noiseStrength = LabeledSlider(
            "地表起伏强度（米）",
            noiseStrength,
            0f,
            12f,
            "只负责小尺度起伏，不负责战场构图。");
        noiseScale = LabeledSlider(
            "地表起伏尺度（米）",
            noiseScale,
            40f,
            180f,
            "数值越大，起伏越平缓。");
    }

    void DrawCenterParameters()
    {
        EditorGUILayout.Space(5f);
        EditorGUILayout.LabelField("中心战术地标", EditorStyles.boldLabel);
        addCenterLandmark = EditorGUILayout.ToggleLeft(
            "添加断裂式中心山脊（保留中间缺口）",
            addCenterLandmark);
        using (new EditorGUI.DisabledScope(!addCenterLandmark))
        {
            centerHeight = LabeledSlider(
                "中心山脊高度（米）",
                centerHeight,
                120f,
                290f,
                "推荐 200～250，应该进入常用战斗高度，但低于飞行上限。");
            centerWidth = LabeledSlider(
                "中心山脊宽度（米）",
                centerWidth,
                55f,
                140f,
                "推荐 80～105，过宽会形成实心墙。");
            centerLength = LabeledSlider(
                "中心山脊总长度（米）",
                centerLength,
                300f,
                650f,
                "推荐约为战斗半径的 65%～75%。");
            centerGap = LabeledSlider(
                "中央穿越缺口（米）",
                centerGap,
                90f,
                230f,
                "推荐不小于飞船转弯直径与两倍翼展中的较大值。");
            centerAngle = LabeledSlider(
                "山脊朝向（度）",
                centerAngle,
                0f,
                180f,
                "改变入口视线和绕行方向。");
            if (GUILayout.Button("换一个中心方向"))
                centerAngle = Mathf.Repeat(centerAngle + 37f, 180f);
        }
    }

    void DrawApplyButtons()
    {
        EditorGUILayout.Space(8f);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.18f, 0.82f, 0.75f);
            if (GUILayout.Button("应用参数并重建地形", GUILayout.Height(36f)))
                ApplyAndRebuild();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("还原进入战场时的地形", GUILayout.Height(36f)))
                RestoreAndRebuild();
        }
        EditorGUILayout.HelpBox(
            "重建时可能出现短暂卡顿，请在暂停战斗或敌人较少时点击。工具不会修改飞船、敌人或物理结构。",
            MessageType.Warning);
    }

    void DrawValidation()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("即时可玩性检查", EditorStyles.boldLabel);
        if (validation == null)
        {
            EditorGUILayout.HelpBox(
                "应用一次参数后会显示检查结果。",
                MessageType.Info);
            return;
        }

        Color previous = GUI.color;
        GUI.color = validation.CanCommit
            ? new Color(0.62f, 1f, 0.68f)
            : new Color(1f, 0.68f, 0.48f);
        EditorGUILayout.LabelField(
            validation.CanCommit ? "结果：通过" : "结果：需要继续调整",
            EditorStyles.boldLabel);
        GUI.color = previous;

        EditorGUILayout.LabelField(
            "总分 / 要求",
            validation.score.ToString("F1") + " / "
            + validation.minimumCommitScore.ToString("F0"));
        EditorGUILayout.LabelField(
            "中心遮挡覆盖率",
            (metrics.centerCoverRatio * 100f).ToString("F0")
            + "%（建议 25%～45%）");
        EditorGUILayout.LabelField(
            "中心最高地形",
            metrics.centerMaximumHeight.ToString("F0")
            + " m；遮挡判定线 "
            + metrics.coverThreshold.ToString("F0") + " m");
        EditorGUILayout.LabelField(
            "遮挡节奏 / 拓扑 / 机动",
            validation.coverRhythmScore.ToString("F0") + " / "
            + validation.topologyScore.ToString("F0") + " / "
            + validation.kinematicScore.ToString("F0"));
        EditorGUILayout.LabelField(
            "平均遮挡 / 最长暴露",
            validation.meanOcclusionSeconds.ToString("F1") + " s / "
            + validation.maximumExposureSeconds.ToString("F1") + " s");

        int hardErrors = CountViolations(
            validation,
            CombatMapViolationSeverity.HardError);
        int warnings = CountViolations(
            validation,
            CombatMapViolationSeverity.Warning);
        EditorGUILayout.LabelField(
            "硬错误 / 警告",
            hardErrors + " / " + warnings);

        if (validation.violations != null)
        {
            int shown = 0;
            for (int index = 0;
                 index < validation.violations.Length && shown < 5;
                 index++)
            {
                CombatMapViolation violation = validation.violations[index];
                if (violation == null)
                    continue;
                EditorGUILayout.HelpBox(
                    violation.code + "\n" + violation.message,
                    violation.severity == CombatMapViolationSeverity.HardError
                        ? MessageType.Error
                        : MessageType.Warning);
                shown++;
            }
        }

        if (GUILayout.Button("复制本次参数与检查结果"))
            CopyReport();
    }

    static void DrawTestGuide()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("建议怎么测试", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "1. Scene 窗口打开 Tools > Planet Combat > Scene PCG Overlay，确认中心山脊、缺口和三条主要航线没有重叠冲突。\n"
            + "2. 在 80～180 米高度连续飞 60 秒：中心不能一眼望穿，也不能被迫爬到山顶以上。\n"
            + "3. 分别从缺口、山脊两端和外围航道穿越，任何路线都不能只有一个出口。\n"
            + "4. 清剿模式观察敌机是否会利用遮挡接近；设施突袭观察三个目标是否都可从至少两侧进入。\n"
            + "5. 连续换 5 个地形 Seed；至少 4 个应通过硬约束，且中心覆盖率保持在 25%～45%。",
            MessageType.Info);
    }

    static float LabeledSlider(
        string label,
        float value,
        float minimum,
        float maximum,
        string hint)
    {
        float result = EditorGUILayout.Slider(label, value, minimum, maximum);
        EditorGUILayout.LabelField(hint, EditorStyles.wordWrappedMiniLabel);
        return result;
    }

    void SetPreset(int preset)
    {
        addCenterLandmark = true;
        switch (preset)
        {
            case 1:
                heightMultiplier = 1.20f;
                featureWidthMultiplier = 1.12f;
                routeWidthMultiplier = 1.02f;
                noiseStrength = 5.5f;
                noiseScale = 82f;
                centerHeight = 260f;
                centerWidth = 108f;
                centerLength = 560f;
                centerGap = 130f;
                centerAngle = 35f;
                break;
            case 2:
                heightMultiplier = 0.95f;
                featureWidthMultiplier = 0.90f;
                routeWidthMultiplier = 1.20f;
                noiseStrength = 3.5f;
                noiseScale = 105f;
                centerHeight = 190f;
                centerWidth = 78f;
                centerLength = 460f;
                centerGap = 190f;
                centerAngle = 18f;
                break;
            case 3:
                heightMultiplier = 1.38f;
                featureWidthMultiplier = 1.25f;
                routeWidthMultiplier = 0.88f;
                noiseStrength = 8f;
                noiseScale = 58f;
                centerHeight = 285f;
                centerWidth = 132f;
                centerLength = 620f;
                centerGap = 100f;
                centerAngle = 70f;
                break;
            default:
                heightMultiplier = 1.05f;
                featureWidthMultiplier = 1f;
                routeWidthMultiplier = 1.05f;
                noiseStrength = 5f;
                noiseScale = 80f;
                centerHeight = 230f;
                centerWidth = 90f;
                centerLength = 520f;
                centerGap = 150f;
                centerAngle = 25f;
                break;
        }
        SavePreferences();
    }

    void TryAttachToWorld(bool force)
    {
        if (!EditorApplication.isPlaying)
            return;
        InfinitePlanarSurfaceWorld world =
            FindObjectOfType<InfinitePlanarSurfaceWorld>();
        if (world == null || !world.IsFiniteCombatArea
            || world.FiniteCombatTerrainPlan == null
            || world.Streamer == null)
        {
            if (force)
                baseline = null;
            return;
        }
        if (baseline != null
            && baseline.worldInstanceId == world.GetInstanceID()
            && baseline.terrain == world.FiniteCombatTerrainPlan)
        {
            return;
        }
        baseline = CaptureBaseline(world);
        validation = baseline != null
            ? CombatMapValidator.Validate(
                baseline.settings,
                baseline.plan)
            : null;
        if (baseline != null)
            metrics = MeasureTerrain(baseline.terrain);
    }

    static Baseline CaptureBaseline(InfinitePlanarSurfaceWorld world)
    {
        FinitePlanetCombatTerrainPlan terrain =
            world != null ? world.FiniteCombatTerrainPlan : null;
        AirCombatMapSettings settings = terrain?.Settings;
        CombatSemanticPlan plan = terrain?.SemanticPlan;
        if (terrain == null || settings == null || plan == null)
            return null;

        var originalStamps = new List<CombatTerrainStamp>();
        if (plan.terrainStamps != null)
        {
            for (int index = 0; index < plan.terrainStamps.Length; index++)
            {
                CombatTerrainStamp stamp = plan.terrainStamps[index];
                if (stamp == null
                    || (stamp.stableId ?? string.Empty).StartsWith(
                        DebugStampPrefix,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                originalStamps.Add(stamp);
            }
        }
        plan.terrainStamps = originalStamps.ToArray();

        var result = new Baseline
        {
            worldInstanceId = world.GetInstanceID(),
            world = world,
            terrain = terrain,
            settings = settings,
            plan = plan,
            originalStamps = plan.terrainStamps,
            routes = plan.routes ?? Array.Empty<CombatSemanticRoute>(),
            mountainHeight = settings.mountainHeight,
            mainRouteWidth = settings.mainRouteWidth,
            canyonRouteWidth = settings.canyonRouteWidth,
            longRangeRouteWidth = settings.longRangeRouteWidth,
            microNoiseStrength = settings.microNoiseStrength,
            microNoiseScale = settings.microNoiseScale
        };
        result.stampRadii = new float[result.originalStamps.Length];
        result.stampFalloffs = new float[result.originalStamps.Length];
        result.stampHeights = new float[result.originalStamps.Length];
        for (int index = 0; index < result.originalStamps.Length; index++)
        {
            CombatTerrainStamp stamp = result.originalStamps[index];
            result.stampRadii[index] = stamp.radius;
            result.stampFalloffs[index] = stamp.falloff;
            result.stampHeights[index] = stamp.height;
        }
        result.routeWidths = new float[result.routes.Length];
        for (int index = 0; index < result.routes.Length; index++)
        {
            result.routeWidths[index] = result.routes[index] != null
                ? result.routes[index].width
                : 0f;
        }
        return result;
    }

    bool HasLiveTerrain()
    {
        return baseline != null
            && baseline.world != null
            && baseline.terrain != null
            && baseline.settings != null
            && baseline.plan != null
            && baseline.world.Streamer != null;
    }

    void ApplyAndRebuild()
    {
        if (!HasLiveTerrain())
            return;
        RestoreValues(false);

        AirCombatMapSettings settings = baseline.settings;
        settings.mountainHeight = Mathf.Clamp(
            baseline.mountainHeight * heightMultiplier,
            20f,
            320f);
        settings.mainRouteWidth = Mathf.Clamp(
            baseline.mainRouteWidth * routeWidthMultiplier,
            40f,
            480f);
        settings.canyonRouteWidth = Mathf.Clamp(
            baseline.canyonRouteWidth * routeWidthMultiplier,
            40f,
            480f);
        settings.longRangeRouteWidth = Mathf.Clamp(
            baseline.longRangeRouteWidth * routeWidthMultiplier,
            40f,
            520f);
        settings.microNoiseStrength = Mathf.Clamp(noiseStrength, 0f, 12f);
        settings.microNoiseScale = Mathf.Clamp(noiseScale, 40f, 180f);

        for (int index = 0; index < baseline.originalStamps.Length; index++)
        {
            CombatTerrainStamp stamp = baseline.originalStamps[index];
            if (stamp == null)
                continue;
            if (stamp.type == CombatTerrainStampType.RidgeCapsule
                || stamp.type == CombatTerrainStampType.MesaCapsule)
            {
                stamp.radius = baseline.stampRadii[index]
                    * featureWidthMultiplier;
                stamp.falloff = baseline.stampFalloffs[index]
                    * featureWidthMultiplier;
                stamp.height = baseline.stampHeights[index]
                    * heightMultiplier;
            }
            else if (stamp.type == CombatTerrainStampType.Corridor
                     || stamp.type == CombatTerrainStampType.Basin)
            {
                stamp.radius = baseline.stampRadii[index]
                    * routeWidthMultiplier;
                stamp.falloff = baseline.stampFalloffs[index]
                    * routeWidthMultiplier;
            }
        }
        for (int index = 0; index < baseline.routes.Length; index++)
        {
            if (baseline.routes[index] != null)
            {
                baseline.routes[index].width = baseline.routeWidths[index]
                    * routeWidthMultiplier;
            }
        }

        var stamps = new List<CombatTerrainStamp>(baseline.originalStamps);
        if (addCenterLandmark)
            AddCenterLandmark(stamps);
        baseline.plan.terrainStamps = stamps.ToArray();

        validation = CombatMapValidator.Validate(settings, baseline.plan);
        baseline.world.Streamer.RebuildImmediate();
        metrics = MeasureTerrain(baseline.terrain);
        SceneView.RepaintAll();
        SavePreferences();
    }

    void AddCenterLandmark(List<CombatTerrainStamp> stamps)
    {
        Vector3 center = baseline.plan.mapCenter;
        float radians = centerAngle * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(
            Mathf.Cos(radians),
            0f,
            Mathf.Sin(radians));
        float halfLength = centerLength * 0.5f;
        float halfGap = Mathf.Min(centerGap * 0.5f, halfLength - 20f);
        float radius = centerWidth * 0.5f;
        float falloff = Mathf.Max(18f, centerWidth * 0.48f);

        stamps.Add(NewStamp(
            DebugStampPrefix + "ridge.a",
            CombatTerrainStampType.RidgeCapsule,
            center - direction * halfLength,
            center - direction * halfGap,
            radius,
            falloff,
            centerHeight));
        stamps.Add(NewStamp(
            DebugStampPrefix + "ridge.b",
            CombatTerrainStampType.RidgeCapsule,
            center + direction * halfGap,
            center + direction * halfLength,
            radius,
            falloff,
            centerHeight * 0.94f));
        stamps.Add(NewStamp(
            DebugStampPrefix + "gap",
            CombatTerrainStampType.Basin,
            center,
            center,
            Mathf.Max(45f, centerGap * 0.42f),
            Mathf.Max(20f, centerGap * 0.18f),
            3f));
    }

    static CombatTerrainStamp NewStamp(
        string id,
        CombatTerrainStampType type,
        Vector3 start,
        Vector3 end,
        float radius,
        float falloff,
        float height)
    {
        return new CombatTerrainStamp
        {
            stableId = id,
            type = type,
            start = start,
            end = end,
            radius = Mathf.Max(1f, radius),
            falloff = Mathf.Max(1f, falloff),
            height = height
        };
    }

    void RestoreAndRebuild()
    {
        if (!HasLiveTerrain())
            return;
        RestoreValues(true);
        validation = CombatMapValidator.Validate(
            baseline.settings,
            baseline.plan);
        baseline.world.Streamer.RebuildImmediate();
        metrics = MeasureTerrain(baseline.terrain);
        SceneView.RepaintAll();
    }

    void RestoreValues(bool restorePlanArray)
    {
        baseline.settings.mountainHeight = baseline.mountainHeight;
        baseline.settings.mainRouteWidth = baseline.mainRouteWidth;
        baseline.settings.canyonRouteWidth = baseline.canyonRouteWidth;
        baseline.settings.longRangeRouteWidth = baseline.longRangeRouteWidth;
        baseline.settings.microNoiseStrength = baseline.microNoiseStrength;
        baseline.settings.microNoiseScale = baseline.microNoiseScale;
        for (int index = 0; index < baseline.originalStamps.Length; index++)
        {
            CombatTerrainStamp stamp = baseline.originalStamps[index];
            if (stamp == null)
                continue;
            stamp.radius = baseline.stampRadii[index];
            stamp.falloff = baseline.stampFalloffs[index];
            stamp.height = baseline.stampHeights[index];
        }
        for (int index = 0; index < baseline.routes.Length; index++)
        {
            if (baseline.routes[index] != null)
                baseline.routes[index].width = baseline.routeWidths[index];
        }
        if (restorePlanArray)
            baseline.plan.terrainStamps = baseline.originalStamps;
    }

    static TerrainMetrics MeasureTerrain(
        FinitePlanetCombatTerrainPlan terrain)
    {
        const int grid = 17;
        float radius = terrain.CombatRadius * 0.30f;
        float threshold = terrain.BaseGroundHeight + Mathf.Clamp(
            terrain.RecommendedFlightCeilingHeight * 0.30f,
            55f,
            105f);
        int inside = 0;
        int covered = 0;
        float maximum = terrain.BaseGroundHeight;
        for (int z = 0; z < grid; z++)
        for (int x = 0; x < grid; x++)
        {
            float px = Mathf.Lerp(-radius, radius, x / (grid - 1f));
            float pz = Mathf.Lerp(-radius, radius, z / (grid - 1f));
            if (px * px + pz * pz > radius * radius)
                continue;
            float height = terrain.SampleHeight(px, pz);
            inside++;
            if (height >= threshold)
                covered++;
            maximum = Mathf.Max(maximum, height);
        }
        return new TerrainMetrics
        {
            centerCoverRatio = inside > 0 ? covered / (float)inside : 0f,
            centerMaximumHeight = maximum - terrain.BaseGroundHeight,
            coverThreshold = threshold - terrain.BaseGroundHeight
        };
    }

    static int CountViolations(
        CombatMapValidationReport report,
        CombatMapViolationSeverity severity)
    {
        int result = 0;
        if (report?.violations == null)
            return result;
        for (int index = 0; index < report.violations.Length; index++)
        {
            if (report.violations[index] != null
                && report.violations[index].severity == severity)
            {
                result++;
            }
        }
        return result;
    }

    void CopyReport()
    {
        if (validation == null || !HasLiveTerrain())
            return;
        var report = new StringBuilder();
        report.AppendLine("PCG 地形测试结果");
        report.AppendLine("Seed：" + baseline.terrain.Seed);
        report.AppendLine("任务：" + baseline.terrain.MissionKind);
        report.AppendLine("山体高度倍率：" + heightMultiplier.ToString("F2"));
        report.AppendLine("山脊宽度倍率：" + featureWidthMultiplier.ToString("F2"));
        report.AppendLine("航道宽度倍率：" + routeWidthMultiplier.ToString("F2"));
        report.AppendLine("噪声：" + noiseStrength.ToString("F1")
                          + " / " + noiseScale.ToString("F0"));
        report.AppendLine("中心山脊：" + (addCenterLandmark ? "开启" : "关闭"));
        if (addCenterLandmark)
        {
            report.AppendLine("中心高度/宽度/长度/缺口/角度："
                              + centerHeight.ToString("F0") + " / "
                              + centerWidth.ToString("F0") + " / "
                              + centerLength.ToString("F0") + " / "
                              + centerGap.ToString("F0") + " / "
                              + centerAngle.ToString("F0"));
        }
        report.AppendLine("验证分数：" + validation.score.ToString("F1"));
        report.AppendLine("中心遮挡覆盖率："
                          + (metrics.centerCoverRatio * 100f).ToString("F0")
                          + "%");
        report.AppendLine("硬错误/警告："
                          + CountViolations(
                              validation,
                              CombatMapViolationSeverity.HardError)
                          + "/"
                          + CountViolations(
                              validation,
                              CombatMapViolationSeverity.Warning));
        EditorGUIUtility.systemCopyBuffer = report.ToString();
        ShowNotification(new GUIContent("测试结果已复制"));
    }

    void LoadPreferences()
    {
        heightMultiplier = EditorPrefs.GetFloat(
            PreferencePrefix + "HeightMultiplier",
            heightMultiplier);
        featureWidthMultiplier = EditorPrefs.GetFloat(
            PreferencePrefix + "FeatureWidthMultiplier",
            featureWidthMultiplier);
        routeWidthMultiplier = EditorPrefs.GetFloat(
            PreferencePrefix + "RouteWidthMultiplier",
            routeWidthMultiplier);
        noiseStrength = EditorPrefs.GetFloat(
            PreferencePrefix + "NoiseStrength",
            noiseStrength);
        noiseScale = EditorPrefs.GetFloat(
            PreferencePrefix + "NoiseScale",
            noiseScale);
        addCenterLandmark = EditorPrefs.GetBool(
            PreferencePrefix + "AddCenterLandmark",
            addCenterLandmark);
        centerHeight = EditorPrefs.GetFloat(
            PreferencePrefix + "CenterHeight",
            centerHeight);
        centerWidth = EditorPrefs.GetFloat(
            PreferencePrefix + "CenterWidth",
            centerWidth);
        centerLength = EditorPrefs.GetFloat(
            PreferencePrefix + "CenterLength",
            centerLength);
        centerGap = EditorPrefs.GetFloat(
            PreferencePrefix + "CenterGap",
            centerGap);
        centerAngle = EditorPrefs.GetFloat(
            PreferencePrefix + "CenterAngle",
            centerAngle);
    }

    void SavePreferences()
    {
        EditorPrefs.SetFloat(
            PreferencePrefix + "HeightMultiplier",
            heightMultiplier);
        EditorPrefs.SetFloat(
            PreferencePrefix + "FeatureWidthMultiplier",
            featureWidthMultiplier);
        EditorPrefs.SetFloat(
            PreferencePrefix + "RouteWidthMultiplier",
            routeWidthMultiplier);
        EditorPrefs.SetFloat(
            PreferencePrefix + "NoiseStrength",
            noiseStrength);
        EditorPrefs.SetFloat(
            PreferencePrefix + "NoiseScale",
            noiseScale);
        EditorPrefs.SetBool(
            PreferencePrefix + "AddCenterLandmark",
            addCenterLandmark);
        EditorPrefs.SetFloat(
            PreferencePrefix + "CenterHeight",
            centerHeight);
        EditorPrefs.SetFloat(
            PreferencePrefix + "CenterWidth",
            centerWidth);
        EditorPrefs.SetFloat(
            PreferencePrefix + "CenterLength",
            centerLength);
        EditorPrefs.SetFloat(
            PreferencePrefix + "CenterGap",
            centerGap);
        EditorPrefs.SetFloat(
            PreferencePrefix + "CenterAngle",
            centerAngle);
    }
}
