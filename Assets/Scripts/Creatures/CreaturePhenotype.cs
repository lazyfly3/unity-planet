using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CreaturePhenotype
{
    public const int CurrentVersion = CreatureGenerationVersions.ImplicitV4;

    public readonly int generatorVersion;
    public readonly int seed;
    public readonly int shapeHash;
    public readonly CreatureTopology topology;
    public readonly CreatureLocomotionArchetype locomotionArchetype;
    public readonly CreatureTorsoSpline torsoSpline;
    public readonly CreatureBodyGraph graph;
    public readonly Vector3[] nodeLocalPositions;
    public readonly Quaternion[] nodeLocalRotations;
    public readonly Vector3[] nodePositions;
    public readonly Quaternion[] nodeRotations;
    public readonly CreaturePhenotypeLeg[] legs;
    public readonly Bounds fieldBounds;
    public readonly Vector3 centerOfMass;
    public readonly float mass;
    public readonly float bodyClearance;
    public readonly float walkSpeed;
    public readonly float trotSpeed;
    public readonly float runSpeed;
    public readonly float strideLength;
    public readonly float stepHeight;
    public readonly float baseGaitFrequency;
    public readonly float slowStanceDutyFactor;
    public readonly float fastStanceDutyFactor;
    public readonly float neutralLegExtension;
    public readonly float frontLoadFraction;
    public readonly bool allowsAerialGait;

    public CreaturePhenotype(
        CreatureGenome genome,
        CreatureTorsoSpline spline,
        CreatureBodyGraph sourceGraph,
        Vector3[] localPositions,
        Quaternion[] localRotations,
        Vector3[] positions,
        Quaternion[] rotations,
        CreaturePhenotypeLeg[] phenotypeLegs,
        Bounds bounds,
        Vector3 calculatedCenterOfMass,
        float calculatedMass,
        float clearance,
        float calculatedWalkSpeed,
        float calculatedTrotSpeed,
        float calculatedRunSpeed,
        float calculatedStrideLength,
        float calculatedStepHeight,
        float calculatedGaitFrequency,
        float calculatedSlowStanceDutyFactor,
        float calculatedFastStanceDutyFactor,
        float calculatedNeutralLegExtension,
        float calculatedFrontLoadFraction,
        bool calculatedAllowsAerialGait,
        int calculatedShapeHash)
    {
        generatorVersion = CurrentVersion;
        seed = genome.seed;
        shapeHash = calculatedShapeHash;
        topology = genome.topology;
        locomotionArchetype = genome.locomotionArchetype;
        torsoSpline = spline;
        graph = sourceGraph;
        nodeLocalPositions = localPositions;
        nodeLocalRotations = localRotations;
        nodePositions = positions;
        nodeRotations = rotations;
        legs = phenotypeLegs;
        fieldBounds = bounds;
        centerOfMass = calculatedCenterOfMass;
        mass = calculatedMass;
        bodyClearance = clearance;
        walkSpeed = calculatedWalkSpeed;
        trotSpeed = calculatedTrotSpeed;
        runSpeed = calculatedRunSpeed;
        strideLength = calculatedStrideLength;
        stepHeight = calculatedStepHeight;
        baseGaitFrequency = calculatedGaitFrequency;
        slowStanceDutyFactor = calculatedSlowStanceDutyFactor;
        fastStanceDutyFactor = calculatedFastStanceDutyFactor;
        neutralLegExtension = calculatedNeutralLegExtension;
        frontLoadFraction = calculatedFrontLoadFraction;
        allowsAerialGait = calculatedAllowsAerialGait;
    }

    public void ApplyToGraph(CreatureBodyGraph target)
    {
        if (target == null || target.nodes == null || target.nodes.Count != nodeLocalPositions.Length)
            throw new ArgumentException("The target body graph does not match this phenotype.", nameof(target));

        target.generatorVersion = CurrentVersion;
        for (int i = 0; i < target.nodes.Count; i++)
        {
            target.nodes[i].localPosition = nodeLocalPositions[i];
            target.nodes[i].localEulerAngles = nodeLocalRotations[i].eulerAngles;
        }
    }

    public float SampleDistance(Vector3 point)
    {
        float distance = SampleTorsoDistance(torsoSpline, point);
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            if (!IsSoftNode(node.type))
                continue;

            float primitiveDistance;
            if (node.type == CreatureBodyNodeType.Head
                || node.type == CreatureBodyNodeType.Muzzle
                || node.type == CreatureBodyNodeType.Foot
                || node.type == CreatureBodyNodeType.Hand)
            {
                Vector3 primitiveCenter = nodePositions[i];
                int parent = node.parentIndex;
                float parentRadius = parent >= 0
                    ? Mathf.Max(0.035f, graph.nodes[parent].radius)
                    : Mathf.Max(0.035f, node.radius);
                bool ungulateFoot = node.type == CreatureBodyNodeType.Foot
                    && locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate;
                if (node.type == CreatureBodyNodeType.Foot)
                    primitiveCenter += nodeRotations[i] * Vector3.forward
                        * (node.size.z * (ungulateFoot ? 0.08f : 0.18f));
                primitiveDistance = SampleEllipsoid(point, primitiveCenter, nodeRotations[i], node.size * 0.5f);
                if (node.type == CreatureBodyNodeType.Foot)
                {
                    int lowerNode = parent >= 0 ? graph.nodes[parent].parentIndex : -1;
                    float lowerRadius = lowerNode >= 0
                        ? Mathf.Max(parentRadius, graph.nodes[lowerNode].radius)
                        : parentRadius;
                    float jointRadius = Mathf.Max(ungulateFoot ? 0.065f : 0.18f,
                        Mathf.Max(
                            lowerRadius * (ungulateFoot ? 0.9f : 1.04f),
                            node.size.y * (ungulateFoot ? 0.54f : 0.86f)));
                    Vector3 ankleCenter = nodePositions[i]
                        + nodeRotations[i] * Vector3.forward * (node.size.z * 0.035f)
                        + nodeRotations[i] * Vector3.up * (node.size.y * 0.12f);
                    Vector3 ankleRadii = ungulateFoot
                        ? new Vector3(jointRadius * 0.88f, jointRadius * 1.3f, jointRadius)
                        : new Vector3(jointRadius * 1.06f, jointRadius, jointRadius * 1.15f);
                    float anklePad = SampleEllipsoid(
                        point, ankleCenter, nodeRotations[i], ankleRadii);
                    primitiveDistance = SmoothMinimum(
                        primitiveDistance, anklePad, Mathf.Clamp(node.size.y * 0.34f, 0.025f, 0.14f));
                }
                if (parent >= 0)
                {
                    float connectionStartRadius = parentRadius;
                    if (node.type == CreatureBodyNodeType.Foot)
                    {
                        int lowerNode = graph.nodes[parent].parentIndex;
                        if (lowerNode >= 0)
                            connectionStartRadius = Mathf.Max(
                                connectionStartRadius, graph.nodes[lowerNode].radius * 0.96f);
                    }
                    float endRadius = node.type == CreatureBodyNodeType.Foot
                        ? Mathf.Max(ungulateFoot ? 0.06f : 0.17f,
                            Mathf.Max(
                                node.size.y * (ungulateFoot ? 0.52f : 0.86f),
                                parentRadius * (ungulateFoot ? 0.9f : 1.08f)))
                        : Mathf.Max(0.035f, node.radius);
                    float connection = SampleTaperedCapsule(
                        point,
                        nodePositions[parent],
                        nodePositions[i],
                        connectionStartRadius,
                        endRadius);
                    primitiveDistance = SmoothMinimum(
                        primitiveDistance, connection, Mathf.Clamp(node.radius * 0.4f, 0.02f, 0.16f));
                }
                if (node.type == CreatureBodyNodeType.Foot)
                {
                    // Clip the complete foot assembly, including the capsule end cap. Clipping
                    // only the ellipsoid allowed the connector to protrude as a pointed sole.
                    Vector3 local = Quaternion.Inverse(nodeRotations[i]) * (point - primitiveCenter);
                    float sole = -local.y - node.size.y * 0.42f;
                    primitiveDistance = Mathf.Max(primitiveDistance, sole);
                }
            }
            else
            {
                int parent = node.parentIndex;
                if (parent < 0)
                    continue;
                float parentRadius = Mathf.Max(0.035f, graph.nodes[parent].radius);
                float radius = Mathf.Max(0.035f, node.radius);
                if (node.type == CreatureBodyNodeType.UpperLeg)
                    parentRadius = Mathf.Max(parentRadius * 0.38f, radius * 1.48f);
                primitiveDistance = SampleTaperedCapsule(
                    point, nodePositions[parent], nodePositions[i], parentRadius, radius);
            }

            float smoothing = Mathf.Clamp(node.radius * 0.55f, 0.025f, 0.24f);
            distance = SmoothMinimum(distance, primitiveDistance, smoothing);
        }
        return distance;
    }

    static bool IsSoftNode(CreatureBodyNodeType type)
    {
        return type != CreatureBodyNodeType.Spine
            && type != CreatureBodyNodeType.Torso
            && type != CreatureBodyNodeType.Horn
            && type != CreatureBodyNodeType.BackPlate
            && type != CreatureBodyNodeType.Sensor;
    }

    static float SampleTorsoDistance(CreatureTorsoSpline spline, Vector3 point)
    {
        float distance = float.PositiveInfinity;
        for (int i = 0; i < spline.points.Count - 1; i++)
        {
            CreatureTorsoControlPoint a = spline.points[i];
            CreatureTorsoControlPoint b = spline.points[i + 1];
            Vector3 segment = b.localPosition - a.localPosition;
            float length = Mathf.Max(0.0001f, segment.magnitude);
            Vector3 tangent = segment / length;
            float projection = Vector3.Dot(point - a.localPosition, tangent);
            float t = Mathf.Clamp01(projection / length);
            Vector3 center = Vector3.LerpUnclamped(a.localPosition, b.localPosition, t);
            float width = Mathf.Max(0.04f, Mathf.LerpUnclamped(a.width * a.taper, b.width * b.taper, t) * 0.5f);
            float height = Mathf.Max(0.04f, Mathf.LerpUnclamped(a.height * a.taper, b.height * b.taper, t) * 0.5f);
            BuildFrame(tangent, Mathf.LerpAngle(a.rollDegrees, b.rollDegrees, t), out Vector3 axisX, out Vector3 axisY);
            Vector3 offset = point - center;
            float x = Vector3.Dot(offset, axisX) / width;
            float y = Vector3.Dot(offset, axisY) / height;
            float outside = projection < 0f ? -projection : projection > length ? projection - length : 0f;
            float axialRadius = Mathf.Max(0.04f, Mathf.Min(width, height));
            float segmentDistance = (Mathf.Sqrt(x * x + y * y + outside * outside / (axialRadius * axialRadius)) - 1f)
                * Mathf.Min(width, height);
            float smoothing = Mathf.Max(0.015f, (a.blendRadius + b.blendRadius) * 0.5f);
            distance = SmoothMinimum(distance, segmentDistance, smoothing);
        }
        return distance;
    }

    static float SampleTaperedCapsule(Vector3 point, Vector3 a, Vector3 b, float radiusA, float radiusB)
    {
        Vector3 segment = b - a;
        float denominator = Mathf.Max(0.0001f, segment.sqrMagnitude);
        float t = Mathf.Clamp01(Vector3.Dot(point - a, segment) / denominator);
        return Vector3.Distance(point, Vector3.LerpUnclamped(a, b, t))
            - Mathf.LerpUnclamped(radiusA, radiusB, t);
    }

    static float SampleEllipsoid(Vector3 point, Vector3 center, Quaternion rotation, Vector3 radii)
    {
        Vector3 local = Quaternion.Inverse(rotation) * (point - center);
        radii = new Vector3(Mathf.Max(0.035f, radii.x), Mathf.Max(0.035f, radii.y), Mathf.Max(0.035f, radii.z));
        Vector3 normalized = new Vector3(local.x / radii.x, local.y / radii.y, local.z / radii.z);
        return (normalized.magnitude - 1f) * Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z));
    }

    static void BuildFrame(Vector3 tangent, float rollDegrees, out Vector3 axisX, out Vector3 axisY)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
        axisX = Vector3.Cross(reference, tangent).normalized;
        axisY = Vector3.Cross(tangent, axisX).normalized;
        Quaternion roll = Quaternion.AngleAxis(rollDegrees, tangent);
        axisX = roll * axisX;
        axisY = roll * axisY;
    }

    static float SmoothMinimum(float a, float b, float smoothing)
    {
        if (float.IsPositiveInfinity(a)) return b;
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / Mathf.Max(0.0001f, smoothing));
        return Mathf.LerpUnclamped(b, a, h) - smoothing * h * (1f - h);
    }
}

