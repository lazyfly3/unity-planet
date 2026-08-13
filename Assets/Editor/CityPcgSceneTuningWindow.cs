using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityPlanet.CityPcg;

/// <summary>
/// 面向关卡美术的城市参数调节器。它只管理独立的编辑器预览城市，
/// 不修改目标场景中原有的环境陷阱、敌机、相机或运行逻辑。
/// </summary>
public sealed class CityPcgSceneTuningWindow : EditorWindow
{
    public const string TargetScenePath =
        "Assets/Scenes/UrbanEnvironmentalTrapTest.unity";

    const string TemplatePath =
        "Assets/Resources/PlanetSurface/UrbanCombatCityTemplate.prefab";
    const string PreviewRootName = "城市程序化生成预览_仅编辑模式";
    const string TerrainObjectName = "城市地形_真实可碰撞地形";
    const string TerrainFolder = "Assets/CityPcgPrinciplesLab/ScenePreview";
    const string TerrainDataPath =
        TerrainFolder + "/UrbanEnvironmentalTrapPreviewTerrain.asset";

    static readonly string[] MissionNames =
    {
        "城市清剿",
        "设施突袭",
        "首领遭遇"
    };

    AirCombatCityPcgLab cityGenerator;
    Vector2 scroll;
    bool showFlightEnvelope = true;
    bool showHeightAndDensity = true;
    bool showSkybridges = true;
    bool showAerialCables = true;
    bool showEnvironmentalTraps = true;
    bool showDifficulty = true;
    bool showDisplayOptions;
    bool parametersChanged;
    AirCombatCityPlan cachedTrapReportPlan;
    bool cachedWindTrapAvailable;
    int cachedWindTrapCount;
    Vector3 cachedWindTrapPosition;

    [MenuItem("Tools/城市 PCG/城市参数调节器", false, 1)]
    public static void OpenWindow()
    {
        CityPcgSceneTuningWindow window =
            GetWindow<CityPcgSceneTuningWindow>("城市参数调节器");
        window.minSize = new Vector2(420f, 560f);
        window.Show();
    }

    void OnEnable()
    {
        FindPreviewCity();
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }

