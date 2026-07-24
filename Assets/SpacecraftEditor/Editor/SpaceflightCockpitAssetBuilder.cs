using System;
using KDL.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SpacecraftEditor.Editor
{
    public static class SpaceflightCockpitAssetBuilder
    {
        const string PreviewRequestKey = "SpaceflightCockpitAssetBuilder.PreviewRequested";
        const string PreviewPreviousModeKey =
            "SpaceflightCockpitAssetBuilder.PreviousCameraMode";
        const string RuntimeCameraModeKey = "spaceflight.cameraMode";
        const string SourceModel =
            "Assets/SpacecraftEditor/Art/Models/Cockpit/UniversalCockpit.fbx";
        const string ResourceRoot = "Assets/Resources/Spaceflight";
        const string MaterialRoot = ResourceRoot + "/CockpitMaterials";
        const string PrefabPath = ResourceRoot + "/UniversalCockpit.prefab";

        [MenuItem("Tools/Spacecraft/Rebuild Flight Cockpit Assets")]
        [AICallable(
            "Reimport the generated UniversalCockpit FBX and rebuild its Unity prefab/material bindings.",
            Category = "Spacecraft.Build")]
        public static string BuildAssets()
        {
            EnsureCockpitLayer();
            EnsureFolder(ResourceRoot);
            EnsureFolder(MaterialRoot);
            AssetDatabase.ImportAsset(SourceModel, ImportAssetOptions.ForceUpdate);

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModel);
            if (source == null)
                throw new InvalidOperationException(
                    "The Blender cockpit FBX is missing: " + SourceModel);

            Material shell = CreateMaterial(
                "CockpitShell",
                new Color(0.012f, 0.032f, 0.052f),
                0.78f,
                0.25f);
            Material trim = CreateMaterial(
                "CockpitTrim",
                new Color(0.025f, 0.11f, 0.16f),
                0.62f,
                0.22f);
            Material cyan = CreateMaterial(
                "CockpitCyan",
                new Color(0.01f, 0.18f, 0.24f),
                0.18f,
                0.25f,
                new Color(0.02f, 0.75f, 1f) * 2.2f);
            Material amber = CreateMaterial(
                "CockpitAmber",
                new Color(0.24f, 0.055f, 0.008f),
                0.22f,
                0.28f,
                new Color(1f, 0.22f, 0.015f) * 2.5f);
            Material rubber = CreateMaterial(
                "CockpitRubber",
                new Color(0.008f, 0.011f, 0.016f),
                0.05f,
                0.72f);

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
                instance = UnityEngine.Object.Instantiate(source);
            instance.name = "UniversalCockpit";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            SetLayerRecursively(instance.transform, 9);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                Material sourceMaterial = renderer.sharedMaterial;
                string materialName = sourceMaterial == null ? string.Empty : sourceMaterial.name;
                renderer.sharedMaterial = ResolveMaterial(
                    materialName,
                    renderer.name,
                    shell,
                    trim,
                    cyan,
                    amber,
                    rubber);
                renderer.shadowCastingMode = renderer.name.EndsWith("_Surface", StringComparison.Ordinal)
                    ? ShadowCastingMode.Off
                    : ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Built the universal Blender cockpit prefab at " + PrefabPath);
            return PrefabPath;
        }

        [MenuItem("Tools/Spacecraft/Validate Flight Cockpit %#k")]
        public static string ValidateAssets()
        {
            if (LayerMask.NameToLayer("CockpitView") != 9)
                throw new InvalidOperationException(
                    "CockpitView must be assigned to layer 9.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("The cockpit prefab was not built.");
            string[] requiredNodes =
            {
                "LeftMFD_Anchor",
                "CenterRadar_Anchor",
                "RightMFD_Anchor",
                "StickPivot",
                "ThrottlePivot"
            };
            foreach (string node in requiredNodes)
                if (FindDeep(prefab.transform, node) == null)
                    throw new InvalidOperationException(
                        $"The cockpit prefab is missing '{node}'.");
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException(
                    "The cockpit view model must not contain colliders.");

            ValidateScene("Assets/Scenes/InterstellarFlight.unity");
            ValidateScene("Assets/Scenes/PlanetApproach.unity");
            const string result =
                "Flight cockpit validation passed: prefab, view layer, controls, instruments and both scenes are configured.";
            Debug.Log(result);
            return result;
        }

        [MenuItem("Tools/Spacecraft/Preview Flight Cockpit")]
        [AICallable(
            "Open InterstellarFlight in Play Mode and switch to the cockpit camera for visual verification.",
            Category = "Spacecraft.Preview")]
        public static void PreviewCockpit()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorPrefs.SetInt(
                PreviewPreviousModeKey,
                PlayerPrefs.GetInt(
                    RuntimeCameraModeKey,
                    (int)SpaceflightCameraMode.ThirdPerson));
            EditorPrefs.SetBool(PreviewRequestKey, true);
            EditorSceneManager.OpenScene(
                "Assets/Scenes/InterstellarFlight.unity",
                OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void RegisterPreviewCallback()
        {
            EditorApplication.playModeStateChanged -= HandlePreviewPlayMode;
            EditorApplication.playModeStateChanged += HandlePreviewPlayMode;
        }

        static void HandlePreviewPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode
                && EditorPrefs.HasKey(PreviewPreviousModeKey))
            {
                PlayerPrefs.SetInt(
                    RuntimeCameraModeKey,
                    EditorPrefs.GetInt(
                        PreviewPreviousModeKey,
                        (int)SpaceflightCameraMode.ThirdPerson));
                PlayerPrefs.Save();
                EditorPrefs.DeleteKey(PreviewPreviousModeKey);
                EditorPrefs.SetBool(PreviewRequestKey, false);
                return;
            }
            if (state != PlayModeStateChange.EnteredPlayMode
                || !EditorPrefs.GetBool(PreviewRequestKey, false))
            {
                return;
            }
            EditorPrefs.SetBool(PreviewRequestKey, false);
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlaying)
                    return;
                InterstellarCameraRig rig =
                    UnityEngine.Object.FindObjectOfType<InterstellarCameraRig>();
                if (rig == null)
                {
                    Debug.LogError("Flight cockpit preview could not find the camera rig.");
                    return;
                }
                rig.SetMode(SpaceflightCameraMode.Cockpit, true);
                Debug.Log("Flight cockpit preview entered Play Mode in cockpit view.");
            };
        }

        static Material ResolveMaterial(
            string materialName,
            string rendererName,
            Material shell,
            Material trim,
            Material cyan,
            Material amber,
            Material rubber)
        {
            string combined = materialName + " " + rendererName;
            if (combined.IndexOf("Amber", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("WarningLight", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return amber;
            }
            if (combined.IndexOf("Cyan", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Surface", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return cyan;
            }
            if (combined.IndexOf("Rubber", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Grip", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Base", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return rubber;
            }
            if (combined.IndexOf("Trim", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Bezel", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Lip", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return trim;
            }
            return shell;
        }

        static void ValidateScene(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
            if (openedForValidation)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                int rigCount = 0;
                int cockpitCount = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    rigCount += root.GetComponentsInChildren<InterstellarCameraRig>(true).Length;
                    cockpitCount += root.GetComponentsInChildren<SpaceflightCockpitController>(true).Length;
                }
                if (rigCount != 1 || cockpitCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Scene '{scenePath}' must contain exactly one camera rig and one cockpit controller "
                        + $"(found rigs={rigCount}, cockpits={cockpitCount}).");
                }
            }
            finally
            {
                if (openedForValidation)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static Transform FindDeep(Transform root, string targetName)
        {
            if (root == null)
                return null;
            if (root.name == targetName)
                return root;
            for (int index = 0; index < root.childCount; index++)
            {
                Transform result = FindDeep(root.GetChild(index), targetName);
                if (result != null)
                    return result;
            }
            return null;
        }

        static Material CreateMaterial(
            string name,
            Color color,
            float metallic,
            float smoothness,
            Color? emission = null)
        {
            string path = MaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                material.name = name;
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
                SetLayerRecursively(root.GetChild(index), layer);
        }

        static void EnsureCockpitLayer()
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
                throw new InvalidOperationException("Unable to load TagManager.asset.");
            SerializedObject tags = new SerializedObject(assets[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            if (layers == null || layers.arraySize <= 9)
                throw new InvalidOperationException("TagManager has no layer slot 9.");
            SerializedProperty cockpitLayer = layers.GetArrayElementAtIndex(9);
            if (cockpitLayer.stringValue != "CockpitView")
            {
                cockpitLayer.stringValue = "CockpitView";
                tags.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(assets[0]);
                AssetDatabase.SaveAssets();
            }
        }

        static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }
    }
}
