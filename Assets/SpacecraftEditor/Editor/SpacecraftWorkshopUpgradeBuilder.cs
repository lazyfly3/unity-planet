using System;
using KDL.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SpacecraftEditor.Editor
{
    public static class SpacecraftWorkshopUpgradeBuilder
    {
        const string WorkshopPrefab =
            "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab";
        const string WorkshopScene =
            "Assets/Scenes/SpacecraftWorkshop.unity";

        [MenuItem("Tools/Spacecraft/Rebuild Workshop Lighting And Snapping")]
        [AICallable(
            "重建设计间棚拍照明，并发布表面法线、网格、邻件对齐和部件表面拼接吸附参数。",
            Category = "Spacecraft.Build",
            Kind = ToolKind.Write)]
        public static string Rebuild()
        {
            UpgradePrefab();
            UpgradeScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return "Workshop upgraded: bright PBR lighting and free-surface modular placement.";
        }

        [AICallable(
            "Validate free-surface placement, optional grid control, edge magnetism, contact tolerance and workshop prefab settings.",
            Category = "Spacecraft.Build",
            Kind = ToolKind.Read)]
        public static string ValidateFreeSurfacePlacement()
        {
            if (!SpacecraftPlacementSolver.TrySnapInterval(
                    1.02f,
                    0.50f,
                    -0.50f,
                    0.50f,
                    0.10f,
                    out float snappedCenter,
                    out PlacementAlignmentKind kind) ||
                !Mathf.Approximately(snappedCenter, 1f) ||
                kind != PlacementAlignmentKind.NeighborEdge)
            {
                throw new InvalidOperationException(
                    "Neighbor edge magnetism validation failed.");
            }

            var left = new SpacecraftPlacementSolver.OrientedBox(
                Vector3.zero,
                Vector3.one * 0.5f,
                Quaternion.identity);
            var touching = new SpacecraftPlacementSolver.OrientedBox(
                Vector3.right,
                Vector3.one * 0.5f,
                Quaternion.identity);
            var penetrating = new SpacecraftPlacementSolver.OrientedBox(
                Vector3.right * 0.97f,
                Vector3.one * 0.5f,
                Quaternion.identity);
            if (SpacecraftPlacementSolver.Overlaps(left, touching, 0.012f) ||
                !SpacecraftPlacementSolver.Overlaps(left, penetrating, 0.012f))
            {
                throw new InvalidOperationException(
                    "Contact-tolerant collision validation failed.");
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopPrefab);
            BuildModeController controller =
                prefab == null
                    ? null
                    : prefab.GetComponentInChildren<BuildModeController>(true);
            if (controller == null)
                throw new InvalidOperationException(
                    "Workshop BuildModeController is missing.");
            var serialized = new SerializedObject(controller);
            if (!serialized.FindProperty("gridSnapRequiresControl").boolValue ||
                !serialized.FindProperty("allowPartSurfacePlacement").boolValue ||
                serialized.FindProperty("neighborAlignmentDistance").floatValue > 0.1001f)
            {
                throw new InvalidOperationException(
                    "Workshop free-placement settings are invalid.");
            }

            return "Free-surface placement validated: exact cursor placement, "
                + "Ctrl grid, 10 cm edge magnetism, part stacking and contact-safe collision.";
        }

        static void UpgradePrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WorkshopPrefab);
            try
            {
                UpgradeHierarchy(root);
                PrefabUtility.SaveAsPrefabAsset(root, WorkshopPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void UpgradeScene()
        {
            Scene scene = SceneManager.GetSceneByPath(WorkshopScene);
            bool openedForBuild = !scene.IsValid() || !scene.isLoaded;
            if (!openedForBuild && scene.isDirty)
                throw new InvalidOperationException(
                    "SpacecraftWorkshop has unsaved changes; save them before rebuilding.");
            Scene previousActive = SceneManager.GetActiveScene();
            if (openedForBuild)
                scene = EditorSceneManager.OpenScene(WorkshopScene, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                foreach (GameObject root in scene.GetRootGameObjects())
                    UpgradeHierarchy(root);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.34f, 0.42f, 0.50f);
                RenderSettings.ambientEquatorColor = new Color(0.24f, 0.29f, 0.34f);
                RenderSettings.ambientGroundColor = new Color(0.12f, 0.14f, 0.17f);
                RenderSettings.ambientIntensity = 1.25f;
                RenderSettings.reflectionIntensity = 1.35f;
                Camera camera = FindInScene<Camera>(scene);
                if (camera != null)
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.018f, 0.035f, 0.052f);
                    camera.allowHDR = true;
                    EditorUtility.SetDirty(camera);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                if (openedForBuild)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void UpgradeHierarchy(GameObject root)
        {
            Transform lights = FindDeepChild(root.transform, "Lights");
            if (lights != null)
            {
                WorkshopLightingRig rig = lights.GetComponent<WorkshopLightingRig>();
                if (rig == null)
                    rig = lights.gameObject.AddComponent<WorkshopLightingRig>();
                EditorUtility.SetDirty(rig);
                EnsureDirectionalLight(
                    lights, "KeyLight", new Vector3(36f, -28f, 0f),
                    new Color(1.00f, 0.91f, 0.80f), 0.8f, LightShadows.Soft);
                EnsureDirectionalLight(
                    lights, "FillLight", new Vector3(325f, 145f, 0f),
                    new Color(0.65f, 0.82f, 1.00f), 0.22f, LightShadows.None);
                EnsurePointLight(
                    lights, "TopLight", new Vector3(0f, 6.5f, -1.5f),
                    new Color(0.82f, 0.91f, 1.00f), 1.25f, 18f);
                EnsurePointLight(
                    lights, "RimLight", new Vector3(-4.5f, 2.5f, 3.5f),
                    new Color(0.30f, 0.72f, 1.00f), 0.65f, 18f);
                EnsureReflectionProbe(lights);
            }

            foreach (BuildModeController controller in
                     root.GetComponentsInChildren<BuildModeController>(true))
            {
                var serialized = new SerializedObject(controller);
                Set(serialized, "snappingEnabled", true);
                Set(serialized, "snapEnterAngle", 24f);
                Set(serialized, "snapReleaseAngle", 32f);
                Set(serialized, "centerSnapNormalizedRadius", 0.16f);
                Set(serialized, "centerSnapWorldDistance", 0.10f);
                Set(serialized, "surfaceGridSize", 0.25f);
                Set(serialized, "neighborAlignmentDistance", 0.10f);
                Set(serialized, "gridSnapRequiresControl", true);
                Set(serialized, "allowPartSurfacePlacement", true);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(controller);
            }

            Camera camera = root.GetComponentInChildren<Camera>(true);
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.018f, 0.035f, 0.052f);
                camera.allowHDR = true;
                EditorUtility.SetDirty(camera);
            }
        }

        static void EnsureReflectionProbe(Transform lights)
        {
            Transform probeTransform = lights.Find("WorkshopReflectionProbe");
            if (probeTransform == null)
            {
                probeTransform = new GameObject("WorkshopReflectionProbe").transform;
                probeTransform.SetParent(lights, false);
            }
            ReflectionProbe probe = probeTransform.GetComponent<ReflectionProbe>();
            if (probe == null)
                probe = probeTransform.gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(30f, 20f, 30f);
            probe.intensity = 1.2f;
            probe.boxProjection = true;
            probe.hdr = true;
            probe.cullingMask = ~0;
            EditorUtility.SetDirty(probe);
        }

        static void EnsureDirectionalLight(
            Transform parent,
            string objectName,
            Vector3 eulerAngles,
            Color color,
            float intensity,
            LightShadows shadows)
        {
            Transform lightTransform = FindOrCreateChild(parent, objectName);
            Light light = lightTransform.GetComponent<Light>();
            if (light == null)
                light = lightTransform.gameObject.AddComponent<Light>();

            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows;
            lightTransform.localPosition = Vector3.zero;
            lightTransform.localRotation = Quaternion.Euler(eulerAngles);
            lightTransform.localScale = Vector3.one;
            EditorUtility.SetDirty(light);
        }

        static void EnsurePointLight(
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Color color,
            float intensity,
            float range)
        {
            Transform lightTransform = FindOrCreateChild(parent, objectName);
            Light light = lightTransform.GetComponent<Light>();
            if (light == null)
                light = lightTransform.gameObject.AddComponent<Light>();

            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            lightTransform.localPosition = localPosition;
            lightTransform.localRotation = Quaternion.identity;
            lightTransform.localScale = Vector3.one;
            EditorUtility.SetDirty(light);
        }

        static Transform FindOrCreateChild(Transform parent, string objectName)
        {
            Transform child = parent.Find(objectName);
            if (child != null)
                return child;

            child = new GameObject(objectName).transform;
            child.SetParent(parent, false);
            return child;
        }

        static Transform FindDeepChild(Transform root, string objectName)
        {
            if (root.name == objectName)
                return root;
            for (int index = 0; index < root.childCount; index++)
            {
                Transform result = FindDeepChild(root.GetChild(index), objectName);
                if (result != null)
                    return result;
            }
            return null;
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T result = root.GetComponentInChildren<T>(true);
                if (result != null)
                    return result;
            }
            return null;
        }

        static void Set(SerializedObject target, string propertyName, bool value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        static void Set(SerializedObject target, string propertyName, float value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null)
                property.floatValue = value;
        }
    }
}
