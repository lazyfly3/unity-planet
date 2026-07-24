#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Bio1AntelopeVariantShowcase))]
public sealed class Bio1AntelopeVariantShowcaseEditor : Editor
{
    static readonly string[] VariantLabels = { "生物 1", "生物 2", "生物 3" };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "进入播放模式后可以编辑场景中的三只羚羊变体。",
                MessageType.Info);
            return;
        }

        var showcase = (Bio1AntelopeVariantShowcase)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("运行时外观编辑", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("状态", showcase.LastValidationResult);
        if (!showcase.IsReady)
        {
            EditorGUILayout.HelpBox("三只羚羊仍在生成。", MessageType.Info);
            Repaint();
            return;
        }
        if (!showcase.IsAppearanceEditorOpen)
        {
            if (GUILayout.Button("打开外观编辑器"))
                showcase.OpenAppearanceEditor();
            Repaint();
            return;
        }

        EditorGUI.BeginDisabledGroup(showcase.IsApplyingAppearance);
        int selected = GUILayout.Toolbar(
            showcase.SelectedVariantIndex, VariantLabels);
        if (selected != showcase.SelectedVariantIndex)
            showcase.SelectEditableVariant(selected);

        NmsAntelopeVariantParameters draft = showcase.EditDraft;
        if (draft != null)
        {
            Vector3 scale = draft.visualScale;
            Vector3 nextScale = new Vector3(
                EditorGUILayout.Slider("宽度", scale.x, 0.82f, 1.22f),
                EditorGUILayout.Slider("高度", scale.y, 0.72f, 1.30f),
                EditorGUILayout.Slider("长度", scale.z, 0.96f, 1.04f));
            if ((nextScale - scale).sqrMagnitude > 0.0000001f)
                showcase.SetDraftScale(nextScale);

            EditorGUILayout.Space();
            DrawModule(showcase, "身体", NmsAntelopeEditablePart.Body);
            DrawModule(showcase, "装饰", NmsAntelopeEditablePart.Accessory);
            DrawModule(showcase, "耳朵", NmsAntelopeEditablePart.Ears);
            DrawModule(showcase, "角", NmsAntelopeEditablePart.Horns);
            DrawModule(showcase, "尾巴", NmsAntelopeEditablePart.Tail);

            EditorGUILayout.Space();
            Color primary = EditorGUILayout.ColorField("主色", draft.primaryColor);
            Color secondary = EditorGUILayout.ColorField("辅色", draft.secondaryColor);
            Color accent = EditorGUILayout.ColorField("强调色", draft.accentColor);
            if (primary != draft.primaryColor
                || secondary != draft.secondaryColor
                || accent != draft.accentColor)
                showcase.SetDraftColors(primary, secondary, accent);
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("应用"))
            showcase.ApplyAppearanceDraft();
        if (GUILayout.Button("恢复"))
            showcase.RevertAppearancePreview();
        if (GUILayout.Button("关闭"))
            showcase.CloseAppearanceEditor();
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
        Repaint();
    }

    static void DrawModule(
        Bio1AntelopeVariantShowcase showcase,
        string label,
        NmsAntelopeEditablePart part)
    {
        NmsAntelopeVariantParameters draft = showcase.EditDraft;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        if (GUILayout.Button("<", GUILayout.Width(28f)))
            showcase.CycleDraftModule(part, -1);
        EditorGUILayout.LabelField(
            NmsAntelopeEditingOptions.DisplayName(draft.GetModule(part)),
            EditorStyles.helpBox);
        if (GUILayout.Button(">", GUILayout.Width(28f)))
            showcase.CycleDraftModule(part, 1);
        EditorGUILayout.EndHorizontal();
    }
}
#endif
