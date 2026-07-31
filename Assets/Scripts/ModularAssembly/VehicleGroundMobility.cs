using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    /// <summary>
    /// Owns ground-mobility coordination while every physical force still
    /// flows through the vehicle's single VehicleForceLedger.
    /// </summary>
    public sealed class VehicleGroundMobility
    {
        private struct AntiRollPair
        {
            public ModularWheelRuntime left;
            public ModularWheelRuntime right;
        }

        private readonly List<ModularWheelRuntime> wheels =
            new List<ModularWheelRuntime>();
        private readonly List<AntiRollPair> antiRollPairs =
            new List<AntiRollPair>();
        private readonly List<Vector2> supportHull =
            new List<Vector2>();
        private Rigidbody body;
        private Transform vehicleRoot;
        private bool flightMode;
        private int groundedWheelCount;
        private float supportRatio;
        private float groundControlWeight;
        private float supportMargin = float.NegativeInfinity;
        private float lateralSupportMargin;
        private float leftSupportMargin;
        private float rightSupportMargin;
        private float trackWidth = 1f;
        private float wheelbase = 1f;
        private float lateralCenter;
        private float lastGravityMagnitude = 9.81f;
        private bool hasStraightLineHeading;
        private Vector3 straightLineHeadingWorld;
        private float straightLineAssistAngle;

        public IReadOnlyList<ModularWheelRuntime> Wheels => wheels;
        public int WheelCount => wheels.Count;
        public int GroundedWheelCount => groundedWheelCount;
        public float SupportRatio => supportRatio;
        public float GroundControlWeight => groundControlWeight;
        public float SupportMargin => supportMargin;
        public float LateralSupportMargin => lateralSupportMargin;
        public float LeftSupportMargin => leftSupportMargin;
        public float RightSupportMargin => rightSupportMargin;
        public float StraightLineAssistAngle =>
            straightLineAssistAngle;
        public bool HasStableGroundSupport =>
            groundedWheelCount >= 3 &&
            supportMargin > 0.08f &&
            supportRatio >= 0.35f;
        public float TopSpeed => wheels
            .Where(item =>
                item != null &&
                item.EffectiveRole != WheelRoleOverride.FreeRolling)
            .Select(item => item.Profile.maximumSpeed)
            .DefaultIfEmpty(0f)
            .Max();

        public void Rebuild(
            Rigidbody targetBody,
            Transform root,
            IEnumerable<ModularWheelRuntime> source)
        {
            ModularWheelRuntime[] previous = wheels.ToArray();
            body = targetBody;
            vehicleRoot = root;
            hasStraightLineHeading = false;
            straightLineAssistAngle = 0f;
            wheels.Clear();
            if (source != null)
            {
                wheels.AddRange(source
                    .Where(item =>
                        item != null &&
                        item.enabled &&
                        item.gameObject.activeInHierarchy)
                    .Distinct());
            }

            foreach (ModularWheelRuntime removed in previous)
            {
                if (removed != null && !wheels.Contains(removed))
                    removed.SetFlightMode(flightMode);
            }
            foreach (ModularWheelRuntime wheel in wheels)
            {
                wheel.BindVehicle(body, vehicleRoot);
                wheel.SetFlightMode(flightMode);
            }

            AssignAutomaticRoles();
            AssignSprungMasses();
            BuildGeometryAndAntiRollPairs();
            if (wheels.Count == 0)
            {
                groundedWheelCount = 0;
                supportRatio = 0f;
                groundControlWeight = 0f;
                supportMargin = float.NegativeInfinity;
                lateralSupportMargin = 0f;
                leftSupportMargin = 0f;
                rightSupportMargin = 0f;
                supportHull.Clear();
            }
        }

        public void SetFlightMode(bool value)
        {
            flightMode = value;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel != null)
                    wheel.SetFlightMode(value);
            }
            if (!value)
            {
                hasStraightLineHeading = false;
                straightLineAssistAngle = 0f;
                groundedWheelCount = 0;
                supportRatio = 0f;
                groundControlWeight = 0f;
                supportMargin = float.NegativeInfinity;
                lateralSupportMargin = 0f;
                leftSupportMargin = 0f;
                rightSupportMargin = 0f;
                supportHull.Clear();
            }
        }

        public void BeginPhysicsStep(
            Vector3 up,
            float gravityMagnitude,
            float fixedStep,
            VehicleForceLedger ledger = null)
        {
            groundedWheelCount = 0;
            supportRatio = 0f;
            if (body == null || vehicleRoot == null || wheels.Count == 0)
            {
                groundControlWeight = 0f;
                supportMargin = float.NegativeInfinity;
                lateralSupportMargin = 0f;
                leftSupportMargin = 0f;
                rightSupportMargin = 0f;
                supportHull.Clear();
                return;
            }

            float step = Mathf.Max(0.0001f, fixedStep);
            float gravityValue = Mathf.Max(0.1f, gravityMagnitude);
            lastGravityMagnitude = gravityValue;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel == null ||
                    !wheel.enabled ||
                    !wheel.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (wheel.ProbeContact(up, step, ledger))
                {
                    groundedWheelCount++;
                    wheel.PrepareSuspension(up, gravityValue, step);
                }
            }

            ApplyAntiRollLoadTransfer();
            UpdateSupportGeometry(up);
            UpdateSupportState(
                up,
                gravityValue,
                step,
                false);
        }

        public void ApplyControl(
            Vector3 up,
            float gravityMagnitude,
            Vector2 move,
            bool braking,
            bool boosting,
            VehicleForceLedger ledger,
            float fixedStep)
        {
            if (body == null || vehicleRoot == null)
                return;

            float throttle = braking
                ? 0f
                : Mathf.Clamp(move.y, -1f, 1f);
            float steeringInput = Mathf.Clamp(move.x, -1f, 1f);
            float gravityValue = gravityMagnitude > 0.1f
                ? gravityMagnitude
                : lastGravityMagnitude;
            Vector3 planarForward = Vector3.ProjectOnPlane(
                vehicleRoot.forward,
                up);
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = Vector3.forward;
            else
                planarForward.Normalize();
            float forwardSpeed = Vector3.Dot(
                body.velocity,
                planarForward);
            float targetSteer = ResolveSafeSteerAngle(
                steeringInput,
                forwardSpeed,
                up,
                gravityValue);
            targetSteer += ResolveStraightLineAssistAngle(
                steeringInput,
                forwardSpeed,
                up,
                gravityValue);
            float maximumInstalledSteer = wheels
                .Where(item =>
                    item != null &&
                    item.EffectiveRole ==
                    WheelRoleOverride.SteerDrive)
                .Select(item => item.Profile.maximumSteerAngle)
                .DefaultIfEmpty(0f)
                .Max();
            if (maximumInstalledSteer > 0.01f)
            {
                targetSteer = ResolveSafeSteerAngle(
                    Mathf.Clamp(
                        targetSteer / maximumInstalledSteer,
                        -1f,
                        1f),
                    forwardSpeed,
                    up,
                    gravityValue);
            }

            UpdateModuleActuationShares();
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel == null ||
                    !wheel.enabled ||
                    !wheel.gameObject.activeInHierarchy)
                {
                    continue;
                }
                float wheelSteer = ResolveAckermannAngle(
                    wheel,
                    targetSteer);
                wheel.ApplyForces(
                    up,
                    throttle,
                    wheelSteer,
                    braking,
                    boosting,
                    1f,
                    fixedStep,
                    ledger);
            }

            // Contact constraints share one rigid body. Four short,
            // alternating sequential passes let each wheel revise its
            // accumulated unilateral impulse. This removes processing-order
            // bias without adding a hidden upright torque.
            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool reverse = (iteration & 1) == 0;
                for (int offset = 0; offset < wheels.Count; offset++)
                {
                    int index = reverse
                        ? wheels.Count - 1 - offset
                        : offset;
                    ModularWheelRuntime wheel = wheels[index];
                    if (wheel == null ||
                        !wheel.enabled ||
                        !wheel.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    wheel.ApplyConstraintCorrection(
                        fixedStep,
                        ledger);
                }
            }
            UpdateSupportGeometry(up);
            UpdateSupportState(
                up,
                gravityValue,
                Mathf.Max(0.0001f, fixedStep),
                true);
        }

        public void ApplyPassive(
            Vector3 up,
            VehicleForceLedger ledger,
            float fixedStep)
        {
            ApplyControl(
                up,
                lastGravityMagnitude,
                Vector2.zero,
                false,
                false,
                ledger,
                fixedStep);
        }

        public void FinalizePhysicsStep(
            Vector3 up,
            float gravityMagnitude,
            VehicleForceLedger ledger,
            float fixedStep)
        {
            if (body == null ||
                vehicleRoot == null ||
                ledger == null ||
                wheels.Count == 0)
            {
                return;
            }

            float step = Mathf.Max(0.0001f, fixedStep);
            float gravityValue = gravityMagnitude > 0.1f
                ? gravityMagnitude
                : lastGravityMagnitude;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel == null ||
                    !wheel.enabled ||
                    !wheel.gameObject.activeInHierarchy)
                {
                    continue;
                }
                wheel.PrepareFinalConstraints(
                    up,
                    gravityValue,
                    step,
                    ledger);
            }

            for (int iteration = 0; iteration < 6; iteration++)
            {
                bool reverse = (iteration & 1) == 0;
                for (int offset = 0;
                     offset < wheels.Count;
                     offset++)
                {
                    int index = reverse
                        ? wheels.Count - 1 - offset
                        : offset;
                    ModularWheelRuntime wheel = wheels[index];
                    if (wheel == null ||
                        !wheel.enabled ||
                        !wheel.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    wheel.ApplyConstraintCorrection(step, ledger);
                }
            }

            groundedWheelCount = wheels.Count(item =>
                item != null &&
                item.enabled &&
                item.gameObject.activeInHierarchy &&
                item.IsGrounded);
            UpdateSupportGeometry(up);
            UpdateSupportState(
                up,
                gravityValue,
                step,
                false);
        }

        private void UpdateSupportState(
            Vector3 up,
            float gravityMagnitude,
            float step,
            bool advanceControlBlend)
        {
            if (body == null)
            {
                supportRatio = 0f;
                if (advanceControlBlend)
                    groundControlWeight = 0f;
                return;
            }

            float supportedForce = 0f;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel != null && wheel.IsGrounded)
                {
                    supportedForce += wheel.NormalForce *
                        Mathf.Max(
                            0f,
                            Vector3.Dot(wheel.ContactNormal, up));
                }
            }
            supportRatio = supportedForce /
                Mathf.Max(
                    1f,
                    body.mass *
                    Mathf.Max(0.1f, gravityMagnitude));
            if (!advanceControlBlend)
                return;

            float contactCoverage;
            if (supportMargin > 0f)
            {
                contactCoverage = Mathf.Clamp01(
                    supportMargin / 0.18f);
            }
            else
            {
                contactCoverage = groundedWheelCount >= 2
                    ? 0.28f
                    : groundedWheelCount == 1 ? 0.14f : 0f;
            }
            float targetWeight =
                Mathf.Clamp01(supportRatio / 0.72f) *
                contactCoverage;
            float response = targetWeight > groundControlWeight
                ? 18f
                : 7f;
            groundControlWeight = Mathf.Lerp(
                groundControlWeight,
                targetWeight,
                1f - Mathf.Exp(-response * step));
            if (targetWeight <= 0f &&
                groundControlWeight < 0.001f)
            {
                groundControlWeight = 0f;
            }
        }

        public static float[] SolveSprungMasses(
            Vector2[] wheelPositionsFromCenterOfMass,
            float totalMass)
        {
            int count = wheelPositionsFromCenterOfMass?.Length ?? 0;
            if (count == 0)
                return Array.Empty<float>();

            bool[] active = Enumerable.Repeat(true, count).ToArray();
            float[] weights = new float[count];
            bool solved = false;
            for (int pass = 0; pass < count; pass++)
            {
                if (!TrySolvePositiveWeights(
                        wheelPositionsFromCenterOfMass,
                        active,
                        weights))
                {
                    break;
                }
                int mostNegative = -1;
                float minimum = -0.0001f;
                for (int index = 0; index < count; index++)
                {
                    if (active[index] && weights[index] < minimum)
                    {
                        minimum = weights[index];
                        mostNegative = index;
                    }
                }
                if (mostNegative < 0)
                {
                    solved = true;
                    break;
                }
                active[mostNegative] = false;
            }

            if (!solved)
            {
                float sum = 0f;
                for (int index = 0; index < count; index++)
                {
                    float distanceSquared =
                        wheelPositionsFromCenterOfMass[index].sqrMagnitude;
                    weights[index] = 1f / (0.25f + distanceSquared);
                    sum += weights[index];
                }
                for (int index = 0; index < count; index++)
                    weights[index] /= Mathf.Max(0.0001f, sum);
            }
            else
            {
                float sum = weights.Sum(item => Mathf.Max(0f, item));
                for (int index = 0; index < count; index++)
                {
                    weights[index] =
                        Mathf.Max(0f, weights[index]) /
                        Mathf.Max(0.0001f, sum);
                }
            }

            float mass = Mathf.Max(0f, totalMass);
            for (int index = 0; index < count; index++)
                weights[index] *= mass;
            return weights;
        }

        private void AssignAutomaticRoles()
        {
            if (wheels.Count == 0 || vehicleRoot == null || body == null)
                return;
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                float longitudinal =
                    LocalPositionFromCenterOfMass(wheel).z;
                minimum = Mathf.Min(minimum, longitudinal);
                maximum = Mathf.Max(maximum, longitudinal);
            }
            float midpoint = (minimum + maximum) * 0.5f;
            bool oneAxle =
                maximum - minimum <
                wheels.Max(item => item.Profile.radius) * 0.65f;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                WheelRoleOverride role;
                if (wheel.Profile.racingFront)
                    role = WheelRoleOverride.SteerDrive;
                else if (wheel.Profile.racingRear ||
                         wheel.Profile.maximumSteerAngle <= 0.01f)
                    role = WheelRoleOverride.DriveOnly;
                else
                {
                    role = oneAxle ||
                           LocalPositionFromCenterOfMass(wheel).z >= midpoint
                        ? WheelRoleOverride.SteerDrive
                        : WheelRoleOverride.DriveOnly;
                }
                wheel.SetAutomaticRole(role);
            }
        }

        private void AssignSprungMasses()
        {
            if (wheels.Count == 0 || body == null)
                return;
            Vector2[] positions = wheels
                .Select(item =>
                {
                    Vector3 local = LocalPositionFromCenterOfMass(item);
                    return new Vector2(local.x, local.z);
                })
                .ToArray();
            float[] masses = SolveSprungMasses(positions, body.mass);
            for (int index = 0; index < wheels.Count; index++)
                wheels[index].SetSprungMass(masses[index]);
        }

        private void BuildGeometryAndAntiRollPairs()
        {
            antiRollPairs.Clear();
            if (wheels.Count == 0)
                return;

            var positions = wheels.ToDictionary(
                item => item,
                LocalPositionFromCenterOfMass);
            float minimumX = positions.Values.Min(item => item.x);
            float maximumX = positions.Values.Max(item => item.x);
            float minimumZ = positions.Values.Min(item => item.z);
            float maximumZ = positions.Values.Max(item => item.z);
            trackWidth = Mathf.Max(0.5f, maximumX - minimumX);
            wheelbase = Mathf.Max(0.75f, maximumZ - minimumZ);
            lateralCenter = (minimumX + maximumX) * 0.5f;

            var left = wheels
                .Where(item => positions[item].x < lateralCenter - 0.05f)
                .OrderBy(item => positions[item].z)
                .ToList();
            var availableRight = wheels
                .Where(item => positions[item].x > lateralCenter + 0.05f)
                .ToList();
            foreach (ModularWheelRuntime leftWheel in left)
            {
                ModularWheelRuntime rightWheel = availableRight
                    .OrderBy(item =>
                        Mathf.Abs(
                            positions[item].z -
                            positions[leftWheel].z) +
                        Mathf.Abs(
                            item.Profile.radius -
                            leftWheel.Profile.radius) * 0.5f)
                    .FirstOrDefault();
                if (rightWheel == null)
                    continue;
                availableRight.Remove(rightWheel);
                antiRollPairs.Add(new AntiRollPair
                {
                    left = leftWheel,
                    right = rightWheel
                });
            }
        }

        private void ApplyAntiRollLoadTransfer()
        {
            foreach (AntiRollPair pair in antiRollPairs)
            {
                if (pair.left == null ||
                    pair.right == null ||
                    !pair.left.IsGrounded ||
                    !pair.right.IsGrounded)
                {
                    continue;
                }
                float averageLoad =
                    (pair.left.NormalForce +
                     pair.right.NormalForce) * 0.5f;
                float rawTransfer =
                    (pair.left.Compression -
                     pair.right.Compression) *
                    averageLoad * 0.8f;
                float limit = Mathf.Min(
                    averageLoad * 0.24f,
                    rawTransfer >= 0f
                        ? pair.right.NormalForce * 0.45f
                        : pair.left.NormalForce * 0.45f);
                float transfer = Mathf.Clamp(
                    rawTransfer,
                    -limit,
                    limit);
                Vector3 commonAxis =
                    pair.left.SuspensionUpWorld +
                    pair.right.SuspensionUpWorld;
                if (commonAxis.sqrMagnitude < 0.0001f)
                    continue;
                commonAxis.Normalize();
                // Equal and opposite chassis forces form a pure roll couple.
                // Adjusting two differently oriented contact normals instead
                // creates a net sideways force on broken terrain.
                pair.left.SetAntiRollForce(
                    commonAxis * transfer);
                pair.right.SetAntiRollForce(
                    -commonAxis * transfer);
            }
        }

        private void UpdateSupportGeometry(Vector3 up)
        {
            supportMargin = float.NegativeInfinity;
            lateralSupportMargin = 0f;
            leftSupportMargin = 0f;
            rightSupportMargin = 0f;
            supportHull.Clear();
            if (body == null || vehicleRoot == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(
                vehicleRoot.forward,
                up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 center = body.worldCenterOfMass;
            var points = new List<Vector2>();
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel == null || !wheel.IsGrounded)
                    continue;
                Vector3 offset = wheel.ContactPoint - center;
                Vector2 point = new Vector2(
                    Vector3.Dot(offset, right),
                    Vector3.Dot(offset, forward));
                bool duplicate = points.Any(existing =>
                    (existing - point).sqrMagnitude < 0.0001f);
                if (!duplicate)
                    points.Add(point);
            }
            if (points.Count == 0)
                return;

            if (points.Count < 3)
                return;

            points.Sort((left, rightPoint) =>
            {
                int x = left.x.CompareTo(rightPoint.x);
                return x != 0
                    ? x
                    : left.y.CompareTo(rightPoint.y);
            });
            var hull = new List<Vector2>();
            foreach (Vector2 point in points)
            {
                while (hull.Count >= 2 &&
                       Cross(
                           hull[hull.Count - 1] -
                           hull[hull.Count - 2],
                           point - hull[hull.Count - 1]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }
            int lowerCount = hull.Count;
            for (int index = points.Count - 2; index >= 0; index--)
            {
                Vector2 point = points[index];
                while (hull.Count > lowerCount &&
                       Cross(
                           hull[hull.Count - 1] -
                           hull[hull.Count - 2],
                           point - hull[hull.Count - 1]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }
            if (hull.Count > 1)
                hull.RemoveAt(hull.Count - 1);
            if (hull.Count < 3)
                return;
            supportHull.AddRange(hull);

            float margin = float.PositiveInfinity;
            for (int index = 0; index < hull.Count; index++)
            {
                Vector2 start = hull[index];
                Vector2 end = hull[(index + 1) % hull.Count];
                Vector2 edge = end - start;
                float edgeLength = edge.magnitude;
                if (edgeLength < 0.0001f)
                    continue;
                float signedDistance =
                    Cross(edge, -start) / edgeLength;
                margin = Mathf.Min(margin, signedDistance);
            }
            supportMargin = margin;
            if (supportMargin <= 0f)
                return;

            leftSupportMargin = RayDistanceToHull(
                supportHull,
                Vector2.left);
            rightSupportMargin = RayDistanceToHull(
                supportHull,
                Vector2.right);
            lateralSupportMargin = Mathf.Min(
                leftSupportMargin,
                rightSupportMargin);
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        private static float RayDistanceToHull(
            IReadOnlyList<Vector2> hull,
            Vector2 direction)
        {
            if (hull == null ||
                hull.Count < 3 ||
                direction.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            Vector2 ray = direction.normalized;
            float result = float.PositiveInfinity;
            for (int index = 0; index < hull.Count; index++)
            {
                Vector2 start = hull[index];
                Vector2 edge =
                    hull[(index + 1) % hull.Count] - start;
                float denominator = Cross(ray, edge);
                if (Mathf.Abs(denominator) < 0.000001f)
                    continue;
                float distance = Cross(start, edge) / denominator;
                float edgeParameter =
                    Cross(start, ray) / denominator;
                if (distance >= 0f &&
                    edgeParameter >= -0.0001f &&
                    edgeParameter <= 1.0001f)
                {
                    result = Mathf.Min(result, distance);
                }
            }
            return float.IsPositiveInfinity(result)
                ? 0f
                : result;
        }

        private float ResolveSafeSteerAngle(
            float steeringInput,
            float forwardSpeed,
            Vector3 up,
            float gravityMagnitude)
        {
            float requestedMaximum = wheels
                .Where(item =>
                    item != null &&
                    item.EffectiveRole ==
                    WheelRoleOverride.SteerDrive)
                .Select(item => item.Profile.maximumSteerAngle)
                .DefaultIfEmpty(0f)
                .Max();
            float requested = steeringInput * requestedMaximum;
            if (Mathf.Abs(requested) < 0.001f)
                return 0f;

            Vector3 averageContact = Vector3.zero;
            int contacts = 0;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel != null && wheel.IsGrounded)
                {
                    averageContact += wheel.ContactPoint;
                    contacts++;
                }
            }
            if (contacts > 0)
                averageContact /= contacts;
            else
                averageContact =
                    body.worldCenterOfMass - up * 0.75f;
            float centerOfMassHeight = Mathf.Max(
                0.35f,
                Vector3.Dot(
                    body.worldCenterOfMass - averageContact,
                    up));
            float gravityValue = Mathf.Max(0.1f, gravityMagnitude);
            float supportLever = contacts >= 2
                ? lateralSupportMargin
                : trackWidth * 0.5f;
            float rolloverAcceleration =
                gravityValue * Mathf.Max(0f, supportLever) /
                centerOfMassHeight * 0.72f;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(
                body.velocity,
                up);
            float speedSquared = planarVelocity.sqrMagnitude;
            float availableLateralGrip = wheels
                .Where(item => item != null && item.IsGrounded)
                .Select(item =>
                    item.Profile.lateralFriction *
                    item.Telemetry.surfaceGrip)
                .DefaultIfEmpty(1f)
                .Min();
            float tireAcceleration =
                gravityValue *
                Mathf.Max(0f, availableLateralGrip) *
                0.82f;
            rolloverAcceleration = Mathf.Min(
                rolloverAcceleration,
                tireAcceleration);
            float rolloverAngle = speedSquared < 0.25f
                ? requestedMaximum
                : Mathf.Atan(
                    rolloverAcceleration * wheelbase /
                    speedSquared) * Mathf.Rad2Deg;
            float responseAngle =
                requestedMaximum /
                (1f + Mathf.Pow(
                    Mathf.Abs(forwardSpeed) /
                    Mathf.Max(18f, TopSpeed * 0.5f),
                    2f) * 0.35f);
            float safeMaximum = Mathf.Max(
                0f,
                Mathf.Min(
                    requestedMaximum,
                    rolloverAngle,
                    responseAngle));
            return Mathf.Sign(requested) *
                   Mathf.Min(Mathf.Abs(requested), safeMaximum);
        }

        private float ResolveStraightLineAssistAngle(
            float steeringInput,
            float forwardSpeed,
            Vector3 up,
            float gravityMagnitude)
        {
            straightLineAssistAngle = 0f;
            if (body == null || vehicleRoot == null)
                return 0f;

            Vector3 currentForward = Vector3.ProjectOnPlane(
                vehicleRoot.forward,
                up);
            if (currentForward.sqrMagnitude < 0.0001f)
                return 0f;
            currentForward.Normalize();

            float neutralInputWeight = 1f - Mathf.InverseLerp(
                0.025f,
                0.16f,
                Mathf.Abs(steeringInput));
            int steerableGroundedCount = wheels.Count(item =>
                item != null &&
                item.IsGrounded &&
                item.EffectiveRole ==
                WheelRoleOverride.SteerDrive);
            if (neutralInputWeight <= 0f ||
                Mathf.Abs(forwardSpeed) < 2f ||
                steerableGroundedCount == 0 ||
                groundControlWeight < 0.15f)
            {
                straightLineHeadingWorld = currentForward;
                hasStraightLineHeading = true;
                return 0f;
            }

            Vector3 transportedHeading = Vector3.ProjectOnPlane(
                straightLineHeadingWorld,
                up);
            if (!hasStraightLineHeading ||
                transportedHeading.sqrMagnitude < 0.0001f)
            {
                straightLineHeadingWorld = currentForward;
                hasStraightLineHeading = true;
                return 0f;
            }
            transportedHeading.Normalize();
            if (Vector3.Dot(
                    currentForward,
                    transportedHeading) < 0.25f)
            {
                straightLineHeadingWorld = currentForward;
                return 0f;
            }
            straightLineHeadingWorld = transportedHeading;

            float requestedMaximum = wheels
                .Where(item =>
                    item != null &&
                    item.IsGrounded &&
                    item.EffectiveRole ==
                    WheelRoleOverride.SteerDrive)
                .Select(item => item.Profile.maximumSteerAngle)
                .DefaultIfEmpty(0f)
                .Max();
            if (requestedMaximum <= 0.01f)
                return 0f;

            Vector3 right =
                Vector3.Cross(up, currentForward).normalized;
            float lateralSpeed =
                Vector3.Dot(body.velocity, right);
            float directionSign = Mathf.Sign(forwardSpeed);
            float headingError = Vector3.SignedAngle(
                currentForward,
                transportedHeading,
                up);
            float slipCorrection =
                Mathf.Atan2(
                    -lateralSpeed,
                    Mathf.Max(4f, Mathf.Abs(forwardSpeed))) *
                Mathf.Rad2Deg * 0.45f;
            float yawRateDegrees =
                Vector3.Dot(body.angularVelocity, up) *
                Mathf.Rad2Deg;
            float requestedAssist =
                (headingError * 0.65f +
                 slipCorrection -
                 yawRateDegrees * 0.12f) *
                directionSign;
            float authority = Mathf.Min(
                3f,
                requestedMaximum * 0.08f);
            float contactWeight = Mathf.Clamp01(
                steerableGroundedCount / 2f);
            requestedAssist = Mathf.Clamp(
                requestedAssist,
                -authority,
                authority) *
                neutralInputWeight *
                groundControlWeight *
                contactWeight;

            straightLineAssistAngle = ResolveSafeSteerAngle(
                requestedAssist / requestedMaximum,
                forwardSpeed,
                up,
                gravityMagnitude);
            return straightLineAssistAngle;
        }

        private float ResolveAckermannAngle(
            ModularWheelRuntime wheel,
            float centerSteerAngle)
        {
            if (wheel == null ||
                wheel.EffectiveRole !=
                WheelRoleOverride.SteerDrive ||
                Mathf.Abs(centerSteerAngle) < 0.001f)
            {
                return 0f;
            }
            float sign = Mathf.Sign(centerSteerAngle);
            float centerRadians =
                Mathf.Abs(centerSteerAngle) * Mathf.Deg2Rad;
            float turnRadius =
                wheelbase /
                Mathf.Max(0.001f, Mathf.Tan(centerRadians));
            float lateralPosition =
                LocalPositionFromCenterOfMass(wheel).x -
                lateralCenter;
            float wheelRadiusToTurnCenter = Mathf.Max(
                0.25f,
                turnRadius - sign * lateralPosition);
            float result = sign *
                Mathf.Atan(wheelbase / wheelRadiusToTurnCenter) *
                Mathf.Rad2Deg;
            return Mathf.Clamp(
                result,
                -wheel.Profile.maximumSteerAngle,
                wheel.Profile.maximumSteerAngle);
        }

        private Vector3 LocalPositionFromCenterOfMass(
            ModularWheelRuntime wheel)
        {
            if (wheel == null || vehicleRoot == null || body == null)
                return Vector3.zero;
            return vehicleRoot.InverseTransformPoint(
                       wheel.NeutralWheelCenterWorld) -
                   body.centerOfMass;
        }

        private void UpdateModuleActuationShares()
        {
            foreach (IGrouping<Transform, ModularWheelRuntime> group
                     in wheels
                         .Where(item => item != null)
                         .GroupBy(item => item.ModuleRoot))
            {
                ModularWheelRuntime[] moduleTyres =
                    group.ToArray();
                if (moduleTyres.Length == 1)
                {
                    moduleTyres[0].SetModuleActuationShare(1f);
                    continue;
                }

                float supportedLoad = moduleTyres
                    .Where(item => item.IsGrounded)
                    .Sum(item => Mathf.Max(0f, item.NormalForce));
                if (supportedLoad > 0.001f)
                {
                    foreach (ModularWheelRuntime tyre in moduleTyres)
                    {
                        float share = tyre.IsGrounded
                            ? Mathf.Max(0f, tyre.NormalForce) /
                              supportedLoad
                            : 0f;
                        tyre.SetModuleActuationShare(share);
                    }
                    continue;
                }

                float equalShare = 1f / moduleTyres.Length;
                foreach (ModularWheelRuntime tyre in moduleTyres)
                    tyre.SetModuleActuationShare(equalShare);
            }
        }

        private static bool TrySolvePositiveWeights(
            Vector2[] positions,
            bool[] active,
            float[] weights)
        {
            Array.Clear(weights, 0, weights.Length);
            float count = 0f;
            float sumX = 0f;
            float sumZ = 0f;
            float sumXX = 0f;
            float sumXZ = 0f;
            float sumZZ = 0f;
            for (int index = 0; index < positions.Length; index++)
            {
                if (!active[index])
                    continue;
                Vector2 point = positions[index];
                count += 1f;
                sumX += point.x;
                sumZ += point.y;
                sumXX += point.x * point.x;
                sumXZ += point.x * point.y;
                sumZZ += point.y * point.y;
            }
            if (count < 1f)
                return false;

            float scale = Mathf.Max(
                1f,
                sumXX + sumZZ);
            float regularization = scale * 0.000001f;
            if (!SolveSymmetric3x3(
                    count,
                    sumX,
                    sumZ,
                    sumXX + regularization,
                    sumXZ,
                    sumZZ + regularization,
                    out Vector3 lambda))
            {
                return false;
            }
            for (int index = 0; index < positions.Length; index++)
            {
                if (!active[index])
                    continue;
                weights[index] =
                    lambda.x +
                    lambda.y * positions[index].x +
                    lambda.z * positions[index].y;
            }
            return true;
        }

        private static bool SolveSymmetric3x3(
            float m00,
            float m01,
            float m02,
            float m11,
            float m12,
            float m22,
            out Vector3 solution)
        {
            float c00 = m11 * m22 - m12 * m12;
            float c01 = m02 * m12 - m01 * m22;
            float c02 = m01 * m12 - m02 * m11;
            float determinant =
                m00 * c00 +
                m01 * c01 +
                m02 * c02;
            if (Mathf.Abs(determinant) < 0.00000001f)
            {
                solution = Vector3.zero;
                return false;
            }
            // The right-hand side is (1, 0, 0), so the first column of
            // the inverse is the complete solution.
            solution = new Vector3(c00, c01, c02) / determinant;
            return true;
        }
    }
}
