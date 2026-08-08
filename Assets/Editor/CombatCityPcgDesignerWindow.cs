using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPlanet.CityPcg;

public sealed class CombatCityPcgDesignerWindow : EditorWindow
{
    const string ProfileFolder = "Assets/Settings/CombatCityPCG/Profiles";
    static readonly Color[] OpportunityColors =
    {
        new Color(0.16f, 0.88f, 1f, 1f),
        new Color(0.25f, 0.66f, 1f, 1f),
        new Color(1f, 0.43f, 0.08f, 1f),
        new Color(0.16f, 1f, 0.58f, 1f),
        new Color(0.82f, 0.34f, 1f, 1f),
        new Color(1f, 0.76f, 0.12f, 1f),
        new Color(0.42f, 0.92f, 1f, 1f),
        new Color(1f, 0.36f, 0.68f, 1f),
        new Color(1f, 0.18f, 0.16f, 1f)
    };

    struct Candidate
    {
        public int seed;
        public AirCombatCityReport report;
    }

    CombatCityPcgDesignProfile profile;
    AirCombatCityMission mission;
    int difficultyTier;
    int seed = 7319;
    AirCombatCityPlan previewPlan;
    AirCombatCityReport previewReport;
    readonly List<Candidate> candidates = new List<Candidate>(4);
    Vector2 scroll;
    bool showScenePreview = true;
    bool showAdvanced;

    [MenuItem("Tools/Planet Combat/战斗城市 PCG 策划工具")]
    static void Open()
    {
        GetWindow<CombatCityPcgDesignerWindow>("战斗城市 PCG");
    }

