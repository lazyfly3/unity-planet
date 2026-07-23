using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class NmsProceduralGaitTemplateExtractor
{
    const int SampleCount = 64;
    const string SourceRigPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeProceduralSource.fbx";
    const string SourceClipPath =
        "Assets/Creatures/Authorized/NMS/AntelopeBaseline/AntelopeInverseBindBaseline.fbx";
    const string SourceClipName = "Antelope_WALK_InvBind";
    const string FamilyPath =
        "Assets/Creatures/Generated/NMS/AntelopeQuadruped/AntelopeQuadrupedFamily.asset";
    const string ProfilePath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeQuadrupedMotionProfile.asset";
    const string OutputPath =
        "Assets/Creatures/Authorized/NMS/Procedural/AntelopeWalkGaitTemplate.asset";

    static readonly string[] BodyTrackNames =
    {
        "HipJNT", "Back1JNT", "Back2JNT", "Back3JNT",
        "Neck1JNT", "Neck2JNT", "HeadJNT", "Tail1JNT"
    };

    [MenuItem("Tools/Creatures/Extract Antelope Procedural Walk Template")]
    public static void Extract()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRigPath);
        NmsCreatureFamilyDefinition family =
            AssetDatabase.LoadAssetAtPath<NmsCreatureFamilyDefinition>(FamilyPath);
        NmsProceduralMotionProfile profile =
            AssetDatabase.LoadAssetAtPath<NmsProceduralMotionProfile>(ProfilePath);
        AnimationClip clip = FindClip(SourceClipPath, SourceClipName);
        if (source == null || family == null || profile == null || clip == null)
            throw new InvalidOperationException(
                "The validated antelope rig, WALK clip, family, or motion profile is missing.");

        GameObject instance = UnityEngine.Object.Instantiate(source);
        instance.name = "AntelopeGaitExtraction";
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var bones = BuildBoneMap(instance.transform);
            ValidateRequiredBones(family, profile, bones);
            float rigScale = CalculateRigScale(family, bones);

            var trackTransforms = new Transform[BodyTrackNames.Length];
            var trackRestPositions = new Vector3[BodyTrackNames.Length];
            var trackRestRotations = new Quaternion[BodyTrackNames.Length];
            var trackPositions = new Vector3[BodyTrackNames.Length][];
            var trackRotations = new Quaternion[BodyTrackNames.Length][];
            for (int i = 0; i < BodyTrackNames.Length; i++)
            {
                trackTransforms[i] = bones[BodyTrackNames[i]];
                trackRestPositions[i] = trackTransforms[i].localPosition;
                trackRestRotations[i] = trackTransforms[i].localRotation;
                trackPositions[i] = new Vector3[SampleCount];
                trackRotations[i] = new Quaternion[SampleCount];
            }

            int legCount = family.LoadBearingChains.Count;
            var feet = new Vector3[legCount][];
            var contactFeet = new Vector3[legCount][];
            var footTransforms = new Transform[legCount];
            var upperTransforms = new Transform[legCount];
            var lowerTransforms = new Transform[legCount];
            var distalTransforms = new Transform[legCount];
            var firstPoles = new Vector3[legCount][];
            var secondPoles = new Vector3[legCount][];
            var restFirstPoles = new Vector3[legCount];
            var restSecondPoles = new Vector3[legCount];
            for (int legIndex = 0; legIndex < legCount; legIndex++)
            {
                NmsCreatureLegChain chain = family.LoadBearingChains[legIndex];
                upperTransforms[legIndex] = bones[chain.bones[0]];
                lowerTransforms[legIndex] = bones[chain.bones[1]];
                distalTransforms[legIndex] = bones[chain.bones[2]];
                footTransforms[legIndex] = bones[chain.bones[3]];
                feet[legIndex] = new Vector3[SampleCount];
                contactFeet[legIndex] = new Vector3[SampleCount];
                firstPoles[legIndex] = new Vector3[SampleCount];
                secondPoles[legIndex] = new Vector3[SampleCount];
                restFirstPoles[legIndex] = GetPoleDirection(
                    instance.transform,
                    upperTransforms[legIndex],
                    lowerTransforms[legIndex],
                    distalTransforms[legIndex],
                    instance.transform.forward);
                restSecondPoles[legIndex] = GetPoleDirection(
                    instance.transform,
                    lowerTransforms[legIndex],
                    distalTransforms[legIndex],
                    footTransforms[legIndex],
                    instance.transform.forward);
            }

            AnimationMode.StartAnimationMode();
            try
            {
                for (int sample = 0; sample < SampleCount; sample++)
                {
                    float time = clip.length * sample / SampleCount;
                    AnimationMode.SampleAnimationClip(instance, clip, time);
                    for (int track = 0; track < trackTransforms.Length; track++)
                    {
                        Transform bone = trackTransforms[track];
                        trackPositions[track][sample] =
                            (bone.localPosition - trackRestPositions[track]) / rigScale;
                        trackRotations[track][sample] = Quaternion.Inverse(
                            trackRestRotations[track]) * bone.localRotation;
                    }
                    for (int leg = 0; leg < legCount; leg++)
                    {
                        Vector3 offset = footTransforms[leg].position
                            - upperTransforms[leg].position;
                        feet[leg][sample] = instance.transform.InverseTransformVector(offset)
                            / rigScale;
                        contactFeet[leg][sample] = instance.transform.InverseTransformPoint(
                            footTransforms[leg].position) / rigScale;
                        firstPoles[leg][sample] = GetRawPoleDirection(
                            instance.transform,
                            upperTransforms[leg], lowerTransforms[leg],
                            distalTransforms[leg]);
                        secondPoles[leg][sample] = GetRawPoleDirection(
                            instance.transform,
                            lowerTransforms[leg], distalTransforms[leg],
                            footTransforms[leg]);
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            for (int leg = 0; leg < legCount; leg++)
            {
                StabilizePoleTrack(firstPoles[leg], restFirstPoles[leg],
                    family.LoadBearingChains[leg].id, "knee");
                StabilizePoleTrack(secondPoles[leg], restSecondPoles[leg],
                    family.LoadBearingChains[leg].id, "hock");
            }

            var boneTracks = new NmsProceduralGaitBoneTrack[BodyTrackNames.Length];
            for (int i = 0; i < boneTracks.Length; i++)
                boneTracks[i] = new NmsProceduralGaitBoneTrack(
                    BodyTrackNames[i], trackPositions[i], trackRotations[i]);

            var legTracks = new NmsProceduralGaitLegTrack[legCount];
            var allStance = new float[legCount][];
            var allProgress = new float[legCount][];
            for (int leg = 0; leg < legCount; leg++)
            {
                BuildContactCurves(
                    contactFeet[leg], out allStance[leg], out allProgress[leg]);
            }
            EnsureMinimumSupport(allStance, contactFeet);
            for (int leg = 0; leg < legCount; leg++)
            {
                RebuildSwingProgress(allStance[leg], allProgress[leg]);
                FindPrimarySwing(
                    family.LoadBearingChains[leg].id,
                    allStance[leg], allProgress[leg], feet[leg],
                    out float liftOffPhase, out float touchDownPhase,
                    out Vector3 liftOffOffset, out Vector3 touchDownOffset);
                legTracks[leg] = new NmsProceduralGaitLegTrack(
                    family.LoadBearingChains[leg].id,
                    feet[leg], firstPoles[leg], secondPoles[leg],
                    allStance[leg], allProgress[leg],
                    liftOffPhase, touchDownPhase, liftOffOffset, touchDownOffset);
            }

            float referenceBodyHeight = CalculateBodyHeight(
                instance.transform, bones[profile.BodyBoneName], footTransforms) / rigScale;
            float normalizedDistance = family.WalkSpeed * clip.length / rigScale;
            NmsProceduralGaitTemplate template =
                AssetDatabase.LoadAssetAtPath<NmsProceduralGaitTemplate>(OutputPath);
            if (template == null)
            {
                template = ScriptableObject.CreateInstance<NmsProceduralGaitTemplate>();
                AssetDatabase.CreateAsset(template, OutputPath);
            }
            template.ConfigureEditor(
                family.FamilyId, SampleCount, clip.length, normalizedDistance,
                referenceBodyHeight, rigScale, boneTracks, legTracks);
            profile.SetWalkGaitTemplateEditor(template);
            EditorUtility.SetDirty(template);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Extracted {SampleCount} samples from {SourceClipName}. "
                + $"Cycle={clip.length:F3}s, normalized distance={normalizedDistance:F3}.",
                template);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    static AnimationClip FindClip(string path, string clipName)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
            if (assets[i] is AnimationClip clip
                && string.Equals(clip.name, clipName, StringComparison.Ordinal))
                return clip;
        return null;
    }

    static Dictionary<string, Transform> BuildBoneMap(Transform root)
    {
        var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
        Transform[] hierarchy = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < hierarchy.Length; i++)
            if (!result.ContainsKey(hierarchy[i].name))
                result.Add(hierarchy[i].name, hierarchy[i]);
        return result;
    }

    static void ValidateRequiredBones(
        NmsCreatureFamilyDefinition family,
        NmsProceduralMotionProfile profile,
        Dictionary<string, Transform> bones)
    {
        for (int i = 0; i < BodyTrackNames.Length; i++)
            if (!bones.ContainsKey(BodyTrackNames[i]))
                throw new InvalidOperationException(
                    $"Validated source is missing body track '{BodyTrackNames[i]}'.");
        for (int i = 0; i < family.LoadBearingChains.Count; i++)
            for (int bone = 0; bone < family.LoadBearingChains[i].bones.Length; bone++)
                if (!bones.ContainsKey(family.LoadBearingChains[i].bones[bone]))
                    throw new InvalidOperationException(
                        $"Validated source is missing '{family.LoadBearingChains[i].bones[bone]}'.");
        if (!bones.ContainsKey(profile.BodyBoneName))
            throw new InvalidOperationException("The profile body bone is missing.");
    }

    static float CalculateRigScale(
        NmsCreatureFamilyDefinition family, Dictionary<string, Transform> bones)
    {
        float sum = 0f;
        for (int i = 0; i < family.LoadBearingChains.Count; i++)
        {
            string[] chain = family.LoadBearingChains[i].bones;
            for (int segment = 0; segment + 1 < chain.Length; segment++)
                sum += Vector3.Distance(bones[chain[segment]].position,
                    bones[chain[segment + 1]].position);
        }
        return Mathf.Max(0.01f, sum / family.LoadBearingChains.Count);
    }

    static Vector3 GetPoleDirection(
        Transform root,
        Transform previous,
        Transform joint,
        Transform next,
        Vector3 fallbackWorld)
    {
        Vector3 axis = next.position - previous.position;
        Vector3 bend = Vector3.ProjectOnPlane(joint.position - previous.position, axis);
        if (bend.sqrMagnitude < 0.000001f)
            bend = Vector3.ProjectOnPlane(fallbackWorld, axis);
        if (bend.sqrMagnitude < 0.000001f)
            bend = Vector3.ProjectOnPlane(root.right, axis);
        return root.InverseTransformDirection(bend.normalized);
    }

    static Vector3 GetRawPoleDirection(
        Transform root, Transform previous, Transform joint, Transform next)
    {
        Vector3 axis = next.position - previous.position;
        Vector3 bend = Vector3.ProjectOnPlane(joint.position - previous.position, axis);
        return bend.sqrMagnitude > 0.000001f
            ? root.InverseTransformDirection(bend.normalized)
            : Vector3.zero;
    }

    static void StabilizePoleTrack(
        Vector3[] values, Vector3 fallback, string chainId, string jointName)
    {
        if (values == null || values.Length == 0)
            throw new InvalidOperationException(
                $"{chainId} {jointName} pole track has no samples.");
        Vector3 previous = fallback.sqrMagnitude > 0.000001f
            ? fallback.normalized : Vector3.forward;
        for (int i = 0; i < values.Length; i++)
        {
            Vector3 current = IsFinite(values[i]) && values[i].sqrMagnitude > 0.000001f
                ? values[i].normalized : previous;
            if (Vector3.Dot(previous, current) < 0f)
                current = -current;
            values[i] = current;
            previous = current;
        }

        var source = (Vector3[])values.Clone();
        for (int i = 0; i < values.Length; i++)
        {
            Vector3 center = source[i];
            Vector3 before = AlignHemisphere(source[Wrap(i - 1, source.Length)], center);
            Vector3 after = AlignHemisphere(source[Wrap(i + 1, source.Length)], center);
            Vector3 filtered = before * 0.25f + center * 0.5f + after * 0.25f;
            values[i] = filtered.sqrMagnitude > 0.000001f
                ? filtered.normalized : center;
        }

        for (int i = 1; i < values.Length; i++)
            if (Vector3.Dot(values[i - 1], values[i]) < 0f)
                values[i] = -values[i];
        if (Vector3.Dot(values[values.Length - 1], values[0]) < 0f)
            throw new InvalidOperationException(
                $"{chainId} {jointName} pole track is not cyclically continuous.");
    }

    static Vector3 AlignHemisphere(Vector3 value, Vector3 reference)
    {
        return Vector3.Dot(value, reference) < 0f ? -value : value;
    }

    static void BuildContactCurves(
        Vector3[] feet,
        out float[] stance,
        out float[] progress)
    {
        int count = feet.Length;
        stance = new float[count];
        progress = new float[count];
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            minimum = Mathf.Min(minimum, feet[i].y);
            maximum = Mathf.Max(maximum, feet[i].y);
        }
        float range = Mathf.Max(0.0001f, maximum - minimum);
        float contactBand = Mathf.Max(range * 0.32f, 0.012f);
        for (int i = 0; i < count; i++)
        {
            stance[i] = feet[i].y <= minimum + contactBand ? 1f : 0f;
        }
        RemoveSingleSampleIslands(stance);
        RebuildSwingProgress(stance, progress);
    }

    static void FindPrimarySwing(
        string chainId,
        float[] stance,
        float[] progress,
        Vector3[] feet,
        out float liftOffPhase,
        out float touchDownPhase,
        out Vector3 liftOffOffset,
        out Vector3 touchDownOffset)
    {
        int count = stance.Length;
        var starts = new List<int>();
        var lengths = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (stance[i] >= 0.5f || stance[Wrap(i - 1, count)] < 0.5f)
                continue;
            int length = 0;
            while (length < count && stance[Wrap(i + length, count)] < 0.5f)
                length++;
            starts.Add(i);
            lengths.Add(length);
        }
        if (starts.Count == 0)
            throw new InvalidOperationException($"{chainId} has no valid swing interval.");

        int primary = 0;
        for (int i = 1; i < lengths.Count; i++)
            if (lengths[i] > lengths[primary])
                primary = i;
        for (int run = 0; run < starts.Count; run++)
        {
            if (run == primary)
                continue;
            if (lengths[run] >= 3)
                throw new InvalidOperationException(
                    $"{chainId} contains more than one substantial swing interval "
                    + $"(starts={string.Join(",", starts)}, "
                    + $"lengths={string.Join(",", lengths)})." );
            for (int i = 0; i < lengths[run]; i++)
                stance[Wrap(starts[run] + i, count)] = 1f;
        }

        RebuildSwingProgress(stance, progress);
        int swingStart = starts[primary];
        int swingLength = lengths[primary];
        int liftOffIndex = Wrap(swingStart - 1, count);
        int touchDownIndex = Wrap(swingStart + swingLength, count);
        liftOffPhase = liftOffIndex / (float)count;
        touchDownPhase = touchDownIndex / (float)count;
        liftOffOffset = feet[liftOffIndex];
        touchDownOffset = feet[touchDownIndex];
    }

    static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    static void RebuildSwingProgress(float[] stance, float[] progress)
    {
        Array.Clear(progress, 0, progress.Length);
        int count = stance.Length;
        for (int i = 0; i < count; i++)
        {
            if (stance[i] >= 0.5f)
                continue;
            int since = 0;
            while (since < count && stance[Wrap(i - since - 1, count)] < 0.5f)
                since++;
            int until = 0;
            while (until < count && stance[Wrap(i + until + 1, count)] < 0.5f)
                until++;
            progress[i] = (since + 1f) / Mathf.Max(1f, since + until + 2f);
        }
    }

    static void RemoveSingleSampleIslands(float[] contacts)
    {
        float[] source = (float[])contacts.Clone();
        for (int i = 0; i < contacts.Length; i++)
        {
            float previous = source[Wrap(i - 1, source.Length)];
            float next = source[Wrap(i + 1, source.Length)];
            if (Mathf.Approximately(previous, next) && !Mathf.Approximately(source[i], previous))
                contacts[i] = previous;
        }
    }

    static void EnsureMinimumSupport(float[][] stance, Vector3[][] feet)
    {
        for (int sample = 0; sample < SampleCount; sample++)
        {
            int support = 0;
            for (int leg = 0; leg < stance.Length; leg++)
                if (stance[leg][sample] >= 0.5f) support++;
            while (support < 2)
            {
                int lowestLeg = -1;
                float lowestHeight = float.PositiveInfinity;
                for (int leg = 0; leg < stance.Length; leg++)
                {
                    if (stance[leg][sample] >= 0.5f
                        || feet[leg][sample].y >= lowestHeight)
                        continue;
                    lowestLeg = leg;
                    lowestHeight = feet[leg][sample].y;
                }
                if (lowestLeg < 0)
                    break;
                stance[lowestLeg][sample] = 1f;
                support++;
            }
        }
    }

    static float CalculateBodyHeight(
        Transform root, Transform body, Transform[] feet)
    {
        Vector3 localBody = root.InverseTransformPoint(body.position);
        float sole = 0f;
        for (int i = 0; i < feet.Length; i++)
            sole += root.InverseTransformPoint(feet[i].position).y;
        return Mathf.Max(0.01f, localBody.y - sole / feet.Length);
    }

    static int Wrap(int value, int count)
    {
        value %= count;
        return value < 0 ? value + count : value;
    }
}
