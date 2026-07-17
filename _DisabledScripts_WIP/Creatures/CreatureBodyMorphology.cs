using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CreatureFaceRegion
{
    public int firstControlPoint;
    public int controlPointCount = 3;
    public float eyeSpacing = 0.48f;
    public float eyeHeight = 0.16f;
    public float mouthHeight = -0.2f;
    public float organSurfaceOffset = 0.025f;
}

[Serializable]
public sealed class CreatureBodyJunction
{
    public int limbRootNodeIndex;
    public int parentSpineNodeIndex;
    public int torsoControlPointIndex;
    public CreatureBodyNodeType limbType;
    public CreatureBodySide side;
    public Vector3 radialDirectionLocal;
    public Vector3 innerPointLocal;
    public Vector3 outerPointLocal;
    public float radius;
    public float blendRadius;
    public float transitionLength;
    public float torsoWeightRange;

    public CreatureBodyJunction Clone()
    {
return (CreatureBodyJunction)MemberwiseClone();
    
    
}
}

public struct CreatureFaceFrame
{
    public Vector3 center;
    public Vector3 forward;
    public Vector3 right;
    public Vector3 up;
    public float halfWidth;
    public float halfHeight;
    public float axialRadius;
}

public static class CreatureBodyMorphology
{
    public static void Ensure(CreatureGenome genome, bool initializeLimbRoots)
    {
if (genome == null || genome.torsoSpline == null || genome.torsoSpline.points == null
            || genome.torsoSpline.points.Count < 2)
            return;

        bool createdFaceRegion = genome.faceRegion == null;
        if (createdFaceRegion)
            genome.faceRegion = CreateFaceRegion(genome);
        if (createdFaceRegion && genome.generatorVersion < 5)
            ApplyGeneratedFaceConstraints(genome);
        if (genome.bodyJunctions == null)
            genome.bodyJunctions = new List<CreatureBodyJunction>();

        RefreshJunctions(genome, initializeLimbRoots || genome.bodyJunctions.Count == 0);
        RefreshSurfaceAttachments(genome);
    
    
}

    public static CreatureFaceRegion CreateFaceRegion(CreatureGenome genome)
    {
uint value = StableHash(unchecked((uint)genome.seed) ^ 0xFACE51D5u);
        int count = Mathf.Clamp(genome.torsoSpline.points.Count >= 6 ? 3 : 2,
            2, genome.torsoSpline.points.Count);
        return new CreatureFaceRegion
        {
            firstControlPoint = genome.torsoSpline.points.Count - count,
            controlPointCount = count,
            eyeSpacing = Mathf.Lerp(.38f, .58f, To01(value)),
            eyeHeight = Mathf.Lerp(.1f, .24f, To01(StableHash(value))),
            mouthHeight = Mathf.Lerp(-.28f, -.14f, To01(StableHash(value ^ 0x91E10DA5u))),
            organSurfaceOffset = -.035f
        };
    
    
}

    public static void ApplyGeneratedFaceConstraints(CreatureGenome genome)
    {
if (genome == null || genome.torsoSpline == null || genome.torsoSpline.points.Count < 2)
            return;
        if (genome.faceRegion == null)
            genome.faceRegion = CreateFaceRegion(genome);

        int start = Mathf.Clamp(genome.faceRegion.firstControlPoint, 1,
            genome.torsoSpline.points.Count - 1);
        CreatureTorsoControlPoint anchor = genome.torsoSpline.points[start - 1];
        uint random = StableHash(unchecked((uint)genome.seed) ^ 0xA341316Cu);
        float browBias = Mathf.Lerp(.94f, 1.14f, To01(random));
        float cheekBias = Mathf.Lerp(.82f, 1.06f, To01(StableHash(random)));
        for (int i = start; i < genome.torsoSpline.points.Count; i++)
        {
            float t = (i - start + 1f) / Mathf.Max(1f, genome.torsoSpline.points.Count - start);
            CreatureTorsoControlPoint point = genome.torsoSpline.points[i];
            float widthFloor = genome.bodyWidth * Mathf.Lerp(.7f, .58f, t) * cheekBias;
            float heightFloor = genome.bodyHeight * Mathf.Lerp(.72f, .62f, t) * browBias;
            point.width = Mathf.Max(point.width, widthFloor, anchor.width * Mathf.Lerp(.82f, .66f, t));
            point.height = Mathf.Max(point.height, heightFloor, anchor.height * Mathf.Lerp(.82f, .68f, t));
            point.taper = Mathf.Max(point.taper, Mathf.Lerp(.96f, .88f, t));
            point.blendRadius = Mathf.Max(point.blendRadius,
                Mathf.Min(point.width, point.height) * .18f);
        }
    
    
}

