#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityPlanet.EditorTools
{
    internal static class CombatMapCaptureOnce
    {
        // Temporary visual verification for curated authored high-rises.
        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            const string request = "Temp/CombatMapCapture.request";
            if (!File.Exists(request)) return;
            File.Delete(request);
            EditorApplication.delayCall += Capture;
        }

        private static void Capture()
        {
            const string scenePath = "Assets/Scenes/CombatMapRuntime.unity";
            Scene scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Additive);
            GameObject horde = null;
            GameObject duel = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "CombatMapEnvironment_Horde") horde = root;
                if (root.name == "CombatMapEnvironment_Duel") duel = root;
            }
            if (horde == null)
            {
                File.WriteAllText(
                    "Temp/CombatMapCapture.result.txt",
                    "FAIL: Horde root missing");
                EditorSceneManager.CloseScene(scene, true);
                return;
            }
            if (duel != null) duel.SetActive(false);
            horde.SetActive(true);
            const int captureLayer = 31;
            foreach (Transform value in
                     horde.GetComponentsInChildren<Transform>(true))
            {
                value.gameObject.layer = captureLayer;
            }
            foreach (Renderer renderer in
                     horde.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = !renderer.name.StartsWith(
                    "PlaceholderVisual_");
            }

            Bounds bounds = default;
            bool found = false;
            foreach (Renderer renderer in horde.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled
                    || renderer.name.Contains("OutskirtsTerrainSkirt"))
                    continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            float span = Mathf.Max(1536f, Mathf.Max(bounds.size.x, bounds.size.z));
            Vector3 mapCenter = horde.transform.position;
            var cameraObject = new GameObject("CombatMapCaptureCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 54f;
            camera.nearClipPlane = 2f;
            camera.farClipPlane = Mathf.Max(5000f, span * 5f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.67f, 0.75f);
            camera.cullingMask = 1 << captureLayer;
            camera.transform.position = mapCenter
                + new Vector3(-850f, 680f, -1050f);
            camera.transform.LookAt(mapCenter + Vector3.up * 55f);

            var lightObject = new GameObject("CombatMapCaptureLight");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.cullingMask = 1 << captureLayer;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.5f, 0.54f);

            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            image.Apply();
            File.WriteAllBytes(
                "Temp/CombatMapHordePreview.png",
                image.EncodeToPNG());

            var modelRoots = new List<Transform>();
            int enabledModelRenderers = 0;
            var shaders = new HashSet<string>();
            var modelDetails = new List<string>();
            Bounds cityBounds = default;
            bool hasCityBounds = false;
            foreach (Transform value in horde.GetComponentsInChildren<Transform>(true))
            {
                if (value.name == "Model_Building"
                    || value.name == "Model_Beacon")
                {
                    modelRoots.Add(value);
                    foreach (Renderer renderer in
                             value.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                            continue;
                        enabledModelRenderers++;
                        if (!hasCityBounds)
                        {
                            cityBounds = renderer.bounds;
                            hasCityBounds = true;
                        }
                        else
                        {
                            cityBounds.Encapsulate(renderer.bounds);
                        }
                        foreach (Material material in renderer.sharedMaterials)
                        {
                            if (material != null && material.shader != null)
                                shaders.Add(material.shader.name);
                        }
                    }
                    Transform upper = value.Find("Upper_00");
                    string visualName = upper != null && upper.childCount > 0
                        ? upper.GetChild(0).name
                        : "missing-upper";
                    modelDetails.Add(
                        value.position.ToString("F1") + " " + visualName);
                }
            }
            Vector3 cityFocus = mapCenter + Vector3.up * 70f;
            if (hasCityBounds)
                cityFocus = cityBounds.center;
            camera.transform.position = cityFocus
                + new Vector3(-270f, 170f, -310f);
            camera.transform.LookAt(cityFocus + Vector3.up * 8f);
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            image.Apply();
            File.WriteAllBytes(
                "Temp/CombatMapHordeCityCloseup.png",
                image.EncodeToPNG());
            File.WriteAllText(
                "Temp/CombatMapCapture.result.txt",
                "PASS\ncenter=" + mapCenter.ToString("F2")
                + "\nbounds=" + bounds.ToString()
                + "\nspan=" + span.ToString("F2")
                + "\ncityFocus=" + cityFocus.ToString("F2")
                + "\nmodels=" + modelRoots.Count
                + "\nenabledModelRenderers=" + enabledModelRenderers
                + "\nshaders=" + string.Join(",", shaders)
                + "\n" + string.Join("\n", modelDetails));
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(lightObject);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif
