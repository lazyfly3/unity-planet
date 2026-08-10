using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.SpaceStation
{
    /// <summary>
    /// Adds two restrained, shadow-free fill lights to the station's non-dock
    /// rooms. The docking-bay key/warm lights are intentionally left alone.
    /// </summary>
    public static class SpaceStationInteriorLightingRuntime
    {
        const string StationSceneName = "SpaceStationUpgradeTest";
        const string RootName = "StationInteriorReadabilityFill";

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterSceneHook()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallForInitialScene()
        {
            Install(SceneManager.GetActiveScene());
        }

        static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Install(scene);
        }

        static void Install(Scene scene)
        {
            if (!scene.IsValid() || scene.name != StationSceneName ||
                GameObject.Find(RootName) != null)
            {
                return;
            }

            GameObject root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            CreateFill(root.transform, "MainRoomFill",
                new Vector3(7.5f, 2.6f, 1f), 6.8f, 0.38f,
                new Color(0.74f, 0.87f, 1f));
            CreateFill(root.transform, "JunctionFill",
                new Vector3(-7.7f, 2.55f, 1f), 5.2f, 0.30f,
                new Color(0.78f, 0.89f, 1f));
        }

        static void CreateFill(
            Transform parent,
            string name,
            Vector3 position,
            float range,
            float intensity,
            Color color)
        {
            GameObject fillObject = new GameObject(name);
            fillObject.transform.SetParent(parent, false);
            fillObject.transform.position = position;

            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = range;
            fill.intensity = intensity;
            fill.color = color;
            fill.shadows = LightShadows.None;
            fill.renderMode = LightRenderMode.ForceVertex;
        }
    }
}
