using System;
using UnityEngine;

[Serializable]
public sealed class NmsProceduralGaitBoneTrack
{
    [SerializeField] string boneName;
    [SerializeField] Vector3[] normalizedPositionOffsets = Array.Empty<Vector3>();
    [SerializeField] Quaternion[] rotationOffsets = Array.Empty<Quaternion>();

    public string BoneName => boneName;
    public int SampleCount => Mathf.Min(
        normalizedPositionOffsets?.Length ?? 0,
        rotationOffsets?.Length ?? 0);

    public void Sample(float phase, out Vector3 position, out Quaternion rotation)
    {
        int count = SampleCount;
        if (count == 0)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return;
        }
        SampleIndices(phase, count, out int a, out int b, out float t);
        position = Vector3.Lerp(normalizedPositionOffsets[a], normalizedPositionOffsets[b], t);
        rotation = Quaternion.Slerp(rotationOffsets[a], rotationOffsets[b], t);
    }

#if UNITY_EDITOR
    public NmsProceduralGaitBoneTrack(
        string name, Vector3[] positions, Quaternion[] rotations)
    {
        boneName = name;
        normalizedPositionOffsets = positions ?? Array.Empty<Vector3>();
        rotationOffsets = rotations ?? Array.Empty<Quaternion>();
    }
#endif

    internal static void SampleIndices(
        float phase, int count, out int a, out int b, out float t)
    {
        float sample = Mathf.Repeat(phase, 1f) * count;
        a = Mathf.FloorToInt(sample) % count;
        b = (a + 1) % count;
        t = sample - Mathf.Floor(sample);
    }
}

[Serializable]
public sealed class NmsProceduralGaitLegTrack
{
    [SerializeField] string chainId;
    [SerializeField] Vector3[] normalizedFootOffsets = Array.Empty<Vector3>();
    [SerializeField] Vector3[] firstPoleDirectionsLocal = Array.Empty<Vector3>();
    [SerializeField] Vector3[] secondPoleDirectionsLocal = Array.Empty<Vector3>();
    [SerializeField] float[] stanceWeights = Array.Empty<float>();
    [SerializeField] float[] swingProgress = Array.Empty<float>();
    [SerializeField, Range(0f, 1f)] float liftOffPhase;
    [SerializeField, Range(0f, 1f)] float touchDownPhase;
    [SerializeField] Vector3 normalizedLiftOffOffset;
    [SerializeField] Vector3 normalizedTouchDownOffset;

    public string ChainId => chainId;
    public int SampleCount => normalizedFootOffsets?.Length ?? 0;
    public int FirstPoleSampleCount => firstPoleDirectionsLocal?.Length ?? 0;
    public int SecondPoleSampleCount => secondPoleDirectionsLocal?.Length ?? 0;
    public float LiftOffPhase => liftOffPhase;
    public float TouchDownPhase => touchDownPhase;
    public Vector3 NormalizedLiftOffOffset => normalizedLiftOffOffset;
    public Vector3 NormalizedTouchDownOffset => normalizedTouchDownOffset;

    public void Sample(
        float phase,
        out Vector3 normalizedFootOffset,
        out float stanceWeight,
        out float normalizedSwingProgress,
        out Vector3 firstPoleLocal,
        out Vector3 secondPoleLocal)
    {
        int count = SampleCount;
        if (count == 0)
        {
            normalizedFootOffset = Vector3.zero;
            stanceWeight = 1f;
            normalizedSwingProgress = 0f;
            firstPoleLocal = Vector3.forward;
            secondPoleLocal = Vector3.forward;
            return;
        }
        NmsProceduralGaitBoneTrack.SampleIndices(
            phase, count, out int a, out int b, out float t);
        normalizedFootOffset = Vector3.Lerp(
            normalizedFootOffsets[a], normalizedFootOffsets[b], t);
        stanceWeight = Mathf.Lerp(Sample(stanceWeights, a, 1f), Sample(stanceWeights, b, 1f), t);
        normalizedSwingProgress = IsSwingPhase(phase)
            ? CircularInverseLerp(liftOffPhase, touchDownPhase, phase)
            : 0f;
        firstPoleLocal = SampleDirection(firstPoleDirectionsLocal, a, b, t);
        secondPoleLocal = SampleDirection(secondPoleDirectionsLocal, a, b, t);
    }

