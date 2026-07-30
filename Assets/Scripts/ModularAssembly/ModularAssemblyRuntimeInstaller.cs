using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    public static class ModularAssemblyRuntimeInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= InstallForScene;
            SceneManager.sceneLoaded += InstallForScene;
        }

        private static void InstallForScene(Scene scene, LoadSceneMode mode)
        {
            if (!ModularLabSceneProfile.AllowsBuildExperience(scene))
            {
                return;
            }

            if (Object.FindObjectOfType<NeoXCleanCatalogOverlay>() == null)
            {
                new GameObject("NeoXCleanCatalogOverlay").AddComponent<NeoXCleanCatalogOverlay>();
            }
            if (Object.FindObjectOfType<GridBuildSlotController>() == null)
            {
                new GameObject("GridBuildSlotController").AddComponent<GridBuildSlotController>();
            }
            if (Object.FindObjectOfType<NeoXCoreVisualReplacer>() == null)
            {
                new GameObject("NeoXCoreVisualReplacer").AddComponent<NeoXCoreVisualReplacer>();
            }
        }
    }
}