    public static void RefreshJunctions(CreatureGenome genome, bool initializeDirections)
    {
if (genome.bodyGraph == null || genome.bodyGraph.nodes == null) return;
        var previous = new Dictionary<int, CreatureBodyJunction>();
        if (genome.bodyJunctions != null)
            foreach (CreatureBodyJunction junction in genome.bodyJunctions)
                if (junction != null) previous[junction.limbRootNodeIndex] = junction;

        var refreshed = new List<CreatureBodyJunction>();
        for (int i = 0; i < genome.bodyGraph.nodes.Count; i++)
        {
            CreatureBodyNode node = genome.bodyGraph.nodes[i];
            if (node.type != CreatureBodyNodeType.UpperLeg
                && node.type != CreatureBodyNodeType.UpperArm)
                continue;
            if (node.parentIndex < 0 || node.parentIndex >= genome.bodyGraph.nodes.Count)
                continue;
            CreatureBodyNode parent = genome.bodyGraph.nodes[node.parentIndex];
            if (parent.type != CreatureBodyNodeType.Spine) continue;

            int pointIndex = Mathf.Clamp(parent.chainIndex, 0, genome.torsoSpline.points.Count - 1);
            CreatureTorsoControlPoint point = genome.torsoSpline.points[pointIndex];
            GetPointFrame(genome.torsoSpline, pointIndex, out Vector3 tangent,
                out Vector3 axisX, out Vector3 axisY);

            Vector3 radial;
            if (!initializeDirections && previous.TryGetValue(i, out CreatureBodyJunction old)
                && old.radialDirectionLocal.sqrMagnitude > .001f)
                radial = old.radialDirectionLocal.normalized;
            else
            {
                Vector3 requested = axisX * node.localPosition.x + axisY * node.localPosition.y;
                radial = requested.sqrMagnitude > .001f ? requested.normalized
                    : axisX * (node.side == CreatureBodySide.Left ? -1f : 1f);
            }

            float halfWidth = Mathf.Max(.04f, point.width * point.taper * .5f);
            float halfHeight = Mathf.Max(.04f, point.height * point.taper * .5f);
            float x = Vector3.Dot(radial, axisX);
            float y = Vector3.Dot(radial, axisY);
            float surfaceRadius = 1f / Mathf.Sqrt(
                x * x / (halfWidth * halfWidth) + y * y / (halfHeight * halfHeight));
            float maximumRoot = Mathf.Min(halfWidth, halfHeight) * .58f;
            float rootRadius = Mathf.Clamp(node.radius, .055f, Mathf.Max(.06f, maximumRoot));
            Vector3 center = point.localPosition;
            Vector3 rootOffset = radial * (surfaceRadius * .82f);
            node.localPosition = rootOffset;
            node.radius = rootRadius;

            refreshed.Add(new CreatureBodyJunction
            {
                limbRootNodeIndex = i,
                parentSpineNodeIndex = node.parentIndex,
                torsoControlPointIndex = pointIndex,
                limbType = node.type,
                side = node.side,
                radialDirectionLocal = radial,
                innerPointLocal = center + radial * Mathf.Max(0f, surfaceRadius - rootRadius * 1.1f),
                outerPointLocal = center + radial * (surfaceRadius + rootRadius * .42f),
                radius = rootRadius * 1.28f,
                blendRadius = Mathf.Max(.04f, rootRadius * .72f),
                transitionLength = Mathf.Max(.12f, rootRadius * 1.45f),
                torsoWeightRange = Mathf.Clamp(.12f + rootRadius * .08f, .12f, .2f)
            });
        }
        genome.bodyJunctions = refreshed;
    
    
}

