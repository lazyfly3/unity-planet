using UnityEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;

public sealed class ArcadeFlightTuningWindow : EditorWindow
{
    const string AssetPath =
        "Assets/Resources/ModularAssembly/ArcadeFlightTuning.asset";

    ArcadeFlightTuningProfile profile;
    SerializedObject serializedProfile;
    Vector2 scroll;
    float previewAcceleration = 8f;

    [MenuItem("Tools/飞船/街机飞行手感调节器")]
    static void Open()
    {
        GetWindow<ArcadeFlightTuningWindow>(
            "街机飞行手感");
    }

    void OnEnable()
    {
        LoadOrCreateProfile();
        minSize = new Vector2(440f, 620f);
    }

    void OnDisable()
    {
        if (serializedProfile != null)
            serializedProfile.ApplyModifiedProperties();
        if (profile != null)
            AssetDatabase.SaveAssetIfDirty(profile);
    }

    void OnInspectorUpdate()
    {
        if (Application.isPlaying)
            Repaint();
    }

    void OnGUI()
    {
        if (profile == null || serializedProfile == null)
        {
            EditorGUILayout.HelpBox(
                "未找到街机飞行参数资产。",
                MessageType.Warning);
            if (GUILayout.Button("重新创建默认参数"))
                LoadOrCreateProfile();
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(
            "玩家街机飞行手感调节器",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "这里只影响玩家直接操控的街机模式。标准模式、Boss AI和任务自动驾驶不会读取这些手感参数。所有加减速仍受实际质量、幸存推进器、空气和重力限制。",
            MessageType.Info);
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "正在运行：参数修改会即时作用于当前玩家飞船。",
                MessageType.None);
        }

        serializedProfile.Update();
        EditorGUI.BeginChangeCheck();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSection("输入响应");
        DrawSlider(
            "movementDeadzone",
            "移动输入死区",
            0f,
            0.3f,
            "过滤手柄摇杆漂移；键盘通常不受影响。");
        DrawSlider(
            "inputResponseExponent",
            "输入幅度曲线",
            0.25f,
            3f,
            "小于1更灵敏，大于1更容易做低速微调。");

        DrawSection("速度与推进改装");
        DrawFloat(
            "baseTargetSpeed",
            "基础目标速度",
            "还未计入推进加速度时的速度基数。");
        DrawFloat(
            "minimumTargetSpeed",
            "最低目标速度",
            "核心跛行模式仍可达到的速度下限。");
        DrawFloat(
            "maximumTargetSpeed",
            "最高目标速度",
            "非Boost状态下的速度上限。");
        DrawSlider(
            "accelerationToSpeed",
            "推进改装速度收益",
            0f,
            10f,
            "幸存推进器越强，目标速度提升越多。");
        DrawSlider(
            "boostSpeedMultiplier",
            "Boost速度倍率",
            1f,
            3f,
            "按住Shift时的目标速度倍率。");

        DrawSection("变向与侧滑");
        DrawSlider(
            "intentResponseSeconds",
            "新方向响应时间（秒）",
            0.03f,
            1f,
            "越小越快建立玩家当前输入方向的速度。");
        DrawSlider(
            "driftResponseSeconds",
            "旧惯性消除时间（秒）",
            0.03f,
            3f,
            "越大越有漂移感；它始终排在新方向和姿态之后。");
        DrawSlider(
            "intentAuthorityFraction",
            "新方向权限占比",
            0.1f,
            1f,
            "限制新方向最多预占多少幸存推进权限。");
        DrawSlider(
            "driftAuthorityFraction",
            "消除侧滑权限上限",
            0f,
            1f,
            "限制系统最多申请多少权限来消除旧惯性。");

        DrawSection("松手急停与定点");
        DrawSlider(
            "stopVelocityGain",
            "急停反馈强度",
            1f,
            30f,
            "越大越快申请用满反向制动力，不会直接归零速度。");
        DrawSlider(
            "stopAuthorityFraction",
            "急停权限占比",
            0.1f,
            1f,
            "允许急停使用多少当前方向制动力。");
        DrawSlider(
            "positionHoldGain",
            "返回松键位置强度",
            0f,
            20f,
            "急停后返回松键位置的力度。");
        DrawSlider(
            "positionHoldAuthorityFraction",
            "定点权限占比",
            0f,
            1f,
            "定点请求可使用的最大方向权限。");

        DrawSection("重力与姿态");
        DrawSlider(
            "movingGravitySupport",
            "移动时重力补偿",
            0f,
            2f,
            "1为正常抵消重力，低于1会下沉，高于1会有上升倾向。");
        DrawSlider(
            "idleGravitySupport",
            "松键时重力补偿",
            0f,
            2f,
            "急停和定点时使用的重力补偿倍率。");
        DrawSlider(
            "aimTorqueMultiplier",
            "瞄准转向力度",
            0.1f,
            2f,
            "只缩放玩家街机模式的瞄准姿态请求。");
        DrawSlider(
            "strafeRollCoupling",
            "横移自动侧倾",
            0f,
            0.8f,
            "A/D横移附带的滚转量，0表示不侧倾。");

        DrawSection("速度预览");
        previewAcceleration = Mathf.Max(
            0f,
            EditorGUILayout.FloatField(
                new GUIContent(
                    "假设可用加速度",
                    "输入一个m/s²数值，预览当前参数算出的目标速度。"),
                previewAcceleration));
        float normalSpeed = TrainingFlightAssist.CalculateTargetSpeed(
            previewAcceleration,
            false,
            profile);
        float boostSpeed = TrainingFlightAssist.CalculateTargetSpeed(
            previewAcceleration,
            true,
            profile);
        EditorGUILayout.LabelField(
            "普通 / Boost目标速度",
            $"{normalSpeed:0.0} / {boostSpeed:0.0} m/s");

        EditorGUILayout.Space(8f);
        DrawPresets();
        EditorGUILayout.EndScrollView();

        if (EditorGUI.EndChangeCheck())
        {
            serializedProfile.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            RefreshLiveCoordinators();
        }

        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("保存参数", GUILayout.Height(30f)))
            {
                serializedProfile.ApplyModifiedProperties();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                RefreshLiveCoordinators();
                ShowNotification(new GUIContent("街机参数已保存"));
            }
            if (GUILayout.Button("选中参数资产", GUILayout.Height(30f)))
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }
        }
    }

    void DrawPresets()
    {
        EditorGUILayout.LabelField("快速预设", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("默认均衡"))
                ApplyPreset(0);
            if (GUILayout.Button("更灵敏"))
                ApplyPreset(1);
            if (GUILayout.Button("更多惯性"))
                ApplyPreset(2);
        }
    }

    void ApplyPreset(int preset)
    {
        Undo.RecordObject(profile, "应用街机飞行预设");
        profile.ApplyPreset((ArcadeFlightTuningPreset)preset);
        EditorUtility.SetDirty(profile);
        serializedProfile.Update();
        RefreshLiveCoordinators();
    }

    void DrawSection(string title)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void DrawSlider(
        string propertyName,
        string label,
        float minimum,
        float maximum,
        string tooltip)
    {
        SerializedProperty property =
            serializedProfile.FindProperty(propertyName);
        if (property == null)
            return;
        EditorGUILayout.Slider(
            property,
            minimum,
            maximum,
            new GUIContent(label, tooltip));
    }

    void DrawFloat(
        string propertyName,
        string label,
        string tooltip)
    {
        SerializedProperty property =
            serializedProfile.FindProperty(propertyName);
        if (property == null)
            return;
        EditorGUILayout.PropertyField(
            property,
            new GUIContent(label, tooltip));
    }

    void LoadOrCreateProfile()
    {
        profile = AssetDatabase.LoadAssetAtPath<
            ArcadeFlightTuningProfile>(AssetPath);
        if (profile == null)
        {
            profile = CreateInstance<ArcadeFlightTuningProfile>();
            profile.ResetToDefaults();
            AssetDatabase.CreateAsset(profile, AssetPath);
            AssetDatabase.SaveAssets();
        }
        serializedProfile = new SerializedObject(profile);
        RefreshLiveCoordinators();
    }

    static void RefreshLiveCoordinators()
    {
        if (!Application.isPlaying)
            return;
        RobocraftMotionCoordinator[] coordinators =
            FindObjectsOfType<RobocraftMotionCoordinator>();
        foreach (RobocraftMotionCoordinator coordinator in coordinators)
            coordinator.ReloadArcadeFlightTuning(false);
    }
}
