using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpacecraftEditor
{
    [Flags]
    public enum PlacementAlignmentKind
    {
        None = 0,
        Grid = 1,
        Center = 2,
        NeighborCenter = 4,
        NeighborEdge = 8
    }

    /// <summary>
    /// Deterministic modular-part placement math. The solver works in assembly
    /// local space so the same cursor hit produces the same result regardless
    /// of the ship's world pose.
    /// </summary>
    public static class SpacecraftPlacementSolver
    {
        public readonly struct OrientedBox
        {
            public readonly Vector3 Center;
            public readonly Vector3 Extents;
            public readonly Quaternion Rotation;

            public OrientedBox(Vector3 center, Vector3 extents, Quaternion rotation)
            {
                Center = center;
                Extents = new Vector3(
                    Mathf.Max(0f, extents.x),
                    Mathf.Max(0f, extents.y),
                    Mathf.Max(0f, extents.z));
                Rotation = rotation;
            }
        }

        public static PlacementSnapAxis ResolveAxis(
            Vector3 localSurfaceNormal,
            PlacementSnapAxis currentAxis,
            bool snappingAllowed,
            float enterAngle,
            float releaseAngle)
        {
            if (!snappingAllowed || localSurfaceNormal.sqrMagnitude < 0.000001f)
                return PlacementSnapAxis.None;

            Vector3 direction = localSurfaceNormal.normalized;
            if (currentAxis != PlacementSnapAxis.None &&
                Vector3.Angle(direction, AxisDirection(currentAxis)) <= releaseAngle)
            {
                return currentAxis;
            }

            float absX = Mathf.Abs(direction.x);
            float absY = Mathf.Abs(direction.y);
            float absZ = Mathf.Abs(direction.z);
            PlacementSnapAxis nearest;
            if (absX >= absY && absX >= absZ)
                nearest = direction.x >= 0f ? PlacementSnapAxis.Right : PlacementSnapAxis.Left;
            else if (absY >= absZ)
                nearest = direction.y >= 0f ? PlacementSnapAxis.Top : PlacementSnapAxis.Bottom;
            else
                nearest = direction.z >= 0f ? PlacementSnapAxis.Forward : PlacementSnapAxis.Rear;

            return Vector3.Angle(direction, AxisDirection(nearest)) <= enterAngle
                ? nearest
                : PlacementSnapAxis.None;
        }

        public static Vector3 AxisDirection(PlacementSnapAxis axis)
        {
            switch (axis)
            {
                case PlacementSnapAxis.Forward: return Vector3.forward;
                case PlacementSnapAxis.Rear: return Vector3.back;
                case PlacementSnapAxis.Left: return Vector3.left;
                case PlacementSnapAxis.Right: return Vector3.right;
                case PlacementSnapAxis.Top: return Vector3.up;
                case PlacementSnapAxis.Bottom: return Vector3.down;
                default: return Vector3.zero;
            }
        }

        public static void SurfaceTangents(
            PlacementSnapAxis axis,
            out Vector3 tangentA,
            out Vector3 tangentB)
        {
            switch (axis)
            {
                case PlacementSnapAxis.Left:
                case PlacementSnapAxis.Right:
                    tangentA = Vector3.up;
                    tangentB = Vector3.forward;
                    break;
                case PlacementSnapAxis.Top:
                case PlacementSnapAxis.Bottom:
                    tangentA = Vector3.right;
                    tangentB = Vector3.forward;
                    break;
                default:
                    tangentA = Vector3.right;
                    tangentB = Vector3.up;
                    break;
            }
        }

        public static Vector3 SnapToGrid(
            Vector3 localPoint,
            PlacementSnapAxis axis,
            float gridSize)
        {
            float step = Mathf.Max(0.0001f, gridSize);
            SurfaceTangents(axis, out Vector3 tangentA, out Vector3 tangentB);
            float valueA = Mathf.Round(Vector3.Dot(localPoint, tangentA) / step) * step;
            float valueB = Mathf.Round(Vector3.Dot(localPoint, tangentB) / step) * step;
            Vector3 normal = AxisDirection(axis);
            return normal * Vector3.Dot(localPoint, normal) +
                   tangentA * valueA +
                   tangentB * valueB;
        }

        public static Vector3 ClearTangents(
            Vector3 localPoint,
            PlacementSnapAxis axis)
        {
            Vector3 normal = AxisDirection(axis);
            return normal * Vector3.Dot(localPoint, normal);
        }

        public static bool IsWithinCenter(
            Vector3 localPoint,
            PlacementSnapAxis axis,
            Vector3 hullHalfExtents,
            float normalizedRadius)
        {
            Vector3 safeExtents = new Vector3(
                Mathf.Max(0.0001f, Mathf.Abs(hullHalfExtents.x)),
                Mathf.Max(0.0001f, Mathf.Abs(hullHalfExtents.y)),
                Mathf.Max(0.0001f, Mathf.Abs(hullHalfExtents.z)));
            SurfaceTangents(axis, out Vector3 tangentA, out Vector3 tangentB);
            float extentA = Vector3.Dot(safeExtents, Abs(tangentA));
            float extentB = Vector3.Dot(safeExtents, Abs(tangentB));
            float a = Vector3.Dot(localPoint, tangentA) / Mathf.Max(0.0001f, extentA);
            float b = Vector3.Dot(localPoint, tangentB) / Mathf.Max(0.0001f, extentB);
            return a * a + b * b <= normalizedRadius * normalizedRadius;
        }

        public static float CalculateMountOffset(
            ShipPartDefinition definition,
            Quaternion worldRotation,
            Vector3 worldSurfaceNormal,
            float scale,
            float clearance)
        {
            if (definition == null || definition.Prefab == null)
                return clearance;
            BoxCollider box = definition.Prefab.GetComponent<BoxCollider>();
            if (box == null)
                return clearance;

            Vector3 partNormal =
                Quaternion.Inverse(worldRotation) * worldSurfaceNormal.normalized;
            Vector3 center = box.center * scale;
            Vector3 extents = box.size * (0.5f * scale);
            float minimumProjection =
                Vector3.Dot(center, partNormal) -
                Vector3.Dot(extents, Abs(partNormal));
            return Mathf.Clamp(
                clearance - minimumProjection,
                -0.25f,
                Mathf.Max(extents.x, extents.y, extents.z) + clearance);
        }

        public static PlacementAlignmentKind AlignToNeighbors(
            Transform assembly,
            IReadOnlyList<SpacecraftPart> parts,
            ShipPartDefinition definition,
            float scale,
            PlacementSnapAxis axis,
            Quaternion localRotation,
            float mountOffset,
            float maxDistance,
            ref Vector3 localSurfacePoint,
            SpacecraftPart ignoreA,
            SpacecraftPart ignoreB)
        {
            if (assembly == null || parts == null || definition == null ||
                definition.Prefab == null || maxDistance <= 0f)
            {
                return PlacementAlignmentKind.None;
            }

            BoxCollider candidateCollider =
                definition.Prefab.GetComponent<BoxCollider>();
            if (candidateCollider == null)
                return PlacementAlignmentKind.None;

            Vector3 normal = AxisDirection(axis);
            SurfaceTangents(axis, out Vector3 tangentA, out Vector3 tangentB);
            Vector3 candidateRoot = localSurfacePoint + normal * mountOffset;
            OrientedBox candidate = CandidateLocalBox(
                candidateCollider,
                candidateRoot,
                localRotation,
                scale);
            Project(candidate, tangentA, out float candidateMinA, out float candidateMaxA);
            Project(candidate, tangentB, out float candidateMinB, out float candidateMaxB);
            Project(candidate, normal, out float candidateMinN, out float candidateMaxN);

            float candidateCenterA = (candidateMinA + candidateMaxA) * 0.5f;
            float candidateCenterB = (candidateMinB + candidateMaxB) * 0.5f;
            float candidateRadiusA = (candidateMaxA - candidateMinA) * 0.5f;
            float candidateRadiusB = (candidateMaxB - candidateMinB) * 0.5f;
            float bestA = maxDistance;
            float bestB = maxDistance;
            float shiftA = 0f;
            float shiftB = 0f;
            PlacementAlignmentKind result = PlacementAlignmentKind.None;

            foreach (SpacecraftPart part in parts)
            {
                if (part == null || part == ignoreA || part == ignoreB)
                    continue;
                foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null || collider.isTrigger || !collider.enabled)
                        continue;
                    OrientedBox neighbor = ColliderWorldBox(collider);
                    ProjectWorldIntoAssembly(
                        neighbor,
                        assembly,
                        normal,
                        out float neighborMinN,
                        out float neighborMaxN);
                    float normalGap = IntervalGap(
                        candidateMinN,
                        candidateMaxN,
                        neighborMinN,
                        neighborMaxN);
                    if (normalGap > Mathf.Max(0.08f, maxDistance))
                        continue;

                    ProjectWorldIntoAssembly(
                        neighbor,
                        assembly,
                        tangentA,
                        out float neighborMinA,
                        out float neighborMaxA);
                    ProjectWorldIntoAssembly(
                        neighbor,
                        assembly,
                        tangentB,
                        out float neighborMinB,
                        out float neighborMaxB);

                    if (TrySnapInterval(
                            candidateCenterA,
                            candidateRadiusA,
                            neighborMinA,
                            neighborMaxA,
                            bestA,
                            out float snappedA,
                            out PlacementAlignmentKind kindA))
                    {
                        bestA = Mathf.Abs(snappedA - candidateCenterA);
                        shiftA = snappedA - candidateCenterA;
                        result = (result & ~(
                            PlacementAlignmentKind.NeighborCenter |
                            PlacementAlignmentKind.NeighborEdge)) | kindA;
                    }
                    if (TrySnapInterval(
                            candidateCenterB,
                            candidateRadiusB,
                            neighborMinB,
                            neighborMaxB,
                            bestB,
                            out float snappedB,
                            out PlacementAlignmentKind kindB))
                    {
                        bestB = Mathf.Abs(snappedB - candidateCenterB);
                        shiftB = snappedB - candidateCenterB;
                        result |= kindB;
                    }
                }
            }

            localSurfacePoint += tangentA * shiftA + tangentB * shiftB;
            return result;
        }

        public static bool TrySnapInterval(
            float currentCenter,
            float candidateRadius,
            float neighborMin,
            float neighborMax,
            float maxDistance,
            out float snappedCenter,
            out PlacementAlignmentKind kind)
        {
            float neighborCenter = (neighborMin + neighborMax) * 0.5f;
            float[] targets =
            {
                neighborCenter,
                neighborMin - candidateRadius,
                neighborMax + candidateRadius,
                neighborMin + candidateRadius,
                neighborMax - candidateRadius
            };
            PlacementAlignmentKind[] kinds =
            {
                PlacementAlignmentKind.NeighborCenter,
                PlacementAlignmentKind.NeighborEdge,
                PlacementAlignmentKind.NeighborEdge,
                PlacementAlignmentKind.NeighborEdge,
                PlacementAlignmentKind.NeighborEdge
            };

            snappedCenter = currentCenter;
            kind = PlacementAlignmentKind.None;
            float best = Mathf.Max(0f, maxDistance) + 0.000001f;
            for (int index = 0; index < targets.Length; index++)
            {
                float distance = Mathf.Abs(targets[index] - currentCenter);
                if (distance >= best)
                    continue;
                best = distance;
                snappedCenter = targets[index];
                kind = kinds[index];
            }
            return kind != PlacementAlignmentKind.None;
        }

        public static bool HasBlockingPartOverlap(
            ShipAssembly assembly,
            ShipPartDefinition definition,
            Pose pose,
            float scale,
            float contactTolerance,
            SpacecraftPart ignoreA,
            SpacecraftPart ignoreB)
        {
            if (assembly == null || definition == null || definition.Prefab == null)
                return false;
            BoxCollider candidateCollider =
                definition.Prefab.GetComponent<BoxCollider>();
            if (candidateCollider == null)
                return false;

            OrientedBox candidate = new OrientedBox(
                pose.position + pose.rotation * (candidateCollider.center * scale),
                candidateCollider.size * (0.5f * scale),
                pose.rotation);
            foreach (SpacecraftPart part in assembly.Parts)
            {
                if (part == null || part == ignoreA || part == ignoreB)
                    continue;
                foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null || collider.isTrigger || !collider.enabled)
                        continue;
                    if (Overlaps(candidate, ColliderWorldBox(collider), contactTolerance))
                        return true;
                }
            }
            return false;
        }

        public static bool Overlaps(
            OrientedBox left,
            OrientedBox right,
            float contactTolerance)
        {
            Vector3 a = Vector3.Max(
                Vector3.zero,
                left.Extents - Vector3.one * (contactTolerance * 0.5f));
            Vector3 b = Vector3.Max(
                Vector3.zero,
                right.Extents - Vector3.one * (contactTolerance * 0.5f));
            Vector3[] axisA =
            {
                left.Rotation * Vector3.right,
                left.Rotation * Vector3.up,
                left.Rotation * Vector3.forward
            };
            Vector3[] axisB =
            {
                right.Rotation * Vector3.right,
                right.Rotation * Vector3.up,
                right.Rotation * Vector3.forward
            };
            float[,] rotation = new float[3, 3];
            float[,] absolute = new float[3, 3];
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                rotation[i, j] = Vector3.Dot(axisA[i], axisB[j]);
                absolute[i, j] = Mathf.Abs(rotation[i, j]) + 0.00001f;
            }

            Vector3 delta = right.Center - left.Center;
            float[] translation =
            {
                Vector3.Dot(delta, axisA[0]),
                Vector3.Dot(delta, axisA[1]),
                Vector3.Dot(delta, axisA[2])
            };
            float[] extentA = { a.x, a.y, a.z };
            float[] extentB = { b.x, b.y, b.z };

            for (int i = 0; i < 3; i++)
            {
                float radiusB =
                    extentB[0] * absolute[i, 0] +
                    extentB[1] * absolute[i, 1] +
                    extentB[2] * absolute[i, 2];
                if (Mathf.Abs(translation[i]) > extentA[i] + radiusB)
                    return false;
            }
            for (int j = 0; j < 3; j++)
            {
                float projected =
                    Mathf.Abs(
                        translation[0] * rotation[0, j] +
                        translation[1] * rotation[1, j] +
                        translation[2] * rotation[2, j]);
                float radiusA =
                    extentA[0] * absolute[0, j] +
                    extentA[1] * absolute[1, j] +
                    extentA[2] * absolute[2, j];
                if (projected > radiusA + extentB[j])
                    return false;
            }

            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                int i1 = (i + 1) % 3;
                int i2 = (i + 2) % 3;
                int j1 = (j + 1) % 3;
                int j2 = (j + 2) % 3;
                float projected = Mathf.Abs(
                    translation[i2] * rotation[i1, j] -
                    translation[i1] * rotation[i2, j]);
                float radiusA =
                    extentA[i1] * absolute[i2, j] +
                    extentA[i2] * absolute[i1, j];
                float radiusB =
                    extentB[j1] * absolute[i, j2] +
                    extentB[j2] * absolute[i, j1];
                if (projected > radiusA + radiusB)
                    return false;
            }
            return true;
        }

        private static OrientedBox CandidateLocalBox(
            BoxCollider collider,
            Vector3 rootPosition,
            Quaternion rootRotation,
            float scale)
        {
            return new OrientedBox(
                rootPosition + rootRotation * (collider.center * scale),
                collider.size * (0.5f * scale),
                rootRotation);
        }

        private static OrientedBox ColliderWorldBox(Collider collider)
        {
            BoxCollider box = collider as BoxCollider;
            if (box != null)
            {
                Vector3 scale = Abs(box.transform.lossyScale);
                return new OrientedBox(
                    box.transform.TransformPoint(box.center),
                    Vector3.Scale(box.size * 0.5f, scale),
                    box.transform.rotation);
            }

            Bounds bounds = collider.bounds;
            return new OrientedBox(bounds.center, bounds.extents, Quaternion.identity);
        }

        private static void Project(
            OrientedBox box,
            Vector3 axis,
            out float minimum,
            out float maximum)
        {
            Vector3 direction = axis.normalized;
            float center = Vector3.Dot(box.Center, direction);
            float radius =
                box.Extents.x * Mathf.Abs(Vector3.Dot(box.Rotation * Vector3.right, direction)) +
                box.Extents.y * Mathf.Abs(Vector3.Dot(box.Rotation * Vector3.up, direction)) +
                box.Extents.z * Mathf.Abs(Vector3.Dot(box.Rotation * Vector3.forward, direction));
            minimum = center - radius;
            maximum = center + radius;
        }

        private static void ProjectWorldIntoAssembly(
            OrientedBox worldBox,
            Transform assembly,
            Vector3 localAxis,
            out float minimum,
            out float maximum)
        {
            Vector3 worldAxis = assembly.TransformDirection(localAxis).normalized;
            Project(worldBox, worldAxis, out float worldMinimum, out float worldMaximum);
            float origin = Vector3.Dot(assembly.position, worldAxis);
            minimum = worldMinimum - origin;
            maximum = worldMaximum - origin;
        }

        private static float IntervalGap(
            float leftMin,
            float leftMax,
            float rightMin,
            float rightMax)
        {
            if (leftMax < rightMin)
                return rightMin - leftMax;
            if (rightMax < leftMin)
                return leftMin - rightMax;
            return 0f;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }
    }
}
