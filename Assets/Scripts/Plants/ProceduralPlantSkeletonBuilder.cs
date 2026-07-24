using System;
using System.Collections.Generic;
using UnityEngine;

public static class ProceduralPlantSkeletonBuilder
{
    const float GoldenAngle = 137.507764f;
    const int MaxBranches = 220;

    struct PendingBranch
    {
        public int parentIndex;
        public int depth;
        public Vector3 start;
        public Vector3 direction;
        public float length;
        public float radius;
        public float phase;
    }

    public static PlantSkeleton Build(PlantSpeciesSnapshot species)
    {
        if (species == null)
            throw new ArgumentNullException(nameof(species));

        PlantGenerationParameters p = species.Parameters.Clone();
        p.Clamp();
        var random = new System.Random(species.Seed);
        var skeleton = new PlantSkeleton();
        float ageGrowth = Smooth01(Mathf.InverseLerp(0f, 0.88f, p.age));
        float trunkLength = p.trunkLength * Mathf.Lerp(0.12f, 1f, ageGrowth);
        float trunkRadius = p.trunkRadius * Mathf.Lerp(0.2f, 1f, Mathf.Sqrt(ageGrowth));
        int trunkSegments = Mathf.Max(3, Mathf.RoundToInt(p.trunkSegments * Mathf.Lerp(0.45f, 1f, ageGrowth)));

        Vector3[] trunk = BuildCurvedPoints(
            Vector3.zero,
            Vector3.up,
            trunkLength,
            trunkSegments,
            p.curvature,
            p.phototropism,
            random,
            0f);
        skeleton.Branches.Add(new PlantBranch(
            -1,
            0,
            trunkRadius,
            Mathf.Max(0.012f, trunkRadius * Mathf.Pow(p.taper, trunkSegments)),
            trunk));

        int effectiveDepth = GetEffectiveDepth(p);
        var pending = new Queue<PendingBranch>();
        AddBudsForBranch(skeleton, 0, p, effectiveDepth, random, pending);

        while (pending.Count > 0 && skeleton.Branches.Count < MaxBranches)
        {
            PendingBranch item = pending.Dequeue();
            int segments = Mathf.Clamp(
                Mathf.RoundToInt(p.trunkSegments * Mathf.Pow(0.78f, item.depth)),
                3,
                10);
            Vector3[] points = BuildCurvedPoints(
                item.start,
                item.direction,
                item.length,
                segments,
                p.curvature * (1f + item.depth * 0.12f),
                p.phototropism,
                random,
                item.phase);
            float endRadius = Mathf.Max(0.008f, item.radius * Mathf.Pow(p.taper, segments));
            int branchIndex = skeleton.Branches.Count;
            skeleton.Branches.Add(new PlantBranch(
                item.parentIndex,
                item.depth,
                item.radius,
                endRadius,
                points));
            AddBudsForBranch(skeleton, branchIndex, p, effectiveDepth, random, pending);
        }

        BuildLeaves(skeleton, species, random);
        CalculateBounds(skeleton);
        return skeleton;
    }

    static int GetEffectiveDepth(PlantGenerationParameters p)
    {
        if (p.age < 0.2f)
            return 0;
        if (p.age < 0.45f)
            return Mathf.Min(1, p.branchDepth);
        if (p.age < 0.7f)
            return Mathf.Min(2, p.branchDepth);
        return p.branchDepth;
    }