public sealed class CreaturePhenotypeLeg
{
    public int upperNode;
    public int lowerNode;
    public int distalNode;
    public int footNode;
    public CreatureBodySide side;
    public float longitudinalPosition;
    public int gaitGroup;
    public float upperLength;
    public float lowerLength;
    public float distalLength;
    public float footSoleOffset;
    public Vector3 bendHintLocal;
    public float minimumJointAngle;
    public float maximumJointAngle;
    public float distalForwardFraction;

    public float TotalLength => upperLength + lowerLength + distalLength;
    public bool IsFront => longitudinalPosition >= 0.5f;
    public bool IsLeft => side == CreatureBodySide.Left;
}

public static class CreaturePhenotypeBuilder
{
    public static CreaturePhenotype Build(CreatureGenome genome)
    {
        if (genome == null) throw new ArgumentNullException(nameof(genome));
        if (genome.topology != CreatureTopology.Quadruped)
            throw new ArgumentException("Creature V4 currently supports quadrupeds only.", nameof(genome));
        string splineError = null;
        if (genome.torsoSpline == null || !genome.torsoSpline.Validate(out splineError))
            throw new ArgumentException(splineError ?? "A valid torso spline is required.", nameof(genome));
        string graphError = null;
        if (genome.bodyGraph == null || !genome.bodyGraph.Validate(out graphError))
            throw new ArgumentException(graphError ?? "A valid body graph is required.", nameof(genome));

        CreatureBodyGraph graph = genome.bodyGraph;
        int count = graph.nodes.Count;
        var localPositions = new Vector3[count];
        var localRotations = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            localPositions[i] = graph.nodes[i].localPosition;
            localRotations[i] = Quaternion.Euler(graph.nodes[i].localEulerAngles);
        }

