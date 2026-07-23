using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CreatureV5EditableParameters
{
    public int version = CreatureGenerationVersions.AnatomicalV5;
    public int schemaVersion = CreatureV5MeshSchema.Current;
    public float bodyLength;
    public float chestWidth;
    public float chestDepth;
    public float waistTuck;
    public float pelvisWidth;
    public float pelvisDepth;
    public float legLength;
    public float legThickness;
    public float distalLegFraction;
    public float neckLength;
    public float neckAngle;
    public float headLength;
    public float headWidth;
    public float headHeight;
    public float hoofLength;
    public float hoofWidth;
    public float bellyRise;
    public float neckRootScale;
    public float muzzleLengthRatio;
    public float earLengthRatio;
    public float earOutwardAngle;

    public CreatureV5EditableParameters Clone()
    {
        return (CreatureV5EditableParameters)MemberwiseClone();
    }

    public static CreatureV5EditableParameters CreateFromGenome(CreatureGenome genome)
    {
        float averageLeg = Mathf.Max(0.8f, (genome.frontLegLength + genome.rearLegLength) * 0.5f);
        return new CreatureV5EditableParameters
        {
            bodyLength = genome.bodyLength,
            chestWidth = genome.bodyWidth * 1.02f,
            chestDepth = genome.bodyHeight * 1.08f,
            waistTuck = 0.72f,
            pelvisWidth = genome.bodyWidth * 0.9f,
            pelvisDepth = genome.bodyHeight * 0.9f,
            legLength = averageLeg,
            legThickness = genome.legThickness,
            distalLegFraction = 0.35f,
            neckLength = genome.neckLength,
            neckAngle = 32f,
            headLength = genome.headLength,
            headWidth = genome.headWidth,
            headHeight = genome.headHeight,
            hoofLength = Mathf.Max(genome.footScale * 1.75f, genome.legThickness * 1.8f),
            hoofWidth = Mathf.Max(genome.footScale * 1.08f, genome.legThickness * 1.05f),
            bellyRise = 0.19f,
            neckRootScale = 1f,
            muzzleLengthRatio = 0.58f,
            earLengthRatio = 0.92f,
            earOutwardAngle = 28f
        };
    }

    public void Clamp()
    {
        if (schemaVersion < CreatureV5MeshSchema.Current)
        {
            if (bellyRise <= 0f) bellyRise = 0.19f;
            if (neckRootScale <= 0f) neckRootScale = 1f;
            if (muzzleLengthRatio <= 0f) muzzleLengthRatio = 0.58f;
            if (earLengthRatio <= 0f) earLengthRatio = 0.92f;
            if (earOutwardAngle <= 0f) earOutwardAngle = 28f;
            schemaVersion = CreatureV5MeshSchema.Current;
        }
        version = CreatureGenerationVersions.AnatomicalV5;
        bodyLength = Mathf.Clamp(bodyLength, 2.8f, 6.2f);
        chestWidth = Mathf.Clamp(chestWidth, bodyLength * 0.18f, bodyLength * 0.42f);
        chestDepth = Mathf.Clamp(chestDepth, bodyLength * 0.2f, bodyLength * 0.42f);
        waistTuck = Mathf.Clamp(waistTuck, 0.52f, 0.88f);
        pelvisWidth = Mathf.Clamp(pelvisWidth, chestWidth * 0.68f, chestWidth * 1.02f);
        pelvisDepth = Mathf.Clamp(pelvisDepth, chestDepth * 0.68f, chestDepth * 1.02f);
        legLength = Mathf.Clamp(legLength, chestDepth * 1.35f, bodyLength * 0.95f);
        legThickness = Mathf.Clamp(legThickness, chestWidth * 0.07f, chestWidth * 0.135f);
        distalLegFraction = Mathf.Clamp(distalLegFraction, 0.3f, 0.42f);
        neckLength = Mathf.Clamp(neckLength, chestDepth * 0.65f, bodyLength * 0.42f);
        neckAngle = Mathf.Clamp(neckAngle, 18f, 48f);
        headLength = Mathf.Clamp(headLength, chestWidth * 0.42f, bodyLength * 0.35f);
        headWidth = Mathf.Clamp(headWidth, chestWidth * 0.26f, chestWidth * 0.52f);
        headHeight = Mathf.Clamp(headHeight, headWidth * 0.72f, headWidth * 1.3f);
        hoofLength = Mathf.Clamp(hoofLength, legThickness * 1.4f, legThickness * 2.6f);
        hoofWidth = Mathf.Clamp(hoofWidth, legThickness * 0.85f, hoofLength * 0.72f);
        bellyRise = Mathf.Clamp(bellyRise, 0.1f, 0.28f);
        neckRootScale = Mathf.Clamp(neckRootScale, 0.82f, 1.2f);
        muzzleLengthRatio = Mathf.Clamp(muzzleLengthRatio, 0.45f, 0.7f);
        earLengthRatio = Mathf.Clamp(earLengthRatio, 0.7f, 1.1f);
        earOutwardAngle = Mathf.Clamp(earOutwardAngle, 15f, 42f);
    }
}