    void OnHierarchyChanged()
    {
        FindPreviewCity();
        Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField(
            "城市程序化生成参数",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "这里调节的是独立编辑器预览城市。原有环境陷阱、敌机循环、相机和物理逻辑不会被修改；进入播放模式时，预览城市与预览地形会自动隐藏。",
            MessageType.Info);

        Scene activeScene = SceneManager.GetActiveScene();
        EditorGUILayout.LabelField(
            "当前场景",
            string.IsNullOrEmpty(activeScene.path)
                ? "未保存场景"
                : activeScene.path);

        if (activeScene.path != TargetScenePath)
        {
            EditorGUILayout.HelpBox(
                "请先打开指定的城市环境陷阱场景。",
                MessageType.Warning);
            if (GUILayout.Button("打开城市环境陷阱场景", GUILayout.Height(30f)))
                OpenTargetScene();
            return;
        }

        if (cityGenerator == null)
        {
            EditorGUILayout.HelpBox(
                "场景中还没有独立城市预览。",
                MessageType.Warning);
            if (GUILayout.Button("在当前场景搭建城市与地形", GUILayout.Height(34f)))
            {
                cityGenerator = EnsurePreviewInActiveScene(true);
                parametersChanged = false;
            }
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawSettings();
        DrawActions();
        DrawReport();
        EditorGUILayout.EndScrollView();
    }

    void DrawSettings()
    {
        SerializedObject serialized = new SerializedObject(cityGenerator);
        serialized.Update();
        SerializedProperty settings = serialized.FindProperty("settings");
        if (settings == null)
        {
            EditorGUILayout.HelpBox("找不到城市参数数据。", MessageType.Error);
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("基础生成参数", EditorStyles.boldLabel);
        DrawSeed(settings, "seed", "城市种子");
        DrawMission(settings.FindPropertyRelative("mission"));
        DrawFloat(settings, "mapSize", "城市边长", "米", 1200f, 4000f);
        DrawPositiveInt(settings, "maximumAttempts", "最大候选次数", 64);
        DrawToggle(settings, "useVisualDistrictThemes", "启用城市美术分区");

        showHeightAndDensity = EditorGUILayout.Foldout(
            showHeightAndDensity,
            "建筑高度与密度",
            true);
        if (showHeightAndDensity)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "尺寸和高度只保留下限，不限制最高输入值；重建时仍会自动保证低层、中层、高层和最高战斗高度依次递增。",
                MessageType.None);
            DrawFloat(settings, "lowAltitude", "低空层高度", "米", 30f, 300f);
            DrawFloat(settings, "mediumAltitude", "中空层高度", "米", 70f, 600f);
            DrawFloat(settings, "highAltitude", "高空层高度", "米", 120f, 1000f);
            DrawFloat(settings, "maximumAltitude", "最高战斗高度", "米", 180f, 1500f);
            DrawFloat(settings, "buildingSpacing", "建筑采样间距", "米", 48f, 300f);
            DrawFloat(settings, "buildingDensity", "建筑密度倍率", "倍", 0.50f, 2f);
            SerializedProperty density =
                settings.FindPropertyRelative("buildingDensity");
            if (density != null && density.floatValue > 1f)
            {
                EditorGUILayout.HelpBox(
                    "超过 1.0 后会继续增加可用地块和填充建筑。数值越高，生成时间、渲染器与建筑碰撞体数量也会增加。",
                    MessageType.Warning);
            }
            EditorGUI.indentLevel--;
        }

        showFlightEnvelope = EditorGUILayout.Foldout(
            showFlightEnvelope,
            "飞行与战斗包线",
            true);
        if (showFlightEnvelope)
        {
            EditorGUI.indentLevel++;
            DrawFloat(settings, "combatSpeed", "设计交战速度", "米/秒", 20f, 400f);
            DrawFloat(settings, "turnRadius", "设计转弯半径", "米", 40f, 600f);
            DrawFloat(settings, "weaponRange", "武器有效射程", "米", 120f, 3000f);
            DrawFloat(settings, "wingspan", "参考机体宽度", "米", 4f, 250f);
            EditorGUI.indentLevel--;
        }

        showDifficulty = EditorGUILayout.Foldout(
            showDifficulty,
            "战术难度参数",
            true);
        if (showDifficulty)
        {
            EditorGUI.indentLevel++;
            SerializedProperty difficulty =
                settings.FindPropertyRelative("combatDifficulty");
            if (difficulty != null)
            {
                EditorGUILayout.LabelField(
                    "城市地形难度（不修改敌人）",
                    EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(
                    "这里只控制可见城市空间：飞行净空、开放暴露区、恢复空间、主次道路和街区连通。敌人数量、敌人入口、增援与首领参数暂不在这个试验场中调整。",
                    MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("应用简单地形"))
                        ApplyTerrainDifficultyPreset(settings, false);
                    if (GUILayout.Button("应用困难地形"))
                        ApplyTerrainDifficultyPreset(settings, true);
                }
                DrawSlider(difficulty, "navigationChallenge", "飞行净空压力", 0f, 1f);
                DrawSlider(difficulty, "exposurePressure", "开放暴露区强度", 0f, 1f);
                DrawSlider(difficulty, "recoveryGenerosity", "恢复宽容度", 0f, 1f);
                DrawSlider(difficulty, "routeLegibility", "主次路线可读性", 0f, 1f);
                DrawSlider(difficulty, "roadWidthScale", "道路实体宽度", 0.7f, 1.4f);
                DrawSlider(difficulty, "blockMergeStrength", "街区合并强度", 0f, 1f);
            }

        showSkybridges = EditorGUILayout.Foldout(
            showSkybridges,
            "连廊难度（空间阻挡）",
            true);
        if (showSkybridges)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "区块按最终合并后的街区组判定：拆除内部道路并合并的街区视为同一区块，其内部连廊不会算作跨区块。两类目标分别满足，不能互相补足。",
                MessageType.None);
            EditorGUILayout.LabelField("普通任务连廊数量", EditorStyles.miniBoldLabel);
            DrawNonNegativeInt(
                settings,
                "intraBlockSkybridgeTarget",
                "区块内连廊目标",
                200);
            DrawNonNegativeInt(
                settings,
                "crossBlockSkybridgeTarget",
                "跨区块连廊目标",
                200);
            EditorGUILayout.LabelField("Boss任务连廊数量", EditorStyles.miniBoldLabel);
            DrawNonNegativeInt(
                settings,
                "bossIntraBlockSkybridgeTarget",
                "Boss区块内目标",
                300);
            DrawNonNegativeInt(
                settings,
                "bossCrossBlockSkybridgeTarget",
                "Boss跨区块目标",
                300);
            EditorGUILayout.LabelField("候选与连接规则", EditorStyles.miniBoldLabel);
            DrawFloat(
                settings,
                "skybridgeMinimumCenterDistance",
                "建筑中心最小距离",
                "米",
                1f,
                120f);
            DrawFloat(
                settings,
                "skybridgeMaximumCenterDistance",
                "建筑中心最大距离",
                "米",
                2f,
                500f);
            DrawFloat(
                settings,
                "skybridgeMinimumHeight",
                "普通连廊最低高度",
                "米",
                1f,
                200f);
            DrawFloat(
                settings,
                "skybridgeFacadeEmbed",
                "嵌入建筑深度",
                "米",
                0f,
                30f);
            DrawPositiveInt(
                settings,
                "skybridgeMaximumSegmentCount",
                "普通连廊最大段数",
                6);
            EditorGUILayout.HelpBox(
                "连廊优先使用单段；单段跨度不适配时，才会在模型安全拉伸范围内自动拼接。提高段数会增加跨区块候选，也会增加桥身网格、碰撞体和渲染压力。输入值不设人为上限。",
                MessageType.None);
            DrawPositiveInt(
                settings,
                "skybridgeMaximumConnectionsPerBuilding",
                "普通建筑连接上限",
                16);
            DrawPositiveInt(
                settings,
                "skybridgeLandmarkMaximumConnections",
                "地标建筑连接上限",
                24);
            int ordinaryTotal = ReadInt(settings, "intraBlockSkybridgeTarget") +
                                ReadInt(settings, "crossBlockSkybridgeTarget");
            int bossTotal = ReadInt(settings, "bossIntraBlockSkybridgeTarget") +
                            ReadInt(settings, "bossCrossBlockSkybridgeTarget");
            if (ordinaryTotal > AirCombatCityPcgLab.MinimumCitySkybridges ||
                bossTotal > AirCombatCityPcgLab.BossCitySkybridgeTarget)
            {
                EditorGUILayout.HelpBox(
                    "连廊目标超过正式默认预算。候选搜索、连廊渲染器和破坏连接数量都会增加；候选不足时会显示规划未通过，但仍会生成可用结果。",
                    MessageType.Warning);
            }
            EditorGUI.indentLevel--;
        }

