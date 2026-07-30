using System;
using System.Collections.Generic;
using ModularAssembly;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    [Serializable]
    public struct ModulePhysicsProfile
    {
        public string sourceId;
        public float dragCoefficient;
        public bool isWing;
        public bool isControlSurface;
        public Vector3 chordLocal;
        public Vector3 spanLocal;
        public Vector3 normalLocal;
        public float wingArea;
        public float spanLength;
        public float meanChord;
        public float aspectRatio;
        public float oswaldEfficiency;
        public float sweepRadians;
        public float dihedralRadians;
        public float incidenceRadians;
        public float controlAreaRatio;
        public float maximumControlDeflectionRadians;
        public float stallStartRadians;
        public float stallEndRadians;
    }

    [Serializable]
    public struct ModuleMassElement
    {
        public string runtimeId;
        public float mass;
        public Vector3 centerLocal;
        public Vector3 sizeLocal;
    }

    [Serializable]
    public struct ExposedAeroFace
    {
        public string runtimeId;
        public Vector3 centerLocal;
        public Vector3 normalLocal;
        public float area;
        public float dragCoefficient;
    }

    [Serializable]
    public struct ModuleAeroSurface
    {
        public string runtimeId;
        public Vector3 aerodynamicCenterLocal;
        public Vector3 chordLocal;
        public Vector3 spanLocal;
        public Vector3 normalLocal;
        public float area;
        public bool isControlSurface;
        public float spanLength;
        public float meanChord;
        public float aspectRatio;
        public float oswaldEfficiency;
        public float sweepRadians;
        public float dihedralRadians;
        public float incidenceRadians;
        public float controlAreaRatio;
        public float maximumControlDeflectionRadians;
        public float stallStartRadians;
        public float stallEndRadians;
    }

    [Serializable]
    public struct VehiclePhysicsSnapshot
    {
        public float totalMass;
        public Vector3 centerOfMassLocal;
        public Vector3 principalInertia;
        public Quaternion principalAxes;
        public Vector3 exposedArea;
        public Vector3 dragArea;
        public float wingArea;
        public float currentLift;
        public float currentDrag;
        public Vector3 liftCenterLocal;
        public Vector3 dragCenterLocal;
        public Vector3 aerodynamicTorqueLocal;
        public float angleOfAttackDegrees;
        public float sideSlipDegrees;
        public bool inertiaTriangleValid;
        public bool inertiaFallbackUsed;
        public float maximumDynamicPressure;
        public int aeroPanelCount;
        public int stalledPanelCount;
        public float leftWingSeparation;
        public float rightWingSeparation;
        public float averageGroundEffect;
        public VehicleFlightEnvelopeState flightEnvelope;
        public VehicleStabilitySnapshot stability;
    }

    public sealed partial class VehiclePhysicsRc3State
    {
        const float StructureCd = 0.7f;
        const float FunctionalCd = 0.9f;
        const float LiftSlope = 4.2f;
        const float WingCd0 = 0.035f;
        const float InducedCd = 0.08f;
        const float StallStart = 18f * Mathf.Deg2Rad;
        const float StallEnd = 35f * Mathf.Deg2Rad;
        const float ControlDeflection = 20f * Mathf.Deg2Rad;

        static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left,
            Vector3Int.up, Vector3Int.down,
            Vector3Int.forward, Vector3Int.back
        };

        readonly List<ModuleMassElement> masses =
            new List<ModuleMassElement>();
        readonly List<ExposedAeroFace> faces =
            new List<ExposedAeroFace>();
        readonly List<ModuleAeroSurface> wings =
            new List<ModuleAeroSurface>();
        readonly Dictionary<Vector3Int, string> occupied =
            new Dictionary<Vector3Int, string>();
        readonly Dictionary<string, ModulePhysicsProfile> profiles =
            new Dictionary<string, ModulePhysicsProfile>(StringComparer.Ordinal);
        Quaternion previousPrincipalAxes = Quaternion.identity;
        bool inertiaTriangleValid = true;
        bool inertiaFallbackUsed;

        public VehicleMassProperties MassProperties { get; private set; }
        public VehiclePhysicsSnapshot Snapshot { get; private set; }
        public IReadOnlyList<ExposedAeroFace> ExposedFaces => faces;
        public IReadOnlyList<ModuleAeroSurface> AeroSurfaces => wings;

        public void Rebuild(
            IReadOnlyList<GridModuleView> views,
            Rigidbody body,
            Transform root)
        {
            masses.Clear();
            faces.Clear();
            wings.Clear();
            occupied.Clear();
            profiles.Clear();

            if (views != null)
            {
                foreach (GridModuleView view in views)
                {
                    GridModuleRecord record = view != null ? view.Record : null;
                    if (record == null || record.Definition == null)
                        continue;
                    NeoXBehaviorModule behavior =
                        view.GetComponentInChildren<NeoXBehaviorModule>(true);
                    ModulePhysicsProfile profile = Profile(view, behavior, root);
                    profiles[record.RuntimeId] = profile;
                    Vector3Int size = GridOrientation.RotatedSize(
                        record.Definition.Footprint,
                        record.Pose.orientation);
                    masses.Add(new ModuleMassElement
                    {
                        runtimeId = record.RuntimeId,
                        mass = Mathf.Max(0.01f, record.Definition.MassKg),
                        centerLocal = GridAssemblyModel.ModuleCenter(record),
                        sizeLocal = (Vector3)size
                    });
                    foreach (Vector3Int cell in GridOrientation.NormalizedCells(
                                 record.Definition.Footprint,
                                 record.Pose.orientation))
                    {
                        occupied[record.Pose.Origin + cell] = record.RuntimeId;
                    }
                    if (!profile.isWing)
                        continue;
                    float chordLength = Extent((Vector3)size, profile.chordLocal);
                    wings.Add(new ModuleAeroSurface
                    {
                        runtimeId = record.RuntimeId,
                        aerodynamicCenterLocal =
                            GridAssemblyModel.ModuleCenter(record) +
                            profile.chordLocal * chordLength * 0.25f,
                        chordLocal = profile.chordLocal,
                        spanLocal = profile.spanLocal,
                        normalLocal = profile.normalLocal,
                        area = profile.wingArea,
                        isControlSurface = profile.isControlSurface,
                        spanLength = profile.spanLength,
                        meanChord = profile.meanChord,
                        aspectRatio = profile.aspectRatio,
                        oswaldEfficiency = profile.oswaldEfficiency,
                        sweepRadians = profile.sweepRadians,
                        dihedralRadians = profile.dihedralRadians,
                        incidenceRadians = profile.incidenceRadians,
                        controlAreaRatio = profile.controlAreaRatio,
                        maximumControlDeflectionRadians =
                            profile.maximumControlDeflectionRadians,
                        stallStartRadians = profile.stallStartRadians,
                        stallEndRadians = profile.stallEndRadians
                    });
                }
            }

            VehicleMassProperties mass = SanitizeMassProperties(CalculateMass());
            ApplyMass(body, mass);
            BuildFaces();
            MassProperties = mass;
            VehiclePhysicsSnapshot snapshot = new VehiclePhysicsSnapshot
            {
                totalMass = mass.totalMass,
                centerOfMassLocal = mass.centerOfMassLocal,
                principalInertia = mass.inertiaTensor,
                principalAxes = mass.inertiaTensorRotation,
                inertiaTriangleValid = inertiaTriangleValid,
                inertiaFallbackUsed = inertiaFallbackUsed
            };
            Vector3 areaPositive = Vector3.zero;
            Vector3 areaNegative = Vector3.zero;
            Vector3 cdPositive = Vector3.zero;
            Vector3 cdNegative = Vector3.zero;
            foreach (ExposedAeroFace face in faces)
            {
                int axis = DominantAxis(face.normalLocal);
                if (face.normalLocal[axis] > 0f)
                {
                    areaPositive[axis] += face.area;
                    cdPositive[axis] += face.area * face.dragCoefficient;
                }
                else
                {
                    areaNegative[axis] += face.area;
                    cdNegative[axis] += face.area * face.dragCoefficient;
                }
            }
            snapshot.exposedArea = (areaPositive + areaNegative) * 0.5f;
            snapshot.dragArea = (cdPositive + cdNegative) * 0.5f;
            foreach (ModuleAeroSurface wing in wings)
                snapshot.wingArea += wing.area;
            Snapshot = snapshot;
            RebuildRc33AeroPanels();
        }

        public Vector3 AngularAccelerationForTorque(Vector3 torque)
        {
            Quaternion rotation = MassProperties.inertiaTensorRotation;
            Vector3 principalTorque = Quaternion.Inverse(rotation) * torque;
            Vector3 inertia = MassProperties.inertiaTensor;
            return rotation * new Vector3(
                principalTorque.x / Mathf.Max(1f, inertia.x),
                principalTorque.y / Mathf.Max(1f, inertia.y),
                principalTorque.z / Mathf.Max(1f, inertia.z));
        }

        public Vector3 TorqueForAngularAcceleration(Vector3 acceleration)
        {
            Quaternion rotation = MassProperties.inertiaTensorRotation;
            Vector3 principal = Quaternion.Inverse(rotation) * acceleration;
            return rotation * Vector3.Scale(principal, MassProperties.inertiaTensor);
        }

        public Vector3 DirectionalAngularAcceleration(Vector3 limits)
        {
            return new Vector3(
                Mathf.Abs(AngularAccelerationForTorque(Vector3.right * limits.x).x),
                Mathf.Abs(AngularAccelerationForTorque(Vector3.up * limits.y).y),
                Mathf.Abs(AngularAccelerationForTorque(Vector3.forward * limits.z).z));
        }

        ModulePhysicsProfile Profile(
            GridModuleView view,
            NeoXBehaviorModule behavior,
            Transform root)
        {
            GridModuleRecord record = view.Record;
            GridModuleBehaviorKind kind = behavior != null
                ? behavior.BehaviorKind
                : GridModuleBehaviorKind.None;
            string sourceId = behavior != null && behavior.SourceId != null
                ? behavior.SourceId.ToLowerInvariant()
                : string.Empty;
            bool horizontalWing =
                sourceId.Contains("large_wing_left_361") ||
                sourceId.Contains("large_wing_right_361") ||
                sourceId.Contains("small_wing_left_231") ||
                sourceId.Contains("small_wing_right_231");
            bool verticalTail =
                sourceId.Contains("rudder_wing_223") ||
                sourceId.Contains("waste_rudder");
            bool wing = horizontalWing || verticalTail;
            bool structural = kind == GridModuleBehaviorKind.None ||
                              kind == GridModuleBehaviorKind.Structure ||
                              kind == GridModuleBehaviorKind.Decoration ||
                              kind == GridModuleBehaviorKind.Core ||
                              record.Definition.Category == GridModuleCategory.Structure ||
                              record.Definition.Category == GridModuleCategory.Armor ||
                              record.Definition.Category == GridModuleCategory.Core;
            ModulePhysicsProfile profile = new ModulePhysicsProfile
            {
                sourceId = sourceId,
                dragCoefficient = structural ? StructureCd : FunctionalCd,
                isWing = wing,
                isControlSurface =
                    verticalTail ||
                    kind == GridModuleBehaviorKind.ControlSurface,
                oswaldEfficiency = horizontalWing ? 0.82f : 0.72f,
                controlAreaRatio = verticalTail ? 0.55f : 0.32f,
                maximumControlDeflectionRadians = ControlDeflection,
                stallStartRadians = StallStart,
                stallEndRadians = StallEnd
            };
            if (!wing)
                return profile;

            Quaternion pose = GridOrientation.Rotation(record.Pose.orientation);
            // Physics axes come from the discrete grid pose, never from a
            // visually offset mesh, muzzle socket or thumbnail orientation.
            bool left = sourceId.Contains("_left_");
            bool large = sourceId.Contains("large_wing_");
            float sweepDegrees = horizontalWing
                ? large ? 18f : 12f
                : 0f;
            float dihedralDegrees = horizontalWing ? 5f : 0f;
            float incidenceDegrees = horizontalWing ? 2f : 0f;
            Vector3 chord = pose * Vector3.forward;
            Vector3 span = verticalTail
                ? pose * Vector3.up
                : pose * Vector3.right;
            Vector3 up = verticalTail
                ? pose * Vector3.right
                : pose * Vector3.up;
            if (horizontalWing)
            {
                float mirrorSign = left ? -1f : 1f;
                span = Quaternion.AngleAxis(
                    mirrorSign * sweepDegrees,
                    pose * Vector3.up) * span;
                span = Quaternion.AngleAxis(
                    mirrorSign * dihedralDegrees,
                    chord) * span;
                chord = Quaternion.AngleAxis(
                    -incidenceDegrees,
                    pose * Vector3.right) * chord;
            }
            chord = Safe(chord, pose * Vector3.forward);
            span = Safe(Vector3.ProjectOnPlane(span, chord), pose * Vector3.right);
            Vector3 normal = Vector3.Cross(chord, span).normalized;
            if (normal.sqrMagnitude < 0.001f)
                normal = pose * Vector3.up;
            if (Vector3.Dot(normal, up) < 0f)
                normal = -normal;
            Vector3 size = (Vector3)GridOrientation.RotatedSize(
                record.Definition.Footprint, record.Pose.orientation);
            profile.chordLocal = chord;
            profile.spanLocal = span;
            profile.normalLocal = normal;
            profile.meanChord = Extent(size, chord);
            profile.spanLength = Extent(size, span);
            profile.wingArea = Mathf.Max(
                0.5f,
                profile.meanChord * profile.spanLength *
                (horizontalWing ? 0.82f : 0.72f));
            profile.aspectRatio =
                profile.spanLength * profile.spanLength /
                Mathf.Max(0.1f, profile.wingArea);
            profile.sweepRadians = sweepDegrees * Mathf.Deg2Rad;
            profile.dihedralRadians = dihedralDegrees * Mathf.Deg2Rad;
            profile.incidenceRadians = incidenceDegrees * Mathf.Deg2Rad;
            return profile;
        }

        VehicleMassProperties CalculateMass()
        {
            float total = 0f;
            Vector3 weighted = Vector3.zero;
            foreach (ModuleMassElement element in masses)
            {
                total += element.mass;
                weighted += element.centerLocal * element.mass;
            }
            total = Mathf.Max(1f, total);
            Vector3 center = weighted / total;
            double[,] matrix = new double[3, 3];
            foreach (ModuleMassElement element in masses)
            {
                Vector3 size = element.sizeLocal;
                Vector3 offset = element.centerLocal - center;
                double mass = element.mass;
                matrix[0, 0] += mass / 12.0 *
                    (size.y * size.y + size.z * size.z) +
                    mass * (offset.y * offset.y + offset.z * offset.z);
                matrix[1, 1] += mass / 12.0 *
                    (size.x * size.x + size.z * size.z) +
                    mass * (offset.x * offset.x + offset.z * offset.z);
                matrix[2, 2] += mass / 12.0 *
                    (size.x * size.x + size.y * size.y) +
                    mass * (offset.x * offset.x + offset.y * offset.y);
                matrix[0, 1] -= mass * offset.x * offset.y;
                matrix[0, 2] -= mass * offset.x * offset.z;
                matrix[1, 2] -= mass * offset.y * offset.z;
            }
            matrix[1, 0] = matrix[0, 1];
            matrix[2, 0] = matrix[0, 2];
            matrix[2, 1] = matrix[1, 2];
            Vector3 inertia;
            Quaternion axes;
            Diagonalize(matrix, out inertia, out axes);
            inertiaTriangleValid = ValidateInertiaTriangle(
                ref inertia,
                out bool requiresFallback);
            inertiaFallbackUsed = requiresFallback;
            if (requiresFallback)
            {
                inertia = BoundingBoxInertia(total, center);
                axes = Quaternion.identity;
            }
            if (Quaternion.Dot(previousPrincipalAxes, axes) < 0f)
            {
                axes = new Quaternion(
                    -axes.x,
                    -axes.y,
                    -axes.z,
                    -axes.w);
            }
            previousPrincipalAxes = axes;
            return new VehicleMassProperties
            {
                totalMass = total,
                centerOfMassLocal = center,
                inertiaTensor = inertia,
                inertiaTensorRotation = axes
            };
        }

        static bool ValidateInertiaTriangle(
            ref Vector3 inertia,
            out bool requiresFallback)
        {
            requiresFallback = false;
            float largest = Mathf.Max(inertia.x, Mathf.Max(inertia.y, inertia.z));
            float sumOther = inertia.x + inertia.y + inertia.z - largest;
            if (largest <= sumOther)
                return true;
            float relativeError =
                (largest - sumOther) / Mathf.Max(1f, largest);
            if (relativeError <= 0.001f)
            {
                if (inertia.x == largest)
                    inertia.x = sumOther;
                else if (inertia.y == largest)
                    inertia.y = sumOther;
                else
                    inertia.z = sumOther;
                return true;
            }
            requiresFallback = true;
            return false;
        }

        Vector3 BoundingBoxInertia(float totalMass, Vector3 center)
        {
            if (masses.Count == 0)
                return Vector3.one;
            Vector3 minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            foreach (ModuleMassElement element in masses)
            {
                Vector3 half = element.sizeLocal * 0.5f;
                minimum = Vector3.Min(minimum, element.centerLocal - half);
                maximum = Vector3.Max(maximum, element.centerLocal + half);
            }
            Vector3 size = Vector3.Max(Vector3.one * 0.01f, maximum - minimum);
            return new Vector3(
                totalMass / 12f * (size.y * size.y + size.z * size.z),
                totalMass / 12f * (size.x * size.x + size.z * size.z),
                totalMass / 12f * (size.x * size.x + size.y * size.y));
        }

        static VehicleMassProperties SanitizeMassProperties(
            VehicleMassProperties value)
        {
            if (!Finite(value.totalMass) || value.totalMass < 1f)
                value.totalMass = 1f;
            if (!Finite(value.centerOfMassLocal))
                value.centerOfMassLocal = Vector3.zero;
            Vector3 inertia = value.inertiaTensor;
            if (!Finite(inertia))
                inertia = Vector3.one;
            value.inertiaTensor = new Vector3(
                Mathf.Clamp(inertia.x, 0.01f, 1e9f),
                Mathf.Clamp(inertia.y, 0.01f, 1e9f),
                Mathf.Clamp(inertia.z, 0.01f, 1e9f));
            Quaternion axes = value.inertiaTensorRotation;
            float magnitude = Mathf.Sqrt(
                axes.x * axes.x + axes.y * axes.y +
                axes.z * axes.z + axes.w * axes.w);
            value.inertiaTensorRotation =
                Finite(magnitude) && magnitude > 0.0001f
                    ? new Quaternion(
                        axes.x / magnitude,
                        axes.y / magnitude,
                        axes.z / magnitude,
                        axes.w / magnitude)
                    : Quaternion.identity;
            return value;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }

        static void ApplyMass(Rigidbody body, VehicleMassProperties mass)
        {
            if (body == null)
                return;
            Vector3 oldCenter = body.worldCenterOfMass;
            Vector3 oldVelocity = body.velocity;
            Vector3 angularVelocity = body.angularVelocity;
            body.mass = mass.totalMass;
            body.centerOfMass = mass.centerOfMassLocal;
            body.inertiaTensorRotation = mass.inertiaTensorRotation;
            body.inertiaTensor = mass.inertiaTensor;
            if (!body.isKinematic)
            {
                Vector3 shift = body.worldCenterOfMass - oldCenter;
                body.velocity = oldVelocity + Vector3.Cross(angularVelocity, shift);
            }
        }

        void BuildFaces()
        {
            if (occupied.Count == 0)
                return;
            Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            foreach (Vector3Int cell in occupied.Keys)
            {
                min = Vector3Int.Min(min, cell);
                max = Vector3Int.Max(max, cell);
            }
            min -= Vector3Int.one;
            max += Vector3Int.one;
            var outside = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            outside.Add(min);
            queue.Enqueue(min);
            while (queue.Count > 0)
            {
                Vector3Int cell = queue.Dequeue();
                foreach (Vector3Int direction in Directions)
                {
                    Vector3Int next = cell + direction;
                    if (!Inside(next, min, max) || occupied.ContainsKey(next) ||
                        !outside.Add(next))
                        continue;
                    queue.Enqueue(next);
                }
            }
            foreach (KeyValuePair<Vector3Int, string> pair in occupied)
            {
                ModulePhysicsProfile profile;
                if (!profiles.TryGetValue(pair.Value, out profile) || profile.isWing)
                    continue;
                foreach (Vector3Int direction in Directions)
                {
                    Vector3Int neighbor = pair.Key + direction;
                    if (occupied.ContainsKey(neighbor) || !outside.Contains(neighbor))
                        continue;
                    faces.Add(new ExposedAeroFace
                    {
                        runtimeId = pair.Value,
                        centerLocal = (Vector3)pair.Key + Vector3.one * 0.5f +
                                      (Vector3)direction * 0.5f,
                        normalLocal = direction,
                        area = 1f,
                        dragCoefficient = profile.dragCoefficient
                    });
                }
            }
        }

        public float EstimateIntakeVisibility(
            Vector3 localCenter,
            Vector3 localDirection,
            string sourceRuntimeId)
        {
            Vector3 direction = Safe(localDirection, Vector3.forward);
            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 0.001f)
                side = Vector3.Cross(direction, Vector3.right);
            side.Normalize();
            Vector3 up = Vector3.Cross(side, direction).normalized;
            Vector3[] offsets =
            {
                Vector3.zero,
                side * 0.38f,
                -side * 0.38f,
                up * 0.38f,
                -up * 0.38f
            };
            int visible = 0;
            for (int index = 0; index < offsets.Length; index++)
            {
                Vector3 start = localCenter + offsets[index];
                Vector3 end = start + direction * 3f;
                if (!PathBlockedLocal(
                        start,
                        end,
                        sourceRuntimeId,
                        string.Empty))
                    visible++;
            }
            return visible / (float)offsets.Length;
        }

        public bool IsAirflowPathBlocked(
            Vector3 worldStart,
            Vector3 worldEnd,
            string sourceRuntimeId,
            string targetRuntimeId,
            Transform root)
        {
            if (root == null)
                return false;
            return PathBlockedLocal(
                root.InverseTransformPoint(worldStart),
                root.InverseTransformPoint(worldEnd),
                sourceRuntimeId,
                targetRuntimeId);
        }

        bool PathBlockedLocal(
            Vector3 start,
            Vector3 end,
            string sourceRuntimeId,
            string targetRuntimeId)
        {
            float length = Vector3.Distance(start, end);
            int steps = Mathf.Clamp(Mathf.CeilToInt(length / 0.2f), 1, 256);
            for (int step = 1; step < steps; step++)
            {
                Vector3 point = Vector3.Lerp(start, end, step / (float)steps);
                Vector3Int cell = Vector3Int.FloorToInt(point);
                if (!occupied.TryGetValue(cell, out string runtimeId))
                    continue;
                if (string.Equals(
                        runtimeId,
                        sourceRuntimeId,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        runtimeId,
                        targetRuntimeId,
                        StringComparison.Ordinal))
                    continue;
                return true;
            }
            return false;
        }

        static void Diagonalize(
            double[,] matrix,
            out Vector3 eigenvalues,
            out Quaternion eigenRotation)
        {
            double[,] vectors =
            {
                { 1.0, 0.0, 0.0 },
                { 0.0, 1.0, 0.0 },
                { 0.0, 0.0, 1.0 }
            };
            for (int iteration = 0; iteration < 24; iteration++)
            {
                int p = 0;
                int q = 1;
                double largest = Math.Abs(matrix[0, 1]);
                if (Math.Abs(matrix[0, 2]) > largest)
                {
                    p = 0; q = 2; largest = Math.Abs(matrix[0, 2]);
                }
                if (Math.Abs(matrix[1, 2]) > largest)
                {
                    p = 1; q = 2; largest = Math.Abs(matrix[1, 2]);
                }
                if (largest < 0.000001)
                    break;
                double angle = 0.5 * Math.Atan2(
                    2.0 * matrix[p, q], matrix[q, q] - matrix[p, p]);
                double c = Math.Cos(angle);
                double s = Math.Sin(angle);
                double app = matrix[p, p];
                double aqq = matrix[q, q];
                double apq = matrix[p, q];
                matrix[p, p] = c * c * app - 2.0 * s * c * apq + s * s * aqq;
                matrix[q, q] = s * s * app + 2.0 * s * c * apq + c * c * aqq;
                matrix[p, q] = matrix[q, p] = 0.0;
                for (int k = 0; k < 3; k++)
                {
                    if (k == p || k == q)
                        continue;
                    double akp = matrix[k, p];
                    double akq = matrix[k, q];
                    matrix[k, p] = matrix[p, k] = c * akp - s * akq;
                    matrix[k, q] = matrix[q, k] = s * akp + c * akq;
                }
                for (int k = 0; k < 3; k++)
                {
                    double vkp = vectors[k, p];
                    double vkq = vectors[k, q];
                    vectors[k, p] = c * vkp - s * vkq;
                    vectors[k, q] = s * vkp + c * vkq;
                }
            }
            double[] values = { matrix[0, 0], matrix[1, 1], matrix[2, 2] };
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (a, b) => values[a].CompareTo(values[b]));
            Vector3 x = EigenVector(vectors, order[0]).normalized;
            Vector3 y = Vector3.ProjectOnPlane(
                EigenVector(vectors, order[1]), x).normalized;
            if (y.sqrMagnitude < 0.001f)
                y = Vector3.Cross(x, Vector3.forward).normalized;
            if (y.sqrMagnitude < 0.001f)
                y = Vector3.Cross(x, Vector3.up).normalized;
            Vector3 z = Vector3.Cross(x, y).normalized;
            if (Vector3.Dot(z, EigenVector(vectors, order[2])) < 0f)
            {
                y = -y;
                z = -z;
            }
            eigenvalues = new Vector3(
                Mathf.Max(1f, (float)values[order[0]]),
                Mathf.Max(1f, (float)values[order[1]]),
                Mathf.Max(1f, (float)values[order[2]]));
            eigenRotation = Quaternion.LookRotation(z, y).normalized;
        }

        static Vector3 EigenVector(double[,] vectors, int column)
        {
            return new Vector3(
                (float)vectors[0, column],
                (float)vectors[1, column],
                (float)vectors[2, column]);
        }

        static float Extent(Vector3 size, Vector3 axis)
        {
            return Mathf.Max(0.01f,
                Mathf.Abs(axis.x) * size.x +
                Mathf.Abs(axis.y) * size.y +
                Mathf.Abs(axis.z) * size.z);
        }

        static Vector3 Safe(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 0.001f
                ? value.normalized
                : fallback.normalized;
        }

        static int DominantAxis(Vector3 value)
        {
            Vector3 a = new Vector3(
                Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
            if (a.x >= a.y && a.x >= a.z)
                return 0;
            return a.y >= a.z ? 1 : 2;
        }

        static bool Inside(Vector3Int value, Vector3Int min, Vector3Int max)
        {
            return value.x >= min.x && value.x <= max.x &&
                   value.y >= min.y && value.y <= max.y &&
                   value.z >= min.z && value.z <= max.z;
        }
    }
}
