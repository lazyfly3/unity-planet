using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.CombatMap.Editor
{
    /// <summary>
    /// Authoring, candidate comparison and semantic diagnostics for the
    /// finite low-altitude duel arena.
    /// </summary>
    public sealed class CombatMapLabWindow : EditorWindow
    {
        Component controller;
        AirCombatMapRecipe recipe;
        SerializedObject serializedRecipe;
        Vector2 scroll;
        int previewSeed = 7319;
        int selectedCandidate;
        string status = "打开实验场后即可生成候选。";

        bool showRoutes = true;
        bool showSpawns = true;
        bool showSightlines = true;
        bool showBoundary = true;
        bool showViolations = true;

        readonly List<CombatMapGenerationResult> candidates =
            new List<CombatMapGenerationResult>();
        readonly List<int> candidateApplySeeds =
            new List<int>();
        int? previousAppliedSeed;
        int currentAppliedSeed = 7319;

        [MenuItem("Tools/Combat Map/Combat Map Lab Window")]
        public static void OpenWindow()
        {
            if (!CombatMapLabAssetBuilder.OpenLaboratoryScene())
                return;

            CombatMapLabWindow window =
                GetWindow<CombatMapLabWindow>();
            window.titleContent =
                new GUIContent("战斗地图实验室");
            window.minSize = new Vector2(500f, 680f);
            window.BindScene();
            window.Show();
        }

        [MenuItem("Tools/战斗地图/打开战斗地图实验室窗口")]
        static void OpenChineseWindow()
        {
            OpenWindow();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("战斗地图实验室");
            minSize = new Vector2(500f, 680f);
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged -=
                OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged +=
                OnPlayModeStateChanged;
            SceneView.duringSceneGui -= DuringSceneGui;
            SceneView.duringSceneGui += DuringSceneGui;
            BindScene();
        }

        void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorApplication.playModeStateChanged -=
                OnPlayModeStateChanged;
            SceneView.duringSceneGui -= DuringSceneGui;
        }

        void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.path == CombatMapLabAssetBuilder.ScenePath)
                BindScene();
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode
                || state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += BindScene;
            }
            Repaint();
        }

        void BindScene()
        {
            controller = CombatMapLabAssetBuilder.FindController();
            AirCombatMapRecipe bound =
                CombatMapLabAssetBuilder.ReadControllerMember(
                    controller,
                    "Recipe",
                    "MapRecipe",
                    "recipe",
                    "mapRecipe") as AirCombatMapRecipe;
            SetRecipe(
                bound != null
                    ? bound
                    : CombatMapLabAssetBuilder
                        .EnsureSemanticV2Recipe());
            RefreshCandidatesFromController();
            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime != null && runtime.Recipe != null)
                currentAppliedSeed = runtime.Recipe.Seed;
            Repaint();
            SceneView.RepaintAll();
        }

        void SetRecipe(AirCombatMapRecipe value)
        {
            recipe = value;
            serializedRecipe = recipe != null
                ? new SerializedObject(recipe)
                : null;
            if (recipe != null)
                previewSeed = recipe.Seed;
        }

        void OnGUI()
        {
            DrawHeader();

            if (SceneManager.GetActiveScene().path
                != CombatMapLabAssetBuilder.ScenePath)
            {
                EditorGUILayout.HelpBox(
                    "当前不是战斗地图实验场。工具不会修改其他场景。",
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(
                           EditorApplication
                               .isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button(
                        "打开战斗地图实验场",
                        GUILayout.Height(32f)))
                    {
                        CombatMapLabAssetBuilder
                            .OpenLaboratoryScene();
                        BindScene();
                    }
                }
                return;
            }

            if (controller == null)
                BindScene();
            if (controller == null)
            {
                EditorGUILayout.HelpBox(
                    "场景中缺少战斗地图运行时控制器。",
                    MessageType.Error);
                if (GUILayout.Button("重建实验场"))
                {
                    CombatMapLabAssetBuilder
                        .RebuildLaboratoryAssets();
                    BindScene();
                }
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawRecipe();
            DrawGenerationControls();
            DrawCandidates();
            DrawOverlayControls();
            DrawDiagnostics();
            EditorGUILayout.EndScrollView();
            DrawBottomToolbar();
        }

        void DrawHeader()
        {
            Rect rect =
                EditorGUILayout.GetControlRect(false, 44f);
            EditorGUI.DrawRect(
                rect,
                new Color(0.055f, 0.075f, 0.095f));
            GUI.Label(
                new Rect(
                    rect.x + 12f,
                    rect.y + 5f,
                    rect.width - 24f,
                    22f),
                "战斗地图实验室 / 语义优先 PCG",
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16,
                    normal =
                    {
                        textColor =
                            new Color(0.45f, 0.88f, 1f)
                    }
                });
            GUI.Label(
                new Rect(
                    rect.x + 12f,
                    rect.y + 25f,
                    rect.width - 24f,
                    17f),
                "先生成战斗决策，再生成地形外观",
                EditorStyles.miniLabel);
        }

        void DrawRecipe()
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "地图配方参数",
                EditorStyles.boldLabel);

            AirCombatMapRecipe picked =
                (AirCombatMapRecipe)EditorGUILayout.ObjectField(
                    "地图配方",
                    recipe,
                    typeof(AirCombatMapRecipe),
                    false);
            if (picked != recipe)
            {
                Undo.RecordObject(
                    controller,
                    "切换战斗地图配方");
                CombatMapLabAssetBuilder.AssignRecipe(
                    controller,
                    picked);
                SetRecipe(picked);
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(
                    controller.gameObject.scene);
            }

            if (serializedRecipe == null)
            {
                EditorGUILayout.HelpBox(
                    "请选择一个空战地图配方资产。",
                    MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            serializedRecipe.Update();
            SerializedProperty iterator =
                serializedRecipe.GetIterator();
            bool enterChildren = true;
            EditorGUI.BeginChangeCheck();
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyPath == "m_Script")
                    continue;
                EditorGUILayout.PropertyField(
                    iterator,
                    new GUIContent(
                        RecipePropertyLabel(iterator.propertyPath)),
                    true);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(
                    recipe,
                    "编辑战斗地图配方");
                serializedRecipe.ApplyModifiedProperties();
                recipe.Clamp();
                EditorUtility.SetDirty(recipe);
            }
            else
            {
                serializedRecipe.ApplyModifiedProperties();
            }
            EditorGUILayout.EndVertical();
        }

        static string RecipePropertyLabel(string propertyPath)
        {
            switch (propertyPath)
            {
                case "seed":
                    return "基础 Seed";
                case "generatorVersion":
                    return "生成器版本";
                case "candidateCount":
                    return "每批候选数量";
                case "maximumCandidateBatches":
                    return "最多候选批次";
                case "mapCenterOffset":
                    return "地图中心偏移";
                case "mapSize":
                    return "地图边长（米）";
                case "chunkSize":
                    return "区块边长（米）";
                case "chunkResolution":
                    return "区块网格分辨率";
                case "spawnDistance":
                    return "双方出生间距（米）";
                case "spawnClearance":
                    return "出生离地高度（米）";
                case "designCombatSpeed":
                    return "设计交战速度（米/秒）";
                case "designTurnRadius":
                    return "设计持续转弯半径（米）";
                case "designWeaponRange":
                    return "设计有效射程（米）";
                case "vehicleWingspan":
                    return "参考翼展（米）";
                case "targetFirstContactSeconds":
                    return "目标首次接触时间（秒）";
                case "targetOcclusionSeconds":
                    return "目标连续遮挡时间（秒）";
                case "targetExposureSeconds":
                    return "目标连续暴露时间（秒）";
                case "mountainHeight":
                    return "中央山体高度（米）";
                case "mainRouteWidth":
                    return "主路线宽度（米）";
                case "canyonRouteWidth":
                    return "峡谷路线宽度（米）";
                case "longRangeRouteWidth":
                    return "远射走廊宽度（米）";
                case "occluderTowerCount":
                    return "遮挡塔数量";
                case "microNoiseStrength":
                    return "微地形噪声强度";
                case "microNoiseScale":
                    return "微地形噪声尺度";
                case "warningRadius":
                    return "边界警告半径（米）";
                case "forfeitRadius":
                    return "判负半径（米）";
                case "forfeitSeconds":
                    return "越界判负倒计时（秒）";
                case "minimumGroundClearance":
                    return "最低离地高度（米）";
                case "maximumGroundClearance":
                    return "最高离地高度（米）";
                case "minimumCommitScore":
                    return "最低应用分数";
                case "maximumRouteTimeImbalance":
                    return "最大路线时间差比例";
                case "layoutOptimizationIterations":
                    return "布局约束优化迭代次数";
                case "validationGridResolution":
                    return "多高度视线场采样边长";
                default:
                    return ObjectNames.NicifyVariableName(propertyPath);
            }
        }

        void DrawGenerationControls()
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "候选生成",
                EditorStyles.boldLabel);
            previewSeed = EditorGUILayout.IntField(
                "预览 Seed",
                previewSeed);
            EditorGUILayout.HelpBox(
                "预览 Seed 不会写回配方；只有“应用候选”会保存当前基础 Seed，并重建生成地图。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(
                       Application.isPlaying
                       && IsCombatActive()))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("生成一个候选"))
                        GenerateOne();
                    if (GUILayout.Button("下一个 Seed"))
                    {
                        unchecked
                        {
                            previewSeed++;
                        }
                        GenerateOne();
                    }
                    if (GUILayout.Button("批量生成 6 个"))
                        GenerateBatch(6);
                }
            }

            if (Application.isPlaying && IsCombatActive())
            {
                EditorGUILayout.HelpBox(
                    "战斗进行中：地图重建已锁定。",
                    MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        void DrawCandidates()
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "候选列表",
                EditorStyles.boldLabel);
            if (candidates.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "尚未生成候选。",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            for (int index = 0;
                 index < candidates.Count;
                 index++)
            {
                CombatMapGenerationResult candidate =
                    candidates[index];
                CombatMapValidationReport report =
                    candidate != null
                        ? candidate.validation
                        : null;
                string label = candidate == null
                    ? index + " / 无效候选"
                    : string.Format(
                        "#{0}  Seed {1}  总分 {2:0.0}  关键最低 {3:0}  {4}",
                        index + 1,
                        candidate.derivedSeed,
                        report != null ? report.score : 0f,
                        report != null
                            ? Mathf.Min(
                                report.topologyScore,
                                Mathf.Min(
                                    report.kinematicScore,
                                    report.coverRhythmScore))
                            : 0f,
                        candidate.CanCommit
                            ? "通过"
                            : "失败");
                bool selected = selectedCandidate == index;
                bool nextSelected = GUILayout.Toggle(
                    selected,
                    label,
                    "Button");
                if (nextSelected && !selected)
                {
                    selectedCandidate = index;
                    PreviewSelectedCandidate();
                    SceneView.RepaintAll();
                }
            }

            CombatMapGenerationResult selectedResult =
                SelectedCandidate;
            using (new EditorGUI.DisabledScope(
                       selectedResult == null
                       || !selectedResult.CanCommit
                       || IsCombatActive()))
            {
                if (GUILayout.Button(
                        "应用候选",
                        GUILayout.Height(28f)))
                {
                    ApplySelected();
                }
            }

            using (new EditorGUI.DisabledScope(
                       IsCombatActive()))
            {
                if (GUILayout.Button("恢复上一个合格方案"))
                    RestorePrevious();
            }
            EditorGUILayout.EndVertical();
        }

        void DrawOverlayControls()
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "语义 Overlay",
                EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            showRoutes =
                EditorGUILayout.Toggle("路线", showRoutes);
            showSpawns =
                EditorGUILayout.Toggle("战术体积与出生区", showSpawns);
            showSightlines =
                EditorGUILayout.Toggle("开局视线", showSightlines);
            showBoundary =
                EditorGUILayout.Toggle("战斗边界", showBoundary);
            showViolations =
                EditorGUILayout.Toggle("失败采样点", showViolations);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            EditorGUILayout.EndVertical();
        }

        void DrawDiagnostics()
        {
            EditorGUILayout.BeginVertical(
                EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "验证结果",
                EditorStyles.boldLabel);
            CombatMapGenerationResult candidate =
                SelectedCandidate;
            CombatMapValidationReport report =
                candidate != null
                    ? candidate.validation
                    : CurrentValidation;
            CombatSemanticPlan plan =
                candidate != null
                    ? candidate.plan
                    : CurrentPlan;

            if (report == null)
            {
                EditorGUILayout.LabelField(
                    "尚无验证报告。",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "总分",
                    report.score.ToString("0.0")
                    + " / "
                    + report.minimumCommitScore.ToString("0.0"));
                if (plan != null && plan.schemaVersion >= 2)
                {
                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField(
                        "V2 关键分项（各 100 分）",
                        EditorStyles.miniBoldLabel);
                    DrawScore(
                        "尺度适配",
                        report.scaleCompatibilityScore,
                        100f);
                    DrawScore(
                        "拓扑韧性",
                        report.topologyScore,
                        100f);
                    DrawScore(
                        "遮挡节奏",
                        report.coverRhythmScore,
                        100f);
                    DrawScore(
                        "机动可行性",
                        report.kinematicScore,
                        100f);
                    EditorGUILayout.LabelField(
                        "首次接触 / 平均遮挡 / 最大暴露",
                        string.Format(
                            "{0:0.0}s / {1:0.0}s / {2:0.0}s",
                            report.firstContactSeconds,
                            report.meanOcclusionSeconds,
                            report.maximumExposureSeconds));
                    EditorGUILayout.LabelField(
                        "最小转弯半径 / 最小盆地直径",
                        string.Format(
                            "{0:0}m / {1:0}m",
                            report.minimumTurnRadius,
                            report.minimumManeuverDiameter));
                    EditorGUILayout.LabelField(
                        "最少出口 / 割点 / 全图眼位",
                        string.Format(
                            "{0} / {1} / {2}",
                            report.minimumExitCount,
                            report.articulationPointCount,
                            report.globalEyePointCount));
                    EditorGUILayout.LabelField(
                        "路线选择熵 / 最大支配率",
                        string.Format(
                            "{0:0.00} / {1:P0}",
                            report.routeUsageEntropy,
                            report.maximumRouteDominance));
                    if (report.lineOfSightOpenFractions != null
                        && report.lineOfSightOpenFractions.Length > 0)
                    {
                        EditorGUILayout.LabelField(
                            "多高度 LOS 开放率",
                            string.Join(
                                "  ",
                                report.lineOfSightOpenFractions
                                    .Select((value, index) =>
                                        (report.lineOfSightBands != null
                                         && index
                                         < report.lineOfSightBands.Length
                                            ? report.lineOfSightBands[index]
                                                .ToString("0")
                                            : index.ToString())
                                        + "m:"
                                        + value.ToString("P0"))));
                    }
                    EditorGUILayout.Space(3f);
                }

                EditorGUILayout.LabelField(
                    "兼容总分构成",
                    EditorStyles.miniBoldLabel);
                DrawScore(
                    "队伍平衡",
                    report.teamBalanceScore,
                    10f);
                DrawScore(
                    "路线多样性",
                    report.routeDiversityScore,
                    20f);
                DrawScore(
                    "视线与遮挡",
                    report.sightlineAndCoverScore,
                    20f);
                DrawScore(
                    "交战空间",
                    report.objectivePressureScore,
                    10f);
                DrawScore(
                    "机动兼容性",
                    report.mobilityCompatibilityScore,
                    20f);
                DrawScore(
                    "出生安全",
                    report.spawnSafetyScore,
                    10f);
                DrawScore(
                    "可读性",
                    report.readabilityScore,
                    5f);
                DrawScore(
                    "性能",
                    report.performanceScore,
                    5f);

                if (report.violations != null)
                {
                    foreach (CombatMapViolation violation
                             in report.violations)
                    {
                        if (violation == null)
                            continue;
                        EditorGUILayout.HelpBox(
                            violation.code + ": "
                            + violation.message,
                            violation.severity
                            == CombatMapViolationSeverity.HardError
                                ? MessageType.Error
                                : MessageType.Warning);
                    }
                }
            }

            if (plan != null)
            {
                EditorGUILayout.LabelField(
                    "校验码",
                    string.IsNullOrEmpty(plan.checksum)
                        ? "（无）"
                        : plan.checksum);
                EditorGUILayout.LabelField(
                    "战术体积 / 路线 / 地形特征 / 遮挡体",
                    string.Format(
                        "{0} / {1} / {2} / {3}",
                        plan.tacticalVolumes != null
                            ? plan.tacticalVolumes.Length
                            : 0,
                        plan.routes != null
                            ? plan.routes.Length
                            : 0,
                        plan.terrainStamps != null
                            ? plan.terrainStamps.Length
                            : 0,
                        plan.occluders != null
                            ? plan.occluders.Length
                            : 0));
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(
                status,
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        static void DrawScore(
            string label,
            float value,
            float maximum)
        {
            Rect rect =
                EditorGUILayout.GetControlRect(false, 18f);
            Rect labelRect =
                new Rect(rect.x, rect.y, 112f, rect.height);
            Rect barRect =
                new Rect(
                    rect.x + 116f,
                    rect.y + 2f,
                    rect.width - 154f,
                    rect.height - 4f);
            Rect numberRect =
                new Rect(
                    rect.xMax - 36f,
                    rect.y,
                    36f,
                    rect.height);
            EditorGUI.LabelField(labelRect, label);
            EditorGUI.ProgressBar(
                barRect,
                Mathf.Clamp01(
                    value / Mathf.Max(0.001f, maximum)),
                string.Empty);
            EditorGUI.LabelField(
                numberRect,
                value.ToString("0"));
        }

        void DrawBottomToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(
                       EditorStyles.toolbar))
            {
                if (GUILayout.Button(
                        "打开场景",
                        EditorStyles.toolbarButton))
                {
                    CombatMapLabAssetBuilder
                        .OpenLaboratoryScene();
                    BindScene();
                }
                using (new EditorGUI.DisabledScope(
                           EditorApplication
                               .isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button(
                            "重建场景",
                            EditorStyles.toolbarButton))
                    {
                        CombatMapLabAssetBuilder
                            .RebuildLaboratoryAssets();
                        BindScene();
                    }
                }
                if (GUILayout.Button(
                        Application.isPlaying
                            ? "停止"
                            : "进入运行模式",
                        EditorStyles.toolbarButton))
                {
                    TogglePlayMode();
                }
                if (GUILayout.Button(
                        "截图",
                        EditorStyles.toolbarButton))
                {
                    status =
                        CombatMapLabAssetBuilder.CapturePreview();
                }
            }
        }

        void GenerateOne()
        {
            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
            {
                status =
                    "错误：场景中缺少战斗地图运行时控制器。";
                return;
            }

            CombatMapGenerationResult result =
                runtime.GenerateBestPreview(previewSeed);
            candidates.Clear();
            candidateApplySeeds.Clear();
            if (result != null)
            {
                candidates.Add(result);
                candidateApplySeeds.Add(previewSeed);
            }
            selectedCandidate = 0;
            status = "已生成预览，基础 Seed=" + previewSeed
                + "；未写回地图配方。";
            Repaint();
            SceneView.RepaintAll();
        }

        void GenerateBatch(int count)
        {
            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
            {
                status =
                    "错误：场景中缺少战斗地图运行时控制器。";
                return;
            }

            candidates.Clear();
            candidateApplySeeds.Clear();
            count = Mathf.Clamp(count, 1, 18);
            for (int index = 0; index < count; index++)
            {
                int seed;
                unchecked
                {
                    seed = previewSeed + index;
                }
                CombatMapGenerationResult result =
                    runtime.GenerateBestPreview(seed);
                if (result == null)
                    continue;
                candidates.Add(result);
                candidateApplySeeds.Add(seed);
            }
            selectedCandidate = FindBestCandidateIndex();
            PreviewSelectedCandidate();
            status = "已生成 " + candidates.Count
                + " 个候选；地图配方 Seed 未改变。";
            Repaint();
            SceneView.RepaintAll();
        }

        void ApplySelected()
        {
            CombatMapGenerationResult candidate =
                SelectedCandidate;
            if (candidate == null || !candidate.CanCommit)
            {
                status = "候选未通过硬约束，不能应用。";
                return;
            }

            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
            {
                status =
                    "错误：场景中缺少战斗地图运行时控制器。";
                return;
            }
            int seed = SelectedApplySeed;
            Undo.RegisterFullObjectHierarchyUndo(
                controller.gameObject,
                "应用战斗地图候选");
            if (!runtime.ApplyCandidate(seed))
            {
                status =
                    "候选在最终验证中失败，未修改已应用方案。";
                return;
            }

            previousAppliedSeed = currentAppliedSeed;
            currentAppliedSeed = seed;
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(
                controller.gameObject.scene);
            status = "候选已应用：基础 Seed=" + seed
                + " / 候选 Seed="
                + runtime.CurrentSeed + " / 分数="
                + (runtime.CurrentValidation != null
                    ? runtime.CurrentValidation.score.ToString("0.0")
                    : "n/a");
            SceneView.RepaintAll();
        }

        void RestorePrevious()
        {
            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null)
            {
                status =
                    "错误：场景中缺少战斗地图运行时控制器。";
                return;
            }
            if (!previousAppliedSeed.HasValue)
            {
                status =
                    "本次工具会话中没有可恢复的方案。";
                return;
            }

            int restoreSeed = previousAppliedSeed.Value;
            if (!runtime.ApplyCandidate(restoreSeed))
            {
                status =
                    "上一个方案未能重新通过验证，未恢复。";
                return;
            }
            int replacedSeed = currentAppliedSeed;
            currentAppliedSeed = restoreSeed;
            previousAppliedSeed = replacedSeed;
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(
                controller.gameObject.scene);
            status =
                "已恢复上一个合格方案：基础 Seed="
                + restoreSeed;
            SceneView.RepaintAll();
        }

        bool TryInvoke(
            string[] methodNames,
            object[] arguments,
            out object result,
            out string error)
        {
            result = null;
            error = "错误：缺少运行时控制器。";
            if (controller == null)
                return false;

            const BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            Type type = controller.GetType();
            foreach (string methodName in methodNames)
            {
                foreach (MethodInfo method
                         in type.GetMethods(flags))
                {
                    if (!string.Equals(
                            method.Name,
                            methodName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters =
                        method.GetParameters();
                    if (parameters.Length != arguments.Length)
                        continue;
                    bool compatible = true;
                    for (int index = 0;
                         index < parameters.Length;
                         index++)
                    {
                        object argument = arguments[index];
                        if (argument != null
                            && !parameters[index].ParameterType
                                .IsInstanceOfType(argument))
                        {
                            compatible = false;
                            break;
                        }
                    }
                    if (!compatible)
                        continue;

                    try
                    {
                        result = method.Invoke(
                            controller,
                            arguments);
                        error = string.Empty;
                        return true;
                    }
                    catch (TargetInvocationException exception)
                    {
                        Exception cause =
                            exception.InnerException ?? exception;
                        Debug.LogException(cause);
                        error = "错误：" + cause.Message;
                        return false;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        error = "错误：" + exception.Message;
                        return false;
                    }
                }
            }

            error = "错误：找不到兼容的方法："
                + string.Join("/", methodNames);
            return false;
        }

        void ReplaceCandidates(object value)
        {
            candidates.Clear();
            candidateApplySeeds.Clear();
            AddCandidates(value);
            for (int index = 0;
                 index < candidates.Count;
                 index++)
            {
                candidateApplySeeds.Add(
                    candidates[index]?.derivedSeed
                    ?? previewSeed);
            }
            selectedCandidate = Mathf.Clamp(
                selectedCandidate,
                0,
                Mathf.Max(0, candidates.Count - 1));
        }

        void AddCandidates(object value)
        {
            if (value == null)
                return;
            CombatMapGenerationResult single =
                value as CombatMapGenerationResult;
            if (single != null)
            {
                candidates.Add(single);
                return;
            }

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
                return;
            foreach (object item in enumerable)
            {
                CombatMapGenerationResult candidate =
                    item as CombatMapGenerationResult;
                if (candidate != null)
                    candidates.Add(candidate);
            }
        }

        void RefreshCandidatesFromController()
        {
            if (controller == null)
                return;
            object value =
                CombatMapLabAssetBuilder.ReadControllerMember(
                    controller,
                    "PreviewCandidates",
                    "Candidates",
                    "CurrentCandidates",
                    "previewCandidates",
                    "candidates");
            if (value != null)
                ReplaceCandidates(value);
            else
            {
                CombatMapRuntimeController runtime =
                    controller as CombatMapRuntimeController;
                if (runtime?.CurrentResult != null)
                {
                    candidates.Clear();
                    candidateApplySeeds.Clear();
                    candidates.Add(runtime.CurrentResult);
                    candidateApplySeeds.Add(
                        runtime.Recipe != null
                            ? runtime.Recipe.Seed
                            : previewSeed);
                    selectedCandidate = 0;
                }
            }
        }

        int FindBestCandidateIndex()
        {
            int result = 0;
            float bestCritical = float.MinValue;
            float bestScore = float.MinValue;
            for (int index = 0;
                 index < candidates.Count;
                 index++)
            {
                CombatMapValidationReport report =
                    candidates[index]?.validation;
                float critical = report != null
                    ? Mathf.Min(
                        report.topologyScore,
                        Mathf.Min(
                            report.kinematicScore,
                            Mathf.Min(
                                report.coverRhythmScore,
                                report.scaleCompatibilityScore)))
                    : float.MinValue;
                float score = report?.score
                    ?? float.MinValue;
                if (critical < bestCritical
                    || (Mathf.Approximately(
                            critical,
                            bestCritical)
                        && score <= bestScore))
                {
                    continue;
                }
                bestCritical = critical;
                bestScore = score;
                result = index;
            }
            return result;
        }

        bool IsCombatActive()
        {
            object value =
                CombatMapLabAssetBuilder.ReadControllerMember(
                    controller,
                    "IsCombatActive",
                    "CombatActive",
                    "isCombatActive");
            return value is bool && (bool)value;
        }

        CombatMapGenerationResult SelectedCandidate
        {
            get
            {
                if (selectedCandidate < 0
                    || selectedCandidate >= candidates.Count)
                {
                    return null;
                }
                return candidates[selectedCandidate];
            }
        }

        int SelectedApplySeed
        {
            get
            {
                if (selectedCandidate >= 0
                    && selectedCandidate
                    < candidateApplySeeds.Count)
                {
                    return candidateApplySeeds[selectedCandidate];
                }
                return previewSeed;
            }
        }

        void PreviewSelectedCandidate()
        {
            CombatMapRuntimeController runtime =
                controller as CombatMapRuntimeController;
            if (runtime == null || SelectedCandidate == null)
                return;
            runtime.GenerateBestPreview(SelectedApplySeed);
        }

        CombatSemanticPlan CurrentPlan =>
            CombatMapLabAssetBuilder.ReadControllerMember(
                controller,
                "CurrentPlan",
                "Plan",
                "currentPlan") as CombatSemanticPlan;

        CombatMapValidationReport CurrentValidation =>
            CombatMapLabAssetBuilder.ReadControllerMember(
                controller,
                "CurrentValidation",
                "Validation",
                "currentValidation")
                as CombatMapValidationReport;

        void TogglePlayMode()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            if (!CombatMapLabAssetBuilder
                    .OpenLaboratoryScene())
            {
                return;
            }
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.isPlaying = true;
        }

        void DuringSceneGui(SceneView sceneView)
        {
            if (SceneManager.GetActiveScene().path
                != CombatMapLabAssetBuilder.ScenePath)
            {
                return;
            }

            CombatSemanticPlan plan =
                SelectedCandidate != null
                    ? SelectedCandidate.plan
                    : CurrentPlan;
            if (plan == null)
                return;

            if (showBoundary)
            {
                Handles.color =
                    new Color(1f, 0.68f, 0.1f, 0.8f);
                Handles.DrawWireDisc(
                    plan.mapCenter,
                    Vector3.up,
                    plan.warningRadius);
                Handles.color =
                    new Color(1f, 0.2f, 0.12f, 0.8f);
                Handles.DrawWireDisc(
                    plan.mapCenter,
                    Vector3.up,
                    plan.forfeitRadius);
            }

            if (showRoutes && plan.routes != null)
            {
                foreach (CombatSemanticRoute route
                         in plan.routes)
                {
                    if (route == null
                        || route.waypoints == null
                        || route.waypoints.Length < 2)
                    {
                        continue;
                    }
                    Handles.color = RouteColor(route.type);
                    Handles.DrawAAPolyLine(
                        Mathf.Clamp(route.width * 0.035f, 2f, 8f),
                        route.waypoints);
                    foreach (Vector3 waypoint
                             in route.waypoints)
                    {
                        Handles.DrawWireDisc(
                            waypoint,
                            Vector3.up,
                            Mathf.Max(4f, route.width * 0.08f));
                    }
                }
            }

            if (showSpawns && plan.tacticalVolumes != null)
            {
                foreach (CombatTacticalVolume volume
                         in plan.tacticalVolumes)
                {
                    if (volume == null)
                        continue;
                    Handles.color = VolumeColor(volume.type);
                    Handles.DrawWireDisc(
                        volume.position,
                        Vector3.up,
                        volume.HorizontalDiameter * 0.5f);
                    Handles.DrawWireCube(
                        volume.position,
                        volume.size);
                    Handles.Label(
                        volume.position
                        + Vector3.up
                        * (volume.size.y * 0.5f + 6f),
                        VolumeDisplayName(volume.type));
                }
            }

            if (showSpawns && plan.anchors != null)
            {
                foreach (CombatSemanticAnchor anchor
                         in plan.anchors)
                {
                    if (anchor == null)
                        continue;
                    Handles.color = AnchorColor(anchor.type);
                    Handles.DrawWireDisc(
                        anchor.position,
                        Vector3.up,
                        Mathf.Max(4f, anchor.radius));
                    Handles.ArrowHandleCap(
                        0,
                        anchor.position,
                        Quaternion.LookRotation(
                            anchor.forward.sqrMagnitude > 0.001f
                                ? anchor.forward
                                : Vector3.forward),
                        Mathf.Max(12f, anchor.radius),
                        EventType.Repaint);
                    Handles.Label(
                        anchor.position + Vector3.up * 6f,
                        AnchorDisplayName(anchor.type));
                }
            }

            if (showSightlines)
                DrawSpawnSightline(plan);

            CombatMapValidationReport report =
                SelectedCandidate != null
                    ? SelectedCandidate.validation
                    : CurrentValidation;
            if (showViolations
                && report != null
                && report.violations != null)
            {
                foreach (CombatMapViolation violation
                         in report.violations)
                {
                    if (violation == null)
                        continue;
                    Handles.color =
                        violation.severity
                        == CombatMapViolationSeverity.HardError
                            ? Color.red
                            : Color.yellow;
                    float size = HandleUtility.GetHandleSize(
                        violation.position) * 0.2f;
                    Handles.SphereHandleCap(
                        0,
                        violation.position,
                        Quaternion.identity,
                        size,
                        EventType.Repaint);
                    Handles.Label(
                        violation.position + Vector3.up * size,
                        violation.code);
                }
            }
        }

        void DrawSpawnSightline(
            CombatSemanticPlan plan)
        {
            CombatSemanticAnchor player =
                plan.FindAnchor(CombatAnchorType.PlayerSpawn);
            CombatSemanticAnchor enemy =
                plan.FindAnchor(CombatAnchorType.EnemySpawn);
            if (player == null || enemy == null)
                return;

            AirCombatMapSettings settings = recipe != null
                ? recipe.CreateValidatedSettings(plan.seed)
                : AirCombatMapSettings.CreateDefault();
            bool blocked =
                !CombatMapGenerator.HasTerrainLineOfSight(
                    settings,
                    plan,
                    player.position,
                    enemy.position);
            Handles.color = blocked
                ? new Color(0.2f, 1f, 0.45f, 0.75f)
                : new Color(1f, 0.12f, 0.08f, 0.9f);
            Handles.DrawDottedLine(
                player.position,
                enemy.position,
                8f);
        }

        static Color RouteColor(CombatRouteType type)
        {
            switch (type)
            {
                case CombatRouteType.TerrainMaskedFlank:
                    return new Color(0.1f, 1f, 0.7f, 0.9f);
                case CombatRouteType.LongRange:
                    return new Color(1f, 0.35f, 0.12f, 0.9f);
                case CombatRouteType.Retreat:
                    return new Color(0.65f, 0.5f, 1f, 0.8f);
                default:
                    return new Color(0.15f, 0.75f, 1f, 0.9f);
            }
        }

        static Color AnchorColor(CombatAnchorType type)
        {
            switch (type)
            {
                case CombatAnchorType.PlayerSpawn:
                    return new Color(0.1f, 0.75f, 1f, 0.9f);
                case CombatAnchorType.EnemySpawn:
                    return new Color(1f, 0.2f, 0.15f, 0.9f);
                case CombatAnchorType.CentralConflict:
                    return new Color(1f, 0.8f, 0.1f, 0.9f);
                case CombatAnchorType.PowerPosition:
                    return new Color(1f, 0.25f, 0.85f, 0.9f);
                default:
                    return new Color(0.65f, 0.75f, 1f, 0.8f);
            }
        }

        static Color VolumeColor(CombatTacticalVolumeType type)
        {
            switch (type)
            {
                case CombatTacticalVolumeType.SpawnBasin:
                    return new Color(0.15f, 0.78f, 1f, 0.75f);
                case CombatTacticalVolumeType.ManeuverBowl:
                    return new Color(1f, 0.72f, 0.08f, 0.8f);
                case CombatTacticalVolumeType.OcclusionGate:
                    return new Color(0.15f, 1f, 0.55f, 0.8f);
                case CombatTacticalVolumeType.ExposureLane:
                    return new Color(0.28f, 0.58f, 1f, 0.8f);
                default:
                    return new Color(0.82f, 0.3f, 1f, 0.75f);
            }
        }

        static string VolumeDisplayName(
            CombatTacticalVolumeType type)
        {
            switch (type)
            {
                case CombatTacticalVolumeType.SpawnBasin:
                    return "出生盆地";
                case CombatTacticalVolumeType.ManeuverBowl:
                    return "回旋交战盆地";
                case CombatTacticalVolumeType.OcclusionGate:
                    return "遮断门";
                case CombatTacticalVolumeType.ExposureLane:
                    return "高风险暴露线";
                default:
                    return "恢复与重新接敌空间";
            }
        }

        static string AnchorDisplayName(CombatAnchorType type)
        {
            switch (type)
            {
                case CombatAnchorType.PlayerSpawn:
                    return "玩家出生区";
                case CombatAnchorType.EnemySpawn:
                    return "敌方出生区";
                case CombatAnchorType.CentralConflict:
                    return "中央交战区";
                case CombatAnchorType.PlayerRetreat:
                    return "玩家撤退点";
                case CombatAnchorType.EnemyRetreat:
                    return "敌方撤退点";
                case CombatAnchorType.PowerPosition:
                    return "强势位置";
                case CombatAnchorType.Landmark:
                    return "中央地标";
                default:
                    return type.ToString();
            }
        }
    }
}