public static class CreatureV5MeshSchema
{
    public const int Current = 2;
}

public enum CreatureV5SurfaceRegion : byte
{
    Torso,
    NeckRoot,
    Neck,
    Head,
    Jaw,
    UpperLeg,
    LowerLeg,
    DistalLeg,
    Joint,
    HoofCrown,
    HoofShell,
    Tail,
    SemanticEdge
}

public struct CreatureV5TorsoSection
{
    public float t;
    public float centerY;
    public float width;
    public float topRadius;
    public float bottomRadius;
    public float exponent;

    public float Height => topRadius + bottomRadius;
}

public struct CreatureV5SocketDescriptor
{
    public int node;
    public int legIndex;
    public float longitudinalPosition;
    public float circumferenceAngleDegrees;
    public int axialSpan;
    public int circumferentialSpan;
    public Vector3 center;
    public Vector3 outward;
}

public struct CreatureV5RestLegPose
{
    public int legIndex;
    public Vector3 hip;
    public Vector3 knee;
    public Vector3 hock;
    public Vector3 hoof;
    public Vector3 bendDirection;
}

public struct CreatureV5HeadProfile
{
    public float neckRootWidth;
    public float upperNeckWidth;
    public float skullWidth;
    public float skullHeight;
    public float muzzleLengthRatio;
}

public struct CreatureV5EarProfile
{
    public float length;
    public float width;
    public float outwardAngle;
    public float thickness;
}

public sealed class CreatureAnatomyProfile
{
    public readonly CreatureLocomotionArchetype family;
    public readonly int torsoRingCount;
    public readonly int torsoRingSegments;
    public readonly int limbRingSegments;
    public readonly int headRingSegments;
    public readonly int tailRingSegments;
    public readonly float frontLoadFraction;

    CreatureAnatomyProfile(
        CreatureLocomotionArchetype sourceFamily,
        int torsoRings,
        int torsoSegments,
        int limbSegments,
        int headSegments,
        int tailSegments,
        float frontLoad)
    {
        family = sourceFamily;
        torsoRingCount = torsoRings;
        torsoRingSegments = torsoSegments;
        limbRingSegments = limbSegments;
        headRingSegments = headSegments;
        tailRingSegments = tailSegments;
        frontLoadFraction = frontLoad;
    }

    public static readonly CreatureAnatomyProfile Ungulate = new CreatureAnatomyProfile(
        CreatureLocomotionArchetype.CursorialUngulate, 24, 16, 12, 12, 8, 0.58f);
}

public sealed class CreatureV5Phenotype
{
    public readonly CreaturePhenotype motion;
    public readonly CreatureAnatomyProfile anatomy;
    public readonly CreatureV5EditableParameters parameters;
    public readonly int shapeHash;
    public readonly int neckNode;
    public readonly int headNode;
    public readonly int muzzleNode;
    public readonly int[] spineNodes;
    public readonly int[] tailNodes;
    public readonly CreatureV5TorsoSection[] torsoSections;
    public readonly CreatureV5SocketDescriptor neckSocket;
    public readonly CreatureV5SocketDescriptor tailSocket;
    public readonly CreatureV5SocketDescriptor[] legSockets;
    public readonly CreatureV5RestLegPose[] restLegPoses;
    public readonly CreatureV5HeadProfile headProfile;
    public readonly CreatureV5EarProfile earProfile;

