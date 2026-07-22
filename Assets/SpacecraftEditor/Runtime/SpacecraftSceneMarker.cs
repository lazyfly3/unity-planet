using UnityEngine;

namespace SpacecraftEditor
{
    public sealed class SpacecraftSceneMarker : MonoBehaviour
    {
        [SerializeField] private string installedVersion = "1.0.0";
        public string InstalledVersion => installedVersion;
    }
}
