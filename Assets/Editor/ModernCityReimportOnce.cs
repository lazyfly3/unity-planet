#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace UnityPlanet.EditorTools
{
    internal static class ModernCityReimportOnce
    {
        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            const string request = "Temp/ModernCityReimport.request";
            if (!File.Exists(request))
                return;
            File.Delete(request);
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int index = 1; index <= 25; index++)
                {
                    AssetDatabase.ImportAsset(
                        "Assets/Pack/Models/Building/Building "
                        + index.ToString("00") + ".FBX",
                        ImportAssetOptions.ForceUpdate);
                }
                string[] dependencies =
                {
                    "Assets/Pack/Models/Materials/Building Atlas.mat",
                    "Assets/Pack/Models/Texture/Building Atlas.tif",
                    "Assets/Pack/Models/Texture/Building Atlas Normal.tif",
                    "Assets/Pack/Models/Texture/Building Illumination.tif"
                };
                foreach (string dependency in dependencies)
                {
                    AssetDatabase.ImportAsset(
                        dependency,
                        ImportAssetOptions.ForceUpdate);
                }
                File.WriteAllText(
                    "Temp/ModernCityReimport.result.txt",
                    "PASS\n25 authored buildings and 4 direct dependencies reimported.");
            }
            catch (Exception exception)
            {
                File.WriteAllText(
                    "Temp/ModernCityReimport.result.txt",
                    "FAIL\n" + exception);
                throw;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }
    }
}
#endif