    static void AddBudsForBranch(
        PlantSkeleton skeleton,
        int branchIndex,
        PlantGenerationParameters p,
        int effectiveDepth,
        System.Random random,
        Queue<PendingBranch> output)
    {
        PlantBranch parent = skeleton.Branches[branchIndex];
        if (parent.Depth >= effectiveDepth)
            return;

        int depth = parent.Depth + 1;
        int budCount = Mathf.Max(1, Mathf.RoundToInt(
            p.budsPerBranch * Mathf.Lerp(0.55f, 1f, p.age) * Mathf.Pow(0.8f, parent.Depth)));
        float parentLength = PolylineLength(parent.Points);
        float childLength = parentLength * p.lengthDecay;
        float childRadius = Mathf.Min(
            parent.EndRadius * 0.92f,
            parent.StartRadius * p.radiusDecay);

        // The sum of child cross sections stays below the parent load-bearing area.
        float areaLimitedRadius = parent.EndRadius * Mathf.Sqrt(0.82f / Mathf.Max(1, budCount));
        childRadius = Mathf.Min(childRadius, areaLimitedRadius);

        for (int i = 0; i < budCount; i++)
        {
            float along = Mathf.Lerp(0.38f, 0.88f, (i + 0.35f) / budCount);
            SamplePolyline(parent.Points, along, out Vector3 position, out Vector3 tangent);
            float azimuth = GoldenAngle * (i + 1 + parent.Depth * 3)
                + SignedRandom(random) * 16f;
            Vector3 radial = RotateAroundAxis(Perpendicular(tangent), tangent, azimuth);
            float branchAngle = p.branchAngle * Mathf.Lerp(0.72f, 1.12f, (float)random.NextDouble());
            Vector3 direction = Quaternion.AngleAxis(branchAngle, radial) * tangent;
            direction = Vector3.Slerp(direction, Vector3.up, p.phototropism * 0.32f).normalized;
            direction = ApplyFamilyDirection(speciesFamily: null, direction, p);
            output.Enqueue(new PendingBranch
            {
                parentIndex = branchIndex,
                depth = depth,
                start = position - tangent * childRadius * 0.35f,
                direction = direction,
                length = childLength * Mathf.Lerp(0.78f, 1.08f, (float)random.NextDouble()),
                radius = childRadius,
                phase = azimuth
            });
        }

        if (p.apicalDominance > 0.12f && parent.Depth > 0)
        {
            Vector3 tangent = (parent.Points[parent.Points.Length - 1]
                - parent.Points[parent.Points.Length - 2]).normalized;
            output.Enqueue(new PendingBranch
            {
                parentIndex = branchIndex,
                depth = depth,
                start = parent.Points[parent.Points.Length - 1] - tangent * childRadius * 0.25f,
                direction = Vector3.Slerp(tangent, Vector3.up, p.phototropism * 0.28f).normalized,
                length = childLength * Mathf.Lerp(0.55f, 0.88f, p.apicalDominance),
                radius = childRadius * 0.82f,
                phase = parent.Depth * GoldenAngle
            });
        }
    }