        FitSocketsToTorso(genome, graph, localPositions, localRotations);
        CalculateWorldPose(graph, localPositions, localRotations, out Vector3[] positions, out Quaternion[] rotations);
        CreaturePhenotypeLeg[] legs = BuildLegs(genome, graph, localPositions);
        CalculateBounds(graph, positions, rotations, genome.torsoSpline, out Bounds bounds);

        float shortestLeg = float.PositiveInfinity;
        for (int i = 0; i < legs.Length; i++) shortestLeg = Mathf.Min(shortestLeg, legs[i].TotalLength);
        if (float.IsPositiveInfinity(shortestLeg)) shortestLeg = Mathf.Max(0.5f, genome.legLength);
        float clearance = CalculateClearance(graph, positions, legs);
        bool elephant = genome.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant;
        bool ungulate = genome.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate;
        bool anatomicalUngulate = ungulate
            && genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5;
        float mass = Mathf.Clamp(bounds.size.x * bounds.size.y * bounds.size.z * 0.16f,
            2f, elephant ? 180f : 90f);
        Vector3 centerOfMass = bounds.center
            + Vector3.down * bounds.extents.y * 0.08f
            + Vector3.forward * genome.bodyLength * (elephant ? 0.055f : 0.035f);
        float frequency = Mathf.Clamp(genome.gaitFrequency,
            elephant ? 0.65f : 0.8f, elephant ? 1.65f : (ungulate ? 2.6f : 2.4f));
        float stride = shortestLeg * (elephant ? 0.32f : (ungulate ? 0.48f : 0.44f));
        float walk = Mathf.Clamp(stride * frequency * 0.9f, 1.2f, 4.2f);
        float trot = walk * (elephant ? 1.28f : 1.55f);
        float run = walk * (elephant ? 1.62f : 2.25f);
        int hash = CalculateShapeHash(genome, localPositions);

