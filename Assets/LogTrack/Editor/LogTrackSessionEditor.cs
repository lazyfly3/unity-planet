#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LogTrack.Editor
{
    [CustomEditor(typeof(LogTrackSession))]
    public class LogTrackSessionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "手动模式：当关闭「Play 时自动启动」时使用。自动模式下无需挂此组件。",
                MessageType.Info);

            var ringBufferProp = serializedObject.FindProperty("ringBufferSize");
            EditorGUILayout.IntSlider(
                ringBufferProp,
                LogTrackSettings.MinRingBufferSize,
                LogTrackSettings.MaxRingBufferSize,
                new GUIContent("Ring Buffer Size", "保留最近多少帧的 LogTrack 记录"));

            DrawPropertiesExcluding(serializedObject, "m_Script", "ringBufferSize");

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