    static Vector3 ApplyFamilyDirection(object speciesFamily, Vector3 direction, PlantGenerationParameters p)
    {
        // Family-specific silhouettes are primarily encoded in their recipes. Keeping this
        // projection common ensures all branches stay inside a readable crown envelope.
        float horizontal = new Vector2(direction.x, direction.z).magnitude;
        float maxHorizontal = Mathf.Lerp(0.35f, 0.9f, Mathf.InverseLerp(0.2f, 2f, p.crownWidth));
        if (horizontal > maxHorizontal)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * maxHorizontal;
            direction = (flat + Vector3.up * Mathf.Max(0.2f, direction.y)).normalized;
        }
        return direction;
    }

    static Vector3[] BuildCurvedPoints(
        Vector3 start,
        Vector3 initialDirection,
        float length,
        int segments,
        float curvature,
        float phototropism,
        System.Random random,
        float phase)
    {
        var points = new Vector3[segments + 1];
        points[0] = start;
        Vector3 direction = initialDirection.normalized;
        float step = length / segments;
        Vector3 bendAxis = RotateAroundAxis(Perpendicular(direction), direction, phase);
        for (int i = 1; i <= segments; i++)
        {
            float wave = Mathf.Sin((i / (float)segments) * Mathf.PI + phase * Mathf.Deg2Rad);
            float bend = curvature * wave * Mathf.Lerp(0.4f, 1f, (float)random.NextDouble());
            direction = (direction
                + bendAxis * bend * 0.16f
                + Vector3.up * phototropism * 0.07f).normalized;
            points[i] = points[i - 1] + direction * step;
        }
        return points;
    }

    static void BuildLeaves(PlantSkeleton skeleton, PlantSpeciesSnapshot species, System.Random random)
    {
        PlantGenerationParameters p = species.Parameters;
        if (p.leafMode == PlantLeafMode.None || p.age < 0.42f || p.leafDensity <= 0f)
            return;

        float leafAge = Smooth01(Mathf.InverseLerp(0.42f, 0.82f, p.age));
        int deepest = 0;
        foreach (PlantBranch branch in skeleton.Branches)
            deepest = Mathf.Max(deepest, branch.Depth);

        for (int branchIndex = 0; branchIndex < skeleton.Branches.Count; branchIndex++)
        {
            PlantBranch branch = skeleton.Branches[branchIndex];
            if (branch.Depth < Mathf.Max(1, deepest - 1))
                continue;
            int count = Mathf.Clamp(
                Mathf.RoundToInt((2f + branch.Depth) * p.leafDensity * leafAge),
                1,
                12);
            Vector3 end = branch.Points[branch.Points.Length - 1];
            Vector3 tangent = (end - branch.Points[branch.Points.Length - 2]).normalized;
            for (int i = 0; i < count; i++)
            {
                float azimuth = GoldenAngle * (i + 1) + SignedRandom(random) * 12f;
                Vector3 side = RotateAroundAxis(Perpendicular(tangent), tangent, azimuth);
                Vector3 direction = Vector3.Slerp(
                    side,
                    Vector3.up,
                    p.leafUpwardBias).normalized;
                skeleton.Leaves.Add(new PlantLeafAnchor
                {
                    position = end + tangent * (0.03f * i),
                    direction = direction,
                    normal = Vector3.Cross(direction, tangent).normalized,
                    scale = p.leafSize * Mathf.Lerp(0.72f, 1.24f, (float)random.NextDouble()) * leafAge,
                    branchIndex = branchIndex
                });
            }
        }
    }

    static void CalculateBounds(PlantSkeleton skeleton)
    {
        if (skeleton.Branches.Count == 0)
        {
            skeleton.Bounds = new Bounds(Vector3.zero, Vector3.zero);
            return;
        }
        Bounds bounds = new Bounds(skeleton.Branches[0].Points[0], Vector3.zero);
        foreach (PlantBranch branch in skeleton.Branches)
            foreach (Vector3 point in branch.Points)
                bounds.Encapsulate(point);
        foreach (PlantLeafAnchor leaf in skeleton.Leaves)
        {
            bounds.Encapsulate(leaf.position + leaf.direction * leaf.scale);
            bounds.Encapsulate(leaf.position - leaf.direction * leaf.scale);
        }
        bounds.Expand(0.08f);
        skeleton.Bounds = bounds;
    }

    static float PolylineLength(Vector3[] points)
    {
        float length = 0f;
        for (int i = 1; i < points.Length; i++)
            length += Vector3.Distance(points[i - 1], points[i]);
        return length;
    }

    static void SamplePolyline(Vector3[] points, float t, out Vector3 position, out Vector3 tangent)
    {
        float scaled = Mathf.Clamp01(t) * (points.Length - 1);
        int index = Mathf.Min(points.Length - 2, Mathf.FloorToInt(scaled));
        float local = scaled - index;
        position = Vector3.Lerp(points[index], points[index + 1], local);
        tangent = (points[index + 1] - points[index]).normalized;
    }

    static Vector3 Perpendicular(Vector3 direction)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.92f
            ? Vector3.up
            : Vector3.right;
        return Vector3.Cross(direction, reference).normalized;
    }

    static Vector3 RotateAroundAxis(Vector3 vector, Vector3 axis, float degrees)
    {
        return Quaternion.AngleAxis(degrees, axis) * vector;
    }

    static float SignedRandom(System.Random random)
    {
        return (float)(random.NextDouble() * 2d - 1d);
    }

    static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