        return new CreaturePhenotype(
            genome, genome.torsoSpline.Clone(), graph,
            localPositions, localRotations, positions, rotations, legs, bounds,
            centerOfMass, mass, clearance, walk, trot, run, stride,
            Mathf.Clamp(shortestLeg * (elephant ? 0.1f : (ungulate ? 0.13f : 0.16f)), 0.14f, 0.75f), frequency,
            elephant ? 0.84f : (ungulate ? 0.74f : 0.72f),
            elephant ? 0.68f : (ungulate ? 0.4f : 0.43f),
            elephant ? 0.94f : (ungulate ? (anatomicalUngulate ? 0.975f : 0.945f) : 0.9f),
            elephant ? 0.66f : (ungulate ? 0.58f : 0.6f),
            !elephant,
            hash);
    }

    public static void ConstrainV4Genome(CreatureGenome genome)
    {
        if (genome == null || genome.topology != CreatureTopology.Quadruped) return;
        genome.generatorVersion = CreaturePhenotype.CurrentVersion;
        genome.legPairCount = 2;
        genome.locomotionArchetype = genome.bodyStyle == 1 || genome.bodyStyle == 3
            ? CreatureLocomotionArchetype.GraviportalElephant
            : genome.bodyStyle == 2
                ? CreatureLocomotionArchetype.CursorialUngulate
                : CreatureLocomotionArchetype.CursorialCanid;

        // Keep V4 variation inside a recognisable, load-bearing quadruped envelope.
        genome.bodyLength = Mathf.Clamp(Mathf.Max(
            genome.bodyLength,
            genome.bodyHeight * 1.8f,
            genome.bodyWidth * 1.7f), 2.8f, 6.2f);
        genome.bodyHeight = Mathf.Clamp(
            genome.bodyHeight, genome.bodyLength * 0.24f, genome.bodyLength * 0.42f);
        genome.bodyWidth = Mathf.Clamp(
            genome.bodyWidth,
            genome.bodyHeight * 0.72f,
            Mathf.Min(genome.bodyHeight * 1.35f, genome.bodyLength * 0.58f));

        float minimumLeg = genome.bodyHeight * 0.78f + genome.bodyWidth * 0.18f;
        float maximumLeg = Mathf.Min(4.5f, genome.bodyLength * 0.66f + genome.bodyHeight * 0.3f);
        genome.frontLegLength = Mathf.Clamp(genome.frontLegLength, minimumLeg, maximumLeg);
        genome.rearLegLength = Mathf.Clamp(genome.rearLegLength, minimumLeg, maximumLeg);
        float averageLeg = (genome.frontLegLength + genome.rearLegLength) * 0.5f;
        float legDifference = averageLeg * 0.12f;
        genome.frontLegLength = Mathf.Clamp(genome.frontLegLength, averageLeg - legDifference, averageLeg + legDifference);
        genome.rearLegLength = Mathf.Clamp(genome.rearLegLength, averageLeg - legDifference, averageLeg + legDifference);
        genome.legLength = Mathf.Max(genome.frontLegLength, genome.rearLegLength);
        bool elephant = genome.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant;
        bool ungulate = genome.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate;
        genome.legThickness = Mathf.Clamp(genome.legThickness,
            genome.bodyWidth * (elephant ? 0.17f : (ungulate ? 0.075f : 0.13f)),
            genome.bodyWidth * (elephant ? 0.29f : (ungulate ? 0.125f : 0.22f)));
        genome.footScale = Mathf.Clamp(genome.footScale,
            genome.legThickness * (elephant ? 1.45f : (ungulate ? 0.9f : 1.3f)),
            genome.legThickness * (elephant ? 2.05f : (ungulate ? 1.18f : 1.85f)));
        genome.legSpread = Mathf.Clamp(genome.legSpread, 0.42f, 0.5f);
        genome.neckLength = Mathf.Clamp(genome.neckLength,
            genome.bodyHeight * (ungulate ? 0.52f : 0.16f),
            genome.bodyLength * (ungulate ? 0.52f : 0.3f));
        genome.headWidth = Mathf.Clamp(genome.headWidth,
            genome.bodyWidth * (ungulate ? 0.3f : 0.4f),
            genome.bodyWidth * (ungulate ? 0.5f : 0.7f));
        genome.headHeight = Mathf.Clamp(genome.headHeight, genome.bodyHeight * 0.34f, genome.bodyHeight * 0.62f);
        genome.headLength = Mathf.Clamp(genome.headLength,
            genome.headWidth * (ungulate ? 1.2f : 0.82f),
            genome.bodyLength * (ungulate ? 0.34f : 0.28f));
        genome.tailLength = Mathf.Clamp(genome.tailLength,
            genome.bodyLength * (ungulate ? 0.1f : 0.16f),
            genome.bodyLength * (ungulate ? 0.28f : 0.55f));
        genome.tailThickness = Mathf.Clamp(genome.tailThickness, genome.bodyWidth * 0.07f, genome.bodyWidth * 0.18f);
        genome.gaitHeight = Mathf.Clamp(genome.gaitHeight, genome.legLength * 0.08f, genome.legLength * 0.2f);
        genome.gaitFrequency = Mathf.Clamp(genome.gaitFrequency, 0.8f, 2.4f);
        if (genome.designLanguage != null)
            genome.designLanguage.ornamentDensity = Mathf.Min(genome.designLanguage.ornamentDensity, 0.28f);
    }

    static void FitSocketsToTorso(
        CreatureGenome genome,
        CreatureBodyGraph graph,
        Vector3[] localPositions,
        Quaternion[] localRotations)
    {
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            if (node.type != CreatureBodyNodeType.UpperLeg)
                continue;

            CreatureTorsoControlPoint shape = CreatureTorsoRigBuilder.SampleShape(
                genome.torsoSpline, node.longitudinalPosition);
            float side = node.side == CreatureBodySide.Left ? -1f : 1f;
            float halfWidth = shape.width * shape.taper * 0.5f;
            float halfHeight = shape.height * shape.taper * 0.5f;
            bool ungulate = genome.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate
                && genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5;
            CreatureV5TorsoSection v5Section = default;
            if (ungulate && genome.v5Parameters != null)
            {
                CreatureV5TorsoSection[] sections = CreatureV5PhenotypeBuilder.BuildTorsoSections(
                    genome.v5Parameters);
                v5Section = CreatureV5PhenotypeBuilder.SampleTorsoSection(
                    sections, node.longitudinalPosition);
                halfWidth = v5Section.width * 0.5f;
                halfHeight = (v5Section.topRadius + v5Section.bottomRadius) * 0.5f;
            }
            float angle = side < 0f
                ? (ungulate ? 230f : 220f)
                : (ungulate ? -50f : -40f);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 socket = new Vector3(
                Mathf.Cos(radians) * halfWidth * 0.96f,
                (ungulate ? v5Section.centerY : 0f)
                    + Mathf.Sin(radians) * halfHeight * 0.96f,
                0f);
            localPositions[i] = socket;
            localRotations[i] = Quaternion.Euler(0f, 0f, side * genome.designLanguage.limbAngularStyle * 10f);
        }
    }

    static void CalculateWorldPose(
        CreatureBodyGraph graph,
        Vector3[] localPositions,
        Quaternion[] localRotations,
        out Vector3[] positions,
        out Quaternion[] rotations)
    {
        positions = new Vector3[graph.nodes.Count];
        rotations = new Quaternion[graph.nodes.Count];
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            int parent = graph.nodes[i].parentIndex;
            if (parent < 0)
            {
                positions[i] = localPositions[i];
                rotations[i] = localRotations[i];
            }
            else
            {
                positions[i] = positions[parent] + rotations[parent] * localPositions[i];
                rotations[i] = rotations[parent] * localRotations[i];
            }
        }
    }

    static CreaturePhenotypeLeg[] BuildLegs(
        CreatureGenome genome,
        CreatureBodyGraph graph,
        Vector3[] localPositions)
    {
        var result = new List<CreaturePhenotypeLeg>(4);
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode upper = graph.nodes[i];
            if (upper.type != CreatureBodyNodeType.UpperLeg) continue;
            int lower = FindChild(graph, i, CreatureBodyNodeType.LowerLeg);
            int distal = lower >= 0 ? FindChild(graph, lower, CreatureBodyNodeType.DistalLeg) : -1;
            int foot = distal >= 0 ? FindChild(graph, distal, CreatureBodyNodeType.Foot) : -1;
            if (lower < 0 || distal < 0 || foot < 0) continue;
            bool elephant = genome.locomotionArchetype == CreatureLocomotionArchetype.GraviportalElephant;
            bool ungulate = genome.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate;
            bool anatomicalUngulate = ungulate
                && genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5;
            bool front = upper.longitudinalPosition >= 0.5f;
            result.Add(new CreaturePhenotypeLeg
            {
                upperNode = i,
                lowerNode = lower,
                distalNode = distal,
                footNode = foot,
                side = upper.side,
                longitudinalPosition = upper.longitudinalPosition,
                gaitGroup = upper.gaitGroup,
                upperLength = localPositions[lower].magnitude,
                lowerLength = localPositions[distal].magnitude,
                distalLength = localPositions[foot].magnitude,
                footSoleOffset = Mathf.Max(0.015f, graph.nodes[foot].size.y * 0.5f),
                bendHintLocal = Vector3.forward * (front ? -1f : 1f),
                minimumJointAngle = elephant ? 12f : (ungulate ? 18f : 28f),
                maximumJointAngle = elephant ? 174f : (ungulate ? (anatomicalUngulate ? 174f : 168f) : 154f),
                distalForwardFraction = elephant ? 0.08f : (ungulate
                    ? (anatomicalUngulate ? (front ? 0.03f : 0.09f) : (front ? 0.05f : 0.16f))
                    : (front ? 0.16f : 0.42f))
            });
        }
        result.Sort((a, b) =>
        {
            int longitudinal = b.longitudinalPosition.CompareTo(a.longitudinalPosition);
            return longitudinal != 0 ? longitudinal : a.side.CompareTo(b.side);
        });
        return result.ToArray();
    }

    static void CalculateBounds(
        CreatureBodyGraph graph,
        Vector3[] positions,
        Quaternion[] rotations,
        CreatureTorsoSpline spline,
        out Bounds bounds)
    {
        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < spline.points.Count; i++)
        {
            CreatureTorsoControlPoint point = spline.points[i];
            Vector3 extent = Vector3.one * (Mathf.Max(point.width, point.height) * 0.65f + point.blendRadius + 0.15f);
            minimum = Vector3.Min(minimum, point.localPosition - extent);
            maximum = Vector3.Max(maximum, point.localPosition + extent);
        }
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            if (node.type == CreatureBodyNodeType.Horn || node.type == CreatureBodyNodeType.BackPlate) continue;
            float radius = Mathf.Max(node.radius, Mathf.Max(node.size.x, Mathf.Max(node.size.y, node.size.z)) * 0.55f);
            Vector3 extent = Vector3.one * (radius + 0.18f);
            minimum = Vector3.Min(minimum, positions[i] - extent);
            maximum = Vector3.Max(maximum, positions[i] + extent);
        }
        bounds = new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
    }

    static float CalculateClearance(CreatureBodyGraph graph, Vector3[] positions, CreaturePhenotypeLeg[] legs)
    {
        float lowest = 0f;
        for (int i = 0; i < legs.Length; i++)
        {
            CreaturePhenotypeLeg leg = legs[i];
            lowest = Mathf.Min(lowest, positions[leg.upperNode].y - leg.TotalLength - leg.footSoleOffset);
        }
        return Mathf.Max(0.25f, -lowest + 0.05f);
    }

    static int CalculateShapeHash(CreatureGenome genome, Vector3[] positions)
    {
        unchecked
        {
            uint hash = 2166136261u;
            Hash(ref hash, genome.seed);
            Hash(ref hash, CreaturePhenotype.CurrentVersion);
            for (int i = 0; i < genome.torsoSpline.points.Count; i++)
            {
                CreatureTorsoControlPoint point = genome.torsoSpline.points[i];
                Hash(ref hash, point.localPosition);
                Hash(ref hash, point.width);
                Hash(ref hash, point.height);
                Hash(ref hash, point.rollDegrees);
                Hash(ref hash, point.blendRadius);
                Hash(ref hash, point.taper);
            }
            for (int i = 0; i < positions.Length; i++) Hash(ref hash, positions[i]);
            return (int)hash;
        }
    }

    static void Hash(ref uint hash, Vector3 value)
    {
        Hash(ref hash, value.x);
        Hash(ref hash, value.y);
        Hash(ref hash, value.z);
    }

    static void Hash(ref uint hash, float value) => Hash(ref hash, value.GetHashCode());
    static void Hash(ref uint hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 16777619u;
        }
    }

    static int FindChild(CreatureBodyGraph graph, int parent, CreatureBodyNodeType type)
    {
        for (int i = parent + 1; i < graph.nodes.Count; i++)
            if (graph.nodes[i].parentIndex == parent && graph.nodes[i].type == type) return i;
        return -1;
    }
}

