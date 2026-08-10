using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityPlanet.CityPcg;

public sealed class 城市风场与磁场控制台 : EditorWindow
{
    readonly List<UrbanEnvironmentalFieldVolume> fields =
        new List<UrbanEnvironmentalFieldVolume>();
    Vector2 scroll;
    double nextRefreshAt;

    [MenuItem("工具/城市战斗/风场与磁场控制台")]
    static void 打开窗口()
    {
        城市风场与磁场控制台 window =
            GetWindow<城市风场与磁场控制台>();
        window.titleContent = new GUIContent("城市环境场");
        window.minSize = new Vector2(520f, 420f);
        window.Show();
    }

    void OnEnable()
    {
        titleContent = new GUIContent("城市环境场");
        刷新场列表();
    }

    void OnHierarchyChange()
    {
        刷新场列表();
        Repaint();
    }

    void OnInspectorUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextRefreshAt)
            return;
        nextRefreshAt = EditorApplication.timeSinceStartup + 0.35d;
        刷新场列表();
        Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField(
            "城市风场与磁场控制台",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "用于城市场景演示和调试。‘开启’会持续保持生效，" +
            "‘关闭’会立即停止作用并释放磁场吸附，‘自动’会恢复正常循环。" +
            "这些控制只影响当前运行实例，不会改写 PCG 难度参数。",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            EditorApplication.isPlaying
                ? "编辑器状态：正在运行"
                : "编辑器状态：未运行",
            GUILayout.Width(190f));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("重新扫描场景", GUILayout.Width(110f)))
            刷新场列表();
        EditorGUILayout.EndHorizontal();

        int windCount = fields.Count(field =>
            field.Kind == UrbanEnvironmentalFieldKind.NaturalStreetGale);
        int magnetCount = fields.Count(field =>
            field.Kind == UrbanEnvironmentalFieldKind.MagneticCourtyard);
        EditorGUILayout.LabelField(
            $"当前找到：风场 {windCount} 个，磁场 {magnetCount} 个");

        EditorGUILayout.Space(6f);
        绘制批量控制(
            "全部风场",
            UrbanEnvironmentalFieldKind.NaturalStreetGale,
            windCount);
        绘制批量控制(
            "全部磁场",
            UrbanEnvironmentalFieldKind.MagneticCourtyard,
            magnetCount);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("场景中的环境场", EditorStyles.boldLabel);
        if (fields.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "当前场景没有已生成的风场或磁场。请进入城市战斗，" +
                "等待 PCG 城市完成生成后点击“重新扫描场景”。",
                MessageType.Warning);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (UrbanEnvironmentalFieldVolume field in fields)
            绘制单个场(field);
        EditorGUILayout.EndScrollView();
    }

    void 绘制批量控制(
        string 标题,
        UrbanEnvironmentalFieldKind 类型,
        int 数量)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"{标题}（{数量}）", GUILayout.Width(150f));
        using (new EditorGUI.DisabledScope(数量 == 0))
        {
            if (GUILayout.Button("开启", GUILayout.Width(72f)))
                设置全部(类型, UrbanEnvironmentalFieldControlMode.ForcedActive);
            if (GUILayout.Button("关闭", GUILayout.Width(72f)))
                设置全部(类型, UrbanEnvironmentalFieldControlMode.ForcedDisabled);
            if (GUILayout.Button("恢复自动", GUILayout.Width(88f)))
                设置全部(类型, UrbanEnvironmentalFieldControlMode.Automatic);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    void 绘制单个场(UrbanEnvironmentalFieldVolume field)
    {
        if (field == null)
            return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(
            field.Kind == UrbanEnvironmentalFieldKind.NaturalStreetGale
                ? "自然街道风场"
                : "三面磁场",
            EditorStyles.boldLabel,
            GUILayout.Width(120f));
        EditorGUILayout.LabelField(field.name);
        if (GUILayout.Button("定位", GUILayout.Width(54f)))
        {
            Selection.activeGameObject = field.gameObject;
            EditorGUIUtility.PingObject(field.gameObject);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(
            $"当前状态：{状态名称(field.State)}    " +
            $"控制方式：{控制名称(field.ControlMode)}    " +
            $"范围：{field.LocalSize.x:0} × {field.LocalSize.y:0} × " +
            $"{field.LocalSize.z:0} 米");

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("开启", GUILayout.Width(72f)))
            设置单个(field, UrbanEnvironmentalFieldControlMode.ForcedActive);
        if (GUILayout.Button("关闭", GUILayout.Width(72f)))
            设置单个(field, UrbanEnvironmentalFieldControlMode.ForcedDisabled);
        if (GUILayout.Button("恢复自动", GUILayout.Width(88f)))
            设置单个(field, UrbanEnvironmentalFieldControlMode.Automatic);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    void 设置全部(
        UrbanEnvironmentalFieldKind 类型,
        UrbanEnvironmentalFieldControlMode 模式)
    {
        int changed = 0;
        foreach (UrbanEnvironmentalFieldVolume field in fields)
        {
            if (field == null || field.Kind != 类型)
                continue;
            field.SetControlMode(模式);
            changed++;
        }
        SceneView.RepaintAll();
        Repaint();
        Debug.Log($"城市环境场控制：已将 {changed} 个{类型名称(类型)}设置为{控制名称(模式)}。");
    }

    void 设置单个(
        UrbanEnvironmentalFieldVolume field,
        UrbanEnvironmentalFieldControlMode 模式)
    {
        if (field == null)
            return;
        field.SetControlMode(模式);
        SceneView.RepaintAll();
        Repaint();
        Debug.Log($"城市环境场控制：{field.name} 已设置为{控制名称(模式)}。", field);
    }

    void 刷新场列表()
    {
        fields.Clear();
        fields.AddRange(Resources
            .FindObjectsOfTypeAll<UrbanEnvironmentalFieldVolume>()
            .Where(field =>
                field != null &&
                field.gameObject.scene.IsValid() &&
                field.gameObject.scene.isLoaded)
            .OrderBy(field => field.Kind)
            .ThenBy(field => field.name, StringComparer.Ordinal));
    }

    static string 状态名称(UrbanEnvironmentalFieldState 状态)
    {
        switch (状态)
        {
            case UrbanEnvironmentalFieldState.Warning: return "预警";
            case UrbanEnvironmentalFieldState.Active: return "开启";
            case UrbanEnvironmentalFieldState.Cooldown: return "冷却";
            default: return "休眠";
        }
    }

    static string 控制名称(UrbanEnvironmentalFieldControlMode 模式)
    {
        switch (模式)
        {
            case UrbanEnvironmentalFieldControlMode.ForcedDisabled:
                return "强制关闭";
            case UrbanEnvironmentalFieldControlMode.ForcedActive:
                return "强制开启";
            default:
                return "自动控制";
        }
    }

    static string 类型名称(UrbanEnvironmentalFieldKind 类型)
    {
        return 类型 == UrbanEnvironmentalFieldKind.NaturalStreetGale
            ? "风场"
            : "磁场";
    }
}
