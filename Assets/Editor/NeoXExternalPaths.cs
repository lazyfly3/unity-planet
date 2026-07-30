#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace UnityPlanet.ModularAssembly.Editor
{
    public static class NeoXExternalPaths
    {
        private const string EnvironmentVariable = "UNITY_PLANET_NEOX_RESEARCH_ROOT";

        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string ResearchRoot
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
                return !string.IsNullOrWhiteSpace(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(Path.Combine(ProjectRoot, "..", "_research", "NeoX"));
            }
        }

        public static string RawRoot => Path.Combine(ResearchRoot, "APKExtracted");

        public static string ConversionRoot => Path.Combine(ResearchRoot, "ModularContent");

        public static string StagingRoot => Path.Combine(ConversionRoot, "Staging");

        public static string SourceCatalogPath =>
            Path.Combine(ConversionRoot, "SourceCatalog", "modular_content_catalog.json");

        public static string RuntimeCatalogPath =>
            Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "ModularContent",
                "modular_content_catalog.json");

        public static string RuntimeBundleOutputRoot =>
            Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "ModularContent",
                "Windows");

        public static string DescribeConfiguration()
        {
            return
                $"NeoX research root: {ResearchRoot}\n" +
                $"Override with {EnvironmentVariable}. Runtime builds do not require this directory.";
        }
    }
}
#endif