    public CreatureV5Phenotype(
        CreaturePhenotype sourceMotion,
        CreatureAnatomyProfile sourceAnatomy,
        CreatureV5EditableParameters sourceParameters,
        int calculatedHash,
        int sourceNeck,
        int sourceHead,
        int sourceMuzzle,
        int[] sourceSpine,
        int[] sourceTail,
        CreatureV5TorsoSection[] sourceTorsoSections,
        CreatureV5SocketDescriptor sourceNeckSocket,
        CreatureV5SocketDescriptor sourceTailSocket,
        CreatureV5SocketDescriptor[] sourceLegSockets,
        CreatureV5RestLegPose[] sourceRestLegPoses,
        CreatureV5HeadProfile sourceHeadProfile,
        CreatureV5EarProfile sourceEarProfile)
    {
        motion = sourceMotion;
        anatomy = sourceAnatomy;
        parameters = sourceParameters;
        shapeHash = calculatedHash;
        neckNode = sourceNeck;
        headNode = sourceHead;
        muzzleNode = sourceMuzzle;
        spineNodes = sourceSpine;
        tailNodes = sourceTail;
        torsoSections = sourceTorsoSections;
        neckSocket = sourceNeckSocket;
        tailSocket = sourceTailSocket;
        legSockets = sourceLegSockets;
        restLegPoses = sourceRestLegPoses;
        headProfile = sourceHeadProfile;
        earProfile = sourceEarProfile;
    }
}

public static class CreatureV5PhenotypeBuilder
{
    public static bool Supports(CreatureGenome genome)
    {
        return genome != null
            && genome.generatorVersion >= CreatureGenerationVersions.AnatomicalV5
            && genome.topology == CreatureTopology.Quadruped
            && genome.locomotionArchetype == CreatureLocomotionArchetype.CursorialUngulate;
    }

    public static CreatureV5Phenotype Build(CreatureGenome genome)
    {
        if (!Supports(genome))
            throw new ArgumentException("V5 currently supports cursorial ungulates only.", nameof(genome));

        if (genome.v5Parameters == null)
            genome.v5Parameters = CreatureV5EditableParameters.CreateFromGenome(genome);
        genome.v5Parameters.Clamp();
        ApplyParameters(genome, genome.v5Parameters);

        CreaturePhenotype motion = CreaturePhenotypeBuilder.Build(genome);
        motion.ApplyToGraph(genome.bodyGraph);
        CollectSemanticNodes(genome.bodyGraph,
            out int neck, out int head, out int muzzle, out int[] spine, out int[] tail);
        CreatureV5TorsoSection[] torsoSections = BuildTorsoSections(genome.v5Parameters);
        CreatureV5SocketDescriptor[] legSockets = BuildLegSockets(motion, torsoSections);
        CreatureV5RestLegPose[] restLegPoses = BuildRestLegPoses(motion);
        CreatureV5SocketDescriptor neckSocket = BuildAxialSocket(
            neck, 0.89f, 90f, 3, 3, torsoSections);
        CreatureV5SocketDescriptor tailSocket = BuildAxialSocket(
            tail.Length > 0 ? tail[0] : -1, 0.08f, 90f, 2, 2, torsoSections);
        CreatureV5HeadProfile headProfile = BuildHeadProfile(genome.v5Parameters);
        CreatureV5EarProfile earProfile = BuildEarProfile(genome.v5Parameters);
        int hash = CalculateHash(motion.shapeHash, genome.v5Parameters);
        return new CreatureV5Phenotype(
            motion, CreatureAnatomyProfile.Ungulate, genome.v5Parameters.Clone(), hash,
            neck, head, muzzle, spine, tail, torsoSections, neckSocket, tailSocket,
            legSockets, restLegPoses, headProfile, earProfile);
    }

    static void ApplyParameters(CreatureGenome genome, CreatureV5EditableParameters value)
    {
        genome.generatorVersion = CreatureGenerationVersions.AnatomicalV5;
        genome.bodyLength = value.bodyLength;
        genome.bodyWidth = value.chestWidth;
        genome.bodyHeight = value.chestDepth;
        genome.frontLegLength = value.legLength * 1.015f;
        genome.rearLegLength = value.legLength * 0.985f;
        genome.legLength = Mathf.Max(genome.frontLegLength, genome.rearLegLength);
        genome.legThickness = value.legThickness;
        genome.neckLength = value.neckLength;
        genome.headLength = value.headLength;
        genome.headWidth = value.headWidth;
        genome.headHeight = value.headHeight;
        genome.footScale = Mathf.Max(value.hoofWidth, value.hoofLength * 0.64f);

        ApplyTorsoProfile(genome.torsoSpline, value);
        ApplyGraphProfile(genome.bodyGraph, genome.torsoSpline, value);
    }

