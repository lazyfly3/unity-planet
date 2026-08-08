#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityPlanet.EDPCG;

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
        const float GraphWindowSeconds = 120f;

        static readonly string[] MainTabLabels =
        {
            "实时压力",
            "参数调节",
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

        static readonly TuningDescriptor[] TuningFields =
        {
            new TuningDescriptor("并发预算", "populationCap", "同时存在上限",
                "当前阶段允许存在的敌机总量。减少它会直接降低持续压迫。", 1f, 28f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "engagementCap", "接战上限",
                "允许同时进入接战状态的敌机数量。", 1f, 16f, true,
                EdpcgLiveApplyPolicy.ApplyAtSafeBoundary),
            new TuningDescriptor("并发预算", "fullSimulationCap", "完整模拟上限",
                "保留完整飞行与战术模拟的敌机数量。", 1f, 16f, true,
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
                "Preview 阶段目标压力带下界。", 0f, 0.8f),
            new TuningDescriptor("节奏与压力", "previewPressureMax", "预览压力上界",
                "Preview 阶段目标压力带上界。", 0.05f, 0.9f),
            new TuningDescriptor("节奏与压力", "engagePressureMin", "接战压力下界",
                "Engage 阶段目标压力带下界。", 0f, 0.8f),
            new TuningDescriptor("节奏与压力", "engagePressureMax", "接战压力上界",
                "Engage 阶段目标压力带上界。", 0.05f, 0.9f),
            new TuningDescriptor("节奏与压力", "peakPressureMin", "峰值压力下界",
                "Peak 阶段目标压力带下界。", 0f, 0.85f),
            new TuningDescriptor("节奏与压力", "peakPressureMax", "峰值压力上界",
                "Peak 阶段目标压力带上界。", 0.05f, 0.9f),
            new TuningDescriptor("节奏与压力", "recoverPressureMin", "恢复压力下界",
                "Recover 阶段目标压力带下界。", 0f, 0.7f),
            new TuningDescriptor("节奏与压力", "recoverPressureMax", "恢复压力上界",
                "Recover 阶段目标压力带上界。", 0.05f, 0.8f),
            new TuningDescriptor("节奏与压力", "hardPressureLimit", "压力硬上限",
                "超过此值时 Director 不再继续增加战场人口。", 0.55f, 0.9f),
            new TuningDescriptor("节奏与压力", "pressureSmoothingSeconds", "压力平滑（秒）",
                "压力信号的响应速度。越大越稳定，但反应越慢。", 0.25f, 5f),

            new TuningDescriptor("AI 策略", "maximumStrategyLevel", "最高策略等级",
                "决定敌机会使用多少侧袭、重定位、脱离与重新接敌策略。", 0f, 3f, true),
            new TuningDescriptor("AI 策略", "suicideTelegraphSeconds", "自爆预警（秒）",
                "自爆兵进入冲刺前给玩家的可读预警时间。", 0.25f, 3f),
            new TuningDescriptor("AI 策略", "suicideCommitSeconds", "自爆承诺窗（秒）",
                "自爆兵保持攻击承诺、避免瞬时反复切状态的时间。", 0.5f, 3f),
            new TuningDescriptor("AI 策略", "suicideBreakAwaySeconds", "自爆脱离时间（秒）",
                "失败攻击后脱离并重组的最短时间。", 0.5f, 5f),
            new TuningDescriptor("AI 策略", "rangedTelegraphSeconds", "远程预警（秒）",
                "远程兵开火前保持可读瞄准提示的时间。", 0.25f, 3f),
            new TuningDescriptor("AI 策略", "rangedBurstSeconds", "远程点射（秒）",
                "一次火力暴露持续时间。", 0.25f, 3f),
            new TuningDescriptor("AI 策略", "rangedRelocationSeconds", "远程换位（秒）",
                "远程兵完成一轮火力后尝试更换掩体或射击角度的时间。", 0.5f, 6f),
            new TuningDescriptor("AI 策略", "lineOfSightHysteresisSeconds", "视线迟滞（秒）",
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
        EdpcgDifficultyProfile profile;
        LabTab selectedTab;
        DetailTab selectedDetailTab;
        EventFilter eventFilter;
        Vector2 liveScroll;
        Vector2 tuningScroll;
        Vector2 detailScroll;
        Vector2 profileScroll;
        string rosterSearch = string.Empty;
        int rosterStateFilter;
        int selectedProfileTier;
        int bookmarkSequence;
        bool graphFrozen;
        float frozenGraphEnd;
        string lastExportFolder = string.Empty;
        string status = "打开非 Boss 星球战斗后，此窗口会自动连接 EDPCG 运行时。";
        MessageType statusType = MessageType.None;
        GUIStyle sectionStyle;
        GUIStyle monoStyle;

        [MenuItem("Tools/Planet Combat/EDPCG Difficulty Lab")]
        static void Open()
        {
            EdpcgDifficultyLabWindow window = GetWindow<
                EdpcgDifficultyLabWindow>("EDPCG Difficulty Lab");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent(
                "EDPCG Difficulty Lab",
                "Experience-Driven PCG 非 Boss 难度调试工具");
            minSize = new Vector2(760f, 520f);
            profile = AssetDatabase.LoadAssetAtPath<EdpcgDifficultyProfile>(
                ProfileAssetPath);
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            BindRuntime();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        }

        void OnInspectorUpdate()
        {
            BindRuntime();
            if (EditorApplication.isPlaying)
                Repaint();
        }

        void OnGUI()
        {
            EnsureStyles();
            BindRuntime();
            DrawTopToolbar();
            selectedTab = (LabTab)GUILayout.Toolbar(
                (int)selectedTab,
                MainTabLabels,
                EditorStyles.toolbarButton,
                GUILayout.Height(24f));

            if (!HasActiveRuntime())
            {
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
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            Color previous = GUI.color;
            GUI.color = active
                ? new Color(0.35f, 1f, 0.55f)
                : new Color(1f, 0.74f, 0.28f);
            GUILayout.Label(active ? "● LIVE" : "● WAITING",
                EditorStyles.toolbarButton, GUILayout.Width(82f));
            GUI.color = previous;

            if (active)
            {
                GUILayout.Label(
                    boundRuntime.MissionId + "  ·  Tier " +
                    boundRuntime.Settings.planetTier + "  ·  " +
                    boundRuntime.Elapsed.ToString("0.0", CultureInfo.InvariantCulture) + "s",
                    EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label("只连接非 Boss EDPCG 实战会话", EditorStyles.miniLabel);
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
                "进入星球实战后会自动连接。等待期间仍可检查六档难度配置。",
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
            DrawMetric("阶段", sample.phase.ToString(),
                phaseElapsed.ToString("0.0") + "s / 剩余 " +
                phaseRemaining.ToString("0.0") + "s");
            DrawMetric("实际压力", sample.actualPressure.ToString("P1"),
                PressureAssessment(sample));
            DrawMetric("目标压力带",
                sample.targetPressureMinimum.ToString("P0") + " – " +
                sample.targetPressureMaximum.ToString("P0"),
                "绿色区间");
            DrawMetric("4s / 8s 预测",
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
            DrawPressureBar("寻路 / 承诺", sample.navigationPressure,
                new Color(0.55f, 0.46f, 1f));
            DrawPressureBar("城市环境", sample.measuredEnvironmentPressure,
                new Color(0.22f, 0.78f, 0.57f));
            DrawPressureBar("玩家负荷", sample.playerStrain,
                new Color(1f, 0.69f, 0.22f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "寻路细分",
                "自爆 " + sample.suicideNavigationPressure.ToString("P0") +
                "   远程 " + sample.rangedNavigationPressure.ToString("P0") +
                "   攻击承诺 " + sample.committedNavigationPressure.ToString("P0") +
                "   玩家受损辅助 " + sample.playerDamageAssist.ToString("P0"));

            EditorGUILayout.Space(8f);
            DrawSectionTitle("Director 实时门控");
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
                "   当前生成间隔 " + runtime.SpawnInterval.ToString("0.00") + "s");

            DrawCityContext(runtime);
            DrawExportContents();
            EditorGUILayout.EndScrollView();
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
                "名单总数、兵种组成和星球 Tier 不支持本局热改，避免敌人被重复生成、结算丢失或任务软锁。" +
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

            GUIStyle policyStyle = descriptor.Policy ==
                                   EdpcgLiveApplyPolicy.ApplyAtSafeBoundary
                ? EditorStyles.miniButtonMid
                : EditorStyles.miniButton;
            GUILayout.Label(
                descriptor.Policy == EdpcgLiveApplyPolicy.ApplyAtSafeBoundary
                    ? "安全边界"
                    : "立即",
                policyStyle,
                GUILayout.Width(64f));

            using (new EditorGUI.DisabledScope(!differs))
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
            GUILayout.Label("最多 64 名；固定名单不会补刷", EditorStyles.miniLabel);
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
                GUILayout.Label(member.role.ToString(), GUILayout.Width(90f));
                GUILayout.Label(member.state.ToString(), GUILayout.Width(126f));
                GUILayout.Label(member.healthRatio.ToString("P0"), GUILayout.Width(56f));
                GUILayout.Label(member.spawnAttempts.ToString(), GUILayout.Width(52f));
                GUILayout.Label(member.resolutionReason == EdpcgEnemyResolutionReason.None
                    ? "-"
                    : member.resolutionReason.ToString(), GUILayout.MinWidth(150f));
                GUILayout.Label(member.creditedKill ? "✓" : "-", GUILayout.Width(42f));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawEventStream(EdpcgEncounterRuntime runtime)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("事件筛选", GUILayout.Width(56f));
            eventFilter = (EventFilter)EditorGUILayout.EnumPopup(
                eventFilter,
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
                GUILayout.Label(item.missionTime.ToString("0.0") + "s", monoStyle,
                    GUILayout.Width(62f));
                GUILayout.Label(item.kind.ToString(), GUILayout.Width(152f));
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
                GUILayout.Label(change.applyPolicy.ToString(), GUILayout.Width(138f));
                GUILayout.Label(change.state.ToString(), GUILayout.Width(74f));
                GUILayout.Label(change.submittedAt.ToString("0.0") + "s",
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
                    item.exitAt.ToString("0.0") + "s", GUILayout.Width(110f));
                GUILayout.Label(item.expiresAt.ToString("0.0") + "s",
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
                        "Tier " + (index + 1), "Button", GUILayout.Width(64f)))
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
                    GUILayout.Width(96f));
                GUILayout.Label(tier.engagePressureMin.ToString("P0") + " – " +
                    tier.engagePressureMax.ToString("P0"), GUILayout.Width(92f));
                GUILayout.Label(tier.peakPressureMin.ToString("P0") + " – " +
                    tier.peakPressureMax.ToString("P0"), GUILayout.Width(92f));
                GUILayout.Label(tier.maximumStrategyLevel.ToString(), GUILayout.Width(48f));
                GUILayout.Label(tier.spawnIntervalSeconds.ToString("0.00") + "s",
                    GUILayout.Width(60f));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8f);
            DrawSelectedTierSummary(profile.tiers[Mathf.Clamp(
                selectedProfileTier, 0, profile.tiers.Length - 1)]);

            if (HasActiveRuntime())
            {
                EditorGUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(
                           boundRuntime.Settings == null || profile == null))
                {
                    if (GUILayout.Button(
                            "将本局当前参数保存到 Tier " +
                            boundRuntime.Settings.planetTier + "（下一局生效）",
                            GUILayout.Height(28f)))
                    {
                        SaveRuntimeTierToProfile(boundRuntime);
                    }
                }
            }
            EditorGUILayout.HelpBox(
                "最高难度固定名单为 64 架。此页只展示曲线和关键预算；要编辑完整持久化字段，" +
                "请定位配置资产后在 Inspector 中修改。Boss 配置不在此工具范围内。",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        void DrawCityContext(EdpcgEncounterRuntime runtime)
        {
            EditorGUILayout.Space(8f);
            DrawSectionTitle("PCG 城市语义与当前路径压力");
            EdpcgCityTacticalRuntimeMap map = runtime.TacticalMap;
            EdpcgPressureSample sample = runtime.CurrentSample;
            if (map == null || !map.HasCityData)
            {
                EditorGUILayout.HelpBox(
                    "当前城市没有可用的 EDPCG 战术区/路线语义。Director 会降级运行，但城市减压与侧袭策略不可验证。",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(
                "当前战术区",
                string.IsNullOrEmpty(sample.activeTacticalAreaId)
                    ? "城市外 / 无语义区"
                    : sample.activeTacticalAreaKind + "  ·  " +
                      sample.activeTacticalAreaId);
            EditorGUILayout.LabelField(
                "城市数据",
                "区域 " + map.Areas.Count +
                "   路线 " + map.Routes.Count +
                "   敌机入口 " + map.Ingresses.Count +
                "   当前路线预留 " + runtime.Reservations.Reservations.Count);

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
                "   维修庭院 " + areaCounts[(int)EdpcgTacticalAreaKind.RepairCourtyard] +
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
                "已保存 Tier " + (tierIndex + 1) + "，将在下一局生效。",
                MessageType.Info);
        }

        void BindRuntime()
        {
            EdpcgEncounterRuntime runtime = EdpcgRuntimeRegistry.Active;
            if (runtime == boundRuntime)
                return;
            boundRuntime = runtime;
            graphFrozen = false;
            detailScroll = Vector2.zero;
            SyncDraft();
            if (runtime != null)
            {
                SetStatus(
                    "已连接 " + runtime.MissionId + "（Tier " +
                    (runtime.Settings != null
                        ? runtime.Settings.planetTier.ToString()
                        : "-") + "）。",
                    MessageType.Info);
            }
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
            string[] names = Enum.GetNames(typeof(EdpcgRosterState));
            string[] result = new string[names.Length + 1];
            result[0] = "全部状态";
            Array.Copy(names, 0, result, 1, names.Length);
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
                return "低于目标，Director 可逐步加压";
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
            GUILayout.Label("Roster ID", GUILayout.Width(150f));
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
            GUILayout.Label("Roster ID", GUILayout.Width(152f));
            GUILayout.Label("区域", GUILayout.Width(150f));
            GUILayout.Label("路线", GUILayout.Width(150f));
            GUILayout.Label("原因", GUILayout.MinWidth(180f));
            EditorGUILayout.EndHorizontal();
        }

        static void DrawChangeHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Change ID", GUILayout.Width(88f));
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
            GUILayout.Label("Reservation ID", GUILayout.Width(112f));
            GUILayout.Label("Owner", GUILayout.Width(150f));
            GUILayout.Label("Route", GUILayout.Width(190f));
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
            GUILayout.Label("令牌 / 冲刺 / 火线", GUILayout.Width(96f));
            GUILayout.Label("接战压力带", GUILayout.Width(92f));
            GUILayout.Label("峰值压力带", GUILayout.Width(92f));
            GUILayout.Label("策略", GUILayout.Width(48f));
            GUILayout.Label("生成", GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }

        static void DrawSelectedTierSummary(EdpcgTierSettings tier)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Tier " + tier.planetTier + " 关键节奏",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "阶段（秒）",
                "Preview " + tier.previewSeconds.ToString("0.#") +
                "   Engage " + tier.engageSeconds.ToString("0.#") +
                "   Peak " + tier.peakSeconds.ToString("0.#") +
                "   Recover " + tier.recoverSeconds.ToString("0.#") +
                "   周期 " + tier.CycleSeconds.ToString("0.#"));
            EditorGUILayout.LabelField(
                "AI 可读性",
                "自爆预警 " + tier.suicideTelegraphSeconds.ToString("0.00") +
                "s   远程预警 " + tier.rangedTelegraphSeconds.ToString("0.00") +
                "s   远程换位 " + tier.rangedRelocationSeconds.ToString("0.00") + "s");
            EditorGUILayout.LabelField(
                "寻路恢复",
                "局部修正 " + tier.localRepairAfterSeconds.ToString("0.0") +
                "s   规避升级 " + tier.evasiveAfterSeconds.ToString("0.0") +
                "s   导航恢复 " + tier.navigationRecoveryAfterSeconds.ToString("0.0") + "s");
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
