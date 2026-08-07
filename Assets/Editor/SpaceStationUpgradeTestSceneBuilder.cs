using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityPlanet.SpaceStation;
using UnityPlanet.SpaceStation.Enhancement;

public static class SpaceStationUpgradeTestSceneBuilder
{
    public const string SourceScenePath = "Assets/ThirdParty/SciFiSpaceModular/SourceScene/SciFiSpaceStationDemo.unity";
    public const string TargetScenePath = "Assets/Scenes/SpaceStationUpgradeTest.unity";
    public const string DefaultShipPath = "Assets/SpacecraftEditor/ExternalFleet/Prefabs/Hulls/sf_stealth_fighter.prefab";
    public const string CelesteNpcPath = "Assets/Ida Faber/Stellar Girl Celeste/Prefabs/SK_Celeste_01 Blue.prefab";

    static readonly Vector3 DockCenter = new Vector3(-20.54f, 0f, 1.2f);
    // Center of the room whose unnumbered floor tile is named "Floor01".
    // The 0.08 m lift matches the CharacterController skin clearance.
    static readonly Vector3 Floor01Spawn = new Vector3(2f, 0.08f, 5f);
    static readonly Vector3 DockingBaySpawn = new Vector3(-20.54f, 0.08f, 9.2f);

    [MenuItem("Tools/Space Station/Rebuild Upgrade Station Test Scene")]
    public static void Build()
    {
        RequireAsset<SceneAsset>(SourceScenePath);
        GameObject shipPrefab = RequireAsset<GameObject>(DefaultShipPath);
        GameObject celestePrefab = RequireAsset<GameObject>(CelesteNpcPath);

        string targetDirectory = Path.GetDirectoryName(TargetScenePath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(targetDirectory) && !AssetDatabase.IsValidFolder(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
            AssetDatabase.Refresh();
        }

        Scene scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(scene, TargetScenePath, false))
        {
            throw new InvalidOperationException($"Failed to create station test scene at {TargetScenePath}.");
        }

        scene = SceneManager.GetActiveScene();
        RemoveSourceCamerasAndLights(scene);
        GameObject environment = GroupImportedEnvironment(scene);
        int repairedWallCount = RepairMissingWallMeshes(environment);
        int colliderCount = AddStaticEnvironmentColliders(environment);

        ConfigureStationRendering();
        GameObject systems = CreateSceneOrganization();
        GameObject dock = CreateDockingBay(systems.transform, shipPrefab);
        CreateStationLights(systems.transform);
        CreateCelesteNpc(systems.transform, environment, celestePrefab);
        GameObject player = CreatePlayer(systems.transform, dock.transform);
        int automaticDoorCount = ConfigureAutomaticDoors(environment, systems.transform, player.transform);
        Transform defaultShip = dock.transform.Find(
            "ParkedDefaultShip_Static_NoFlightPhysics");
        if (defaultShip == null)
        {
            throw new InvalidOperationException(
                "The default parked ship was not created in DockingBay_A.");
        }
        SpaceStationDockedShipController dockedShip =
            dock.AddComponent<SpaceStationDockedShipController>();
        dockedShip.Configure(
            player.transform,
            defaultShip.gameObject,
            Floor01Spawn,
            DockingBaySpawn);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException($"Failed to save station test scene at {TargetScenePath}.");
        }
        EnsureTargetSceneInBuildSettings();