    static void ApplyTorsoProfile(CreatureTorsoSpline spline, CreatureV5EditableParameters value)
    {
        if (spline == null || spline.points == null || spline.points.Count < 3) return;
        CreatureV5TorsoSection[] sections = BuildTorsoSections(value);
        for (int i = 0; i < spline.points.Count; i++)
        {
            float t = i / (float)(spline.points.Count - 1);
            CreatureV5TorsoSection section = SampleTorsoSection(sections, t);
            CreatureTorsoControlPoint point = spline.points[i];
            point.localPosition = new Vector3(
                0f, section.centerY,
                Mathf.Lerp(-value.bodyLength * 0.5f, value.bodyLength * 0.5f, t));
            point.width = section.width;
            point.height = section.Height;
            point.taper = 1f;
            point.rollDegrees = 0f;
            point.blendRadius = Mathf.Min(point.width, point.height) * 0.12f;
        }
    }

    static void ApplyGraphProfile(
        CreatureBodyGraph graph,
        CreatureTorsoSpline spline,
        CreatureV5EditableParameters value)
    {
        if (graph == null || graph.nodes == null) return;
        var spine = new List<int>();
        for (int i = 0; i < graph.nodes.Count; i++)
            if (graph.nodes[i].type == CreatureBodyNodeType.Spine) spine.Add(i);

        Vector3 previous = Vector3.zero;
        for (int i = 0; i < spine.Count; i++)
        {
            float t = spine.Count == 1 ? 0f : i / (float)(spine.Count - 1);
            Vector3 position = CreatureTorsoRigBuilder.SamplePosition(spline, t);
            CreatureTorsoControlPoint shape = CreatureTorsoRigBuilder.SampleShape(spline, t);
            CreatureBodyNode node = graph.nodes[spine[i]];
            node.localPosition = i == 0 ? position : position - previous;
            node.size = new Vector3(shape.width, shape.height,
                i == 0 ? 0.1f : Vector3.Distance(previous, position));
            node.radius = Mathf.Min(shape.width, shape.height) * 0.5f;
            previous = position;
        }

        float upperFraction = 0.34f;
        float lowerFraction = Mathf.Max(0.2f, 1f - upperFraction - value.distalLegFraction);
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode upper = graph.nodes[i];
            if (upper.type != CreatureBodyNodeType.UpperLeg) continue;
            int lowerIndex = FindChild(graph, i, CreatureBodyNodeType.LowerLeg);
            int distalIndex = lowerIndex >= 0
                ? FindChild(graph, lowerIndex, CreatureBodyNodeType.DistalLeg) : -1;
            int footIndex = distalIndex >= 0
                ? FindChild(graph, distalIndex, CreatureBodyNodeType.Foot) : -1;
            if (lowerIndex < 0 || distalIndex < 0 || footIndex < 0) continue;

            float total = upper.longitudinalPosition >= 0.5f
                ? value.legLength * 1.015f : value.legLength * 0.985f;
            bool front = upper.longitudinalPosition >= 0.5f;
            upper.longitudinalPosition = front ? 0.83f : 0.2f;
            float upperLength = total * upperFraction;
            float lowerLength = total * lowerFraction;
            float distalLength = total - upperLength - lowerLength;
            upper.size = new Vector3(value.legThickness * 1.12f, upperLength, value.legThickness * 1.04f);
            upper.radius = value.legThickness * 0.56f;

            CreatureBodyNode lower = graph.nodes[lowerIndex];
            Vector3 upperDirection = front
                ? new Vector3(0f, -1f, -0.055f).normalized
                : new Vector3(0f, -1f, 0.16f).normalized;
            lower.localPosition = upperDirection * upperLength;
            lower.size = new Vector3(value.legThickness * 0.84f, lowerLength, value.legThickness * 0.78f);
            lower.radius = value.legThickness * 0.42f;

            CreatureBodyNode distal = graph.nodes[distalIndex];
            Vector3 lowerDirection = front
                ? new Vector3(0f, -1f, 0.035f).normalized
                : new Vector3(0f, -1f, -0.2f).normalized;
            distal.localPosition = lowerDirection * lowerLength;
            distal.size = new Vector3(value.legThickness * 0.6f, distalLength, value.legThickness * 0.56f);
            distal.radius = value.legThickness * 0.3f;

            CreatureBodyNode foot = graph.nodes[footIndex];
            Vector3 distalDirection = front
                ? new Vector3(0f, -1f, 0.025f).normalized
                : new Vector3(0f, -1f, 0.08f).normalized;
            foot.localPosition = distalDirection * distalLength;
            float frontWidth = upper.longitudinalPosition >= 0.5f ? 1.04f : 0.98f;
            foot.size = new Vector3(value.hoofWidth * frontWidth,
                value.hoofWidth * 0.72f, value.hoofLength);
            foot.radius = value.hoofWidth * 0.42f;
        }

