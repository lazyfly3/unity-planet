using System;
using UnityEditor;
using UnityEngine;

namespace SpacecraftEditor.Editor
{
    public sealed class SpacecraftPcgWindow : EditorWindow
    {
        static readonly string[] Archetypes = { "balanced", "spindle", "saucer" };
        static readonly string[] Modules =
        {
            "ThrusterSmall", "ThrusterMedium", "ThrusterLarge",
            "SweptWing", "DeltaWing", "Canard", "VerticalFin",
            "Radiator", "SensorMast", "EngineNacelle", "ArmorFairing",
            "EnergyPulse", "KineticRepeater"
        };

        string blenderPath;
        SpacecraftPcgScope scope;
        int archetypeIndex;
        int seedIndex;
        int moduleIndex;
        Vector2 logScroll;
        string log = "Ready.";

        [MenuItem("Tools/Spacecraft/PCG Fleet")]
        static void Open()
        {
            GetWindow<SpacecraftPcgWindow>("Spacecraft PCG");
        }

        void OnEnable()
        {
            blenderPath = SpacecraftPcgPipeline.ResolveBlenderPath();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Blender 5.1 Offline Fleet Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Generation runs outside play mode, validates in Temp, and publishes only after Blender succeeds.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            blenderPath = EditorGUILayout.TextField("Blender", blenderPath);
            if (GUILayout.Button("Browse", GUILayout.Width(72f)))
            {
                string selected = EditorUtility.OpenFilePanel("Select Blender 5.1", "", "exe");
                if (!string.IsNullOrEmpty(selected))
                    blenderPath = selected;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Save Blender Path"))
            {
                EditorPrefs.SetString(SpacecraftPcgPipeline.BlenderEditorPrefsKey, blenderPath);
                log = "Saved Blender path.";
            }

            EditorGUILayout.Space();
            scope = (SpacecraftPcgScope)EditorGUILayout.EnumPopup("Selected Scope", scope);
            if (scope == SpacecraftPcgScope.Hull)
            {
                archetypeIndex = EditorGUILayout.Popup("Archetype", archetypeIndex, Archetypes);
                seedIndex = EditorGUILayout.IntSlider("Seed Index", seedIndex, 0, 7);
            }
            else
            {
                moduleIndex = EditorGUILayout.Popup("Module", moduleIndex, Modules);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate Selected", GUILayout.Height(32f)))
                RunSelected();
            if (GUILayout.Button("Generate All", GUILayout.Height(32f)))
                Run(() => SpacecraftPcgPipeline.GenerateAll(blenderPath));
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Validate Published Assets"))
                Run(SpacecraftPcgPipeline.ValidatePublishedAssets);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
            logScroll = EditorGUILayout.BeginScrollView(logScroll);
            EditorGUILayout.SelectableLabel(log, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void RunSelected()
        {
            if (scope == SpacecraftPcgScope.Hull)
            {
                Run(() => SpacecraftPcgPipeline.GenerateSelectedHull(
                    Archetypes[archetypeIndex],
                    seedIndex,
                    blenderPath));
            }
            else
            {
                Run(() => SpacecraftPcgPipeline.GenerateSelectedModule(
                    Modules[moduleIndex],
                    blenderPath));
            }
        }

        void Run(Func<string> action)
        {
            try
            {
                log = action();
            }
            catch (Exception exception)
            {
                log = exception.ToString();
                Debug.LogException(exception);
            }
            Repaint();
        }
    }
}