        showAerialCables = EditorGUILayout.Foldout(
            showAerialCables,
            "电线难度（减速障碍）",
            true);
        if (showAerialCables)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("电线生成", EditorStyles.miniBoldLabel);
            DrawFloat(
                settings,
                "aerialCableDensityMultiplier",
                "电线密度倍率",
                "倍",
                0f,
                3f);
            DrawNonNegativeInt(
                settings,
                "aerialCableMinimumCount",
                "最少电线束数",
                48);
            DrawNonNegativeInt(
                settings,
                "aerialCableMaximumCount",
                "最多电线束数",
                96);
            DrawFloat(
                settings,
                "aerialCableMinimumCenterDistance",
                "建筑中心最小距离",
                "米",
                1f,
                180f);
            DrawFloat(
                settings,
                "aerialCableMaximumCenterDistance",
                "建筑中心最大距离",
                "米",
                2f,
                600f);
            DrawFloat(
                settings,
                "aerialCableMinimumOpenSpan",
                "电线最小净跨度",
                "米",
                1f,
                180f);
            DrawFloat(
                settings,
                "aerialCableMaximumOpenSpan",
                "电线最大净跨度",
                "米",
                2f,
                500f);
            DrawFloat(
                settings,
                "aerialCableSagRatio",
                "下垂比例",
                "倍",
                0f,
                0.2f);
            DrawFloat(
                settings,
                "aerialCableMinimumSag",
                "最小下垂",
                "米",
                0f,
                30f);
            DrawFloat(
                settings,
                "aerialCableMaximumSag",
                "最大下垂",
                "米",
                0f,
                60f);
            DrawFloat(
                settings,
                "aerialCableVerticalSeparation",
                "三线垂直间距",
                "米",
                0f,
                12f);
            DrawPositiveInt(
                settings,
                "aerialCableMaximumConnectionsPerBuilding",
                "普通建筑连接上限",
                12);
            DrawPositiveInt(
                settings,
                "aerialCableLandmarkMaximumConnections",
                "地标建筑连接上限",
                16);

            EditorGUILayout.LabelField("触碰减速", EditorStyles.miniBoldLabel);
            DrawSlider(
                settings,
                "playerCableSlowdown",
                "玩家减速比例",
                0f,
                0.9f);
            DrawSlider(
                settings,
                "enemyCableSlowdown",
                "小怪减速比例",
                0f,
                0.9f);
            DrawFloat(
                settings,
                "cableMinimumAffectedSpeed",
                "生效最低速度",
                "米/秒",
                0f,
                80f);
            DrawFloat(
                settings,
                "cableRepeatCooldown",
                "重复触发间隔",
                "秒",
                0.05f,
                2f);
            DrawFloat(
                settings,
                "cableTriggerRadius",
                "触发粗细",
                "米",
                0.05f,
                4f);
            DrawRangedInt(
                settings,
                "cableTriggerSegmentsPerCurve",
                "每根线触发分段",
                1,
                16);

            EditorGUILayout.LabelField("撞线晃动", EditorStyles.miniBoldLabel);
            DrawFloat(
                settings,
                "playerCableSwayAmplitude",
                "玩家撞线晃动幅度",
                "米",
                0f,
                4f);
            DrawFloat(
                settings,
                "enemyCableSwayAmplitude",
                "小怪撞线晃动幅度",
                "米",
                0f,
                4f);
            DrawFloat(
                settings,
                "cableSwayDuration",
                "晃动持续时间",
                "秒",
                0.05f,
                4f);