        int tailOrder = 0;
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode tail = graph.nodes[i];
            if (tail.type != CreatureBodyNodeType.Tail) continue;
            float segmentLength = value.bodyLength * (tailOrder == 0 ? 0.055f : 0.04f);
            tail.localPosition = new Vector3(0f,
                value.pelvisDepth * (tailOrder == 0 ? 0.14f : -0.055f), -segmentLength);
            tail.localEulerAngles = new Vector3(tailOrder == 0 ? 18f : 8f, 0f, 0f);
            tail.radius = value.pelvisWidth * Mathf.Lerp(0.12f, 0.045f, Mathf.Clamp01(tailOrder / 2f));
            tail.size = Vector3.one * tail.radius * 2f;
            tailOrder++;
        }

        for (int i = 0; i < graph.nodes.Count; i++)
        {
            CreatureBodyNode node = graph.nodes[i];
            if (node.type == CreatureBodyNodeType.Neck)
            {
                node.localPosition = new Vector3(0f, value.chestDepth * 0.12f, value.neckLength * 0.18f);
                node.localEulerAngles = new Vector3(-value.neckAngle, 0f, 0f);
                node.size = new Vector3(value.headWidth * 0.72f, value.headHeight * 0.72f, value.neckLength);
                node.radius = value.headWidth * 0.34f;
            }
            else if (node.type == CreatureBodyNodeType.Head)
            {
                node.localPosition = new Vector3(0f, value.neckLength * 0.22f, value.neckLength * 0.58f);
                node.localEulerAngles = new Vector3(value.neckAngle * 0.88f, 0f, 0f);
                node.size = new Vector3(value.headWidth, value.headHeight, value.headLength);
                node.radius = value.headWidth * 0.45f;
            }
            else if (node.type == CreatureBodyNodeType.Muzzle)
            {
                node.localPosition = new Vector3(0f, -value.headHeight * 0.08f, value.headLength * 0.42f);
                node.size = new Vector3(value.headWidth * 0.62f, value.headHeight * 0.46f, value.headLength * 0.72f);
                node.radius = value.headWidth * 0.27f;
            }
        }
        graph.generatorVersion = CreatureGenerationVersions.AnatomicalV5;
    }

    public static CreatureV5TorsoSection[] BuildTorsoSections(CreatureV5EditableParameters value)
    {
        float chest = value.chestDepth;
        float waistBottom = Mathf.Max(chest * 0.27f, chest * (0.54f - value.bellyRise));
        return new[]
        {
            Section(0f, value.pelvisWidth * 0.42f, value.pelvisDepth * 0.24f,
                value.pelvisDepth * 0.2f, value.pelvisDepth * 0.12f, 1.82f),
            Section(0.08f, value.pelvisWidth * 0.82f, value.pelvisDepth * 0.41f,
                value.pelvisDepth * 0.35f, value.pelvisDepth * 0.06f, 1.84f),
            Section(0.22f, value.pelvisWidth, value.pelvisDepth * 0.47f,
                value.pelvisDepth * 0.48f, value.pelvisDepth * 0.012f, 1.86f),
            Section(0.36f, Mathf.Lerp(value.pelvisWidth, value.chestWidth * value.waistTuck, 0.5f),
                chest * 0.41f, chest * 0.37f, chest * 0.008f, 1.9f),
            Section(0.5f, value.chestWidth * value.waistTuck,
                chest * 0.37f, waistBottom, chest * 0.012f, 2f),
            Section(0.64f, value.chestWidth * 0.93f,
                chest * 0.45f, chest * 0.48f, chest * 0.008f, 1.88f),
            Section(0.79f, value.chestWidth,
                chest * 0.47f, chest * 0.55f, chest * 0.012f, 1.82f),
            Section(0.9f, value.chestWidth * 0.96f,
                chest * 0.5f, chest * 0.52f, chest * 0.02f, 1.86f),
            Section(1f, value.chestWidth * 0.58f,
                chest * 0.32f, chest * 0.42f, chest * 0.03f, 1.86f)
        };
    }

    public static CreatureV5TorsoSection SampleTorsoSection(
        CreatureV5TorsoSection[] sections,
        float t)
    {
        t = Mathf.Clamp01(t);
        for (int i = 0; i < sections.Length - 1; i++)
        {
            if (t > sections[i + 1].t) continue;
            float blend = Mathf.InverseLerp(sections[i].t, sections[i + 1].t, t);
            blend = blend * blend * (3f - 2f * blend);
            return new CreatureV5TorsoSection
            {
                t = t,
                centerY = Mathf.Lerp(sections[i].centerY, sections[i + 1].centerY, blend),
                width = Mathf.Lerp(sections[i].width, sections[i + 1].width, blend),
                topRadius = Mathf.Lerp(sections[i].topRadius, sections[i + 1].topRadius, blend),
                bottomRadius = Mathf.Lerp(sections[i].bottomRadius, sections[i + 1].bottomRadius, blend),
                exponent = Mathf.Lerp(sections[i].exponent, sections[i + 1].exponent, blend)
            };
        }
        return sections[sections.Length - 1];
    }

    static CreatureV5TorsoSection Section(
        float t,
        float width,
        float top,
        float bottom,
        float centerY,
        float exponent)
    {
        return new CreatureV5TorsoSection
        {
            t = t,
            width = width,
            topRadius = top,
            bottomRadius = bottom,
            centerY = centerY,
            exponent = exponent
        };
    }

    static CreatureV5SocketDescriptor[] BuildLegSockets(
        CreaturePhenotype motion,
        CreatureV5TorsoSection[] sections)
    {
        var sockets = new CreatureV5SocketDescriptor[motion.legs.Length];
        for (int i = 0; i < sockets.Length; i++)
        {
            CreaturePhenotypeLeg leg = motion.legs[i];
            float angle = leg.IsLeft ? 230f : 310f;
            float radians = angle * Mathf.Deg2Rad;
            sockets[i] = new CreatureV5SocketDescriptor
            {
                node = leg.upperNode,
                legIndex = i,
                longitudinalPosition = leg.IsFront ? 0.83f : 0.2f,
                circumferenceAngleDegrees = angle,
                axialSpan = 3,
                circumferentialSpan = 3,
                center = motion.nodePositions[leg.upperNode],
                outward = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f).normalized
            };
        }
        return sockets;
    }

    static CreatureV5RestLegPose[] BuildRestLegPoses(CreaturePhenotype motion)
    {
        var poses = new CreatureV5RestLegPose[motion.legs.Length];
        for (int i = 0; i < poses.Length; i++)
        {
            CreaturePhenotypeLeg leg = motion.legs[i];
            poses[i] = new CreatureV5RestLegPose
            {
                legIndex = i,
                hip = motion.nodePositions[leg.upperNode],
                knee = motion.nodePositions[leg.lowerNode],
                hock = motion.nodePositions[leg.distalNode],
                hoof = motion.nodePositions[leg.footNode],
                bendDirection = leg.IsFront ? Vector3.back : Vector3.forward
            };
        }
        return poses;
    }

    static CreatureV5SocketDescriptor BuildAxialSocket(
        int node,
        float t,
        float angle,
        int axialSpan,
        int circumferentialSpan,
        CreatureV5TorsoSection[] sections)
    {
        CreatureV5TorsoSection section = SampleTorsoSection(sections, t);
        float radians = angle * Mathf.Deg2Rad;
        float verticalRadius = Mathf.Sin(radians) >= 0f ? section.topRadius : section.bottomRadius;
        Vector3 outward = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f).normalized;
        return new CreatureV5SocketDescriptor
        {
            node = node,
            legIndex = -1,
            longitudinalPosition = t,
            circumferenceAngleDegrees = angle,
            axialSpan = axialSpan,
            circumferentialSpan = circumferentialSpan,
            center = new Vector3(
                Mathf.Cos(radians) * section.width * 0.5f,
                section.centerY + Mathf.Sin(radians) * verticalRadius,
                0f),
            outward = outward
        };
    }

    static CreatureV5HeadProfile BuildHeadProfile(CreatureV5EditableParameters value)
    {
        float upperNeckWidth = Mathf.Min(value.headWidth * 0.82f, value.chestWidth * 0.4f);
        return new CreatureV5HeadProfile
        {
            neckRootWidth = value.chestWidth * 0.52f * value.neckRootScale,
            upperNeckWidth = upperNeckWidth,
            skullWidth = Mathf.Max(value.headWidth, upperNeckWidth * 1.15f),
            skullHeight = value.headHeight,
            muzzleLengthRatio = value.muzzleLengthRatio
        };
    }

    static CreatureV5EarProfile BuildEarProfile(CreatureV5EditableParameters value)
    {
        float length = value.headHeight * value.earLengthRatio;
        return new CreatureV5EarProfile
        {
            length = length,
            width = length * 0.42f,
            outwardAngle = value.earOutwardAngle,
            thickness = Mathf.Max(0.018f, value.headWidth * 0.035f)
        };
    }

    static void CollectSemanticNodes(
        CreatureBodyGraph graph,
        out int neck,
        out int head,
        out int muzzle,
        out int[] spine,
        out int[] tail)
    {
        neck = head = muzzle = -1;
        var spineList = new List<int>();
        var tailList = new List<int>();
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            switch (graph.nodes[i].type)
            {
                case CreatureBodyNodeType.Spine: spineList.Add(i); break;
                case CreatureBodyNodeType.Neck: if (neck < 0) neck = i; break;
                case CreatureBodyNodeType.Head: if (head < 0) head = i; break;
                case CreatureBodyNodeType.Muzzle: if (muzzle < 0) muzzle = i; break;
                case CreatureBodyNodeType.Tail: tailList.Add(i); break;
            }
        }
        spine = spineList.ToArray();
        tail = tailList.ToArray();
    }

    static int FindChild(CreatureBodyGraph graph, int parent, CreatureBodyNodeType type)
    {
        for (int i = parent + 1; i < graph.nodes.Count; i++)
            if (graph.nodes[i].parentIndex == parent && graph.nodes[i].type == type) return i;
        return -1;
    }

    static int CalculateHash(int motionHash, CreatureV5EditableParameters p)
    {
        unchecked
        {
            int hash = motionHash;
            hash = hash * 31 + CreatureGenerationVersions.AnatomicalV5;
            hash = hash * 31 + CreatureV5MeshSchema.Current;
            hash = hash * 31 + p.bodyLength.GetHashCode();
            hash = hash * 31 + p.chestWidth.GetHashCode();
            hash = hash * 31 + p.chestDepth.GetHashCode();
            hash = hash * 31 + p.waistTuck.GetHashCode();
            hash = hash * 31 + p.pelvisWidth.GetHashCode();
            hash = hash * 31 + p.pelvisDepth.GetHashCode();
            hash = hash * 31 + p.legLength.GetHashCode();
            hash = hash * 31 + p.legThickness.GetHashCode();
            hash = hash * 31 + p.distalLegFraction.GetHashCode();
            hash = hash * 31 + p.neckLength.GetHashCode();
            hash = hash * 31 + p.neckAngle.GetHashCode();
            hash = hash * 31 + p.headLength.GetHashCode();
            hash = hash * 31 + p.headWidth.GetHashCode();
            hash = hash * 31 + p.headHeight.GetHashCode();
            hash = hash * 31 + p.hoofLength.GetHashCode();
            hash = hash * 31 + p.hoofWidth.GetHashCode();
            hash = hash * 31 + p.bellyRise.GetHashCode();
            hash = hash * 31 + p.neckRootScale.GetHashCode();
            hash = hash * 31 + p.muzzleLengthRatio.GetHashCode();
            hash = hash * 31 + p.earLengthRatio.GetHashCode();
            hash = hash * 31 + p.earOutwardAngle.GetHashCode();
            return hash;
        }
    }
}
