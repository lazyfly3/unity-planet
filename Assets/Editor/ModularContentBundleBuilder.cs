#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;

namespace UnityPlanet.ModularAssembly.Editor
{
    public static class ModularContentBundleBuilder
    {
        private const string TemporaryRoot = "Assets/__NeoXGeneratedTemp";
        private const string BuildRequestFile = ".build-curated-neox-runtime";

        [InitializeOnLoadMethod]
        private static void QueueRequestedBuild()
        {
            EditorApplication.delayCall += TryBuildRequested;
        }

        private static void TryBuildRequested()
        {
            string requestPath = Path.Combine(NeoXExternalPaths.ProjectRoot, BuildRequestFile);
            if (!File.Exists(requestPath))
            {
                return;
            }

            try
            {
                BuildWindowsBundles();
                File.Delete(requestPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/Modular Assembly/Build Curated NeoX Runtime Bundles")]
        public static void BuildWindowsBundles()
        {
            string stagingRoot = NeoXExternalPaths.StagingRoot;
            string outputRoot = NeoXExternalPaths.RuntimeBundleOutputRoot;
            string catalogPath = NeoXExternalPaths.RuntimeCatalogPath;
            if (!Directory.Exists(stagingRoot))
            {
                throw new DirectoryNotFoundException(
                    "NeoX conversion staging is outside the Unity project and was not found.\n" +
                    NeoXExternalPaths.DescribeConfiguration());
            }
            if (!File.Exists(catalogPath))
            {
                throw new FileNotFoundException("Runtime NeoX catalog is missing.", catalogPath);
            }

            string temporaryAbsolute = Path.Combine(
                NeoXExternalPaths.ProjectRoot,
                TemporaryRoot.Replace('/', Path.DirectorySeparatorChar));
            if (AssetDatabase.IsValidFolder(TemporaryRoot))
            {
                AssetDatabase.DeleteAsset(TemporaryRoot);
            }
            Directory.CreateDirectory(temporaryAbsolute);

            CuratedCopyResult copyResult = CopyCuratedStaging(
                stagingRoot,
                temporaryAbsolute,
                catalogPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            try
            {
                List<AssetBundleBuild> builds = BuildMap();
                if (builds.Count == 0)
                {
                    throw new InvalidOperationException("No curated NeoX assets were selected.");
                }

                if (Directory.Exists(outputRoot))
                {
                    Directory.Delete(outputRoot, true);
                }
                Directory.CreateDirectory(outputRoot);

                Debug.Log(
                    $"Curated NeoX Bundle build started: " +
                    $"{copyResult.SelectedModules} modules, {copyResult.CopiedAssets} source assets.");
                AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                    outputRoot,
                    builds.ToArray(),
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    BuildTarget.StandaloneWindows64);
                if (manifest == null)
                {
                    throw new InvalidOperationException("Unity returned a null AssetBundleManifest.");
                }

                WriteBuildReport(builds, copyResult);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"Built {builds.Count} curated NeoX runtime bundles to {outputRoot}");
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(TemporaryRoot))
                {
                    AssetDatabase.DeleteAsset(TemporaryRoot);
                }
                AssetDatabase.Refresh();
            }
        }

        private static CuratedCopyResult CopyCuratedStaging(
            string source,
            string destination,
            string catalogPath)
        {
            ModularContentCatalogData catalog =
                JsonUtility.FromJson<ModularContentCatalogData>(File.ReadAllText(catalogPath));
            ModularContentRecord[] records = catalog?.items ?? Array.Empty<ModularContentRecord>();
            Dictionary<string, ModularContentRecord> bySourceId = records
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.sourceId))
                .GroupBy(record => record.sourceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            ModularContentRecord[] selected = records
                .Where(record =>
                    record != null &&
                    record.selectableForAirBuild &&
                    string.Equals(record.contentKind, "module", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            HashSet<string> relativeAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModularContentRecord record in selected)
            {
                CollectRecordAssets(record, bySourceId, visited, relativeAssets);
            }

            int copied = 0;
            List<string> missingModels = new List<string>();
            foreach (string relative in relativeAssets.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                string normalized = NormalizeAssetAddress(relative);
                string inputPath = Path.Combine(
                    source,
                    normalized.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(inputPath))
                {
                    if (normalized.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                    {
                        missingModels.Add(normalized);
                    }
                    continue;
                }

                string outputPath = Path.Combine(
                    destination,
                    normalized.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? destination);
                File.Copy(inputPath, outputPath, true);
                copied++;
            }

            if (missingModels.Count > 0)
            {
                throw new FileNotFoundException(
                    "Curated NeoX models are missing from external staging:\n" +
                    string.Join("\n", missingModels));
            }

            return new CuratedCopyResult
            {
                SelectedModules = selected.Length,
                CopiedAssets = copied
            };
        }

        private static void CollectRecordAssets(
            ModularContentRecord record,
            IReadOnlyDictionary<string, ModularContentRecord> bySourceId,
            ISet<string> visited,
            ISet<string> assets)
        {
            if (record == null || !visited.Add(record.sourceId ?? record.neoXId ?? Guid.NewGuid().ToString()))
            {
                return;
            }

            AddAddress(assets, record.assetAddress);
            if (string.IsNullOrWhiteSpace(record.assetAddress) &&
                !string.IsNullOrWhiteSpace(record.sourcePath))
            {
                AddAddress(
                    assets,
                    record.sourcePath.EndsWith(".mesh", StringComparison.OrdinalIgnoreCase)
                        ? record.sourcePath + ".obj"
                        : record.sourcePath);
            }

            string modelAddress = NormalizeAssetAddress(record.assetAddress);
            if (modelAddress.EndsWith(".mesh.obj", StringComparison.OrdinalIgnoreCase))
            {
                string stem = modelAddress.Substring(0, modelAddress.Length - ".mesh.obj".Length);
                AddAddress(assets, stem + "_d.png");
                AddAddress(assets, stem + "_n.png");
                AddAddress(assets, stem + "_m.png");
                AddAddress(assets, stem + "_d_dzt.png");
            }

            ModularContentRelatedFiles related = record.related;
            foreach (string texture in related?.textures ?? Array.Empty<string>())
            {
                AddAddress(assets, texture);
            }
            foreach (ModularContentMaterialBinding binding in
                     related?.materialBindings ?? Array.Empty<ModularContentMaterialBinding>())
            {
                AddAddress(assets, binding?.albedo);
                AddAddress(assets, binding?.normal);
                AddAddress(assets, binding?.metallic);
                AddAddress(assets, binding?.emission);
            }

            foreach (string sourceId in record.lodSourceIds ?? Array.Empty<string>())
            {
                if (bySourceId.TryGetValue(sourceId, out ModularContentRecord child))
                {
                    CollectRecordAssets(child, bySourceId, visited, assets);
                }
            }
        }

        private static void AddAddress(ISet<string> assets, string address)
        {
            string normalized = NormalizeAssetAddress(address);
            if (!string.IsNullOrWhiteSpace(normalized) && normalized.Contains("/"))
            {
                assets.Add(normalized);
            }
        }

        private static string NormalizeAssetAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return string.Empty;
            }

            string normalized = address.Replace('\\', '/').TrimStart('/');
            if (normalized.EndsWith(".ktx", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 4) + ".png";
            }
            return normalized;
        }

