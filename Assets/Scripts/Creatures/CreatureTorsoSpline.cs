using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CreatureTorsoControlPoint
{
    public Vector3 localPosition;
    [Min(0.08f)] public float width = 1f;
    [Min(0.08f)] public float height = 1f;
    public float rollDegrees;
    [Min(0f)] public float blendRadius = 0.2f;
    [Range(0.2f, 2f)] public float taper = 1f;

    public CreatureTorsoControlPoint Clone()
    {
        return (CreatureTorsoControlPoint)MemberwiseClone();
    }
}

[Serializable]
public sealed class CreatureTorsoSpline
{
    public const int MinimumPointCount = 3;
    public const int MaximumPointCount = 10;

    public int version = 1;
    public List<CreatureTorsoControlPoint> points = new List<CreatureTorsoControlPoint>();

    public CreatureTorsoSpline Clone()
    {
        var clone = new CreatureTorsoSpline { version = version };
        if (points != null)
            foreach (CreatureTorsoControlPoint point in points)
                clone.points.Add(point != null ? point.Clone() : null);
        return clone;
    }

    public bool Validate(out string error)
    {
        if (points == null || points.Count < MinimumPointCount || points.Count > MaximumPointCount)
        {
            error = $"Torso requires {MinimumPointCount}-{MaximumPointCount} control points.";
            return false;
        }

        for (int i = 0; i < points.Count; i++)
        {
            CreatureTorsoControlPoint point = points[i];
            if (point == null || !IsFinite(point.localPosition) || !IsFinite(point.width)
                || !IsFinite(point.height) || !IsFinite(point.rollDegrees)
                || !IsFinite(point.blendRadius) || !IsFinite(point.taper))
            {
                error = $"Torso point {i} contains a non-finite value.";
                return false;
            }
            if (point.width < 0.08f || point.height < 0.08f || point.taper < 0.2f)
            {
                error = $"Torso point {i} is too small.";
                return false;
            }
            if (i > 0 && Vector3.Distance(points[i - 1].localPosition, point.localPosition) < 0.08f)
            {
                error = $"Torso points {i - 1} and {i} overlap.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public static CreatureTorsoSpline CreateLegacyFallback(float length, float width, float height, int count = 5)
    {
        count = Mathf.Clamp(count, MinimumPointCount, MaximumPointCount);
        var spline = new CreatureTorsoSpline();
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float profile = Mathf.Lerp(0.62f, 1f, Mathf.Sin(t * Mathf.PI));
            spline.points.Add(new CreatureTorsoControlPoint
            {
                localPosition = new Vector3(0f, 0f, Mathf.Lerp(-length * 0.5f, length * 0.5f, t)),
                width = Mathf.Max(0.12f, width * profile),
                height = Mathf.Max(0.12f, height * profile),
                blendRadius = Mathf.Min(width, height) * 0.2f,
                taper = profile
            });
        }
        return spline;
    }

    static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

[Serializable]
public sealed class CreatureTorsoPresetData
{
    public int version = 1;
    public int generatorVersion;
    public int seed;
    public CreatureTopology topology;
    public CreatureTorsoSpline torsoSpline;
}

public static class CreatureTorsoSplineGenerator
{
    public static CreatureTorsoSpline Generate(CreatureGenome genome)
    {
        var random = new TorsoRandom(unchecked((uint)(genome.seed * 486187739 + 104729)));
        int count = genome.topology == CreatureTopology.Serpentine
            ? Mathf.Clamp(genome.spineSegmentCount, 7, CreatureTorsoSpline.MaximumPointCount)
            : random.Range(4, 8);
        var spline = new CreatureTorsoSpline();
        bool v4Quadruped = genome.generatorVersion >= CreaturePhenotype.CurrentVersion
            && genome.topology == CreatureTopology.Quadruped;
        float curveX = random.Range(v4Quadruped ? -0.08f : -0.32f, v4Quadruped ? 0.08f : 0.32f)
            * genome.bodyWidth;
        float curveY = random.Range(v4Quadruped ? -0.08f : -0.28f, v4Quadruped ? 0.14f : 0.4f)
            * genome.bodyHeight;
        float wave = random.Range(v4Quadruped ? -0.05f : -0.24f, v4Quadruped ? 0.05f : 0.24f)
            * genome.bodyWidth;
        float massCenter = random.Range(v4Quadruped ? 0.38f : 0.28f, v4Quadruped ? 0.62f : 0.72f);
        float waist = random.Range(v4Quadruped ? 0.06f : 0.08f, v4Quadruped ? 0.16f : 0.38f);
        float roll = random.Range(v4Quadruped ? -5f : -24f, v4Quadruped ? 5f : 24f);

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float endProfile = Mathf.Lerp(v4Quadruped ? 0.72f : 0.5f, 1f, Mathf.Sin(t * Mathf.PI));
            float massProfile = Mathf.Exp(-Mathf.Pow((t - massCenter) / 0.28f, 2f));
            float waistProfile = 1f - waist * Mathf.Exp(-Mathf.Pow((t - 0.5f) / 0.16f, 2f));
            float widthScale = Mathf.Clamp(endProfile * waistProfile
                + massProfile * (v4Quadruped ? 0.18f : 0.24f), 0.42f, v4Quadruped ? 1.2f : 1.35f);
            float heightScale = Mathf.Clamp(endProfile + massProfile * random.Range(
                v4Quadruped ? 0.06f : 0.08f, v4Quadruped ? 0.16f : 0.3f),
                0.42f, v4Quadruped ? 1.18f : 1.35f);
            float arch = Mathf.Sin(t * Mathf.PI);
            float sideWave = Mathf.Sin(t * Mathf.PI * 2f + random.Range(-0.3f, 0.3f));

            spline.points.Add(new CreatureTorsoControlPoint
            {
                localPosition = new Vector3(
                    curveX * arch + wave * sideWave,
                    curveY * arch + random.Range(v4Quadruped ? -0.015f : -0.04f,
                        v4Quadruped ? 0.015f : 0.04f) * genome.bodyHeight,
                    Mathf.Lerp(-genome.bodyLength * 0.5f, genome.bodyLength * 0.5f, t)),
                width = Mathf.Max(0.16f, genome.bodyWidth * widthScale
                    * random.Range(v4Quadruped ? 0.97f : 0.9f, v4Quadruped ? 1.03f : 1.1f)),
                height = Mathf.Max(0.16f, genome.bodyHeight * heightScale
                    * random.Range(v4Quadruped ? 0.97f : 0.9f, v4Quadruped ? 1.03f : 1.1f)),
                rollDegrees = Mathf.Lerp(-roll, roll, t)
                    + random.Range(v4Quadruped ? -2f : -8f, v4Quadruped ? 2f : 8f),
                blendRadius = Mathf.Min(genome.bodyWidth, genome.bodyHeight) * random.Range(0.14f, 0.28f),
                taper = v4Quadruped
                    ? Mathf.Lerp(0.86f, 1f, Mathf.Sin(t * Mathf.PI))
                    : Mathf.Clamp(endProfile, 0.35f, 1.3f)
            });
        }

        return spline;
    }

    struct TorsoRandom
    {
        uint state;

        public TorsoRandom(uint seed)
        {
            state = seed == 0 ? 0xA341316Cu : seed;
        }

        public float Range(float minimum, float maximum)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return Mathf.LerpUnclamped(minimum, maximum, (state & 0x00FFFFFFu) / 16777216f);
        }

        public int Range(int minimum, int maximum)
        {
            return minimum + Mathf.FloorToInt(Range(0f, 1f) * (maximum - minimum));
        }
    }
}
