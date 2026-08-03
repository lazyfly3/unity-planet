#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.EditorTools
{
    internal static class CombatMapOpenPreviewOnce
    {
        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            const string request = "Temp/CombatMapOpenPreview.request";
            if (!File.Exists(request))
                return;
            File.Delete(request);
            EditorApplication.delayCall += Open;
        }

        private static void Open()
        {
            const string path = "Assets/Scenes/CombatMapRuntime.unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    path,
                    OpenSceneMode.Additive);
            }
            GameObject horde = null;
            GameObject duel = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "CombatMapEnvironment_Horde")
                    horde = root;
                else if (root.name == "CombatMapEnvironment_Duel")
                    duel = root;
            }
            if (horde == null)
            {
                File.WriteAllText(
                    "Temp/CombatMapOpenPreview.result.txt",
                    "FAIL: Horde root missing");
                return;
            }
            if (duel != null)
                duel.SetActive(false);
            horde.SetActive(true);
            Bounds bounds = default;
            bool found = false;
            foreach (Renderer renderer in
                     horde.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = !renderer.name.StartsWith(
                    "PlaceholderVisual_");
                if (!renderer.enabled
                    || renderer.name.Contains("OutskirtsTerrainSkirt"))
                {
                    continue;
                }
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            Selection.activeGameObject = horde;
            SceneManager.SetActiveScene(scene);
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && found)
            {
                view.pivot = bounds.center + Vector3.up * 20f;
                view.rotation = Quaternion.Euler(28f, 225f, 0f);
                view.size = Mathf.Max(bounds.size.x, bounds.size.z) * 0.48f;
                view.Repaint();
            }
            File.WriteAllText(
                "Temp/CombatMapOpenPreview.result.txt",
                "PASS\nLoaded additively without saving preview state.");
        }
    }
}
#endif
