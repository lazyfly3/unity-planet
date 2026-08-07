using UnityEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;

[InitializeOnLoad]
public sealed class EnemyDamageTuningWindow : EditorWindow
{
    const string GlobalKey = "UnityPlanet.EnemyDamage.Global";
    const string RangedKey = "UnityPlanet.EnemyDamage.Ranged";
    const string SuicideKey = "UnityPlanet.EnemyDamage.Suicide";

    static EnemyDamageTuningWindow()
    {
        EditorApplication.delayCall += ApplySessionValues;
        EditorApplication.playModeStateChanged += HandlePlayModeChanged;
    }

    [MenuItem("Tools/战斗/敌人伤害实时调节")]
    static void Open()
    {
        EnemyDamageTuningWindow window = GetWindow<EnemyDamageTuningWindow>();
        window.titleContent = new GUIContent("敌人伤害");
        window.minSize = new Vector2(390f, 390f);
        window.Show();
    }

    void OnEnable()
    {
        titleContent = new GUIContent("敌人伤害");
        ApplySessionValues();
        EditorApplication.update += RepaintDuringPlay;
    }

    void OnDisable()
    {
        EditorApplication.update -= RepaintDuringPlay;
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("敌人伤害实时调节", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "在 Play 模式中拖动后立即生效。只改变敌人对玩家造成的耐久伤害，" +
            "不会改变玩家武器、敌机生命、碰撞受力或 PCG/物理结构。",
            MessageType.Info);

        bool playing = EditorApplication.isPlaying;
        Color previousColor = GUI.color;
        GUI.color = playing
            ? new Color(0.55f, 1f, 0.62f)
            : new Color(1f, 0.78f, 0.38f);
        EditorGUILayout.LabelField(
            playing ? "● 正在实时应用" : "● 等待进入 Play 模式",
            EditorStyles.boldLabel);
        GUI.color = previousColor;

        float global = SessionState.GetFloat(GlobalKey, 1f);
        float ranged = SessionState.GetFloat(RangedKey, 1f);
        float suicide = SessionState.GetFloat(SuicideKey, 1f);

        EditorGUI.BeginChangeCheck();
        global = EditorGUILayout.Slider(
            new GUIContent("总伤害倍率", "同时控制所有敌人攻击"),
            global,
            EnemyDamageRuntimeTuning.MinimumMultiplier,
            EnemyDamageRuntimeTuning.MaximumMultiplier);
        ranged = EditorGUILayout.Slider(
            new GUIContent("远程射击倍率", "叠乘在总伤害倍率上"),
            ranged,
            EnemyDamageRuntimeTuning.MinimumMultiplier,
            5f);
        suicide = EditorGUILayout.Slider(
            new GUIContent("自爆伤害倍率", "叠乘在总伤害倍率上"),
            suicide,
            EnemyDamageRuntimeTuning.MinimumMultiplier,
            5f);
        if (EditorGUI.EndChangeCheck())
            SaveAndApply(global, ranged, suicide);

        EditorGUILayout.Space(6f);
        EditorGUILayout.BeginHorizontal();
        DrawPresetButton("1×", 1f);
        DrawPresetButton("1.5×", 1.5f);
        DrawPresetButton("2×", 2f);
        DrawPresetButton("3×", 3f);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("当前换算", EditorStyles.boldLabel);
        float rangedScale = global * ranged;
        float suicideScale = global * suicide;
        EditorGUILayout.LabelField(
            $"远程实际倍率：{rangedScale:0.00}×  " +
            $"（突击机每发 {10f * rangedScale:0.0}，炮艇每发 {12f * rangedScale:0.0}）");
        EditorGUILayout.LabelField(
            $"自爆实际倍率：{suicideScale:0.00}×  " +
            $"（爆心 {110f * suicideScale:0.0}，边缘按距离衰减）");
        EditorGUILayout.HelpBox(
            "倍率保存在当前 Unity 编辑器会话中，脚本重载和重新进入 Play 后仍会恢复；" +
            "关闭 Unity 后自动回到 1×，不会误写正式平衡参数。",
            MessageType.None);

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("全部恢复 1×"))
            SaveAndApply(1f, 1f, 1f);
    }

    static void DrawPresetButton(string label, float multiplier)
    {
        if (GUILayout.Button(label))
            SaveAndApply(multiplier, 1f, 1f);
    }

    static void SaveAndApply(float global, float ranged, float suicide)
    {
        global = Mathf.Clamp(global, 0f, 10f);
        ranged = Mathf.Clamp(ranged, 0f, 5f);
        suicide = Mathf.Clamp(suicide, 0f, 5f);
        SessionState.SetFloat(GlobalKey, global);
        SessionState.SetFloat(RangedKey, ranged);
        SessionState.SetFloat(SuicideKey, suicide);
        EnemyDamageRuntimeTuning.Configure(global, ranged, suicide);
    }

    static void ApplySessionValues()
    {
        SaveAndApply(
            SessionState.GetFloat(GlobalKey, 1f),
            SessionState.GetFloat(RangedKey, 1f),
            SessionState.GetFloat(SuicideKey, 1f));
    }

    static void HandlePlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode ||
            state == PlayModeStateChange.ExitingEditMode)
        {
            ApplySessionValues();
        }
    }

    void RepaintDuringPlay()
    {
        if (EditorApplication.isPlaying)
            Repaint();
    }
}