        private static List<AssetBundleBuild> BuildMap()
        {
            List<string> assets = new List<string>();
            List<string> addresses = new List<string>();
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
                if (!relative.StartsWith("block/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                assets.Add(path);
                addresses.Add(relative);
            }

            return assets.Count == 0
                ? new List<AssetBundleBuild>()
                : new List<AssetBundleBuild>
                {
                    new AssetBundleBuild
                    {
                        assetBundleName = "modules/block",
                        assetNames = assets.ToArray(),
                        addressableNames = addresses.ToArray()
                    }
                };
        }

        private static void WriteBuildReport(
            IReadOnlyCollection<AssetBundleBuild> builds,
            CuratedCopyResult copyResult)
        {
            string reportPath = Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "ModularContent",
                "bundle_build_report.json");
            string json = JsonUtility.ToJson(new BundleBuildReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                bundleCount = builds.Count,
                assetCount = builds.Sum(build => build.assetNames.Length),
                selectedModuleCount = copyResult.SelectedModules,
                copiedSourceAssetCount = copyResult.CopiedAssets,
                bundles = builds.Select(build => build.assetBundleName).ToArray()
            }, true);
            File.WriteAllText(reportPath, json);
        }

        private sealed class CuratedCopyResult
        {
            public int SelectedModules;
            public int CopiedAssets;
        }

        [Serializable]
        private sealed class BundleBuildReport
        {
            public string generatedUtc;
            public int bundleCount;
            public int assetCount;
            public int selectedModuleCount;
            public int copiedSourceAssetCount;
            public string[] bundles;
        }
    }
}
#endif
