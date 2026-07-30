using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityPlanet.ModularAssembly.Editor
{
    public static class NeoXWeaponEffectImporter
    {
        [Serializable]
        sealed class Catalog
        {
            public int formatVersion = 1;
            public long generatedUtcTicks;
            public EffectRecord[] effects = Array.Empty<EffectRecord>();
        }

        [Serializable]
        sealed class EffectRecord
        {
            public string sourcePath;
            public string family;
            public string phase;
            public string[] supportedNodes;
            public string[] unsupportedNodes;
            public bool validHeader;
        }

        static readonly string[] WeaponTokens =
        {
            "machinegun", "antiair", "gatlin", "missile",
            "snipercannon", "energy_cannon", "heavy_laser"
        };

        static readonly string[] SupportedTokens =
        {
            "ColorFrame", "ColorFramePar", "EmissionDirDegreeFrame",
            "EmissionRate", "Life", "Size", "Speed", "Gravity",
            "Texture", "Trail", "Light", "Dummy", "Mesh"
        };

        [MenuItem("Tools/Modular Assembly/Build NeoX Weapon Effect Catalog")]
        public static void BuildCatalog()
        {
            string sourceRoot = Path.Combine(
                NeoXExternalPaths.RawRoot,
                "g98_release_out_netease_94_nxpk",
                "sfx");
            if (!Directory.Exists(sourceRoot))
            {
                Debug.LogError(
                    "NeoX SFX source directory was not found: " +
                    sourceRoot);
                return;
            }
            List<EffectRecord> records = Directory
                .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                    path.EndsWith(".sfx", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".nfx", StringComparison.OrdinalIgnoreCase))
                .Where(path => WeaponTokens.Any(token =>
                    path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(path => Parse(path, sourceRoot))
                .OrderBy(record => record.sourcePath, StringComparer.Ordinal)
                .ToList();
            Catalog catalog = new Catalog
            {
                generatedUtcTicks = DateTime.UtcNow.Ticks,
                effects = records.ToArray()
            };
            string outputDirectory = Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "ModularContent");
            Directory.CreateDirectory(outputDirectory);
            string output = Path.Combine(
                outputDirectory,
                "weapon_effect_catalog.json");
            File.WriteAllText(
                output,
                JsonUtility.ToJson(catalog, true),
                new UTF8Encoding(false));
            string report = Path.Combine(
                outputDirectory,
                "weapon_effect_conversion_report.json");
            File.WriteAllText(
                report,
                JsonUtility.ToJson(new ConversionReport
                {
                    sourceCount = records.Count,
                    validHeaderCount = records.Count(item => item.validHeader),
                    fallbackCount = records.Count(item =>
                        item.unsupportedNodes != null &&
                        item.unsupportedNodes.Length > 0)
                }, true),
                new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log(
                "NeoX weapon effect catalog generated: " +
                records.Count + " records.");
        }

        [Serializable]
        sealed class ConversionReport
        {
            public int sourceCount;
            public int validHeaderCount;
            public int fallbackCount;
        }

        static EffectRecord Parse(string path, string root)
        {
            byte[] data = File.ReadAllBytes(path);
            bool valid = data.Length >= 4 &&
                         data[0] == 0xC1 &&
                         data[1] == 0x59 &&
                         data[2] == 0x41 &&
                         data[3] == 0x0D;
            string[] symbols = ExtractAsciiSymbols(data);
            string[] supported = symbols
                .Where(symbol => SupportedTokens.Any(token =>
                    symbol.IndexOf(
                        token,
                        StringComparison.OrdinalIgnoreCase) >= 0))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(64)
                .ToArray();
            string[] unsupported = symbols
                .Where(symbol =>
                    (symbol.EndsWith("Frame", StringComparison.OrdinalIgnoreCase) ||
                     symbol.EndsWith("Par", StringComparison.OrdinalIgnoreCase)) &&
                    !supported.Contains(
                        symbol,
                        StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(64)
                .ToArray();
            string relative = path.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar)
                .Replace('\\', '/');
            return new EffectRecord
            {
                sourcePath = "sfx/" + relative,
                family = Family(relative),
                phase = Phase(relative),
                supportedNodes = supported,
                unsupportedNodes = unsupported,
                validHeader = valid
            };
        }

        static string[] ExtractAsciiSymbols(byte[] data)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            for (int index = 0; index < data.Length; index++)
            {
                byte value = data[index];
                if (value >= 32 && value <= 126)
                {
                    current.Append((char)value);
                    continue;
                }
                if (current.Length >= 3)
                    result.Add(current.ToString());
                current.Length = 0;
            }
            if (current.Length >= 3)
                result.Add(current.ToString());
            return result.ToArray();
        }

        static string Family(string path)
        {
            return WeaponTokens.FirstOrDefault(token =>
                path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? "weapon";
        }

        static string Phase(string path)
        {
            string lower = path.ToLowerInvariant();
            if (lower.Contains("behit") || lower.Contains("_end"))
                return "impact";
            if (lower.Contains("boost") || lower.Contains("trail"))
                return "projectile";
            if (lower.Contains("aim"))
                return "aim";
            if (lower.Contains("open"))
                return "open";
            if (lower.Contains("close"))
                return "close";
            if (lower.Contains("emit"))
                return "muzzle";
            if (lower.Contains("fire") || lower.Contains("shoot"))
                return "fire";
            return "idle";
        }
    }
}