    public bool IsComplete(int requiredSamples, out string error)
    {
        if (requiredSamples < 1
            || normalizedFootOffsets == null || normalizedFootOffsets.Length != requiredSamples
            || firstPoleDirectionsLocal == null
            || firstPoleDirectionsLocal.Length != requiredSamples
            || secondPoleDirectionsLocal == null
            || secondPoleDirectionsLocal.Length != requiredSamples
            || stanceWeights == null || stanceWeights.Length != requiredSamples
            || swingProgress == null || swingProgress.Length != requiredSamples)
        {
            error = $"Leg track '{chainId}' has incomplete schema-2 sample arrays.";
            return false;
        }
        if (!Finite(normalizedLiftOffOffset) || !Finite(normalizedTouchDownOffset)
            || CircularDistance(liftOffPhase, touchDownPhase) <= 0.001f)
        {
            error = $"Leg track '{chainId}' has an invalid swing interval.";
            return false;
        }
        for (int i = 0; i < requiredSamples; i++)
        {
            if (!Finite(normalizedFootOffsets[i])
                || !UnitDirection(firstPoleDirectionsLocal[i])
                || !UnitDirection(secondPoleDirectionsLocal[i])
                || !Finite(stanceWeights[i]) || !Finite(swingProgress[i]))
            {
                error = $"Leg track '{chainId}' contains invalid sample {i}.";
                return false;
            }
            int next = (i + 1) % requiredSamples;
            if (Vector3.Dot(firstPoleDirectionsLocal[i], firstPoleDirectionsLocal[next]) < 0f
                || Vector3.Dot(secondPoleDirectionsLocal[i], secondPoleDirectionsLocal[next]) < 0f)
            {
                error = $"Leg track '{chainId}' has a pole-vector hemisphere flip at {i}.";
                return false;
            }
        }
        error = null;
        return true;
    }

    bool IsSwingPhase(float phase)
    {
        float fromLift = Mathf.Repeat(phase - liftOffPhase, 1f);
        return fromLift <= CircularDistance(liftOffPhase, touchDownPhase) + 0.0001f;
    }

    static float CircularInverseLerp(float start, float end, float phase)
    {
        float length = CircularDistance(start, end);
        return length > 0.0001f
            ? Mathf.Clamp01(Mathf.Repeat(phase - start, 1f) / length)
            : 0f;
    }

    static float CircularDistance(float start, float end)
    {
        return Mathf.Repeat(end - start, 1f);
    }

    static Vector3 SampleDirection(Vector3[] values, int a, int b, float t)
    {
        if (values == null || a < 0 || b < 0
            || a >= values.Length || b >= values.Length)
            return Vector3.forward;
        Vector3 first = values[a];
        Vector3 second = values[b];
        if (Vector3.Dot(first, second) < 0f)
            second = -second;
        Vector3 result = Vector3.Slerp(first, second, t);
        return result.sqrMagnitude > 0.000001f ? result.normalized : Vector3.forward;
    }

    static float Sample(float[] values, int index, float fallback)
    {
        return values != null && index >= 0 && index < values.Length
            ? values[index] : fallback;
    }

    static bool Finite(Vector3 value)
    {
        return Finite(value.x) && Finite(value.y) && Finite(value.z);
    }

    static bool Finite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    static bool UnitDirection(Vector3 value)
    {
        return Finite(value) && Mathf.Abs(value.sqrMagnitude - 1f) <= 0.01f;
    }

#if UNITY_EDITOR
    public NmsProceduralGaitLegTrack(
        string id,
        Vector3[] feet,
        Vector3[] firstPoles,
        Vector3[] secondPoles,
        float[] contacts,
        float[] progress,
        float sourceLiftOffPhase,
        float sourceTouchDownPhase,
        Vector3 sourceLiftOffOffset,
        Vector3 sourceTouchDownOffset)
    {
        chainId = id;
        normalizedFootOffsets = feet ?? Array.Empty<Vector3>();
        firstPoleDirectionsLocal = firstPoles ?? Array.Empty<Vector3>();
        secondPoleDirectionsLocal = secondPoles ?? Array.Empty<Vector3>();
        stanceWeights = contacts ?? Array.Empty<float>();
        swingProgress = progress ?? Array.Empty<float>();
        liftOffPhase = Mathf.Repeat(sourceLiftOffPhase, 1f);
        touchDownPhase = Mathf.Repeat(sourceTouchDownPhase, 1f);
        normalizedLiftOffOffset = sourceLiftOffOffset;
        normalizedTouchDownOffset = sourceTouchDownOffset;
    }
#endif
}

