using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using KDL.Editor;
using UnityEditor;
using UnityEngine;

namespace SpacecraftEditor.Editor
{
    public enum SpacecraftPcgScope
    {
        Hull,
        Module
    }

    [Serializable]
    internal sealed class SpacecraftPcgReport
    {
        public bool success;
        public string pipelineVersion;
        public string upstreamRevision;
        public string compatibilityRevision;
        public string blenderVersion;
        public SpacecraftPcgResult[] results = Array.Empty<SpacecraftPcgResult>();
        public string[] errors = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class SpacecraftPcgResult
    {
        public string kind;
        public string id;
        public string archetype;
        public int seedIndex;
        public string seed;
        public string hullId;
        public Vector3 dimensions;
        public string fbx;
        public string thumbnail;
        public string sha256;
        public string[] errors = Array.Empty<string>();
    }

    public static class SpacecraftPcgPipeline
    {
        public const string BlenderEditorPrefsKey = "SpacecraftPCG.BlenderPath";
        public const string DefaultBlenderPath =
            @"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe";

        const string ManifestRelativePath = "Tools/Blender/SpaceshipPCG/fleet_manifest.json";
        const string ScriptRelativePath = "Tools/Blender/SpaceshipPCG/generate_fleet.py";
        const string GeneratedRoot = "Assets/SpacecraftEditor/Art/Generated";
        const string GeneratedHullRoot = GeneratedRoot + "/Hulls";
        const string GeneratedModuleRoot = GeneratedRoot + "/Modules";
        const string GeneratedThumbnailRoot = GeneratedRoot + "/Thumbnails";
        const string GeneratedPbrRoot = GeneratedRoot + "/PBR";
        const string GeneratedPbrMaterialRoot = GeneratedRoot + "/Materials";
        const string GeneratedHullPrefabRoot = "Assets/SpacecraftEditor/Prefabs/Generated/Hulls";
        const string GeneratedHullDataRoot = "Assets/SpacecraftEditor/Data/Generated/Hulls";
        const string PirateHardpointRoot = "Assets/Resources/Spaceflight/Pirates";

        static readonly string[] Archetypes = { "balanced", "spindle", "saucer" };
        static readonly Dictionary<string, string> CanonicalHullPaths =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "balanced", "Assets/SpacecraftEditor/Data/hull_balanced.asset" },
                { "spindle", "Assets/SpacecraftEditor/Data/hull_spindle.asset" },
                { "saucer", "Assets/SpacecraftEditor/Data/hull_saucer.asset" }
            };