        Selection.activeGameObject = player;
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath));
        Debug.Log($"[SpaceStation] Built isolated test scene. Repaired package walls: {repairedWallCount}; " +
                  $"environment colliders: {colliderCount}; " +
                  $"automatic doors: {automaticDoorCount}; " +
                  $"dock: {dock.name}; scene: {TargetScenePath}. Scene is enabled in Build Settings.");
    }

    static T RequireAsset<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            throw new FileNotFoundException($"Required station asset was not found: {path}");
        }

        return asset;
    }

    static void EnsureTargetSceneInBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            new List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
        int existingIndex = scenes.FindIndex(item =>
            string.Equals(
                item.path,
                TargetScenePath,
                StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            EditorBuildSettingsScene existing =
                scenes[existingIndex];
            if (!existing.enabled)
            {
                scenes[existingIndex] =
                    new EditorBuildSettingsScene(
                        existing.path,
                        true);
            }
        }
        else
        {
            scenes.Add(new EditorBuildSettingsScene(
                TargetScenePath,
                true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void RemoveSourceCamerasAndLights(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<Camera>(true) != null || root.GetComponentInChildren<Light>(true) != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    static GameObject GroupImportedEnvironment(Scene scene)
    {
        GameObject environment = new GameObject("StationEnvironment_Imported");
        SceneManager.MoveGameObjectToScene(environment, scene);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != environment)
            {
                root.transform.SetParent(environment.transform, true);
            }
        }

        return environment;
    }

    static int AddStaticEnvironmentColliders(GameObject environment)
    {
        int added = 0;
        MeshFilter[] meshFilters = environment.GetComponentsInChildren<MeshFilter>(true);
        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic |
                                  StaticEditorFlags.OccluderStatic |
                                  StaticEditorFlags.OccludeeStatic |
                                  StaticEditorFlags.ReflectionProbeStatic;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh == null)
            {
                continue;
            }

            GameObject target = meshFilter.gameObject;
            if (target.GetComponent<Collider>() == null)
            {
                MeshCollider collider = target.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
                collider.convex = false;
                added++;
            }

            GameObjectUtility.SetStaticEditorFlags(target, flags);
        }

        return added;
    }

    static int RepairMissingWallMeshes(GameObject environment)
    {
        Mesh replacement = null;
        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(
                     "Assets/ThirdParty/SciFiSpaceModular/Meshes/Wall01.fbx"))
        {
            Mesh mesh = asset as Mesh;
            if (mesh != null && mesh.name == "SM_Wall01")
            {
                replacement = mesh;
                break;
            }
        }

        if (replacement == null)
        {
            throw new FileNotFoundException("The replacement SM_Wall01 mesh could not be loaded.");
        }

        int repaired = 0;
        foreach (MeshFilter meshFilter in environment.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.sharedMesh != null || !meshFilter.name.StartsWith("Wall01", StringComparison.Ordinal))
            {
                continue;
            }

            // Thirteen instances in the vendor scene point to an FBX GUID that is absent from
            // the unitypackage. Recreate them as clean scene objects so no missing-prefab link
            // remains. The available Wall01 uses meter units instead of centimeters.
            GameObject missingObject = meshFilter.gameObject;
            MeshRenderer missingRenderer = missingObject.GetComponent<MeshRenderer>();
            Transform missingTransform = missingObject.transform;
            Transform parent = missingTransform.parent;
            int siblingIndex = missingTransform.GetSiblingIndex();

            GameObject repairedObject = new GameObject(missingObject.name);
            repairedObject.layer = missingObject.layer;
            repairedObject.tag = missingObject.tag;
            repairedObject.SetActive(missingObject.activeSelf);
            repairedObject.transform.SetParent(parent, false);
            repairedObject.transform.SetSiblingIndex(siblingIndex);
            repairedObject.transform.localPosition = missingTransform.localPosition;
            repairedObject.transform.localRotation = missingTransform.localRotation;
            repairedObject.transform.localScale = missingTransform.localScale * 0.01f;

            MeshFilter repairedFilter = repairedObject.AddComponent<MeshFilter>();
            repairedFilter.sharedMesh = replacement;
            MeshRenderer repairedRenderer = repairedObject.AddComponent<MeshRenderer>();
            CopyRendererSettings(missingRenderer, repairedRenderer);

            UnityEngine.Object.DestroyImmediate(missingObject);
            repaired++;
        }

        return repaired;
    }

    static void CopyRendererSettings(MeshRenderer source, MeshRenderer destination)
    {
        if (source == null)
        {
            return;
        }

        destination.sharedMaterials = source.sharedMaterials;
        destination.enabled = source.enabled;
        destination.shadowCastingMode = source.shadowCastingMode;
        destination.receiveShadows = source.receiveShadows;
        destination.motionVectorGenerationMode = source.motionVectorGenerationMode;
        destination.lightProbeUsage = source.lightProbeUsage;
        destination.reflectionProbeUsage = source.reflectionProbeUsage;
        destination.probeAnchor = source.probeAnchor;
        destination.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        destination.sortingLayerID = source.sortingLayerID;
        destination.sortingOrder = source.sortingOrder;
    }

    static void ConfigureStationRendering()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.18f, 0.24f, 0.29f);
        RenderSettings.ambientEquatorColor = new Color(0.10f, 0.14f, 0.17f);
        RenderSettings.ambientGroundColor = new Color(0.035f, 0.045f, 0.055f);
        RenderSettings.ambientIntensity = 0.9f;
        RenderSettings.fog = false;
    }

    static GameObject CreateSceneOrganization()
    {
        GameObject systems = new GameObject("SpaceStationTestSystems");
        new GameObject("FutureNPCs").transform.SetParent(systems.transform, false);
        new GameObject("FutureUpgradeStations").transform.SetParent(systems.transform, false);
        return systems;
    }

    static GameObject CreateDockingBay(Transform parent, GameObject shipPrefab)
    {
        GameObject dock = new GameObject("DockingBay_A_WestAtrium");
        dock.transform.SetParent(parent, false);
        dock.transform.position = DockCenter;

        GameObject marker = new GameObject("DockingBay_A_Bounds_12x20x6");
        marker.transform.SetParent(dock.transform, false);

        CreateDockGuides(dock.transform);
        CreateStaticShip(dock.transform, shipPrefab);
        return dock;
    }

    static void CreateDockGuides(Transform parent)
    {
        Material guideMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/ThirdParty/SciFiSpaceModular/Materials/Detail.mat");
        if (guideMaterial == null)
        {
            return;
        }

        CreateGuideStrip("DockGuide_Left", parent, new Vector3(-4.25f, 0.035f, 0f), new Vector3(0.10f, 0.035f, 10.2f), guideMaterial);
        CreateGuideStrip("DockGuide_Right", parent, new Vector3(4.25f, 0.035f, 0f), new Vector3(0.10f, 0.035f, 10.2f), guideMaterial);
        CreateGuideStrip("DockGuide_Front", parent, new Vector3(0f, 0.035f, 5.05f), new Vector3(8.6f, 0.035f, 0.10f), guideMaterial);
        CreateGuideStrip("DockGuide_Rear", parent, new Vector3(0f, 0.035f, -5.05f), new Vector3(8.6f, 0.035f, 0.10f), guideMaterial);
    }

    static void CreateGuideStrip(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        strip.name = name;
        strip.transform.SetParent(parent, false);
        strip.transform.localPosition = localPosition;
        strip.transform.localScale = localScale;
        strip.GetComponent<MeshRenderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(strip.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(strip, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
    }

    static GameObject CreateStaticShip(Transform parent, GameObject prefab)
    {
        GameObject ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        ship.name = "ParkedDefaultShip_Static_NoFlightPhysics";
        ship.transform.SetParent(parent, true);
        ship.transform.SetPositionAndRotation(DockCenter, Quaternion.identity);
        ship.transform.localScale = Vector3.one;

        RemoveShipRuntimeComponents(ship);

        Bounds initialBounds = CalculateWorldRendererBounds(ship);
        float scale = Mathf.Min(
            1.35f,
            7.5f / Mathf.Max(0.01f, initialBounds.size.x),
            8.4f / Mathf.Max(0.01f, initialBounds.size.z),
            2.9f / Mathf.Max(0.01f, initialBounds.size.y));
        ship.transform.localScale = Vector3.one * scale;

        Bounds scaledBounds = CalculateWorldRendererBounds(ship);
        Vector3 desiredCenter = new Vector3(DockCenter.x, 0.12f + scaledBounds.extents.y, DockCenter.z);
        ship.transform.position += desiredCenter - scaledBounds.center;

        Bounds localBounds = CalculateLocalRendererBounds(ship.transform);
        BoxCollider collider = ship.AddComponent<BoxCollider>();
        collider.center = localBounds.center;
        collider.size = localBounds.size;
        collider.isTrigger = false;

        SetStaticRecursively(ship);
        return ship;
    }

    static void RemoveShipRuntimeComponents(GameObject ship)
    {
        foreach (Rigidbody body in ship.GetComponentsInChildren<Rigidbody>(true))
        {
            UnityEngine.Object.DestroyImmediate(body);
        }

        foreach (Joint joint in ship.GetComponentsInChildren<Joint>(true))
        {
            UnityEngine.Object.DestroyImmediate(joint);
        }

        foreach (Collider collider in ship.GetComponentsInChildren<Collider>(true))
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }

        foreach (MonoBehaviour behaviour in ship.GetComponentsInChildren<MonoBehaviour>(true))
        {
            UnityEngine.Object.DestroyImmediate(behaviour);
        }
    }

    static void SetStaticRecursively(GameObject root)
    {
        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic |
                                  StaticEditorFlags.OccluderStatic |
                                  StaticEditorFlags.OccludeeStatic |
                                  StaticEditorFlags.ReflectionProbeStatic;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);
        }
    }

    static Bounds CalculateWorldRendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    static Bounds CalculateLocalRendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool initialized = false;

        foreach (Renderer renderer in renderers)
        {
            Bounds worldBounds = renderer.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 worldCorner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                Vector3 localCorner = root.InverseTransformPoint(worldCorner);
                if (!initialized)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return initialized ? localBounds : new Bounds(Vector3.zero, Vector3.one);
    }

    static void CreateStationLights(Transform parent)
    {
        GameObject fillObject = new GameObject("StationDirectionalFill");
        fillObject.transform.SetParent(parent, false);
        fillObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.72f, 0.86f, 1f);
        fill.intensity = 0.32f;
        fill.shadows = LightShadows.Soft;

        CreateHangarLight(parent, "DockKeyLight", DockCenter + new Vector3(0f, 5.45f, 0f), new Color(0.76f, 0.90f, 1f), 2.25f, true);
        CreateHangarLight(parent, "DockWarmLight_A", DockCenter + new Vector3(0f, 4.2f, -6.4f), new Color(1f, 0.48f, 0.22f), 1.35f, false);
        CreateHangarLight(parent, "DockWarmLight_B", DockCenter + new Vector3(0f, 4.2f, 6.4f), new Color(1f, 0.48f, 0.22f), 1.35f, false);

        // The source station has emissive ceiling panels but no actual lights outside
        // the docking bay. Keep these short-range fills on the room/corridor side of
        // x = -16.5 so the hangar lighting remains visually independent.
        Color interiorColor = new Color(0.76f, 0.88f, 1f);
        CreateInteriorLight(parent, "InteriorFill_Main_South", new Vector3(4f, 2.45f, -2f), interiorColor, 1.30f);
        CreateInteriorLight(parent, "InteriorFill_Main_North", new Vector3(4f, 2.45f, 4f), interiorColor, 1.30f);
        CreateInteriorLight(parent, "InteriorFill_Corridor_South", new Vector3(-12.5f, 2.45f, -5f), interiorColor, 1.20f);
        CreateInteriorLight(parent, "InteriorFill_Corridor_Center", new Vector3(-12.5f, 2.45f, 1f), interiorColor, 1.20f);
        CreateInteriorLight(parent, "InteriorFill_Corridor_North", new Vector3(-12.5f, 2.45f, 7f), interiorColor, 1.20f);
    }

    static void CreateInteriorLight(Transform parent, string name, Vector3 position, Color color, float intensity)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = 5.5f;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.Auto;
    }

    static void CreateHangarLight(Transform parent, string name, Vector3 position, Color color, float intensity, bool shadows)
    {
        GameObject lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.position = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = 11f;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }

    static void CreateCelesteNpc(Transform systems, GameObject environment, GameObject prefab)
    {
        Transform npcParent = systems.Find("FutureNPCs");
        if (npcParent == null)
        {
            throw new InvalidOperationException("FutureNPCs scene group was not created.");
        }

        Transform floor = RequireSceneTransform(
            environment,
            "Floor01 (10)",
            // Vendor prefab pivot is at the opposite tile corner; renderer bounds
            // below provide the actual walkable deck center (-10.5, 0, 1).
            new Vector3(-8.5f, 0f, -1f));
        Bounds floorBounds = CalculateWorldRendererBounds(floor.gameObject);

        GameObject npc = (GameObject)PrefabUtility.InstantiatePrefab(
            prefab,
            systems.gameObject.scene);
        npc.name = "NPC_Celeste_Floor01_10";
        npc.transform.SetParent(npcParent, true);
        npc.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, -90f, 0f));
        npc.transform.localScale = Vector3.one;

        Bounds initialBounds = CalculateWorldRendererBounds(npc);
        float scale = 1.78f / Mathf.Max(0.01f, initialBounds.size.y);
        npc.transform.localScale = Vector3.one * scale;
        npc.transform.position = new Vector3(floorBounds.center.x, 0f, floorBounds.center.z);

        Bounds scaledBounds = CalculateWorldRendererBounds(npc);
        npc.transform.position += Vector3.up * (floorBounds.max.y + 0.008f - scaledBounds.min.y);

        CapsuleCollider collider = npc.AddComponent<CapsuleCollider>();
        collider.direction = 1;
        collider.isTrigger = false;
        collider.radius = 0.32f;
        collider.height = 1.88f;
        collider.center = new Vector3(0f, 0.905f, 0f);

        npc.AddComponent<SpaceStationEnhancementNpcController>();

        ApplyNaturalStandingPose(npc);
        foreach (SkinnedMeshRenderer renderer in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.updateWhenOffscreen = false;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    static void ApplyNaturalStandingPose(GameObject npc)
    {
        Animator animator = npc.GetComponentInChildren<Animator>(true);
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            return;
        }

        PoseArm(
            animator,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            npc.transform,
            -1f);
        PoseArm(
            animator,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            npc.transform,
            1f);
        animator.applyRootMotion = false;
        animator.enabled = false;
    }

    static void PoseArm(
        Animator animator,
        HumanBodyBones upperArmBone,
        HumanBodyBones lowerArmBone,
        HumanBodyBones handBone,
        Transform character,
        float sideSign)
    {
        Transform upperArm = animator.GetBoneTransform(upperArmBone);
        Transform lowerArm = animator.GetBoneTransform(lowerArmBone);
        Transform hand = animator.GetBoneTransform(handBone);
        if (upperArm == null || lowerArm == null || hand == null)
        {
            return;
        }

        Vector3 side = character.right * sideSign;
        Vector3 currentUpperDirection = (lowerArm.position - upperArm.position).normalized;
        Vector3 desiredUpperDirection =
            (-character.up * 0.98f + side * 0.18f + character.forward * 0.03f).normalized;
        upperArm.rotation =
            Quaternion.FromToRotation(currentUpperDirection, desiredUpperDirection) *
            upperArm.rotation;

        Vector3 currentLowerDirection = (hand.position - lowerArm.position).normalized;
        Vector3 desiredLowerDirection =
            (-character.up * 0.93f + side * 0.10f + character.forward * 0.30f).normalized;
        lowerArm.rotation =
            Quaternion.FromToRotation(currentLowerDirection, desiredLowerDirection) *
            lowerArm.rotation;
    }

    static int ConfigureAutomaticDoors(GameObject environment, Transform systems, Transform player)
    {
        GameObject automaticDoors = new GameObject("AutomaticDoors");
        automaticDoors.transform.SetParent(systems, false);

        int configured = 0;
        configured += CreateAutomaticDoor(
            automaticDoors.transform,
            "AutoDoor_Door01_North",
            new Vector3(0f, 0f, 5f),
            new[] { RequireSceneTransform(environment, "Door01_Door", new Vector3(0f, 0f, 5f)) },
            new[] { Vector3.up * 2.8f },
            player);
        configured += CreateAutomaticDoor(
            automaticDoors.transform,
            "AutoDoor_Door01_South",
            new Vector3(-0.04f, 0f, -3f),
            new[] { RequireSceneTransform(environment, "Door01_Door (1)", new Vector3(-0.04f, 0f, -3f)) },
            new[] { Vector3.up * 2.8f },
            player);
        configured += CreateAutomaticDoor(
            automaticDoors.transform,
            "AutoDoor_Door02_DockingEntrance",
            new Vector3(-16.5f, 0f, 8f),
            new[]
            {
                RequireSceneTransform(environment, "Door02_Door", new Vector3(-16.5f, 0f, 8f)),
                RequireSceneTransform(environment, "Door02_Door (1)", new Vector3(-16.5f, 0f, 8f))
            },
            new[] { Vector3.back * 1.05f, Vector3.forward * 1.05f },
            player);
        configured += CreateAutomaticDoor(
            automaticDoors.transform,
            "AutoDoor_Door03_Central",
            new Vector3(-16.5f, 0f, 0.5f),
            new[]
            {
                RequireSceneTransform(environment, "Door03_Door01", new Vector3(-16.5f, 0f, 0.5f)),
                RequireSceneTransform(environment, "Door03_Door02", new Vector3(-16.5f, 0f, 0.5f))
            },
            new[] { Vector3.back * 1.1f, Vector3.forward * 1.1f },
            player);
        configured += CreateAutomaticDoor(
            automaticDoors.transform,
            "AutoDoor_Door04_South",
            new Vector3(-16.5f, 0f, -5.5f),
            new[]
            {
                RequireSceneTransform(environment, "Door04_Door", new Vector3(-16.5f, 0f, -5.5f)),
                RequireSceneTransform(environment, "Door04_Door (1)", new Vector3(-16.5f, 0f, -5.5f))
            },
            new[] { Vector3.back * 1.05f, Vector3.forward * 1.05f },
            player);

        return configured;
    }

    static int CreateAutomaticDoor(
        Transform parent,
        string name,
        Vector3 sensorPosition,
        Transform[] panels,
        Vector3[] openWorldOffsets,
        Transform player)
    {
        if (panels.Length != openWorldOffsets.Length)
        {
            throw new ArgumentException($"Door {name} has mismatched panel and offset counts.");
        }

        GameObject controllerObject = new GameObject(name);
        controllerObject.transform.SetParent(parent, false);
        controllerObject.transform.position = sensorPosition;

        foreach (Transform panel in panels)
        {
            GameObjectUtility.SetStaticEditorFlags(panel.gameObject, 0);
        }

        SpaceStationAutomaticDoor door = controllerObject.AddComponent<SpaceStationAutomaticDoor>();
        door.Configure(panels, openWorldOffsets, player);
        return 1;
    }

    static Transform RequireSceneTransform(GameObject environment, string name, Vector3 expectedPosition)
    {
        Transform bestMatch = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Transform candidate in environment.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name != name)
            {
                continue;
            }

            float distance = (candidate.position - expectedPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = candidate;
            }
        }

        if (bestMatch == null || bestDistance > 0.05f * 0.05f)
        {
            throw new InvalidOperationException($"Door panel {name} was not found near {expectedPosition}.");
        }

        return bestMatch;
    }

    static GameObject CreatePlayer(Transform parent, Transform dock)
    {
        GameObject player = new GameObject("StationWalker");
        player.tag = "Player";
        player.transform.SetParent(parent, false);
        player.transform.position = Floor01Spawn;

        Vector3 lookDirection = dock.position - Floor01Spawn;
        lookDirection.y = 0f;
        player.transform.rotation = lookDirection.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            : Quaternion.identity;

        CharacterController character = player.AddComponent<CharacterController>();
        character.height = 1.8f;
        character.radius = 0.34f;
        character.center = new Vector3(0f, 0.9f, 0f);
        character.skinWidth = 0.06f;
        character.stepOffset = 0.34f;
        character.slopeLimit = 50f;
        character.minMoveDistance = 0f;

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.62f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.008f, 0.012f, 0.018f);
        camera.fieldOfView = 72f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 300f;
        cameraObject.AddComponent<AudioListener>();

        player.AddComponent<SpaceStationFirstPersonController>();
        return player;
    }
}