public static class CreatureV4SkinWeightSolver
{
    public static BoneWeight[] Calculate(
        IReadOnlyList<Vector3> vertices,
        CreaturePhenotype phenotype,
        CreatureRig rig)
    {
        var result = new BoneWeight[vertices.Count];
        for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
        {
            Vector3 vertex = vertices[vertexIndex];
            if (TryCalculateFootWeight(vertex, phenotype, rig, out BoneWeight footWeight))
            {
                result[vertexIndex] = footWeight;
                continue;
            }
            if (TryCalculateLegWeight(vertex, phenotype, rig, out BoneWeight legWeight))
            {
                result[vertexIndex] = legWeight;
                continue;
            }
            CreaturePhenotypeLeg nearestLeg = FindNearestLeg(vertex, phenotype);
            int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
            float d0 = float.PositiveInfinity, d1 = float.PositiveInfinity;
            float d2 = float.PositiveInfinity, d3 = float.PositiveInfinity;
            for (int nodeIndex = 0; nodeIndex < phenotype.graph.nodes.Count; nodeIndex++)
            {
                CreatureBodyNode node = phenotype.graph.nodes[nodeIndex];
                if (node.type == CreatureBodyNodeType.Torso) continue;
                float distance = BoneDistance(vertex, nodeIndex, phenotype);
                if (IsLimb(node.type) && !BelongsToLeg(nodeIndex, nearestLeg))
                    distance += phenotype.fieldBounds.size.magnitude * 2f;

                if (distance < d0)
                {
                    d3 = d2; i3 = i2; d2 = d1; i2 = i1; d1 = d0; i1 = i0; d0 = distance; i0 = nodeIndex;
                }
                else if (distance < d1)
                {
                    d3 = d2; i3 = i2; d2 = d1; i2 = i1; d1 = distance; i1 = nodeIndex;
                }
                else if (distance < d2)
                {
                    d3 = d2; i3 = i2; d2 = distance; i2 = nodeIndex;
                }
                else if (distance < d3)
                {
                    d3 = distance; i3 = nodeIndex;
                }
            }

            float scale = Mathf.Max(0.08f, phenotype.fieldBounds.extents.magnitude * 0.08f);
            float w0 = Mathf.Exp(-d0 / scale);
            float w1 = Mathf.Exp(-d1 / scale);
            float w2 = Mathf.Exp(-d2 / scale);
            float w3 = Mathf.Exp(-d3 / scale);
            float sum = Mathf.Max(0.000001f, w0 + w1 + w2 + w3);
            result[vertexIndex] = new BoneWeight
            {
                boneIndex0 = rig.nodeBoneIndices[i0], weight0 = w0 / sum,
                boneIndex1 = rig.nodeBoneIndices[i1], weight1 = w1 / sum,
                boneIndex2 = rig.nodeBoneIndices[i2], weight2 = w2 / sum,
                boneIndex3 = rig.nodeBoneIndices[i3], weight3 = w3 / sum
            };
        }
        return result;
    }

