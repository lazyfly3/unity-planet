using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class NmsAuthorizedAntelopeLibraryBuilder
{
    const string RigPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeProceduralSource.fbx";
    const string AuthoritativeWalkPath =
        "Assets/Creatures/Authorized/NMS/AntelopeBaseline/AntelopeInverseBindBaseline.fbx";
    const string AuthoritativeWalkName = "Antelope_WALK_InvBind";
    const string FallbackPath =
        "Assets/Creatures/Authorized/NMS/Prefabs/AntelopeVariant_0042.prefab";
    const string BaseAnimatorControllerPath =
        "Assets/Creatures/Authorized/NMS/AntelopeFamily/Animations/AntelopeLocomotion.controller";
    const string AnimatorControllerPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeSafeLocomotion.overrideController";
    const string LegacyAnimatorControllerPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeSafeLocomotion.controller";
    static readonly string[] SourceAnimationPaths =
    {
        "Assets/Creatures/Authorized/NMS/AntelopeFamily/Animations/Runtime/AntelopeIdle.anim",
        "Assets/Creatures/Authorized/NMS/AntelopeFamily/Animations/Runtime/AntelopeWalk.anim",
        "Assets/Creatures/Authorized/NMS/AntelopeFamily/Animations/Runtime/AntelopeRun.anim"
    };
    const string SafeWalkPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeSafeWalk.anim";
    const string ObsoleteSafeIdlePath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeSafeIdle.anim";
    const string ObsoleteSafeRunPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeSafeRun.anim";
    const string ModuleFolder =
        "Assets/Creatures/Authorized/NMS/AntelopeFamily/Modules/antelope";
    const string OutputPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeAuthorizedLibrary.asset";

    static readonly HashSet<string> SupportedModules = new HashSet<string>(
        StringComparer.Ordinal)
    {
        "bodydeer", "bodyfat", "headdeer", "deereyes",
        "hdears1", "hdears9",
        "hdhorns1", "hdhorns2", "hdhorns3", "hdhorns4", "hdhorns5",
        "tailalien0", "tailalien1", "tailalien4",
        "deeracc1n2", "deeracc2n2", "deeracc5n2", "deeracc6n2",
        "deeracc23", "deeracc24", "deeracc25",
        "fatacc1n", "fatacc2n", "fatacc5n", "fatacc6n",
        "fatacc11ok", "fatacc12ok", "fatacc14ok"
    };

    [MenuItem("Tools/Creatures/Build Authorized Antelope Procedural Library")]
    public static void Build()
    {
        ModelImporter rigImporter = AssetImporter.GetAtPath(RigPath) as ModelImporter;
        if (rigImporter != null && !rigImporter.isReadable)
        {
            rigImporter.isReadable = true;
            rigImporter.SaveAndReimport();
        }
        GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
        GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(FallbackPath);
        BuildCompatibleAnimator(rig);
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath);
        if (rig == null || fallback == null || controller == null)
            throw new InvalidOperationException("Authorized antelope rig or fallback prefab is missing.");

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ModuleFolder });
        var modules = new List<NmsAuthorizedAntelopeModule>(SupportedModules.Count);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            NmsAuthorizedModuleBinding binding = prefab != null
                ? prefab.GetComponent<NmsAuthorizedModuleBinding>()
                : null;
            SkinnedMeshRenderer renderer = prefab != null
                ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true)
                : null;
            if (binding == null || renderer == null || renderer.sharedMesh == null)
                continue;
            string selectionId = Normalize(binding.ModuleId);
            if (SupportedModules.Contains(selectionId))
                modules.Add(new NmsAuthorizedAntelopeModule(
                    selectionId,
                    renderer.sharedMesh.name,
                    prefab,
                    renderer.sharedMaterials));
        }
        modules.Sort((left, right) => string.CompareOrdinal(
            left.SelectionId, right.SelectionId));
        if (modules.Count != SupportedModules.Count)
            throw new InvalidOperationException(
                $"Authorized antelope library found {modules.Count}/{SupportedModules.Count} modules.");

        NmsAuthorizedAntelopeLibrary library =
            AssetDatabase.LoadAssetAtPath<NmsAuthorizedAntelopeLibrary>(OutputPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<NmsAuthorizedAntelopeLibrary>();
            AssetDatabase.CreateAsset(library, OutputPath);
        }
        library.ConfigureEditor(rig, fallback, controller, modules.ToArray());
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Debug.Log($"Authorized antelope procedural library built with {modules.Count} modules.");
    }

    static void BuildCompatibleAnimator(GameObject rig)
    {
        if (rig == null)
            throw new InvalidOperationException("Cannot build animations without the canonical rig.");
        GameObject authoritativeWalkRig =
            AssetDatabase.LoadAssetAtPath<GameObject>(AuthoritativeWalkPath);
        AnimationClip authoritativeWalk =
            LoadClipAtPath(AuthoritativeWalkPath, AuthoritativeWalkName);
        if (authoritativeWalkRig == null || authoritativeWalk == null)
            throw new InvalidOperationException(
                $"Missing authoritative Blender walk '{AuthoritativeWalkName}'.");
        AnimationClip safeWalk = AssetDatabase.LoadAssetAtPath<AnimationClip>(SafeWalkPath);
        if (safeWalk == null)
        {
            safeWalk = new AnimationClip { name = "AntelopeSafeWalk" };
            AssetDatabase.CreateAsset(safeWalk, SafeWalkPath);
        }
        safeWalk.ClearCurves();
        safeWalk.frameRate = authoritativeWalk.frameRate;
        safeWalk.wrapMode = WrapMode.Loop;
        RetargetClip(authoritativeWalkRig, rig, authoritativeWalk, safeWalk);
        AnimationClipSettings settings =
            AnimationUtility.GetAnimationClipSettings(authoritativeWalk);
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(safeWalk, settings);
        EditorUtility.SetDirty(safeWalk);
        AssetDatabase.DeleteAsset(ObsoleteSafeIdlePath);
        AssetDatabase.DeleteAsset(ObsoleteSafeRunPath);

        RuntimeAnimatorController baseController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BaseAnimatorControllerPath);
        if (baseController == null)
            throw new InvalidOperationException(
                $"Missing base Animator Controller: {BaseAnimatorControllerPath}");
        if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(LegacyAnimatorControllerPath) != null)
            AssetDatabase.DeleteAsset(LegacyAnimatorControllerPath);
        if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimatorControllerPath) != null)
            AssetDatabase.DeleteAsset(AnimatorControllerPath);
        var controller = new AnimatorOverrideController(baseController)
        {
            name = "AntelopeSafeLocomotion"
        };
        for (int i = 0; i < SourceAnimationPaths.Length; i++)
        {
            AnimationClip source =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceAnimationPaths[i]);
            controller[source.name] = safeWalk;
        }
        AssetDatabase.CreateAsset(controller, AnimatorControllerPath);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static AnimationClip LoadClipAtPath(string path, string clipName)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
            if (assets[i] is AnimationClip clip
                && string.Equals(clip.name, clipName, StringComparison.Ordinal))
                return clip;
        return null;
    }

    static void RetargetClip(
        GameObject sourcePrefab,
        GameObject targetPrefab,
        AnimationClip sourceClip,
        AnimationClip targetClip)
    {
        GameObject sourceRoot = UnityEngine.Object.Instantiate(sourcePrefab);
        sourceRoot.name = "AntelopeAnimationRetargetSource";
        try
        {
            var targetByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform[] targetTransforms =
                targetPrefab.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < targetTransforms.Length; i++)
                if (!targetByName.ContainsKey(targetTransforms[i].name))
                    targetByName.Add(targetTransforms[i].name, targetTransforms[i]);

            var tracks = new List<RetargetTrack>();
            var trackedNames = new HashSet<string>(StringComparer.Ordinal);
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceClip);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding binding = bindings[i];
                if (binding.type != typeof(Transform))
                    continue;
                Transform source = sourceRoot.transform.Find(binding.path);
                if (source == null || !trackedNames.Add(source.name)
                    || !targetByName.TryGetValue(source.name, out Transform target))
                    continue;
                tracks.Add(new RetargetTrack(
                    source, target,
                    AnimationUtility.CalculateTransformPath(
                        target, targetPrefab.transform)));
            }
            if (tracks.Count == 0)
                throw new InvalidOperationException(
                    $"Animation '{sourceClip.name}' has no bones shared with the canonical rig.");

            int sampleCount = Mathf.Max(
                2, Mathf.CeilToInt(sourceClip.length * sourceClip.frameRate) + 1);
            AnimationMode.StartAnimationMode();
            try
            {
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    float time = sourceClip.length * sample / (sampleCount - 1f);
                    AnimationMode.SampleAnimationClip(sourceRoot, sourceClip, time);
                    for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
                        tracks[trackIndex].AddSample(time);
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            for (int i = 0; i < tracks.Count; i++)
                tracks[i].Write(targetClip);
            targetClip.EnsureQuaternionContinuity();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceRoot);
        }
    }

    sealed class RetargetTrack
    {
        static readonly string[] Properties =
        {
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y",
            "m_LocalRotation.z", "m_LocalRotation.w",
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z"
        };

        readonly Transform source;
        readonly string targetPath;
        readonly Vector3 sourceRestPosition;
        readonly Quaternion sourceRestRotation;
        readonly Vector3 sourceRestScale;
        readonly Vector3 targetRestPosition;
        readonly Quaternion targetRestRotation;
        readonly Vector3 targetRestScale;
        readonly AnimationCurve[] curves = new AnimationCurve[Properties.Length];
        Quaternion previousRotation;
        bool hasPreviousRotation;

        public RetargetTrack(Transform sourceBone, Transform targetBone, string path)
        {
            source = sourceBone;
            targetPath = path;
            sourceRestPosition = source.localPosition;
            sourceRestRotation = source.localRotation;
            sourceRestScale = source.localScale;
            targetRestPosition = targetBone.localPosition;
            targetRestRotation = targetBone.localRotation;
            targetRestScale = targetBone.localScale;
            for (int i = 0; i < curves.Length; i++)
                curves[i] = new AnimationCurve();
        }

        public void AddSample(float time)
        {
            Vector3 position = targetRestPosition
                + source.localPosition - sourceRestPosition;
            Quaternion sourceDelta =
                Quaternion.Inverse(sourceRestRotation) * source.localRotation;
            Quaternion rotation = targetRestRotation * sourceDelta;
            if (hasPreviousRotation && Quaternion.Dot(previousRotation, rotation) < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            previousRotation = rotation;
            hasPreviousRotation = true;
            Vector3 scale = new Vector3(
                targetRestScale.x * SafeRatio(source.localScale.x, sourceRestScale.x),
                targetRestScale.y * SafeRatio(source.localScale.y, sourceRestScale.y),
                targetRestScale.z * SafeRatio(source.localScale.z, sourceRestScale.z));
            float[] values =
            {
                position.x, position.y, position.z,
                rotation.x, rotation.y, rotation.z, rotation.w,
                scale.x, scale.y, scale.z
            };
            for (int i = 0; i < curves.Length; i++)
                curves[i].AddKey(time, values[i]);
        }

        public void Write(AnimationClip clip)
        {
            for (int curveIndex = 0; curveIndex < curves.Length; curveIndex++)
            {
                AnimationCurve curve = curves[curveIndex];
                for (int keyIndex = 0; keyIndex < curve.length; keyIndex++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(
                        curve, keyIndex, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(
                        curve, keyIndex, AnimationUtility.TangentMode.Linear);
                }
                AnimationUtility.SetEditorCurve(
                    clip,
                    EditorCurveBinding.FloatCurve(
                        targetPath, typeof(Transform), Properties[curveIndex]),
                    curve);
            }
        }

        static float SafeRatio(float value, float baseline)
        {
            return Mathf.Abs(baseline) > 0.00001f ? value / baseline : 1f;
        }
    }

    static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var result = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
            if (char.IsLetterOrDigit(value[i]))
                result.Append(char.ToLowerInvariant(value[i]));
        string normalized = result.ToString();
        return normalized.StartsWith("antelope", StringComparison.Ordinal)
            ? normalized.Substring("antelope".Length)
            : normalized;
    }
}
