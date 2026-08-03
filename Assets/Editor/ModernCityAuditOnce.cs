#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityPlanet.EditorTools
{
    internal static class ModernCityAuditOnce
    {
        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            const string request = "Temp/ModernCityAudit.request";
            if (!File.Exists(request))
                return;
            File.Delete(request);
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            var output = new StringBuilder();
            for (int index = 1; index <= 25; index++)
            {
                string path = "Assets/Pack/Models/Building/Building "
                    + index.ToString("00") + ".FBX";
                GameObject asset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                {
                    output.AppendLine(index.ToString("00") + " MISSING");
                    continue;
                }
                GameObject instance = Object.Instantiate(asset);
                instance.hideFlags = HideFlags.HideAndDontSave;
                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                bool found = false;
                Bounds bounds = default;
                string shader = "none";
                foreach (Renderer renderer in renderers)
                {
                    if (!found)
                    {
                        bounds = renderer.bounds;
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                    if (renderer.sharedMaterial != null
                        && renderer.sharedMaterial.shader != null)
                    {
                        shader = renderer.sharedMaterial.shader.name;
                    }
                }
                Vector3 size = bounds.size;
                float ratio = size.y / Mathf.Max(0.001f, Mathf.Max(size.x, size.z));
                output.AppendLine(
                    index.ToString("00")
                    + " size=" + size.ToString("F2")
                    + " ratio=" + ratio.ToString("F2")
                    + " renderers=" + renderers.Length
                    + " shader=" + shader);
                Object.DestroyImmediate(instance);
            }
            File.WriteAllText("Temp/ModernCityAudit.result.txt", output.ToString());
        }
    }
}
#endif