    public static void ApplyJunctionBones(CreatureGenome genome, CreatureRig rig)
    {
if (genome.bodyJunctions == null || rig == null) return;
        foreach (CreatureBodyJunction junction in genome.bodyJunctions)
        {
            int index = junction.limbRootNodeIndex;
            if (index < 0 || index >= rig.nodeBones.Length) continue;
            rig.nodeBones[index].localPosition = rig.graph.nodes[index].localPosition;
        }
    
    
}

    public static void RefreshSurfaceAttachments(CreatureGenome genome)
    {
if (genome == null || genome.bodyGraph == null || genome.torsoSpline == null) return;
        for (int i = 0; i < genome.bodyGraph.nodes.Count; i++)
        {
            CreatureBodyNode node = genome.bodyGraph.nodes[i];
            if (node.type != CreatureBodyNodeType.Horn
                && node.type != CreatureBodyNodeType.BackPlate
                && node.type != CreatureBodyNodeType.Sensor)
                continue;
            if (node.parentIndex < 0 || node.parentIndex >= genome.bodyGraph.nodes.Count) continue;
            CreatureBodyNode parent = genome.bodyGraph.nodes[node.parentIndex];
            if (parent.type != CreatureBodyNodeType.Spine) continue;
            int pointIndex = Mathf.Clamp(parent.chainIndex, 0, genome.torsoSpline.points.Count - 1);
            CreatureTorsoControlPoint point = genome.torsoSpline.points[pointIndex];
            GetPointFrame(genome.torsoSpline, pointIndex, out _, out Vector3 axisX, out Vector3 axisY);
            float side = node.side == CreatureBodySide.Left ? -.18f
                : node.side == CreatureBodySide.Right ? .18f : 0f;
            Vector3 radial = (axisY + axisX * side).normalized;
            float surfaceRadius = Mathf.Max(.05f, point.height * point.taper * .5f);
            float embed = Mathf.Clamp(node.radius * .8f, .025f, surfaceRadius * .28f);
            Vector3 guess = point.localPosition + radial * surfaceRadius;
            Vector3 surface = ProjectToSurface(genome, guess);
            Vector3 normal = SampleSurfaceNormal(genome, surface);
            Vector3 embeddedRoot = surface - normal * embed;
            node.localPosition = embeddedRoot - point.localPosition;
            node.localEulerAngles = Quaternion.FromToRotation(Vector3.up, normal).eulerAngles;
        }
    
    
}

    public static void InvalidateJunctionDirection(CreatureGenome genome, int limbRootNodeIndex)
    {
if (genome == null || genome.bodyJunctions == null) return;
        genome.bodyJunctions.RemoveAll(junction =>
            junction != null && junction.limbRootNodeIndex == limbRootNodeIndex);
    
    
}

    public static bool TryGetFaceFrame(CreatureGenome genome, out CreatureFaceFrame frame)
    {
frame = default;
        if (genome == null || genome.torsoSpline == null || genome.torsoSpline.points.Count < 2)
            return false;
        int last = genome.torsoSpline.points.Count - 1;
        CreatureTorsoControlPoint point = genome.torsoSpline.points[last];
        GetPointFrame(genome.torsoSpline, last, out Vector3 tangent,
            out Vector3 axisX, out Vector3 axisY);
        frame = new CreatureFaceFrame
        {
            center = point.localPosition,
            forward = tangent,
            right = axisX,
            up = axisY,
            halfWidth = Mathf.Max(.04f, point.width * point.taper * .5f),
            halfHeight = Mathf.Max(.04f, point.height * point.taper * .5f),
            axialRadius = Mathf.Max(.04f, Mathf.Min(point.width, point.height) * point.taper * .5f)
        };
        return true;
    
    
}

    public static Vector3 GetFaceSurfacePoint(
        CreatureGenome genome, CreatureFaceFrame frame,
        float horizontal, float vertical, float surfaceOffset)
    {
float x = horizontal * frame.halfWidth;
        float y = vertical * frame.halfHeight;
        float radial = Mathf.Clamp01(1f - horizontal * horizontal - vertical * vertical);
        float z = frame.axialRadius * Mathf.Sqrt(radial);
        Vector3 guess = frame.center + frame.right * x + frame.up * y + frame.forward * z;
        Vector3 surface = ProjectToSurface(genome, guess);
        return surface + SampleSurfaceNormal(genome, surface) * surfaceOffset;
    
    
}

    public static Vector3 ProjectToSurface(CreatureGenome genome, Vector3 localPoint)
    {
if (genome == null || genome.torsoSpline == null) return localPoint;
        Vector3 point = localPoint;
        for (int i = 0; i < 6; i++)
        {
            float distance = CreatureImplicitBodyMesher.SampleDistance(
                genome.torsoSpline, genome.bodyJunctions, point);
            if (Mathf.Abs(distance) < .001f) break;
            Vector3 normal = SampleSurfaceNormal(genome, point);
            if (normal.sqrMagnitude < .5f) break;
            point -= normal * distance;
        }
        return point;
    
    
}

    public static Vector3 SampleSurfaceNormal(CreatureGenome genome, Vector3 localPoint)
    {
const float epsilon = .008f;
        CreatureTorsoSpline spline = genome.torsoSpline;
        IReadOnlyList<CreatureBodyJunction> junctions = genome.bodyJunctions;
        float x = CreatureImplicitBodyMesher.SampleDistance(spline, junctions,
            localPoint + Vector3.right * epsilon)
            - CreatureImplicitBodyMesher.SampleDistance(spline, junctions,
                localPoint - Vector3.right * epsilon);
        float y = CreatureImplicitBodyMesher.SampleDistance(spline, junctions,
            localPoint + Vector3.up * epsilon)
            - CreatureImplicitBodyMesher.SampleDistance(spline, junctions,
                localPoint - Vector3.up * epsilon);
        float z = CreatureImplicitBodyMesher.SampleDistance(spline, junctions,
            localPoint + Vector3.forward * epsilon)
            - CreatureImplicitBodyMesher.SampleDistance(spline, junctions,
                localPoint - Vector3.forward * epsilon);
        Vector3 normal = new Vector3(x, y, z);
        return normal.sqrMagnitude > .000001f ? normal.normalized : Vector3.forward;
    
    
}

    public static void GetPointFrame(CreatureTorsoSpline spline, int index,
        out Vector3 tangent, out Vector3 axisX, out Vector3 axisY)
    {
int last = spline.points.Count - 1;
        tangent = index <= 0
            ? spline.points[1].localPosition - spline.points[0].localPosition
            : index >= last
                ? spline.points[last].localPosition - spline.points[last - 1].localPosition
                : spline.points[index + 1].localPosition - spline.points[index - 1].localPosition;
        tangent = tangent.sqrMagnitude > .0001f ? tangent.normalized : Vector3.forward;
        Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > .9f
            ? Vector3.forward : Vector3.up;
        axisX = Vector3.Cross(reference, tangent).normalized;
        axisY = Vector3.Cross(tangent, axisX).normalized;
        float roll = spline.points[Mathf.Clamp(index, 0, last)].rollDegrees;
        Quaternion rotation = Quaternion.AngleAxis(roll, tangent);
        axisX = rotation * axisX;
        axisY = rotation * axisY;
    
    
}

    static uint StableHash(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        return value ^ (value >> 16);
    }

    static float To01(uint value)
    {
        return (value & 0x00FFFFFFu) / 16777216f;
    }
}
