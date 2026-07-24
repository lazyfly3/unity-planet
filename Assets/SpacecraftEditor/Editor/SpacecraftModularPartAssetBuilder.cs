using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KDL.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpacecraftEditor.Editor
{
    /// <summary>
    /// Publishes reviewed Blender modules into data-driven Unity assets. This has no menu item on purpose;
    /// automation invokes it explicitly after the source FBX files have passed six-view review.
    /// </summary>
    public static class SpacecraftModularPartAssetBuilder
    {
        const string ModelRoot = "Assets/SpacecraftEditor/Art/Models/ModularParts";
        const string IconRoot = "Assets/SpacecraftEditor/Art/Icons/ModularParts";
        const string PrefabRoot = "Assets/SpacecraftEditor/Prefabs/ModularParts";
        const string DataRoot = "Assets/SpacecraftEditor/Data/ModularParts";
        const string PaintRoot = "Assets/SpacecraftEditor/Art/Materials/Paints";
        const string WorkshopPrefab = "Assets/Resources/Spacecraft/SpacecraftWorkshopRoot.prefab";
        const string WorkshopScene = "Assets/Scenes/SpacecraftWorkshop.unity";
        const string InterstellarScene = "Assets/Scenes/InterstellarFlight.unity";
        const string PlanetApproachScene = "Assets/Scenes/PlanetApproach.unity";
        const string SpaceflightResourceRoot = "Assets/Resources/Spaceflight";
        const string PirateResourceRoot = SpaceflightResourceRoot + "/Pirates";
        const string CombatVfxCatalogPath = SpaceflightResourceRoot + "/SpaceCombatVfxCatalog.asset";

        sealed class PartSpec
        {
            public string Id;
            public string Asset;
            public string Label;
            public float Mass;
            public SpacecraftPartCategory Category;
            public SpacecraftPartPlacementMode PlacementMode;
        }

        static readonly PartSpec[] Decorations =
        {
            Decor("decor.swept_wing", "SweptWing", "装甲板", 8f),
            Decor("decor.delta_wing", "DeltaWing", "侧舱模块", 12f),
            Decor("decor.canard", "Canard", "管线组", 4f),
            Decor("decor.vertical_fin", "VerticalFin", "天线阵列", 6f),
            Decor("decor.armor_fairing", "ArmorFairing", "炮塔座", 15f),
            Decor("decor.radiator", "Radiator", "散热片组", 9f),
            Decor("decor.sensor_mast", "SensorMast", "传感器桅杆", 3f),
            Decor("decor.engine_nacelle", "EngineNacelle", "发动机舱", 10f)
        };

        static PartSpec Decor(
            string id,
            string asset,
            string label,
            float mass,
            SpacecraftPartPlacementMode placementMode = SpacecraftPartPlacementMode.SurfaceConforming)
        {
            return new PartSpec
            {
                Id = id,
                Asset = asset,
                Label = label,
                Mass = mass,
                Category = SpacecraftPartCategory.Decoration,
                PlacementMode = placementMode
            };
        }

        public static string BuildAll()
        {
            EnsureFolder(PrefabRoot);
            EnsureFolder(DataRoot);
            EnsureFolder(PaintRoot);
            EnsureFolder(SpaceflightResourceRoot);
            EnsureFolder(PirateResourceRoot);
            ConfigureIcons();

            SpacecraftMaterialDefinition[] paints = BuildPaintLibrary();
            var definitions = new List<ShipPartDefinition>();
            foreach (PartSpec spec in Decorations)
                definitions.Add(BuildDecoration(spec, paints[0].Material));
            definitions.AddRange(BuildWeaponFamily("KineticRepeater", "动能速射炮", true,
                SpaceWeaponMountMode.Fixed, paints[0].Material));
            definitions.AddRange(BuildWeaponFamily("EnergyPulse", "能量脉冲炮", false,
                SpaceWeaponMountMode.Fixed, paints[0].Material));
            definitions.AddRange(BuildWeaponFamily("KineticRepeater", "动能速射炮 云台", true,
                SpaceWeaponMountMode.Gimbaled, paints[0].Material));
            definitions.AddRange(BuildWeaponFamily("EnergyPulse", "能量脉冲炮 云台", false,
                SpaceWeaponMountMode.Gimbaled, paints[0].Material));

            UpdateWorkshopPrefab(definitions.ToArray(), paints);
            UpdateWorkshopScene(definitions.ToArray(), paints);
            UpdateInterstellarScene(definitions.ToArray(), paints);
            UpdateFlightHudScene(PlanetApproachScene);
            BuildCombatVfxCatalog();
            BuildPirateAssets(definitions.ToArray(), paints);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "Published 8 decorations, 12 fixed-size weapon definitions, combat VFX, hardpoints and the PCG pirate base.";
        }

        [MenuItem("Tools/Spacecraft/Rebuild 8 Metal PBR Materials")]
        [AICallable(
            "重建并发布恰好 8 套独立金属 PBR 材质到组装界面、飞行场景和海盗 Prefab。",
            Category = "Spacecraft.Materials",
            Kind = ToolKind.Write)]
        public static string RebuildEightMetalMaterialLibrary()
        {
            SpacecraftSurfaceMaterialBuilder.Rebuild();
            SpacecraftMaterialDefinition[] paints = BuildPaintLibrary();
            UpdateMaterialCatalogPrefab(WorkshopPrefab, paints);
            UpdateMaterialCatalogPrefab(
                PirateResourceRoot + "/ProceduralPirateShip.prefab",
                paints);
            UpdateMaterialCatalogScene(WorkshopScene, paints);
            UpdateMaterialCatalogScene(InterstellarScene, paints);
            UpdateMaterialCatalogScene(PlanetApproachScene, paints);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return "Published exactly 8 independent metal PBR material sets.";
        }

        static void UpdateMaterialCatalogPrefab(
            string path,
            SpacecraftMaterialDefinition[] paints)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                return;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SpacecraftMaterialCatalog catalog =
                    root.GetComponentInChildren<SpacecraftMaterialCatalog>(true);
                if (catalog == null)
                    return;
                catalog.Configure(paints, "paint.deep_space_blue");
                EditorUtility.SetDirty(catalog);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void UpdateMaterialCatalogScene(
            string path,
            SpacecraftMaterialDefinition[] paints)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                return;
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedForBuild = !scene.IsValid() || !scene.isLoaded;
            if (!openedForBuild && scene.isDirty)
            {
                throw new InvalidOperationException(
                    "Cannot rebuild material catalog while the scene has unsaved changes: " + path);
            }
            if (openedForBuild)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                SpacecraftMaterialCatalog catalog =
                    FindInScene<SpacecraftMaterialCatalog>(scene);
                if (catalog == null)
                    return;
                catalog.Configure(paints, "paint.deep_space_blue");
                EditorUtility.SetDirty(catalog);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedForBuild)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Tools/Spacecraft/Rebuild Unified Flight HUD %#h")]
        public static string RebuildFlightHudScenes()
        {
            SpaceflightCockpitAssetBuilder.BuildAssets();
            UpdateFlightHudScene(InterstellarScene);
            UpdateFlightHudScene(PlanetApproachScene);
            AssetDatabase.SaveAssets();
            const string message =
                "Rebuilt the unified flight HUD and cockpit integration in InterstellarFlight and PlanetApproach.";
            Debug.Log(message);
            return message;
        }

        [MenuItem("Tools/Spacecraft/Ensure Interstellar Dual Scale Scene")]
        [AICallable(
            "Create and save the astronomical kilometre layer, local metre physics bubble, and astronomical camera in InterstellarFlight without touching the active scene.",
            Category = "Spacecraft.Build",
            Kind = ToolKind.Write)]
        public static string EnsureInterstellarDualScaleScene()
        {
            Scene scene = SceneManager.GetSceneByPath(InterstellarScene);
            bool openedForBuild = !scene.IsValid() || !scene.isLoaded;
            if (!openedForBuild && scene.isDirty)
            {
                return "InterstellarFlight is open with unsaved changes; "
                    + "the runtime fallback remains active and the scene was not overwritten.";
            }
            if (openedForBuild)
                scene = EditorSceneManager.OpenScene(InterstellarScene, OpenSceneMode.Additive);
            try
            {
                EnsureSpaceScaleHierarchy(scene);
                EditorSceneManager.SaveScene(scene);
                return "InterstellarFlight dual-scale hierarchy saved.";
            }
            finally
            {
                if (openedForBuild)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [AICallable(
            "Validate the saved InterstellarFlight dual-scale roots, layers, camera, and 1U=1km scale without entering Play Mode.",
            Category = "Spacecraft.Build",
            Kind = ToolKind.Read)]
        public static string ValidateInterstellarDualScaleScene()
        {
            Scene scene = SceneManager.GetSceneByPath(InterstellarScene);
            bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
            if (openedForValidation)
                scene = EditorSceneManager.OpenScene(InterstellarScene, OpenSceneMode.Additive);
            try
            {
                GameObject astronomicalRoot = FindNamedObjectInScene(
                    scene,
                    "AstronomicalRoot");
                GameObject physicsRoot = FindNamedObjectInScene(
                    scene,
                    "SpacePhysicsBubble");
                GameObject planetRoot = FindNamedObjectInScene(
                    scene,
                    "PlanetRuntimeRoot");
                GameObject astronomicalCamera = FindNamedObjectInScene(
                    scene,
                    "AstronomicalCamera");
                if (astronomicalRoot == null
                    || physicsRoot == null
                    || planetRoot == null
                    || astronomicalCamera == null)
                {
                    return "Dual-scale validation failed: a required scene object is missing.";
                }
                int kilometerLayer = LayerMask.NameToLayer("SpaceKilometerView");
                int physicsLayer = LayerMask.NameToLayer("SpacePhysicsBubble");
                if (astronomicalRoot.layer != kilometerLayer
                    || planetRoot.layer != kilometerLayer
                    || astronomicalCamera.layer != kilometerLayer
                    || physicsRoot.layer != physicsLayer)
                {
                    return "Dual-scale validation failed: one or more layers are incorrect.";
                }
                if (planetRoot.transform.parent != astronomicalRoot.transform)
                {
                    return "Dual-scale validation failed: PlanetRuntimeRoot is not under AstronomicalRoot.";
                }
                Camera camera = astronomicalCamera.GetComponent<Camera>();
                AstronomicalCameraSynchronizer synchronizer =
                    astronomicalCamera.GetComponent<AstronomicalCameraSynchronizer>();
                SpaceKilometerScaleValidator validator =
                    astronomicalRoot.GetComponent<SpaceKilometerScaleValidator>();
                if (camera == null || synchronizer == null || validator == null)
                {
                    return "Dual-scale validation failed: a required component is missing.";
                }
                if (!validator.Validate(out string scaleError))
                    return "Dual-scale validation failed: " + scaleError;
                if (!Mathf.Approximately(
                        SpaceKilometerScale.ToKilometerUnits(1000f),
                        1f)
                    || !Mathf.Approximately(
                        (float)SpaceKilometerScale.ToKilometerUnitsPerSecond(250d),
                        0.25f)
                    || !Mathf.Approximately(
                        (float)SpaceKilometerScale.ToKilometerUnitsPerSecondSquared(6d),
                        0.006f)
                    || !InterstellarFlightRuntime.ShouldEnterHighSpeed(1000f)
                    || InterstellarFlightRuntime.ShouldEnterHighSpeed(999f)
                    || !InterstellarFlightRuntime.CanReturnToTactical(600f)
                    || InterstellarFlightRuntime.CanReturnToTactical(601f))
                {
                    return "Dual-scale validation failed: a conversion or hysteresis invariant changed.";
                }
                return "Dual-scale validation passed: 1000m=1U, exact range 100000km, "
                    + "high-speed hysteresis 1000/600m/s.";
            }
            finally
            {
                if (openedForValidation)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Tools/Spacecraft/Preview Unified Flight HUD %#j")]
        public static void PreviewUnifiedFlightHud()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(InterstellarScene, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        static SpacecraftMaterialDefinition[] BuildPaintLibrary()
        {
            string[] ids =
            {
                "paint.deep_space_blue", "paint.gunmetal", "paint.ceramic_white",
                "paint.warning_red", "paint.industrial_copper", "paint.explorer_green",
                "paint.brushed_brass", "paint.graphite_pitted"
            };
            string[] labels =
            {
                "深空钛蓝", "喷砂枪灰", "航空拉丝铝", "红色镀锌钢",
                "氧化工业铜", "绿色军用钢", "拉丝黄铜", "蚀刻石墨金属"
            };
            Color[] colors =
            {
                new Color(0.18f, 0.42f, 0.70f),
                new Color(0.32f, 0.35f, 0.38f),
                new Color(0.76f, 0.80f, 0.82f),
                new Color(0.66f, 0.15f, 0.12f),
                new Color(0.63f, 0.29f, 0.10f),
                new Color(0.18f, 0.42f, 0.28f),
                new Color(0.67f, 0.48f, 0.14f),
                new Color(0.18f, 0.20f, 0.22f)
            };

            var result = new SpacecraftMaterialDefinition[ids.Length];
            Shader shader = Shader.Find("Standard");
            for (int index = 0; index < ids.Length; index++)
            {
                string path = PaintRoot + "/" + ids[index].Replace("paint.", string.Empty) + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = labels[index] };
                    AssetDatabase.CreateAsset(material, path);
                }
                var definition = new SpacecraftMaterialDefinition();
                definition.Configure(ids[index], labels[index], material, colors[index]);
                result[index] = definition;
            }
            return result;
        }

        static ShipPartDefinition BuildDecoration(PartSpec spec, Material defaultPaint)
        {
            GameObject prefab = BuildPartPrefab(spec.Asset, false, defaultPaint);
            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconRoot + "/" + spec.Asset + ".png");
            ShipPartDefinition definition = LoadOrCreateDefinition(DataRoot + "/" + spec.Id.Replace('.', '_') + ".asset");
            definition.ConfigureGeneral(spec.Id, spec.Label, prefab, icon, spec.Category, spec.Mass,
                SpacecraftPartScaleMode.Free, 1f, "paint.deep_space_blue", null, spec.PlacementMode);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static IEnumerable<ShipPartDefinition> BuildWeaponFamily(
            string asset,
            string label,
            bool kinetic,
            SpaceWeaponMountMode mountMode,
            Material defaultPaint)
        {
            GameObject prefab = BuildPartPrefab(asset, true, defaultPaint);
            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconRoot + "/" + asset + ".png");
            float[] scales = { 0.72f, 1f, 1.3f };
            float[] masses = kinetic ? new[] { 9f, 18f, 31f } : new[] { 8f, 16f, 28f };
            for (int index = 0; index < 3; index++)
            {
                var size = (SpaceWeaponMountSize)(index + 1);
                float power = 1f + index * 0.55f;
                var weapon = new SpacecraftWeaponDefinition();
                if (kinetic)
                {
                    weapon.Configure(SpaceWeaponDamageChannel.Kinetic, SpaceWeaponFireMode.Repeater, size, 1,
                        8f - index * 0.8f, 7f * power, 520f, 1800f, 0.38f - index * 0.06f,
                        300 - index * 50, 0f, 0.038f * power, 0.24f, 0.48f * power, 5f * power,
                        mountMode, 18f, 120f);
                }
                else
                {
                    weapon.Configure(SpaceWeaponDamageChannel.Energy, SpaceWeaponFireMode.Cannon, size, 2,
                        5f - index * 0.35f, 11f * power, 760f, 1900f, 0.16f,
                        0, 4f * power, 0.062f * power, 0.27f, 0.30f * power, 3.5f * power,
                        mountMode, 18f, 120f);
                }

                string id = "weapon." + (kinetic ? "kinetic_repeater" : "energy_pulse") +
                            (mountMode == SpaceWeaponMountMode.Gimbaled ? ".gimbal" : string.Empty) +
                            ".s" + (index + 1);
                ShipPartDefinition definition = LoadOrCreateDefinition(DataRoot + "/" + id.Replace('.', '_') + ".asset");
                definition.ConfigureGeneral(id, label + " S" + (index + 1), prefab, icon,
                    SpacecraftPartCategory.Weapon, masses[index], SpacecraftPartScaleMode.Fixed, scales[index],
                    "paint.deep_space_blue", weapon);
                EditorUtility.SetDirty(definition);
                yield return definition;
            }
        }

        static GameObject BuildPartPrefab(string assetName, bool weapon, Material defaultPaint)
        {
            string prefabPath = PrefabRoot + "/" + assetName + ".prefab";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelRoot + "/" + assetName + ".fbx");
            if (source == null)
                throw new InvalidOperationException("Missing reviewed model: " + assetName);

            var root = new GameObject(assetName);
            if (weapon)
                root.AddComponent<WeaponPart>();
            else
                root.AddComponent<DecorationPart>();

            GameObject model = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (model == null)
                throw new InvalidOperationException("Could not instantiate model: " + assetName);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            // Blender authored +Z as mount normal and +Y as ship forward. Standard FBX import maps
            // them to Unity +Y and -Z; this wrapper restores the runtime +Z/+Y convention.
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Transform gimbalPivot = null;
            if (weapon)
            {
                gimbalPivot = new GameObject("GimbalPivot").transform;
                gimbalPivot.SetParent(root.transform, false);
                gimbalPivot.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                model.transform.SetParent(gimbalPivot, true);
            }

            Material structure = AssetDatabase.LoadAssetAtPath<Material>("Assets/SpacecraftEditor/Art/Materials/DarkStructure.mat");
            Material accent = AssetDatabase.LoadAssetAtPath<Material>("Assets/SpacecraftEditor/Art/Materials/BrushedSteel.mat");
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                string lower = renderer.name.ToLowerInvariant();
                bool isStructure = lower.Contains("structure") || lower.Contains("barrel") ||
                                   lower.Contains("breech") || lower.Contains("mount") ||
                                   lower.Contains("frame");
                bool isAccent = lower.Contains("accent") || lower.Contains("coil") ||
                                lower.Contains("emitter") || lower.Contains("glow");
                renderer.sharedMaterial = isStructure ? structure : isAccent ? accent : defaultPaint;
            }

            if (weapon)
            {
                Transform sourceMuzzle = model.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(candidate => candidate.name.Equals("Muzzle", StringComparison.OrdinalIgnoreCase));
                Vector3 muzzlePosition = sourceMuzzle == null
                    ? new Vector3(0f, 2f, 0.35f)
                    : root.transform.InverseTransformPoint(sourceMuzzle.position);
                if (sourceMuzzle != null)
                    sourceMuzzle.name = "AuthoringMuzzle";
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(gimbalPivot, false);
                muzzle.position = root.transform.TransformPoint(muzzlePosition);
                muzzle.localRotation = Quaternion.identity;
            }

            Bounds bounds = CalculateLocalBounds(root.transform, model.GetComponentsInChildren<Renderer>(true));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
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
                    Vector3 point = world.center + Vector3.Scale(world.extents, new Vector3(x, y, z));
                    point = root.InverseTransformPoint(point);
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

        static ShipPartDefinition LoadOrCreateDefinition(string path)
        {
            ShipPartDefinition definition = AssetDatabase.LoadAssetAtPath<ShipPartDefinition>(path);
            if (definition != null)
                return definition;
            definition = ScriptableObject.CreateInstance<ShipPartDefinition>();
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        static void UpdateWorkshopPrefab(ShipPartDefinition[] added, SpacecraftMaterialDefinition[] paints)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WorkshopPrefab);
            try
            {
                PartCatalog catalog = root.GetComponentInChildren<PartCatalog>(true);
                if (catalog == null)
                    throw new InvalidOperationException("Workshop prefab has no PartCatalog.");
                catalog.Configure(MergeCatalog(catalog.Definitions, added));
                SpacecraftMaterialCatalog materialCatalog = root.GetComponentInChildren<SpacecraftMaterialCatalog>(true);
                if (materialCatalog == null)
                    materialCatalog = catalog.gameObject.AddComponent<SpacecraftMaterialCatalog>();
                materialCatalog.Configure(paints, "paint.deep_space_blue");
                BuildWorkshopUi(root);
                PrefabUtility.SaveAsPrefabAsset(root, WorkshopPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void UpdateWorkshopScene(ShipPartDefinition[] added, SpacecraftMaterialDefinition[] paints)
        {
            Scene scene = SceneManager.GetSceneByPath(WorkshopScene);
            bool openedForBuild = !scene.IsValid() || !scene.isLoaded;
            if (openedForBuild)
                scene = EditorSceneManager.OpenScene(WorkshopScene, OpenSceneMode.Additive);

            try
            {
                PartCatalog catalog = FindInScene<PartCatalog>(scene);
                if (catalog == null)
                    throw new InvalidOperationException("Workshop scene has no PartCatalog.");

                catalog.Configure(MergeCatalog(catalog.Definitions, added));
                SpacecraftMaterialCatalog materialCatalog = FindInScene<SpacecraftMaterialCatalog>(scene);
                if (materialCatalog == null)
                    materialCatalog = catalog.gameObject.AddComponent<SpacecraftMaterialCatalog>();
                materialCatalog.Configure(paints, "paint.deep_space_blue");

                WorkshopUIReferences references = FindInScene<WorkshopUIReferences>(scene);
                if (references == null)
                    throw new InvalidOperationException("Workshop scene has no WorkshopUIReferences.");
                BuildWorkshopUi(references.transform.root.gameObject);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedForBuild)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void UpdateInterstellarScene(ShipPartDefinition[] added, SpacecraftMaterialDefinition[] paints)
        {
            Scene scene = EditorSceneManager.OpenScene(InterstellarScene, OpenSceneMode.Additive);
            try
            {
                PartCatalog catalog = FindInScene<PartCatalog>(scene);
                if (catalog == null)
                {
                    var catalogs = new GameObject("SpacecraftCatalogs");
                    SceneManager.MoveGameObjectToScene(catalogs, scene);
                    catalog = catalogs.AddComponent<PartCatalog>();
                }
                catalog.Configure(MergeCatalog(catalog.Definitions, added));
                SpacecraftMaterialCatalog materialCatalog = FindInScene<SpacecraftMaterialCatalog>(scene);
                if (materialCatalog == null)
                    materialCatalog = catalog.gameObject.AddComponent<SpacecraftMaterialCatalog>();
                materialCatalog.Configure(paints, "paint.deep_space_blue");

                InterstellarShipController controller = FindInScene<InterstellarShipController>(scene);
                if (controller != null && controller.GetComponent<SpacecraftWeaponSystem>() == null)
                    controller.gameObject.AddComponent<SpacecraftWeaponSystem>();
                InterstellarWarpGateController warpGate =
                    FindInScene<InterstellarWarpGateController>(scene);
                if (warpGate == null)
                {
                    var gateSystem = new GameObject("WarpGateSystem");
                    SceneManager.MoveGameObjectToScene(gateSystem, scene);
                    gateSystem.AddComponent<InterstellarWarpGateController>();
                }
                PersistentSpaceflightFade transition =
                    FindInScene<PersistentSpaceflightFade>(scene);
                if (transition == null)
                {
                    var transitionObject =
                        new GameObject("SpaceflightTransitionOverlay");
                    SceneManager.MoveGameObjectToScene(transitionObject, scene);
                    transitionObject.AddComponent<PersistentSpaceflightFade>();
                }
                InterstellarCruiseController cruise =
                    FindInScene<InterstellarCruiseController>(scene);
                if (cruise != null)
                {
                    var cruiseSettings = new SerializedObject(cruise);
                    cruiseSettings.FindProperty("alignmentDuration").floatValue = 0.65f;
                    cruiseSettings.FindProperty("spoolDuration").floatValue = 1.15f;
                    cruiseSettings.FindProperty("transitDuration").floatValue = 0.9f;
                    cruiseSettings.FindProperty("exitDuration").floatValue = 0.65f;
                    cruiseSettings.FindProperty("cooldownDuration").floatValue = 0.6f;
                    cruiseSettings.ApplyModifiedPropertiesWithoutUndo();
                }
                EnsureSpaceScaleHierarchy(scene);
                BuildInterstellarWeaponHud(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void UpdateFlightHudScene(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedForBuild = !scene.IsValid() || !scene.isLoaded;
            if (openedForBuild)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                if (string.Equals(scenePath, InterstellarScene, StringComparison.Ordinal))
                    EnsureSpaceScaleHierarchy(scene);
                BuildInterstellarWeaponHud(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedForBuild)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void BuildCombatVfxCatalog()
        {
            SpaceCombatVfxCatalog catalog = AssetDatabase.LoadAssetAtPath<SpaceCombatVfxCatalog>(CombatVfxCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SpaceCombatVfxCatalog>();
                AssetDatabase.CreateAsset(catalog, CombatVfxCatalogPath);
            }

            const string effects = "Assets/FORGE3D/Sci-Fi Effects/Effects/";
            const string sounds = "Assets/FORGE3D/Sci-Fi Effects/Sounds/";
            var kinetic = new SpaceWeaponVfxDefinition();
            kinetic.Configure(
                SpaceWeaponDamageChannel.Kinetic,
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Vulcan/vulcan_muzzle.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Vulcan/vulcan_projectile.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Vulcan/vulcan_impact.prefab"),
                AssetDatabase.LoadAssetAtPath<AudioClip>(sounds + "weapon_gatling_006.wav"),
                null,
                1.4f,
                new Vector3(0.85f, 0.85f, 3.2f),
                1.35f,
                0.22f);
            var energy = new SpaceWeaponVfxDefinition();
            energy.Configure(
                SpaceWeaponDamageChannel.Energy,
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Plasma Gun/plasma_gun_muzzle_flash.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Plasma Gun/plasma_gun_projectile_001.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Plasma Gun/plasma_gun_flare.prefab"),
                AssetDatabase.LoadAssetAtPath<AudioClip>(sounds + "weapon_laser_006.wav"),
                null,
                1.65f,
                new Vector3(1.1f, 1.1f, 2.6f),
                1.6f,
                0.3f);
            catalog.Configure(
                new[] { kinetic, energy },
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Explosions/Explosion_001.prefab"),
                AssetDatabase.LoadAssetAtPath<GameObject>(effects + "Explosions/Explosion_003.prefab"));
            EditorUtility.SetDirty(catalog);
        }

        static void BuildPirateAssets(
            ShipPartDefinition[] publishedParts,
            SpacecraftMaterialDefinition[] paints)
        {
            ShipHullDefinition[] hulls = AssetDatabase.FindAssets("t:ShipHullDefinition", new[] { "Assets/SpacecraftEditor/Data" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShipHullDefinition>)
                .Where(value => value != null)
                .OrderBy(value => value.HullId, StringComparer.Ordinal)
                .ToArray();
            ShipPartDefinition[] thrusters = AssetDatabase.FindAssets("t:ShipPartDefinition", new[] { "Assets/SpacecraftEditor/Data" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ShipPartDefinition>)
                .Where(value => value != null && value.Category == SpacecraftPartCategory.Thruster)
                .ToArray();
            ShipPartDefinition[] allParts = thrusters.Concat(publishedParts)
                .GroupBy(value => value.PartId, StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(value => value.Category)
                .ThenBy(value => value.PartId, StringComparer.Ordinal)
                .ToArray();
            SpacecraftHardpointLayout[] layouts = hulls.Select(BuildHardpointLayout).ToArray();
            BuildPirateShipPrefab(hulls, allParts, paints, layouts);
            BuildPirateEncounterPrototype();
            UpdatePirateEncounterScene();
        }

        static SpacecraftHardpointLayout BuildHardpointLayout(ShipHullDefinition hull)
        {
            string safeId = hull.HullId.Replace('.', '_');
            string path = PirateResourceRoot + "/Hardpoints_" + safeId + ".asset";
            SpacecraftHardpointLayout layout = AssetDatabase.LoadAssetAtPath<SpacecraftHardpointLayout>(path);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<SpacecraftHardpointLayout>();
                AssetDatabase.CreateAsset(layout, path);
            }

            Vector3 size = hull.Dimensions;
            var points = new[]
            {
                Hardpoint("engine_outer_l", "engine_outer_r", SpacecraftPartCategory.Thruster,
                    new Vector3(-size.x * 0.28f, 0f, size.z * 0.43f), new Vector3(0f, 180f, 0f)),
                Hardpoint("engine_outer_r", "engine_outer_l", SpacecraftPartCategory.Thruster,
                    new Vector3(size.x * 0.28f, 0f, size.z * 0.43f), new Vector3(0f, 180f, 0f)),
                Hardpoint("engine_upper_l", "engine_upper_r", SpacecraftPartCategory.Thruster,
                    new Vector3(-size.x * 0.16f, size.y * 0.28f, size.z * 0.45f), new Vector3(0f, 180f, 0f)),
                Hardpoint("engine_upper_r", "engine_upper_l", SpacecraftPartCategory.Thruster,
                    new Vector3(size.x * 0.16f, size.y * 0.28f, size.z * 0.45f), new Vector3(0f, 180f, 0f)),
                Hardpoint("weapon_dorsal_l", "weapon_dorsal_r", SpacecraftPartCategory.Weapon,
                    new Vector3(-size.x * 0.34f, size.y * 0.46f, size.z * 0.08f), new Vector3(90f, 0f, 0f)),
                Hardpoint("weapon_dorsal_r", "weapon_dorsal_l", SpacecraftPartCategory.Weapon,
                    new Vector3(size.x * 0.34f, size.y * 0.46f, size.z * 0.08f), new Vector3(90f, 0f, 0f)),
                Hardpoint("weapon_side_l", "weapon_side_r", SpacecraftPartCategory.Weapon,
                    new Vector3(-size.x * 0.48f, 0f, size.z * 0.12f), new Vector3(90f, 0f, 0f)),
                Hardpoint("weapon_side_r", "weapon_side_l", SpacecraftPartCategory.Weapon,
                    new Vector3(size.x * 0.48f, 0f, size.z * 0.12f), new Vector3(90f, 0f, 0f))
            };
            var dorsal = new SpacecraftDecorationRegion();
            dorsal.Configure("dorsal", Vector3.zero,
                new Vector3(size.x * 0.38f, size.y * 0.48f, size.z * 0.36f), Vector3.zero);
            var lateral = new SpacecraftDecorationRegion();
            lateral.Configure("lateral", new Vector3(0f, 0f, -size.z * 0.12f),
                new Vector3(size.x * 0.48f, size.y * 0.30f, size.z * 0.28f), new Vector3(-90f, 0f, 0f));
            layout.Configure(hull.HullId, points, new[] { dorsal, lateral }, 0.55f, 0.045f);
            EditorUtility.SetDirty(layout);
            return layout;
        }

        static SpacecraftHardpoint Hardpoint(
            string id,
            string mirror,
            SpacecraftPartCategory category,
            Vector3 position,
            Vector3 euler)
        {
            var result = new SpacecraftHardpoint();
            result.Configure(id, mirror, category, position, euler,
                SpaceWeaponMountSize.S1, SpaceWeaponMountSize.S3, true, true);
            return result;
        }

        static void BuildPirateShipPrefab(
            ShipHullDefinition[] hulls,
            ShipPartDefinition[] parts,
            SpacecraftMaterialDefinition[] paints,
            SpacecraftHardpointLayout[] layouts)
        {
            const string path = PirateResourceRoot + "/ProceduralPirateShip.prefab";
            var root = new GameObject("ProceduralPirateShip");
            var body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.drag = 0f;
            body.angularDrag = 0.05f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Transform hullVisual = new GameObject("Hull").transform;
            hullVisual.SetParent(root.transform, false);
            Transform partsRoot = new GameObject("Parts").transform;
            partsRoot.SetParent(root.transform, false);
            Transform catalogs = new GameObject("Catalogs").transform;
            catalogs.SetParent(root.transform, false);
            var hullCatalog = catalogs.gameObject.AddComponent<HullCatalog>();
            hullCatalog.Configure(hulls);
            var partCatalog = catalogs.gameObject.AddComponent<PartCatalog>();
            partCatalog.Configure(parts);
            var materialCatalog = catalogs.gameObject.AddComponent<SpacecraftMaterialCatalog>();
            materialCatalog.Configure(paints, "paint.deep_space_blue");

            var collider = root.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.enabled = false;
            var hullController = root.AddComponent<ShipHullController>();
            Material mainPaint = paints.Length == 0 ? null : paints[0].Material;
            Material accent = AssetDatabase.LoadAssetAtPath<Material>("Assets/SpacecraftEditor/Art/Materials/BrushedSteel.mat");
            hullController.Configure(hullVisual, collider, mainPaint, accent);
            var assembly = root.AddComponent<ShipAssembly>();
            assembly.Configure(body, partsRoot, partCatalog, hulls.Length == 0 ? 12000f : hulls[0].BaseMass);
            var command = root.AddComponent<PirateCommandBuffer>();
            var ifcs = root.AddComponent<SpacecraftIfcsMotor>();
            ifcs.Configure(body, assembly, hullController, command, false);
            var weapons = root.AddComponent<SpacecraftWeaponSystem>();
            weapons.SetCommandSource(command);
            weapons.SetDamageMultiplier(0.05f);
            root.AddComponent<SpacecraftDamageReceiver>();
            Transform aimPoint = new GameObject("AimPoint").transform;
            aimPoint.SetParent(root.transform, false);
            var combatant = root.AddComponent<SpaceCombatant>();
            combatant.Configure(SpaceCombatFaction.Pirate, aimPoint, true);
            var generator = root.AddComponent<ProceduralPirateShipGenerator>();
            root.AddComponent<PirateShipAiController>();
            root.AddComponent<PirateShipLifecycle>();
            var serialized = new SerializedObject(generator);
            SetObjectReference(serialized, "hullCatalog", hullCatalog);
            SetObjectReference(serialized, "partCatalog", partCatalog);
            SetObjectReference(serialized, "materialCatalog", materialCatalog);
            SetObjectReference(serialized, "hullController", hullController);
            SetObjectReference(serialized, "assembly", assembly);
            SetObjectReference(serialized, "ifcsMotor", ifcs);
            SetObjectReference(serialized, "weaponSystem", weapons);
            SetObjectReference(serialized, "commandBuffer", command);
            SerializedProperty layoutProperty = serialized.FindProperty("layouts");
            layoutProperty.arraySize = layouts.Length;
            for (int index = 0; index < layouts.Length; index++)
                layoutProperty.GetArrayElementAtIndex(index).objectReferenceValue = layouts[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        static void BuildPirateEncounterPrototype()
        {
            const string path = PirateResourceRoot + "/PirateEncounterPrototype.prefab";
            var root = new GameObject("PirateEncounterPrototype");
            root.AddComponent<PirateEncounterDirector>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        static void UpdatePirateEncounterScene()
        {
            Scene scene = EditorSceneManager.OpenScene(InterstellarScene, OpenSceneMode.Additive);
            try
            {
                GameObject systems = null;
                GameObject runtimeRoot = null;
                InterstellarFlightRuntime runtime = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == "EnemySystems")
                        systems = root;
                    else if (root.name == "EnemyRuntimeRoot")
                        runtimeRoot = root;
                    if (runtime == null)
                        runtime = root.GetComponentInChildren<InterstellarFlightRuntime>(true);
                }

                if (systems == null)
                {
                    systems = new GameObject("EnemySystems");
                    SceneManager.MoveGameObjectToScene(systems, scene);
                }
                if (runtimeRoot == null)
                {
                    runtimeRoot = new GameObject("EnemyRuntimeRoot");
                    SceneManager.MoveGameObjectToScene(runtimeRoot, scene);
                }

                PirateEncounterDirector director = systems.GetComponent<PirateEncounterDirector>() ??
                                                   systems.AddComponent<PirateEncounterDirector>();
                director.enabled = true;
                var serialized = new SerializedObject(director);
                serialized.FindProperty("encountersEnabled").boolValue = true;
                serialized.FindProperty("pirateShipPrefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PirateResourceRoot + "/ProceduralPirateShip.prefab");
                serialized.FindProperty("flightRuntime").objectReferenceValue = runtime;
                serialized.FindProperty("enemyRoot").objectReferenceValue = runtimeRoot.transform;
                serialized.FindProperty("firstSpawnMinimumDistance").floatValue = 65f;
                serialized.FindProperty("firstSpawnMaximumDistance").floatValue = 85f;
                serialized.FindProperty("minimumSpawnDistance").floatValue = 80f;
                serialized.FindProperty("maximumSpawnDistance").floatValue = 120f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void EnsureSpaceScaleHierarchy(Scene scene)
        {
            int kilometerLayer = LayerMask.NameToLayer("SpaceKilometerView");
            int physicsLayer = LayerMask.NameToLayer("SpacePhysicsBubble");

            GameObject astronomicalRoot = null;
            GameObject physicsRoot = null;
            GameObject planetRoot = null;
            GameObject astronomicalCamera = null;
            astronomicalRoot = FindNamedObjectInScene(scene, "AstronomicalRoot");
            physicsRoot = FindNamedObjectInScene(scene, "SpacePhysicsBubble");
            planetRoot = FindNamedObjectInScene(scene, "PlanetRuntimeRoot");
            astronomicalCamera = FindNamedObjectInScene(scene, "AstronomicalCamera");

            if (astronomicalRoot == null)
            {
                astronomicalRoot = new GameObject("AstronomicalRoot");
                SceneManager.MoveGameObjectToScene(astronomicalRoot, scene);
            }
            if (kilometerLayer >= 0)
                astronomicalRoot.layer = kilometerLayer;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(astronomicalRoot);
            if (astronomicalRoot.GetComponent<SpaceKilometerScaleValidator>() == null)
                astronomicalRoot.AddComponent<SpaceKilometerScaleValidator>();

            if (planetRoot != null && planetRoot.transform.parent != astronomicalRoot.transform)
            {
                planetRoot.transform.SetParent(astronomicalRoot.transform, false);
                planetRoot.transform.localPosition = Vector3.zero;
                planetRoot.transform.localRotation = Quaternion.identity;
                planetRoot.transform.localScale = Vector3.one;
                if (kilometerLayer >= 0)
                    planetRoot.layer = kilometerLayer;
            }

            if (physicsRoot == null)
            {
                physicsRoot = new GameObject("SpacePhysicsBubble");
                SceneManager.MoveGameObjectToScene(physicsRoot, scene);
            }
            if (physicsLayer >= 0)
                physicsRoot.layer = physicsLayer;

            if (astronomicalCamera == null)
            {
                astronomicalCamera = new GameObject("AstronomicalCamera");
                SceneManager.MoveGameObjectToScene(astronomicalCamera, scene);
            }
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(astronomicalCamera);
            Camera camera = astronomicalCamera.GetComponent<Camera>()
                ?? astronomicalCamera.AddComponent<Camera>();
            camera.enabled = false;
            if (astronomicalCamera.GetComponent<AstronomicalCameraSynchronizer>() == null)
                astronomicalCamera.AddComponent<AstronomicalCameraSynchronizer>();
            if (kilometerLayer >= 0)
                astronomicalCamera.layer = kilometerLayer;
        }

        static GameObject FindNamedObjectInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameObject match = FindNamedObjectInHierarchy(root, objectName);
                if (match != null)
                    return match;
            }
            return null;
        }

        static GameObject FindNamedObjectInHierarchy(
            GameObject candidate,
            string objectName)
        {
            if (candidate == null)
                return null;
            if (string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                return candidate;
            Transform root = candidate.transform;
            for (int index = 0; index < root.childCount; index++)
            {
                GameObject match = FindNamedObjectInHierarchy(
                    root.GetChild(index).gameObject,
                    objectName);
                if (match != null)
                    return match;
            }
            return null;
        }

        static ShipPartDefinition[] MergeCatalog(
            IReadOnlyList<ShipPartDefinition> current,
            ShipPartDefinition[] added)
        {
            var values = new Dictionary<string, ShipPartDefinition>(StringComparer.Ordinal);
            if (current != null)
            {
                for (int index = 0; index < current.Count; index++)
                {
                    ShipPartDefinition definition = current[index];
                    if (definition != null && definition.Category == SpacecraftPartCategory.Thruster)
                        values[definition.PartId] = definition;
                }
            }
            foreach (ShipPartDefinition definition in added)
                values[definition.PartId] = definition;
            return values.Values
                .OrderBy(value => value.Category)
                .ThenBy(value => value.PartId, StringComparer.Ordinal)
                .ToArray();
        }

        static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }

        static void ConfigureIcons()
        {
            foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { IconRoot })
                         .Select(AssetDatabase.GUIDToAssetPath))
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }

        static void BuildWorkshopUi(GameObject root)
        {
            WorkshopUIReferences refs = root.GetComponentInChildren<WorkshopUIReferences>(true);
            if (refs == null || refs.PartCardRoot == null)
                return;
            Transform panel = FindAncestor(refs.PartCardRoot, "PartLibraryPanel") ?? refs.PartCardRoot.parent;
            Font font = refs.SelectionText == null ? null : refs.SelectionText.font;
            refs.DecorationTabButton = CreateOrUpdateButton(panel, "DecorationTab", "装饰品", new Vector2(16f, -108f), new Vector2(104f, 34f), font);
            refs.ThrusterTabButton = CreateOrUpdateButton(panel, "ThrusterTab", "推进器", new Vector2(128f, -108f), new Vector2(104f, 34f), font);
            refs.WeaponTabButton = CreateOrUpdateButton(panel, "WeaponTab", "武器", new Vector2(240f, -108f), new Vector2(104f, 34f), font);

            RectTransform partCards = refs.PartCardRoot;
            Transform inspector = refs.SelectionText == null ? panel : refs.SelectionText.transform.parent;
            RectTransform scrollView = FindOrCreateRect(panel, "PartScrollView");
            scrollView.anchorMin = Vector2.zero;
            scrollView.anchorMax = Vector2.one;
            scrollView.pivot = new Vector2(0.5f, 0.5f);
            scrollView.offsetMin = new Vector2(12f, 310f);
            scrollView.offsetMax = new Vector2(-12f, -150f);
            scrollView.SetSiblingIndex(inspector.GetSiblingIndex());

            ScrollRect scrollRect = scrollView.GetComponent<ScrollRect>();
            if (scrollRect == null)
                scrollRect = scrollView.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = FindOrCreateRect(scrollView, "Viewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            if (viewport.GetComponent<RectMask2D>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();

            partCards.SetParent(viewport, false);
            partCards.anchorMin = new Vector2(0f, 1f);
            partCards.anchorMax = new Vector2(1f, 1f);
            partCards.pivot = new Vector2(0.5f, 1f);
            partCards.anchoredPosition = Vector2.zero;
            partCards.sizeDelta = Vector2.zero;
            ContentSizeFitter fitter = partCards.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = partCards.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = partCards;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 36f;

            RectTransform swatches = FindOrCreateRect(inspector, "MaterialSwatches");
            swatches.anchorMin = new Vector2(0f, 1f);
            swatches.anchorMax = new Vector2(0f, 1f);
            swatches.pivot = new Vector2(0f, 1f);
            swatches.anchoredPosition = new Vector2(18f, -194f);
            swatches.sizeDelta = new Vector2(238f, 28f);
            var layout = swatches.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
                layout = swatches.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;

            Button swatchTemplate = swatches.Find("SwatchTemplate") == null
                ? CreateOrUpdateButton(swatches, "SwatchTemplate", string.Empty, Vector2.zero, new Vector2(28f, 28f), font)
                : swatches.Find("SwatchTemplate").GetComponent<Button>();
            swatchTemplate.GetComponent<RectTransform>().sizeDelta = new Vector2(28f, 28f);
            swatchTemplate.gameObject.SetActive(false);
            refs.MaterialSwatchRoot = swatches;
            refs.MaterialSwatchTemplate = swatchTemplate;

            refs.WeaponGroup1Button = CreateOrUpdateButton(inspector, "WeaponGroup1", "武器组 1", new Vector2(18f, -159f), new Vector2(112f, 30f), font);
            refs.WeaponGroup2Button = CreateOrUpdateButton(inspector, "WeaponGroup2", "武器组 2", new Vector2(144f, -159f), new Vector2(112f, 30f), font);
            EditorUtility.SetDirty(refs);
        }

        static Transform FindAncestor(Transform start, string name)
        {
            Transform current = start;
            while (current != null)
            {
                if (current.name == name)
                    return current;
                current = current.parent;
            }
            return null;
        }

        static Button CreateOrUpdateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchoredPosition,
            Vector2 size,
            Font font)
        {
            Transform existing = parent.Find(name);
            GameObject gameObject = existing == null ? new GameObject(name, typeof(RectTransform)) : existing.gameObject;
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image image = gameObject.GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();
            image.color = new Color(0.035f, 0.22f, 0.31f, 0.96f);
            Button button = gameObject.GetComponent<Button>();
            if (button == null)
                button = gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (!string.IsNullOrEmpty(label))
            {
                Transform labelTransform = gameObject.transform.Find("Label");
                GameObject labelObject = labelTransform == null
                    ? new GameObject("Label", typeof(RectTransform))
                    : labelTransform.gameObject;
                labelObject.transform.SetParent(gameObject.transform, false);
                RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                Text text = labelObject.GetComponent<Text>();
                if (text == null)
                    text = labelObject.AddComponent<Text>();
                text.text = label;
                text.font = font;
                text.fontSize = 15;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(0.75f, 0.94f, 1f);
                text.raycastTarget = false;
            }
            return button;
        }

        static RectTransform FindOrCreateRect(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return existing as RectTransform;
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        static void RemoveDirectChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        static void BuildInterstellarWeaponHud(Scene scene)
        {
            InterstellarFlightHud hud = FindInScene<InterstellarFlightHud>(scene);
            if (hud == null)
                throw new InvalidOperationException(
                    $"Scene '{scene.path}' has no InterstellarFlightHud.");
            Canvas canvas = hud.GetComponent<Canvas>();
            if (canvas == null)
                canvas = hud.GetComponentInParent<Canvas>(true);
            if (canvas == null)
                throw new InvalidOperationException(
                    $"Scene '{scene.path}' flight HUD has no Canvas.");
            RemoveDirectChild(canvas.transform, "VJoyBoundary");
            RemoveDirectChild(canvas.transform, "VJoyCursor");
            ConfigureInterstellarFlightHudLayout(canvas.transform);
            Font font = hud.GetComponentInChildren<Text>(true) == null ? null : hud.GetComponentInChildren<Text>(true).font;
            RectTransform root = FindOrCreateRect(canvas.transform, "WeaponHud");
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = Vector2.zero;

            Text crosshair = CreateOrUpdateText(root, "WeaponCrosshair", "+", new Vector2(0f, 0f), new Vector2(52f, 52f), 34, font, TextAnchor.MiddleCenter);
            Text group = CreateOrUpdateText(root, "WeaponGroupText", "武器组 1", new Vector2(-24f, 86f), new Vector2(220f, 28f), 18, font, TextAnchor.MiddleRight);
            Text ammo = CreateOrUpdateText(root, "WeaponAmmoText", "弹药 --", new Vector2(-24f, 58f), new Vector2(220f, 28f), 17, font, TextAnchor.MiddleRight);
            Text capacitor = CreateOrUpdateText(root, "WeaponCapacitorText", "电容 --", new Vector2(-24f, 30f), new Vector2(220f, 28f), 17, font, TextAnchor.MiddleRight);
            Text heat = CreateOrUpdateText(root, "WeaponHeatText", "热量 --", new Vector2(-24f, 2f), new Vector2(220f, 28f), 17, font, TextAnchor.MiddleRight);
            Text mount = CreateOrUpdateText(root, "WeaponMountText", "挂载 FIXED", new Vector2(-24f, -26f), new Vector2(220f, 28f), 17, font, TextAnchor.MiddleRight);
            Text locked = CreateOrUpdateText(root, "WeaponLockText", "搜索目标", new Vector2(-24f, -54f), new Vector2(220f, 28f), 17, font, TextAnchor.MiddleRight);
            ConfigureRightAnchoredText(group);
            ConfigureRightAnchoredText(ammo);
            ConfigureRightAnchoredText(capacitor);
            ConfigureRightAnchoredText(heat);
            ConfigureRightAnchoredText(mount);
            ConfigureRightAnchoredText(locked);
            RectTransform targetMarker = FindOrCreateRect(canvas.transform, "WeaponTargetMarker");
            targetMarker.anchorMin = new Vector2(0.5f, 0.5f);
            targetMarker.anchorMax = new Vector2(0.5f, 0.5f);
            targetMarker.pivot = new Vector2(0.5f, 0.5f);
            targetMarker.anchoredPosition = Vector2.zero;
            targetMarker.sizeDelta = new Vector2(104f, 104f);
            Text obsoleteMarkerText = targetMarker.GetComponent<Text>();
            if (obsoleteMarkerText != null)
                UnityEngine.Object.DestroyImmediate(obsoleteMarkerText);
            SpaceTargetLockGraphic targetGraphic = targetMarker.GetComponent<SpaceTargetLockGraphic>();
            if (targetGraphic == null)
                targetGraphic = targetMarker.gameObject.AddComponent<SpaceTargetLockGraphic>();
            targetGraphic.color = new Color(0.08f, 0.82f, 1f, 0.96f);
            targetGraphic.raycastTarget = false;
            targetMarker.gameObject.SetActive(false);

            var threatArrows = new RectTransform[3];
            for (int index = 0; index < threatArrows.Length; index++)
            {
                Text threatText = CreateOrUpdateText(
                    root,
                    "EnemyThreatArrow" + (index + 1),
                    "▲",
                    Vector2.zero,
                    new Vector2(64f, 64f),
                    48,
                    font,
                    TextAnchor.MiddleCenter);
                threatText.color = new Color(1f, 0.38f, 0.08f, 1f);
                Outline outline = threatText.GetComponent<Outline>();
                if (outline == null)
                    outline = threatText.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0.25f, 0.035f, 0.01f, 0.95f);
                outline.effectDistance = new Vector2(2f, -2f);
                threatArrows[index] = threatText.rectTransform;
                threatText.gameObject.SetActive(false);
            }

            SerializedObject serialized = new SerializedObject(hud);
            SetObjectReference(serialized, "weaponGroupText", group);
            SetObjectReference(serialized, "weaponAmmoText", ammo);
            SetObjectReference(serialized, "weaponCapacitorText", capacitor);
            SetObjectReference(serialized, "weaponHeatText", heat);
            SetObjectReference(serialized, "weaponMountText", mount);
            SetObjectReference(serialized, "weaponLockText", locked);
            SetObjectReference(serialized, "weaponTargetMarker", targetMarker);
            SetObjectReference(serialized, "pirateEncounterDirector", FindInScene<PirateEncounterDirector>(scene));
            SerializedProperty threatProperty = serialized.FindProperty("enemyThreatArrows");
            if (threatProperty != null)
            {
                threatProperty.arraySize = threatArrows.Length;
                for (int index = 0; index < threatArrows.Length; index++)
                    threatProperty.GetArrayElementAtIndex(index).objectReferenceValue = threatArrows[index];
            }
            SerializedProperty crosshairProperty = serialized.FindProperty("weaponCrosshair");
            if (crosshairProperty != null)
                crosshairProperty.objectReferenceValue = crosshair.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            hud.RebuildUnifiedLayout();
            InterstellarCameraRig cameraRig = FindInScene<InterstellarCameraRig>(scene);
            if (cameraRig == null)
                throw new InvalidOperationException(
                    $"Scene '{scene.path}' has no InterstellarCameraRig.");
            SerializedObject cameraRigSerialized = new SerializedObject(cameraRig);
            SerializedProperty cockpitFov =
                cameraRigSerialized.FindProperty("cockpitFieldOfView");
            SerializedProperty cockpitSpeedFov =
                cameraRigSerialized.FindProperty("cockpitSpeedFovAddition");
            SerializedProperty cockpitRecoil =
                cameraRigSerialized.FindProperty("cockpitRecoilLimits");
            if (cockpitFov != null)
                cockpitFov.floatValue = 66f;
            if (cockpitSpeedFov != null)
                cockpitSpeedFov.floatValue = 2f;
            if (cockpitRecoil != null)
                cockpitRecoil.vector3Value = new Vector3(0.03f, 0.03f, 0.08f);
            cameraRigSerialized.ApplyModifiedPropertiesWithoutUndo();
            SpaceflightCockpitController cockpit =
                cameraRig.GetComponent<SpaceflightCockpitController>();
            if (cockpit == null)
                cockpit = cameraRig.gameObject.AddComponent<SpaceflightCockpitController>();
            if (canvas.transform.Find("UnifiedFlightHud") == null)
                throw new InvalidOperationException(
                    $"Scene '{scene.path}' did not create UnifiedFlightHud.");
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(cameraRig);
            EditorUtility.SetDirty(cockpit);
        }

        static void ConfigureInterstellarFlightHudLayout(Transform canvas)
        {
            ConfigureTopLeftText(canvas.Find("SpeedText"), -142f, 20);
            ConfigureTopLeftText(canvas.Find("FlightModeText"), -178f, 20);
            ConfigureTopLeftText(canvas.Find("AuthorityText"), -210f, 20);
            ConfigureTopLeftText(canvas.Find("SpeedLimitText"), -242f, 20);
            ConfigureTopLeftText(canvas.Find("BoostText"), -274f, 20);

            RectTransform integrity = canvas.Find("IntegrityWidget") as RectTransform;
            if (integrity != null)
            {
                integrity.anchorMin = new Vector2(0.59f, 0.84f);
                integrity.anchorMax = new Vector2(0.95f, 0.97f);
                integrity.anchoredPosition = Vector2.zero;
                integrity.sizeDelta = Vector2.zero;
            }
        }

        static void ConfigureTopLeftText(Transform target, float y, int fontSize)
        {
            RectTransform rect = target as RectTransform;
            if (rect == null)
                return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, y);
            rect.sizeDelta = new Vector2(360f, 28f);
            Text text = rect.GetComponent<Text>();
            if (text != null)
                text.fontSize = fontSize;
        }

        static void ConfigureRightAnchoredText(Text text)
        {
            if (text == null)
                return;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
        }

        static Text CreateOrUpdateText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            int fontSize,
            Font font,
            TextAnchor anchor)
        {
            RectTransform rect = FindOrCreateRect(parent, name);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = rect.GetComponent<Text>();
            if (text == null)
                text = rect.gameObject.AddComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = new Color(0.45f, 0.92f, 1f);
            text.raycastTarget = false;
            return text;
        }

        static void SetObjectReference(SerializedObject serialized, string field, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null)
                property.objectReferenceValue = value;
        }

        static void EnsureFolder(string path)
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
    }
}
