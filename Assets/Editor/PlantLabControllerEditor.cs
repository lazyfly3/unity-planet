using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlantLabController))]
public sealed class PlantLabControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "程序化植物实验：运行场景后使用中文面板调整参数。主预览会在停止拖动 180ms 后重建，"
            + "点击“应用到球面散布”才会替换共享变体池。",
            MessageType.Info);
        DrawDefaultInspector();

        PlantLabController controller = (PlantLabController)target;
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("重新生成主预览"))
                controller.RegeneratePreview();
            if (GUILayout.Button("应用到球面散布"))
                controller.ApplyToScatter();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("300 株"))
                controller.SetStressMode(false);
            if (GUILayout.Button("1000 株压力测试"))
                controller.SetStressMode(true);
            EditorGUILayout.EndHorizontal();
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("运行 plant 场景后可使用这些按钮。", MessageType.None);
    }
}