[CreateAssetMenu(
    menuName = "Creatures/NMS Procedural Gait Template",
    fileName = "NmsProceduralGaitTemplate")]
public sealed class NmsProceduralGaitTemplate : ScriptableObject
{
    public const int CurrentSchema = 2;

    [SerializeField] int schemaVersion = CurrentSchema;
    [SerializeField] string familyId = "AntelopeQuadruped";
    [SerializeField, Min(8)] int sampleCount = 64;
    [SerializeField, Min(0.01f)] float cycleDuration = 1f;
    [SerializeField, Min(0.01f)] float normalizedDistancePerCycle = 1f;
    [SerializeField, Min(0.01f)] float referenceBodyHeight = 1f;
    [SerializeField, Min(0.01f)] float referenceRigScale = 1f;
    [SerializeField] NmsProceduralGaitBoneTrack[] boneTracks =
        Array.Empty<NmsProceduralGaitBoneTrack>();
    [SerializeField] NmsProceduralGaitLegTrack[] legTracks =
        Array.Empty<NmsProceduralGaitLegTrack>();

    public int SchemaVersion => schemaVersion;
    public string FamilyId => familyId;
    public int SampleCount => sampleCount;
    public float CycleDuration => cycleDuration;
    public float NormalizedDistancePerCycle => normalizedDistancePerCycle;
    public float ReferenceBodyHeight => referenceBodyHeight;
    public float ReferenceRigScale => referenceRigScale;
    public NmsProceduralGaitBoneTrack[] BoneTracks => boneTracks;

    public bool IsValidFor(string requestedFamily, int requiredLegs, out string error)
    {
        if (schemaVersion != CurrentSchema)
        {
            error = $"Unsupported gait-template schema {schemaVersion}.";
            return false;
        }
        if (!string.Equals(familyId, requestedFamily, StringComparison.Ordinal))
        {
            error = $"Gait template '{familyId}' cannot drive '{requestedFamily}'.";
            return false;
        }
        if (sampleCount < 8 || boneTracks == null || boneTracks.Length == 0
            || legTracks == null || legTracks.Length != requiredLegs)
        {
            error = "Gait template has incomplete body or leg samples.";
            return false;
        }
        for (int i = 0; i < boneTracks.Length; i++)
            if (boneTracks[i] == null || boneTracks[i].SampleCount != sampleCount)
            {
                error = "Gait template contains an incomplete bone track.";
                return false;
            }
        for (int i = 0; i < legTracks.Length; i++)
            if (legTracks[i] == null)
            {
                error = "Gait template contains a null leg track.";
                return false;
            }
            else if (!legTracks[i].IsComplete(sampleCount, out error))
            {
                return false;
            }
        error = null;
        return true;
    }

    public bool TryGetLeg(string chainId, out NmsProceduralGaitLegTrack result)
    {
        if (legTracks != null)
            for (int i = 0; i < legTracks.Length; i++)
                if (legTracks[i] != null
                    && string.Equals(legTracks[i].ChainId, chainId, StringComparison.Ordinal))
                {
                    result = legTracks[i];
                    return true;
                }
        result = null;
        return false;
    }

#if UNITY_EDITOR
    public void ConfigureEditor(
        string sourceFamily,
        int samples,
        float duration,
        float distancePerCycle,
        float bodyHeight,
        float rigReference,
        NmsProceduralGaitBoneTrack[] bones,
        NmsProceduralGaitLegTrack[] legs)
    {
        schemaVersion = CurrentSchema;
        familyId = sourceFamily;
        sampleCount = Mathf.Max(8, samples);
        cycleDuration = Mathf.Max(0.01f, duration);
        normalizedDistancePerCycle = Mathf.Max(0.01f, distancePerCycle);
        referenceBodyHeight = Mathf.Max(0.01f, bodyHeight);
        referenceRigScale = Mathf.Max(0.01f, rigReference);
        boneTracks = bones ?? Array.Empty<NmsProceduralGaitBoneTrack>();
        legTracks = legs ?? Array.Empty<NmsProceduralGaitLegTrack>();
    }
#endif
}