        static readonly Dictionary<string, string> ModulePrefabPaths =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ThrusterSmall", "Assets/SpacecraftEditor/Prefabs/ThrusterSmall.prefab" },
                { "ThrusterMedium", "Assets/SpacecraftEditor/Prefabs/ThrusterMedium.prefab" },
                { "ThrusterLarge", "Assets/SpacecraftEditor/Prefabs/ThrusterLarge.prefab" },
                { "SweptWing", "Assets/SpacecraftEditor/Prefabs/ModularParts/SweptWing.prefab" },
                { "DeltaWing", "Assets/SpacecraftEditor/Prefabs/ModularParts/DeltaWing.prefab" },
                { "Canard", "Assets/SpacecraftEditor/Prefabs/ModularParts/Canard.prefab" },
                { "VerticalFin", "Assets/SpacecraftEditor/Prefabs/ModularParts/VerticalFin.prefab" },
                { "Radiator", "Assets/SpacecraftEditor/Prefabs/ModularParts/Radiator.prefab" },
                { "SensorMast", "Assets/SpacecraftEditor/Prefabs/ModularParts/SensorMast.prefab" },
                { "EngineNacelle", "Assets/SpacecraftEditor/Prefabs/ModularParts/EngineNacelle.prefab" },
                { "ArmorFairing", "Assets/SpacecraftEditor/Prefabs/ModularParts/ArmorFairing.prefab" },
                { "EnergyPulse", "Assets/SpacecraftEditor/Prefabs/ModularParts/EnergyPulse.prefab" },
                { "KineticRepeater", "Assets/SpacecraftEditor/Prefabs/ModularParts/KineticRepeater.prefab" }
            };

        static readonly HashSet<string> ThrusterModules =
            new HashSet<string>(new[] { "ThrusterSmall", "ThrusterMedium", "ThrusterLarge" });
        static readonly HashSet<string> WeaponModules =
            new HashSet<string>(new[] { "EnergyPulse", "KineticRepeater" });

        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ??
            throw new InvalidOperationException("Unity project root could not be resolved.");

        public static string ResolveBlenderPath()
        {
            string configured = EditorPrefs.GetString(BlenderEditorPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;
            return File.Exists(DefaultBlenderPath) ? DefaultBlenderPath : configured;
        }

        public static string GenerateAll(string blenderPath = null)
        {
            return RunGenerator("all", string.Empty, blenderPath, true);
        }

        [MenuItem("Tools/Spacecraft/PCG/Generate All Assets")]
        static void GenerateAllFromMenu()
        {
            GenerateAll();
        }

        public static string GenerateSelectedHull(string archetype, int seedIndex, string blenderPath = null)
        {
            if (!Archetypes.Contains(archetype))
                throw new ArgumentOutOfRangeException(nameof(archetype), archetype, "Unknown PCG archetype.");
            return RunGenerator(
                "hull",
                archetype + "_" + Mathf.Clamp(seedIndex, 0, 7).ToString("00"),
                blenderPath,
                true);
        }

        public static string GenerateSelectedModule(string moduleId, string blenderPath = null)
        {
            if (!ModulePrefabPaths.ContainsKey(moduleId))
                throw new ArgumentOutOfRangeException(nameof(moduleId), moduleId, "Unknown PCG module.");
            return RunGenerator("module", moduleId, blenderPath, true);
        }

        [AICallable(
            "验证已发布的 PCG 船体、模块、LOD、碰撞体和材质绑定。",
            Category = "Spacecraft.PCG",
            Kind = ToolKind.Read)]
        public static string ValidatePublishedAssets()
        {
            var errors = new List<string>();
            ShipHullDefinition[] hulls = LoadPcgHulls();
            if (hulls.Length != 24)
                errors.Add($"Expected 24 PCG hull definitions, found {hulls.Length}.");

            string[] expectedCanonical = { "hull.balanced", "hull.spindle", "hull.saucer" };
            foreach (string id in expectedCanonical)
            {
                if (!hulls.Any(hull => hull != null && hull.HullId == id))
                    errors.Add("Missing canonical hull ID: " + id);
            }

            var duplicateIds = hulls
                .Where(hull => hull != null)
                .GroupBy(hull => hull.HullId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicateIds.Length > 0)
                errors.Add("Duplicate hull IDs: " + string.Join(", ", duplicateIds));

            foreach (ShipHullDefinition hull in hulls)
            {
                if (hull == null)
                    continue;
                if (hull.ModelPrefab == null)
                    errors.Add(hull.HullId + " has no model prefab.");
                if (hull.CollisionMesh == null)
                    errors.Add(hull.HullId + " has no collision mesh.");
                if (hull.Thumbnail == null)
                    errors.Add(hull.HullId + " has no thumbnail.");
                if (hull.FlightProfile == null)
                    errors.Add(hull.HullId + " has no flight profile.");
            }

            foreach (KeyValuePair<string, string> pair in ModulePrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Value);
                if (prefab == null)
                {
                    errors.Add("Missing module prefab: " + pair.Value);
                    continue;
                }
                Transform pcgModel = FindDeepChild(prefab.transform, "PCGModel");
                if (pcgModel == null)
                    errors.Add(pair.Key + " has no PCGModel.");
                Type expected = ThrusterModules.Contains(pair.Key)
                    ? typeof(ThrusterPart)
                    : WeaponModules.Contains(pair.Key)
                        ? typeof(WeaponPart)
                        : typeof(DecorationPart);
                if (prefab.GetComponent(expected) == null)
                    errors.Add(pair.Key + " is missing " + expected.Name + ".");
            }

            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "Spaceship PCG validation failed:\n- " + string.Join("\n- ", errors));

            string message =
                $"Spaceship PCG validation passed: {hulls.Length} hulls and {ModulePrefabPaths.Count} modules.";
            UnityEngine.Debug.Log(message);
            return message;
        }

        [MenuItem("Tools/Spacecraft/PCG/Validate Published Assets")]
        static void ValidateFromMenu()
        {
            ValidatePublishedAssets();
        }

        [MenuItem("Tools/Spacecraft/PCG/Publish Latest Staging Run")]
        static void PublishLatestFromMenu()
        {
            PublishLatestStaging();
        }

        [AICallable(
            "原子发布最近一次成功的 Blender PCG staging 结果并重建 Unity 资产。",
            Category = "Spacecraft.PCG",
            Kind = ToolKind.Write)]
        public static string PublishLatestStaging()
        {
            string root = Path.Combine(ProjectRoot, "Temp", "SpaceshipPCG");
            string reportPath = Directory.Exists(root)
                ? Directory.GetFiles(root, "report.json", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (string.IsNullOrEmpty(reportPath))
                throw new FileNotFoundException("No staged Spaceship PCG report was found under Temp.");
            SpacecraftPcgReport report =
                JsonUtility.FromJson<SpacecraftPcgReport>(File.ReadAllText(reportPath));
            if (report == null || !report.success)
                throw new InvalidOperationException("The newest staged Spaceship PCG run did not succeed.");
            Publish(Path.GetDirectoryName(reportPath), reportPath, report);
            return ValidatePublishedAssets();
        }

        static string RunGenerator(
            string scope,
            string selectedId,
            string blenderPath,
            bool publish)
        {
            blenderPath = string.IsNullOrWhiteSpace(blenderPath)
                ? ResolveBlenderPath()
                : blenderPath;
            if (!File.Exists(blenderPath))
                throw new FileNotFoundException("Blender 5.1 executable was not found.", blenderPath);

            string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
                           Guid.NewGuid().ToString("N").Substring(0, 8);
            string staging = Path.Combine(ProjectRoot, "Temp", "SpaceshipPCG", runId);
            string reportPath = Path.Combine(staging, "report.json");
            Directory.CreateDirectory(staging);

            string manifest = Path.Combine(ProjectRoot, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string script = Path.Combine(ProjectRoot, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var arguments = new StringBuilder();
            arguments.Append("--background --factory-startup --python ")
                .Append(Quote(script))
                .Append(" -- --manifest ").Append(Quote(manifest))
                .Append(" --scope ").Append(scope);
            if (!string.IsNullOrEmpty(selectedId))
                arguments.Append(" --id ").Append(Quote(selectedId));
            arguments.Append(" --staging ").Append(Quote(staging))
                .Append(" --report ").Append(Quote(reportPath));

            var output = new StringBuilder();
            var error = new StringBuilder();
            var startInfo = new ProcessStartInfo
            {
                FileName = blenderPath,
                Arguments = arguments.ToString(),
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            EditorUtility.DisplayProgressBar(
                "Spaceship PCG",
                string.IsNullOrEmpty(selectedId) ? "Generating fleet in Blender..." : "Generating " + selectedId,
                0.2f);
            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (_, args) =>
                    {
                        if (args.Data != null)
                            output.AppendLine(args.Data);
                    };
                    process.ErrorDataReceived += (_, args) =>
                    {
                        if (args.Data != null)
                            error.AppendLine(args.Data);
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            $"Blender PCG exited with code {process.ExitCode}.\n{error}\n{output}");
                    }
                }

                if (!File.Exists(reportPath))
                    throw new InvalidOperationException("Blender completed without producing report.json.");
                SpacecraftPcgReport report =
                    JsonUtility.FromJson<SpacecraftPcgReport>(File.ReadAllText(reportPath));
                if (report == null || !report.success)
                {
                    string reportErrors = report == null
                        ? "Report could not be parsed."
                        : string.Join("\n", report.errors ?? Array.Empty<string>());
                    throw new InvalidOperationException("Blender PCG validation failed:\n" + reportErrors);
                }

                if (publish)
                {
                    EditorUtility.DisplayProgressBar("Spaceship PCG", "Publishing Unity assets...", 0.65f);
                    Publish(staging, reportPath, report);
                    EditorUtility.DisplayProgressBar("Spaceship PCG", "Validating published assets...", 0.9f);
                    ValidatePublishedAssets();
                }

                string message =
                    $"Spaceship PCG generated {report.results.Length} assets with Blender {report.blenderVersion}.";
                UnityEngine.Debug.Log(message + "\n" + output);
                return message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Publish(string staging, string reportPath, SpacecraftPcgReport report)
        {
            EnsureAssetFolder(GeneratedHullRoot);
            EnsureAssetFolder(GeneratedModuleRoot);
            EnsureAssetFolder(GeneratedThumbnailRoot);
            EnsureAssetFolder(GeneratedPbrRoot);
            EnsureAssetFolder(GeneratedPbrMaterialRoot);
            EnsureAssetFolder(GeneratedHullPrefabRoot);
            EnsureAssetFolder(GeneratedHullDataRoot);

            var copies = new List<Tuple<string, string>>();
            foreach (SpacecraftPcgResult result in report.results)
            {
                if (result == null)
                    continue;
                string sourceFbx = Path.Combine(staging, result.fbx.Replace('/', Path.DirectorySeparatorChar));
                string sourceThumbnail =
                    Path.Combine(staging, result.thumbnail.Replace('/', Path.DirectorySeparatorChar));
                string fbxDestination = result.kind == "hull"
                    ? GeneratedHullRoot + "/" + result.id + ".fbx"
                    : GeneratedModuleRoot + "/" + result.id + ".fbx";
                string thumbnailDestination = GeneratedThumbnailRoot + "/" + result.id + ".png";
                copies.Add(Tuple.Create(sourceFbx, AssetPathToAbsolute(fbxDestination)));
                copies.Add(Tuple.Create(sourceThumbnail, AssetPathToAbsolute(thumbnailDestination)));
            }
            string stagedPbrRoot = Path.Combine(staging, "PBR");
            if (!Directory.Exists(stagedPbrRoot))
                throw new InvalidOperationException("Blender run did not produce the required PBR texture set.");
            foreach (string sourceTexture in Directory.GetFiles(stagedPbrRoot, "*.png"))
            {
                copies.Add(Tuple.Create(
                    sourceTexture,
                    AssetPathToAbsolute(GeneratedPbrRoot + "/" + Path.GetFileName(sourceTexture))));
            }
            copies.Add(Tuple.Create(
                reportPath,
                AssetPathToAbsolute(GeneratedRoot + "/pcg_report.json")));
            copies.Add(Tuple.Create(
                Path.Combine(ProjectRoot, ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                AssetPathToAbsolute(GeneratedRoot + "/fleet_manifest.json")));

            CopyWithRollback(copies, () =>
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigurePbrTextureImporters();
                SpacecraftModularPartAssetBuilder.RebuildEightMetalMaterialLibrary();
                foreach (SpacecraftPcgResult result in report.results)
                {
                    string assetPath = result.kind == "hull"
                        ? GeneratedHullRoot + "/" + result.id + ".fbx"
                        : GeneratedModuleRoot + "/" + result.id + ".fbx";
                    ConfigureModelImporter(assetPath);
                    ConfigureThumbnailImporter(GeneratedThumbnailRoot + "/" + result.id + ".png");
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                foreach (SpacecraftPcgResult result in report.results.Where(item => item.kind == "hull"))
                    PublishHull(result);
                foreach (SpacecraftPcgResult result in report.results.Where(item => item.kind == "module"))
                    PublishModule(result);

                RebuildHullCatalogsAndPirateLayouts();
                UpdatePartThumbnails();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            });
        }

        static void PublishHull(SpacecraftPcgResult result)
        {
            string fbxPath = GeneratedHullRoot + "/" + result.id + ".fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null)
                throw new InvalidOperationException("Generated hull FBX could not be loaded: " + fbxPath);
            Mesh collision = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<Mesh>()
                .FirstOrDefault(mesh => mesh.name.Equals("Collision", StringComparison.OrdinalIgnoreCase));
            if (collision == null)
                throw new InvalidOperationException(result.id + " has no Collision mesh.");

            var root = new GameObject(result.id);
            try
            {
                GameObject model = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (model == null)
                    throw new InvalidOperationException("Could not instantiate " + fbxPath);
                PrefabUtility.UnpackPrefabInstance(
                    model,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                model.name = "PCGModel";
                model.transform.SetParent(root.transform, false);
                ConfigureVisualModel(model, false);

                string prefabPath = GeneratedHullPrefabRoot + "/" + result.id + ".prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Sprite thumbnail =
                    AssetDatabase.LoadAssetAtPath<Sprite>(GeneratedThumbnailRoot + "/" + result.id + ".png");
                UpsertHullDefinition(result, prefab, collision, thumbnail);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void UpsertHullDefinition(
            SpacecraftPcgResult result,
            GameObject modelPrefab,
            Mesh collision,
            Sprite thumbnail)
        {
            string archetype = result.archetype;
            ShipHullDefinition canonical =
                AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(CanonicalHullPaths[archetype]);
            if (canonical == null)
                throw new InvalidOperationException("Missing canonical hull definition for " + archetype);

            ShipHullDefinition target;
            if (result.seedIndex == 0)
            {
                target = canonical;
            }
            else
            {
                string path = GeneratedHullDataRoot + "/" + result.id + ".asset";
                target = AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(path);
                if (target == null)
                {
                    target = UnityEngine.Object.Instantiate(canonical);
                    target.name = result.id;
                    AssetDatabase.CreateAsset(target, path);
                }
            }

            var serialized = new SerializedObject(target);
            SetString(serialized, "hullId", result.hullId);
            SetString(
                serialized,
                "displayName",
                ArchetypeDisplayName(archetype) + " PCG " + (result.seedIndex + 1).ToString("00"));
            SetString(
                serialized,
                "description",
                $"由 SpaceshipGenerator 种子 {result.seed} 离线生成；性能继承 {archetype} 原型。");
            SetReference(serialized, "modelPrefab", modelPrefab);
            SetReference(serialized, "thumbnail", thumbnail);
            SetReference(serialized, "collisionMesh", collision);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        static void PublishModule(SpacecraftPcgResult result)
        {
            if (!ModulePrefabPaths.TryGetValue(result.id, out string prefabPath))
                return;
            string fbxPath = GeneratedModuleRoot + "/" + result.id + ".fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (source == null)
                throw new InvalidOperationException("Generated module FBX could not be loaded: " + fbxPath);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true)
                             .Where(value => value != root.transform &&
                                             (value.name == "PCGModel" || value.name == "Model"))
                             .OrderByDescending(value => Depth(value))
                             .ToArray())
                {
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
                }

                if (ThrusterModules.Contains(result.id))
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(renderer is ParticleSystemRenderer) &&
                            !renderer.transform.IsChildOf(FindDeepChild(root.transform, "ExhaustFX")))
                            renderer.enabled = false;
                    }
                }

                Transform parent = WeaponModules.Contains(result.id)
                    ? FindDeepChild(root.transform, "GimbalPivot") ?? root.transform
                    : root.transform;
                GameObject model = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (model == null)
                    throw new InvalidOperationException("Could not instantiate " + fbxPath);
                PrefabUtility.UnpackPrefabInstance(
                    model,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                model.name = "PCGModel";
                model.transform.SetParent(parent, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = ThrusterModules.Contains(result.id)
                    ? Quaternion.identity
                    : Quaternion.Euler(90f, 0f, 0f);
                model.transform.localScale = Vector3.one;
                ConfigureVisualModel(model, true);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void ConfigureVisualModel(GameObject model, bool module)
        {
            Material[] palette = LoadOrCreatePbrPalette();

            var lodRenderers = new Dictionary<int, List<Renderer>>
            {
                { 0, new List<Renderer>() },
                { 1, new List<Renderer>() },
                { 2, new List<Renderer>() }
            };
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                Transform node = renderer.transform;
                if (node.name.IndexOf("Collision", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    renderer.enabled = false;
                    node.gameObject.SetActive(false);
                    continue;
                }
                int lod = node.name.IndexOf("LOD1", StringComparison.OrdinalIgnoreCase) >= 0
                    ? 1
                    : node.name.IndexOf("LOD2", StringComparison.OrdinalIgnoreCase) >= 0
                        ? 2
                        : 0;
                lodRenderers[lod].Add(renderer);
                Material[] assigned = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int index = 0; index < assigned.Length; index++)
                    assigned[index] = palette[Mathf.Min(index, palette.Length - 1)];
                renderer.sharedMaterials = assigned;
            }

            LODGroup group = model.GetComponent<LODGroup>() ?? model.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.62f, lodRenderers[0].ToArray()),
                new LOD(0.24f, lodRenderers[1].ToArray()),
                new LOD(0.04f, lodRenderers[2].ToArray())
            });
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = false;
            group.RecalculateBounds();
        }

        static Material[] LoadOrCreatePbrPalette()
        {
            Texture2D baseColor = LoadPbrTexture("BaseColor");
            Texture2D metallicSmoothness = LoadPbrTexture("MetallicSmoothness");
            Texture2D normal = LoadPbrTexture("Normal");
            Texture2D ao = LoadPbrTexture("AO");
            Texture2D emission = LoadPbrTexture("Emission");
            if (baseColor == null || metallicSmoothness == null || normal == null || ao == null)
                throw new InvalidOperationException("The generated industrial PBR texture set is incomplete.");

            return new[]
            {
                UpsertPbrMaterial(
                    "HullPrimary",
                    new Color(0.16f, 0.29f, 0.34f),
                    baseColor,
                    metallicSmoothness,
                    normal,
                    ao,
                    null,
                    Color.black),
                UpsertPbrMaterial(
                    "HullDark",
                    new Color(0.025f, 0.04f, 0.05f),
                    baseColor,
                    metallicSmoothness,
                    normal,
                    ao,
                    null,
                    Color.black),
                UpsertPbrMaterial(
                    "HullEmission",
                    new Color(0.02f, 0.42f, 0.72f),
                    baseColor,
                    metallicSmoothness,
                    normal,
                    ao,
                    emission,
                    new Color(0.02f, 0.72f, 1f) * 5f),
                UpsertPbrMaterial(
                    "Accent",
                    new Color(0.38f, 0.42f, 0.44f),
                    baseColor,
                    metallicSmoothness,
                    normal,
                    ao,
                    null,
                    Color.black)
            };
        }

        static Texture2D LoadPbrTexture(string suffix)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(
                GeneratedPbrRoot + "/IndustrialHull_" + suffix + ".png");
        }

        static Material UpsertPbrMaterial(
            string name,
            Color tint,
            Texture2D baseColor,
            Texture2D metallicSmoothness,
            Texture2D normal,
            Texture2D ao,
            Texture2D emission,
            Color emissionColor)
        {
            string path = GeneratedPbrMaterialRoot + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    throw new InvalidOperationException("Built-in Standard shader was not found.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_Color", tint);
            material.SetTexture("_MainTex", baseColor);
            material.SetTexture("_MetallicGlossMap", metallicSmoothness);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_GlossMapScale", 1f);
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 0.55f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_OcclusionMap", ao);
            material.SetFloat("_OcclusionStrength", 1f);
            if (emission != null)
            {
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", emissionColor);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                material.SetTexture("_EmissionMap", null);
                material.SetColor("_EmissionColor", Color.black);
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static void ConfigurePbrTextureImporters()
        {
            foreach (string path in AssetDatabase.FindAssets(
                         "IndustrialHull_ t:Texture2D",
                         new[] { GeneratedPbrRoot })
                     .Select(AssetDatabase.GUIDToAssetPath))
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool normal = name.EndsWith("_Normal", StringComparison.Ordinal);
                bool color = name.EndsWith("_BaseColor", StringComparison.Ordinal) ||
                             name.EndsWith("_Emission", StringComparison.Ordinal);
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = color;
                importer.alphaSource = name.EndsWith(
                    "_MetallicSmoothness",
                    StringComparison.Ordinal)
                    ? TextureImporterAlphaSource.FromInput
                    : TextureImporterAlphaSource.None;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 8;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        static void RebuildHullCatalogsAndPirateLayouts()
        {
            // PCG generation remains available for experimentation and review, but the
            // active runtime catalog is now owned exclusively by External Fleet V1.
            // Never reactivate the archived 24-hull catalog from this legacy publisher.
            UnityEngine.Debug.Log(
                "PCG hulls were generated for review only; active catalogs remain on External Fleet V1.");
        }

        static SpacecraftHardpointLayout UpsertHardpointLayout(ShipHullDefinition hull)
        {
            string safeId = hull.HullId.Replace('.', '_');
            string path = PirateHardpointRoot + "/Hardpoints_" + safeId + ".asset";
            SpacecraftHardpointLayout layout =
                AssetDatabase.LoadAssetAtPath<SpacecraftHardpointLayout>(path);
            if (layout == null)
            {
                string archetype = ResolveArchetype(hull.HullId);
                string canonicalPath =
                    PirateHardpointRoot + "/Hardpoints_hull_" + archetype + ".asset";
                SpacecraftHardpointLayout canonical =
                    AssetDatabase.LoadAssetAtPath<SpacecraftHardpointLayout>(canonicalPath);
                if (canonical == null)
                    throw new InvalidOperationException("Missing canonical hardpoint layout: " + canonicalPath);
                layout = UnityEngine.Object.Instantiate(canonical);
                layout.name = "Hardpoints_" + safeId;
                AssetDatabase.CreateAsset(layout, path);
                layout.Configure(
                    hull.HullId,
                    canonical.Hardpoints,
                    canonical.DecorationRegions,
                    canonical.MinimumForwardAcceleration,
                    canonical.MaximumLateralMassImbalance);
            }
            else if (layout.HullId != hull.HullId)
            {
                layout.Configure(
                    hull.HullId,
                    layout.Hardpoints,
                    layout.DecorationRegions,
                    layout.MinimumForwardAcceleration,
                    layout.MaximumLateralMassImbalance);
            }
            EditorUtility.SetDirty(layout);
            return layout;
        }

        static ShipHullDefinition[] LoadPcgHulls()
        {
            var hulls = new List<ShipHullDefinition>(24);
            foreach (string archetype in Archetypes)
            {
                ShipHullDefinition canonical =
                    AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(CanonicalHullPaths[archetype]);
                if (canonical != null)
                    hulls.Add(canonical);
                for (int seed = 1; seed < 8; seed++)
                {
                    string path =
                        GeneratedHullDataRoot + "/" + archetype + "_" + seed.ToString("00") + ".asset";
                    ShipHullDefinition variant =
                        AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(path);
                    if (variant != null)
                        hulls.Add(variant);
                }
            }
            return hulls.ToArray();
        }

        static void UpdatePartThumbnails()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:ShipPartDefinition",
                new[] { "Assets/SpacecraftEditor/Data" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ShipPartDefinition definition =
                    AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(path);
                if (definition == null)
                    continue;
                string moduleId = ModuleIdForPart(definition.PartId);
                if (string.IsNullOrEmpty(moduleId))
                    continue;
                Sprite sprite =
                    AssetDatabase.LoadAssetAtPath<Sprite>(
                        GeneratedThumbnailRoot + "/" + moduleId + ".png");
                if (sprite == null)
                    continue;
                var serialized = new SerializedObject(definition);
                SetReference(serialized, "thumbnail", sprite);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(definition);
            }
        }

        static string ModuleIdForPart(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return string.Empty;
            if (partId == "thruster.small") return "ThrusterSmall";
            if (partId == "thruster.medium") return "ThrusterMedium";
            if (partId == "thruster.large") return "ThrusterLarge";
            if (partId.StartsWith("weapon.energy_pulse", StringComparison.Ordinal)) return "EnergyPulse";
            if (partId.StartsWith("weapon.kinetic_repeater", StringComparison.Ordinal)) return "KineticRepeater";
            string[] pieces = partId.Split('.');
            if (pieces.Length < 2)
                return string.Empty;
            return pieces[1] switch
            {
                "swept_wing" => "SweptWing",
                "delta_wing" => "DeltaWing",
                "canard" => "Canard",
                "vertical_fin" => "VerticalFin",
                "radiator" => "Radiator",
                "sensor_mast" => "SensorMast",
                "engine_nacelle" => "EngineNacelle",
                "armor_fairing" => "ArmorFairing",
                _ => string.Empty
            };
        }

        static void ConfigureModelImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                return;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        static void ConfigureThumbnailImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        static void CopyWithRollback(
            IReadOnlyList<Tuple<string, string>> copies,
            Action afterCopy)
        {
            string backupRoot = Path.Combine(
                ProjectRoot,
                "Temp",
                "SpaceshipPCG",
                "backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupRoot);
            var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var created = new List<string>();
            try
            {
                for (int index = 0; index < copies.Count; index++)
                {
                    string source = copies[index].Item1;
                    string destination = copies[index].Item2;
                    if (!File.Exists(source))
                        throw new FileNotFoundException("PCG output is missing.", source);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ProjectRoot);
                    if (File.Exists(destination))
                    {
                        string backup = Path.Combine(backupRoot, index.ToString("0000") + Path.GetExtension(destination));
                        File.Copy(destination, backup, true);
                        existing[destination] = backup;
                    }
                    else
                    {
                        created.Add(destination);
                    }
                    File.Copy(source, destination, true);
                }
                afterCopy();
            }
            catch
            {
                foreach (string path in created)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                foreach (KeyValuePair<string, string> pair in existing)
                    File.Copy(pair.Value, pair.Key, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }

        static void EnsureAssetFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        static string AssetPathToAbsolute(string path)
        {
            return Path.Combine(ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar));
        }

        static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        static int Depth(Transform transform)
        {
            int depth = 0;
            while (transform.parent != null)
            {
                depth++;
                transform = transform.parent;
            }
            return depth;
        }

        static Transform FindDeepChild(Transform root, string name)
        {
            if (root == null)
                return null;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return transform;
            }
            return null;
        }

        static void SetString(SerializedObject serialized, string name, string value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null)
                property.stringValue = value ?? string.Empty;
        }

        static void SetReference(SerializedObject serialized, string name, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property != null)
                property.objectReferenceValue = value;
        }

        static string ArchetypeDisplayName(string archetype)
        {
            return archetype == "spindle" ? "侦察型" : archetype == "saucer" ? "重载型" : "均衡型";
        }

        static string ResolveArchetype(string hullId)
        {
            if (hullId.IndexOf("spindle", StringComparison.OrdinalIgnoreCase) >= 0)
                return "spindle";
            if (hullId.IndexOf("saucer", StringComparison.OrdinalIgnoreCase) >= 0)
                return "saucer";
            return "balanced";
        }
    }
}
