using UnityEngine;

namespace UnityPlanet.CityPcg
{
    /// <summary>
    /// Keeps an authored PCG preview visible in Edit Mode while preventing it
    /// from entering Play Mode and overlapping the scene's real test harness.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CityPcgEditorPreviewOnly : MonoBehaviour
    {
        void OnEnable()
        {
            if (Application.isPlaying)
                gameObject.SetActive(false);
        }

        void Update()
        {
            if (Application.isPlaying && gameObject.activeSelf)
                gameObject.SetActive(false);
        }
    }
}