            float cableMultiplier = ReadFloat(
                settings,
                "aerialCableDensityMultiplier");
            int cableMaximum = ReadInt(settings, "aerialCableMaximumCount");
            int triggerSegments = ReadInt(
                settings,
                "cableTriggerSegmentsPerCurve");
            float estimatedTriggers = cableMaximum * cableMultiplier *
                                      triggerSegments * 3f;
            if (estimatedTriggers > 468f)
            {
                EditorGUILayout.HelpBox(
                    "当前上限预计最多生成约 " +
                    Mathf.CeilToInt(estimatedTriggers) +
                    " 个静态触发段。它们不会阻挡飞船，但会增加物理宽相检测与场景层级数量。",
                    MessageType.Warning);
            }
            EditorGUI.indentLevel--;
        }

            EditorGUI.indentLevel--;
        }

        showEnvironmentalTraps = EditorGUILayout.Foldout(
            showEnvironmentalTraps,
            "环境陷阱分布",
            true);
        if (showEnvironmentalTraps)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "数量至少为 1，输入值不设人为上限；实际数量仍受合法街区和风道候选限制。城市会始终保留两个基础恢复庭院维持战术结构，磁场数量为 1 时另一个庭院只是普通掩体，不会通电。",
                MessageType.None);
            EditorGUILayout.LabelField("陷阱数量", EditorStyles.miniBoldLabel);
            DrawPositiveInt(
                settings,
                "naturalStreetGaleCount",
                "自然风场数量",
                8);
            DrawPositiveInt(
                settings,
                "magneticCourtyardCount",
                "磁场庭院数量",
                8);
            int requestedTrapCount = ReadInt(
                                         settings,
                                         "naturalStreetGaleCount") +
                                     ReadInt(
                                         settings,
                                         "magneticCourtyardCount");
            if (requestedTrapCount > 6)
            {
                EditorGUILayout.HelpBox(
                    "当前请求超过 6 个实战环境陷阱。每个风场包含触发体和粒子层，每个磁场庭院包含三面连续碰撞代理、磁墙节点与灯光；数量越高，运行时物理宽相和透明特效压力越大。",
                    MessageType.Warning);
            }
            EditorGUILayout.LabelField("位置规则", EditorStyles.miniBoldLabel);
            EditorGUILayout.HelpBox(
                "磁场庭院只会落在能容纳三面实体磁墙的合法街区，风场仍需满足连续道路、侧向入口和末端撞击面。随机性由城市种子决定：同一种子可复现，换种子才会改变位置。",
                MessageType.None);
            DrawSlider(
                settings,
                "environmentalTrapRandomness",
                "位置随机性",
                0f,
                1f);
            DrawSlider(
                settings,
                "environmentalTrapEdgeBias",
                "外圈倾向",
                0f,
                1f);
            DrawFloat(
                settings,
                "environmentalTrapPreferredMinimumRadius",
                "首选距城市中心最小距离",
                "米",
                0f,
                1000f);
            DrawFloat(
                settings,
                "environmentalTrapPreferredMaximumRadius",
                "首选距城市中心最大距离",
                "米",
                1f,
                1600f);
            DrawFloat(
                settings,
                "environmentalTrapEdgeClearance",
                "距城市边缘留白",
                "米",
                0f,
                600f);
            DrawFloat(
                settings,
                "environmentalTrapMinimumSeparation",
                "陷阱之间首选间距",
                "米",
                0f,
                1200f);
            EditorGUI.indentLevel--;
        }

        showDisplayOptions = EditorGUILayout.Foldout(
            showDisplayOptions,
            "场景显示选项",
            true);
        if (showDisplayOptions)
        {
            EditorGUI.indentLevel++;
            DrawToggle(serialized, "keepBuildingColliders", "保留建筑碰撞体");
            DrawToggle(serialized, "showSemanticGizmos", "显示战术辅助标记");
            DrawToggle(serialized, "showRuntimePanel", "显示运行时调试面板");
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle("生成城外视觉背景", false);
            EditorGUILayout.HelpBox(
                "该场景固定关闭城外低模背景，只保留正式城市、道路和真实地形。",
                MessageType.None);
            EditorGUI.indentLevel--;
        }

        if (serialized.ApplyModifiedProperties())
        {
            parametersChanged = true;
            EditorUtility.SetDirty(cityGenerator);
        }
    }

    void DrawActions()
    {
        EditorGUILayout.Space(8f);
        if (parametersChanged)
        {
            EditorGUILayout.HelpBox(
                "参数已经改变。点击下方按钮后，场景中的城市才会按新参数完整重建。",
                MessageType.Warning);
        }

        if (GUILayout.Button("应用参数并重建城市", GUILayout.Height(36f)))
        {
            RebuildAndSave();
            parametersChanged = false;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("恢复正式默认参数"))
            {
                RestoreDefaults();
                RebuildAndSave();
                parametersChanged = false;
            }
            if (GUILayout.Button("在场景中定位城市"))
                FocusCity();
        }
    }

    void DrawReport()
    {
        AirCombatCityReport report = cityGenerator.Report;
        if (report == null)
            return;

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("当前生成结果", EditorStyles.boldLabel);
        int lowCount = Mathf.Max(
            0,
            report.buildingCount - report.mediumBuildingCount -
            report.highBuildingCount);
        EditorGUILayout.LabelField("规划状态", report.valid ? "有效" : "未通过");
        EditorGUILayout.LabelField("实际使用种子", report.resolvedSeed.ToString());
        EditorGUILayout.LabelField("规划建筑数量", report.buildingCount.ToString());
        EditorGUILayout.LabelField("低层建筑", lowCount.ToString());
        EditorGUILayout.LabelField("中层建筑", report.mediumBuildingCount.ToString());
        EditorGUILayout.LabelField("高层建筑", report.highBuildingCount.ToString());
        EditorGUILayout.LabelField("最高塔楼高度", report.maximumTowerHeight.ToString("0.0") + " 米");
        int crossBlockBridges = cityGenerator.LastCrossRoadBlockSkybridgeCount;
        int intraBlockBridges = Mathf.Max(
            0,
            cityGenerator.LastSkybridgeCount - crossBlockBridges);
        EditorGUILayout.LabelField("实际区块内连廊", intraBlockBridges.ToString());
        EditorGUILayout.LabelField("实际跨区块连廊", crossBlockBridges.ToString());
        EditorGUILayout.LabelField(
            "区块内连廊候选",
            cityGenerator.LastIntraBlockSkybridgeCandidateCount.ToString());
        EditorGUILayout.LabelField(
            "跨区块连廊候选",
            cityGenerator.LastCrossBlockSkybridgeCandidateCount.ToString());
        int requestedIntraBlockBridges = cityGenerator.Settings.mission ==
                                         AirCombatCityMission.BossEncounter
            ? cityGenerator.Settings.bossIntraBlockSkybridgeTarget
            : cityGenerator.Settings.intraBlockSkybridgeTarget;
        int requestedCrossBlockBridges = cityGenerator.Settings.mission ==
                                         AirCombatCityMission.BossEncounter
            ? cityGenerator.Settings.bossCrossBlockSkybridgeTarget
            : cityGenerator.Settings.crossBlockSkybridgeTarget;
        if (intraBlockBridges < requestedIntraBlockBridges ||
            crossBlockBridges < requestedCrossBlockBridges)
        {
            EditorGUILayout.HelpBox(
                "请求的连廊配额超过本次布局可实现的最大合法数量，已按最大配额生成：区块内 " +
                intraBlockBridges + "/" + requestedIntraBlockBridges +
                "，跨区块 " + crossBlockBridges + "/" +
                requestedCrossBlockBridges + "。两类不会互相补足。",
                MessageType.Warning);
        }
        EditorGUILayout.LabelField(
            "实际电线束数",
            cityGenerator.LastAerialCableCount.ToString());
        EditorGUILayout.LabelField(
            "可用电线候选",
            cityGenerator.LastAerialCableCandidateCount.ToString());
        DrawEnvironmentalTrapReport(cityGenerator.Plan, cityGenerator.Settings);
        EditorGUILayout.LabelField("道路宽度范围",
            report.minimumRoadWidth.ToString("0.0") + "－" +
            report.maximumRoadWidth.ToString("0.0") + " 米");
        EditorGUILayout.LabelField("城外视觉背景", "关闭");
        EditorGUILayout.LabelField("播放模式隔离", "已启用，不干扰原有陷阱");

        if (!report.valid)
            EditorGUILayout.HelpBox(report.failureReason, MessageType.Error);
    }

    void RebuildAndSave()
    {
        if (cityGenerator == null)
            return;

        Undo.RecordObject(cityGenerator, "调整城市生成参数");
        CopySettings(
            cityGenerator.Settings.ValidatedCopy(),
            cityGenerator.Settings);
        ForcePreviewOptions(cityGenerator);
        cityGenerator.Rebuild();
        EnsureTerrain(cityGenerator.transform, cityGenerator.Settings.mapSize);
        EditorUtility.SetDirty(cityGenerator);
        Scene scene = cityGenerator.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
        Repaint();
    }

    void RestoreDefaults()
    {
        if (cityGenerator == null)
            return;
        Undo.RecordObject(cityGenerator, "恢复城市正式默认参数");
        CopySettings(new AirCombatCitySettings(), cityGenerator.Settings);
        EditorUtility.SetDirty(cityGenerator);
    }

    void FocusCity()
    {
        if (cityGenerator == null)
            return;
        Selection.activeGameObject = cityGenerator.gameObject;
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.pivot = new Vector3(0f, 130f, 0f);
            view.size = cityGenerator.Settings.mapSize * 0.72f;
            view.rotation = Quaternion.Euler(34f, 42f, 0f);
            view.Repaint();
        }
    }

    void FocusNaturalWindPreview()
    {
        if (cityGenerator == null || !cachedWindTrapAvailable)
            return;
        Vector3 worldPosition = cityGenerator.transform.TransformPoint(
            cachedWindTrapPosition);
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.pivot = worldPosition + Vector3.up * 58f;
            view.size = 230f;
            view.rotation = Quaternion.Euler(31f, 38f, 0f);
            view.Repaint();
        }

        Transform previewRoot = cityGenerator.transform.Find(
            "Generated_AirCombatCity_空战语义先行/" +
            "04D_环境陷阱预览_仅视觉无物理");
        if (previewRoot != null && previewRoot.childCount > 0)
            Selection.activeGameObject = previewRoot.GetChild(0).gameObject;
    }

    void FindPreviewCity()
    {
        cityGenerator = null;
        Scene activeScene = SceneManager.GetActiveScene();
        AirCombatCityPcgLab[] generators =
            Object.FindObjectsOfType<AirCombatCityPcgLab>(true);
        for (int index = 0; index < generators.Length; index++)
        {
            AirCombatCityPcgLab candidate = generators[index];
            if (candidate.gameObject.scene != activeScene)
                continue;
            if (candidate.GetComponent<CityPcgEditorPreviewOnly>() == null &&
                candidate.gameObject.name != PreviewRootName)
            {
                continue;
            }
            cityGenerator = candidate;
            return;
        }
    }

    static void OpenTargetScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
    }

    /// <summary>
    /// Used by the menu window and by editor automation to build the same
    /// isolated preview without touching the trap harness.
    /// </summary>
    public static AirCombatCityPcgLab EnsurePreviewInActiveScene(bool saveScene)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != TargetScenePath)
        {
            Debug.LogError("请先打开城市环境陷阱场景：" + TargetScenePath);
            return null;
        }

        AirCombatCityPcgLab generator = null;
        AirCombatCityPcgLab[] existing =
            Object.FindObjectsOfType<AirCombatCityPcgLab>(true);
        for (int index = 0; index < existing.Length; index++)
        {
            if (existing[index].gameObject.scene == scene &&
                (existing[index].GetComponent<CityPcgEditorPreviewOnly>() != null ||
                 existing[index].gameObject.name == PreviewRootName))
            {
                generator = existing[index];
                break;
            }
        }

        bool created = false;
        if (generator == null)
        {
            GameObject template =
                AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            if (template == null)
            {
                Debug.LogError("找不到正式城市模板：" + TemplatePath);
                return null;
            }

            GameObject preview = PrefabUtility.InstantiatePrefab(
                template,
                scene) as GameObject;
            if (preview == null)
                return null;
            PrefabUtility.UnpackPrefabInstance(
                preview,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            preview.name = PreviewRootName;
            preview.tag = "EditorOnly";
            preview.AddComponent<CityPcgEditorPreviewOnly>();
            generator = preview.GetComponent<AirCombatCityPcgLab>();
            ForcePreviewOptions(generator);
            preview.SetActive(true);
            created = true;
        }

        if (generator == null)
            return null;

        ForcePreviewOptions(generator);
        if (!created)
            generator.Rebuild();
        EnsureTerrain(generator.transform, generator.Settings.mapSize);
        EditorUtility.SetDirty(generator);
        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene)
        {
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        Selection.activeGameObject = generator.gameObject;
        SceneView.RepaintAll();
        return generator;
    }

    static void ForcePreviewOptions(AirCombatCityPcgLab generator)
    {
        if (generator == null)
            return;
        SerializedObject serialized = new SerializedObject(generator);
        serialized.Update();
        SetBool(serialized, "buildVisualBackground", false);
        SetBool(serialized, "showRuntimePanel", false);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        generator.Settings.removeUpperParameterLimits = true;
        EditorUtility.SetDirty(generator);
    }

    static void EnsureTerrain(Transform previewRoot, float mapSize)
    {
        EnsureFolder("Assets", "CityPcgPrinciplesLab");
        EnsureFolder("Assets/CityPcgPrinciplesLab", "ScenePreview");
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(
            TerrainDataPath);
        if (data == null)
        {
            data = new TerrainData
            {
                heightmapResolution = 65,
                baseMapResolution = 64,
                alphamapResolution = 64
            };
            data.SetHeights(0, 0, new float[65, 65]);
            AssetDatabase.CreateAsset(data, TerrainDataPath);
        }

        float terrainSize = Mathf.Clamp(mapSize * 1.5f, 1800f, 3900f);
        data.size = new Vector3(terrainSize, 120f, terrainSize);

        Transform terrainTransform = previewRoot.Find(TerrainObjectName);
        Terrain terrain;
        if (terrainTransform == null)
        {
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = TerrainObjectName;
            terrainObject.tag = "EditorOnly";
            terrainObject.transform.SetParent(previewRoot, false);
            terrain = terrainObject.GetComponent<Terrain>();
        }
        else
        {
            terrain = terrainTransform.GetComponent<Terrain>();
            if (terrain != null)
                terrain.terrainData = data;
            TerrainCollider collider =
                terrainTransform.GetComponent<TerrainCollider>();
            if (collider != null)
                collider.terrainData = data;
        }

        if (terrain == null)
            return;
        terrain.transform.localPosition = new Vector3(
            -terrainSize * 0.5f,
            -0.12f,
            -terrainSize * 0.5f);
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 12f;
        terrain.basemapDistance = 1000f;
        terrain.shadowCastingMode = ShadowCastingMode.Off;
        terrain.gameObject.isStatic = true;
        terrain.Flush();
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
    }

    static void CopySettings(
        AirCombatCitySettings source,
        AirCombatCitySettings destination)
    {
        destination.seed = source.seed;
        destination.mission = source.mission;
        destination.mapSize = source.mapSize;
        destination.combatSpeed = source.combatSpeed;
        destination.turnRadius = source.turnRadius;
        destination.weaponRange = source.weaponRange;
        destination.wingspan = source.wingspan;
        destination.lowAltitude = source.lowAltitude;
        destination.mediumAltitude = source.mediumAltitude;
        destination.highAltitude = source.highAltitude;
        destination.maximumAltitude = source.maximumAltitude;
        destination.buildingSpacing = source.buildingSpacing;
        destination.buildingDensity = source.buildingDensity;
        destination.maximumAttempts = source.maximumAttempts;
        destination.intraBlockSkybridgeTarget =
            source.intraBlockSkybridgeTarget;
        destination.crossBlockSkybridgeTarget =
            source.crossBlockSkybridgeTarget;
        destination.bossIntraBlockSkybridgeTarget =
            source.bossIntraBlockSkybridgeTarget;
        destination.bossCrossBlockSkybridgeTarget =
            source.bossCrossBlockSkybridgeTarget;
        destination.skybridgeMinimumCenterDistance =
            source.skybridgeMinimumCenterDistance;
        destination.skybridgeMaximumCenterDistance =
            source.skybridgeMaximumCenterDistance;
        destination.skybridgeMinimumHeight = source.skybridgeMinimumHeight;
        destination.skybridgeFacadeEmbed = source.skybridgeFacadeEmbed;
        destination.skybridgeMaximumSegmentCount =
            source.skybridgeMaximumSegmentCount;
        destination.skybridgeMaximumConnectionsPerBuilding =
            source.skybridgeMaximumConnectionsPerBuilding;
        destination.skybridgeLandmarkMaximumConnections =
            source.skybridgeLandmarkMaximumConnections;
        destination.aerialCableDensityMultiplier =
            source.aerialCableDensityMultiplier;
        destination.aerialCableMinimumCount = source.aerialCableMinimumCount;
        destination.aerialCableMaximumCount = source.aerialCableMaximumCount;
        destination.aerialCableMinimumCenterDistance =
            source.aerialCableMinimumCenterDistance;
        destination.aerialCableMaximumCenterDistance =
            source.aerialCableMaximumCenterDistance;
        destination.aerialCableMinimumOpenSpan =
            source.aerialCableMinimumOpenSpan;
        destination.aerialCableMaximumOpenSpan =
            source.aerialCableMaximumOpenSpan;
        destination.aerialCableSagRatio = source.aerialCableSagRatio;
        destination.aerialCableMinimumSag = source.aerialCableMinimumSag;
        destination.aerialCableMaximumSag = source.aerialCableMaximumSag;
        destination.aerialCableMaximumConnectionsPerBuilding =
            source.aerialCableMaximumConnectionsPerBuilding;
        destination.aerialCableLandmarkMaximumConnections =
            source.aerialCableLandmarkMaximumConnections;
        destination.aerialCableVerticalSeparation =
            source.aerialCableVerticalSeparation;
        destination.cableTriggerRadius = source.cableTriggerRadius;
        destination.cableTriggerSegmentsPerCurve =
            source.cableTriggerSegmentsPerCurve;
        destination.cableMinimumAffectedSpeed =
            source.cableMinimumAffectedSpeed;
        destination.playerCableSlowdown = source.playerCableSlowdown;
        destination.enemyCableSlowdown = source.enemyCableSlowdown;
        destination.cableRepeatCooldown = source.cableRepeatCooldown;
        destination.playerCableSwayAmplitude =
            source.playerCableSwayAmplitude;
        destination.enemyCableSwayAmplitude = source.enemyCableSwayAmplitude;
        destination.cableSwayDuration = source.cableSwayDuration;
        destination.naturalStreetGaleCount = source.naturalStreetGaleCount;
        destination.magneticCourtyardCount = source.magneticCourtyardCount;
        destination.environmentalTrapRandomness =
            source.environmentalTrapRandomness;
        destination.environmentalTrapEdgeBias =
            source.environmentalTrapEdgeBias;
        destination.environmentalTrapPreferredMinimumRadius =
            source.environmentalTrapPreferredMinimumRadius;
        destination.environmentalTrapPreferredMaximumRadius =
            source.environmentalTrapPreferredMaximumRadius;
        destination.environmentalTrapEdgeClearance =
            source.environmentalTrapEdgeClearance;
        destination.environmentalTrapMinimumSeparation =
            source.environmentalTrapMinimumSeparation;
        destination.useVisualDistrictThemes = source.useVisualDistrictThemes;
        destination.removeUpperParameterLimits =
            source.removeUpperParameterLimits;
        destination.combatDifficulty = source.Difficulty.ValidatedCopy();
    }

    void DrawEnvironmentalTrapReport(
        AirCombatCityPlan plan,
        AirCombatCitySettings settings)
    {
        if (plan == null || settings == null)
            return;

        int recoveryPocketCount = 0;
        for (int index = 0; index < plan.volumes.Count; index++)
        {
            AirCombatTacticalVolume volume = plan.volumes[index];
            if (volume != null &&
                volume.kind == AirCombatVolumeKind.RecoveryPocket)
            {
                recoveryPocketCount++;
            }
        }
        int magneticIndex = 0;
        int plannedMagneticCount = Mathf.Min(
            Mathf.Max(1, settings.magneticCourtyardCount),
            recoveryPocketCount);
        EditorGUILayout.LabelField(
            "计划磁场陷阱数量",
            plannedMagneticCount.ToString());
        for (int index = 0; index < plan.volumes.Count; index++)
        {
            AirCombatTacticalVolume volume = plan.volumes[index];
            if (volume == null ||
                volume.kind != AirCombatVolumeKind.RecoveryPocket)
            {
                continue;
            }
            Vector2 point = new Vector2(volume.center.x, volume.center.z);
            bool energized = magneticIndex < plannedMagneticCount;
            EditorGUILayout.LabelField(
                (energized ? "磁场庭院 " : "普通恢复庭院 ") +
                (++magneticIndex),
                "(" + point.x.ToString("0") + ", " +
                point.y.ToString("0") + ")，距中心 " +
                point.magnitude.ToString("0") + " 米");
        }

        if (!ReferenceEquals(cachedTrapReportPlan, plan))
        {
            cachedTrapReportPlan = plan;
            cachedWindTrapCount =
                UrbanEnvironmentalFieldDirector.ResolvePlannedWindTrapCount(
                    plan,
                    settings.ValidatedCopy(),
                    out cachedWindTrapPosition);
            cachedWindTrapAvailable = cachedWindTrapCount > 0;
        }
        EditorGUILayout.LabelField(
            "计划自然风场数量",
            cachedWindTrapCount.ToString());
        if (cachedWindTrapAvailable)
        {
            Vector2 point = new Vector2(
                cachedWindTrapPosition.x,
                cachedWindTrapPosition.z);
            EditorGUILayout.LabelField(
                "自然风场",
                "(" + point.x.ToString("0") + ", " +
                point.y.ToString("0") + ")，距中心 " +
                point.magnitude.ToString("0") + " 米");
            if (GUILayout.Button("在场景中定位自然风场"))
                FocusNaturalWindPreview();
        }
        else
        {
            EditorGUILayout.LabelField("自然风场", "当前候选不足");
        }
    }

    static void DrawMission(SerializedProperty property)
    {
        if (property == null)
            return;
        property.enumValueIndex = EditorGUILayout.Popup(
            "任务类型",
            Mathf.Clamp(property.enumValueIndex, 0, MissionNames.Length - 1),
            MissionNames);
    }

    static void DrawSeed(
        SerializedProperty parent,
        string propertyName,
        string label)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property == null)
            return;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            property.intValue = EditorGUILayout.DelayedIntField(
                property.intValue);
            if (GUILayout.Button(
                    new GUIContent(
                        "随机",
                        "生成一个新的随机城市种子；仍需点击“应用参数并重建城市”。"),
                    GUILayout.Width(64f)))
            {
                int randomSeed = System.Guid.NewGuid().GetHashCode() &
                                 int.MaxValue;
                property.intValue = randomSeed == 0 ? 1 : randomSeed;
                GUI.FocusControl(null);
            }
        }
    }

    static void DrawPositiveInt(
        SerializedProperty parent,
        string propertyName,
        string label,
        int suggestedMaximum)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property == null)
            return;

        int current = Mathf.Max(1, property.intValue);
        int dynamicMaximum = Mathf.Max(
            suggestedMaximum,
            Mathf.CeilToInt(current * 1.35f));
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            EditorGUI.BeginChangeCheck();
            int dragged = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                current,
                1f,
                dynamicMaximum));
            bool draggedChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            int typed = EditorGUILayout.DelayedIntField(
                current,
                GUILayout.Width(72f));
            bool typedChanged = EditorGUI.EndChangeCheck();
            property.intValue = Mathf.Max(
                1,
                typedChanged ? typed : draggedChanged ? dragged : current);
        }
    }

    static void DrawNonNegativeInt(
        SerializedProperty parent,
        string propertyName,
        string label,
        int suggestedMaximum)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property == null)
            return;

        int current = Mathf.Max(0, property.intValue);
        int dynamicMaximum = Mathf.Max(
            suggestedMaximum,
            Mathf.CeilToInt(current * 1.35f),
            1);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            EditorGUI.BeginChangeCheck();
            int dragged = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                current,
                0f,
                dynamicMaximum));
            bool draggedChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            int typed = EditorGUILayout.DelayedIntField(
                current,
                GUILayout.Width(72f));
            bool typedChanged = EditorGUI.EndChangeCheck();
            property.intValue = Mathf.Max(
                0,
                typedChanged ? typed : draggedChanged ? dragged : current);
        }
    }

    static void DrawRangedInt(
        SerializedProperty parent,
        string propertyName,
        string label,
        int minimum,
        int maximum)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property == null)
            return;
        property.intValue = EditorGUILayout.IntSlider(
            label,
            property.intValue,
            minimum,
            maximum);
    }

    static int ReadInt(SerializedProperty parent, string propertyName)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        return property != null ? property.intValue : 0;
    }

    static float ReadFloat(SerializedProperty parent, string propertyName)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        return property != null ? property.floatValue : 0f;
    }

    static void DrawFloat(
        SerializedProperty parent,
        string propertyName,
        string label,
        string unit,
        float minimum,
        float suggestedMaximum)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property == null)
            return;

        float current = Mathf.Max(minimum, property.floatValue);
        float dynamicMaximum = Mathf.Max(
            suggestedMaximum,
            current * 1.35f,
            minimum + 1f);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(
                new GUIContent(label + "（" + unit + "）"));
            EditorGUI.BeginChangeCheck();
            float dragged = GUILayout.HorizontalSlider(
                current,
                minimum,
                dynamicMaximum);
            bool draggedChanged = EditorGUI.EndChangeCheck();
            EditorGUI.BeginChangeCheck();
            float typed = EditorGUILayout.DelayedFloatField(
                current,
                GUILayout.Width(72f));
            bool typedChanged = EditorGUI.EndChangeCheck();
            property.floatValue = Mathf.Max(
                minimum,
                typedChanged ? typed : draggedChanged ? dragged : current);
        }
    }

    static void DrawSlider(
        SerializedProperty parent,
        string propertyName,
        string label,
        float minimum,
        float maximum)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.floatValue = EditorGUILayout.Slider(label, property.floatValue, minimum, maximum);
    }

    static void ApplyTerrainDifficultyPreset(
        SerializedProperty settings,
        bool difficult)
    {
        SerializedProperty difficulty =
            settings.FindPropertyRelative("combatDifficulty");
        if (difficulty == null)
            return;

        SetFloat(
            difficulty,
            "navigationChallenge",
            difficult ? 0.78f : 0.20f);
        SetFloat(
            difficulty,
            "exposurePressure",
            difficult ? 0.78f : 0.20f);
        SetFloat(
            difficulty,
            "recoveryGenerosity",
            difficult ? 0.35f : 0.85f);
        SetFloat(
            difficulty,
            "routeLegibility",
            difficult ? 0.58f : 0.92f);
        SetFloat(
            difficulty,
            "roadWidthScale",
            difficult ? 0.86f : 1.08f);
        SetFloat(
            difficulty,
            "blockMergeStrength",
            difficult ? 0.78f : 0.10f);

        // These are physical city obstacles.  The preset changes their amount,
        // but deliberately leaves player/enemy slowdown physics untouched.
        SetFloat(settings, "buildingDensity", difficult ? 1.02f : 0.84f);
        SetInt(settings, "intraBlockSkybridgeTarget", difficult ? 130 : 84);
        SetInt(settings, "crossBlockSkybridgeTarget", difficult ? 22 : 8);
        SetInt(settings, "bossIntraBlockSkybridgeTarget", difficult ? 190 : 120);
        SetInt(settings, "bossCrossBlockSkybridgeTarget", difficult ? 30 : 12);
        SetFloat(
            settings,
            "aerialCableDensityMultiplier",
            difficult ? 1.10f : 0.45f);
        SetInt(settings, "aerialCableMinimumCount", difficult ? 18 : 6);
        SetInt(settings, "aerialCableMaximumCount", difficult ? 28 : 12);
    }

    static void DrawToggle(
        SerializedProperty parent,
        string propertyName,
        string label)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.boolValue = EditorGUILayout.Toggle(label, property.boolValue);
    }

    static void DrawToggle(
        SerializedObject serialized,
        string propertyName,
        string label)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.boolValue = EditorGUILayout.Toggle(label, property.boolValue);
    }

    static void SetBool(
        SerializedObject serialized,
        string propertyName,
        bool value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    static void SetFloat(
        SerializedProperty parent,
        string propertyName,
        float value)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    static void SetInt(
        SerializedProperty parent,
        string propertyName,
        int value)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            property.intValue = value;
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, name);
    }
}
