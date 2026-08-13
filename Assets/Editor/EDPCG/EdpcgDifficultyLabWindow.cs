#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityPlanet.CityPcg;
using UnityPlanet.EDPCG;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.EditorTools
{
    /// <summary>
    /// Dockable editor-only EDPCG lab. The window observes the active non-Boss
    /// encounter but never renders into the Game view or reads gameplay input.
    /// </summary>
    public sealed class EdpcgDifficultyLabWindow : EditorWindow
    {
        enum LabTab
        {
            LivePressure,
            Tuning,
            CityTactical,
            RosterAndEvents,
            Profile
        }

        enum DetailTab
        {
            Roster,
            Events,
            TuningChanges,
            RouteReservations
        }

        enum EventFilter
        {
            All,
            Navigation,
            Combat,
            City,
            TuningAndValidation
        }

        readonly struct TuningDescriptor
        {
            public readonly string Category;
            public readonly string Field;
            public readonly string Label;
            public readonly string Tooltip;
            public readonly float Minimum;
            public readonly float Maximum;
            public readonly bool Integer;
            public readonly EdpcgLiveApplyPolicy Policy;

            public TuningDescriptor(
                string category,
                string field,
                string label,
                string tooltip,
                float minimum,
                float maximum,
                bool integer = false,
                EdpcgLiveApplyPolicy policy = EdpcgLiveApplyPolicy.ApplyNow)
            {
                Category = category;
                Field = field;
                Label = label;
                Tooltip = tooltip;
                Minimum = minimum;
                Maximum = maximum;
                Integer = integer;
                Policy = policy;
            }
        }

        const string ProfileAssetPath =
            "Assets/Resources/EDPCG/EdpcgDifficultyProfile.asset";
        const string CityChallengeProfileAssetPath =
            "Assets/Resources/EDPCG/EdpcgCityTacticalChallengeProfile.asset";
        const string CityGeometryProfileAssetPath =
            "Assets/Resources/CombatCityPCG/CombatCityDefault.asset";
        const float GraphWindowSeconds = 120f;

        static readonly string[] MainTabLabels =
        {
            "实时压力",
            "参数调节",
            "城市战术",
            "名单与事件",
            "难度配置"
        };

        static readonly string[] DetailTabLabels =
        {
            "固定名单",
            "事件流",
            "调参记录",
            "路线预留"
        };

        static readonly string[] EventFilterLabels =
        {
            "全部事件", "导航", "战斗", "城市", "调参与验证"
        };

        static readonly TuningDescriptor[] TuningFields =
        {
            new TuningDescriptor("战力与耐久", "enemyHealthMultiplier", "敌机生命倍率",
                "仅影响之后入场的普通敌机；当前已入场敌机保持生成时生命值。", 0.5f, 3f),

            new TuningDescriptor("并发预算", "populationCap", "同时存在上限",
                "当前阶段允许存在的敌机总量。减少它会直接降低持续压迫。", 1f, 28f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "engagementCap", "接战上限",
                "允许同时进入接战状态的敌机数量。", 1f, 16f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "fullSimulationCap", "完整模拟上限",
                "当前运行时尚未接线，仅保留旧配置值供审计，不能在本局修改。", 1f, 16f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "attackTokenCap", "攻击令牌",
                "同一时刻可真正执行攻击的敌机数量。", 1f, 4f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "suicideCommitCap", "自爆冲刺上限",
                "同一时刻允许进入不可随意取消的自爆冲刺数量。", 1f, 4f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "rangedFireLaneCap", "远程火线数量",
                "同一时刻被占用的远程射击走廊数量。", 1f, 4f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "pressureDirectionCap", "压力方向数",
                "玩家需要同时关注的主要威胁方向数。", 1f, 3f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),

            new TuningDescriptor("节奏与压力", "spawnIntervalSeconds", "生成间隔（秒）",
                "名单成员进入战场的最小间隔。", 0.25f, 8f),
            new TuningDescriptor("节奏与压力", "sharedAttackCooldownSeconds", "共享攻击冷却",
                "全体敌机共享的攻击节奏门控。", 0.1f, 1.5f),
            new TuningDescriptor("节奏与压力", "previewPressureMin", "预览压力下界",
                "预览阶段目标压力带下界。", 0f, 0.8f),
            new TuningDescriptor("节奏与压力", "previewPressureMax", "预览压力上界",
                "预览阶段目标压力带上界。", 0.05f, 0.9f),
            new TuningDescriptor("节奏与压力", "engagePressureMin", "接战压力下界",
                "接战阶段目标压力带下界。", 0f, 0.8f),
            new TuningDescriptor("节奏与压力", "engagePressureMax", "接战压力上界",
                "接战阶段目标压力带上界。", 0.05f, 0.9f),
            new TuningDescriptor("节奏与压力", "peakPressureMin", "峰值压力下界",
                "峰值阶段目标压力带下界。", 0f, 0.85f),
            new TuningDescriptor("节奏与压力", "peakPressureMax", "峰值压力上界",
                "峰值阶段目标压力带上界。", 0.05f, 0.9f),
            new TuningDescriptor("节奏与压力", "recoverPressureMin", "恢复压力下界",
                "恢复阶段目标压力带下界。", 0f, 0.7f),
            new TuningDescriptor("节奏与压力", "recoverPressureMax", "恢复压力上界",
                "恢复阶段目标压力带上界。", 0.05f, 0.8f),
            new TuningDescriptor("节奏与压力", "hardPressureLimit", "压力硬上限",
                "超过此值时敌群调度器不再继续增加战场人口。", 0.55f, 0.9f),
            new TuningDescriptor("节奏与压力", "pressureSmoothingSeconds", "压力平滑（秒）",
                "压力信号的响应速度。越大越稳定，但反应越慢。", 0.25f, 5f),

            new TuningDescriptor("压力闭环", "pressureControlStepSeconds", "增压步进（秒）",
                "实际压力持续低于目标带后，每隔多久逐级释放人口、接战和攻击预算。", 1f, 6f),
            new TuningDescriptor("压力闭环", "pressureControlReleaseSeconds", "辅助回收（秒）",
                "压力回到目标带后，每隔多久回收一级临时增压，避免压力反复跳动。", 2f, 8f),
            new TuningDescriptor("压力闭环", "maximumPressureAssistSteps", "最大辅助档",
                "持续低压时最多释放多少级临时预算；仍不会超过本难度档的正式硬上限。", 1f, 4f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("压力闭环", "pressureTargetTolerance", "目标容差",
                "目标带边缘的缓冲范围，避免增压与减压在临界值附近来回切换。", 0f, 0.1f),
            new TuningDescriptor("压力闭环", "closeApproachDistance", "近距威胁距离（米）",
                "普通敌机进入该距离后会计入近距逼近压力；这不会改变武器射程。", 120f, 420f),

            new TuningDescriptor("敌机策略", "maximumStrategyLevel", "最高策略等级",
                "决定敌机会使用多少侧袭、重定位、脱离与重新接敌策略。", 0f, 3f, true),
            new TuningDescriptor("敌机策略", "suicideTelegraphSeconds", "自爆预警（秒）",
                "自爆兵进入冲刺前给玩家的可读预警时间。", 0.25f, 3f),
            new TuningDescriptor("敌机策略", "suicideCommitSeconds", "自爆承诺窗（秒）",
                "自爆兵保持攻击承诺、避免瞬时反复切状态的时间。", 0.5f, 3f),
            new TuningDescriptor("敌机策略", "suicideBreakAwaySeconds", "自爆脱离时间（秒）",
                "失败攻击后脱离并重组的最短时间。", 0.5f, 5f),
            new TuningDescriptor("敌机策略", "rangedTelegraphSeconds", "远程预警（秒）",
                "远程兵开火前保持可读瞄准提示的时间。", 0.25f, 3f),
            new TuningDescriptor("敌机策略", "rangedBurstSeconds", "远程点射（秒）",
                "一次火力暴露持续时间。", 0.25f, 3f),
            new TuningDescriptor("敌机策略", "rangedRelocationSeconds", "远程换位（秒）",
                "远程兵完成一轮火力后尝试更换掩体或射击角度的时间。", 0.5f, 6f),
            new TuningDescriptor("敌机策略", "lineOfSightHysteresisSeconds", "视线迟滞（秒）",
                "避免建筑边缘遮挡造成状态抖动。", 0.1f, 1f),

            new TuningDescriptor("寻路与恢复", "semanticReplanSeconds", "语义重规划（秒）",
                "基于城市战术区与路线重新规划的最小间隔。", 0.25f, 4f),
            new TuningDescriptor("寻路与恢复", "localRepairAfterSeconds", "局部修正阈值（秒）",
                "先尝试局部方向与速度修正，避免过早瞬移或回收。", 1f, 4f),
            new TuningDescriptor("寻路与恢复", "evasiveAfterSeconds", "规避升级阈值（秒）",
                "局部修正失败后切换规避航线的时间。", 3f, 7f),
            new TuningDescriptor("寻路与恢复", "navigationRecoveryAfterSeconds", "导航回收阈值（秒）",
                "持续无法前进后进入可审计的导航恢复流程。", 6f, 12f),
            new TuningDescriptor("寻路与恢复", "reservationLeaseSeconds", "路线租约（秒）",
                "敌机占用狭窄路线或射击走廊的预留有效期。", 2f, 6f),
            new TuningDescriptor("寻路与恢复", "areaEnterDwellSeconds", "区域进入迟滞（秒）",
                "确认进入战术区域前需要保持的时间。", 0.25f, 2f),
            new TuningDescriptor("寻路与恢复", "areaExitDwellSeconds", "区域离开迟滞（秒）",
                "确认离开战术区域前需要保持的时间。", 0.5f, 3f)
        };

        readonly Dictionary<string, float> draft =
            new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<string, float> submittedDraft =
            new Dictionary<string, float>(StringComparer.Ordinal);

        EdpcgEncounterRuntime boundRuntime;
        FinitePlanetUrbanCombatRuntime scenePreviewUrban;
        AirCombatCityPcgLab scenePreviewLab;
        EdpcgCityTacticalRuntimeMap scenePreviewMap;
        AirCombatCityPlan scenePreviewPlan;
        EdpcgGridFireAnalysis sceneGridFireAnalysis;
        readonly EdpcgGridFireAnalysis[] sceneLayerFireAnalyses =
            new EdpcgGridFireAnalysis[3];
        EdpcgCityTacticalChallengeReport sceneChallengeReport;
        int sceneGridFireSignature;
        bool sceneGridFireAnalysisStale;
        double cityChallengeSaveAt;
        double nextSceneThreatSignatureCheckAt;
        EdpcgDifficultyProfile profile;
        EdpcgCityTacticalChallengeProfile cityChallengeProfile;
        LabTab selectedTab;
        DetailTab selectedDetailTab;
        EventFilter eventFilter;
        Vector2 liveScroll;
        Vector2 tuningScroll;
        Vector2 cityScroll;
        Vector2 detailScroll;
        Vector2 profileScroll;
        string rosterSearch = string.Empty;
        int rosterStateFilter;
        int selectedProfileTier;
        int selectedCityChallengeTier;
        [SerializeField] int selectedCityChallengeMission;
        int bookmarkSequence;
        bool graphFrozen;
        [SerializeField] bool sceneOptionsInitialized;
        [SerializeField] int sceneOptionsSchemaVersion;
        [SerializeField] bool sceneOverlayEnabled;
        [SerializeField] bool sceneShowAreas = true;
        [SerializeField] bool sceneShowRoutes = true;
        [SerializeField] bool sceneShowIngresses = true;
        [SerializeField] bool sceneShowReservations = true;
        [SerializeField] bool sceneShowPressureDirections = true;
        [SerializeField] bool sceneShowTacticalArrows;
        [SerializeField] bool sceneShowGridFire = true;
        [SerializeField] bool sceneShowEffectiveFiringEnvelope = true;
        [SerializeField] bool sceneShowBlockedFire = true;
        [SerializeField] bool sceneShowEvasions = true;
        [SerializeField] bool sceneShowGapWindows = true;
        [SerializeField] bool sceneShowFinalBallistics = true;
        [SerializeField] int sceneThreatViewMode = 1;
        [SerializeField] int sceneThreatRoleFilter;
        [SerializeField] int sceneMaximumThreatChannels = 3;
        [SerializeField] bool sceneFollowPlayerGrid = true;
        [SerializeField] int sceneFireAltitudeLayer = 1;
        [SerializeField] int sceneSelectedGridX = 3;
        [SerializeField] int sceneSelectedGridZ = 3;
        [SerializeField] bool sceneShowBuildings;
        [SerializeField] bool sceneShowSkybridges = true;
        [SerializeField] bool sceneShowCables = true;
        [SerializeField] bool sceneShowLabels = true;
        float frozenGraphEnd;
        string lastExportFolder = string.Empty;
        string status = "打开非 Boss 星球战斗后，此窗口会自动连接 EDPCG 运行时。";
        MessageType statusType = MessageType.None;
        GUIStyle sectionStyle;
        GUIStyle monoStyle;

        [MenuItem("工具/星球战斗/EDPCG压力与城市战术工具")]
        static void Open()
        {
            EdpcgDifficultyLabWindow window = GetWindow<
                EdpcgDifficultyLabWindow>("EDPCG压力与城市战术工具");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent(
                "EDPCG压力与城市战术工具",
                "普通小怪 EDPCG 压力、城市战术与场景可视化工具");
            minSize = new Vector2(760f, 520f);
            if (!sceneOptionsInitialized || sceneOptionsSchemaVersion < 4)
            {
                sceneShowAreas = false;
                sceneShowRoutes = false;
                sceneShowIngresses = false;
                sceneShowReservations = false;
                sceneShowPressureDirections = false;
                sceneShowTacticalArrows = false;
                sceneShowGridFire = true;
                sceneShowEffectiveFiringEnvelope = true;
                sceneShowBlockedFire = false;
                sceneShowEvasions = true;
                sceneShowGapWindows = true;
                sceneShowFinalBallistics = true;
                sceneThreatViewMode = 1;
                sceneThreatRoleFilter = 0;
                sceneMaximumThreatChannels = 3;
                sceneFollowPlayerGrid = true;
                sceneShowSkybridges = false;
                sceneShowCables = false;
                sceneShowLabels = true;
                sceneOptionsInitialized = true;
                sceneOptionsSchemaVersion = 4;
            }
            profile = AssetDatabase.LoadAssetAtPath<EdpcgDifficultyProfile>(
                ProfileAssetPath);
            cityChallengeProfile = AssetDatabase.LoadAssetAtPath<
                EdpcgCityTacticalChallengeProfile>(
                CityChallengeProfileAssetPath);
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            EdpcgSceneTacticalOverlay.Attach(this);
            BindRuntime();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            EdpcgSceneTacticalOverlay.Detach(this);
            FlushPendingCityChallengeSave(true);
        }

        void OnInspectorUpdate()
        {
            FlushPendingCityChallengeSave();
            BindRuntime();
            if (EditorApplication.isPlaying)
                Repaint();
        }

        void OnGUI()
        {
            EnsureStyles();
            BindRuntime();
            UpdateSceneOverlay();
            DrawTopToolbar();
            selectedTab = (LabTab)GUILayout.Toolbar(
                (int)selectedTab,
                MainTabLabels,
                EditorStyles.toolbarButton,
                GUILayout.Height(24f));

            if (!HasActiveRuntime())
            {
                if (selectedTab == LabTab.CityTactical && HasCityPreview())
                    DrawCityTactical(null);
                else
                    DrawIdleState();
                DrawStatusBar();
                return;
            }

            switch (selectedTab)
            {
                case LabTab.Tuning:
                    DrawTuning(boundRuntime);
                    break;
                case LabTab.RosterAndEvents:
                    DrawRosterAndEvents(boundRuntime);
                    break;
                case LabTab.CityTactical:
                    DrawCityTactical(boundRuntime);
                    break;
                case LabTab.Profile:
                    DrawProfile();
                    break;
                default:
                    DrawLive(boundRuntime);
                    break;
            }
            DrawStatusBar();
        }

        void DrawTopToolbar()
        {
            bool active = HasActiveRuntime();
            bool preview = !active && HasCityPreview();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            Color previous = GUI.color;
            GUI.color = active
                ? new Color(0.35f, 1f, 0.55f)
                : preview
                    ? new Color(0.35f, 0.82f, 1f)
                    : new Color(1f, 0.74f, 0.28f);
            GUILayout.Label(active ? "● 实时" : preview ? "● 城市预览" : "● 等待",
                EditorStyles.toolbarButton, GUILayout.Width(82f));
            GUI.color = previous;

            if (active)
            {
                GUILayout.Label(new GUIContent(
                    ChineseMission(boundRuntime.MissionId) + "  ·  难度档 " +
                    boundRuntime.Settings.planetTier + "  ·  " +
                    boundRuntime.Elapsed.ToString("0.0", CultureInfo.InvariantCulture) + "秒",
                    "内部任务标识：" + boundRuntime.MissionId),
                    EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label(
                    preview
                        ? "已读取正式城市语义，普通小怪调度尚未运行"
                        : "只连接非 Boss EDPCG 实战会话",
                    EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!active))
            {
                if (GUILayout.Button(
                        new GUIContent("添加书签", "在遥测事件流中记录当前时刻"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(72f)))
                {
                    AddBookmark();
                }
                if (GUILayout.Button(
                        new GUIContent(
                            "导出全部",
                            "导出 PNG/SVG 曲线、CSV、JSON、HTML、清单以及可复用 SVG 图标"),
                        EditorStyles.toolbarButton,
                        GUILayout.Width(72f)))
                {
                    ExportSession(boundRuntime);
                }
            }
            using (new EditorGUI.DisabledScope(
                       string.IsNullOrEmpty(lastExportFolder) ||
                       !Directory.Exists(lastExportFolder)))
            {
                if (GUILayout.Button("打开导出目录", EditorStyles.toolbarButton,
                        GUILayout.Width(88f)))
                {
                    EditorUtility.RevealInFinder(lastExportFolder);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawIdleState()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.HelpBox(
                "当前没有运行中的非 Boss EDPCG 会话。工具不会在 Game 画面创建对象或响应 F7/F8；" +
                "进入星球实战后会自动连接。若已生成正式城市，可切到“城市战术”页预览全部中文区块与意图箭头；" +
                "等待期间仍可检查六档难度配置。",
                MessageType.Info);
            DrawProfile();
        }

        void DrawLive(EdpcgEncounterRuntime runtime)
        {
            EdpcgPressureSample sample = runtime.CurrentSample;
            liveScroll = EditorGUILayout.BeginScrollView(liveScroll);
            DrawSectionTitle("当前阶段与压力预算");

            runtime.Settings.ResolvePhase(
                runtime.Elapsed,
                out float phaseElapsed,
                out float phaseRemaining);
            EditorGUILayout.BeginHorizontal();
            DrawMetric("阶段", ChinesePhase(sample.phase),
                phaseElapsed.ToString("0.0") + " 秒 / 剩余 " +
                phaseRemaining.ToString("0.0") + " 秒");
            DrawMetric("实际压力", sample.actualPressure.ToString("P1"),
                PressureAssessment(sample));
            DrawMetric("目标压力带",
                sample.targetPressureMinimum.ToString("P0") + " – " +
                sample.targetPressureMaximum.ToString("P0"),
                "绿色区间");
            DrawMetric("4秒 / 8秒预测",
                sample.forecastPressure4Seconds.ToString("P0") + " / " +
                sample.forecastPressure8Seconds.ToString("P0"),
                ForecastAssessment(sample));
            DrawMetric("策略等级", runtime.StrategyLevel + " / " +
                runtime.Settings.maximumStrategyLevel,
                StrategyDescription(runtime.StrategyLevel));
            DrawMetric("名单进度", sample.resolvedCount + " / " + sample.rosterCount,
                "有效击落 " + runtime.CreditedKills + " / " +
                runtime.Settings.requiredCreditedKills);
            EditorGUILayout.EndHorizontal();
            int liveRangedCount = runtime.Settings.strikerCount +
                                  runtime.Settings.gunshipCount;
            float liveRangedShare = liveRangedCount /
                                    Mathf.Max(1f, runtime.Settings.rosterCount);
            EditorGUILayout.LabelField(
                "本局敌机结构",
                "远程 " + liveRangedCount + "（" +
                liveRangedShare.ToString("P0") + "） / 自爆 " +
                runtime.Settings.interceptorCount + "；其中突击 " +
                runtime.Settings.strikerCount + "、炮艇 " +
                runtime.Settings.gunshipCount);

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("最近 " + GraphWindowSeconds.ToString("0") + " 秒",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            string freezeLabel = graphFrozen ? "继续实时曲线" : "冻结曲线";
            if (GUILayout.Button(freezeLabel, GUILayout.Width(100f)))
            {
                graphFrozen = !graphFrozen;
                frozenGraphEnd = runtime.Elapsed;
            }
            EditorGUILayout.EndHorizontal();

            Rect plot = GUILayoutUtility.GetRect(
                300f,
                340f,
                GUILayout.ExpandWidth(true));
            EdpcgEditorPressureChart.Draw(
                plot,
                runtime.Recorder.Samples,
                GraphWindowSeconds,
                graphFrozen ? frozenGraphEnd : runtime.Elapsed,
                runtime.Settings.hardPressureLimit);
            DrawChartLegend(graphFrozen);

            EditorGUILayout.Space(8f);
            DrawSectionTitle("压力来源");
            EditorGUILayout.BeginHorizontal();
            DrawPressureBar("敌情", sample.enemyThreatPressure,
                new Color(1f, 0.35f, 0.24f));
            DrawPressureBar("攻击承诺", sample.navigationPressure,
                new Color(0.55f, 0.46f, 1f));
            DrawPressureBar("城市环境", sample.measuredEnvironmentPressure,
                new Color(0.22f, 0.78f, 0.57f));
            DrawPressureBar("玩家负荷", sample.playerStrain,
                new Color(1f, 0.69f, 0.22f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "战斗细分",
                "自爆 " + sample.suicideNavigationPressure.ToString("P0") +
                "   远程 " + sample.rangedNavigationPressure.ToString("P0") +
                "   攻击承诺 " + sample.committedNavigationPressure.ToString("P0") +
                "   玩家受损辅助 " + sample.playerDamageAssist.ToString("P0"));

            EditorGUILayout.Space(8f);
            DrawSectionTitle("观测压力（当前不参与刷怪控制）");
            EditorGUILayout.BeginHorizontal();
            DrawPressureBar("实际火力暴露", sample.observedFirePressure,
                new Color(1f, 0.38f, 0.18f));
            DrawPressureBar("自爆拦截压力", sample.observedInterceptPressure,
                new Color(1f, 0.18f, 0.12f));
            DrawPressureBar("多方向位移压力", sample.observedDisplacementPressure,
                new Color(0.95f, 0.72f, 0.18f));
            DrawPressureBar("系统健康问题", sample.systemHealthIssueRatio,
                new Color(0.65f, 0.45f, 1f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "观测摘要",
                "战斗压力 " + sample.observedCombatPressure.ToString("P0") +
                "   来压方向 " + sample.pressureDirectionCount +
                "   玩家风险 " + sample.observedPlayerRisk.ToString("P0") +
                "   环境净影响 " +
                (sample.environmentObservationAvailable
                    ? sample.observedNetEnvironmentPressure.ToString("P0")
                    : "尚无真实受力事件"));
            EditorGUILayout.HelpBox(
                "这一组数值目前只用于观察和校准，不会改变人口、攻击令牌、生成间隔或战术等级。" +
                "导航失败被单列为系统健康问题，不会伪装成战斗难度。",
                MessageType.Info);

            EditorGUILayout.Space(8f);
            DrawSectionTitle("敌群调度实时门控");
            EditorGUILayout.BeginHorizontal();
            DrawMetric("当前增压档", sample.pressureAssistLevel.ToString(),
                sample.pressureAssistLevel > 0
                    ? "压力持续偏低，正在逐级释放临时预算"
                    : "未启用临时增压");
            DrawMetric("当前制动档", sample.pressureBrakeLevel.ToString(),
                sample.pressureBrakeLevel > 0
                    ? "压力持续偏高，正在逐级收紧临时预算"
                    : "未启用临时制动");
            DrawMetric("正在追击", sample.pursuingThreatCount.ToString(),
                "正在接近、重新接敌、预警或攻击的普通敌机");
            DrawMetric("近距逼近", sample.closeApproachThreatCount.ToString(),
                "进入近距威胁范围的普通敌机");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            DrawUsageBar("战场人口", sample.activeCount, runtime.PopulationCap);
            DrawUsageBar("正在接战", sample.engagementCount, runtime.EngagementCap);
            DrawUsageBar("攻击令牌", sample.attackTokensUsed, runtime.AttackTokenCap);
            DrawUsageBar("自爆冲刺", sample.suicideCommitCount,
                runtime.Settings.suicideCommitCap);
            DrawUsageBar("远程火线", sample.rangedFireLaneCount,
                runtime.Settings.rangedFireLaneCap);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "队列 / 恢复",
                "未生成 " + sample.unspawnedCount +
                "   排队 " + sample.queuedCount +
                "   生成恢复 " + sample.spawnRecoveryCount +
                "   导航恢复 " + sample.navigationRecoveryCount +
                "   当前生成间隔 " + runtime.SpawnInterval.ToString("0.00") + " 秒");
            if (sample.spawnRecoveryCount >=
                Mathf.Max(3, sample.activeCount * 2))
            {
                EditorGUILayout.HelpBox(
                    "刷新入口瓶颈：有 " + sample.spawnRecoveryCount +
                    " 名敌人正在等待合法出生点，场上仅 " +
                    sample.activeCount +
                    " 名。当前低压力主要来自合法出生位置解析失败，" +
                    "可能涉及入口占用、距离、碰撞或航路约束，" +
                    "不是增压档不足。请先检查城市入口和生成位置诊断。",
                    MessageType.Warning);
            }

            if (EdpcgFacilityAssaultPacingPolicy.Applies(runtime.MissionId))
                DrawFacilityAssaultPacing(runtime, sample);

            DrawCityContext(runtime);
            DrawLiveGridPressure(runtime);
            DrawExportContents();
            EditorGUILayout.EndScrollView();
        }

        void DrawFacilityAssaultPacing(
            EdpcgEncounterRuntime runtime,
            EdpcgPressureSample sample)
        {
            FacilityAssaultDifficultySpec facilitySpec =
                FacilityAssaultDifficultySpec.Resolve(
                    runtime.Settings.planetTier - 1);
            EditorGUILayout.Space(8f);
            DrawSectionTitle("设备突袭专属节奏覆盖");
            EditorGUILayout.HelpBox(
                "仅对正式设备突袭任务生效：预览阶段最多 3 秒；基础生成间隔按 72% 加速；" +
                "设施受击或被摧毁时可提交一次有冷却的增援调度请求。" +
                "这不是强制刷怪，仍必须通过硬压力上限、战场人口上限和合法入口检查。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            DrawMetric(
                "节奏覆盖",
                "预览 ≤ 3秒 / 间隔 72%",
                "在当前难度档的原始节奏上覆盖，不改写六档难度资产");
            DrawMetric(
                "低压增援步进",
                EdpcgFacilityAssaultPacingPolicy
                    .FacilityLowPressureStepSeconds.ToString("0.0") + " 秒",
                "持续低于目标带时逐级释放，不会在一帧内暴增敌人");
            DrawMetric(
                "近距威胁判定",
                EdpcgFacilityAssaultPacingPolicy
                    .FacilityCloseApproachDistance.ToString("0") + " 米",
                "追击敌机进入该范围后才计入近距压力信号");
            DrawMetric(
                "设施受击冷却",
                EdpcgFacilityAssaultPacingPolicy
                    .ObjectiveAlertCooldownSeconds.ToString("0.0") + " 秒",
                "任意设施受击后，三座设施共享一次增援请求冷却");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawMetric(
                "受击增援提示",
                runtime.SpawnUrgency > 0
                    ? "待调度 " + runtime.SpawnUrgency + " 档"
                    : "当前无待调度请求",
                runtime.SpawnUrgency > 0
                    ? "调度器会提前检查一次生成机会，但不会绕过安全门控"
                    : "请求可能尚未触发，或已被调度器消费；同一设施受击提示带有冷却");
            DrawMetric(
                "已毁设施",
                runtime.FacilityAssaultDestroyedObjectiveCount.ToString(),
                "只统计本次正式设备突袭会话已确认摧毁的设施");
            DrawMetric(
                "当前增压档",
                sample.pressureAssistLevel.ToString(),
                sample.pressureAssistLevel > 0
                    ? "低压力闭环正在释放临时预算，仍受所有硬约束"
                    : "当前没有低压力临时增压");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawMetric(
                "连通装甲层",
                facilitySpec.ShellLayers + " 层 / " +
                facilitySpec.ModuleCount + " 块",
                "三座设施分别生成；单座硬上限 " +
                FacilityAssaultDifficultySpec
                    .MaximumModulesPerFacility + " 块");
            DrawMetric(
                "单块耐久",
                facilitySpec.ModuleIntegrity.ToString("0"),
                "设施独立耐久，不会改写玩家模块定义");
            DrawMetric(
                "核心耐久",
                facilitySpec.CoreIntegrity.ToString("0"),
                "从任一方向逐层打通一条装甲通道后核心才会暴露");
            DrawMetric(
                "合法摆放包络",
                (facilitySpec.RequiredHalfExtents.x * 2f).ToString("0") +
                " 米立方体",
                "由城市预留地和最终实体碰撞双重验证");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "硬约束状态",
                "压力 " + sample.actualPressure.ToString("P1") +
                " / 上限 " + runtime.Settings.hardPressureLimit.ToString("P0") +
                "   人口 " + sample.activeCount + " / " + runtime.PopulationCap +
                "   入口：仍需通过城市入口、距离与导航合法性检查");

            IReadOnlyList<FinitePlanetFacilityAssaultObjective> facilities =
                FinitePlanetFacilityAssaultObjective.ActiveObjectives;
            if (facilities.Count > 0)
            {
                EditorGUILayout.Space(3f);
                EditorGUILayout.LabelField(
                    "三座设施实时状态",
                    EditorStyles.miniBoldLabel);
                for (int index = 0; index < facilities.Count; index++)
                {
                    FinitePlanetFacilityAssaultObjective facility =
                        facilities[index];
                    if (facility == null)
                        continue;
                    EditorGUILayout.LabelField(
                        "设施 " + (facility.ObjectiveIndex + 1),
                        facility.StatusLabel +
                        "   外壳剩余 " + facility.LiveArmorModuleCount +
                        "/" + facility.ArmorModuleCount);
                }
            }
        }

        void DrawCityTactical(EdpcgEncounterRuntime runtime)
        {
            EdpcgCityTacticalRuntimeMap map = runtime != null
                ? runtime.TacticalMap
                : scenePreviewMap;
            FinitePlanetUrbanCombatRuntime urban = runtime != null
                ? runtime.UrbanRuntime
                : scenePreviewUrban;
            AirCombatCityPcgLab lab = runtime == null ? scenePreviewLab : null;
            cityScroll = EditorGUILayout.BeginScrollView(cityScroll);
            DrawSectionTitle("场景视图可视化");
            EditorGUILayout.HelpBox(
                "这是纯编辑器绘制：不会创建游戏对象、碰撞体或场景辅助组件，也不会写入场景或影响游戏画面。" +
                "关闭总开关或关闭本窗口后，场景视图标记会立即消失。",
                MessageType.Info);
            EditorGUILayout.LabelField(
                "当前城市战术运行方式",
                runtime != null && runtime.Settings != null
                    ? ChineseIntegrationMode(runtime.Settings.integrationMode)
                    : "城市语义预览（战斗调度尚未开始）");

            EditorGUI.BeginChangeCheck();
            sceneOverlayEnabled = EditorGUILayout.ToggleLeft(
                "在场景视图显示城市战术设计",
                sceneOverlayEnabled,
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!sceneOverlayEnabled))
            {
                EditorGUILayout.BeginHorizontal();
                sceneShowAreas = EditorGUILayout.ToggleLeft("战术区域", sceneShowAreas,
                    GUILayout.Width(105f));
                sceneShowRoutes = EditorGUILayout.ToggleLeft("飞行路线", sceneShowRoutes,
                    GUILayout.Width(105f));
                sceneShowIngresses = EditorGUILayout.ToggleLeft("敌机入口", sceneShowIngresses,
                    GUILayout.Width(105f));
                sceneShowReservations = EditorGUILayout.ToggleLeft("实时路线预留",
                    sceneShowReservations, GUILayout.Width(125f));
                sceneShowPressureDirections = EditorGUILayout.ToggleLeft("来压方向",
                    sceneShowPressureDirections, GUILayout.Width(105f));
                sceneShowTacticalArrows = EditorGUILayout.ToggleLeft("宏观区块意图",
                    sceneShowTacticalArrows, GUILayout.Width(125f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                sceneShowGridFire = EditorGUILayout.ToggleLeft("三维威胁域总开关",
                    sceneShowGridFire, GUILayout.Width(125f));
                using (new EditorGUI.DisabledScope(!sceneShowGridFire))
                {
                    sceneShowEffectiveFiringEnvelope = EditorGUILayout.ToggleLeft(
                        "有效枪位包络", sceneShowEffectiveFiringEnvelope,
                        GUILayout.Width(105f));
                    sceneShowBlockedFire = EditorGUILayout.ToggleLeft("被挡枪线",
                        sceneShowBlockedFire, GUILayout.Width(105f));
                    sceneShowEvasions = EditorGUILayout.ToggleLeft("推荐规避箭头",
                        sceneShowEvasions, GUILayout.Width(125f));
                }
                sceneFollowPlayerGrid = EditorGUILayout.ToggleLeft("跟随玩家方格",
                    sceneFollowPlayerGrid, GUILayout.Width(125f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                sceneShowBuildings = EditorGUILayout.ToggleLeft("最终实体建筑",
                    sceneShowBuildings, GUILayout.Width(105f));
                sceneShowSkybridges = EditorGUILayout.ToggleLeft("空中连廊",
                    sceneShowSkybridges, GUILayout.Width(105f));
                sceneShowCables = EditorGUILayout.ToggleLeft("减速电线",
                    sceneShowCables, GUILayout.Width(105f));
                sceneShowLabels = EditorGUILayout.ToggleLeft("中文标注",
                    sceneShowLabels, GUILayout.Width(105f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("聚焦战术城市", GUILayout.Width(110f)))
                    FocusTacticalCity(map);
                EditorGUILayout.EndHorizontal();
            }
            if (EditorGUI.EndChangeCheck())
            {
                UpdateSceneOverlay();
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(8f);
            DrawGridFireAnalysisControls();

            EditorGUILayout.Space(8f);
            DrawSectionTitle("最终实体城市快照");
            AirCombatCityRuntimeGeometrySnapshot snapshot = urban != null
                ? urban.RuntimeGeometrySnapshot
                : lab != null
                    ? lab.RuntimeGeometrySnapshot
                    : null;
            if (snapshot == null || !snapshot.IsUsable)
            {
                EditorGUILayout.HelpBox(
                    "当前会话没有可用的最终实体城市快照。战术区域和路线仍可查看，" +
                    "建筑、连廊、电线与碰撞预算不会伪造显示。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                DrawMetric("建筑", snapshot.instantiatedBuildingCount.ToString(),
                    "规划 " + snapshot.plannedBuildingCount);
                DrawMetric("连廊", snapshot.skybridgeCount.ToString(),
                    "实体连接段");
                DrawMetric("电线", snapshot.aerialCableCount.ToString(),
                    "触发体 " + snapshot.aerialCableTriggerCount);
                DrawMetric("碰撞体", snapshot.colliderCount.ToString(),
                    "其中触发器 " + snapshot.triggerColliderCount);
                DrawMetric("城市生成耗时",
                    snapshot.totalBuildMilliseconds.ToString("0.0") + " 毫秒",
                    "快照 " + snapshot.snapshotBuildMilliseconds.ToString("0.0") + " 毫秒");
                EditorGUILayout.EndHorizontal();
                if (snapshot.totalBuildMilliseconds > 2500f ||
                    snapshot.colliderCount > 1200)
                {
                    EditorGUILayout.HelpBox(
                        "当前城市接近编辑器诊断预算：请结合目标设备检查生成尖峰和物理开销。" +
                        "本工具只报告，不会自动删除建筑或降低配额。",
                        MessageType.Warning);
                }
            }

            DrawCityContext(map, runtime != null ? runtime.CurrentSample : null,
                runtime != null && runtime.Reservations != null
                    ? runtime.Reservations.Reservations.Count
                    : 0);
            EditorGUILayout.Space(8f);
            DrawSectionTitle("颜色说明");
            EditorGUILayout.LabelField(
                "方格底色：绿色容易    黄色中等    橙色困难    红色高危；" +
                "综合成立枪线、命中紧迫度、脱离余量与机动净空");
            EditorGUILayout.LabelField(
                "黄色：主机动路线    蓝色：掩蔽侧翼路线    橙色：远程火力路线    " +
                "粉色：环境陷阱专用路线    金色粗线：实时预留");
            EditorGUILayout.LabelField(
                "青色：空中连廊    红色：减速电线    红色箭头：玩家当前受到威胁的方向");
            EditorGUILayout.LabelField(
                "蓝色虚线与叉号：已验证被楼群阻断的枪线    橙色双箭头：期望对枪方向    " +
                "粉色箭头：诱敌方向    绿色箭头：换层或离开出生区");
            EditorGUILayout.EndScrollView();
        }

        void DrawGridFireAnalysisControls()
        {
            DrawSectionTitle("8×8 空战三维威胁与区块难度");
            EditorGUILayout.HelpBox(
                "这是只读三维潜在威胁预览，不是实时寻路或开火授权。它搜索远程机的有效枪位包络：" +
                "突击机约170米、炮艇约220米，并由贴地追击下限、160米主航层、玩家相对重接敌高度、城市边界、建筑和连廊共同裁切；" +
                "基础空间方向会在邻近建筑轮廓与楼顶附近自适应加密。" +
                "每个候选仍必须从正式路线锚点经5米机体通道到达，并通过连续可见下界与弹丸通道，按八方位×上/同层/下显示。" +
                "正式航线本身仍按真实420米开火许可检查，因此不会漏掉敌机沿路线飞行时形成的远距枪线。" +
                "“4秒/8秒”是从最近合法路线锚点开始计算的局部建线下界，不包含当前敌机先飞到该锚点的时间。" +
                "楼缝由球内候选检查水平及斜向两侧建筑；复杂转弯、刹停和真实敌机当前位置仍需播放模式物理复核。" +
                "楼缝枪位当前只用于诊断，不会主动把实战敌机引入窄缝。" +
                "方格底色是综合难度：同时计算火力紧迫度、可用脱离路线、逃逸余量和机动净空；" +
                "没有成立枪线时不会因为“无需转移”被误判为困难。" +
                "综合难度＝火力60%＋脱离28%×响应需求＋机动12%。",
                MessageType.Warning);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            sceneThreatViewMode = EditorGUILayout.Popup(
                "分析口径",
                Mathf.Clamp(sceneThreatViewMode, 0, 3),
                new[]
                {
                    "几何可见方向", "路线锚点起4秒下界", "路线锚点起8秒下界", "本难度静态预算"
                });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            sceneThreatRoleFilter = EditorGUILayout.Popup(
                "敌机类型",
                Mathf.Clamp(sceneThreatRoleFilter, 0, 2),
                new[] { "全部远程兵", "突击机", "炮艇" });
            sceneMaximumThreatChannels = EditorGUILayout.IntSlider(
                "最多显示通道", sceneMaximumThreatChannels, 1, 3);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            sceneShowGapWindows = EditorGUILayout.ToggleLeft(
                "楼缝射击窗", sceneShowGapWindows, GUILayout.Width(115f));
            sceneShowFinalBallistics = EditorGUILayout.ToggleLeft(
                "最终直线弹道", sceneShowFinalBallistics,
                GUILayout.Width(125f));
            EditorGUILayout.LabelField(
                "低/中/高表示玩家所在高度；敌机占位在有效高度带内连续取高，不再固定为三档离散点。",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                UpdateSceneOverlay();
                SceneView.RepaintAll();
            }

            DrawCityChallengeProfileEditor();
            DrawCityChallengeReport(sceneChallengeReport);

            EditorGUI.BeginChangeCheck();
            int layer = GUILayout.Toolbar(
                Mathf.Clamp(sceneFireAltitudeLayer, 0, 2),
                new[] { "低空层", "中空层", "高空层" },
                GUILayout.Height(24f));
            if (EditorGUI.EndChangeCheck())
            {
                sceneFireAltitudeLayer = layer;
                RefreshGridFireAnalysis(true);
                UpdateSceneOverlay();
                SceneView.RepaintAll();
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            sceneSelectedGridX = EditorGUILayout.IntSlider(
                "选中方格 X", sceneSelectedGridX, 0, 7);
            sceneSelectedGridZ = EditorGUILayout.IntSlider(
                "Z", sceneSelectedGridZ, 0, 7);
            if (GUILayout.Button("重新分析", GUILayout.Width(82f)))
                RefreshGridFireAnalysis(true);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                sceneSelectedGridX = Mathf.Clamp(sceneSelectedGridX, 0, 7);
                sceneSelectedGridZ = Mathf.Clamp(sceneSelectedGridZ, 0, 7);
                UpdateSceneOverlay();
                SceneView.RepaintAll();
            }

            if (sceneGridFireAnalysis == null ||
                !sceneGridFireAnalysis.IsUsable)
            {
                EditorGUILayout.HelpBox(
                    "当前城市缺少最终实体几何快照，暂时不能进行可信枪线分析。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawMetric("分析方格",
                sceneGridFireAnalysis.cells.Count.ToString(),
                "可飞 " + sceneGridFireAnalysis.flyableCellCount);
            DrawMetric("可达候选",
                sceneGridFireAnalysis.sourceCount.ToString(),
                "路线连续段与有效枪位包络");
            DrawMetric("成立射击窗",
                sceneGridFireAnalysis.fireWindowCount.ToString(),
                "楼缝 " + sceneGridFireAnalysis.gapWindowCount);
            DrawMetric("掠过可见",
                sceneGridFireAnalysis.fleetingWindowCount.ToString(),
                "看见但来不及开火");
            DrawMetric("几何可信度",
                sceneGridFireAnalysis.geometryConfidence.ToString("P0"),
                "轴对齐包围盒与连廊中心线保守估算");
            DrawMetric("当前层耗时",
                sceneGridFireAnalysis.buildMilliseconds.ToString("0.0") +
                " 毫秒",
                "只在重建时计算");
            EditorGUILayout.EndHorizontal();
            float threeLayerMilliseconds = 0f;
            for (int layerIndex = 0;
                 layerIndex < sceneLayerFireAnalyses.Length;
                 layerIndex++)
            {
                if (sceneLayerFireAnalyses[layerIndex] != null)
                {
                    threeLayerMilliseconds +=
                        sceneLayerFireAnalyses[layerIndex].buildMilliseconds;
                }
            }
            EditorGUILayout.LabelField(
                "三层威胁域重建总耗时",
                threeLayerMilliseconds.ToString("0.0") +
                " 毫秒（几何未变化时读取缓存，不会每帧重算）");
            if (threeLayerMilliseconds > 750f)
            {
                EditorGUILayout.HelpBox(
                    "当前城市的三层诊断接近1秒。它只影响编辑器重新分析；正式战斗默认不构建该数据。" +
                    "建议调参时松开滑条后再点“重新分析”，不要把实验性实战重定位开关打开。",
                    MessageType.Warning);
            }

            if (!sceneGridFireAnalysis.TryGetCell(
                    sceneSelectedGridX,
                    sceneSelectedGridZ,
                    out EdpcgGridFireCell cell) || cell == null)
            {
                return;
            }
            string cellSummary = "此大格内部9个采样点均不满足参考机宽";
            if (cell.flyable)
            {
                BuildFilteredCellSummary(cell,
                    out int shownDirections,
                    out float shownVolume,
                    out float shownFastestHit,
                    out int shownGapWindows);
                EdpcgGridDifficultyBreakdown difficulty =
                    EdpcgGridDifficultyEvaluator.Evaluate(
                        cell, shownDirections, shownVolume,
                        shownFastestHit, HasFilteredCellCrossfire(cell));
                cellSummary = "方向 " + shownDirections +
                              "   楼缝窗口 " + shownGapWindows +
                              "   威胁体积 " + shownVolume.ToString("P0") +
                              "   综合难度 " + difficulty.score.ToString("P0") +
                              "（" + difficulty.ChineseLevel + "）" +
                              "   火力 " + difficulty.fireThreat.ToString("P0") +
                              "   脱离 " + difficulty.escapeDifficulty.ToString("P0") +
                              "   机动 " + difficulty.maneuverDifficulty.ToString("P0") +
                              "   有效脱离 " +
                              difficulty.recommendedExitCount +
                              (HasFilteredCellCrossfire(cell)
                                  ? "   可执行交叉火力"
                                  : string.Empty) +
                              (float.IsPositiveInfinity(shownFastestHit)
                                  ? string.Empty
                                  : "   最早命中 " +
                                    shownFastestHit.ToString("0.0") + "秒");
            }
            EditorGUILayout.LabelField(
                "当前方格 " + sceneSelectedGridX + "," +
                sceneSelectedGridZ,
                cellSummary);
            int shown = 0;
            for (int index = 0;
                 index < cell.fireWindows.Count && shown < 8;
                 index++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[index];
                if (window == null || !WindowMatchesDisplay(window))
                    continue;
                shown++;
                if (shown > sceneMaximumThreatChannels)
                    break;
                EditorGUILayout.LabelField(
                    window.throughBuildingGap ? "楼缝射击窗" : "可达射击窗",
                    ChineseFireSource(window.sourceKind) + "｜" +
                    ChineseThreatSector(window.azimuthSector) +
                    ChineseElevationBand(window.elevationBand) + "｜" +
                    window.approachSeconds.ToString("0.0") + "秒到位｜" +
                    "连续可见 " + window.visibleWindowSeconds.ToString("0.0") +
                    "秒｜最早命中 " +
                    window.firstHitSeconds.ToString("0.0") + "秒" +
                    (window.robustHullClear
                        ? string.Empty
                        : "｜仅5米导航口径通过"));
            }
            if (cell.fleetingWindowCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "本格另有 " + cell.fleetingWindowCount +
                    " 条“掠过可见”：敌机会在楼缝中看见玩家，但连续时间不足以完成视线稳定和开火预警。",
                    MessageType.None);
            }
            if (sceneShowBlockedFire)
            {
                int blockedShown = 0;
                for (int index = 0;
                     index < cell.fireLines.Count && blockedShown < 3;
                     index++)
                {
                    EdpcgGridFireLine line = cell.fireLines[index];
                    if (line == null || line.incoming ||
                        !LineMatchesRole(line))
                        continue;
                    blockedShown++;
                    EditorGUILayout.LabelField(
                        "被挡方向",
                        ChineseFireSource(line.sourceKind) + "｜" +
                        ChineseThreatSector(line.threatSector) +
                        ChineseElevationBand(line.elevationBand) + "｜被 " +
                        line.blockerId + " 阻断");
                }
            }
        }

        void DrawLiveGridPressure(EdpcgEncounterRuntime runtime)
        {
            if (runtime == null)
                return;
            EditorGUILayout.Space(8f);
            DrawSectionTitle("玩家当前方格与普通小怪地形压力");
            EdpcgGridFireCell cell = runtime.ActiveGridFireCell;
            EdpcgGridFireAnalysis analysis = runtime.ActiveGridFireAnalysis;
            EdpcgCityTacticalChallengeSettings challenge =
                runtime.CityChallengeSettings;
            if (cell == null || analysis == null || challenge == null)
            {
                EditorGUILayout.HelpBox(
                    "当前会话还没有可用的方格枪线状态。它只会在正式城市普通小怪战斗中出现，首领战不会创建EDPCG方格调度。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                "玩家所在方格",
                cell.gridX + "," + cell.gridZ + "｜" +
                ChineseFireAltitude(analysis.altitudeLayer) + "｜停留 " +
                runtime.ActiveGridCellDwellSeconds.ToString("0.0") + " 秒");
            EditorGUILayout.LabelField(
                "规划枪线压力",
                "来火方向 " + cell.threatDirectionCount +
                "   可见枪线 " + cell.incomingLineCount +
                "   已挡枪线 " + cell.blockedLineCount +
                "   安全出口 " + cell.safeExitCount +
                "   方格压力 " + cell.pressureScore.ToString("P0"));
            bool dwellReached = runtime.ActiveGridCellDwellSeconds >=
                                challenge.playerCellDwellSeconds;
            bool safe = cell.safeExitCount >= challenge.minimumSafeExitCount &&
                        cell.pressureScore <=
                        challenge.maximumAcceptedCellPressure;
            EditorGUILayout.LabelField(
                "普通远程兵重定位",
                !challenge.enableOrdinaryRangedReposition
                    ? "安全默认：实验性枪位驱动已关闭；预览不改变敌机"
                    : !dwellReached
                        ? "等待玩家持续停留，阈值 " +
                          challenge.playerCellDwellSeconds.ToString("0.0") +
                          " 秒"
                        : !safe
                            ? "不允许：安全出口或压力上限未满足"
                            : "允许：最多 " +
                              challenge.maximumPressureDirections +
                              " 个来火方向；只移动已有突击机/炮艇");
            EditorGUILayout.LabelField(
                "本局战术题目",
                ChinesePuzzleKind(challenge.puzzleKind));
            DrawCityChallengeReport(runtime.CityChallengeReport);
        }

        void DrawCityChallengeProfileEditor()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("正式城市战术题目配置",
                EditorStyles.miniBoldLabel);
            cityChallengeProfile =
                (EdpcgCityTacticalChallengeProfile)EditorGUILayout.ObjectField(
                    "配置资产",
                    cityChallengeProfile,
                    typeof(EdpcgCityTacticalChallengeProfile),
                    false);
            if (cityChallengeProfile == null)
            {
                EditorGUILayout.HelpBox(
                    "正式配置资产尚未创建。未创建时运行时会使用内存默认值，策划修改不能持久化。",
                    MessageType.Warning);
                if (GUILayout.Button("创建正式城市战术题目配置",
                        GUILayout.Height(26f)))
                {
                    cityChallengeProfile = CreateCityChallengeProfileAsset();
                }
                EditorGUILayout.EndVertical();
                return;
            }

            cityChallengeProfile.EnsureInitialized();
            EditorGUI.BeginChangeCheck();
            CombatCityPcgDesignProfile geometryProfile =
                (CombatCityPcgDesignProfile)EditorGUILayout.ObjectField(
                    "正式城市几何配置",
                    cityChallengeProfile.cityGeometryProfile,
                    typeof(CombatCityPcgDesignProfile),
                    false);
            EdpcgDifficultyProfile ordinaryProfile =
                (EdpcgDifficultyProfile)EditorGUILayout.ObjectField(
                    "正式普通小怪配置",
                    cityChallengeProfile.ordinaryEnemyProfile,
                    typeof(EdpcgDifficultyProfile),
                    false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(cityChallengeProfile,
                    "修改城市战术配置引用");
                cityChallengeProfile.cityGeometryProfile = geometryProfile;
                cityChallengeProfile.ordinaryEnemyProfile = ordinaryProfile;
                EditorUtility.SetDirty(cityChallengeProfile);
                AssetDatabase.SaveAssets();
            }
            EditorGUI.BeginChangeCheck();
            selectedCityChallengeTier = EditorGUILayout.IntSlider(
                "难度档", selectedCityChallengeTier + 1, 1, 6) - 1;
            if (EditorGUI.EndChangeCheck())
            {
                sceneGridFireSignature = 0;
                sceneGridFireAnalysisStale = true;
                SetStatus("已切换难度档；当前预览保持不变，" +
                          "请点“重新分析”更新三层结果。",
                    MessageType.Info);
                UpdateSceneOverlay();
                SceneView.RepaintAll();
            }
            EdpcgCityTacticalChallengeSettings challenge =
                cityChallengeProfile.tiers[
                    Mathf.Clamp(selectedCityChallengeTier, 0, 5)];
            EdpcgCityTacticalChallengeSettings edited =
                challenge.ValidatedCopy();
            EditorGUI.BeginChangeCheck();
            edited.puzzleKind =
                (EdpcgCityTacticalPuzzleKind)EditorGUILayout.Popup(
                    "战术题目",
                    (int)edited.puzzleKind,
                    new[]
                    {
                        "单侧火力压迫",
                        "交叉火力拆解",
                        "连续换掩体",
                        "诱敌进入陷阱",
                        "垂直高度压力",
                        "开放捷径取舍"
                    });
            edited.enableOrdinaryRangedReposition =
                EditorGUILayout.Toggle(
                    "启用实验性实战枪位驱动",
                    edited.enableOrdinaryRangedReposition);
            edited.playerCellDwellSeconds = EditorGUILayout.Slider(
                "玩家停留判定（秒）",
                edited.playerCellDwellSeconds,
                2f,
                20f);
            edited.maximumPressureDirections = EditorGUILayout.IntSlider(
                "最多来火方向",
                edited.maximumPressureDirections,
                1,
                3);
            edited.minimumSafeExitCount = EditorGUILayout.IntSlider(
                "重定位前最少安全出口",
                edited.minimumSafeExitCount,
                1,
                4);
            edited.maximumAcceptedCellPressure = EditorGUILayout.Slider(
                "允许重定位的最高方格压力",
                edited.maximumAcceptedCellPressure,
                0f,
                1f);
            edited.minimumCrossfireCellCount = EditorGUILayout.IntSlider(
                "目标交叉火力格下限",
                edited.minimumCrossfireCellCount,
                0,
                64);
            edited.maximumCrossfireCellCount = EditorGUILayout.IntSlider(
                "目标交叉火力格上限",
                edited.maximumCrossfireCellCount,
                edited.minimumCrossfireCellCount,
                64);
            edited.allowStrikerReposition = EditorGUILayout.Toggle(
                "允许突击机换枪位",
                edited.allowStrikerReposition);
            edited.allowGunshipReposition = EditorGUILayout.Toggle(
                "允许炮艇换枪位",
                edited.allowGunshipReposition);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(cityChallengeProfile,
                    "修改城市战术题目配置");
                cityChallengeProfile.tiers[selectedCityChallengeTier] =
                    edited.ValidatedCopy();
                EditorUtility.SetDirty(cityChallengeProfile);
                cityChallengeSaveAt =
                    EditorApplication.timeSinceStartup + 0.35d;
                sceneGridFireAnalysisStale = true;
                SetStatus("城市战术题目配置已修改；松开控件后自动保存。" +
                          "当前预览保持不变，请点“重新分析”更新三层结果。",
                    MessageType.Info);
            }
            DrawFormalCityGeometryTierEditor(
                cityChallengeProfile.cityGeometryProfile,
                selectedCityChallengeTier);
            EditorGUILayout.HelpBox(
                "该资产是高层编排契约：它引用城市几何与普通小怪配置，但城市PCG不会反向引用EDPCG。" +
                "三维威胁域默认只做诊断。实验性实战枪位驱动目前默认关闭，避免未经路线进度复核的候选让敌机越过枪位再折返；" +
                "它不会新增敌人、不会修改攻击令牌，也不会用于首领战。",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        void DrawFormalCityGeometryTierEditor(
            CombatCityPcgDesignProfile geometryProfile,
            int difficultyTier)
        {
            if (geometryProfile == null)
            {
                EditorGUILayout.HelpBox(
                    "没有引用正式城市几何配置，正式城市仍会使用模板自己的默认资产。",
                    MessageType.Warning);
                return;
            }
            geometryProfile.EnsureInitialized();
            selectedCityChallengeMission = GUILayout.Toolbar(
                Mathf.Clamp(selectedCityChallengeMission, 0, 1),
                new[] { "城市清剿几何", "设施突袭几何" });
            CombatCityMissionProfile missionProfile =
                selectedCityChallengeMission == 1
                    ? geometryProfile.assault
                    : geometryProfile.clearance;
            int tier = Mathf.Clamp(difficultyTier, 0, 5);
            CombatCityDifficultyProfile original =
                missionProfile.difficultyTiers[tier];
            CombatCityDifficultyProfile edited = original.ValidatedCopy();
            EditorGUILayout.LabelField(
                "正式城市空间输入（下一次生成生效）",
                EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            edited.navigationChallenge = EditorGUILayout.Slider(
                "机动净空压力", edited.navigationChallenge, 0f, 1f);
            edited.exposurePressure = EditorGUILayout.Slider(
                "开放枪线压力", edited.exposurePressure, 0f, 1f);
            edited.recoveryGenerosity = EditorGUILayout.Slider(
                "脱离恢复宽容", edited.recoveryGenerosity, 0f, 1f);
            edited.tacticalOpportunityDensity = EditorGUILayout.Slider(
                "垂直与战术机会", edited.tacticalOpportunityDensity, 0f, 1f);
            edited.routeLegibility = EditorGUILayout.Slider(
                "主次路线可读性", edited.routeLegibility, 0f, 1f);
            edited.roadWidthScale = EditorGUILayout.Slider(
                "道路宽度倍率", edited.roadWidthScale, 0.7f, 1.4f);
            edited.blockMergeStrength = EditorGUILayout.Slider(
                "街区合并强度", edited.blockMergeStrength, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(geometryProfile,
                    "修改正式城市空间输入");
                // combatPressure / boss / destruction are deliberately kept
                // untouched here: this section owns ordinary-enemy terrain.
                original.navigationChallenge = edited.navigationChallenge;
                original.exposurePressure = edited.exposurePressure;
                original.recoveryGenerosity = edited.recoveryGenerosity;
                original.tacticalOpportunityDensity =
                    edited.tacticalOpportunityDensity;
                original.routeLegibility = edited.routeLegibility;
                original.roadWidthScale = edited.roadWidthScale;
                original.blockMergeStrength = edited.blockMergeStrength;
                EditorUtility.SetDirty(geometryProfile);
                cityChallengeSaveAt =
                    EditorApplication.timeSinceStartup + 0.35d;
                SetStatus(
                    "正式城市空间输入已修改；松开控件后自动合并保存。" +
                    "不会热改当前战斗，将在下一次城市生成时生效。",
                    MessageType.Info);
            }
            EditorGUILayout.HelpBox(
                "这里直接编辑正式城市资产，不复制一套临时参数。只包含会改变普通小怪城市空间的字段；" +
                "敌人数量、首领追击和城市破坏不在此处混调。",
                MessageType.None);
        }

        void DrawCityChallengeReport(
            EdpcgCityTacticalChallengeReport report)
        {
            if (report == null || !report.evaluable)
                return;
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                (report.passed ? "当前城市通过战术题目验收。" :
                    "当前城市未通过战术题目验收。") + report.summary,
                report.passed ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField(
                "题目测量",
                "单侧来火格 " + report.singleDirectionCellCount +
                "   交叉火力格 " + report.crossfireCellCount +
                "   可拆交叉格 " + report.solvableCrossfireCellCount +
                "   连续转移格 " + report.safeTransferCellCount);
            EditorGUILayout.LabelField(
                "补充测量",
                "有掩体格 " + report.coverCellCount +
                "   开放压力格 " + report.openPressureCellCount +
                "   高度差异格 " + report.verticalDifferenceCellCount +
                "   真实陷阱路线 " + report.environmentalTrapRouteCount);
            EditorGUILayout.LabelField(
                "三维火力窗",
                "成立窗口格 " + report.establishedFireWindowCellCount +
                "   楼缝窗口格 " + report.buildingGapWindowCellCount +
                "   掠过可见格 " + report.fleetingVisibilityCellCount +
                "   正逃逸余量格 " + report.positiveEscapeMarginCellCount);
            EditorGUILayout.LabelField(
                "时间与体积",
                "平均受威胁体积 " +
                report.averageThreatenedVolumeRatio.ToString("P0") +
                "   平均最早命中 " +
                report.averageFastestHitSeconds.ToString("0.0") + " 秒");
        }

        void UpdateSceneOverlay()
        {
            EdpcgSceneTacticalOverlay.Configure(
                HasActiveRuntime() ? boundRuntime : null,
                scenePreviewMap,
                scenePreviewUrban,
                scenePreviewLab,
                sceneGridFireAnalysis,
                sceneSelectedGridX,
                sceneSelectedGridZ,
                SelectGridFireCell,
                new EdpcgSceneOverlayOptions
                {
                    enabled = sceneOverlayEnabled,
                    areas = sceneShowAreas,
                    routes = sceneShowRoutes,
                    ingresses = sceneShowIngresses,
                    reservations = sceneShowReservations,
                    pressureDirections = sceneShowPressureDirections,
                    tacticalArrows = sceneShowTacticalArrows,
                    gridFire = sceneShowGridFire,
                    effectiveFiringEnvelope =
                        sceneShowEffectiveFiringEnvelope,
                    blockedFire = sceneShowBlockedFire,
                    evasions = sceneShowEvasions,
                    gapWindows = sceneShowGapWindows,
                    finalBallistics = sceneShowFinalBallistics,
                    threatViewMode = (EdpcgAirThreatViewMode)Mathf.Clamp(
                        sceneThreatViewMode, 0, 3),
                    threatRoleFilter = (EdpcgAirThreatRoleFilter)Mathf.Clamp(
                        sceneThreatRoleFilter, 0, 2),
                    maximumThreatChannels = Mathf.Clamp(
                        sceneMaximumThreatChannels, 1, 3),
                    buildings = sceneShowBuildings,
                    skybridges = sceneShowSkybridges,
                    cables = sceneShowCables,
                    labels = sceneShowLabels
                });
        }

        EdpcgAirThreatAnalysisOptions BuildSceneThreatOptions()
        {
            AirCombatCitySettings citySettings = scenePreviewUrban != null
                ? scenePreviewUrban.CitySettings
                : scenePreviewLab != null
                    ? scenePreviewLab.Settings
                    : null;
            EdpcgAirThreatAnalysisOptions options =
                EdpcgAirThreatAnalysisOptions.CreateDefault(
                    citySettings, selectedCityChallengeTier);
            if (cityChallengeProfile != null)
            {
                options.challengeSettings = cityChallengeProfile.Resolve(
                    selectedCityChallengeTier);
                EdpcgDifficultyProfile enemyProfile =
                    cityChallengeProfile.ordinaryEnemyProfile != null
                        ? cityChallengeProfile.ordinaryEnemyProfile
                        : profile;
                if (enemyProfile != null)
                    options.tierSettings = enemyProfile.Resolve(
                        selectedCityChallengeTier);
            }
            return options;
        }

        static int BuildGridFireSignature(
            AirCombatCityPlan plan,
            AirCombatCityRuntimeGeometrySnapshot snapshot,
            int difficultyTier,
            EdpcgAirThreatAnalysisOptions options)
        {
            unchecked
            {
                int hash = 486187739;
                hash = hash * 31 + (plan?.resolvedSeed ?? 0);
                hash = hash * 31 + Mathf.Clamp(difficultyTier, 0, 5);
                hash = hash * 31 + (snapshot?.schemaVersion ?? 0);
                if (options != null)
                {
                    hash = HashFloat(hash, options.playerDesignSpeed);
                    hash = HashFloat(hash,
                        options.playerDesignTurnRadius);
                    hash = HashFloat(hash,
                        options.playerDesignClimbSpeed);
                    hash = HashFloat(hash, options.routeSampleSpacing);
                    hash = HashFloat(hash,
                        options.maximumApproachDistance);
                    EdpcgTierSettings tier = options.tierSettings;
                    if (tier != null)
                    {
                        hash = hash * 31 + tier.strikerCount;
                        hash = hash * 31 + tier.gunshipCount;
                        hash = hash * 31 + tier.attackTokenCap;
                        hash = hash * 31 + tier.rangedFireLaneCap;
                        hash = hash * 31 + tier.pressureDirectionCap;
                        hash = HashFloat(hash,
                            tier.rangedTelegraphSeconds);
                        hash = HashFloat(hash,
                            tier.lineOfSightHysteresisSeconds);
                    }
                    EdpcgCityTacticalChallengeSettings challenge =
                        options.challengeSettings;
                    if (challenge != null)
                    {
                        hash = hash * 31 +
                            challenge.maximumPressureDirections;
                        hash = hash * 31 +
                            (challenge.allowStrikerReposition ? 1 : 0);
                        hash = hash * 31 +
                            (challenge.allowGunshipReposition ? 1 : 0);
                    }
                }
                if (snapshot?.buildings != null)
                {
                    for (int index = 0; index < snapshot.buildings.Length;
                         index++)
                    {
                        Bounds bounds = snapshot.buildings[index].localBounds;
                        hash = HashVector(hash, bounds.center);
                        hash = HashVector(hash, bounds.size);
                    }
                }
                if (snapshot?.routes != null)
                {
                    for (int route = 0; route < snapshot.routes.Length; route++)
                    {
                        AirCombatRuntimeRouteGeometry geometry =
                            snapshot.routes[route];
                        hash = hash * 31 + (int)geometry.kind;
                        if (geometry.localPoints == null)
                            continue;
                        for (int point = 0;
                             point < geometry.localPoints.Length;
                             point++)
                        {
                            hash = HashVector(hash,
                                geometry.localPoints[point]);
                        }
                    }
                }
                if (snapshot?.skybridges != null)
                {
                    for (int index = 0;
                         index < snapshot.skybridges.Length;
                         index++)
                    {
                        hash = HashVector(hash,
                            snapshot.skybridges[index].localStart);
                        hash = HashVector(hash,
                            snapshot.skybridges[index].localEnd);
                    }
                }
                return hash;
            }
        }

        static int HashVector(int hash, Vector3 value)
        {
            unchecked
            {
                hash = hash * 31 + Mathf.RoundToInt(value.x * 10f);
                hash = hash * 31 + Mathf.RoundToInt(value.y * 10f);
                hash = hash * 31 + Mathf.RoundToInt(value.z * 10f);
                return hash;
            }
        }

        static int HashFloat(int hash, float value)
        {
            unchecked
            {
                return hash * 31 + Mathf.RoundToInt(value * 1000f);
            }
        }

        static void FocusTacticalCity(EdpcgCityTacticalRuntimeMap map)
        {
            if (map == null ||
                SceneView.lastActiveSceneView == null)
            {
                return;
            }
            bool hasBounds = false;
            Bounds bounds = default(Bounds);
            for (int index = 0; index < map.Areas.Count; index++)
            {
                Bounds area = map.Areas[index].Bounds;
                if (!hasBounds)
                {
                    bounds = area;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(area);
                }
            }
            for (int routeIndex = 0;
                 routeIndex < map.Routes.Count;
                 routeIndex++)
            {
                Vector3[] points = map.Routes[routeIndex].Points;
                if (points == null)
                    continue;
                for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
                {
                    if (!hasBounds)
                    {
                        bounds = new Bounds(points[pointIndex], Vector3.one * 10f);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(points[pointIndex]);
                    }
                }
            }
            if (hasBounds)
                SceneView.lastActiveSceneView.Frame(bounds, false);
        }

        void DrawTuning(EdpcgEncounterRuntime runtime)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "滑杆只修改草稿。并发预算在下一次阶段切换的安全边界生效；其余参数立即生效。",
                EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("从运行时重载", GUILayout.Width(110f)))
                SyncDraft();
            if (GUILayout.Button("全部回退", GUILayout.Width(82f)))
            {
                runtime.RevertAllTuning();
                SyncDraft();
                SetStatus("已回退到本局启动参数。", MessageType.Info);
            }
            GUI.backgroundColor = new Color(0.35f, 0.72f, 1f);
            if (GUILayout.Button("应用草稿", GUILayout.Width(96f)))
                ApplyDraft(runtime);
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            tuningScroll = EditorGUILayout.BeginScrollView(tuningScroll);
            string currentCategory = string.Empty;
            for (int index = 0; index < TuningFields.Length; index++)
            {
                TuningDescriptor descriptor = TuningFields[index];
                if (!string.Equals(currentCategory, descriptor.Category,
                        StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(currentCategory))
                        EditorGUILayout.Space(8f);
                    currentCategory = descriptor.Category;
                    DrawSectionTitle(currentCategory);
                }
                DrawTuningField(runtime, descriptor);
            }
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "名单总数、兵种组成和星球难度档不支持本局热改，避免敌人被重复生成、结算丢失或任务软锁。" +
                "这类变化请在难度配置资产中修改，并在下一局验证。",
                MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        void DrawTuningField(
            EdpcgEncounterRuntime runtime,
            TuningDescriptor descriptor)
        {
            if (!draft.TryGetValue(descriptor.Field, out float value))
                return;
            FieldInfo field = FindSettingsField(descriptor.Field);
            float liveValue = field == null
                ? value
                : Convert.ToSingle(
                    field.GetValue(runtime.Settings),
                    CultureInfo.InvariantCulture);
            bool differs = Mathf.Abs(liveValue - value) > 0.0001f;

            EditorGUILayout.BeginHorizontal();
            GUIContent label = new GUIContent(descriptor.Label, descriptor.Tooltip);
            EditorGUILayout.LabelField(label, GUILayout.Width(150f));
            bool isUnusedLegacyField = string.Equals(
                descriptor.Field,
                "fullSimulationCap",
                StringComparison.Ordinal);
            using (new EditorGUI.DisabledScope(isUnusedLegacyField))
            {
                EditorGUI.BeginChangeCheck();
                float candidate = descriptor.Integer
                    ? EditorGUILayout.IntSlider(
                        Mathf.RoundToInt(value),
                        Mathf.RoundToInt(descriptor.Minimum),
                        Mathf.RoundToInt(descriptor.Maximum),
                        GUILayout.MinWidth(260f))
                    : EditorGUILayout.Slider(
                        value,
                        descriptor.Minimum,
                        descriptor.Maximum,
                        GUILayout.MinWidth(260f));
                if (EditorGUI.EndChangeCheck())
                    draft[descriptor.Field] = descriptor.Integer
                        ? Mathf.Round(candidate)
                        : candidate;
            }

            GUIStyle policyStyle = descriptor.Policy ==
                                   EdpcgLiveApplyPolicy.ApplyAtSafeBoundary
                ? EditorStyles.miniButtonMid
                : EditorStyles.miniButton;
            GUILayout.Label(isUnusedLegacyField
                    ? "尚未接线"
                    : descriptor.Policy == EdpcgLiveApplyPolicy.ApplyAtSafeBoundary
                    ? "安全边界"
                    : "立即",
                policyStyle,
                GUILayout.Width(64f));

            using (new EditorGUI.DisabledScope(!differs || isUnusedLegacyField))
            {
                if (GUILayout.Button("↶", GUILayout.Width(28f)))
                {
                    draft[descriptor.Field] = liveValue;
                    submittedDraft.Remove(descriptor.Field);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawRosterAndEvents(EdpcgEncounterRuntime runtime)
        {
            EdpcgPressureSample sample = runtime.CurrentSample;
            EditorGUILayout.BeginHorizontal();
            DrawMetric("未生成", sample.unspawnedCount.ToString(), "固定名单中尚未入场");
            DrawMetric("排队", sample.queuedCount.ToString(), "等待生成预算");
            DrawMetric("活跃", sample.activeCount.ToString(), "当前战场对象");
            DrawMetric("导航恢复", sample.navigationRecoveryCount.ToString(), "可审计恢复状态");
            DrawMetric("已解决", sample.resolvedCount.ToString(), "击落 / 自爆 / 技术清场");
            DrawMetric("路线预留",
                runtime.Reservations.Reservations.Count.ToString(),
                "城市狭窄路线占用");
            EditorGUILayout.EndHorizontal();

            selectedDetailTab = (DetailTab)GUILayout.Toolbar(
                (int)selectedDetailTab,
                DetailTabLabels,
                GUILayout.Height(23f));
            switch (selectedDetailTab)
            {
                case DetailTab.Events:
                    DrawEventStream(runtime);
                    break;
                case DetailTab.TuningChanges:
                    DrawChangeLog(runtime);
                    break;
                case DetailTab.RouteReservations:
                    DrawReservations(runtime);
                    break;
                default:
                    DrawRoster(runtime);
                    break;
            }
        }

        void DrawRoster(EdpcgEncounterRuntime runtime)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("筛选", GUILayout.Width(34f));
            rosterSearch = GUILayout.TextField(rosterSearch,
                GUI.skin.FindStyle("ToolbarSearchTextField") ??
                EditorStyles.toolbarTextField,
                GUILayout.Width(210f));
            string[] stateOptions = BuildRosterStateOptions();
            rosterStateFilter = EditorGUILayout.Popup(
                rosterStateFilter,
                stateOptions,
                EditorStyles.toolbarPopup,
                GUILayout.Width(120f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "本局共 " + runtime.RosterCount + " 名；固定名单不会越额补刷",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            DrawRosterHeader();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            IReadOnlyList<EdpcgRosterMember> roster = runtime.Roster;
            for (int index = 0; index < roster.Count; index++)
            {
                EdpcgRosterMember member = roster[index];
                if (!RosterMatchesFilter(member))
                    continue;
                EditorGUILayout.BeginHorizontal(index % 2 == 0
                    ? EditorStyles.helpBox
                    : GUIStyle.none);
                GUILayout.Label(member.rosterIndex.ToString("00"), monoStyle,
                    GUILayout.Width(38f));
                GUILayout.Label(ShortId(member.rosterMemberId, 18), monoStyle,
                    GUILayout.Width(150f));
                GUILayout.Label(ChineseRole(member.role), GUILayout.Width(90f));
                GUILayout.Label(ChineseRosterState(member.state), GUILayout.Width(126f));
                GUILayout.Label(member.healthRatio.ToString("P0"), GUILayout.Width(56f));
                GUILayout.Label(member.spawnAttempts.ToString(), GUILayout.Width(52f));
                GUILayout.Label(member.resolutionReason == EdpcgEnemyResolutionReason.None
                    ? "-"
                    : ChineseResolutionReason(member.resolutionReason),
                    GUILayout.MinWidth(150f));
                GUILayout.Label(member.creditedKill ? "✓" : "-", GUILayout.Width(42f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawEventStream(EdpcgEncounterRuntime runtime)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("事件筛选", GUILayout.Width(56f));
            eventFilter = (EventFilter)EditorGUILayout.Popup(
                (int)eventFilter,
                EventFilterLabels,
                EditorStyles.toolbarPopup,
                GUILayout.Width(150f));
            GUILayout.FlexibleSpace();
            GUILayout.Label("最新事件在上", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            DrawEventHeader();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            IReadOnlyList<EdpcgTelemetryEvent> events = runtime.Recorder.Events;
            int rendered = 0;
            for (int index = events.Count - 1; index >= 0 && rendered < 300; index--)
            {
                EdpcgTelemetryEvent item = events[index];
                if (!EventMatchesFilter(item.kind))
                    continue;
                rendered++;
                EditorGUILayout.BeginHorizontal(rendered % 2 == 0
                    ? EditorStyles.helpBox
                    : GUIStyle.none);
                GUILayout.Label(item.missionTime.ToString("0.0") + " 秒", monoStyle,
                    GUILayout.Width(62f));
                GUILayout.Label(ChineseEvent(item.kind), GUILayout.Width(152f));
                GUILayout.Label(ShortId(item.rosterMemberId, 18), monoStyle,
                    GUILayout.Width(152f));
                GUILayout.Label(ShortId(item.areaId, 20), monoStyle,
                    GUILayout.Width(150f));
                GUILayout.Label(ShortId(item.routeId, 20), monoStyle,
                    GUILayout.Width(150f));
                GUILayout.Label(item.reasonCode, GUILayout.MinWidth(180f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawChangeLog(EdpcgEncounterRuntime runtime)
        {
            EditorGUILayout.HelpBox(
                "每次应用都有 change id、候选值、生效策略和状态。Pending 项会在下一阶段边界应用。",
                MessageType.None);
            DrawChangeHeader();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            IReadOnlyList<EdpcgRuntimeChange> changes = runtime.Recorder.Changes;
            for (int index = changes.Count - 1; index >= 0; index--)
            {
                EdpcgRuntimeChange change = changes[index];
                EditorGUILayout.BeginHorizontal(index % 2 == 0
                    ? EditorStyles.helpBox
                    : GUIStyle.none);
                GUILayout.Label(change.changeId, monoStyle, GUILayout.Width(88f));
                GUILayout.Label(change.fieldPath, monoStyle, GUILayout.Width(180f));
                GUILayout.Label(change.previousSerializedValue + " → " +
                    change.candidateSerializedValue, monoStyle, GUILayout.Width(150f));
                GUILayout.Label(ChineseApplyPolicy(change.applyPolicy),
                    GUILayout.Width(138f));
                GUILayout.Label(ChineseChangeState(change.state), GUILayout.Width(74f));
                GUILayout.Label(change.submittedAt.ToString("0.0") + " 秒",
                    GUILayout.Width(58f));
                bool canRevert = change.state == EdpcgRuntimeChangeState.Applied ||
                                 change.state == EdpcgRuntimeChangeState.Pending;
                using (new EditorGUI.DisabledScope(!canRevert))
                {
                    if (GUILayout.Button("回退", GUILayout.Width(56f)))
                    {
                        if (runtime.RevertChange(change.changeId))
                        {
                            SyncDraft();
                            SetStatus("已回退 " + change.changeId + "。",
                                MessageType.Info);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawReservations(EdpcgEncounterRuntime runtime)
        {
            EditorGUILayout.HelpBox(
                "自爆兵主要预留接近/冲刺通道；远程兵主要预留掩体侧翼和射击走廊。" +
                "租约到期必须释放，避免敌人永久等待形成软锁。",
                MessageType.None);
            DrawReservationHeader();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            IReadOnlyList<EdpcgRouteReservation> reservations =
                runtime.Reservations.Reservations;
            for (int index = 0; index < reservations.Count; index++)
            {
                EdpcgRouteReservation item = reservations[index];
                EditorGUILayout.BeginHorizontal(index % 2 == 0
                    ? EditorStyles.helpBox
                    : GUIStyle.none);
                GUILayout.Label(item.reservationId, monoStyle, GUILayout.Width(112f));
                GUILayout.Label(ShortId(item.ownerRosterMemberId, 18), monoStyle,
                    GUILayout.Width(150f));
                GUILayout.Label(ShortId(item.routeId, 24), monoStyle,
                    GUILayout.Width(190f));
                GUILayout.Label(item.enterAt.ToString("0.0") + " – " +
                    item.exitAt.ToString("0.0") + " 秒", GUILayout.Width(110f));
                GUILayout.Label(item.expiresAt.ToString("0.0") + " 秒",
                    GUILayout.Width(72f));
                GUILayout.Label(item.priority.ToString(), GUILayout.Width(52f));
                GUILayout.Label(item.direction.ToString(), GUILayout.Width(48f));
                EditorGUILayout.EndHorizontal();
            }
            if (reservations.Count == 0)
                EditorGUILayout.HelpBox("当前没有路线预留。", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        void DrawProfile()
        {
            profileScroll = EditorGUILayout.BeginScrollView(profileScroll);
            DrawSectionTitle("六档难度资产");
            EditorGUILayout.BeginHorizontal();
            profile = (EdpcgDifficultyProfile)EditorGUILayout.ObjectField(
                "持久化配置",
                profile,
                typeof(EdpcgDifficultyProfile),
                false);
            if (profile == null)
            {
                if (GUILayout.Button("创建默认配置", GUILayout.Width(110f)))
                {
                    profile = CreateProfileAsset();
                    SetStatus("已创建 " + ProfileAssetPath, MessageType.Info);
                }
            }
            else
            {
                if (GUILayout.Button("在 Project 中定位", GUILayout.Width(120f)))
                {
                    Selection.activeObject = profile;
                    EditorGUIUtility.PingObject(profile);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (profile == null)
            {
                EditorGUILayout.HelpBox(
                    "尚未创建 EDPCG 配置资产。运行时会使用内存默认值，但无法持久化调参结果。",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            profile.EnsureInitialized();
            DrawProfileHeader();
            for (int index = 0; index < profile.tiers.Length; index++)
            {
                EdpcgTierSettings tier = profile.tiers[index];
                EditorGUILayout.BeginHorizontal(index == selectedProfileTier
                    ? EditorStyles.helpBox
                    : GUIStyle.none);
                if (GUILayout.Toggle(index == selectedProfileTier,
                        "难度 " + (index + 1), "Button", GUILayout.Width(64f)))
                {
                    selectedProfileTier = index;
                }
                GUILayout.Label(tier.rosterCount.ToString(), GUILayout.Width(52f));
                GUILayout.Label(tier.interceptorCount + " / " + tier.strikerCount +
                    " / " + tier.gunshipCount, GUILayout.Width(110f));
                GUILayout.Label(tier.populationCap + " / " + tier.engagementCap,
                    GUILayout.Width(92f));
                GUILayout.Label(tier.attackTokenCap + " / " +
                    tier.suicideCommitCap + " / " + tier.rangedFireLaneCap,
                    GUILayout.Width(108f));
                GUILayout.Label(tier.engagePressureMin.ToString("P0") + " – " +
                    tier.engagePressureMax.ToString("P0"), GUILayout.Width(92f));
                GUILayout.Label(tier.peakPressureMin.ToString("P0") + " – " +
                    tier.peakPressureMax.ToString("P0"), GUILayout.Width(92f));
                GUILayout.Label(tier.maximumStrategyLevel.ToString(), GUILayout.Width(48f));
                GUILayout.Label(tier.spawnIntervalSeconds.ToString("0.00") + " 秒",
                    GUILayout.Width(60f));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8f);
            DrawSelectedTierSummary(profile.tiers[Mathf.Clamp(
                selectedProfileTier, 0, profile.tiers.Length - 1)]);
            EdpcgTierSettings selectedTier = profile.tiers[Mathf.Clamp(
                selectedProfileTier, 0, profile.tiers.Length - 1)];
            int tacticalMode = selectedTier.integrationMode >=
                               EdpcgIntegrationMode.TacticalAssignments
                ? 1
                : 0;
            EditorGUI.BeginChangeCheck();
            tacticalMode = EditorGUILayout.Popup(
                new GUIContent(
                    "城市战术运行方式",
                    "兼容模式保留旧的远程兵共用选区；角色化分配让突击机与炮艇使用不同合法区域和路线。下一局生效。"),
                tacticalMode,
                new[] { "兼容模式", "角色化城市战术分配" });
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "修改EDPCG城市战术运行方式");
                selectedTier.integrationMode = tacticalMode == 0
                    ? EdpcgIntegrationMode.Legacy
                    : EdpcgIntegrationMode.TacticalAssignments;
                selectedTier.ValidateInPlace();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                SetStatus("城市战术运行方式已保存，将在下一局生效。",
                    MessageType.Info);
            }

            if (HasActiveRuntime())
            {
                EditorGUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(
                           boundRuntime.Settings == null || profile == null))
                {
                    if (GUILayout.Button(
                            "将本局当前参数保存到难度档 " +
                            boundRuntime.Settings.planetTier + "（下一局生效）",
                            GUILayout.Height(28f)))
                    {
                        SaveRuntimeTierToProfile(boundRuntime);
                    }
                }
            }
            EditorGUILayout.HelpBox(
                "最高难度名单由配置决定，当前为 " +
                profile.tiers[profile.tiers.Length - 1].rosterCount +
                " 架。此页只展示曲线和关键预算；要编辑完整持久化字段，" +
                "请定位配置资产后在 Inspector 中修改。Boss 配置不在此工具范围内。",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        void DrawCityContext(EdpcgEncounterRuntime runtime)
        {
            DrawCityContext(
                runtime != null ? runtime.TacticalMap : null,
                runtime != null ? runtime.CurrentSample : null,
                runtime != null && runtime.Reservations != null
                    ? runtime.Reservations.Reservations.Count
                    : 0);
        }

        void DrawCityContext(
            EdpcgCityTacticalRuntimeMap map,
            EdpcgPressureSample sample,
            int reservationCount)
        {
            EditorGUILayout.Space(8f);
            DrawSectionTitle("PCG 城市语义与当前路径压力");
            if (map == null || !map.HasCityData)
            {
                EditorGUILayout.HelpBox(
                    "当前城市没有可用的 EDPCG 战术区/路线语义。敌群调度会降级运行，但城市减压与侧袭策略不可验证。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                "当前战术区",
                sample == null || string.IsNullOrEmpty(sample.activeTacticalAreaId)
                    ? "城市外／无战术区"
                    : ChineseAreaKind(sample.activeTacticalAreaKind));
            EditorGUILayout.LabelField(
                "城市数据",
                "区域 " + map.Areas.Count +
                "   路线 " + map.Routes.Count +
                "   敌机入口 " + map.Ingresses.Count +
                "   当前路线预留 " + reservationCount +
                "   区块意图箭头 " + map.TacticalArrows.Count);

            int[] areaCounts = new int[Enum.GetValues(typeof(EdpcgTacticalAreaKind)).Length];
            for (int index = 0; index < map.Areas.Count; index++)
            {
                int kind = (int)map.Areas[index].Kind;
                if (kind >= 0 && kind < areaCounts.Length)
                    areaCounts[kind]++;
            }
            EditorGUILayout.LabelField(
                "区域覆盖",
                "出生安全 " + areaCounts[(int)EdpcgTacticalAreaKind.SpawnSafeAirspace] +
                "   中央机动 " + areaCounts[(int)EdpcgTacticalAreaKind.CentralManeuverDistrict] +
                "   高楼掩体链 " + areaCounts[(int)EdpcgTacticalAreaKind.HighRiseOcclusionChain] +
                "   开放捷径 " + areaCounts[(int)EdpcgTacticalAreaKind.ExposedFireShortcut] +
                "   三面磁场 " + areaCounts[(int)EdpcgTacticalAreaKind.MagneticCourtyard] +
                "   高度换层 " + areaCounts[(int)EdpcgTacticalAreaKind.LowMidVerticalTransition]);
        }

        void DrawExportContents()
        {
            EditorGUILayout.Space(8f);
            DrawSectionTitle("导出内容");
            EditorGUILayout.HelpBox(
                "“导出全部”会生成压力曲线 PNG 与可编辑 SVG、逐帧遥测 CSV、完整会话 JSON、" +
                "可浏览 HTML 报告、manifest，以及实际压力/预测/目标带/敌情/寻路/城市/玩家负荷等 SVG 图标。",
                MessageType.None);
            if (!string.IsNullOrEmpty(lastExportFolder))
                EditorGUILayout.SelectableLabel(lastExportFolder,
                    EditorStyles.textField, GUILayout.Height(18f));
        }

        void ApplyDraft(EdpcgEncounterRuntime runtime)
        {
            int submitted = 0;
            for (int index = 0; index < TuningFields.Length; index++)
            {
                TuningDescriptor descriptor = TuningFields[index];
                FieldInfo field = FindSettingsField(descriptor.Field);
                if (field == null ||
                    !draft.TryGetValue(descriptor.Field, out float candidate))
                {
                    continue;
                }

                float current = Convert.ToSingle(
                    field.GetValue(runtime.Settings),
                    CultureInfo.InvariantCulture);
                bool alreadySubmitted = submittedDraft.TryGetValue(
                    descriptor.Field, out float submittedValue) &&
                    Mathf.Abs(submittedValue - candidate) < 0.0001f;
                if (Mathf.Abs(current - candidate) < 0.0001f || alreadySubmitted)
                    continue;

                string serialized = descriptor.Integer
                    ? Mathf.RoundToInt(candidate).ToString(
                        CultureInfo.InvariantCulture)
                    : candidate.ToString("R", CultureInfo.InvariantCulture);
                if (!runtime.TryApplyTuning(
                        descriptor.Field,
                        serialized,
                        descriptor.Policy,
                        out _,
                        out string error))
                {
                    SetStatus("应用失败：" + error, MessageType.Error);
                    return;
                }
                submittedDraft[descriptor.Field] = candidate;
                submitted++;
            }

            SetStatus(
                submitted == 0
                    ? "草稿与运行时一致，没有重复提交。"
                    : "已提交 " + submitted +
                      " 项变更；带“安全边界”的项目将在下一次阶段切换时生效。",
                MessageType.Info);
        }

        void SyncDraft()
        {
            draft.Clear();
            submittedDraft.Clear();
            if (boundRuntime == null || boundRuntime.Settings == null)
                return;
            for (int index = 0; index < TuningFields.Length; index++)
            {
                FieldInfo field = FindSettingsField(TuningFields[index].Field);
                if (field == null)
                    continue;
                float value = Convert.ToSingle(
                    field.GetValue(boundRuntime.Settings),
                    CultureInfo.InvariantCulture);
                draft[TuningFields[index].Field] = value;
                submittedDraft[TuningFields[index].Field] = value;
            }
        }

        void AddBookmark()
        {
            if (!HasActiveRuntime())
                return;
            string bookmark = "editor-bookmark-" +
                              (++bookmarkSequence).ToString("D3");
            boundRuntime.Recorder.RecordEvent(
                boundRuntime.Elapsed,
                EdpcgTelemetryEventKind.Bookmark,
                bookmark);
            SetStatus("已记录书签 " + bookmark + "。", MessageType.Info);
        }

        void ExportSession(EdpcgEncounterRuntime runtime)
        {
            try
            {
                lastExportFolder = EdpcgExportService.ExportAll(runtime);
                SetStatus("导出完成：" + lastExportFolder, MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus("导出失败：" + exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        void SaveRuntimeTierToProfile(EdpcgEncounterRuntime runtime)
        {
            if (profile == null)
                profile = CreateProfileAsset();
            profile.EnsureInitialized();
            int tierIndex = Mathf.Clamp(runtime.Settings.planetTier - 1, 0, 5);
            profile.tiers[tierIndex] = runtime.Settings.ValidatedCopy();
            profile.EnsureInitialized();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            SetStatus(
                "已保存难度档 " + (tierIndex + 1) + "，将在下一局生效。",
                MessageType.Info);
        }

        void BindRuntime()
        {
            EdpcgEncounterRuntime runtime = EdpcgRuntimeRegistry.Active;
            bool changed = runtime != boundRuntime;
            if (changed)
                boundRuntime = runtime;

            RefreshScenePreview();
            if (!changed)
                return;

            graphFrozen = false;
            detailScroll = Vector2.zero;
            SyncDraft();
            if (runtime != null)
            {
                SetStatus(
                    "已连接 " + ChineseMission(runtime.MissionId) + "（难度档 " +
                    (runtime.Settings != null
                        ? runtime.Settings.planetTier.ToString()
                        : "-") + "）。",
                    MessageType.Info);
            }
        }

        void RefreshScenePreview()
        {
            if (HasActiveRuntime() && boundRuntime.TacticalMap != null &&
                boundRuntime.UrbanRuntime != null)
            {
                scenePreviewUrban = boundRuntime.UrbanRuntime;
                scenePreviewLab = null;
                scenePreviewMap = boundRuntime.TacticalMap;
                scenePreviewPlan = scenePreviewUrban.Plan;
                RefreshGridFireAnalysis(false);
                return;
            }

            FinitePlanetUrbanCombatRuntime urban =
                UnityEngine.Object.FindFirstObjectByType<
                    FinitePlanetUrbanCombatRuntime>(FindObjectsInactive.Include);
            AirCombatCityPlan plan = urban != null && urban.IsReady
                ? urban.Plan
                : null;
            AirCombatCityPcgLab lab = urban == null
                ? UnityEngine.Object.FindFirstObjectByType<AirCombatCityPcgLab>(
                    FindObjectsInactive.Include)
                : null;
            if (plan == null && lab != null)
                plan = lab.Plan;

            if (urban == scenePreviewUrban && lab == scenePreviewLab &&
                plan == scenePreviewPlan &&
                scenePreviewMap != null)
            {
                RefreshGridFireAnalysis(false);
                return;
            }

            scenePreviewUrban = urban;
            scenePreviewLab = lab;
            scenePreviewPlan = plan;
            scenePreviewMap = plan == null
                ? null
                : urban != null
                    ? EdpcgCityTacticalRuntimeMap.Build(urban)
                    : EdpcgCityTacticalRuntimeMap.Build(lab);
            sceneGridFireSignature = 0;
            RefreshGridFireAnalysis(true);
        }

        void RefreshGridFireAnalysis(bool force)
        {
            if (sceneGridFireAnalysisStale && !force)
                return;
            if (force)
                sceneGridFireAnalysisStale = false;
            if (HasActiveRuntime() &&
                boundRuntime.ActiveGridFireAnalysis != null)
            {
                sceneGridFireAnalysis =
                    boundRuntime.ActiveGridFireAnalysis;
                if (sceneFollowPlayerGrid &&
                    boundRuntime.ActiveGridFireCell != null)
                {
                    sceneSelectedGridX =
                        boundRuntime.ActiveGridFireCell.gridX;
                    sceneSelectedGridZ =
                        boundRuntime.ActiveGridFireCell.gridZ;
                }
                sceneChallengeReport = boundRuntime.CityChallengeReport;
                return;
            }
            if (!force && sceneGridFireAnalysis != null &&
                EditorApplication.timeSinceStartup <
                nextSceneThreatSignatureCheckAt)
            {
                return;
            }
            nextSceneThreatSignatureCheckAt =
                EditorApplication.timeSinceStartup + 0.5d;
            AirCombatCityRuntimeGeometrySnapshot snapshot =
                scenePreviewUrban != null
                    ? scenePreviewUrban.RuntimeGeometrySnapshot
                    : scenePreviewLab != null
                        ? scenePreviewLab.RuntimeGeometrySnapshot
                        : null;
            if (scenePreviewPlan == null || snapshot == null ||
                !snapshot.IsUsable)
            {
                sceneGridFireAnalysis = null;
                for (int layerIndex = 0;
                     layerIndex < sceneLayerFireAnalyses.Length;
                     layerIndex++)
                {
                    sceneLayerFireAnalyses[layerIndex] = null;
                }
                sceneChallengeReport = null;
                sceneGridFireSignature = 0;
                return;
            }

            EdpcgAirThreatAnalysisOptions threatOptions =
                BuildSceneThreatOptions();
            int signature = BuildGridFireSignature(
                scenePreviewPlan, snapshot, selectedCityChallengeTier,
                threatOptions);
            int selectedLayer = Mathf.Clamp(sceneFireAltitudeLayer, 0, 2);
            if (!force && sceneLayerFireAnalyses[selectedLayer] != null &&
                sceneGridFireSignature == signature)
            {
                sceneGridFireAnalysis =
                    sceneLayerFireAnalyses[selectedLayer];
                return;
            }

            for (int layerIndex = 0;
                 layerIndex < sceneLayerFireAnalyses.Length;
                 layerIndex++)
            {
                EdpcgFireAnalysisAltitudeLayer layer =
                    (EdpcgFireAnalysisAltitudeLayer)layerIndex;
                sceneLayerFireAnalyses[layerIndex] = scenePreviewUrban != null
                    ? EdpcgGridFireAnalyzer.Build(
                        scenePreviewUrban, layer, -1f, threatOptions)
                    : EdpcgGridFireAnalyzer.Build(
                        scenePreviewLab, layer, -1f, threatOptions);
            }
            EdpcgGridFireAnalyzer.LinkAltitudeLayers(
                sceneLayerFireAnalyses[0],
                sceneLayerFireAnalyses[1],
                sceneLayerFireAnalyses[2],
                snapshot,
                threatOptions);
            sceneGridFireAnalysis = sceneLayerFireAnalyses[selectedLayer];
            sceneGridFireSignature = signature;
            sceneGridFireAnalysisStale = false;
            RefreshSceneChallengeReport();
        }

        void FlushPendingCityChallengeSave(bool force = false)
        {
            if (cityChallengeSaveAt <= 0d)
                return;
            if (!force &&
                (EditorApplication.timeSinceStartup < cityChallengeSaveAt ||
                 GUIUtility.hotControl != 0))
            {
                return;
            }
            cityChallengeSaveAt = 0d;
            bool challengeDirty = cityChallengeProfile != null &&
                                  EditorUtility.IsDirty(
                                      cityChallengeProfile);
            bool geometryDirty = cityChallengeProfile != null &&
                                 cityChallengeProfile.cityGeometryProfile !=
                                 null &&
                                 EditorUtility.IsDirty(
                                     cityChallengeProfile
                                         .cityGeometryProfile);
            if (challengeDirty || geometryDirty)
            {
                AssetDatabase.SaveAssets();
            }
        }

        void RefreshSceneChallengeReport()
        {
            if (HasActiveRuntime() &&
                boundRuntime.CityChallengeReport != null &&
                boundRuntime.CityChallengeReport.evaluable)
            {
                sceneChallengeReport = boundRuntime.CityChallengeReport;
                return;
            }
            if (cityChallengeProfile == null)
            {
                sceneChallengeReport = null;
                return;
            }
            EdpcgCityTacticalChallengeSettings challenge =
                cityChallengeProfile.Resolve(selectedCityChallengeTier);
            sceneChallengeReport =
                EdpcgCityTacticalChallengeEvaluator.Evaluate(
                    challenge,
                    sceneLayerFireAnalyses[0],
                    sceneLayerFireAnalyses[1],
                    sceneLayerFireAnalyses[2],
                    scenePreviewMap);
        }

        void SelectGridFireCell(int gridX, int gridZ)
        {
            sceneSelectedGridX = Mathf.Clamp(gridX, 0, 7);
            sceneSelectedGridZ = Mathf.Clamp(gridZ, 0, 7);
            UpdateSceneOverlay();
            Repaint();
        }

        bool HasCityPreview()
        {
            return (scenePreviewUrban != null || scenePreviewLab != null) &&
                   scenePreviewMap != null &&
                   scenePreviewMap.HasCityData;
        }

        bool HasActiveRuntime()
        {
            return boundRuntime != null &&
                   boundRuntime.IsRunning &&
                   boundRuntime.Settings != null &&
                   boundRuntime.Recorder != null;
        }

        void HandlePlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                boundRuntime = null;
                scenePreviewUrban = null;
                scenePreviewLab = null;
                scenePreviewMap = null;
                scenePreviewPlan = null;
                sceneGridFireAnalysis = null;
                sceneGridFireSignature = 0;
                draft.Clear();
                submittedDraft.Clear();
                graphFrozen = false;
            }
            Repaint();
        }

        bool RosterMatchesFilter(EdpcgRosterMember member)
        {
            if (rosterStateFilter > 0 &&
                (int)member.state != rosterStateFilter - 1)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(rosterSearch))
                return true;
            string needle = rosterSearch.Trim();
            return member.rosterMemberId.IndexOf(
                       needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   member.role.ToString().IndexOf(
                       needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   member.state.ToString().IndexOf(
                       needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool EventMatchesFilter(EdpcgTelemetryEventKind kind)
        {
            switch (eventFilter)
            {
                case EventFilter.Navigation:
                    return kind == EdpcgTelemetryEventKind.PathRequested ||
                           kind == EdpcgTelemetryEventKind.PathFailed ||
                           kind == EdpcgTelemetryEventKind.NavigationRecovery ||
                           kind == EdpcgTelemetryEventKind.ReservationCreated ||
                           kind == EdpcgTelemetryEventKind.ReservationReleased;
                case EventFilter.Combat:
                    return kind == EdpcgTelemetryEventKind.Spawned ||
                           kind == EdpcgTelemetryEventKind.AttackTokenAcquired ||
                           kind == EdpcgTelemetryEventKind.AttackTokenReleased ||
                           kind == EdpcgTelemetryEventKind.Damage ||
                           kind == EdpcgTelemetryEventKind.Collision ||
                           kind == EdpcgTelemetryEventKind.EnemyResolved;
                case EventFilter.City:
                    return kind == EdpcgTelemetryEventKind.AreaEntered ||
                           kind == EdpcgTelemetryEventKind.AreaExited ||
                           kind == EdpcgTelemetryEventKind.ReservationCreated ||
                           kind == EdpcgTelemetryEventKind.ReservationReleased;
                case EventFilter.TuningAndValidation:
                    return kind == EdpcgTelemetryEventKind.TuningChange ||
                           kind == EdpcgTelemetryEventKind.ValidationError ||
                           kind == EdpcgTelemetryEventKind.Bookmark;
                default:
                    return true;
            }
        }

        static string[] BuildRosterStateOptions()
        {
            EdpcgRosterState[] states = (EdpcgRosterState[])Enum.GetValues(
                typeof(EdpcgRosterState));
            string[] result = new string[states.Length + 1];
            result[0] = "全部状态";
            for (int index = 0; index < states.Length; index++)
                result[index + 1] = ChineseRosterState(states[index]);
            return result;
        }

        static FieldInfo FindSettingsField(string fieldName)
        {
            return typeof(EdpcgTierSettings).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public);
        }

        static EdpcgDifficultyProfile CreateProfileAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/EDPCG"))
                AssetDatabase.CreateFolder("Assets/Resources", "EDPCG");
            EdpcgDifficultyProfile existing = AssetDatabase.LoadAssetAtPath<
                EdpcgDifficultyProfile>(ProfileAssetPath);
            if (existing != null)
                return existing;
            EdpcgDifficultyProfile created = CreateInstance<
                EdpcgDifficultyProfile>();
            created.EnsureInitialized();
            AssetDatabase.CreateAsset(created, ProfileAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = created;
            return created;
        }

        static EdpcgCityTacticalChallengeProfile
            CreateCityChallengeProfileAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/EDPCG"))
                AssetDatabase.CreateFolder("Assets/Resources", "EDPCG");
            EdpcgCityTacticalChallengeProfile existing =
                AssetDatabase.LoadAssetAtPath<
                    EdpcgCityTacticalChallengeProfile>(
                    CityChallengeProfileAssetPath);
            if (existing != null)
                return existing;

            EdpcgDifficultyProfile enemyProfile =
                AssetDatabase.LoadAssetAtPath<EdpcgDifficultyProfile>(
                    ProfileAssetPath) ?? CreateProfileAsset();
            CombatCityPcgDesignProfile cityProfile =
                AssetDatabase.LoadAssetAtPath<CombatCityPcgDesignProfile>(
                    CityGeometryProfileAssetPath);
            EdpcgCityTacticalChallengeProfile created = CreateInstance<
                EdpcgCityTacticalChallengeProfile>();
            created.cityGeometryProfile = cityProfile;
            created.ordinaryEnemyProfile = enemyProfile;
            created.EnsureInitialized();
            AssetDatabase.CreateAsset(created, CityChallengeProfileAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = created;
            return created;
        }

        void EnsureStyles()
        {
            if (sectionStyle == null)
            {
                sectionStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(2, 2, 5, 4)
                };
            }
            if (monoStyle == null)
            {
                monoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf")
                        as Font
                };
            }
        }

        void DrawStatusBar()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(status, statusType);
        }

        void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
            Repaint();
        }

        void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, sectionStyle);
        }

        static void DrawMetric(string label, string value, string hint)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox,
                GUILayout.MinWidth(112f), GUILayout.ExpandWidth(true));
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.Label(value, EditorStyles.boldLabel);
            GUILayout.Label(hint, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        static string ChinesePhase(EdpcgEncounterPhase phase)
        {
            switch (phase)
            {
                case EdpcgEncounterPhase.Preview: return "预览";
                case EdpcgEncounterPhase.Engage: return "接战";
                case EdpcgEncounterPhase.Peak: return "峰值";
                case EdpcgEncounterPhase.Recover: return "恢复";
                default: return "未知";
            }
        }

        static string ChineseIntegrationMode(EdpcgIntegrationMode mode)
        {
            switch (mode)
            {
                case EdpcgIntegrationMode.Legacy: return "兼容模式（旧选区规则）";
                case EdpcgIntegrationMode.ObserveOnly: return "仅观测";
                case EdpcgIntegrationMode.TacticalAssignments:
                    return "角色化城市战术分配";
                case EdpcgIntegrationMode.PressureV3Control:
                    return "第三版压力控制（未开放）";
                default: return "未知";
            }
        }

        static string ChineseMission(string missionId)
        {
            if (string.Equals(missionId, "wind_canyon",
                    StringComparison.Ordinal))
            {
                return "风谷扫描任务";
            }
            if (string.Equals(missionId, "industrial_outpost",
                    StringComparison.Ordinal))
            {
                return "工业据点突袭任务";
            }
            if (string.Equals(missionId, "modular_boss",
                    StringComparison.Ordinal))
            {
                return "模块化首领任务";
            }
            if (string.IsNullOrWhiteSpace(missionId))
                return "普通清场任务";
            return "普通清场任务";
        }

        static string ChineseAreaKind(EdpcgTacticalAreaKind kind)
        {
            switch (kind)
            {
                case EdpcgTacticalAreaKind.SpawnSafeAirspace:
                    return "出生缓冲区";
                case EdpcgTacticalAreaKind.CentralManeuverDistrict:
                    return "中央机动区";
                case EdpcgTacticalAreaKind.HighRiseOcclusionChain:
                    return "高楼遮挡区";
                case EdpcgTacticalAreaKind.ExposedFireShortcut:
                    return "暴露火力捷径";
                case EdpcgTacticalAreaKind.MagneticCourtyard:
                    return "磁场诱敌区";
                case EdpcgTacticalAreaKind.LowMidVerticalTransition:
                    return "高度换层区";
                default:
                    return "未分类战术区";
            }
        }

        bool WindowMatchesDisplay(EdpcgAirFireWindow window)
        {
            if (window == null)
                return false;
            EdpcgAirThreatRoleFilter role =
                (EdpcgAirThreatRoleFilter)Mathf.Clamp(
                    sceneThreatRoleFilter, 0, 2);
            if (role == EdpcgAirThreatRoleFilter.Striker &&
                window.sourceKind != EdpcgGridFireSourceKind.Striker)
                return false;
            if (role == EdpcgAirThreatRoleFilter.Gunship &&
                window.sourceKind != EdpcgGridFireSourceKind.Gunship)
                return false;
            if (!sceneShowGapWindows && window.throughBuildingGap)
                return false;
            switch ((EdpcgAirThreatViewMode)Mathf.Clamp(
                        sceneThreatViewMode, 0, 3))
            {
                case EdpcgAirThreatViewMode.WithinFourSeconds:
                    return window.firstHitSeconds <= 4f;
                case EdpcgAirThreatViewMode.WithinEightSeconds:
                    return window.firstHitSeconds <= 8f;
                case EdpcgAirThreatViewMode.AuthorizedByEdpcg:
                    return window.authorizedByTier;
                default:
                    return true;
            }
        }

        bool LineMatchesRole(EdpcgGridFireLine line)
        {
            if (line == null)
                return false;
            EdpcgAirThreatRoleFilter role =
                (EdpcgAirThreatRoleFilter)Mathf.Clamp(
                    sceneThreatRoleFilter, 0, 2);
            return role == EdpcgAirThreatRoleFilter.All ||
                   role == EdpcgAirThreatRoleFilter.Striker &&
                   line.sourceKind == EdpcgGridFireSourceKind.Striker ||
                   role == EdpcgAirThreatRoleFilter.Gunship &&
                   line.sourceKind == EdpcgGridFireSourceKind.Gunship;
        }

        void BuildFilteredCellSummary(
            EdpcgGridFireCell cell,
            out int directionCount,
            out float threatenedVolume,
            out float fastestHit,
            out int gapWindowCount)
        {
            int low = 0;
            int mid = 0;
            int high = 0;
            int sampleMask = 0;
            fastestHit = float.PositiveInfinity;
            gapWindowCount = 0;
            EdpcgAirThreatViewMode mode =
                (EdpcgAirThreatViewMode)Mathf.Clamp(
                    sceneThreatViewMode, 0, 3);
            if (mode == EdpcgAirThreatViewMode.Potential)
            {
                for (int index = 0; index < cell.fireLines.Count; index++)
                {
                    EdpcgGridFireLine line = cell.fireLines[index];
                    if (line == null || !line.incoming ||
                        !LineMatchesRole(line) ||
                        (!sceneShowGapWindows &&
                         line.throughBuildingGap))
                    {
                        continue;
                    }
                    SetDirectionBit(ref low, ref mid, ref high,
                        line.threatSector, line.elevationBand);
                    sampleMask |= line.threatenedSampleMask;
                }
            }
            for (int index = 0; index < cell.fireWindows.Count; index++)
            {
                EdpcgAirFireWindow window = cell.fireWindows[index];
                if (!WindowMatchesDisplay(window))
                    continue;
                fastestHit = Mathf.Min(
                    fastestHit, window.firstHitSeconds);
                sampleMask |= window.threatenedSampleMask;
                if (window.throughBuildingGap)
                    gapWindowCount++;
                if (mode != EdpcgAirThreatViewMode.Potential)
                {
                    SetDirectionBit(ref low, ref mid, ref high,
                        window.azimuthSector, window.elevationBand);
                }
            }
            directionCount = mode ==
                             EdpcgAirThreatViewMode.AuthorizedByEdpcg
                ? CountBits(low | mid | high)
                : CountBits(low) + CountBits(mid) + CountBits(high);
            threatenedVolume = cell.flyableSubSampleCount <= 0
                ? 0f
                : CountBits(sampleMask) /
                  (float)cell.flyableSubSampleCount;
        }

        static void SetDirectionBit(
            ref int low,
            ref int mid,
            ref int high,
            int azimuth,
            int elevation)
        {
            int bit = 1 << Mathf.Clamp(azimuth, 0, 7);
            if (elevation < 0)
                low |= bit;
            else if (elevation > 0)
                high |= bit;
            else
                mid |= bit;
        }

        bool HasFilteredCellCrossfire(EdpcgGridFireCell cell)
        {
            for (int first = 0; first < cell.fireWindows.Count; first++)
            {
                EdpcgAirFireWindow left = cell.fireWindows[first];
                if (!WindowMatchesDisplay(left))
                    continue;
                for (int second = first + 1;
                     second < cell.fireWindows.Count;
                     second++)
                {
                    EdpcgAirFireWindow right = cell.fireWindows[second];
                    if (!WindowMatchesDisplay(right))
                        continue;
                    int separation = Mathf.Abs(
                        left.azimuthSector - right.azimuthSector);
                    separation = Mathf.Min(separation, 8 - separation);
                    bool separated = separation >= 2 ||
                        separation >= 1 &&
                        left.elevationBand != right.elevationBand;
                    float overlap = Mathf.Min(
                        left.approachSeconds + left.visibleWindowSeconds,
                        right.approachSeconds + right.visibleWindowSeconds) -
                        Mathf.Max(left.setupSeconds, right.setupSeconds);
                    bool independent = !string.Equals(
                        left.stableId, right.stableId,
                        StringComparison.Ordinal) &&
                        (!string.Equals(left.routeId, right.routeId,
                            StringComparison.Ordinal) ||
                         Vector3.Distance(left.firingWorldPosition,
                             right.firingWorldPosition) >= 24f);
                    if (separated && independent && overlap >= 0.25f)
                        return true;
                }
            }
            return false;
        }

        static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }

        static string ChineseElevationBand(int elevationBand)
        {
            return elevationBand > 0 ? "上方" :
                elevationBand < 0 ? "下方" : "同层";
        }

        static string ChineseFireSource(EdpcgGridFireSourceKind kind)
        {
            return kind == EdpcgGridFireSourceKind.Gunship
                ? "炮艇"
                : "突击机";
        }

        static string ChineseThreatSector(int sector)
        {
            switch (sector & 7)
            {
                case 0: return "北侧";
                case 1: return "东北";
                case 2: return "东侧";
                case 3: return "东南";
                case 4: return "南侧";
                case 5: return "西南";
                case 6: return "西侧";
                default: return "西北";
            }
        }

        static string ChineseFireAltitude(
            EdpcgFireAnalysisAltitudeLayer layer)
        {
            switch (layer)
            {
                case EdpcgFireAnalysisAltitudeLayer.Low:
                    return "低空层";
                case EdpcgFireAnalysisAltitudeLayer.High:
                    return "高空层";
                default:
                    return "中空层";
            }
        }

        static string ChinesePuzzleKind(EdpcgCityTacticalPuzzleKind kind)
        {
            switch (kind)
            {
                case EdpcgCityTacticalPuzzleKind.SingleSidePressure:
                    return "单侧火力压迫";
                case EdpcgCityTacticalPuzzleKind.CrossfireBreak:
                    return "交叉火力拆解";
                case EdpcgCityTacticalPuzzleKind.CoverRelay:
                    return "连续换掩体";
                case EdpcgCityTacticalPuzzleKind.TrapLure:
                    return "诱敌进入陷阱";
                case EdpcgCityTacticalPuzzleKind.VerticalPressure:
                    return "垂直高度压力";
                default:
                    return "开放捷径取舍";
            }
        }

        static string ChineseRole(HordeEnemyRole role)
        {
            switch (role)
            {
                case HordeEnemyRole.Interceptor: return "自爆截击机";
                case HordeEnemyRole.Striker: return "突击机";
                case HordeEnemyRole.Gunship: return "炮艇";
                default: return "未知兵种";
            }
        }

        static string ChineseRosterState(EdpcgRosterState state)
        {
            switch (state)
            {
                case EdpcgRosterState.Unspawned: return "未生成";
                case EdpcgRosterState.Queued: return "排队";
                case EdpcgRosterState.SpawnRecovery: return "生成恢复";
                case EdpcgRosterState.Active: return "活跃";
                case EdpcgRosterState.NavigationRecovery: return "导航恢复";
                case EdpcgRosterState.Resolved: return "已解决";
                default: return "未知";
            }
        }

        static string ChineseResolutionReason(EdpcgEnemyResolutionReason reason)
        {
            switch (reason)
            {
                case EdpcgEnemyResolutionReason.KilledByPlayer: return "玩家击毁";
                case EdpcgEnemyResolutionReason.PlayerCausedEnvironment: return "玩家诱导环境击毁";
                case EdpcgEnemyResolutionReason.SelfDetonated: return "自行引爆";
                case EdpcgEnemyResolutionReason.NavigationRecovery: return "导航恢复";
                case EdpcgEnemyResolutionReason.TechnicalFinalClear: return "技术清场";
                default: return "无";
            }
        }

        static string ChineseApplyPolicy(EdpcgLiveApplyPolicy policy)
        {
            switch (policy)
            {
                case EdpcgLiveApplyPolicy.ApplyNow: return "立即生效";
                case EdpcgLiveApplyPolicy.ApplyAtSafeBoundary: return "安全边界生效";
                case EdpcgLiveApplyPolicy.RequiresRestart: return "下局生效";
                default: return "未知";
            }
        }

        static string ChineseChangeState(EdpcgRuntimeChangeState state)
        {
            switch (state)
            {
                case EdpcgRuntimeChangeState.Draft: return "草稿";
                case EdpcgRuntimeChangeState.Pending: return "等待";
                case EdpcgRuntimeChangeState.Applied: return "已应用";
                case EdpcgRuntimeChangeState.Reverted: return "已回退";
                case EdpcgRuntimeChangeState.Cancelled: return "已取消";
                default: return "未知";
            }
        }

        static string ChineseEvent(EdpcgTelemetryEventKind kind)
        {
            switch (kind)
            {
                case EdpcgTelemetryEventKind.SessionStarted: return "会话开始";
                case EdpcgTelemetryEventKind.SessionEnded: return "会话结束";
                case EdpcgTelemetryEventKind.PhaseChanged: return "阶段变化";
                case EdpcgTelemetryEventKind.SpawnQueued: return "加入生成队列";
                case EdpcgTelemetryEventKind.Spawned: return "已生成";
                case EdpcgTelemetryEventKind.SpawnRecovery: return "生成恢复";
                case EdpcgTelemetryEventKind.AttackTokenAcquired: return "取得攻击令牌";
                case EdpcgTelemetryEventKind.AttackTokenReleased: return "释放攻击令牌";
                case EdpcgTelemetryEventKind.StateChanged: return "状态变化";
                case EdpcgTelemetryEventKind.PathRequested: return "请求路线";
                case EdpcgTelemetryEventKind.PathFailed: return "路线失败";
                case EdpcgTelemetryEventKind.ReservationCreated: return "创建路线预留";
                case EdpcgTelemetryEventKind.ReservationReleased: return "释放路线预留";
                case EdpcgTelemetryEventKind.AreaEntered: return "进入战术区域";
                case EdpcgTelemetryEventKind.AreaExited: return "离开战术区域";
                case EdpcgTelemetryEventKind.Damage: return "伤害";
                case EdpcgTelemetryEventKind.Collision: return "碰撞";
                case EdpcgTelemetryEventKind.EnemyResolved: return "敌机已解决";
                case EdpcgTelemetryEventKind.NavigationRecovery: return "导航恢复";
                case EdpcgTelemetryEventKind.TuningChange: return "参数修改";
                case EdpcgTelemetryEventKind.Bookmark: return "书签";
                case EdpcgTelemetryEventKind.ValidationError: return "验证错误";
                default: return "未知事件";
            }
        }

        static void DrawPressureBar(string label, float value, Color color)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField(label + "  " + value.ToString("P0"),
                EditorStyles.miniLabel);
            Rect rect = GUILayoutUtility.GetRect(60f, 12f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.30f));
            Rect fill = rect;
            fill.width *= Mathf.Clamp01(value);
            EditorGUI.DrawRect(fill, color);
            EditorGUILayout.EndVertical();
        }

        static void DrawUsageBar(string label, int value, int cap)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox,
                GUILayout.MinWidth(120f), GUILayout.ExpandWidth(true));
            GUILayout.Label(label + "  " + value + " / " + cap,
                EditorStyles.miniLabel);
            Rect rect = GUILayoutUtility.GetRect(60f, 10f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.30f));
            Rect fill = rect;
            fill.width *= cap <= 0 ? 0f : Mathf.Clamp01(value / (float)cap);
            EditorGUI.DrawRect(fill, new Color(0.24f, 0.64f, 0.96f));
            EditorGUILayout.EndVertical();
        }

        static void DrawChartLegend(bool frozen)
        {
            EditorGUILayout.BeginHorizontal();
            DrawLegendSwatch(new Color(1f, 0.64f, 0.18f), "实际压力");
            DrawLegendSwatch(new Color(0.22f, 0.68f, 1f), "4 秒预测");
            DrawLegendSwatch(new Color(0.68f, 0.44f, 1f), "8 秒预测");
            DrawLegendSwatch(new Color(0.22f, 0.75f, 0.47f, 0.7f), "阶段目标带");
            DrawLegendSwatch(new Color(1f, 0.30f, 0.26f), "压力硬上限");
            GUILayout.FlexibleSpace();
            if (frozen)
                GUILayout.Label("曲线已冻结；实战仍在运行", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        static void DrawLegendSwatch(Color color, string label)
        {
            Rect rect = GUILayoutUtility.GetRect(14f, 10f, GUILayout.Width(14f));
            rect.y += 3f;
            EditorGUI.DrawRect(rect, color);
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(72f));
        }

        static string PressureAssessment(EdpcgPressureSample sample)
        {
            if (sample.actualPressure < sample.targetPressureMinimum)
                return "低于目标，敌群调度器可逐步加压";
            if (sample.actualPressure > sample.targetPressureMaximum)
                return "高于目标，应减少并发或攻击许可";
            return "处于目标压力带";
        }

        static string ForecastAssessment(EdpcgPressureSample sample)
        {
            if (sample.forecastPressure8Seconds > sample.targetPressureMaximum)
                return "预计超带";
            if (sample.forecastPressure4Seconds < sample.targetPressureMinimum)
                return "预计偏低";
            return "预计可控";
        }

        static string StrategyDescription(int level)
        {
            switch (level)
            {
                case 0: return "直接可读，优先减压";
                case 1: return "基础换位与脱离";
                case 2: return "侧袭、掩体链与重接敌";
                default: return "完整城市战术协同";
            }
        }

        static string ShortId(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value))
                return "-";
            if (value.Length <= maximumLength)
                return value;
            return "…" + value.Substring(value.Length - maximumLength + 1);
        }

        static void DrawRosterHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("#", GUILayout.Width(38f));
            GUILayout.Label("名单成员ID", GUILayout.Width(150f));
            GUILayout.Label("兵种", GUILayout.Width(90f));
            GUILayout.Label("状态", GUILayout.Width(126f));
            GUILayout.Label("生命", GUILayout.Width(56f));
            GUILayout.Label("生成次数", GUILayout.Width(52f));
            GUILayout.Label("解决原因", GUILayout.MinWidth(150f));
            GUILayout.Label("记分", GUILayout.Width(42f));
            EditorGUILayout.EndHorizontal();
        }

        static void DrawEventHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("时间", GUILayout.Width(62f));
            GUILayout.Label("事件", GUILayout.Width(152f));
            GUILayout.Label("名单成员ID", GUILayout.Width(152f));
            GUILayout.Label("区域", GUILayout.Width(150f));
            GUILayout.Label("路线", GUILayout.Width(150f));
            GUILayout.Label("原因", GUILayout.MinWidth(180f));
            EditorGUILayout.EndHorizontal();
        }

        static void DrawChangeHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("修改ID", GUILayout.Width(88f));
            GUILayout.Label("字段", GUILayout.Width(180f));
            GUILayout.Label("旧值 → 新值", GUILayout.Width(150f));
            GUILayout.Label("策略", GUILayout.Width(138f));
            GUILayout.Label("状态", GUILayout.Width(74f));
            GUILayout.Label("提交", GUILayout.Width(58f));
            GUILayout.Label("", GUILayout.Width(56f));
            EditorGUILayout.EndHorizontal();
        }

        static void DrawReservationHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("预留ID", GUILayout.Width(112f));
            GUILayout.Label("占用成员", GUILayout.Width(150f));
            GUILayout.Label("路线", GUILayout.Width(190f));
            GUILayout.Label("占用窗口", GUILayout.Width(110f));
            GUILayout.Label("到期", GUILayout.Width(72f));
            GUILayout.Label("优先级", GUILayout.Width(52f));
            GUILayout.Label("方向", GUILayout.Width(48f));
            EditorGUILayout.EndHorizontal();
        }

        static void DrawProfileHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("难度", GUILayout.Width(64f));
            GUILayout.Label("名单", GUILayout.Width(52f));
            GUILayout.Label("自爆 / 突击 / 炮艇", GUILayout.Width(110f));
            GUILayout.Label("人口 / 接战", GUILayout.Width(92f));
            GUILayout.Label("攻击许可 / 自爆 / 远程", GUILayout.Width(108f));
            GUILayout.Label("接战压力带", GUILayout.Width(92f));
            GUILayout.Label("峰值压力带", GUILayout.Width(92f));
            GUILayout.Label("策略", GUILayout.Width(48f));
            GUILayout.Label("生成", GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }

        static void DrawSelectedTierSummary(EdpcgTierSettings tier)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("难度档 " + tier.planetTier + " 关键节奏",
                EditorStyles.boldLabel);
            int rangedCount = tier.strikerCount + tier.gunshipCount;
            float rangedShare = rangedCount / Mathf.Max(1f, tier.rosterCount);
            EditorGUILayout.LabelField(
                "敌机结构",
                "远程 " + rangedCount + "（" +
                rangedShare.ToString("P0") + "） / 自爆 " +
                tier.interceptorCount + "；其中突击 " + tier.strikerCount +
                "、炮艇 " + tier.gunshipCount);
            EditorGUILayout.LabelField(
                "阶段（秒）",
                "预览 " + tier.previewSeconds.ToString("0.#") +
                "   接战 " + tier.engageSeconds.ToString("0.#") +
                "   峰值 " + tier.peakSeconds.ToString("0.#") +
                "   恢复 " + tier.recoverSeconds.ToString("0.#") +
                "   周期 " + tier.CycleSeconds.ToString("0.#"));
            EditorGUILayout.LabelField(
                "敌机可读性",
                "自爆预警 " + tier.suicideTelegraphSeconds.ToString("0.00") +
                " 秒   远程预警 " + tier.rangedTelegraphSeconds.ToString("0.00") +
                " 秒   远程换位 " + tier.rangedRelocationSeconds.ToString("0.00") + " 秒");
            EditorGUILayout.LabelField(
                "寻路恢复",
                "局部修正 " + tier.localRepairAfterSeconds.ToString("0.0") +
                " 秒   规避升级 " + tier.evasiveAfterSeconds.ToString("0.0") +
                " 秒   导航恢复 " + tier.navigationRecoveryAfterSeconds.ToString("0.0") + " 秒");
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