    static bool TryCalculateFootWeight(
        Vector3 vertex,
        CreaturePhenotype phenotype,
        CreatureRig rig,
        out BoneWeight weight)
    {
        CreaturePhenotypeLeg nearest = null;
        float nearestScore = float.PositiveInfinity;
        float nearestFootScore = float.PositiveInfinity;
        float nearestConnectorScore = float.PositiveInfinity;
        float nearestConnectorT = 0f;
        for (int i = 0; i < phenotype.legs.Length; i++)
        {
            CreaturePhenotypeLeg leg = phenotype.legs[i];
            CreatureBodyNode foot = phenotype.graph.nodes[leg.footNode];
            Quaternion rotation = phenotype.nodeRotations[leg.footNode];
            bool ungulate = phenotype.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate;
            Vector3 center = phenotype.nodePositions[leg.footNode]
                + rotation * Vector3.forward * (foot.size.z * (ungulate ? 0.08f : 0.18f));
            Vector3 local = Quaternion.Inverse(rotation) * (vertex - center);
            Vector3 radii = foot.size * 0.5f;
            float score = new Vector3(
                local.x / Mathf.Max(0.035f, radii.x),
                local.y / Mathf.Max(0.035f, radii.y),
                local.z / Mathf.Max(0.035f, radii.z)).magnitude;
            Vector3 connectorStart = phenotype.nodePositions[leg.distalNode];
            Vector3 connectorEnd = phenotype.nodePositions[leg.footNode];
            Vector3 connector = connectorEnd - connectorStart;
            float connectorT = Mathf.Clamp01(Vector3.Dot(vertex - connectorStart, connector)
                / Mathf.Max(0.0001f, connector.sqrMagnitude));
            float connectorRadius = Mathf.Max(ungulate ? 0.055f : 0.14f,
                phenotype.graph.nodes[leg.distalNode].radius * (ungulate ? 1.05f : 1.18f));
            float connectorScore = Vector3.Distance(
                vertex, connectorStart + connector * connectorT) / connectorRadius;
            float selectionScore = Mathf.Min(score, connectorScore);
            if (selectionScore >= nearestScore) continue;
            nearestScore = selectionScore;
            nearestFootScore = score;
            nearestConnectorScore = connectorScore;
            nearestConnectorT = connectorT;
            nearest = leg;
        }

        if (nearest == null || (nearestFootScore > 1.65f && nearestConnectorScore > 1.35f))
        {
            weight = default;
            return false;
        }

        float ellipsoidInfluence = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(1.65f, 0.95f, nearestFootScore));
        float radialInfluence = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(1.35f, 0.82f, nearestConnectorScore));
        float connectorInfluence = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0.12f, 0.88f, nearestConnectorT)) * radialInfluence;
        float footInfluence = Mathf.Max(ellipsoidInfluence, connectorInfluence);
        weight = new BoneWeight
        {
            boneIndex0 = rig.nodeBoneIndices[nearest.footNode],
            weight0 = footInfluence,
            boneIndex1 = rig.nodeBoneIndices[nearest.distalNode],
            weight1 = 1f - footInfluence
        };
        return true;
    }

    static bool TryCalculateLegWeight(
        Vector3 vertex,
        CreaturePhenotype phenotype,
        CreatureRig rig,
        out BoneWeight weight)
    {
        CreaturePhenotypeLeg leg = FindNearestLeg(vertex, phenotype);
        if (leg == null)
        {
            weight = default;
            return false;
        }

        float upperDistance = DistanceToSegment(vertex,
            phenotype.nodePositions[leg.upperNode], phenotype.nodePositions[leg.lowerNode]);
        float lowerDistance = DistanceToSegment(vertex,
            phenotype.nodePositions[leg.lowerNode], phenotype.nodePositions[leg.distalNode]);
        float distalDistance = DistanceToSegment(vertex,
            phenotype.nodePositions[leg.distalNode], phenotype.nodePositions[leg.footNode]);
        float radius = Mathf.Max(
            phenotype.graph.nodes[leg.upperNode].radius,
            phenotype.graph.nodes[leg.lowerNode].radius);
        if (Mathf.Min(upperDistance, Mathf.Min(lowerDistance, distalDistance)) > radius * 1.55f)
        {
            weight = default;
            return false;
        }

        // Keep each segment firm and blend only around the knee and hock/wrist joints.
        float scale = Mathf.Max(0.018f, radius * 0.22f);
        float upperInfluence = Mathf.Exp(-upperDistance / scale);
        float lowerInfluence = Mathf.Exp(-lowerDistance / scale);
        float distalInfluence = Mathf.Exp(-distalDistance / scale);
        float sum = Mathf.Max(0.000001f, upperInfluence + lowerInfluence + distalInfluence);
        if (upperInfluence >= lowerInfluence && upperInfluence >= distalInfluence)
        {
            weight = new BoneWeight
            {
                boneIndex0 = rig.nodeBoneIndices[leg.upperNode],
                weight0 = upperInfluence / sum,
                boneIndex1 = rig.nodeBoneIndices[leg.lowerNode],
                weight1 = lowerInfluence / sum,
                boneIndex2 = rig.nodeBoneIndices[leg.distalNode],
                weight2 = distalInfluence / sum
            };
        }
        else if (lowerInfluence >= distalInfluence)
        {
            weight = new BoneWeight
            {
                boneIndex0 = rig.nodeBoneIndices[leg.lowerNode],
                weight0 = lowerInfluence / sum,
                boneIndex1 = rig.nodeBoneIndices[leg.upperNode],
                weight1 = upperInfluence / sum,
                boneIndex2 = rig.nodeBoneIndices[leg.distalNode],
                weight2 = distalInfluence / sum
            };
        }
        else
        {
            weight = new BoneWeight
            {
                boneIndex0 = rig.nodeBoneIndices[leg.distalNode],
                weight0 = distalInfluence / sum,
                boneIndex1 = rig.nodeBoneIndices[leg.lowerNode],
                weight1 = lowerInfluence / sum,
                boneIndex2 = rig.nodeBoneIndices[leg.upperNode],
                weight2 = upperInfluence / sum
            };
        }
        return true;
    }

    static float BoneDistance(Vector3 vertex, int nodeIndex, CreaturePhenotype phenotype)
    {
        CreaturePhenotypeLeg leg = FindLegContainingNode(nodeIndex, phenotype);
        if (leg != null)
        {
            if (nodeIndex == leg.upperNode)
                return DistanceToSegment(vertex,
                    phenotype.nodePositions[leg.upperNode], phenotype.nodePositions[leg.lowerNode]);
            if (nodeIndex == leg.lowerNode)
                return DistanceToSegment(vertex,
                    phenotype.nodePositions[leg.lowerNode], phenotype.nodePositions[leg.distalNode]);
            if (nodeIndex == leg.distalNode)
                return DistanceToSegment(vertex,
                    phenotype.nodePositions[leg.distalNode], phenotype.nodePositions[leg.footNode]);
            return Vector3.Distance(vertex, phenotype.nodePositions[leg.footNode]);
        }

        CreatureBodyNode node = phenotype.graph.nodes[nodeIndex];
        int parent = node.parentIndex;
        Vector3 a = parent >= 0 ? phenotype.nodePositions[parent] : phenotype.nodePositions[nodeIndex];
        return DistanceToSegment(vertex, a, phenotype.nodePositions[nodeIndex]);
    }

    static CreaturePhenotypeLeg FindLegContainingNode(int nodeIndex, CreaturePhenotype phenotype)
    {
        for (int i = 0; i < phenotype.legs.Length; i++)
        {
            CreaturePhenotypeLeg leg = phenotype.legs[i];
            if (nodeIndex == leg.upperNode || nodeIndex == leg.lowerNode
                || nodeIndex == leg.distalNode || nodeIndex == leg.footNode)
                return leg;
        }
        return null;
    }

    static CreaturePhenotypeLeg FindNearestLeg(Vector3 vertex, CreaturePhenotype phenotype)
    {
        CreaturePhenotypeLeg nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < phenotype.legs.Length; i++)
        {
            CreaturePhenotypeLeg leg = phenotype.legs[i];
            Vector3 hip = phenotype.nodePositions[leg.upperNode];
            Vector3 knee = phenotype.nodePositions[leg.lowerNode];
            Vector3 hock = phenotype.nodePositions[leg.distalNode];
            Vector3 foot = phenotype.nodePositions[leg.footNode];
            float distance = Mathf.Min(
                Mathf.Min(DistanceToSegment(vertex, hip, knee), DistanceToSegment(vertex, knee, hock)),
                DistanceToSegment(vertex, hock, foot));
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = leg;
        }
        return nearest;
    }

    static bool BelongsToLeg(int nodeIndex, CreaturePhenotypeLeg leg)
    {
        return leg != null
            && (nodeIndex == leg.upperNode || nodeIndex == leg.lowerNode
                || nodeIndex == leg.distalNode || nodeIndex == leg.footNode);
    }

    static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 segment = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, segment) / Mathf.Max(0.0001f, segment.sqrMagnitude));
        return Vector3.Distance(point, a + segment * t);
    }

    static bool IsLimb(CreatureBodyNodeType type)
    {
        return type == CreatureBodyNodeType.UpperLeg
            || type == CreatureBodyNodeType.LowerLeg
            || type == CreatureBodyNodeType.DistalLeg
            || type == CreatureBodyNodeType.Foot
            || type == CreatureBodyNodeType.UpperArm
            || type == CreatureBodyNodeType.LowerArm
            || type == CreatureBodyNodeType.Hand;
    }
}