    void OnEnable()
    {
        if (profile == null)
        {
            profile = Resources.Load<CombatCityPcgDesignProfile>(
                AirCombatCityPcgLab.DefaultDesignProfileResourcePath);
        }
        SceneView.duringSceneGui += DrawScenePreview;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= DrawScenePreview;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("战斗驱动的非线性城市 PCG", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "先配置玩家需要做出的战术选择，再由道路、建筑和连廊承载。商业/工业等只作为可选美术主题。",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            profile = (CombatCityPcgDesignProfile)EditorGUILayout.ObjectField(
                "策划配置",
                profile,
                typeof(CombatCityPcgDesignProfile),
                false);
            if (GUILayout.Button("新建", GUILayout.Width(64f)))
                CreateProfile();
        }

        if (profile == null)
        {
            EditorGUILayout.HelpBox("请创建或选择一个策划配置。", MessageType.Warning);
            return;
        }

        profile.EnsureInitialized();
        var serialized = new SerializedObject(profile);
        serialized.Update();
        EditorGUILayout.PropertyField(
            serialized.FindProperty("displayName"),
            new GUIContent("配置名称"));
        EditorGUILayout.PropertyField(
            serialized.FindProperty("useVisualDistrictThemes"),
            new GUIContent("启用可选美术分区"));

        mission = (AirCombatCityMission)EditorGUILayout.EnumPopup("任务类型", mission);
        difficultyTier = EditorGUILayout.IntSlider("星球难度", difficultyTier + 1, 1, 6) - 1;
        seed = EditorGUILayout.IntField("候选 Seed", seed);

        SerializedProperty difficulty = ResolveDifficultyProperty(serialized);
        if (difficulty != null)
            DrawDifficulty(difficulty);

        showAdvanced = EditorGUILayout.Foldout(
            showAdvanced,
            "高级物理包线（只读预览）",
            true);
        if (showAdvanced)
        {
            CombatCityDifficultyProfile resolved = profile.Resolve(
                mission,
                difficultyTier);
            AirCombatCitySettings settings = CreateSettings(resolved, seed);
            EditorGUILayout.LabelField("主通道宽度", settings.MainCorridorWidth.ToString("0.0") + " m");
            EditorGUILayout.LabelField("侧路宽度", settings.FlankCorridorWidth.ToString("0.0") + " m");
            EditorGUILayout.LabelField("机动区直径", settings.ManeuverDiameter.ToString("0.0") + " m");
            EditorGUILayout.LabelField("恢复区直径", settings.RecoveryDiameter.ToString("0.0") + " m");
        }

        serialized.ApplyModifiedProperties();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("生成预览"))
                GeneratePreview(seed);
            if (GUILayout.Button("生成 4 个候选"))
                GenerateCandidates();
            if (GUILayout.Button("应用到选中的城市"))
                ApplyToSelectedLab();
        }
        showScenePreview = EditorGUILayout.Toggle("Scene 显示机会网络", showScenePreview);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawReport();
        DrawCandidates();
        EditorGUILayout.EndScrollView();
    }

    SerializedProperty ResolveDifficultyProperty(SerializedObject serialized)
    {
        string missionProperty = mission == AirCombatCityMission.FacilityAssault
            ? "assault"
            : mission == AirCombatCityMission.BossEncounter ? "boss" : "clearance";
        SerializedProperty missionProfile = serialized.FindProperty(missionProperty);
        if (missionProfile == null)
            return null;
        SerializedProperty tiers = missionProfile.FindPropertyRelative("difficultyTiers");
        if (tiers == null || tiers.arraySize != 6)
            return null;
        return tiers.GetArrayElementAtIndex(Mathf.Clamp(difficultyTier, 0, 5));
    }

    static void DrawDifficulty(SerializedProperty difficulty)
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("策划语义参数", EditorStyles.boldLabel);
        DrawSlider(difficulty, "navigationChallenge", "导航挑战");
        DrawSlider(difficulty, "combatPressure", "战斗压力");
        DrawSlider(difficulty, "exposurePressure", "暴露压力");
        DrawSlider(difficulty, "recoveryGenerosity", "恢复慷慨度");
        DrawSlider(difficulty, "tacticalOpportunityDensity", "战术机会密度");
        DrawSlider(difficulty, "routeLegibility", "路线可读性");
        DrawSlider(difficulty, "bossPursuitPressure", "Boss 追击压力");
        DrawSlider(difficulty, "destructionUtility", "破坏收益");
        DrawSlider(difficulty, "decisionComplexity", "决策复杂度");
        DrawSliderRange(difficulty, "roadWidthScale", "道路宽度倍率", 0.7f, 1.4f);
        DrawSlider(difficulty, "blockMergeStrength", "街区合并强度");
    }

    static void DrawSlider(
        SerializedProperty parent,
        string propertyName,
        string label)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            EditorGUILayout.Slider(property, 0f, 1f, new GUIContent(label));
    }

    static void DrawSliderRange(
        SerializedProperty parent,
        string propertyName,
        string label,
        float minimum,
        float maximum)
    {
        SerializedProperty property = parent.FindPropertyRelative(propertyName);
        if (property != null)
            EditorGUILayout.Slider(property, minimum, maximum, new GUIContent(label));
    }

    void DrawReport()
    {
        if (previewReport == null)
            return;
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("生成报告", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            previewReport.valid ? "候选通过全部静态验证。" : previewReport.failureReason,
            previewReport.valid ? MessageType.Info : MessageType.Error);
        EditorGUILayout.LabelField("解析 Seed", previewReport.resolvedSeed.ToString());
        EditorGUILayout.LabelField("建筑 / 航路",
            previewReport.buildingCount + " / " + previewReport.routeCount);
        EditorGUILayout.LabelField("有效战术机会", previewReport.tacticalOpportunityCount.ToString());
        EditorGUILayout.LabelField("最差位置可选解法", previewReport.minimumTacticalChoices.ToString());
        EditorGUILayout.LabelField("最长连续暴露", previewReport.longestExposureSeconds.ToString("0.0") + " s");
        EditorGUILayout.LabelField("最近恢复机会", previewReport.nearestRecoverySeconds.ToString("0.0") + " s");
        EditorGUILayout.LabelField("恢复 / 风筝 / 暴露捷径",
            previewReport.recoveryOpportunityCount + " / " +
            previewReport.kiteLoopOpportunityCount + " / " +
            previewReport.exposureShortcutCount);
        EditorGUILayout.LabelField("机会网络",
            previewReport.tacticalOpportunityNetworkValid ? "有效" : "无效");
        EditorGUILayout.LabelField("支配性路线",
            previewReport.dominantRouteDetected ? "检测到" : "未检测到");
        EditorGUILayout.LabelField("战术街区 / 连通区域",
            previewReport.tacticalBlockCount + " / " +
            previewReport.tacticalRegionCount);
        EditorGUILayout.LabelField("未分配街区",
            previewReport.unassignedTacticalBlockCount.ToString());
        EditorGUILayout.LabelField("合并街区组 / 移除道路段",
            previewReport.mergedBlockGroupCount + " / " +
            previewReport.removedInternalRoadSegments);
        EditorGUILayout.LabelField("掩体区移除道路 / 高楼封边",
            previewReport.occlusionMergedRoadSegments + " / " +
            previewReport.occlusionBoundaryTowerCount);
        EditorGUILayout.LabelField("封边高楼 / 高楼后空气墙",
            previewReport.combatBoundaryTowerCount + " / " +
            previewReport.combatBoundaryAirWallCount);
        EditorGUILayout.LabelField("伏击连廊 / 高台攻击区",
            previewReport.destructionAmbushBridgeCount + " / 已禁用");
        EditorGUILayout.LabelField("恢复口袋阻断中心输出",
            previewReport.recoveryPocketOutputBlockedCount + " / 2");
        EditorGUILayout.LabelField("道路宽度范围",
            previewReport.minimumRoadWidth.ToString("0.0") + " - " +
            previewReport.maximumRoadWidth.ToString("0.0") + " m");
        EditorGUILayout.LabelField("全城用途覆盖",
            previewReport.tacticalBlockCoverageValid ? "有效" : "无效");
        EditorGUILayout.LabelField("稳定校验值", previewReport.checksum.ToString());
    }

    void DrawCandidates()
    {
        if (candidates.Count == 0)
            return;
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("候选 Seed", EditorStyles.boldLabel);
        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate candidate = candidates[i];
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    candidate.seed + "  |  " +
                    (candidate.report.valid ? "通过" : "拒绝") + "  |  机会 " +
                    candidate.report.tacticalOpportunityCount + "  |  校验 " +
                    candidate.report.checksum);
                if (GUILayout.Button("查看", GUILayout.Width(48f)))
                {
                    seed = candidate.seed;
                    GeneratePreview(seed);
                }
                if (GUILayout.Button("锁定", GUILayout.Width(48f)))
                    LockCandidate(candidate);
            }
        }
    }

    void GeneratePreview(int targetSeed)
    {
        CombatCityDifficultyProfile difficulty = profile.Resolve(
            mission,
            difficultyTier);
        previewPlan = AirCombatCityGenerator.Generate(
            CreateSettings(difficulty, targetSeed),
            out previewReport);
        SceneView.RepaintAll();
        Repaint();
    }

    void GenerateCandidates()
    {
        candidates.Clear();
        for (int i = 0; i < 4; i++)
        {
            int candidateSeed = unchecked(seed + i * 7919);
            AirCombatCityGenerator.Generate(
                CreateSettings(profile.Resolve(mission, difficultyTier), candidateSeed),
                out AirCombatCityReport report);
            candidates.Add(new Candidate { seed = candidateSeed, report = report });
        }
    }

    AirCombatCitySettings CreateSettings(
        CombatCityDifficultyProfile difficulty,
        int targetSeed)
    {
        return new AirCombatCitySettings
        {
            seed = targetSeed,
            mission = mission,
            combatDifficulty = difficulty.ValidatedCopy(),
            useVisualDistrictThemes = profile.useVisualDistrictThemes
        };
    }

    void LockCandidate(Candidate candidate)
    {
        Undo.RecordObject(profile, "Lock combat city PCG seed");
        profile.lockedSeed = candidate.seed;
        profile.lockedChecksum = candidate.report.checksum;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    void ApplyToSelectedLab()
    {
        AirCombatCityPcgLab lab = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<AirCombatCityPcgLab>()
            : null;
        if (lab == null)
        {
            EditorUtility.DisplayDialog(
                "没有选中城市生成器",
                "请在 Hierarchy 中选中带有 AirCombatCityPcgLab 的对象。",
                "确定");
            return;
        }
        Undo.RecordObject(lab, "Apply combat city PCG design profile");
        lab.ConfigureDesignProfile(profile, mission, difficultyTier, seed);
        previewPlan = lab.Plan;
        previewReport = lab.Report;
        EditorUtility.SetDirty(lab);
        SceneView.RepaintAll();
        Repaint();
    }

    void CreateProfile()
    {
        EnsureFolder("Assets/Settings");
        EnsureFolder("Assets/Settings/CombatCityPCG");
        EnsureFolder(ProfileFolder);
        var created = CreateInstance<CombatCityPcgDesignProfile>();
        created.EnsureInitialized();
        string path = AssetDatabase.GenerateUniqueAssetPath(
            ProfileFolder + "/CombatCityPcgDesignProfile.asset");
        AssetDatabase.CreateAsset(created, path);
        AssetDatabase.SaveAssets();
        profile = created;
        Selection.activeObject = created;
        EditorGUIUtility.PingObject(created);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        string name = path.Substring(split + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    void DrawScenePreview(SceneView sceneView)
    {
        if (!showScenePreview || previewPlan == null)
            return;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        for (int i = 0; i < previewPlan.opportunities.Count; i++)
        {
            TacticalOpportunity opportunity = previewPlan.opportunities[i];
            Handles.color = OpportunityColors[(int)opportunity.kind %
                                               OpportunityColors.Length];
            DrawOpportunityOutline(opportunity);
            Handles.Label(
                new Vector3(
                    opportunity.bounds.center.x,
                    PreviewAltitude(opportunity) + 8f,
                    opportunity.bounds.center.z),
                OpportunityDisplayName(opportunity.kind) +
                "\n收益 " + opportunity.utility.ToString("0.00") +
                " / 风险 " + opportunity.risk.ToString("0.00") +
                " / 实体 " + opportunity.physicalFeatureCount);
            for (int exit = 0; exit < opportunity.exits.Length; exit++)
            {
                Vector3 point = new Vector3(
                    opportunity.exits[exit].x,
                    PreviewAltitude(opportunity),
                    opportunity.exits[exit].z);
                Handles.SphereHandleCap(0, point, Quaternion.identity, 6f,
                    EventType.Repaint);
            }
        }

        Handles.color = new Color(0.52f, 0.9f, 1f, 0.58f);
        for (int i = 0; i < previewPlan.opportunities.Count; i++)
        {
            TacticalOpportunity source = previewPlan.opportunities[i];
            for (int connection = 0;
                 connection < source.connectedOpportunityIds.Length;
                 connection++)
            {
                TacticalOpportunity target = FindOpportunity(
                    source.connectedOpportunityIds[connection]);
                if (target != null)
                    Handles.DrawDottedLine(source.bounds.center, target.bounds.center, 5f);
            }
        }
    }

    static void DrawOpportunityOutline(TacticalOpportunity opportunity)
    {
        float y = PreviewAltitude(opportunity);
        if (opportunity.kind == TacticalOpportunityKind.KiteLoop)
        {
            float radius = Mathf.Max(
                opportunity.loopRadius,
                Mathf.Min(opportunity.bounds.extents.x,
                    opportunity.bounds.extents.z));
            Handles.DrawWireDisc(
                new Vector3(opportunity.bounds.center.x, y,
                    opportunity.bounds.center.z),
                Vector3.up,
                radius);
            return;
        }
        Bounds bounds = opportunity.bounds;
        Vector3[] points =
        {
            new Vector3(bounds.min.x, y, bounds.min.z),
            new Vector3(bounds.min.x, y, bounds.max.z),
            new Vector3(bounds.max.x, y, bounds.max.z),
            new Vector3(bounds.max.x, y, bounds.min.z),
            new Vector3(bounds.min.x, y, bounds.min.z)
        };
        Handles.DrawAAPolyLine(2.5f, points);
    }

    static float PreviewAltitude(TacticalOpportunity opportunity)
    {
        switch (opportunity.kind)
        {
            case TacticalOpportunityKind.RecoveryPocket:
            case TacticalOpportunityKind.DestructionAmbush:
            case TacticalOpportunityKind.TacticalChoke:
                return Mathf.Max(5f, opportunity.bounds.min.y + 5f);
            case TacticalOpportunityKind.AttackPerch:
                return opportunity.bounds.max.y + 2f;
            default:
                return opportunity.bounds.center.y;
        }
    }

    static string OpportunityDisplayName(TacticalOpportunityKind kind)
    {
        switch (kind)
        {
            case TacticalOpportunityKind.ManeuverBowl: return "中央机动街区";
            case TacticalOpportunityKind.OcclusionChain: return "少道路高楼掩体区";
            case TacticalOpportunityKind.ExposureShortcut: return "开放火力捷径";
            case TacticalOpportunityKind.RecoveryPocket: return "维修庭院";
            case TacticalOpportunityKind.KiteLoop: return "环绕街区";
            case TacticalOpportunityKind.TacticalChoke: return "战术连廊窄口";
            case TacticalOpportunityKind.VerticalEscape: return "低中空换层通道";
            case TacticalOpportunityKind.AttackPerch: return "有掩体攻击平台";
            case TacticalOpportunityKind.DestructionAmbush: return "连廊倒塌伏击区";
            default: return "战斗街区";
        }
    }

    TacticalOpportunity FindOpportunity(string stableId)
    {
        for (int i = 0; i < previewPlan.opportunities.Count; i++)
        {
            if (previewPlan.opportunities[i].stableId == stableId)
                return previewPlan.opportunities[i];
        }
        return null;
    }
}
