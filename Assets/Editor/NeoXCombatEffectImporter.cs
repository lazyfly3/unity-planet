using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityPlanet.ModularAssembly.Editor
{
    public static class NeoXCombatEffectImporter
    {
        private const string FinalRoot = "Assets/Resources/CombatFeedback/NeoX";
        private const string StagingRoot = "Assets/Resources/CombatFeedback/_NeoX_Staging";
        private const string BackupRoot = "Assets/Resources/CombatFeedback/_NeoX_Backup";
        private const string CatalogName = "NeoXCombatEffectCatalog.asset";

        [Serializable]
        private sealed class RecipeDocument
        {
            public int formatVersion;
            public string generatedUtc;
            public Recipe[] recipes;
            public ToolRecord vgmstream;
            public AudioInventory audioInventory;
        }

        [Serializable]
        private sealed class ToolRecord
        {
            public string version;
            public string path;
            public bool available;
            public string archiveSha256;
        }

        [Serializable]
        private sealed class AudioInventory
        {
            public int bnk;
            public int pck;
            public int xml;
            public int txt;
            public string mappingStatus;
            public string reason;
        }

        [Serializable]
        private sealed class Recipe
        {
            public string id;
            public string source;
            public string sourcePath;
            public bool sourceResolved;
            public string family;
            public string stage;
            public string kind;
            public string colorHex;
            public float duration;
            public float scale;
            public string convertedTexture;
            public string convertedAudio;
            public string[] dependencies;
            public string[] missingDependencies;
            public string[] supportedNodes;
            public string[] degradedNodes;
            public bool degraded;
            public string fallbackReason;
        }

        [Serializable]
        private sealed class ConversionReport
        {
            public int formatVersion = 1;
            public string generatedUtc;
            public bool published;
            public int recipeCount;
            public int succeeded;
            public int degraded;
            public string[] validationErrors;
            public NeoXEffectConversionRecord[] records;
        }

        private sealed class BuildContext
        {
            public Texture2D SoftTexture;
            public Mesh ShardMesh;
            public readonly List<NeoXCombatEffectCatalogEntry> CatalogEntries = new();
            public readonly List<NeoXEffectConversionRecord> Records = new();
            public readonly List<string> Errors = new();
        }

        [MenuItem("Tools/Modular Assembly/Build NeoX Combat Feedback")]
        public static void Build()
        {
            string recipePath = Path.Combine(
                NeoXExternalPaths.RawRoot,
                "NeoXAnalysis",
                "neox_combat_effect_recipes.json");
            string reportPath = Path.Combine(
                NeoXExternalPaths.RawRoot,
                "NeoXAnalysis",
                "combat_effect_conversion_report.json");

            if (!File.Exists(recipePath))
            {
                throw new FileNotFoundException(
                    "Generate the external NeoX combat recipes first.\n" +
                    NeoXExternalPaths.DescribeConfiguration(),
                    recipePath);
            }

            RecipeDocument document = JsonUtility.FromJson<RecipeDocument>(
                File.ReadAllText(recipePath));
            if (document == null || document.recipes == null || document.recipes.Length == 0)
            {
                throw new InvalidDataException("NeoX combat effect recipe file is empty.");
            }

            BuildContext context = new BuildContext();
            PrepareStaging();
            try
            {
                context.SoftTexture = CreateSoftTexture();
                context.ShardMesh = CreateShardMesh();
                foreach (Recipe recipe in document.recipes)
                {
                    BuildRecipe(recipe, context);
                }

                CreateCatalog(context);
                ValidateStaging(context);
                if (context.Errors.Count == 0)
                {
                    PublishStaging(context);
                }
            }
            catch (Exception exception)
            {
                context.Errors.Add(exception.ToString());
                Debug.LogException(exception);
            }
            finally
            {
                WriteReport(reportPath, document, context);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            if (context.Errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "NeoX combat effect conversion failed:\n" +
                    string.Join("\n", context.Errors));
            }

            Debug.Log(
                $"NeoX combat feedback published: {context.CatalogEntries.Count} effects, " +
                $"{context.Records.Count(record => record.degraded)} degraded.");
        }

        private static void PrepareStaging()
        {
            EnsureAssetFolder("Assets/Resources");
            EnsureAssetFolder("Assets/Resources/CombatFeedback");
            if (AssetDatabase.IsValidFolder(StagingRoot))
            {
                AssetDatabase.DeleteAsset(StagingRoot);
            }

            Directory.CreateDirectory(ToAbsolute(StagingRoot));
            Directory.CreateDirectory(ToAbsolute(StagingRoot + "/Textures"));
            Directory.CreateDirectory(ToAbsolute(StagingRoot + "/Materials"));
            Directory.CreateDirectory(ToAbsolute(StagingRoot + "/Meshes"));
            Directory.CreateDirectory(ToAbsolute(StagingRoot + "/Prefabs"));
            Directory.CreateDirectory(ToAbsolute(StagingRoot + "/Audio"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static Texture2D CreateSoftTexture()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "NeoX_SoftParticle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.6f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            string path = StagingRoot + "/Textures/NeoX_SoftParticle.asset";
            AssetDatabase.CreateAsset(texture, path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Mesh CreateShardMesh()
        {
            Mesh mesh = new Mesh { name = "NeoX_IrregularShard" };
            mesh.vertices = new[]
            {
                new Vector3(-0.42f, -0.18f, -0.12f),
                new Vector3(0.48f, -0.11f, -0.18f),
                new Vector3(0.08f, 0.55f, 0.06f),
                new Vector3(-0.05f, 0.02f, 0.52f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1,
                0, 1, 3,
                1, 2, 3,
                2, 0, 3
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string path = StagingRoot + "/Meshes/NeoX_IrregularShard.asset";
            AssetDatabase.CreateAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        private static void BuildRecipe(Recipe recipe, BuildContext context)
        {
            NeoXEffectConversionRecord record = new NeoXEffectConversionRecord
            {
                sourceId = recipe.id,
                sourceFile = recipe.sourcePath,
                dependencies = recipe.dependencies ?? Array.Empty<string>(),
                supportedNodes = recipe.supportedNodes ?? Array.Empty<string>(),
                degradedNodes = recipe.degradedNodes ?? Array.Empty<string>(),
                degraded = recipe.degraded
            };
            context.Records.Add(record);

            try
            {
                Color color = ParseColor(recipe.colorHex, Color.white);
                Texture2D texture = ImportConvertedTexture(recipe, context.SoftTexture);
                Material primary = CreateParticleMaterial(recipe.id, color, texture, false);
                Material smoke = CreateParticleMaterial(
                    recipe.id + "_smoke",
                    new Color(0.25f, 0.29f, 0.34f, 0.72f),
                    context.SoftTexture,
                    true);
                record.outputMaterial = AssetDatabase.GetAssetPath(primary);
                record.outputTexture = AssetDatabase.GetAssetPath(texture);

                GameObject root = new GameObject("NeoX_" + Sanitize(recipe.id));
                NeoXCombatEffectMarker marker = root.AddComponent<NeoXCombatEffectMarker>();
                marker.expectedLifetime = Mathf.Max(0.05f, recipe.duration);
                marker.localEmissionAxis = Vector3.forward;
                marker.usesWorldSimulation = true;

                BuildEffectGraph(root.transform, recipe, color, primary, smoke, context.ShardMesh);
                string prefabPath = StagingRoot + "/Prefabs/" + root.name + ".prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);
                if (prefab == null)
                {
                    throw new InvalidOperationException("Prefab save returned null: " + prefabPath);
                }

                AudioClip audio = ImportAudio(recipe);
                record.outputAudio = audio != null ? AssetDatabase.GetAssetPath(audio) : string.Empty;
                record.outputPrefab = prefabPath;
                record.succeeded = true;
                record.failureReason = recipe.fallbackReason;

                context.CatalogEntries.Add(
                    new NeoXCombatEffectCatalogEntry
                    {
                        sourceId = recipe.id,
                        sourcePath = recipe.sourcePath,
                        family = recipe.family,
                        stage = ParseStage(recipe.stage),
                        prefab = prefab,
                        audioClip = audio,
                        degraded = recipe.degraded,
                        fallbackReason = recipe.fallbackReason
                    });
            }
            catch (Exception exception)
            {
                record.succeeded = false;
                record.failureReason = exception.Message;
                context.Errors.Add($"{recipe.id}: {exception.Message}");
            }
        }

        private static Texture2D ImportConvertedTexture(Recipe recipe, Texture2D fallback)
        {
            if (string.IsNullOrWhiteSpace(recipe.convertedTexture) ||
                !File.Exists(recipe.convertedTexture))
            {
                return fallback;
            }

            string extension = Path.GetExtension(recipe.convertedTexture);
            string assetPath =
                StagingRoot + "/Textures/" + Sanitize(recipe.id) + "_source" + extension;
            File.Copy(recipe.convertedTexture, ToAbsolute(assetPath), true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) ?? fallback;
        }

        private static AudioClip ImportAudio(Recipe recipe)
        {
            if (string.IsNullOrWhiteSpace(recipe.convertedAudio) ||
                !File.Exists(recipe.convertedAudio))
            {
                return null;
            }

            string extension = Path.GetExtension(recipe.convertedAudio);
            string assetPath =
                StagingRoot + "/Audio/" + Sanitize(recipe.id) + extension;
            File.Copy(recipe.convertedAudio, ToAbsolute(assetPath), true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        }

        private static Material CreateParticleMaterial(
            string id,
            Color color,
            Texture texture,
            bool alphaBlend)
        {
            Shader shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
            {
                shader = Shader.Find(
                    alphaBlend
                        ? "Legacy Shaders/Particles/Alpha Blended"
                        : "Legacy Shaders/Particles/Additive");
            }
            if (shader == null)
            {
                throw new InvalidOperationException("No supported particle shader is available.");
            }

            Material material = new Material(shader)
            {
                name = "NeoX_" + Sanitize(id),
                enableInstancing = true,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
            };
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", alphaBlend ? 2f : 1f);
            }

            string path = StagingRoot + "/Materials/" + material.name + ".mat";
            AssetDatabase.CreateAsset(material, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static void BuildEffectGraph(
            Transform root,
            Recipe recipe,
            Color color,
            Material primary,
            Material smoke,
            Mesh shardMesh)
        {
            float scale = Mathf.Max(0.1f, recipe.scale);
            float duration = Mathf.Max(0.1f, recipe.duration);
            switch ((recipe.kind ?? string.Empty).ToLowerInvariant())
            {
                case "break":
                    CreateBurst(
                        root, "IrregularShards", color, primary, duration,
                        10, 18, 2.5f * scale, 7f * scale, 0.18f * scale, 0.55f * scale,
                        ParticleSystemShapeType.Hemisphere, true, shardMesh, false);
                    CreateBurst(
                        root, "BreakSparks", new Color(1f, 0.55f, 0.18f, 1f), primary,
                        duration * 0.55f, 14, 24, 4f * scale, 10f * scale,
                        0.04f * scale, 0.12f * scale,
                        ParticleSystemShapeType.Cone, false, null, true);
                    break;
                case "smoke":
                    CreateBurst(
                        root, "DetachedSmoke", color, smoke, duration,
                        10, 16, 0.2f * scale, 1.2f * scale,
                        0.45f * scale, 1.25f * scale,
                        ParticleSystemShapeType.Sphere, false, null, false);
                    break;
                case "explosion":
                    CreateBurst(
                        root, "ExplosionCore", color, primary, duration * 0.45f,
                        12, 20, 2f * scale, 8f * scale,
                        0.45f * scale, 1.8f * scale,
                        ParticleSystemShapeType.Sphere, false, null, false);
                    CreateBurst(
                        root, "ExplosionSparks", new Color(1f, 0.86f, 0.28f, 1f),
                        primary, duration * 0.7f, 22, 38, 8f * scale, 18f * scale,
                        0.035f * scale, 0.11f * scale,
                        ParticleSystemShapeType.Sphere, false, null, true);
                    CreateBurst(
                        root, "ExplosionSmoke", new Color(0.22f, 0.24f, 0.28f, 0.72f),
                        smoke, duration, 8, 14, 0.4f * scale, 2f * scale,
                        0.55f * scale, 1.6f * scale,
                        ParticleSystemShapeType.Sphere, false, null, false);
                    break;
                case "shield":
                    CreateBurst(
                        root, "ShieldFragments", color, primary, duration,
                        26, 42, 4f * scale, 11f * scale,
                        0.045f * scale, 0.16f * scale,
                        ParticleSystemShapeType.Sphere, false, null, true);
                    CreateBurst(
                        root, "ShieldCore", new Color(color.r, color.g, color.b, 0.5f),
                        primary, duration * 0.5f, 6, 10, 1f * scale, 4f * scale,
                        0.35f * scale, 0.9f * scale,
                        ParticleSystemShapeType.Sphere, false, null, false);
                    break;
                case "energy":
                    CreateBurst(
                        root, "EnergyCore", color, primary, duration * 0.7f,
                        10, 16, 1.5f * scale, 5f * scale,
                        0.2f * scale, 0.65f * scale,
                        ParticleSystemShapeType.Sphere, false, null, false);
                    CreateBurst(
                        root, "EnergyArcs", Color.Lerp(color, Color.white, 0.35f),
                        primary, duration, 16, 28, 4f * scale, 12f * scale,
                        0.025f * scale, 0.09f * scale,
                        ParticleSystemShapeType.Sphere, false, null, true);
                    break;
                case "laser":
                    CreateBurst(
                        root, "LaserImpact", color, primary, duration,
                        14, 24, 5f * scale, 13f * scale,
                        0.025f * scale, 0.08f * scale,
                        ParticleSystemShapeType.Cone, false, null, true);
                    break;
                default:
                    CreateBurst(
                        root, "ImpactSparks", color, primary, duration,
                        12, 22, 4f * scale, 11f * scale,
                        0.025f * scale, 0.09f * scale,
                        ParticleSystemShapeType.Cone, false, null, true);
                    CreateBurst(
                        root, "ImpactCore", Color.Lerp(color, Color.white, 0.2f),
                        primary, duration * 0.45f, 4, 8, 0.5f * scale, 2f * scale,
                        0.12f * scale, 0.35f * scale,
                        ParticleSystemShapeType.Hemisphere, false, null, false);
                    break;
            }
        }

        private static ParticleSystem CreateBurst(
            Transform parent,
            string name,
            Color color,
            Material material,
            float lifetime,
            short minCount,
            short maxCount,
            float minSpeed,
            float maxSpeed,
            float minSize,
            float maxSize,
            ParticleSystemShapeType shapeType,
            bool meshParticles,
            Mesh mesh,
            bool velocityStretched)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.duration = Mathf.Max(0.1f, lifetime);
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.08f, lifetime * 0.35f),
                Mathf.Max(0.1f, lifetime));
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.maxParticles = Math.Max(maxCount * 2, 32);
            main.gravityModifier = meshParticles ? 0.75f : 0.12f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(
                new[] { new ParticleSystem.Burst(0f, minCount, maxCount) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = shapeType;
            shape.radius = 0.08f;
            if (shapeType == ParticleSystemShapeType.Cone)
            {
                shape.angle = 24f;
                shape.rotation = new Vector3(0f, 0f, 0f);
            }

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime =
                system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color * 0.65f, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime =
                system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 0.35f, 1f, meshParticles ? 0.8f : 1.25f));

            ParticleSystemRenderer renderer =
                child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.alignment = ParticleSystemRenderSpace.World;
            if (meshParticles && mesh != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = mesh;
            }
            else if (velocityStretched)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.12f;
                renderer.lengthScale = 1.8f;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            return system;
        }

        private static void CreateCatalog(BuildContext context)
        {
            NeoXCombatEffectCatalog catalog =
                ScriptableObject.CreateInstance<NeoXCombatEffectCatalog>();
            catalog.EditorReplaceEntries(context.CatalogEntries);
            AssetDatabase.CreateAsset(catalog, StagingRoot + "/" + CatalogName);
            EditorUtility.SetDirty(catalog);
        }

        private static void ValidateStaging(BuildContext context)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { StagingRoot });
            if (prefabGuids.Length != context.CatalogEntries.Count)
            {
                context.Errors.Add(
                    $"Expected {context.CatalogEntries.Count} prefabs, found {prefabGuids.Length}.");
            }

            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    context.Errors.Add("Prefab cannot be loaded: " + path);
                    continue;
                }

                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                if (systems.Length == 0)
                {
                    context.Errors.Add("Prefab has no particle systems: " + path);
                }
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    if (materials == null || materials.Length == 0)
                    {
                        context.Errors.Add("Renderer has no material: " + path);
                        continue;
                    }
                    foreach (Material material in materials)
                    {
                        if (material == null || material.shader == null)
                        {
                            context.Errors.Add("Renderer material is missing: " + path);
                        }
                        else if (material.shader.name == "Hidden/InternalErrorShader")
                        {
                            context.Errors.Add("Pink error shader detected: " + path);
                        }
                    }
                }
            }

            NeoXCombatEffectCatalog catalog =
                AssetDatabase.LoadAssetAtPath<NeoXCombatEffectCatalog>(
                    StagingRoot + "/" + CatalogName);
            if (catalog == null || catalog.Entries.Count != context.CatalogEntries.Count)
            {
                context.Errors.Add("Generated runtime catalog is invalid.");
            }
        }

        private static void PublishStaging(BuildContext context)
        {
            if (AssetDatabase.IsValidFolder(BackupRoot))
            {
                AssetDatabase.DeleteAsset(BackupRoot);
            }

            bool movedExisting = false;
            if (AssetDatabase.IsValidFolder(FinalRoot))
            {
                string backupError = AssetDatabase.MoveAsset(FinalRoot, BackupRoot);
                if (!string.IsNullOrEmpty(backupError))
                {
                    context.Errors.Add("Cannot move current catalog to backup: " + backupError);
                    return;
                }
                movedExisting = true;
            }

            string publishError = AssetDatabase.MoveAsset(StagingRoot, FinalRoot);
            if (!string.IsNullOrEmpty(publishError))
            {
                context.Errors.Add("Cannot publish staging catalog: " + publishError);
                if (movedExisting)
                {
                    AssetDatabase.MoveAsset(BackupRoot, FinalRoot);
                }
                return;
            }

            if (movedExisting && AssetDatabase.IsValidFolder(BackupRoot))
            {
                AssetDatabase.DeleteAsset(BackupRoot);
            }

            foreach (NeoXEffectConversionRecord record in context.Records)
            {
                record.outputPrefab = PublishedPath(record.outputPrefab);
                record.outputMaterial = PublishedPath(record.outputMaterial);
                record.outputTexture = PublishedPath(record.outputTexture);
                record.outputAudio = PublishedPath(record.outputAudio);
            }
        }

        private static void WriteReport(
            string reportPath,
            RecipeDocument document,
            BuildContext context)
        {
            ConversionReport report = new ConversionReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                published = context.Errors.Count == 0,
                recipeCount = document.recipes?.Length ?? 0,
                succeeded = context.Records.Count(record => record.succeeded),
                degraded = context.Records.Count(record => record.degraded),
                validationErrors = context.Errors.ToArray(),
                records = context.Records.ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? string.Empty);
            string temporary = reportPath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(report, true) + Environment.NewLine);
            if (File.Exists(reportPath))
            {
                File.Replace(temporary, reportPath, null);
            }
            else
            {
                File.Move(temporary, reportPath);
            }
        }

        private static NeoXCombatEffectStage ParseStage(string value)
        {
            return Enum.TryParse(value, true, out NeoXCombatEffectStage stage)
                ? stage
                : NeoXCombatEffectStage.Hit;
        }

        private static Color ParseColor(string html, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(html) &&
                ColorUtility.TryParseHtmlString(html, out Color color))
            {
                return color;
            }
            return fallback;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Unnamed";
            }
            return new string(
                value.Select(character =>
                    char.IsLetterOrDigit(character) || character == '_'
                        ? character
                        : '_').ToArray());
        }

        private static string ToAbsolute(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string PublishedPath(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath)
                ? string.Empty
                : assetPath.Replace(StagingRoot, FinalRoot);
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }
            Directory.CreateDirectory(ToAbsolute(assetPath));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
