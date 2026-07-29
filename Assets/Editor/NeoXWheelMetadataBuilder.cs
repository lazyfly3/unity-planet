using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityPlanet.ModularAssembly;

public static class NeoXWheelMetadataBuilder
{
    private const string SourceCatalog =
        "ModularContent/SourceCatalog/modular_content_catalog.json";
    private const string RuntimeCatalog =
        "Assets/StreamingAssets/ModularContent/modular_content_catalog.json";

    [MenuItem("Tools/Modular Assembly/Refresh NeoX Wheel Metadata")]
    public static void Refresh()
    {
        string projectRoot =
            Directory.GetParent(Application.dataPath).FullName;
        string sourcePath = Path.Combine(projectRoot, SourceCatalog);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(
                "NeoX content catalog is missing.",
                sourcePath);

        ModularContentCatalogData data =
            JsonUtility.FromJson<ModularContentCatalogData>(
                File.ReadAllText(sourcePath));
        int updated = 0;
        foreach (ModularContentRecord record in
                 data.items ?? Array.Empty<ModularContentRecord>())
        {
            if (record == null ||
                !WheelModuleProfile.IsWheelModuleId(record.neoXId))
            {
                continue;
            }
            AirBuildCatalog.ApplyDefaults(record);
            string gisPath = Path.Combine(
                data.sourceRoot ?? string.Empty,
                record.animationDataPath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            if (File.Exists(gisPath))
                ReadGisMetadata(record, File.ReadAllBytes(gisPath));
            else
                record.animationDecodeStatus =
                    "missing-gis+procedural";
            updated++;
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(sourcePath, json);
        string runtimePath = Path.Combine(projectRoot, RuntimeCatalog);
        Directory.CreateDirectory(Path.GetDirectoryName(runtimePath));
        File.WriteAllText(runtimePath, json);
        AssetDatabase.Refresh();
        Debug.Log(
            $"NeoX wheel metadata refreshed for {updated} records.");
    }

    private static void ReadGisMetadata(
        ModularContentRecord record,
        byte[] bytes)
    {
        string raw = System.Text.Encoding.ASCII.GetString(bytes);
        var bones = new List<string>();
        foreach (string value in new[] { "biped", "wheel", "wheel2" })
        {
            if (raw.IndexOf(
                    value,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bones.Add(value);
            }
        }
        var clips = new List<string>();
        foreach (string value in new[]
                 {
                     "idle",
                     "wheel_rotate",
                     "wheel_rotate_back",
                     "wheel_rotate_backward"
                 })
        {
            if (raw.IndexOf(
                    value,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                clips.Add(value);
            }
        }
        record.animationBones = bones.Distinct().ToArray();
        record.animationClips = clips.Distinct().ToArray();
        record.animationDecodeStatus =
            clips.Count > 0
                ? "metadata+procedural"
                : "unknown-gis+procedural";
    }
}
