using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Removes the two legacy build-platform slabs while preserving the
    /// perimeter posts and every PlanetLab flight-environment surface.
    /// </summary>
    internal sealed class BuildPlatformSlabRemover : MonoBehaviour
    {
        private const string LabSceneName = "ModularAssemblyLab";
        private const string PlatformObjectName = "IndustrialTestPlatform";
        private const float SlabMinimumHorizontalSize = 2f;

        private int searchFramesRemaining = 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (SceneManager.GetActiveScene().name != LabSceneName ||
                FindObjectOfType<BuildPlatformSlabRemover>() != null)
            {
                return;
            }

            new GameObject(nameof(BuildPlatformSlabRemover))
                .AddComponent<BuildPlatformSlabRemover>();
        }

        private void LateUpdate()
        {
            bool removedSlab = false;
            Transform[] sceneTransforms = FindObjectsOfType<Transform>(true);

            for (int i = 0; i < sceneTransforms.Length; i++)
            {
                Transform candidate = sceneTransforms[i];
                if (candidate == null || candidate.gameObject.name != PlatformObjectName)
                {
                    continue;
                }

                Vector3 size = candidate.lossyScale;
                if (Mathf.Abs(size.x) < SlabMinimumHorizontalSize ||
                    Mathf.Abs(size.z) < SlabMinimumHorizontalSize)
                {
                    continue;
                }

                Destroy(candidate.gameObject);
                removedSlab = true;
            }

            searchFramesRemaining--;
            if (removedSlab || searchFramesRemaining <= 0)
            {
                enabled = false;
            }
        }
    }
}
