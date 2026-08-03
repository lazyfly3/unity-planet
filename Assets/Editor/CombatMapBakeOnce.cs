#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UnityPlanet.EditorTools
{
    internal static class CombatMapBakeOnce
    {
        // Temporary targeted bake runner for grounded authored high-rises.
        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            const string request = "Temp/CombatMapBake.request";
            if (!File.Exists(request))
                return;
            File.Delete(request);
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            try
            {
                Scene runtime = SceneManager.GetSceneByPath(
                    "Assets/Scenes/CombatMapRuntime.unity");
                if (runtime.IsValid() && runtime.isLoaded)
                {
                    Scene lab = SceneManager.GetSceneByPath(
                        "Assets/Scenes/CombatMapLab.unity");
                    if (lab.IsValid() && lab.isLoaded)
                        SceneManager.SetActiveScene(lab);
                    EditorSceneManager.CloseScene(runtime, true);
                }
                CombatMapRuntimeSceneBaker.BakeRuntimeScene();
                File.WriteAllText("Temp/CombatMapBake.result.txt", "PASS");
            }
            catch (Exception exception)
            {
                File.WriteAllText(
                    "Temp/CombatMapBake.result.txt",
                    "FAIL\n" + exception);
                throw;
            }
        }
    }
}
#endif
