#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityPlanet.ModularAssembly.Editor
{
    public static class ModularContentBundleBuilder
    {
        private const string TemporaryRoot = "Assets/__NeoXGeneratedTemp";

        [MenuItem("Tools/Modular Assembly/Build NeoX Windows Bundles")]
        public static void BuildWindowsBundles()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string stagingRoot = Path.Combine(projectRoot, "ModularContent", "Staging");
            string outputRoot = Path.Combine(projectRoot, "ModularContent", "Windows");
            if (!Directory.Exists(stagingRoot))
            {
                throw new DirectoryNotFoundException(
                    "Missing ModularContent/Staging. Run APKExtracted/build_modular_content.py --convert-models first.");
            }

            string temporaryAbsolute = Path.Combine(projectRoot, TemporaryRoot);
            if (!Directory.Exists(temporaryAbsolute))
            {
                Directory.CreateDirectory(temporaryAbsolute);
                CopyStaging(stagingRoot, temporaryAbsolute);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            Debug.Log("NeoX flight Bundle build started.");
            List<AssetBundleBuild> builds = BuildMap();
            Directory.CreateDirectory(outputRoot);
            Directory.CreateDirectory(Path.Combine(outputRoot, "modules"));
            Directory.CreateDirectory(Path.Combine(outputRoot, "props"));
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                outputRoot,
                builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);
            if (manifest == null)
            {
                throw new InvalidOperationException("Unity returned a null AssetBundleManifest.");
            }

            WriteBuildReport(projectRoot, builds);
            AssetDatabase.DeleteAsset(TemporaryRoot);
            AssetDatabase.Refresh();
            Debug.Log($"Built {builds.Count} NeoX bundles to {outputRoot}");
        }

        private static void CopyStaging(string source, string destination)
        {
            foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, destination));
            }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = file.Replace(source, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, true);
            }
        }

        private static List<AssetBundleBuild> BuildMap()
        {
            Dictionary<string, List<string>> assets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> addresses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { TemporaryRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                bool isModel = path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);
                bool isTexture = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                if (!isModel && !isTexture)
                {
                    continue;
                }

                string relative = path.Substring(TemporaryRoot.Length + 1).Replace('\\', '/');
                string[] parts = relative.Split('/');
                string package = parts.Length > 0 ? parts[0].ToLowerInvariant() : "other";
                if (package != "block")
                {
                    continue;
                }
                string kind = package == "block" ? "modules" : "props";
                string bundle = $"{kind}/{package}";
                if (!assets.TryGetValue(bundle, out List<string> list))
                {
                    list = new List<string>();
                    assets[bundle] = list;
                    addresses[bundle] = new List<string>();
                }
                list.Add(path);
                addresses[bundle].Add(relative);
            }

            return assets.Select(pair => new AssetBundleBuild
            {
                assetBundleName = pair.Key,
                assetNames = pair.Value.ToArray(),
                addressableNames = addresses[pair.Key].ToArray()
            }).ToList();
        }


        private static void WriteBuildReport(string projectRoot, IReadOnlyCollection<AssetBundleBuild> builds)
        {
            string reportPath = Path.Combine(projectRoot, "ModularContent", "bundle_build_report.json");
            string json = JsonUtility.ToJson(new BundleBuildReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                bundleCount = builds.Count,
                assetCount = builds.Sum(build => build.assetNames.Length),
                bundles = builds.Select(build => build.assetBundleName).ToArray()
            }, true);
            File.WriteAllText(reportPath, json);
        }

        [Serializable]
        private sealed class BundleBuildReport
        {
            public string generatedUtc;
            public int bundleCount;
            public int assetCount;
            public string[] bundles;
        }
    }
}
#endif
