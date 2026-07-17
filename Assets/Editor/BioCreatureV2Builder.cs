using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class BioCreatureV2Builder
{
    const string AuthoritativeFamilyPath =
        "Assets/Creatures/Authorized/NMS/AntelopeBaseline/AntelopeInverseBindBaseline.fbx";
    const string OutputDirectory = "Assets/Creatures/V2/FixedAntelope";
    const string PrefabPath = OutputDirectory + "/FixedAntelopeV2.prefab";
    const string AuthoritativeLocomotionPath = AuthoritativeFamilyPath;
    const string AuthoritativeWalkClipName = "Antelope_WALK_InvBind";
    const string WalkControllerPath = OutputDirectory + "/FixedAntelopeWalkV2.controller";
    const string ScenePath = "Assets/Scenes/bio_v2.unity";

    static readonly string[] RequiredModuleObjectNames =
    {
        "_Body_Deer",
        "_Head_Deer",
        "DeerEyes",
        "_HDEars_1",
        "_Tail_Alien1"
    };

    public static string Build()
    {
        Directory.CreateDirectory(OutputDirectory);
        ConfigureAuthoritativeAnimationImport();
        GameObject prefab = BuildPrefab();
        BuildScene(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return $"Built {PrefabPath} and {ScenePath}";
    }

    static GameObject BuildPrefab()
    {
        GameObject familyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AuthoritativeFamilyPath);
        if (familyPrefab == null)
            throw new InvalidOperationException("Authoritative Antelope family FBX was not found.");

        var root = new GameObject("FixedAntelopeV2");
        try
        {
            Transform importSpace = new GameObject("ImportSpace").transform;
            importSpace.SetParent(root.transform, false);
            importSpace.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // The extracted FBX prefabs already carry their centimeter-to-meter
            // compensation on their imported roots. Applying another .01 here
            // shrinks both the mesh and skeleton a second time.
            importSpace.localScale = Vector3.one;

            GameObject family = (GameObject)PrefabUtility.InstantiatePrefab(familyPrefab, importSpace);
            family.name = "CanonicalRig";
            family.transform.localPosition = Vector3.zero;
            family.transform.localRotation = Quaternion.identity;

            var requiredNames = new HashSet<string>(RequiredModuleObjectNames, StringComparer.Ordinal);
            var foundRequiredNames = new HashSet<string>(StringComparer.Ordinal);
            var renderers = new List<SkinnedMeshRenderer>();
            Renderer[] allRenderers = family.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer candidate = allRenderers[i];
                bool knownModule = StableAntelopeSpeciesRandomizer.IsKnownModuleName(
                    candidate.gameObject.name);
                candidate.enabled = false;
                if (!knownModule)
                    continue;
                if (candidate is not SkinnedMeshRenderer skinned || skinned.sharedMesh == null)
                    throw new InvalidOperationException(
                        $"Authoritative module '{candidate.name}' is not a skinned mesh.");
                skinned.updateWhenOffscreen = true;
                renderers.Add(skinned);
                if (requiredNames.Contains(candidate.gameObject.name))
                    foundRequiredNames.Add(candidate.gameObject.name);
            }
            if (foundRequiredNames.Count != RequiredModuleObjectNames.Length)
                throw new InvalidOperationException(
                    $"Authoritative family contains {foundRequiredNames.Count}/" +
                    $"{RequiredModuleObjectNames.Length} required stable modules.");

            StableAntelopeSpeciesRandomizer species =
                root.AddComponent<StableAntelopeSpeciesRandomizer>();
            species.Configure(family.transform, 12345, true);
            species.ApplySpecies(12345);
            SkinnedMeshRenderer[] activeRenderers = species.ActiveRenderers;

            AnimatorController walkController = BuildWalkController();
            Animator animator = family.GetComponent<Animator>();
            if (animator == null)
                animator = family.AddComponent<Animator>();
            animator.enabled = true;
            animator.runtimeAnimatorController = walkController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;

            FixedLegChainV2[] legDefinitions = CreateLegDefinitions();
            NativeCreatureRootMotionBaseline rootMotion =
                root.AddComponent<NativeCreatureRootMotionBaseline>();
            rootMotion.Configure(animator, 1.35f);

            ConservativeFootPlacementIK footPlacement =
                root.AddComponent<ConservativeFootPlacementIK>();
            footPlacement.Configure(family.transform, activeRenderers, legDefinitions, ~0);

            FixedQuadrupedRigV2 validator = root.AddComponent<FixedQuadrupedRigV2>();
            validator.Configure(family.transform, activeRenderers, legDefinitions, true);
            if (!validator.ValidateConfiguration(out string message))
                throw new InvalidOperationException("Fixed V2 validation failed before saving: " + message);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null)
                throw new InvalidOperationException("Failed to save the fixed Antelope V2 prefab.");
            return saved;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void BuildScene(GameObject prefab)
    {
        Scene openTarget = SceneManager.GetSceneByPath(ScenePath);
        bool replaceOpenTarget = openTarget.IsValid() && openTarget.isLoaded;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Scene previousActive = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        if (replaceOpenTarget)
            EditorSceneManager.CloseScene(openTarget, true);
        try
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "StaticGround";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            GameObject creature = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            creature.name = "FixedAntelopeV2";
            creature.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            Bounds bounds = CalculateBounds(creature);
            creature.transform.position += Vector3.up * -bounds.min.y;
            bounds = CalculateBounds(creature);

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 46f;
            camera.nearClipPlane = .05f;
            cameraObject.AddComponent<AudioListener>();
            Vector3 target = bounds.center + Vector3.up * bounds.extents.y * .05f;
            float distance = Mathf.Max(5f, bounds.size.magnitude * 1.7f);
            cameraObject.transform.position = target + new Vector3(distance * .65f, distance * .32f, -distance);
            cameraObject.transform.rotation = Quaternion.LookRotation(target - cameraObject.transform.position, Vector3.up);
            cameraObject.transform.SetParent(creature.transform, true);

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            // This scene validates deformation, so long cast shadows would obscure
            // whether a silhouette comes from geometry or lighting.
            light.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            GameObject fillObject = new GameObject("Fill Light");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .35f;
            fill.color = new Color(.55f, .72f, 1f);
            fillObject.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.32f, .38f, .48f);
            RenderSettings.ambientEquatorColor = new Color(.18f, .22f, .28f);
            RenderSettings.ambientGroundColor = new Color(.08f, .09f, .11f);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Failed to save bio_v2 scene.");
        }
        finally
        {
            if (replaceOpenTarget && scene.IsValid() && scene.isLoaded)
                SceneManager.SetActiveScene(scene);
            else if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
            if (!replaceOpenTarget && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void ConfigureAuthoritativeAnimationImport()
    {
        AssetDatabase.ImportAsset(AuthoritativeLocomotionPath, ImportAssetOptions.ForceUpdate);
        ModelImporter importer = AssetImporter.GetAtPath(AuthoritativeLocomotionPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException(
                "The authoritative Antelope locomotion FBX is missing: " +
                AuthoritativeLocomotionPath);

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.resampleCurves = false;
        importer.SaveAndReimport();

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
            throw new InvalidOperationException(
                "The authoritative Antelope FBX does not contain an importable animation take.");
        for (int i = 0; i < clips.Length; i++)
        {
            if (i == 0)
                clips[i].name = AuthoritativeWalkClipName;
            clips[i].loopTime = true;
            clips[i].loopPose = false;
            clips[i].keepOriginalPositionY = true;
            clips[i].keepOriginalPositionXZ = true;
            clips[i].keepOriginalOrientation = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimatorController BuildWalkController()
    {
        AnimationClip walk = null;
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(AuthoritativeLocomotionPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is not AnimationClip clip
                || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(clip.name, AuthoritativeWalkClipName, StringComparison.Ordinal)
                || clip.name.IndexOf(AuthoritativeWalkClipName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                walk = clip;
                break;
            }
        }
        if (walk == null)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not AnimationClip clip
                    || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (walk == null || clip.length > walk.length)
                    walk = clip;
            }
        }
        if (walk == null)
        {
            string clipNames = string.Join(", ", Array.ConvertAll(
                Array.FindAll(assets, asset => asset is AnimationClip),
                asset => asset.name));
            throw new InvalidOperationException(
                $"The authoritative walk clip '{AuthoritativeWalkClipName}' was not found in " +
                $"{AuthoritativeLocomotionPath}. Available clips: {clipNames}");
        }
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(WalkControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(WalkControllerPath);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        ChildAnimatorState[] existingStates = stateMachine.states;
        for (int i = 0; i < existingStates.Length; i++)
            stateMachine.RemoveState(existingStates[i].state);

        AnimatorState walkState = stateMachine.AddState("Native WALK");
        walkState.motion = walk;
        walkState.speed = 1f;
        stateMachine.defaultState = walkState;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static FixedLegChainV2[] CreateLegDefinitions()
    {
        return new[]
        {
            Leg("FrontLeft", "LF2ShoulderJNT", "LF2ElbowJNT", "LF2WristJNT", "LF2FootJNT"),
            Leg("FrontRight", "RF2ShoulderJNT", "RF2ElbowJNT", "RF2WristJNT", "RF2FootJNT"),
            Leg("RearLeft", "LBLegJNT", "LBKneeJNT", "LBAnkleJNT", "LBFootJNT"),
            Leg("RearRight", "RBLegJNT", "RBKneeJNT", "RBAnkleJNT", "RBFootJNT")
        };
    }

    static FixedLegChainV2 Leg(string id, string upper, string lower, string ankle, string foot)
    {
        return new FixedLegChainV2
        {
            id = id,
            upperBone = upper,
            lowerBone = lower,
            ankleBone = ankle,
            footBone = foot
        };
    }

    static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Renderer first = null;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i].enabled)
            {
                first = renderers[i];
                break;
            }
        if (first == null)
            return new Bounds(root.transform.position, Vector3.one);
        Bounds bounds = first.bounds;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != first && renderers[i].enabled)
                bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
