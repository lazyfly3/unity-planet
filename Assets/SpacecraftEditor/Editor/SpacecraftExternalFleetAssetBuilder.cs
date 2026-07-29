using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KDL.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SpacecraftEditor.Editor
{
    /// <summary>
    /// Publishes the five reviewed C_Veh006 ships and the curated Fleet I modules.
    /// Source packages remain outside Assets; only normalized runtime assets are copied.
    /// </summary>
    // Publishes the reviewed third-party fleet into the runtime-safe catalog.
    public static class SpacecraftExternalFleetAssetBuilder
    {
        const string StagingRoot = "Library/SpaceshipPCGStaging/external_fleet_v1";
        const string CandidateSource =
            "Library/SpaceshipAssetReview/c_veh006/candidates/source";
        const string FleetSource =
            "Library/SpaceshipAssetReview/fleet_i_modules";
        const string PublishRoot = "Assets/SpacecraftEditor/ExternalFleet";
        const string HullModelRoot = PublishRoot + "/Models/Hulls";
        const string ModuleModelRoot = PublishRoot + "/Models/Modules";
        const string TextureRoot = PublishRoot + "/Textures";
        const string HullMaterialRoot = PublishRoot + "/Materials/Hulls";
        const string ModuleMaterialRoot = PublishRoot + "/Materials/Modules";
        const string HullPrefabRoot = PublishRoot + "/Prefabs/Hulls";
        const string ModulePrefabRoot = PublishRoot + "/Prefabs/Modules";
        const string HullDataRoot = PublishRoot + "/Data/Hulls";
        const string ModuleDataRoot = PublishRoot + "/Data/Modules";
        const string IconRoot = PublishRoot + "/Icons";
        const string WorkshopPrefab =
            "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab";
        const string PirateResourceRoot = "Assets/Resources/Spaceflight/Pirates";

        static readonly string[] TargetScenes =
        {
            "Assets/Scenes/SpacecraftWorkshop.unity",
            "Assets/Scenes/InterstellarFlight.unity",
            "Assets/Scenes/PlanetApproach.unity"
        };

        static readonly string[] ExternalHullOrder =
        {
            "hull.a30_thunderbolt",
            "hull.sf_stealth_fighter",
            "hull.sf_modular_pirate",
            "hull.sf_dropship_r35",
            "hull.sf_fighter_gr2"
        };

        [Serializable]
        sealed class FleetManifest
        {
            public ShipManifest[] ships;
        }

        [Serializable]
        sealed class ShipManifest
        {
            public string hullId;
            public string displayName;
            public string key;
            public float[] dimensions;
            public MaterialManifest[] materials;
            public string[] sourceTextures;
        }

        [Serializable]
        sealed class MaterialManifest
        {
            public string name;
            public TextureManifest[] textures;
            public bool glass;
            public bool emission;
        }

        [Serializable]
        sealed class TextureManifest
        {
            public string file;
            public string[] roles;
            public bool nonColor;
        }

        sealed class ModuleSpec
        {
            public string Key;
            public string[] Sources;
            public string MaterialFamily;
            public float Size;
            public bool Weapon;
        }

        static ModuleSpec Module(
            string key,
            float size,
            bool weapon,
            string family,
            params string[] sources)
        {
            return new ModuleSpec
            {
                Key = key,
                Size = size,
                Weapon = weapon,
                MaterialFamily = family,
                Sources = sources
            };
        }

        static readonly ModuleSpec[] Modules =
        {
            Module("kinetic_s1", 0.62f, true, "FrigateWeapons",
                "Frigates/FrigateWeapons/FrigateLightTurretBody.obj",
                "Frigates/FrigateWeapons/FrigateLightTurretBarrel.obj"),
            Module("kinetic_s2", 0.88f, true, "DestroyerWeapons",
                "Destroyers/DestroyerWeapons/DestroyerMediumTurretBody.obj",
                "Destroyers/DestroyerWeapons/DestroyerMediumTurretBarrel.obj"),
            Module("kinetic_s3", 1.16f, true, "DestroyerWeapons",
                "Destroyers/DestroyerWeapons/DestroyerHeavyTurretBody.obj",
                "Destroyers/DestroyerWeapons/DestroyerHeavyTurretBarrel.obj"),
            Module("energy_s1", 0.55f, true, "CorvetteWeapons",
                "Corvettes/CorvetteWeapons/LaserTurret.obj",
                "Corvettes/CorvetteWeapons/LaserTurretBarrel.obj"),
            Module("energy_s2", 0.78f, true, "CorvetteWeapons",
                "Corvettes/CorvetteWeapons/LaserTurret.obj",
                "Corvettes/CorvetteWeapons/LaserTurretBarrel.obj"),
            Module("energy_s3", 1.02f, true, "CorvetteWeapons",
                "Corvettes/CorvetteWeapons/LaserTurret.obj",
                "Corvettes/CorvetteWeapons/LaserTurretBarrel.obj"),
            Module("flak_s1", 0.62f, true, "CorvetteWeapons",
                "Corvettes/CorvetteWeapons/FlakTurret.obj",
                "Corvettes/CorvetteWeapons/FlakTurretBarrelStatic.obj"),
            Module("flak_s2", 0.92f, true, "DestroyerWeapons",
                "Destroyers/DestroyerWeapons/DestroyerFlakTurretBody.obj",
                "Destroyers/DestroyerWeapons/DestroyerFlakTurretBarrel.obj"),
            Module("missile_s1", 0.72f, true, "CorvetteWeapons",
                "Corvettes/CorvetteWeapons/MissileMountLeft.obj",
                "Corvettes/CorvetteWeapons/MissileMountRight.obj",
                "Corvettes/CorvetteMissiles/SmallCorvetteMissile1.obj"),
            Module("missile_s2", 0.96f, true, "FrigateWeapons",
                "Frigates/FrigateWeapons/FrigateMissileMountLeft.obj",
                "Frigates/FrigateWeapons/FrigateMissileMountRight.obj",
                "Corvettes/CorvetteMissiles/SmallCorvetteMissile2.obj"),
            Module("torpedo_s2", 1.0f, true, "DestroyerWeapons",
                "Destroyers/DestroyerWeapons/DestroyerHeavyTorpedoTube.obj"),
            Module("antenna", 0.78f, false, "MothershipWeaponsEquipment",
                "Mothership1/MothershipWeaponEquipment/DefaultAntenna.obj"),
            Module("gunport", 0.70f, false, "MothershipWeaponsEquipment",
                "Mothership1/MothershipWeaponEquipment/Gunport.obj"),
            Module("hangar_deck", 1.05f, false, "MothershipWeaponsEquipment",
                "Mothership1/MothershipWeaponEquipment/DefaultHangarDeck.obj"),
            Module("fighter_facility", 1.05f, false, "MothershipWeaponsEquipment",
                "Mothership1/MothershipWeaponEquipment/FighterCorvetteFacility.obj"),
            Module("resource_platform", 1.0f, false, "MothershipWeaponsEquipment",
                "Mothership1/MothershipWeaponEquipment/ResourceUnloadingPlatform.obj")
        };

        [MenuItem("Tools/Spacecraft/Publish External Fleet V1")]
        [AICallable(
            "Publish the reviewed five-ship external fleet, native PBR materials and curated Fleet I modules into the spacecraft editor.",
            Category = "Spacecraft.Build",
            Kind = ToolKind.Write)]
        public static string BuildAll()
        {
            string projectRoot = ProjectRoot();
            string manifestPath = Path.Combine(projectRoot, StagingRoot,
                "external_fleet_manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException(
                    "Run prepare_external_fleet.py before publishing.", manifestPath);

            EnsureFolders();
            ArchiveLegacyPcgAssets();
            CopyReviewedAssets(projectRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            FleetManifest manifest =
                JsonUtility.FromJson<FleetManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.ships == null || manifest.ships.Length != 5)
                throw new InvalidOperationException("External fleet manifest must contain exactly five ships.");

            ConfigureImportedAssets(manifest);
            Dictionary<string, Material> fleetMaterials = BuildFleetMaterials();
            Dictionary<string, GameObject> modulePrefabs = BuildModulePrefabs(fleetMaterials);
            ShipPartDefinition[] parts = BuildPartDefinitions(modulePrefabs);
            ShipHullDefinition[] hulls = BuildHullDefinitions(manifest);
            SpacecraftMaterialDefinition[] paints = BuildNativePaintLibrary();
            SpacecraftHardpointLayout[] layouts = BuildPirateLayouts(hulls);

            UpdateRuntimePrefabs(hulls, parts, paints, layouts);
            UpdateRuntimeScenes(hulls, parts, paints, layouts);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ValidatePublishedAssets(hulls, parts);
            string result =
                "Published 5 external hulls with native materials, 31 part definitions and 5 pirate layouts.";
            Debug.Log(result);
            return result;
        }

        [MenuItem("Tools/Spacecraft/Validate External Fleet V1")]
        public static string Validate()
        {
            ShipHullDefinition[] hulls = LoadExternalHulls();
            ShipPartDefinition[] parts = LoadRuntimeParts();
            ValidatePublishedAssets(hulls, parts);
            string result = "External fleet validation passed.";
            Debug.Log(result);
            return result;
        }

        static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        static void EnsureFolders()
        {
            foreach (string folder in new[]
                     {
                         HullModelRoot, ModuleModelRoot, TextureRoot,
                         HullMaterialRoot, ModuleMaterialRoot, HullPrefabRoot,
                         ModulePrefabRoot, HullDataRoot, ModuleDataRoot, IconRoot,
                         "Assets/SpacecraftEditor/LegacyPCG"
                     })
                Directory.CreateDirectory(Path.Combine(ProjectRoot(), folder));
        }

        static void CopyReviewedAssets(string projectRoot)
        {
            string staging = Path.Combine(projectRoot, StagingRoot);
            foreach (string directory in Directory.GetDirectories(staging))
            {
                string key = Path.GetFileName(directory);
                string target = Path.Combine(projectRoot, HullModelRoot, key);
                Directory.CreateDirectory(target);
                foreach (string file in Directory.GetFiles(directory))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".fbx" && extension != ".png")
                        continue;
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                }
            }

            string candidateRoot = Path.Combine(projectRoot, CandidateSource);
            foreach (string hullId in ExternalHullOrder)
            {
                string key = hullId.Replace("hull.", string.Empty).Replace('.', '_');
                ShipManifest ship = ReadShipManifest(
                    Path.Combine(staging, key, "manifest.json"));
                string sourceTextures = Path.Combine(
                    candidateRoot, ship.displayName, "textures");
                string targetTextures = Path.Combine(projectRoot, TextureRoot, "Hulls", key);
                CopyDirectory(sourceTextures, targetTextures);
            }

            string converted = Path.Combine(projectRoot, FleetSource, "converted_obj");
            foreach (ModuleSpec module in Modules)
            foreach (string source in module.Sources)
            {
                string from = Path.Combine(converted, source.Replace('/', Path.DirectorySeparatorChar));
                string to = Path.Combine(projectRoot, ModuleModelRoot,
                    source.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to, true);
            }
            CopyDirectory(
                Path.Combine(projectRoot, FleetSource, "source", "Textures"),
                Path.Combine(projectRoot, TextureRoot, "FleetI"));
        }

        static ShipManifest ReadShipManifest(string path)
        {
            ShipManifest ship = JsonUtility.FromJson<ShipManifest>(File.ReadAllText(path));
            if (ship == null)
                throw new InvalidOperationException("Invalid ship manifest: " + path);
            return ship;
        }

        static void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source))
                return;
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        static void ConfigureImportedAssets(FleetManifest manifest)
        {
            foreach (ShipManifest ship in manifest.ships)
            {
                string fbxPath = HullModelRoot + "/" + ship.key + "/" + ship.key + ".fbx";
                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null)
                {
                    importer.globalScale = 1f;
                    importer.importBlendShapes = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.meshCompression = ModelImporterMeshCompression.Medium;
                    importer.isReadable = false;
                    // The Blender manifest is the authoritative material-slot map.
                    // Disabling FBX embedded materials prevents Unity from reinterpreting
                    // one legacy texture as both albedo and normal during import.
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                }
                string thumbnail = HullModelRoot + "/" + ship.key + "/" +
                                   ship.key + "_thumbnail.png";
                ConfigureSprite(thumbnail);
                ConfigureShipTextures(ship);
            }

            string[] objGuids = AssetDatabase.FindAssets("t:Model", new[] { ModuleModelRoot });
            foreach (string guid in objGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                    continue;
                importer.globalScale = 1f;
                importer.importBlendShapes = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.SaveAndReimport();
            }
            ConfigureFleetTextures();
        }

        static void ConfigureSprite(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        static void ConfigureShipTextures(ShipManifest ship)
        {
            var roles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (MaterialManifest material in ship.materials ?? Array.Empty<MaterialManifest>())
            foreach (TextureManifest texture in material.textures ?? Array.Empty<TextureManifest>())
            {
                if (!roles.TryGetValue(texture.file, out HashSet<string> set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    roles[texture.file] = set;
                }
                foreach (string role in texture.roles ?? Array.Empty<string>())
                    set.Add(role);
            }
            foreach (string file in ship.sourceTextures ?? Array.Empty<string>())
            {
                string path = TextureRoot + "/Hulls/" + ship.key + "/" + file;
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                roles.TryGetValue(file, out HashSet<string> set);
                bool baseColor = set != null && set.Contains("Base Color");
                bool normal = set != null && set.Contains("Normal") && !baseColor;
                bool linear = set != null && set.Any(role =>
                    role.IndexOf("Roughness", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    role.IndexOf("Metallic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    role.IndexOf("Specular", StringComparison.OrdinalIgnoreCase) >= 0);
                importer.textureType = normal
                    ? TextureImporterType.NormalMap
                    : TextureImporterType.Default;
                importer.sRGBTexture = !normal && !linear;
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 8;
                importer.isReadable = linear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        static void ConfigureFleetTextures()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D",
                new[] { TextureRoot + "/FleetI" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                string name = Path.GetFileNameWithoutExtension(path);
                bool normal = name.EndsWith("Normal", StringComparison.OrdinalIgnoreCase);
                bool color = name.IndexOf("Albedo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("Illumination", StringComparison.OrdinalIgnoreCase) >= 0;
                importer.textureType = normal
                    ? TextureImporterType.NormalMap
                    : TextureImporterType.Default;
                importer.sRGBTexture = color && !normal;
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 8;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        static ShipHullDefinition[] BuildHullDefinitions(FleetManifest manifest)
        {
            var lookup = manifest.ships.ToDictionary(ship => ship.hullId, StringComparer.Ordinal);
            var result = new List<ShipHullDefinition>(5);
            foreach (string hullId in ExternalHullOrder)
            {
                ShipManifest ship = lookup[hullId];
                Dictionary<string, Material> materials = BuildShipMaterials(ship);
                GameObject prefab = BuildHullPrefab(ship, materials);
                string fbxPath = HullModelRoot + "/" + ship.key + "/" + ship.key + ".fbx";
                Mesh collision = BuildRuntimeMesh(fbxPath, ship.key, "Collision");
                Mesh placement = BuildRuntimeMesh(fbxPath, ship.key, "PlacementSurface");
                Sprite thumbnail = AssetDatabase.LoadAssetAtPath<Sprite>(
                    HullModelRoot + "/" + ship.key + "/" + ship.key + "_thumbnail.png");
                string dataPath = HullDataRoot + "/" + ship.key + ".asset";
                ShipHullDefinition definition =
                    AssetDatabase.LoadAssetAtPath<ShipHullDefinition>(dataPath);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ShipHullDefinition>();
                    AssetDatabase.CreateAsset(definition, dataPath);
                }
                Vector3 dimensions = new Vector3(
                    SnapWholeDimension(ship.dimensions[0]),
                    SnapWholeDimension(ship.dimensions[1]),
                    SnapWholeDimension(ship.dimensions[2]));
                definition.Configure(
                    ship.hullId,
                    ship.displayName,
                    "外部审核舰体；保留原厂 PBR 材质并支持自由表面装载。",
                    prefab,
                    thumbnail,
                    collision,
                    placement,
                    12000f,
                    dimensions);
                EditorUtility.SetDirty(definition);
                result.Add(definition);
            }
            return result.ToArray();
        }

        static float SnapWholeDimension(float value)
        {
            float whole = Mathf.Round(value);
            return Mathf.Abs(value - whole) <= 0.0001f ? whole : value;
        }

        static Dictionary<string, Material> BuildShipMaterials(ShipManifest ship)
        {
            var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (MaterialManifest source in ship.materials ?? Array.Empty<MaterialManifest>())
            {
                string safe = SafeName(source.name);
                string path = HullMaterialRoot + "/" + ship.key + "_" + safe + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Standard"));
                    AssetDatabase.CreateAsset(material, path);
                }
                material.name = ship.key + "_" + source.name;
                Shader standardShader = Shader.Find("Standard");
                if (material.shader != standardShader)
                    material.shader = standardShader;
                if (source.glass)
                    ConfigureGlass(material);
                else
                {
                    ResetStandardMaterial(material);
                    ConfigureShipPbr(material, ship, source);
                }
                EditorUtility.SetDirty(material);
                result[source.name] = material;
            }
            return result;
        }

        static void ConfigureShipPbr(
            Material material,
            ShipManifest ship,
            MaterialManifest source)
        {
            TextureManifest baseTexture = FindRole(source, "Base Color");
            TextureManifest normalTexture = FindRole(source, "Normal");
            TextureManifest metallicTexture = FindRole(source, "Metallic");
            TextureManifest roughnessTexture = FindRole(source, "Roughness");
            TextureManifest emissionTexture =
                FindRole(source, "Emission") ?? FindRole(source, "Emission Color");

            Texture2D albedo = LoadShipTexture(ship, baseTexture);
            if (albedo != null)
                material.SetTexture("_MainTex", albedo);
            if (normalTexture != null &&
                (baseTexture == null ||
                 !normalTexture.file.Equals(baseTexture.file, StringComparison.OrdinalIgnoreCase)))
            {
                Texture2D normal = LoadShipTexture(ship, normalTexture);
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            if (roughnessTexture != null || metallicTexture != null)
            {
                Texture2D packed = BuildMetallicSmoothness(
                    ship, source, metallicTexture, roughnessTexture);
                if (packed != null)
                {
                    material.SetTexture("_MetallicGlossMap", packed);
                    material.EnableKeyword("_METALLICGLOSSMAP");
                    material.SetFloat("_GlossMapScale", 0.82f);
                }
            }
            if (emissionTexture != null)
            {
                Texture2D emission = LoadShipTexture(ship, emissionTexture);
                if (emission != null)
                {
                    material.SetTexture("_EmissionMap", emission);
                    material.SetColor("_EmissionColor", Color.white * 1.6f);
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags =
                        MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }
            material.SetFloat("_Metallic", metallicTexture == null ? 0.18f : 1f);
            material.SetFloat("_Glossiness", 0.48f);
        }

        static TextureManifest FindRole(MaterialManifest material, string role)
        {
            return (material.textures ?? Array.Empty<TextureManifest>())
                .FirstOrDefault(texture =>
                    (texture.roles ?? Array.Empty<string>())
                    .Any(value => value.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        static Texture2D LoadShipTexture(ShipManifest ship, TextureManifest texture)
        {
            return texture == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(
                    TextureRoot + "/Hulls/" + ship.key + "/" + texture.file);
        }

        static Texture2D BuildMetallicSmoothness(
            ShipManifest ship,
            MaterialManifest material,
            TextureManifest metallicSource,
            TextureManifest roughnessSource)
        {
            Texture2D metallic = LoadShipTexture(ship, metallicSource);
            Texture2D roughness = LoadShipTexture(ship, roughnessSource);
            if (metallic == null && roughness == null)
                return null;
            int width = Mathf.Min(1024, roughness != null ? roughness.width : metallic.width);
            int height = Mathf.Min(1024, roughness != null ? roughness.height : metallic.height);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                byte metal = metallic == null
                    ? (byte)38
                    : (byte)Mathf.RoundToInt(metallic.GetPixelBilinear(u, v).grayscale * 255f);
                byte smooth = roughness == null
                    ? (byte)128
                    : (byte)Mathf.RoundToInt(
                        (1f - roughness.GetPixelBilinear(u, v).grayscale) * 255f);
                pixels[y * width + x] = new Color32(metal, metal, metal, smooth);
            }
            var packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            packed.SetPixels32(pixels);
            packed.Apply(false, false);
            string output = TextureRoot + "/Hulls/" + ship.key + "/" +
                            SafeName(material.name) + "_MetallicSmoothness.png";
            File.WriteAllBytes(Path.Combine(ProjectRoot(), output), packed.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(packed);
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(output) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(output);
        }

        static void ResetStandardMaterial(Material material)
        {
            ClearStandardMaps(material);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Mode", 0f);
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = -1;
        }

        static void ConfigureGlass(Material material)
        {
            ClearStandardMaps(material);
            material.SetColor("_Color", new Color(0.12f, 0.35f, 0.48f, 0.24f));
            material.SetFloat("_Metallic", 0.05f);
            material.SetFloat("_Glossiness", 0.88f);
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        static void ClearStandardMaps(Material material)
        {
            foreach (string property in new[]
                     {
                         "_MainTex", "_BumpMap", "_MetallicGlossMap",
                         "_SpecGlossMap", "_OcclusionMap", "_EmissionMap"
                     })
            {
                if (material.HasProperty(property))
                    material.SetTexture(property, null);
            }
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_SPECGLOSSMAP");
            material.DisableKeyword("_EMISSION");
        }

        static GameObject BuildHullPrefab(
            ShipManifest ship,
            Dictionary<string, Material> materials)
        {
            string modelPath = HullModelRoot + "/" + ship.key + "/" + ship.key + ".fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null)
                throw new InvalidOperationException("Missing imported hull: " + modelPath);
            var root = new GameObject(ship.key);
            GameObject model = PrefabUtility.InstantiatePrefab(source) as GameObject;
            PrefabUtility.UnpackPrefabInstance(
                model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            foreach (Transform transform in model.GetComponentsInChildren<Transform>(true)
                         .Where(value => value.name == "Collision" ||
                                         value.name == "PlacementSurface")
                         .OrderByDescending(value => Depth(value))
                         .ToArray())
            {
                if (transform != model.transform)
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
            }

            var bindings = new List<SpacecraftPaintBinding.RendererBinding>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] assigned = renderer.sharedMaterials;
                var slots = new List<int>();
                for (int index = 0; index < assigned.Length; index++)
                {
                    string sourceName = assigned[index] == null ? string.Empty : assigned[index].name;
                    MaterialManifest manifestMaterial =
                        ship.materials.FirstOrDefault(value =>
                            sourceName.StartsWith(value.name, StringComparison.OrdinalIgnoreCase));
                    if (manifestMaterial == null && index < ship.materials.Length)
                        manifestMaterial = ship.materials[index];
                    Material replacement = manifestMaterial == null
                        ? FindMaterial(materials, sourceName)
                        : materials[manifestMaterial.name];
                    if (replacement != null)
                        assigned[index] = replacement;
                    if (manifestMaterial != null && !manifestMaterial.glass &&
                        manifestMaterial.name.IndexOf("HUD", StringComparison.OrdinalIgnoreCase) < 0)
                        slots.Add(index);
                }
                renderer.sharedMaterials = assigned;
                var binding = new SpacecraftPaintBinding.RendererBinding();
                binding.Configure(renderer, slots.ToArray());
                bindings.Add(binding);
            }
            var paintBinding = root.AddComponent<SpacecraftPaintBinding>();
            paintBinding.Configure(bindings.ToArray());
            string prefabPath = HullPrefabRoot + "/" + ship.key + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
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

        static Material FindMaterial(
            Dictionary<string, Material> materials,
            string sourceName)
        {
            foreach (KeyValuePair<string, Material> pair in materials)
            {
                if (sourceName.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }
            return null;
        }

        static Mesh BuildRuntimeMesh(string assetPath, string shipKey, string meshName)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Missing imported hull: " + assetPath);
            try
            {
                MeshFilter filter = instance.GetComponentsInChildren<MeshFilter>(true)
                    .FirstOrDefault(value =>
                        value.name.Equals(meshName, StringComparison.OrdinalIgnoreCase));
                if (filter == null || filter.sharedMesh == null)
                    throw new InvalidOperationException(
                        "Missing mesh " + meshName + " in " + assetPath);

                // FBX axis conversion is represented on the imported hierarchy. Baking that
                // transform keeps the standalone collider/placement meshes aligned with the
                // rendered prefab when they are assigned directly to a MeshCollider.
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                Mesh baked = UnityEngine.Object.Instantiate(filter.sharedMesh);
                baked.name = shipKey + "_" + meshName;

                Vector3[] vertices = baked.vertices;
                for (int index = 0; index < vertices.Length; index++)
                    vertices[index] = matrix.MultiplyPoint3x4(vertices[index]);
                baked.vertices = vertices;

                Vector3[] normals = baked.normals;
                for (int index = 0; index < normals.Length; index++)
                    normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
                baked.normals = normals;

                Vector4[] tangents = baked.tangents;
                for (int index = 0; index < tangents.Length; index++)
                {
                    Vector3 direction = matrix.MultiplyVector(
                        new Vector3(tangents[index].x, tangents[index].y, tangents[index].z))
                        .normalized;
                    tangents[index] =
                        new Vector4(direction.x, direction.y, direction.z, tangents[index].w);
                }
                baked.tangents = tangents;
                baked.RecalculateBounds();

                string outputPath = HullModelRoot + "/" + shipKey + "/" +
                                    shipKey + "_" + meshName + ".asset";
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(baked, outputPath);
                    return baked;
                }

                EditorUtility.CopySerialized(baked, existing);
                existing.name = shipKey + "_" + meshName;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(baked);
                return existing;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static Dictionary<string, Material> BuildFleetMaterials()
        {
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (string family in Modules.Select(module => module.MaterialFamily)
                         .Concat(new[] { "CorvetteMissiles" }).Distinct())
            {
                string path = ModuleMaterialRoot + "/" + family + "_Grey.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Standard (Specular setup)"));
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = Shader.Find("Standard (Specular setup)");
                ResetStandardMaterial(material);
                string albedoName = family == "CorvetteMissiles"
                    ? family + "Albedo.png"
                    : family + "GreyAlbedoAO.png";
                Texture2D albedo = FindFleetTexture(albedoName) ??
                                   FindFleetTexture(family + "GreyAlbedo.png");
                Texture2D normal = FindFleetTexture(family + "Normal.png");
                Texture2D ao = FindFleetTexture(family + "AO.png");
                Texture2D specular = FindFleetTexture(family + "PBRSpecular.tga") ??
                                     FindFleetTexture(family + "PBRSpecular1.tga");
                Texture2D emission = FindFleetTexture(family + "Illumination.tga");
                material.SetTexture("_MainTex", albedo);
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (ao != null)
                    material.SetTexture("_OcclusionMap", ao);
                if (specular != null)
                {
                    material.SetTexture("_SpecGlossMap", specular);
                    material.EnableKeyword("_SPECGLOSSMAP");
                }
                if (emission != null)
                {
                    material.SetTexture("_EmissionMap", emission);
                    material.SetColor("_EmissionColor", Color.white * 1.4f);
                    material.EnableKeyword("_EMISSION");
                }
                material.SetFloat("_Glossiness", 0.52f);
                EditorUtility.SetDirty(material);
                result[family] = material;
            }
            return result;
        }

        static Texture2D FindFleetTexture(string filename)
        {
            string[] guids = AssetDatabase.FindAssets(
                Path.GetFileNameWithoutExtension(filename),
                new[] { TextureRoot + "/FleetI" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).Equals(filename, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        static Dictionary<string, GameObject> BuildModulePrefabs(
            Dictionary<string, Material> materials)
        {
            var result = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (ModuleSpec spec in Modules)
                result[spec.Key] = BuildModulePrefab(spec, materials);
            return result;
        }

        static GameObject BuildModulePrefab(
            ModuleSpec spec,
            Dictionary<string, Material> materials)
        {
            var root = new GameObject(spec.Key);
            if (spec.Weapon)
                root.AddComponent<WeaponPart>();
            else
                root.AddComponent<DecorationPart>();
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            foreach (string source in spec.Sources)
            {
                string path = ModuleModelRoot + "/" + source;
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null)
                    throw new InvalidOperationException("Missing Fleet I module: " + path);
                GameObject instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
                PrefabUtility.UnpackPrefabInstance(
                    instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = Path.GetFileNameWithoutExtension(source);
                instance.transform.SetParent(model.transform, false);
                Material material = source.IndexOf(
                        "CorvetteMissiles", StringComparison.OrdinalIgnoreCase) >= 0
                    ? materials["CorvetteMissiles"]
                    : materials[spec.MaterialFamily];
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = material;
            }
            Bounds before = CalculateLocalBounds(root.transform,
                model.GetComponentsInChildren<Renderer>(true));
            float scale = spec.Size / Mathf.Max(0.001f,
                Mathf.Max(before.size.x, Mathf.Max(before.size.y, before.size.z)));
            model.transform.localScale = Vector3.one * scale;
            Bounds after = CalculateLocalBounds(root.transform,
                model.GetComponentsInChildren<Renderer>(true));
            model.transform.localPosition +=
                new Vector3(-after.center.x, -after.min.y, -after.min.z);

            Transform gimbal = null;
            if (spec.Weapon)
            {
                gimbal = new GameObject("GimbalPivot").transform;
                gimbal.SetParent(root.transform, false);
                model.transform.SetParent(gimbal, true);
            }
            Bounds finalBounds = CalculateLocalBounds(root.transform,
                model.GetComponentsInChildren<Renderer>(true));
            if (spec.Weapon)
            {
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(gimbal, false);
                muzzle.localPosition = new Vector3(
                    finalBounds.center.x,
                    finalBounds.max.y + 0.02f,
                    finalBounds.center.z);
                muzzle.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
            var collider = root.AddComponent<BoxCollider>();
            collider.center = finalBounds.center;
            collider.size = finalBounds.size;

            var entries = new List<SpacecraftPaintBinding.RendererBinding>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var entry = new SpacecraftPaintBinding.RendererBinding();
                entry.Configure(renderer, Array.Empty<int>());
                entries.Add(entry);
            }
            var binding = root.AddComponent<SpacecraftPaintBinding>();
            binding.Configure(entries.ToArray());

            string pathOut = ModulePrefabRoot + "/" + spec.Key + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, pathOut);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static Bounds CalculateLocalBounds(Transform root, Renderer[] renderers)
        {
            bool initialized = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = root.InverseTransformPoint(
                        world.center + Vector3.Scale(world.extents, new Vector3(x, y, z)));
                    if (!initialized)
                    {
                        result = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                        result.Encapsulate(point);
                }
            }
            return result;
        }

        static ShipPartDefinition[] BuildPartDefinitions(
            Dictionary<string, GameObject> prefabs)
        {
            ReplaceExistingWeaponFamily("weapon.kinetic_repeater", "kinetic", prefabs);
            ReplaceExistingWeaponFamily("weapon.energy_pulse", "energy", prefabs);
            ReplaceDecoration("decor.sensor_mast", "原厂天线", prefabs["antenna"]);
            ReplaceDecoration("decor.armor_fairing", "原厂炮口整流罩", prefabs["gunport"]);

            Sprite icon = EnsureModuleIcon();
            UpsertDecoration("decor.hangar_deck", "机库甲板", prefabs["hangar_deck"], icon, 18f);
            UpsertDecoration("decor.fighter_facility", "战机维护设施",
                prefabs["fighter_facility"], icon, 22f);
            UpsertDecoration("decor.resource_platform", "资源装卸平台",
                prefabs["resource_platform"], icon, 16f);
            UpsertWeapon("weapon.flak.s1", "近防炮 S1", prefabs["flak_s1"], icon,
                SpaceWeaponMountSize.S1, SpaceWeaponFireMode.Repeater, 10f, 4f, 420f, 900f, 480);
            UpsertWeapon("weapon.flak.s2", "近防炮 S2", prefabs["flak_s2"], icon,
                SpaceWeaponMountSize.S2, SpaceWeaponFireMode.Repeater, 8f, 7f, 460f, 1100f, 380);
            UpsertWeapon("weapon.missile_rack.s1", "导弹架 S1", prefabs["missile_s1"], icon,
                SpaceWeaponMountSize.S1, SpaceWeaponFireMode.Missile, 1.5f, 24f, 360f, 1500f, 12);
            UpsertWeapon("weapon.missile_rack.s2", "导弹架 S2", prefabs["missile_s2"], icon,
                SpaceWeaponMountSize.S2, SpaceWeaponFireMode.Missile, 1.1f, 40f, 340f, 1700f, 8);
            UpsertWeapon("weapon.torpedo.s2", "重型鱼雷管 S2", prefabs["torpedo_s2"], icon,
                SpaceWeaponMountSize.S2, SpaceWeaponFireMode.Missile, 0.7f, 68f, 300f, 1900f, 5);

            ShipPartDefinition[] result = LoadRuntimeParts();
            if (result.Length != 31)
                throw new InvalidOperationException(
                    "Runtime part catalog must contain exactly 31 definitions, found " + result.Length);
            return result;
        }

        static void ReplaceExistingWeaponFamily(
            string prefix,
            string prefabPrefix,
            Dictionary<string, GameObject> prefabs)
        {
            for (int size = 1; size <= 3; size++)
            foreach (string gimbal in new[] { string.Empty, ".gimbal" })
            {
                string id = prefix + gimbal + ".s" + size;
                ShipPartDefinition definition = FindPart(id);
                if (definition == null)
                    throw new InvalidOperationException("Missing existing part: " + id);
                definition.ConfigureGeneral(
                    definition.PartId,
                    definition.DisplayName,
                    prefabs[prefabPrefix + "_s" + size],
                    definition.Thumbnail,
                    definition.Category,
                    definition.BaseMass,
                    definition.ScaleMode,
                    definition.FixedScale,
                    SpacecraftPaintBinding.NativePaintId,
                    definition.Weapon,
                    definition.PlacementMode);
                EditorUtility.SetDirty(definition);
            }
        }

        static void ReplaceDecoration(string id, string label, GameObject prefab)
        {
            ShipPartDefinition definition = FindPart(id);
            if (definition == null)
                throw new InvalidOperationException("Missing existing part: " + id);
            definition.ConfigureGeneral(
                id, label, prefab, definition.Thumbnail, definition.Category,
                definition.BaseMass, definition.ScaleMode, definition.FixedScale,
                SpacecraftPaintBinding.NativePaintId, null, definition.PlacementMode);
            EditorUtility.SetDirty(definition);
        }

        static ShipPartDefinition UpsertDecoration(
            string id,
            string label,
            GameObject prefab,
            Sprite icon,
            float mass)
        {
            ShipPartDefinition definition = LoadOrCreatePart(id);
            definition.ConfigureGeneral(
                id, label, prefab, icon, SpacecraftPartCategory.Decoration,
                mass, SpacecraftPartScaleMode.Free, 1f,
                SpacecraftPaintBinding.NativePaintId);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static ShipPartDefinition UpsertWeapon(
            string id,
            string label,
            GameObject prefab,
            Sprite icon,
            SpaceWeaponMountSize size,
            SpaceWeaponFireMode mode,
            float rounds,
            float damage,
            float speed,
            float range,
            int ammunition)
        {
            var weapon = new SpacecraftWeaponDefinition();
            weapon.Configure(
                SpaceWeaponDamageChannel.Explosive, mode, size, 1,
                rounds, damage, speed, range, mode == SpaceWeaponFireMode.Repeater ? 1.2f : 0.15f,
                ammunition, 0f, 0.05f, 0.25f, 0.55f, damage * 0.5f);
            ShipPartDefinition definition = LoadOrCreatePart(id);
            definition.ConfigureGeneral(
                id, label, prefab, icon, SpacecraftPartCategory.Weapon,
                10f + (int)size * 6f, SpacecraftPartScaleMode.Fixed, 1f,
                SpacecraftPaintBinding.NativePaintId, weapon);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static ShipPartDefinition LoadOrCreatePart(string id)
        {
            string path = ModuleDataRoot + "/" + id.Replace('.', '_') + ".asset";
            ShipPartDefinition definition =
                AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ShipPartDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }
            return definition;
        }

        static ShipPartDefinition FindPart(string id)
        {
            return AssetDatabase.FindAssets("t:ShipPartDefinition",
                    new[] { "Assets/SpacecraftEditor/Data", PublishRoot + "/Data" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShipPartDefinition>)
                .FirstOrDefault(value => value != null && value.PartId == id);
        }

        static ShipPartDefinition[] LoadRuntimeParts()
        {
            var intended = new HashSet<string>(StringComparer.Ordinal)
            {
                "thruster.small", "thruster.medium", "thruster.large",
                "decor.swept_wing", "decor.delta_wing", "decor.canard",
                "decor.vertical_fin", "decor.armor_fairing", "decor.radiator",
                "decor.sensor_mast", "decor.engine_nacelle",
                "decor.hangar_deck", "decor.fighter_facility", "decor.resource_platform",
                "weapon.flak.s1", "weapon.flak.s2",
                "weapon.missile_rack.s1", "weapon.missile_rack.s2", "weapon.torpedo.s2"
            };
            foreach (string channel in new[] { "kinetic_repeater", "energy_pulse" })
            for (int size = 1; size <= 3; size++)
            {
                intended.Add("weapon." + channel + ".s" + size);
                intended.Add("weapon." + channel + ".gimbal.s" + size);
            }
            return AssetDatabase.FindAssets("t:ShipPartDefinition",
                    new[] { "Assets/SpacecraftEditor/Data", PublishRoot + "/Data" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShipPartDefinition>)
                .Where(value => value != null && intended.Contains(value.PartId))
                .GroupBy(value => value.PartId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(value => value.Category)
                .ThenBy(value => value.PartId, StringComparer.Ordinal)
                .ToArray();
        }

        static Sprite EnsureModuleIcon()
        {
            string path = IconRoot + "/fleet_i_module.png";
            if (!File.Exists(Path.Combine(ProjectRoot(), path)))
            {
                var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                var pixels = new Color32[128 * 128];
                for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    bool edge = x < 5 || y < 5 || x > 122 || y > 122;
                    bool stripe = ((x + y) / 16) % 2 == 0;
                    pixels[y * 128 + x] = edge
                        ? new Color32(38, 226, 255, 255)
                        : stripe
                            ? new Color32(48, 58, 68, 255)
                            : new Color32(28, 34, 42, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(ProjectRoot(), path), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            ConfigureSprite(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static SpacecraftMaterialDefinition[] BuildNativePaintLibrary()
        {
            SpacecraftMaterialCatalog catalog = null;
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopPrefab);
            if (root != null)
                catalog = root.GetComponentInChildren<SpacecraftMaterialCatalog>(true);
            var result = new List<SpacecraftMaterialDefinition>();
            var native = new SpacecraftMaterialDefinition();
            native.Configure(
                SpacecraftPaintBinding.NativePaintId,
                "原厂",
                null,
                new Color(0.36f, 0.42f, 0.48f, 1f));
            result.Add(native);
            if (catalog != null && catalog.Definitions != null)
            {
                foreach (SpacecraftMaterialDefinition definition in catalog.Definitions)
                {
                    if (definition != null &&
                        definition.MaterialId != SpacecraftPaintBinding.NativePaintId)
                        result.Add(definition);
                }
            }
            return result
                .GroupBy(value => value.MaterialId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }

        static SpacecraftHardpointLayout[] BuildPirateLayouts(
            ShipHullDefinition[] hulls)
        {
            var result = new List<SpacecraftHardpointLayout>(hulls.Length);
            foreach (ShipHullDefinition hull in hulls)
            {
                string path = PirateResourceRoot + "/Hardpoints_" +
                              hull.HullId.Replace('.', '_') + ".asset";
                SpacecraftHardpointLayout layout =
                    AssetDatabase.LoadAssetAtPath<SpacecraftHardpointLayout>(path);
                if (layout == null)
                {
                    layout = ScriptableObject.CreateInstance<SpacecraftHardpointLayout>();
                    AssetDatabase.CreateAsset(layout, path);
                }
                Vector3 size = hull.Dimensions;
                var points = new[]
                {
                    Hardpoint("engine_l", "engine_r", SpacecraftPartCategory.Thruster,
                        new Vector3(-size.x * 0.24f, 0f, -size.z * 0.44f),
                        new Vector3(0f, 180f, 0f)),
                    Hardpoint("engine_r", "engine_l", SpacecraftPartCategory.Thruster,
                        new Vector3(size.x * 0.24f, 0f, -size.z * 0.44f),
                        new Vector3(0f, 180f, 0f)),
                    Hardpoint("weapon_l", "weapon_r", SpacecraftPartCategory.Weapon,
                        new Vector3(-size.x * 0.30f, size.y * 0.46f, size.z * 0.05f),
                        new Vector3(90f, 0f, 0f)),
                    Hardpoint("weapon_r", "weapon_l", SpacecraftPartCategory.Weapon,
                        new Vector3(size.x * 0.30f, size.y * 0.46f, size.z * 0.05f),
                        new Vector3(90f, 0f, 0f))
                };
                var region = new SpacecraftDecorationRegion();
                region.Configure("surface", Vector3.zero,
                    new Vector3(size.x * 0.45f, size.y * 0.48f, size.z * 0.38f),
                    Vector3.zero);
                layout.Configure(hull.HullId, points, new[] { region }, 0.55f, 0.05f);
                EditorUtility.SetDirty(layout);
                result.Add(layout);
            }
            return result.ToArray();
        }

        static SpacecraftHardpoint Hardpoint(
            string id,
            string mirror,
            SpacecraftPartCategory category,
            Vector3 position,
            Vector3 rotation)
        {
            var result = new SpacecraftHardpoint();
            result.Configure(id, mirror, category, position, rotation);
            return result;
        }

        static void UpdateRuntimePrefabs(
            ShipHullDefinition[] hulls,
            ShipPartDefinition[] parts,
            SpacecraftMaterialDefinition[] paints,
            SpacecraftHardpointLayout[] layouts)
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { "Assets/Resources/Spacecraft", PirateResourceRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (HullCatalog catalog in root.GetComponentsInChildren<HullCatalog>(true))
                    {
                        catalog.Configure(hulls);
                        EditorUtility.SetDirty(catalog);
                        changed = true;
                    }
                    foreach (PartCatalog catalog in root.GetComponentsInChildren<PartCatalog>(true))
                    {
                        catalog.Configure(parts);
                        EditorUtility.SetDirty(catalog);
                        changed = true;
                    }
                    foreach (SpacecraftMaterialCatalog catalog in
                             root.GetComponentsInChildren<SpacecraftMaterialCatalog>(true))
                    {
                        catalog.Configure(paints, "paint.deep_space_blue");
                        EditorUtility.SetDirty(catalog);
                        changed = true;
                    }
                    foreach (ProceduralPirateShipGenerator generator in
                             root.GetComponentsInChildren<ProceduralPirateShipGenerator>(true))
                    {
                        SetObjectArray(generator, "layouts", layouts);
                        EditorUtility.SetDirty(generator);
                        changed = true;
                    }
                    if (changed)
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        static void UpdateRuntimeScenes(
            ShipHullDefinition[] hulls,
            ShipPartDefinition[] parts,
            SpacecraftMaterialDefinition[] paints,
            SpacecraftHardpointLayout[] layouts)
        {
            foreach (string path in TargetScenes)
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (!opened && scene.isDirty)
                    throw new InvalidOperationException(
                        "Target scene has unsaved changes and was not overwritten: " + path);
                if (opened)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (HullCatalog catalog in FindInScene<HullCatalog>(scene))
                    {
                        catalog.Configure(hulls);
                        EditorUtility.SetDirty(catalog);
                    }
                    foreach (PartCatalog catalog in FindInScene<PartCatalog>(scene))
                    {
                        catalog.Configure(parts);
                        EditorUtility.SetDirty(catalog);
                    }
                    foreach (SpacecraftMaterialCatalog catalog in
                             FindInScene<SpacecraftMaterialCatalog>(scene))
                    {
                        catalog.Configure(paints, "paint.deep_space_blue");
                        EditorUtility.SetDirty(catalog);
                    }
                    foreach (ProceduralPirateShipGenerator generator in
                             FindInScene<ProceduralPirateShipGenerator>(scene))
                    {
                        SetObjectArray(generator, "layouts", layouts);
                        EditorUtility.SetDirty(generator);
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                finally
                {
                    if (opened)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        static IEnumerable<T> FindInScene<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true));
        }

        static void SetObjectArray<T>(
            UnityEngine.Object target,
            string propertyName,
            T[] values) where T : UnityEngine.Object
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                return;
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static ShipHullDefinition[] LoadExternalHulls()
        {
            var values = AssetDatabase.FindAssets("t:ShipHullDefinition",
                    new[] { HullDataRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShipHullDefinition>)
                .Where(value => value != null)
                .ToDictionary(value => value.HullId, StringComparer.Ordinal);
            return ExternalHullOrder.Select(id => values[id]).ToArray();
        }

        static void ValidatePublishedAssets(
            ShipHullDefinition[] hulls,
            ShipPartDefinition[] parts)
        {
            if (hulls == null || hulls.Length != 5)
                throw new InvalidOperationException("Hull catalog must contain exactly five hulls.");
            if (!hulls.Select(value => value.HullId).SequenceEqual(ExternalHullOrder))
                throw new InvalidOperationException("External hull order or IDs are incorrect.");
            foreach (ShipHullDefinition hull in hulls)
            {
                if (hull.ModelPrefab == null || hull.Thumbnail == null ||
                    hull.CollisionMesh == null || hull.PlacementSurfaceMesh == null)
                    throw new InvalidOperationException(
                        "Hull is missing a required runtime asset: " + hull.HullId);
                if (hull.CollisionMesh.triangles.Length / 3 > 200)
                    throw new InvalidOperationException("Collision budget exceeded: " + hull.HullId);
                if (hull.PlacementSurfaceMesh.triangles.Length / 3 > 18000)
                    throw new InvalidOperationException("Placement budget exceeded: " + hull.HullId);
                Renderer[] renderers = hull.ModelPrefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0 ||
                    hull.ModelPrefab.GetComponentInChildren<SpacecraftPaintBinding>(true) == null)
                    throw new InvalidOperationException("Hull paint binding is missing: " + hull.HullId);
            }
            if (parts == null || parts.Length != 31 ||
                parts.Any(value => value == null || value.Prefab == null || value.Thumbnail == null))
                throw new InvalidOperationException("All 31 part definitions require prefabs and thumbnails.");
        }

        static void ArchiveLegacyPcgAssets()
        {
            MoveAssetIfPresent(
                "Assets/SpacecraftEditor/Art/Generated/Hulls",
                "Assets/SpacecraftEditor/LegacyPCG/Art/Hulls");
            MoveAssetIfPresent(
                "Assets/SpacecraftEditor/Data/Generated/Hulls",
                "Assets/SpacecraftEditor/LegacyPCG/Data/GeneratedHulls");
            MoveAssetIfPresent(
                "Assets/SpacecraftEditor/Prefabs/Generated/Hulls",
                "Assets/SpacecraftEditor/LegacyPCG/Prefabs/Hulls");

            Directory.CreateDirectory(Path.Combine(
                ProjectRoot(), "Assets/SpacecraftEditor/LegacyPCG/Data/Canonical"));
            foreach (string name in new[]
                     {
                         "hull_balanced.asset", "hull_saucer.asset", "hull_spindle.asset",
                         "HullBalancedCollisionMesh.asset", "HullSaucerCollisionMesh.asset",
                         "HullSpindleCollisionMesh.asset", "HullCollisionMesh.asset"
                     })
                MoveAssetIfPresent(
                    "Assets/SpacecraftEditor/Data/" + name,
                    "Assets/SpacecraftEditor/LegacyPCG/Data/Canonical/" + name);

            Directory.CreateDirectory(Path.Combine(
                ProjectRoot(), "Assets/SpacecraftEditor/LegacyPCG/Thumbnails"));
            foreach (string archetype in new[] { "balanced", "saucer", "spindle" })
            for (int seed = 0; seed < 8; seed++)
            {
                string name = archetype + "_" + seed.ToString("00") + ".png";
                MoveAssetIfPresent(
                    "Assets/SpacecraftEditor/Art/Generated/Thumbnails/" + name,
                    "Assets/SpacecraftEditor/LegacyPCG/Thumbnails/" + name);
            }

            Directory.CreateDirectory(Path.Combine(
                ProjectRoot(), "Assets/SpacecraftEditor/LegacyPCG/PirateHardpoints"));
            string[] guids = AssetDatabase.FindAssets(
                "Hardpoints_hull_ t:SpacecraftHardpointLayout",
                new[] { PirateResourceRoot });
            foreach (string guid in guids)
            {
                string source = AssetDatabase.GUIDToAssetPath(guid);
                string destination =
                    "Assets/SpacecraftEditor/LegacyPCG/PirateHardpoints/" +
                    Path.GetFileName(source);
                MoveAssetIfPresent(source, destination);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static void MoveAssetIfPresent(string source, string destination)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(source)))
                return;
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(destination)))
                return;
            Directory.CreateDirectory(Path.Combine(
                ProjectRoot(), Path.GetDirectoryName(destination)));
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException(
                    "Could not archive " + source + ": " + error);
        }

        static string SafeName(string value)
        {
            return new string(value.Select(character =>
                    char.IsLetterOrDigit(character) ? character : '_')
                .ToArray()).Trim('_');
        }
    }
}
