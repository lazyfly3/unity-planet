using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlanet.ModularAssembly
{
    public enum WheelRoleOverride
    {
        Auto,
        SteerDrive,
        DriveOnly,
        FreeRolling
    }

    public static class WheelRoleSettings
    {
        private const string Prefix = "wheelRole=";

        public static string Serialize(WheelRoleOverride value) =>
            Prefix + value;

        public static WheelRoleOverride Parse(string settings)
        {
            if (string.IsNullOrEmpty(settings))
                return WheelRoleOverride.Auto;
            int start = settings.IndexOf(
                Prefix,
                StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return WheelRoleOverride.Auto;
            start += Prefix.Length;
            int end = settings.IndexOf(';', start);
            string value = end >= 0
                ? settings.Substring(start, end - start)
                : settings.Substring(start);
            return Enum.TryParse(
                value,
                true,
                out WheelRoleOverride parsed)
                ? parsed
                : WheelRoleOverride.Auto;
        }
    }

    [Serializable]
    public sealed class WheelModuleProfile
    {
        public string neoXId;
        public float radius = 0.5f;
        public float width = 0.42f;
        public float massKg = 60f;
        public float maximumSteerAngle = 35f;
        public float maximumSpeed = 35f;
        public float suspensionTravel = 0.24f;
        public float targetCompression = 0.5f;
        public float suspensionDampingRatio = 0.9f;
        public float loadCapacityKg = 850f;
        public float maximumNormalLoad = 5f;
        public float maximumMotorTorque = 950f;
        public float maximumMotorPower = 45000f;
        public float maximumBrakeTorque = 1800f;
        public float longitudinalFriction = 1.15f;
        public float lateralFriction = 1.25f;
        public float longitudinalSlipStiffness = 5.5f;
        public float lateralSlipStiffness = 6f;
        public float rollingResistance = 0.016f;
        public bool racingFront;
        public bool racingRear;

        private static readonly string[] WheelIds =
        {
            "wheel_basic_111",
            "wheel_m_222",
            "wheel_l_422",
            "speedwheel_small_l_322",
            "speedwheel_small_r_322",
            "speedwheel_large_l_522",
            "speedwheel_large_r_522"
        };

        public static bool IsWheelModuleId(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   WheelIds.Any(id =>
                       value.IndexOf(
                           id,
                           StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static WheelModuleProfile ForNeoXId(string value)
        {
            string id = value ?? string.Empty;
            var result = new WheelModuleProfile { neoXId = id };
            if (Contains(id, "wheel_m_222"))
            {
                result.radius = 1f;
                result.width = 0.72f;
                result.massKg = 120f;
                result.maximumSteerAngle = 32f;
                result.maximumSpeed = 45f;
                result.loadCapacityKg = 1800f;
                result.maximumMotorTorque = 2200f;
                result.maximumMotorPower = 90000f;
                result.maximumBrakeTorque = 4200f;
            }
            else if (Contains(id, "wheel_l_422"))
            {
                result.radius = 1.35f;
                result.width = 0.95f;
                result.massKg = 220f;
                result.maximumSteerAngle = 26f;
                result.maximumSpeed = 40f;
                result.loadCapacityKg = 3200f;
                result.maximumMotorTorque = 4400f;
                result.maximumMotorPower = 160000f;
                result.maximumBrakeTorque = 7600f;
                result.longitudinalFriction = 1.1f;
                result.lateralFriction = 1.2f;
            }
            else if (Contains(id, "speedwheel_small"))
            {
                result.radius = 1.02f;
                result.width = 0.76f;
                result.massKg = 100f;
                result.maximumSteerAngle = 28f;
                result.maximumSpeed = 65f;
                result.loadCapacityKg = 1400f;
                result.maximumMotorTorque = 2600f;
                result.maximumMotorPower = 180000f;
                result.maximumBrakeTorque = 4800f;
                result.longitudinalFriction = 1.28f;
                result.lateralFriction = 1.38f;
                result.racingFront = true;
            }
            else if (Contains(id, "speedwheel_large"))
            {
                result.radius = 1.34f;
                result.width = 1f;
                result.massKg = 180f;
                result.maximumSteerAngle = 0f;
                result.maximumSpeed = 65f;
                result.loadCapacityKg = 2600f;
                result.maximumMotorTorque = 5200f;
                result.maximumMotorPower = 280000f;
                result.maximumBrakeTorque = 8500f;
                result.longitudinalFriction = 1.3f;
                result.lateralFriction = 1.4f;
                result.racingRear = true;
            }
            result.suspensionTravel = Mathf.Clamp(
                result.radius * 0.25f,
                0.2f,
                0.65f);
            return result;
        }

        private static bool Contains(string value, string term) =>
            value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [Serializable]
    public struct WheelContactTelemetry
    {
        public bool grounded;
        public int validSamples;
        public int patchSamples;
        public Vector3 point;
        public Vector3 normal;
        public float droop;
        public float visualDroop;
        public float compression;
        public float penetrationDepth;
        public float suspensionSpeed;
        public float sprungMass;
        public float springRate;
        public float normalForce;
        public float longitudinalSpeed;
        public float lateralSpeed;
        public float longitudinalForce;
        public float lateralForce;
        public float slipRatio;
        public float slipAngleDegrees;
        public float wheelAngularSpeed;
        public float surfaceGrip;
        public bool obstacleContact;
        public Vector3 obstaclePoint;
        public Vector3 obstacleNormal;
        public float obstacleClearance;
        public float obstacleForce;
    }

    public sealed class ModularWheelRuntime : MonoBehaviour
    {
        private struct VolumeContact
        {
            public Collider collider;
            public Vector3 point;
            public Vector3 normal;
            public float droop;

            public bool valid => collider != null;
        }

        private const float MinimumGroundNormalDot = 0.28f;
        // The construction limit is 1024 modules. Keep enough query capacity
        // that a dense self-collider hierarchy cannot consume every slot and
        // hide the terrain hit before ValidNonSelf filters the results.
        private const int MaximumQueryResults = 4096;
        private const int IgnoreRaycastLayer = 2;
        private static readonly float[] LongitudinalSamples =
            { -0.68f, 0f, 0.68f };
        private static readonly float[] WidthSamples =
            { -0.48f, 0f, 0.48f };
        // The volume shell is deliberately denser than the 3x3 load patch.
        // Twenty-five tangent slices plus the conservative radial bias below
        // bound the largest supported tyre to within a few millimetres instead
        // of leaving the large gaps produced by the former nine-slice shell.
        private const int VolumeLongitudinalSampleCount = 25;
        // A complete shell is required for final-force sweeps: grounded tyres
        // can still be driven upward into a ceiling by a late RCS/explosion.
        private const int ObstacleCircumferenceSampleCount = 64;

        private RaycastHit[] hits = new RaycastHit[32];
        private Collider[] overlaps = new Collider[32];
        private readonly RaycastHit[] contactSamples =
            new RaycastHit[LongitudinalSamples.Length * WidthSamples.Length];
        private readonly float[] sampleDroops =
            new float[LongitudinalSamples.Length * WidthSamples.Length];
        private readonly bool[] validSamples =
            new bool[LongitudinalSamples.Length * WidthSamples.Length];

        private Collider[] placementColliders = Array.Empty<Collider>();
        private bool[] placementColliderStates = Array.Empty<bool>();
        private Rigidbody body;
        private Rigidbody groundBody;
        private Rigidbody obstacleBody;
        private Collider contactCollider;
        private Collider obstacleCollider;
        private CapsuleCollider penetrationProbe;
        private GameObject penetrationProbeObject;
        private Transform vehicleRoot;
        private Transform moduleRoot;
        private Transform boundVisual;
        private Transform suspensionPivot;
        private Transform steeringPivot;
        private Transform[] rollingVisuals = Array.Empty<Transform>();
        private ModularWheelRuntime[] moduleTyreElements =
            Array.Empty<ModularWheelRuntime>();
        private Quaternion[] rollingVisualBaseRotations =
            Array.Empty<Quaternion>();
        private Vector3[] rollingVisualAxes = Array.Empty<Vector3>();
        private int[] rollingVisualTyreIndices = Array.Empty<int>();
        private WheelRoleOverride configuredRole;
        private WheelRoleOverride automaticRole;
        private Vector3 axleLocal = Vector3.right;
        private Vector3 rollingForwardLocal = Vector3.forward;
        private Vector3 baseAxleWorld = Vector3.right;
        private Vector3 baseForwardWorld = Vector3.forward;
        private Vector3 suspensionUpWorld = Vector3.up;
        private Vector3 environmentUpWorld = Vector3.up;
        private Vector3 contactPoint;
        private Vector3 contactNormal = Vector3.up;
        private Vector3 groundVelocity;
        private Vector3 obstaclePoint;
        private Vector3 obstacleNormal;
        private Vector3 obstacleGroundVelocity;
        private Vector3 antiRollForceWorld;
        private Vector3 previousWheelCenterWorld;
        private Vector3 probeAnchorWorld;
        private bool grounded;
        private bool obstacleContact;
        private bool contactForcesAppliedThisStep;
        private bool sprungMassAssigned;
        private bool hadPreviousContact;
        private bool hasPreviousWheelCenter;
        private float compression;
        private float suspensionDroop;
        private float penetrationDepth;
        private float suspensionSpeed;
        private float steerAngle;
        private float wheelAngularSpeed;
        private float assignedSprungMass;
        private float normalForce;
        private float normalConstraintForce;
        private float obstacleConstraintForce;
        private float springRate;
        private float surfaceGrip = 1f;
        private float obstacleClearance;
        private float obstacleSweepDistance;
        private float visualAxleSign = 1f;
        private Vector3 tyreCenterLocal;
        private int tyreElementIndex;
        private int tyreElementCount = 1;
        private float moduleActuationShare = 1f;
        private bool hasAuthoredTyreGeometry;
        private WheelContactTelemetry telemetry;

        public WheelModuleProfile Profile { get; private set; } =
            WheelModuleProfile.ForNeoXId(string.Empty);
        public bool IsGrounded => grounded;
        public float Compression => compression;
        public float SuspensionDroop => suspensionDroop;
        public float SuspensionSpeed => suspensionSpeed;
        public float AssignedSprungMass => assignedSprungMass;
        public float NormalForce => normalForce;
        public float SteerAngle => steerAngle;
        public float WheelAngularSpeed => wheelAngularSpeed;
        public float CurrentVisualDroop => ResolveVisualDroop();
        public Vector3 ContactPoint =>
            grounded ? contactPoint : NeutralWheelCenterWorld;
        public Vector3 ContactNormal =>
            grounded ? contactNormal : Vector3.up;
        public Vector3 AnchorWorld => NeutralWheelCenterWorld;
        public Vector3 NeutralWheelCenterWorld =>
            (moduleRoot != null ? moduleRoot : transform)
            .TransformPoint(tyreCenterLocal);
        public Vector3 TyreCenterLocal => tyreCenterLocal;
        public Transform ModuleRoot =>
            moduleRoot != null ? moduleRoot : transform;
        public Transform VisualMotionRoot =>
            steeringPivot != null ? steeringPivot : ModuleRoot;
        public int TyreElementIndex => tyreElementIndex;
        public int TyreElementCount => tyreElementCount;
        public float ModuleActuationShare => moduleActuationShare;
        public int RollingVisualCount => rollingVisuals.Length;
        public bool HasAuthoredTyreGeometry =>
            hasAuthoredTyreGeometry;
        public Vector3 BaseForwardWorld => baseForwardWorld;
        public Vector3 BaseAxleWorld => baseAxleWorld;
        public Vector3 SuspensionUpWorld => suspensionUpWorld;
        public Collider ContactCollider => contactCollider;
        public Rigidbody GroundBody => groundBody;
        public WheelContactTelemetry Telemetry => telemetry;
        public WheelRoleOverride ConfiguredRole => configuredRole;
        public WheelRoleOverride EffectiveRole =>
            configuredRole == WheelRoleOverride.Auto
                ? automaticRole
                : configuredRole;

        public void SetSprungMass(float value)
        {
            assignedSprungMass = Mathf.Max(0f, value);
            sprungMassAssigned = true;
        }

        public void Configure(
            ModularContentRecord record,
            string behaviorSettings)
        {
            ConfigureTyreElement(
                record,
                behaviorSettings,
                0,
                transform);
        }

        public void ConfigureTyreElement(
            ModularContentRecord record,
            string behaviorSettings,
            int elementIndex,
            Transform owningModuleRoot)
        {
            Profile = WheelModuleProfile.ForNeoXId(record?.neoXId);
            configuredRole = WheelRoleSettings.Parse(behaviorSettings);
            moduleRoot = owningModuleRoot != null
                ? owningModuleRoot
                : transform;
            tyreElementIndex = 0;
            tyreElementCount = 1;
            tyreCenterLocal = Vector3.zero;
            moduleActuationShare = 1f;
            hasAuthoredTyreGeometry = false;
            moduleTyreElements =
                Array.Empty<ModularWheelRuntime>();
            if (WheelModuleGeometryCatalog.TryResolve(
                    record?.neoXId,
                    out WheelTyreGeometry[] tyres) &&
                tyres.Length > 0)
            {
                tyreElementIndex = Mathf.Clamp(
                    elementIndex,
                    0,
                    tyres.Length - 1);
                tyreElementCount = tyres.Length;
                WheelTyreGeometry geometry =
                    tyres[tyreElementIndex];
                tyreCenterLocal = geometry.centerLocal;
                Profile.radius = geometry.radius;
                Profile.width = geometry.width;
                Profile.massKg /= tyreElementCount;
                Profile.loadCapacityKg /= tyreElementCount;
                moduleActuationShare =
                    1f / tyreElementCount;
                hasAuthoredTyreGeometry = true;
            }
            axleLocal = ReadAxis(
                record?.wheelAxleLocal,
                Vector3.right);
            rollingForwardLocal = ReadAxis(
                record?.wheelRollingForwardLocal,
                Vector3.forward);
            if (boundVisual != null &&
                !hasAuthoredTyreGeometry)
                CalibrateProfileToVisualEnvelope(boundVisual);
            CapturePlacementColliders();
            if (rollingVisuals.Length > 0)
            {
                rollingVisualTyreIndices = rollingVisuals
                    .Select(ResolveRollingVisualTyreIndex)
                    .ToArray();
                RefreshModuleTyreElements();
            }
            RefreshRollingVisualAxes();
        }

        public void SetModuleActuationShare(float value)
        {
            moduleActuationShare = Mathf.Clamp01(value);
        }

        public void BindVisual(Transform visual)
        {
            if (visual == null)
                return;
            boundVisual = visual;
            suspensionPivot = suspensionPivot != null
                ? suspensionPivot
                : CreatePivot("WheelSuspension", transform);
            steeringPivot = steeringPivot != null
                ? steeringPivot
                : CreatePivot("WheelSteering", suspensionPivot);
            visual.SetParent(steeringPivot, false);
            if (!hasAuthoredTyreGeometry)
                CalibrateProfileToVisualEnvelope(visual);

            rollingVisuals = visual
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item != visual && IsRollingVisualName(item.name))
                .OrderBy(item => RollingVisualNameOrder(item.name))
                .ToArray();
            rollingVisualBaseRotations = rollingVisuals
                .Select(item => item.localRotation)
                .ToArray();
            rollingVisualTyreIndices = rollingVisuals
                .Select(ResolveRollingVisualTyreIndex)
                .ToArray();
            RefreshModuleTyreElements();
            RefreshRollingVisualAxes();
        }

        public void BindVehicle(Rigidbody targetBody, Transform root)
        {
            body = targetBody;
            vehicleRoot = root;
            CapturePlacementColliders();
        }

        public void SetAutomaticRole(WheelRoleOverride role)
        {
            automaticRole = role;
        }

        public void SetFlightMode(bool value)
        {
            CapturePlacementColliders();
            for (int index = 0; index < placementColliders.Length; index++)
            {
                if (placementColliders[index] != null)
                {
                    placementColliders[index].enabled =
                        value ? false : placementColliderStates[index];
                }
            }
            if (!value)
            {
                grounded = false;
                obstacleContact = false;
                normalConstraintForce = 0f;
                obstacleConstraintForce = 0f;
                hadPreviousContact = false;
                hasPreviousWheelCenter = false;
                compression = 0f;
                suspensionDroop = Profile.suspensionTravel;
                penetrationDepth = 0f;
                suspensionSpeed = 0f;
                normalForce = 0f;
                steerAngle = 0f;
                wheelAngularSpeed = 0f;
                telemetry = default;
            }
        }

        public bool ProbeContact(Vector3 up)
        {
            return ProbeContact(up, Time.fixedDeltaTime, null);
        }

        public bool ProbeContact(Vector3 up, float fixedStep)
        {
            return ProbeContact(up, fixedStep, null);
        }

        public bool ProbeContact(
            Vector3 up,
            float fixedStep,
            VehicleForceLedger forceLedger)
        {
            bool previousWasGrounded = hadPreviousContact;
            float previousDroop = suspensionDroop;
            Rigidbody previousGroundBody = groundBody;
            grounded = false;
            groundBody = null;
            contactCollider = null;
            normalForce = 0f;
            normalConstraintForce = 0f;
            obstacleConstraintForce = 0f;
            springRate = 0f;
            suspensionSpeed = 0f;
            penetrationDepth = 0f;
            telemetry.longitudinalForce = 0f;
            telemetry.lateralForce = 0f;
            telemetry.normalForce = 0f;
            contactForcesAppliedThisStep = false;
            antiRollForceWorld = Vector3.zero;
            ResetObstacleContact();

            if (body == null)
                return false;
            if (up.sqrMagnitude < 0.0001f)
                up = Vector3.up;
            else
                up.Normalize();

            ResolveBaseFrame(up);
            Vector3 neutralWheelCenter =
                NeutralWheelCenterWorld;
            probeAnchorWorld = neutralWheelCenter;
            Array.Clear(validSamples, 0, validSamples.Length);

            float step = Mathf.Max(0.0001f, fixedStep);
            float travel = Mathf.Max(0.05f, Profile.suspensionTravel);
            float radius = Mathf.Max(0.12f, Profile.radius);
            float skin = Mathf.Max(0.025f, radius * 0.035f);
            Vector3 predictedAnchorVelocity = forceLedger != null
                ? PredictedBodyPointVelocity(
                    neutralWheelCenter,
                    forceLedger,
                    step)
                : body.GetPointVelocity(neutralWheelCenter);
            float downwardSpeed = Mathf.Max(
                0f,
                -Vector3.Dot(
                    predictedAnchorVelocity,
                    suspensionUpWorld));
            float recoveryHeight = radius + travel;
            float predictiveDrop = downwardSpeed * step * 1.5f;
            PhysicsScene physicsScene =
                body.gameObject.scene.GetPhysicsScene();
            int queryLayerMask = ResolveQueryLayerMask();

            float minimumDroop = float.PositiveInfinity;
            int minimumIndex = -1;
            int validCount = 0;
            float maximumRecoveryPenetration =
                Mathf.Max(0.06f, radius * 0.22f);
            int sampleIndex = 0;
            for (int longitudinalIndex = 0;
                 longitudinalIndex < LongitudinalSamples.Length;
                 longitudinalIndex++)
            {
                float longitudinalOffset =
                    LongitudinalSamples[longitudinalIndex] * radius;
                float circleDepth = Mathf.Sqrt(Mathf.Max(
                    0f,
                    radius * radius -
                    longitudinalOffset * longitudinalOffset));
                for (int widthIndex = 0;
                     widthIndex < WidthSamples.Length;
                     widthIndex++, sampleIndex++)
                {
                    float widthOffset =
                        WidthSamples[widthIndex] * Profile.width;
                    Vector3 origin =
                        neutralWheelCenter +
                        baseForwardWorld * longitudinalOffset +
                        baseAxleWorld * widthOffset +
                        suspensionUpWorld *
                        (skin + recoveryHeight);
                    float maximumDistance =
                        skin + recoveryHeight + travel +
                        circleDepth + radius * 0.25f +
                        predictiveDrop;
                    int hitCount = RaycastBuffered(
                        physicsScene,
                        origin,
                        -suspensionUpWorld,
                        maximumDistance,
                        queryLayerMask,
                        QueryTriggerInteraction.Ignore);
                    float nearestDistance = float.PositiveInfinity;
                    RaycastHit nearest = default;
                    for (int hitIndex = 0;
                         hitIndex < hitCount;
                         hitIndex++)
                    {
                        RaycastHit candidate = hits[hitIndex];
                        if (!ValidContact(
                                candidate,
                                environmentUpWorld,
                                suspensionUpWorld))
                            continue;
                        float candidateDroop =
                            candidate.distance -
                            (skin + recoveryHeight) -
                            circleDepth;
                        // A top face far above the axle is a ceiling, not a
                        // deeply penetrated road surface.
                        if (candidateDroop <
                            -maximumRecoveryPenetration)
                        {
                            continue;
                        }
                        if (candidate.distance < nearestDistance)
                        {
                            nearestDistance = candidate.distance;
                            nearest = candidate;
                        }
                    }
                    if (float.IsPositiveInfinity(nearestDistance))
                        continue;

                    float droop =
                        nearestDistance -
                        (skin + recoveryHeight) -
                        circleDepth;
                    validSamples[sampleIndex] = true;
                    contactSamples[sampleIndex] = nearest;
                    sampleDroops[sampleIndex] = droop;
                    validCount++;
                    if (droop < minimumDroop)
                    {
                        minimumDroop = droop;
                        minimumIndex = sampleIndex;
                    }
                }
            }

            RaycastHit volumeContact;
            float volumeDroop;
            bool hasVolumeContact = ProbeWheelVolume(
                physicsScene,
                queryLayerMask,
                radius,
                travel,
                predictiveDrop,
                maximumRecoveryPenetration,
                out volumeContact,
                out volumeDroop);
            if (hasVolumeContact &&
                (minimumIndex < 0 || volumeDroop < minimumDroop))
            {
                minimumDroop = volumeDroop;
                minimumIndex = -2;
            }
            VolumeContact overlapContact = ProbeWheelOverlap(
                physicsScene,
                queryLayerMask,
                radius,
                travel);
            if (overlapContact.valid &&
                (minimumIndex == -1 ||
                 overlapContact.droop < minimumDroop))
            {
                minimumDroop = overlapContact.droop;
                minimumIndex = -3;
            }

            if (minimumIndex == -1)
            {
                // Use the last known wheel centre only after the ground probe
                // has failed. This avoids starting a sphere sweep overlapped
                // with the road when an unloaded suspension is fully extended.
                ProbeObstacle(
                    physicsScene,
                    step,
                    radius,
                    travel,
                    forceLedger);
                hadPreviousContact = false;
                compression = 0f;
                suspensionDroop = travel;
                previousWheelCenterWorld =
                    neutralWheelCenter -
                    suspensionUpWorld * travel;
                hasPreviousWheelCenter = true;
                telemetry = new WheelContactTelemetry
                {
                    grounded = false,
                    droop = travel,
                    visualDroop = travel,
                    sprungMass = assignedSprungMass,
                    wheelAngularSpeed = wheelAngularSpeed,
                    obstacleContact = obstacleContact,
                    obstaclePoint = obstaclePoint,
                    obstacleNormal = obstacleNormal,
                    obstacleClearance = obstacleClearance
                };
                return false;
            }

            float patchTolerance = Mathf.Max(0.035f, radius * 0.12f);
            Collider primaryCollider;
            Vector3 primaryPoint;
            Vector3 primaryNormal;
            if (minimumIndex >= 0)
            {
                RaycastHit primary = contactSamples[minimumIndex];
                primaryCollider = primary.collider;
                primaryPoint = primary.point;
                primaryNormal = primary.normal;
            }
            else if (minimumIndex == -2)
            {
                primaryCollider = volumeContact.collider;
                primaryPoint = volumeContact.point;
                primaryNormal = volumeContact.normal;
            }
            else
            {
                primaryCollider = overlapContact.collider;
                primaryPoint = overlapContact.point;
                primaryNormal = overlapContact.normal;
            }
            Rigidbody primaryBody = primaryCollider != null
                ? primaryCollider.attachedRigidbody
                : null;
            float totalWeight = 0f;
            Vector3 pointSum = Vector3.zero;
            Vector3 normalSum = Vector3.zero;
            float gripSum = 0f;
            int patchCount = 0;
            for (int index = 0; index < validSamples.Length; index++)
            {
                if (!validSamples[index] ||
                    sampleDroops[index] >
                    minimumDroop + patchTolerance ||
                    !SameContactManifold(
                        primaryCollider,
                        primaryBody,
                        contactSamples[index].collider))
                {
                    continue;
                }
                float weight = 1f /
                    (0.025f +
                     Mathf.Max(0f, sampleDroops[index] - minimumDroop));
                pointSum += contactSamples[index].point * weight;
                normalSum += contactSamples[index].normal * weight;
                gripSum += ResolveSurfaceGrip(
                    contactSamples[index].collider) * weight;
                totalWeight += weight;
                patchCount++;
            }

            contactPoint = totalWeight > 0.0001f
                ? pointSum / totalWeight
                : primaryPoint;
            contactNormal = normalSum.sqrMagnitude > 0.0001f
                ? normalSum.normalized
                : primaryNormal.normalized;
            if (Vector3.Dot(contactNormal, up) < 0f)
                contactNormal = -contactNormal;
            contactCollider = primaryCollider;
            groundBody = contactCollider != null
                ? contactCollider.attachedRigidbody
                : null;
            groundVelocity =
                groundBody != null && groundBody != body
                    ? groundBody.GetPointVelocity(contactPoint)
                    : Vector3.zero;
            Vector3 relativeVelocity =
                body.GetPointVelocity(contactPoint) -
                groundVelocity;
            grounded = true;
            hadPreviousContact = true;
            suspensionDroop = minimumDroop;
            compression = Mathf.Clamp01(
                1f - suspensionDroop / travel);
            penetrationDepth = Mathf.Max(
                0f,
                -suspensionDroop -
                ResolveConservativeEnvelopeBias(radius));
            bool withinSuspensionReach =
                suspensionDroop <= travel + 0.001f;
            suspensionSpeed = !withinSuspensionReach
                ? 0f
                : previousWasGrounded &&
                  previousGroundBody == groundBody
                    ? Mathf.Clamp(
                        (previousDroop - suspensionDroop) / step,
                        -80f,
                        80f)
                    : Mathf.Clamp(
                        -Vector3.Dot(
                            relativeVelocity,
                            suspensionUpWorld),
                        -80f,
                        80f);
            surfaceGrip = totalWeight > 0.0001f
                ? gripSum / totalWeight
                : ResolveSurfaceGrip(contactCollider);
            if (patchCount == 0)
                patchCount = 1;
            // The resolved droop gives the real tyre centre. Sweeping before
            // this point can place the sphere inside flat ground and turn the
            // overlap normal into a false vertical-obstacle response.
            ProbeObstacle(
                physicsScene,
                step,
                radius,
                travel,
                forceLedger);
            previousWheelCenterWorld =
                neutralWheelCenter -
                suspensionUpWorld * suspensionDroop;
            hasPreviousWheelCenter = true;
            telemetry = new WheelContactTelemetry
            {
                grounded = true,
                validSamples = validCount,
                patchSamples = patchCount,
                point = contactPoint,
                normal = contactNormal,
                droop = suspensionDroop,
                visualDroop = suspensionDroop,
                compression = compression,
                penetrationDepth = penetrationDepth,
                suspensionSpeed = suspensionSpeed,
                sprungMass = assignedSprungMass,
                wheelAngularSpeed = wheelAngularSpeed,
                surfaceGrip = surfaceGrip,
                obstacleContact = obstacleContact,
                obstaclePoint = obstaclePoint,
                obstacleNormal = obstacleNormal,
                obstacleClearance = obstacleClearance
            };
            return true;
        }

        public float PrepareSuspension(
            Vector3 up,
            float gravityMagnitude,
            float fixedStep)
        {
            normalForce = 0f;
            springRate = 0f;
            if (!grounded || body == null)
                return 0f;

            float step = Mathf.Max(0.0001f, fixedStep);
            float gravityValue = Mathf.Max(0.1f, gravityMagnitude);
            float loadMass = sprungMassAssigned
                ? Mathf.Max(0.1f, assignedSprungMass)
                : body.mass;
            float tunedMass = Mathf.Min(
                loadMass,
                Mathf.Max(1f, Profile.loadCapacityKg));
            float travel = Mathf.Max(0.05f, Profile.suspensionTravel);
            float targetCompression = Mathf.Clamp(
                Profile.targetCompression,
                0.3f,
                0.75f);
            springRate = tunedMass * gravityValue /
                         (travel * targetCompression);

            // Keep the explicit spring integration safely above five physics
            // steps per natural time scale. Heavy overloads therefore bottom
            // out instead of creating an unstable numerical spring.
            float stableSpringLimit =
                loadMass / (25f * step * step);
            springRate = Mathf.Min(springRate, stableSpringLimit);
            float damping = 2f *
                Mathf.Clamp(Profile.suspensionDampingRatio, 0.5f, 1.15f) *
                Mathf.Sqrt(Mathf.Max(0.01f, springRate * loadMass));

            float compressionDistance = compression * travel;
            float springForce = springRate * compressionDistance;
            float dampingForce = damping * suspensionSpeed;
            float bumpStart = 0.82f;
            float bumpRatio = Mathf.Clamp01(
                (compression - bumpStart) /
                Mathf.Max(0.01f, 1f - bumpStart));
            float bumpForce =
                springRate * travel * 3f * bumpRatio * bumpRatio;
            float maximumForce =
                loadMass * gravityValue *
                Mathf.Max(1f, Profile.maximumNormalLoad);
            normalForce = Mathf.Clamp(
                springForce + dampingForce + bumpForce,
                0f,
                maximumForce);

            telemetry.suspensionSpeed = suspensionSpeed;
            telemetry.springRate = springRate;
            telemetry.normalForce = normalForce;
            telemetry.sprungMass = loadMass;
            return normalForce;
        }

        public void AdjustNormalForce(float delta)
        {
            if (!grounded)
                return;
            normalForce = Mathf.Max(0f, normalForce + delta);
            telemetry.normalForce = normalForce;
        }

        public void SetAntiRollForce(Vector3 value)
        {
            antiRollForceWorld = grounded &&
                                 VehicleWholeBodyAudit.Finite(value)
                ? value
                : Vector3.zero;
        }

        public void ApplyForces(
            Vector3 up,
            float throttle,
            float targetSteerAngle,
            bool braking,
            bool boosting,
            float driveScale,
            float fixedStep,
            VehicleForceLedger forceLedger)
        {
            float step = Mathf.Max(0.0001f, fixedStep);
            WheelRoleOverride role = EffectiveRole;
            bool steers = role == WheelRoleOverride.SteerDrive &&
                          Profile.maximumSteerAngle > 0.01f;
            bool drives = role == WheelRoleOverride.SteerDrive ||
                          role == WheelRoleOverride.DriveOnly;
            float steerTarget = steers
                ? Mathf.Clamp(
                    targetSteerAngle,
                    -Profile.maximumSteerAngle,
                    Profile.maximumSteerAngle)
                : 0f;
            steerAngle = Mathf.MoveTowards(
                steerAngle,
                steerTarget,
                (Mathf.Abs(steerTarget) < Mathf.Abs(steerAngle)
                    ? 320f
                    : 125f) * step);

            if (body == null)
                return;
            Vector3 obstacleForce =
                ResolveObstacleConstraintForce(step, forceLedger);
            if (obstacleForce.sqrMagnitude > 0f &&
                forceLedger != null)
            {
                forceLedger.AddForceAtPosition(
                    obstacleForce,
                    obstaclePoint);
                ApplyReaction(
                    obstacleBody,
                    -obstacleForce * step,
                    obstaclePoint,
                    forceLedger);
            }

            if (!grounded)
            {
                UpdateFreeWheel(
                    drives,
                    throttle,
                    braking,
                    boosting,
                    driveScale,
                    step);
                return;
            }

            float baseNormalForce = normalForce;
            float groundConstraintDelta = ResolveGroundConstraintForce(
                step,
                forceLedger,
                false);
            normalForce += groundConstraintDelta;
            telemetry.normalForce = normalForce;
            Vector3 suspensionForce =
                contactNormal * normalForce +
                antiRollForceWorld;
            if (forceLedger != null)
            {
                forceLedger.AddForceAtPosition(
                    suspensionForce,
                    contactPoint);
                ApplyReaction(
                    groundBody,
                    -suspensionForce * step,
                    contactPoint,
                    forceLedger);
                contactForcesAppliedThisStep = true;
            }

            Vector3 steeringForward =
                Quaternion.AngleAxis(
                    steerAngle,
                    suspensionUpWorld) *
                baseForwardWorld;
            Vector3 forward = Vector3.ProjectOnPlane(
                steeringForward,
                contactNormal);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(
                    vehicleRoot != null
                        ? vehicleRoot.forward
                        : transform.forward,
                    contactNormal);
            forward.Normalize();
            Vector3 right =
                Vector3.Cross(contactNormal, forward).normalized;

            Vector3 relativeVelocity =
                body.GetPointVelocity(contactPoint) -
                groundVelocity;
            Vector3 predictedRelativeVelocity =
                PredictedBodyPointVelocity(
                    contactPoint,
                    forceLedger,
                    step) -
                PredictedOtherPointVelocity(
                    groundBody,
                    contactPoint,
                    forceLedger,
                    groundVelocity);
            float longitudinalSpeed =
                Vector3.Dot(relativeVelocity, forward);
            float lateralSpeed =
                Vector3.Dot(relativeVelocity, right);
            float predictedLongitudinalSpeed =
                Vector3.Dot(predictedRelativeVelocity, forward);
            float predictedLateralSpeed =
                Vector3.Dot(predictedRelativeVelocity, right);

            float inertia = Mathf.Max(
                0.05f,
                0.5f * Profile.massKg *
                Profile.radius * Profile.radius);
            float driveInput = drives
                ? Mathf.Clamp(throttle * driveScale, -1f, 1f)
                : 0f;
            float driveDirection = Mathf.Sign(driveInput);
            float directionSpeed =
                driveDirection * wheelAngularSpeed * Profile.radius;
            float speedLimit = driveInput < 0f
                ? Profile.maximumSpeed * 0.58f
                : Profile.maximumSpeed;
            float speedTaper = driveDirection == 0f
                ? 0f
                : 1f - Mathf.InverseLerp(
                    speedLimit * 0.88f,
                    speedLimit,
                    directionSpeed);
            float availableTorque = Mathf.Min(
                Profile.maximumMotorTorque *
                moduleActuationShare,
                Profile.maximumMotorPower *
                moduleActuationShare /
                Mathf.Max(1f, Mathf.Abs(wheelAngularSpeed)));
            float previousSlip = telemetry.slipRatio;
            float tractionScale =
                Mathf.Sign(previousSlip) == driveDirection &&
                Mathf.Abs(previousSlip) > 0.16f
                    ? Mathf.Clamp(
                        0.16f / Mathf.Abs(previousSlip),
                        0.18f,
                        1f)
                    : 1f;
            float motorTorque =
                driveInput * availableTorque *
                Mathf.Clamp01(speedTaper) *
                tractionScale *
                (boosting ? 1.2f : 1f);

            float referenceSpin = Mathf.Abs(wheelAngularSpeed) > 0.15f
                ? wheelAngularSpeed
                : longitudinalSpeed /
                  Mathf.Max(0.05f, Profile.radius);
            float brakeTorque = 0f;
            if (braking && role != WheelRoleOverride.FreeRolling &&
                Mathf.Abs(referenceSpin) > 0.001f)
            {
                float stopTorque =
                    Mathf.Abs(referenceSpin) * inertia / step;
                brakeTorque =
                    -Mathf.Sign(referenceSpin) *
                    Mathf.Min(
                        Profile.maximumBrakeTorque *
                        moduleActuationShare,
                        stopTorque);
            }
            float bearingTorque = Mathf.Abs(wheelAngularSpeed) > 0.01f
                ? -Mathf.Sign(wheelAngularSpeed) *
                  normalForce * Profile.rollingResistance *
                  Profile.radius
                : 0f;
            float predictedAngularSpeed =
                wheelAngularSpeed +
                (motorTorque + brakeTorque + bearingTorque) /
                inertia * step;
            float wheelSurfaceSpeed =
                predictedAngularSpeed * Profile.radius;
            float slipDenominator = Mathf.Max(
                1.5f,
                Mathf.Abs(longitudinalSpeed),
                Mathf.Abs(wheelSurfaceSpeed));
            float slipRatio =
                (wheelSurfaceSpeed - longitudinalSpeed) /
                slipDenominator;
            float slipAngle = Mathf.Atan2(
                lateralSpeed,
                Mathf.Max(1f, Mathf.Abs(longitudinalSpeed)));

            float maximumLongitudinal =
                Mathf.Max(0f, normalForce) *
                surfaceGrip *
                Profile.longitudinalFriction;
            float maximumLateral =
                Mathf.Max(0f, normalForce) *
                surfaceGrip *
                Profile.lateralFriction;
            float suspensionReach =
                Mathf.Max(0.05f, Profile.suspensionTravel);
            float impactReferenceForce = Mathf.Max(
                1f,
                baseNormalForce +
                Mathf.Max(0.1f, assignedSprungMass) *
                9.81f * 2f);
            float impactTractionScale =
                suspensionDroop > suspensionReach + 0.001f
                    ? 0f
                    : 1f /
                      (1f +
                       normalConstraintForce /
                       impactReferenceForce);
            maximumLongitudinal *= impactTractionScale;
            maximumLateral *= impactTractionScale;
            float longitudinalForce = maximumLongitudinal > 0f
                ? maximumLongitudinal *
                  (float)Math.Tanh(
                      slipRatio *
                      Profile.longitudinalSlipStiffness)
                : 0f;
            float lateralForce = maximumLateral > 0f
                ? -maximumLateral *
                  (float)Math.Tanh(
                      slipAngle *
                      Profile.lateralSlipStiffness)
                : 0f;

            float slipVelocity =
                wheelSurfaceSpeed - longitudinalSpeed;
            float predictedSlipVelocity =
                wheelSurfaceSpeed - predictedLongitudinalSpeed;
            float combinedLongitudinalInverseMass =
                PairEffectiveInverseMass(
                    contactPoint,
                    forward,
                    groundBody) +
                Profile.radius * Profile.radius / inertia;
            if (longitudinalForce * slipVelocity > 0f)
            {
                if (longitudinalForce *
                    predictedSlipVelocity <= 0f)
                {
                    longitudinalForce = 0f;
                }
                else
                {
                    float noReverseForce =
                        Mathf.Abs(predictedSlipVelocity) /
                        Mathf.Max(
                            0.0001f,
                            combinedLongitudinalInverseMass * step) *
                        0.98f;
                    longitudinalForce = Mathf.Clamp(
                        longitudinalForce,
                        -noReverseForce,
                        noReverseForce);
                }
            }
            if (lateralForce * lateralSpeed < 0f)
            {
                if (lateralForce * predictedLateralSpeed >= 0f)
                {
                    lateralForce = 0f;
                }
                else
                {
                    float noReverseLateralForce =
                        Mathf.Abs(predictedLateralSpeed) /
                        Mathf.Max(
                            0.0001f,
                            PairEffectiveInverseMass(
                                contactPoint,
                                right,
                                groundBody) * step) *
                        0.98f;
                    lateralForce = Mathf.Clamp(
                        lateralForce,
                        -noReverseLateralForce,
                        noReverseLateralForce);
                }
            }

            float usage = 0f;
            if (maximumLongitudinal > 0.001f)
            {
                float normalized =
                    longitudinalForce / maximumLongitudinal;
                usage += normalized * normalized;
            }
            else
            {
                longitudinalForce = 0f;
            }
            if (maximumLateral > 0.001f)
            {
                float normalized = lateralForce / maximumLateral;
                usage += normalized * normalized;
            }
            else
            {
                lateralForce = 0f;
            }
            if (usage > 1f)
            {
                float scale = 1f / Mathf.Sqrt(usage);
                longitudinalForce *= scale;
                lateralForce *= scale;
            }

            wheelAngularSpeed =
                predictedAngularSpeed -
                longitudinalForce * Profile.radius /
                inertia * step;
            float maximumAngularSpeed =
                Profile.maximumSpeed * 2.2f /
                Mathf.Max(0.05f, Profile.radius) + 20f;
            wheelAngularSpeed = Mathf.Clamp(
                wheelAngularSpeed,
                -maximumAngularSpeed,
                maximumAngularSpeed);

            Vector3 tireForce =
                forward * longitudinalForce +
                right * lateralForce;
            if (forceLedger != null)
            {
                forceLedger.AddForceAtPosition(
                    tireForce,
                    contactPoint);
                ApplyReaction(
                    groundBody,
                    -tireForce * step,
                    contactPoint,
                    forceLedger);
            }

            telemetry.longitudinalSpeed = longitudinalSpeed;
            telemetry.lateralSpeed = lateralSpeed;
            telemetry.longitudinalForce = longitudinalForce;
            telemetry.lateralForce = lateralForce;
            telemetry.slipRatio = slipRatio;
            telemetry.slipAngleDegrees = slipAngle * Mathf.Rad2Deg;
            telemetry.wheelAngularSpeed = wheelAngularSpeed;
        }

        public void PrepareFinalConstraints(
            Vector3 up,
            float gravityMagnitude,
            float fixedStep,
            VehicleForceLedger forceLedger)
        {
            if (body == null || forceLedger == null)
                return;

            float step = Mathf.Max(0.0001f, fixedStep);
            float radius = Mathf.Max(0.12f, Profile.radius);
            float travel = Mathf.Max(
                0.05f,
                Profile.suspensionTravel);
            PhysicsScene physicsScene =
                body.gameObject.scene.GetPhysicsScene();
            if (!grounded)
            {
                if (ProbeContact(up, step, forceLedger))
                {
                    PrepareSuspension(
                        up,
                        gravityMagnitude,
                        step);
                }
            }
            else
            {
                ResolveBaseFrame(up);
                ProbeObstacle(
                    physicsScene,
                    step,
                    radius,
                    travel,
                    forceLedger);
            }

            if (!grounded || contactForcesAppliedThisStep)
                return;

            float constraint = ResolveGroundConstraintForce(
                step,
                forceLedger,
                false);
            normalForce += constraint;
            telemetry.normalForce = normalForce;
            Vector3 force = contactNormal * normalForce;
            forceLedger.AddForceAtPosition(force, contactPoint);
            ApplyReaction(
                groundBody,
                -force * step,
                contactPoint,
                forceLedger);
            contactForcesAppliedThisStep = true;
        }

        public void ApplyConstraintCorrection(
            float fixedStep,
            VehicleForceLedger forceLedger)
        {
            if (body == null || forceLedger == null)
                return;
            float step = Mathf.Max(0.0001f, fixedStep);
            Vector3 obstacleForce =
                ResolveObstacleConstraintForce(step, forceLedger);
            if (obstacleForce.sqrMagnitude > 0f)
            {
                forceLedger.AddForceAtPosition(
                    obstacleForce,
                    obstaclePoint);
                ApplyReaction(
                    obstacleBody,
                    -obstacleForce * step,
                    obstaclePoint,
                    forceLedger);
            }
            if (!grounded)
                return;

            float correction = ResolveGroundConstraintForce(
                step,
                forceLedger,
                true);
            if (Mathf.Abs(correction) <= 0.001f)
                return;
            normalForce = Mathf.Max(
                0f,
                normalForce + correction);
            telemetry.normalForce = normalForce;
            Vector3 force = contactNormal * correction;
            forceLedger.AddForceAtPosition(force, contactPoint);
            ApplyReaction(
                groundBody,
                -force * step,
                contactPoint,
                forceLedger);
        }

        // Compatibility entry point for older callers and diagnostic tools.
        public void ApplyForces(
            Vector3 up,
            Vector3 vehicleForward,
            float throttle,
            float steering,
            bool braking,
            bool boosting,
            float supportedMass,
            int groundedWheelCount,
            VehicleForceLedger forceLedger = null)
        {
            if (!sprungMassAssigned)
            {
                SetSprungMass(
                    supportedMass /
                    Mathf.Max(1, groundedWheelCount));
            }
            PrepareSuspension(
                up,
                Physics.gravity.magnitude > 0.1f
                    ? Physics.gravity.magnitude
                    : 9.81f,
                Time.fixedDeltaTime);
            ApplyForces(
                up,
                throttle,
                steering * Profile.maximumSteerAngle,
                braking,
                boosting,
                1f,
                Time.fixedDeltaTime,
                forceLedger);
        }

        public static float ResolveSurfaceGrip(Collider collider)
        {
            if (collider == null || collider.sharedMaterial == null)
                return 1f;
            PhysicMaterial material = collider.sharedMaterial;
            float sourceFriction = Mathf.Max(
                material.dynamicFriction,
                material.staticFriction * 0.85f);
            return Mathf.Clamp(sourceFriction / 0.6f, 0f, 1.75f);
        }

        private bool ProbeWheelVolume(
            PhysicsScene physicsScene,
            int queryLayerMask,
            float radius,
            float travel,
            float predictiveDrop,
            float recoveryPenetration,
            out RaycastHit bestHit,
            out float bestDroop)
        {
            bestHit = default;
            bestDroop = float.PositiveInfinity;
            float probeRadius = ResolveVolumeProbeRadius(radius);
            float maximumLongitudinal =
                Mathf.Max(0f, radius - probeRadius);
            float coverageBias =
                ResolveConservativeEnvelopeBias(radius);
            float startOffset =
                Mathf.Max(0.025f, recoveryPenetration);
            float castDistance =
                startOffset + travel + predictiveDrop +
                radius * 0.25f;

            for (int sampleIndex = 0;
                 sampleIndex < VolumeLongitudinalSampleCount;
                 sampleIndex++)
            {
                float normalized =
                    ResolveNormalizedVolumeSample(sampleIndex);
                float longitudinalOffset =
                    normalized * maximumLongitudinal;
                float circleDepth = Mathf.Sqrt(Mathf.Max(
                    0f,
                    radius * radius -
                    longitudinalOffset * longitudinalOffset));
                Vector3 capsuleCenter =
                    NeutralWheelCenterWorld +
                    baseForwardWorld * longitudinalOffset -
                    suspensionUpWorld *
                    (Mathf.Max(
                         0f,
                         circleDepth - probeRadius) +
                     coverageBias) +
                    suspensionUpWorld * startOffset;
                ResolveCapsuleEndpoints(
                    capsuleCenter,
                    probeRadius,
                    out Vector3 point1,
                    out Vector3 point2);
                int hitCount = CapsuleCastBuffered(
                    physicsScene,
                    point1,
                    point2,
                    probeRadius,
                    -suspensionUpWorld,
                    castDistance,
                    queryLayerMask,
                    QueryTriggerInteraction.Ignore);
                for (int hitIndex = 0;
                     hitIndex < hitCount;
                     hitIndex++)
                {
                    RaycastHit candidate = hits[hitIndex];
                    if (!ValidContact(
                            candidate,
                            environmentUpWorld,
                            suspensionUpWorld))
                    {
                        continue;
                    }
                    float candidateDroop =
                        candidate.distance - startOffset;
                    if (candidateDroop >= bestDroop)
                        continue;
                    bestDroop = candidateDroop;
                    bestHit = candidate;
                }
            }
            return bestHit.collider != null;
        }

        private VolumeContact ProbeWheelOverlap(
            PhysicsScene physicsScene,
            int queryLayerMask,
            float radius,
            float travel)
        {
            VolumeContact best = default;
            float probeRadius = ResolveVolumeProbeRadius(radius);
            float maximumLongitudinal =
                Mathf.Max(0f, radius - probeRadius);
            float coverageBias =
                ResolveConservativeEnvelopeBias(radius);
            float estimatedDroop = hadPreviousContact
                ? Mathf.Clamp(
                    suspensionDroop,
                    -radius * 1.5f,
                    travel)
                : travel;
            Vector3 wheelCenter =
                NeutralWheelCenterWorld -
                suspensionUpWorld * estimatedDroop;
            Quaternion probeRotation = Quaternion.LookRotation(
                baseForwardWorld,
                suspensionUpWorld);
            EnsurePenetrationProbe(probeRadius);
            penetrationProbeObject.SetActive(true);
            try
            {
                for (int sampleIndex = 0;
                     sampleIndex < VolumeLongitudinalSampleCount;
                     sampleIndex++)
                {
                    float normalized =
                        ResolveNormalizedVolumeSample(sampleIndex);
                    float longitudinalOffset =
                        normalized * maximumLongitudinal;
                    float circleDepth = Mathf.Sqrt(Mathf.Max(
                        0f,
                        radius * radius -
                        longitudinalOffset * longitudinalOffset));
                    Vector3 capsuleCenter =
                        wheelCenter +
                        baseForwardWorld * longitudinalOffset -
                        suspensionUpWorld *
                        (Mathf.Max(
                             0f,
                             circleDepth - probeRadius) +
                         coverageBias);
                    ResolveCapsuleEndpoints(
                        capsuleCenter,
                        probeRadius,
                        out Vector3 point1,
                        out Vector3 point2);
                    int overlapCount = OverlapCapsuleBuffered(
                        physicsScene,
                        point1,
                        point2,
                        probeRadius,
                        queryLayerMask,
                        QueryTriggerInteraction.Ignore);
                    for (int index = 0; index < overlapCount; index++)
                    {
                        Collider candidate = overlaps[index];
                        if (!ValidNonSelf(candidate))
                            continue;

                        bool penetrates = Physics.ComputePenetration(
                            penetrationProbe,
                            capsuleCenter,
                            probeRotation,
                            candidate,
                            candidate.transform.position,
                            candidate.transform.rotation,
                            out Vector3 direction,
                            out float distance);
                        float normalProjection = penetrates
                            ? Vector3.Dot(
                                direction,
                                suspensionUpWorld)
                            : 0f;
                        if (penetrates &&
                            distance > 0.0001f &&
                            Vector3.Dot(
                                direction,
                                environmentUpWorld) >=
                            MinimumGroundNormalDot &&
                            normalProjection >= 0.05f)
                        {
                            float droop =
                                estimatedDroop -
                                distance /
                                Mathf.Max(0.05f, normalProjection);
                            if (!best.valid || droop < best.droop)
                            {
                                best = new VolumeContact
                                {
                                    collider = candidate,
                                    normal = direction.normalized,
                                    point =
                                        capsuleCenter -
                                        direction.normalized *
                                        (probeRadius - distance),
                                    droop = droop
                                };
                            }
                        }
                    }
                }

                // A valid MTD is the most local and least ambiguous answer.
                // Never replace it with a bounds-based surface search: a
                // disconnected bridge deck can share the same MeshCollider.
                if (best.valid)
                    return best;

                // If the tyre crossed a one-sided mesh completely, an overlap
                // query has no candidate and downward probes start below the
                // surface. Sweep the actual previous-to-current tread paths.
                // The first crossed surface is the only historically reachable
                // recovery target.
                if (hasPreviousWheelCenter)
                {
                    VolumeContact historical = default;
                    float historicalDistance = float.PositiveInfinity;
                    for (int sampleIndex = 0;
                         sampleIndex < VolumeLongitudinalSampleCount;
                         sampleIndex++)
                    {
                        float normalized =
                            ResolveNormalizedVolumeSample(sampleIndex);
                        float longitudinalOffset =
                            normalized * maximumLongitudinal;
                        float circleDepth = Mathf.Sqrt(Mathf.Max(
                            0f,
                            radius * radius -
                            longitudinalOffset * longitudinalOffset));
                        Vector3 capsuleCenter =
                            wheelCenter +
                            baseForwardWorld * longitudinalOffset -
                            suspensionUpWorld *
                            (Mathf.Max(
                                 0f,
                                 circleDepth - probeRadius) +
                             coverageBias);
                        ResolveCapsuleEndpoints(
                            capsuleCenter,
                            probeRadius,
                            out Vector3 point1,
                            out Vector3 point2);
                        TryResolveHistoricalSurface(
                            physicsScene,
                            wheelCenter,
                            capsuleCenter,
                            probeRadius,
                            estimatedDroop,
                            radius,
                            travel,
                            queryLayerMask,
                            ref historical,
                            ref historicalDistance);
                        TryResolveHistoricalSurface(
                            physicsScene,
                            wheelCenter,
                            point1,
                            probeRadius,
                            estimatedDroop,
                            radius,
                            travel,
                            queryLayerMask,
                            ref historical,
                            ref historicalDistance);
                        TryResolveHistoricalSurface(
                            physicsScene,
                            wheelCenter,
                            point2,
                            probeRadius,
                            estimatedDroop,
                            radius,
                            travel,
                            queryLayerMask,
                            ref historical,
                            ref historicalDistance);
                    }
                    if (historical.valid)
                        return historical;
                }

                // ComputePenetration can return no useful MTD for a concave
                // mesh. Only in that case, and only for colliders that really
                // overlap the tread, search a small local height window.
                for (int sampleIndex = 0;
                     sampleIndex < VolumeLongitudinalSampleCount;
                     sampleIndex++)
                {
                    float normalized =
                        ResolveNormalizedVolumeSample(sampleIndex);
                    float longitudinalOffset =
                        normalized * maximumLongitudinal;
                    float circleDepth = Mathf.Sqrt(Mathf.Max(
                        0f,
                        radius * radius -
                        longitudinalOffset * longitudinalOffset));
                    Vector3 capsuleCenter =
                        wheelCenter +
                        baseForwardWorld * longitudinalOffset -
                        suspensionUpWorld *
                        (Mathf.Max(
                             0f,
                             circleDepth - probeRadius) +
                         coverageBias);
                    ResolveCapsuleEndpoints(
                        capsuleCenter,
                        probeRadius,
                        out Vector3 point1,
                        out Vector3 point2);
                    int overlapCount = OverlapCapsuleBuffered(
                        physicsScene,
                        point1,
                        point2,
                        probeRadius,
                        queryLayerMask,
                        QueryTriggerInteraction.Ignore);
                    for (int index = 0; index < overlapCount; index++)
                    {
                        Collider candidate = overlaps[index];
                        if (!ValidNonSelf(candidate))
                            continue;
                        TryResolveDeepSurface(
                            physicsScene,
                            candidate,
                            capsuleCenter,
                            probeRadius,
                            estimatedDroop,
                            radius,
                            travel,
                            queryLayerMask,
                            ref best);
                        TryResolveDeepSurface(
                            physicsScene,
                            candidate,
                            point1,
                            probeRadius,
                            estimatedDroop,
                            radius,
                            travel,
                            queryLayerMask,
                            ref best);
                        TryResolveDeepSurface(
                            physicsScene,
                            candidate,
                            point2,
                            probeRadius,
                            estimatedDroop,
                            radius,
                            travel,
                            queryLayerMask,
                            ref best);
                    }
                }
            }
            finally
            {
                penetrationProbeObject.SetActive(false);
            }
            return best;
        }

        private void TryResolveDeepSurface(
            PhysicsScene physicsScene,
            Collider candidate,
            Vector3 capsulePoint,
            float probeRadius,
            float estimatedDroop,
            float radius,
            float travel,
            int queryLayerMask,
            ref VolumeContact best)
        {
            float recoveryReach =
                Mathf.Max(0.05f, radius + travel);
            Vector3 tyreBottom =
                capsulePoint - suspensionUpWorld * probeRadius;
            Vector3 neutralWheelCenter = NeutralWheelCenterWorld;
            float anchorProjection =
                Vector3.Dot(
                    neutralWheelCenter,
                    environmentUpWorld);
            float minimumProjection = Mathf.Max(
                Vector3.Dot(tyreBottom, environmentUpWorld),
                anchorProjection - recoveryReach);
            float maximumProjection =
                anchorProjection + recoveryReach;

            if (hadPreviousContact && hasPreviousWheelCenter)
            {
                Vector3 previousAnchor =
                    previousWheelCenterWorld +
                    suspensionUpWorld * estimatedDroop;
                float anchorMotion = Mathf.Min(
                    radius * 0.35f,
                    Vector3.Distance(
                        neutralWheelCenter,
                        previousAnchor));
                float historyAllowance =
                    travel + probeRadius * 2f + anchorMotion;
                float previousSurfaceProjection =
                    Vector3.Dot(contactPoint, environmentUpWorld);
                minimumProjection = Mathf.Max(
                    minimumProjection,
                    previousSurfaceProjection - historyAllowance);
                maximumProjection = Mathf.Min(
                    maximumProjection,
                    previousSurfaceProjection + historyAllowance);
            }
            if (maximumProjection <= minimumProjection + 0.001f)
                return;

            float scanStep = Mathf.Clamp(
                probeRadius * 0.5f,
                0.015f,
                Mathf.Max(0.02f, radius * 0.05f));
            float skin = Mathf.Max(0.005f, scanStep * 0.25f);
            int stepCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (maximumProjection - minimumProjection) /
                    scanStep));
            RaycastHit nearest = default;
            float nearestPenetration = float.PositiveInfinity;
            for (int stepIndex = 0;
                 stepIndex < stepCount;
                 stepIndex++)
            {
                float bandBottom =
                    minimumProjection + stepIndex * scanStep;
                float bandTop = Mathf.Min(
                    maximumProjection + skin,
                    bandBottom + scanStep + skin);
                Vector3 origin =
                    tyreBottom +
                    environmentUpWorld *
                    (bandTop -
                     Vector3.Dot(tyreBottom, environmentUpWorld));
                int hitCount = RaycastBuffered(
                    physicsScene,
                    origin,
                    -environmentUpWorld,
                    bandTop - bandBottom + skin,
                    queryLayerMask,
                    QueryTriggerInteraction.Ignore);
                for (int hitIndex = 0;
                     hitIndex < hitCount;
                     hitIndex++)
                {
                    RaycastHit hit = hits[hitIndex];
                    if (hit.collider != candidate ||
                        !ValidContact(
                            hit,
                            environmentUpWorld,
                            suspensionUpWorld))
                    {
                        continue;
                    }
                    float hitProjection =
                        Vector3.Dot(hit.point, environmentUpWorld);
                    if (hitProjection <
                            minimumProjection - skin ||
                        hitProjection >
                            maximumProjection + skin)
                    {
                        continue;
                    }
                    float penetrationAlongSuspension =
                        Vector3.Dot(
                            hit.point - tyreBottom,
                            suspensionUpWorld);
                    if (penetrationAlongSuspension <= 0.0001f ||
                        penetrationAlongSuspension >=
                        nearestPenetration)
                    {
                        continue;
                    }
                    nearestPenetration =
                        penetrationAlongSuspension;
                    nearest = hit;
                }
                if (nearest.collider != null)
                    break;
            }
            if (nearest.collider == null)
                return;

            float droop =
                estimatedDroop - nearestPenetration;
            if (!best.valid || droop < best.droop)
            {
                best = new VolumeContact
                {
                    collider = candidate,
                    point = nearest.point,
                    normal = nearest.normal.normalized,
                    droop = droop
                };
            }
        }

        private void TryResolveHistoricalSurface(
            PhysicsScene physicsScene,
            Vector3 currentWheelCenter,
            Vector3 capsulePoint,
            float probeRadius,
            float estimatedDroop,
            float radius,
            float travel,
            int queryLayerMask,
            ref VolumeContact best,
            ref float bestDistance)
        {
            Vector3 currentBottom =
                capsulePoint - suspensionUpWorld * probeRadius;
            Vector3 previousBottom =
                currentBottom +
                (previousWheelCenterWorld - currentWheelCenter);
            Vector3 sweep = currentBottom - previousBottom;
            float sweepLength = sweep.magnitude;
            float recoveryReach =
                Mathf.Max(0.05f, radius + travel);
            float skin = Mathf.Max(0.01f, probeRadius * 0.5f);
            if (sweepLength <= 0.0001f ||
                sweepLength > recoveryReach + probeRadius * 2f)
            {
                return;
            }

            Vector3 direction = sweep / sweepLength;
            if (Vector3.Dot(direction, suspensionUpWorld) >= -0.05f)
                return;
            Vector3 origin = previousBottom - direction * skin;
            int hitCount = RaycastBuffered(
                physicsScene,
                origin,
                direction,
                sweepLength + skin * 2f,
                queryLayerMask,
                QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0;
                 hitIndex < hitCount;
                 hitIndex++)
            {
                RaycastHit hit = hits[hitIndex];
                if (!ValidContact(
                        hit,
                        environmentUpWorld,
                        suspensionUpWorld) ||
                    Vector3.Dot(direction, hit.normal) >= -0.05f ||
                    hit.distance >= bestDistance)
                {
                    continue;
                }

                float anchorProjection =
                    Vector3.Dot(
                        NeutralWheelCenterWorld,
                        environmentUpWorld);
                float hitProjection =
                    Vector3.Dot(hit.point, environmentUpWorld);
                if (Mathf.Abs(hitProjection - anchorProjection) >
                    recoveryReach)
                {
                    continue;
                }
                if (hadPreviousContact)
                {
                    float previousSurfaceProjection =
                        Vector3.Dot(contactPoint, environmentUpWorld);
                    float historyAllowance =
                        travel + probeRadius * 2f +
                        Mathf.Min(radius * 0.35f, sweepLength);
                    if (Mathf.Abs(
                            hitProjection -
                            previousSurfaceProjection) >
                        historyAllowance)
                    {
                        continue;
                    }
                }

                float penetrationAlongSuspension =
                    Vector3.Dot(
                        hit.point - currentBottom,
                        suspensionUpWorld);
                if (penetrationAlongSuspension <= 0.0001f)
                    continue;
                bestDistance = hit.distance;
                best = new VolumeContact
                {
                    collider = hit.collider,
                    point = hit.point,
                    normal = hit.normal.normalized,
                    droop =
                        estimatedDroop -
                        penetrationAlongSuspension
                };
            }
        }

        private void ProbeObstacle(
            PhysicsScene physicsScene,
            float step,
            float radius,
            float travel,
            VehicleForceLedger forceLedger)
        {
            Collider previousCollider = obstacleCollider;
            Rigidbody previousBody = obstacleBody;
            Vector3 previousPoint = obstaclePoint;
            Vector3 previousNormal = obstacleNormal;
            float previousConstraint = obstacleConstraintForce;
            obstacleConstraintForce = 0f;
            ResetObstacleContact();
            float estimatedDroop = grounded
                ? Mathf.Clamp(suspensionDroop, -radius * 0.22f, travel)
                : hadPreviousContact
                    ? Mathf.Clamp(suspensionDroop, -radius * 0.22f, travel)
                    : travel;
            Vector3 currentCenter =
                NeutralWheelCenterWorld -
                suspensionUpWorld * estimatedDroop;
            Vector3 pointVelocity = forceLedger != null
                ? PredictedBodyPointVelocity(
                    currentCenter,
                    forceLedger,
                    step)
                : body.GetPointVelocity(currentCenter);
            bool airborneSweep = !grounded;
            // Downward motion of an already predictive-grounded tyre is owned
            // by the unilateral ground constraint. Sweeping it a second time
            // against the same road creates duplicate, triangle-dependent
            // impulses and can roll a symmetric vehicle on landing. Upward
            // late forces remain in the full sweep so thin ceilings cannot be
            // crossed after the ordinary wheel pass.
            float suspensionAxisSpeed = Vector3.Dot(
                pointVelocity,
                suspensionUpWorld);
            Vector3 sweepVelocity =
                !grounded || suspensionAxisSpeed > 0f
                    ? pointVelocity
                    : Vector3.ProjectOnPlane(
                        pointVelocity,
                        suspensionUpWorld);
            Vector3 predictedCenter =
                currentCenter + sweepVelocity * step;
            Vector3 start = hasPreviousWheelCenter
                ? previousWheelCenterWorld
                : currentCenter;
            float maximumHistoryDistance =
                Mathf.Max(5f, radius * 10f);
            if ((currentCenter - start).sqrMagnitude >
                maximumHistoryDistance * maximumHistoryDistance)
            {
                start = currentCenter;
            }
            Vector3 sweep = predictedCenter - start;
            if (sweep.sqrMagnitude < 0.000025f)
            {
                FinalizeObstacleProbe(
                    previousCollider,
                    previousBody,
                    previousPoint,
                    previousNormal,
                    previousConstraint,
                    step,
                    forceLedger);
                return;
            }

            float sweepLength = sweep.magnitude;
            Vector3 direction = sweep / sweepLength;
            float skin = Mathf.Max(0.025f, radius * 0.045f);
            start -= direction * skin;
            sweepLength += skin * 2f;
            float currentDistanceFromStart =
                Mathf.Max(
                    0f,
                    Vector3.Dot(currentCenter - start, direction));
            float probeRadius = ResolveVolumeProbeRadius(radius);
            float shellRadius = ResolveObstacleShellRadius(
                radius,
                probeRadius);
            int queryLayerMask = ResolveQueryLayerMask();

            for (int sampleIndex = 0;
                 sampleIndex < ObstacleCircumferenceSampleCount;
                 sampleIndex++)
            {
                float angle =
                    Mathf.PI * 2f * sampleIndex /
                    ObstacleCircumferenceSampleCount;
                Vector3 radialDirection =
                    baseForwardWorld * Mathf.Cos(angle) +
                    suspensionUpWorld * Mathf.Sin(angle);
                Vector3 capsuleCenter =
                    start +
                    radialDirection * shellRadius;
                ResolveCapsuleEndpoints(
                    capsuleCenter,
                    probeRadius,
                    out Vector3 point1,
                    out Vector3 point2);
                int hitCount = CapsuleCastBuffered(
                    physicsScene,
                    point1,
                    point2,
                    probeRadius,
                    direction,
                    sweepLength,
                    queryLayerMask,
                    QueryTriggerInteraction.Ignore);
                for (int hitIndex = 0;
                     hitIndex < hitCount;
                     hitIndex++)
                {
                    RaycastHit candidate = hits[hitIndex];
                    if (!ValidNonSelf(candidate) ||
                        Vector3.Dot(candidate.normal, direction) >= -0.05f)
                    {
                        continue;
                    }
                    if (!airborneSweep &&
                        SameContactManifold(
                            contactCollider,
                            groundBody,
                            candidate.collider) &&
                        candidate.distance <=
                        currentDistanceFromStart + skin * 0.5f)
                    {
                        // SphereCast reports a synthetic normal opposite the
                        // cast direction when a lower-envelope probe starts
                        // touching its current road manifold. It is not a
                        // wall; real terrain faces ahead have positive TOI.
                        continue;
                    }
                    float clearance =
                        candidate.distance -
                        currentDistanceFromStart;
                    if (clearance >= obstacleSweepDistance)
                        continue;
                    obstacleSweepDistance = clearance;
                    float approachProjection = Mathf.Max(
                        0.05f,
                        -Vector3.Dot(
                            candidate.normal,
                            direction));
                    obstacleClearance =
                        clearance * approachProjection;
                    obstaclePoint = candidate.point;
                    obstacleNormal = candidate.normal.normalized;
                    obstacleCollider = candidate.collider;
                    obstacleBody =
                        candidate.collider.attachedRigidbody;
                    obstacleContact = true;
                }
            }
            FinalizeObstacleProbe(
                previousCollider,
                previousBody,
                previousPoint,
                previousNormal,
                previousConstraint,
                step,
                forceLedger);
        }

        private void FinalizeObstacleProbe(
            Collider previousCollider,
            Rigidbody previousBody,
            Vector3 previousPoint,
            Vector3 previousNormal,
            float previousConstraint,
            float step,
            VehicleForceLedger forceLedger)
        {
            bool sameConstraint =
                obstacleContact &&
                previousConstraint > 0f &&
                SameContactManifold(
                    previousCollider,
                    previousBody,
                    obstacleCollider) &&
                Vector3.Dot(
                    previousNormal,
                    obstacleNormal) >= 0.7f;
            if (sameConstraint)
            {
                obstacleConstraintForce = previousConstraint;
            }
            else if (previousConstraint > 0f &&
                     previousNormal.sqrMagnitude > 0.5f &&
                     forceLedger != null)
            {
                Vector3 release =
                    -previousNormal.normalized *
                    previousConstraint;
                forceLedger.AddForceAtPosition(
                    release,
                    previousPoint);
                ApplyReaction(
                    previousBody,
                    -release * step,
                    previousPoint,
                    forceLedger);
            }
            if (obstacleContact &&
                obstacleBody != null &&
                obstacleBody != body)
            {
                obstacleGroundVelocity =
                    obstacleBody.GetPointVelocity(obstaclePoint);
            }
            telemetry.obstacleContact = obstacleContact;
            telemetry.obstaclePoint = obstaclePoint;
            telemetry.obstacleNormal = obstacleNormal;
            telemetry.obstacleClearance = obstacleClearance;
            telemetry.obstacleForce = obstacleConstraintForce;
        }

        private void ResetObstacleContact()
        {
            obstacleContact = false;
            obstacleCollider = null;
            obstacleBody = null;
            obstaclePoint = Vector3.zero;
            obstacleNormal = Vector3.zero;
            obstacleGroundVelocity = Vector3.zero;
            obstacleClearance = float.PositiveInfinity;
            obstacleSweepDistance = float.PositiveInfinity;
            telemetry.obstacleContact = false;
            telemetry.obstaclePoint = Vector3.zero;
            telemetry.obstacleNormal = Vector3.zero;
            telemetry.obstacleClearance = float.PositiveInfinity;
            telemetry.obstacleForce = obstacleConstraintForce;
        }

        private float ResolveVolumeProbeRadius(float radius)
        {
            float width = Mathf.Max(0.05f, Profile.width);
            float circumferenceMinimum =
                radius *
                Mathf.Sin(
                    Mathf.PI /
                    ObstacleCircumferenceSampleCount) *
                1.05f;
            return Mathf.Max(
                0.04f,
                Mathf.Min(
                    radius * 0.2f,
                    Mathf.Max(
                        width * 0.12f,
                        circumferenceMinimum)));
        }

        private static float ResolveNormalizedVolumeSample(int index)
        {
            return -1f +
                   2f * Mathf.Clamp(
                       index,
                       0,
                       VolumeLongitudinalSampleCount - 1) /
                   (VolumeLongitudinalSampleCount - 1);
        }

        private static float ResolveConservativeEnvelopeBias(float radius)
        {
            // Numeric upper bound for the sag between adjacent tangent
            // capsules with the supported profile ratios. This is collision
            // skin, not suspension travel, and is kept below 15 mm.
            return Mathf.Max(0.006f, radius * 0.011f);
        }

        private static float ResolveObstacleShellRadius(
            float radius,
            float probeRadius)
        {
            float halfAngle =
                Mathf.PI / ObstacleCircumferenceSampleCount;
            float radialChord =
                radius * Mathf.Sin(halfAngle);
            float discriminant = Mathf.Max(
                0f,
                probeRadius * probeRadius -
                radialChord * radialChord);
            // Adjacent probe capsules meet exactly on the requested tyre
            // radius. At sample centres the result is slightly conservative,
            // which prevents high-speed tunnelling between casts.
            return Mathf.Max(
                0f,
                radius * Mathf.Cos(halfAngle) -
                Mathf.Sqrt(discriminant));
        }

        private void ResolveCapsuleEndpoints(
            Vector3 center,
            float radius,
            out Vector3 point1,
            out Vector3 point2)
        {
            // Endpoint centres reach the nominal tread shoulders. The rounded
            // caps therefore sit outside the visible tyre rather than leaving
            // an inward shoulder gap where a narrow ridge can penetrate.
            float halfSegment =
                Mathf.Max(0.05f, Profile.width) * 0.5f;
            Vector3 offset = baseAxleWorld * halfSegment;
            point1 = center - offset;
            point2 = center + offset;
        }

        private void EnsurePenetrationProbe(float worldRadius)
        {
            if (penetrationProbe == null)
            {
                penetrationProbeObject =
                    new GameObject("WheelPenetrationQuery");
                penetrationProbeObject.hideFlags =
                    HideFlags.HideAndDontSave;
                penetrationProbeObject.layer = IgnoreRaycastLayer;
                if (body != null &&
                    body.gameObject.scene.IsValid())
                {
                    SceneManager.MoveGameObjectToScene(
                        penetrationProbeObject,
                        body.gameObject.scene);
                }
                penetrationProbe =
                    penetrationProbeObject.AddComponent<
                        CapsuleCollider>();
                penetrationProbe.isTrigger = true;
                penetrationProbe.direction = 0;
                penetrationProbe.center = Vector3.zero;
                penetrationProbeObject.SetActive(false);
            }
            penetrationProbe.radius = worldRadius;
            penetrationProbe.height = Mathf.Max(
                worldRadius * 2f,
                Mathf.Max(0.05f, Profile.width) +
                worldRadius * 2f);
        }

        private int RaycastBuffered(
            PhysicsScene physicsScene,
            Vector3 origin,
            Vector3 direction,
            float distance,
            int layerMask,
            QueryTriggerInteraction triggerInteraction)
        {
            while (true)
            {
                int count = physicsScene.Raycast(
                    origin,
                    direction,
                    hits,
                    distance,
                    layerMask,
                    triggerInteraction);
                if (count < hits.Length ||
                    hits.Length >= MaximumQueryResults)
                {
                    return count;
                }
                Array.Resize(
                    ref hits,
                    Mathf.Min(
                        MaximumQueryResults,
                        hits.Length * 2));
            }
        }

        private int CapsuleCastBuffered(
            PhysicsScene physicsScene,
            Vector3 point1,
            Vector3 point2,
            float radius,
            Vector3 direction,
            float distance,
            int layerMask,
            QueryTriggerInteraction triggerInteraction)
        {
            while (true)
            {
                int count = physicsScene.CapsuleCast(
                    point1,
                    point2,
                    radius,
                    direction,
                    hits,
                    distance,
                    layerMask,
                    triggerInteraction);
                if (count < hits.Length ||
                    hits.Length >= MaximumQueryResults)
                {
                    return count;
                }
                Array.Resize(
                    ref hits,
                    Mathf.Min(
                        MaximumQueryResults,
                        hits.Length * 2));
            }
        }

        private int OverlapCapsuleBuffered(
            PhysicsScene physicsScene,
            Vector3 point1,
            Vector3 point2,
            float radius,
            int layerMask,
            QueryTriggerInteraction triggerInteraction)
        {
            while (true)
            {
                int count = physicsScene.OverlapCapsule(
                    point1,
                    point2,
                    radius,
                    overlaps,
                    layerMask,
                    triggerInteraction);
                if (count < overlaps.Length ||
                    overlaps.Length >= MaximumQueryResults)
                {
                    return count;
                }
                Array.Resize(
                    ref overlaps,
                    Mathf.Min(
                        MaximumQueryResults,
                        overlaps.Length * 2));
            }
        }

        private int ResolveQueryLayerMask()
        {
            if (body == null)
                return ~0;
            int sourceLayer = body.gameObject.layer;
            int result = 0;
            for (int layer = 0; layer < 32; layer++)
            {
                if (!Physics.GetIgnoreLayerCollision(
                        sourceLayer,
                        layer))
                {
                    result |= 1 << layer;
                }
            }
            return result & ~(1 << IgnoreRaycastLayer);
        }

        private static bool SameContactManifold(
            Collider primaryCollider,
            Rigidbody primaryBody,
            Collider candidateCollider)
        {
            if (primaryCollider == null || candidateCollider == null)
                return false;
            return primaryBody != null
                ? candidateCollider.attachedRigidbody == primaryBody
                : candidateCollider == primaryCollider;
        }

        private bool ValidContact(
            RaycastHit value,
            Vector3 environmentUp,
            Vector3 suspensionUp)
        {
            return ValidNonSelf(value) &&
                   Vector3.Dot(value.normal, environmentUp) >=
                   MinimumGroundNormalDot &&
                   Vector3.Dot(value.normal, suspensionUp) >= 0.05f;
        }

        private bool ValidNonSelf(RaycastHit value)
        {
            return ValidNonSelf(value.collider);
        }

        private bool ValidNonSelf(Collider value)
        {
            return value != null &&
                   value.attachedRigidbody != body &&
                   !value.transform.IsChildOf(body.transform);
        }

        private void ResolveBaseFrame(Vector3 up)
        {
            environmentUpWorld = up.sqrMagnitude > 0.0001f
                ? up.normalized
                : Vector3.up;
            Transform orientationRoot = ModuleRoot;
            Vector3 rawAxle =
                orientationRoot.TransformDirection(axleLocal);
            if (rawAxle.sqrMagnitude < 0.0001f)
                rawAxle = orientationRoot.right;
            rawAxle.Normalize();
            Vector3 visualAxleWorld = rawAxle;

            Vector3 rawForward = Vector3.ProjectOnPlane(
                orientationRoot.TransformDirection(
                    rollingForwardLocal),
                rawAxle);
            if (rawForward.sqrMagnitude < 0.0001f)
            {
                rawForward = Vector3.ProjectOnPlane(
                    vehicleRoot != null
                        ? vehicleRoot.forward
                        : orientationRoot.forward,
                    rawAxle);
            }
            if (rawForward.sqrMagnitude < 0.0001f)
                rawForward = Vector3.Cross(
                    rawAxle,
                    environmentUpWorld);
            rawForward.Normalize();

            // Mirrored generic wheels are yawed 180 degrees so their hubs
            // face the hull. That reverses both catalogue axes even though
            // their physical rolling plane is unchanged. Canonicalize only
            // the sign pair to the vehicle's expected forward direction;
            // sideways/custom wheel planes remain untouched.
            Vector3 expectedForward = vehicleRoot != null
                ? Vector3.ProjectOnPlane(
                    vehicleRoot.forward,
                    rawAxle)
                : Vector3.zero;
            if (expectedForward.sqrMagnitude > 0.0001f &&
                Vector3.Dot(
                    rawForward,
                    expectedForward.normalized) < -0.05f)
            {
                rawForward = -rawForward;
                rawAxle = -rawAxle;
            }

            Vector3 rawSuspensionUp =
                Vector3.Cross(rawForward, rawAxle).normalized;
            if (Vector3.Dot(
                    rawSuspensionUp,
                    environmentUpWorld) < 0f)
            {
                rawAxle = -rawAxle;
                rawSuspensionUp = -rawSuspensionUp;
            }
            baseAxleWorld = rawAxle;
            baseForwardWorld = rawForward;
            suspensionUpWorld = rawSuspensionUp;
            visualAxleSign =
                Vector3.Dot(baseAxleWorld, visualAxleWorld) >= 0f
                    ? 1f
                    : -1f;
        }

        private void UpdateFreeWheel(
            bool drives,
            float throttle,
            bool braking,
            bool boosting,
            float driveScale,
            float step)
        {
            float inertia = Mathf.Max(
                0.05f,
                0.5f * Profile.massKg *
                Profile.radius * Profile.radius);
            if (drives && !braking)
            {
                float torque =
                    Mathf.Clamp(throttle * driveScale, -1f, 1f) *
                    Profile.maximumMotorTorque *
                    moduleActuationShare *
                    (boosting ? 1.2f : 1f);
                wheelAngularSpeed += torque / inertia * step;
            }
            if (braking)
            {
                wheelAngularSpeed = Mathf.MoveTowards(
                    wheelAngularSpeed,
                    0f,
                    Profile.maximumBrakeTorque *
                    moduleActuationShare /
                    inertia * step);
            }
            wheelAngularSpeed *= Mathf.Exp(-0.18f * step);
            float maximumAngularSpeed =
                Profile.maximumSpeed * 2.2f /
                Mathf.Max(0.05f, Profile.radius) + 20f;
            wheelAngularSpeed = Mathf.Clamp(
                wheelAngularSpeed,
                -maximumAngularSpeed,
                maximumAngularSpeed);
            telemetry.wheelAngularSpeed = wheelAngularSpeed;
        }

        private float ResolveGroundConstraintForce(
            float step,
            VehicleForceLedger forceLedger,
            bool suspensionAlreadyPlanned)
        {
            if (!grounded ||
                body == null ||
                contactNormal.sqrMagnitude < 0.5f)
            {
                return 0f;
            }

            Vector3 relativeVelocity =
                PredictedBodyPointVelocity(
                    contactPoint,
                    forceLedger,
                    step) -
                PredictedOtherPointVelocity(
                    groundBody,
                    contactPoint,
                    forceLedger,
                    groundVelocity);
            float inverseMass = PairEffectiveInverseMass(
                contactPoint,
                contactNormal,
                groundBody);
            float suspensionToNormal = Mathf.Clamp(
                Vector3.Dot(
                    suspensionUpWorld,
                    contactNormal),
                0.05f,
                1f);
            float permittedClosingSpeed =
                Mathf.Max(0f, suspensionDroop) *
                suspensionToNormal /
                Mathf.Max(0.0001f, step);
            float penetration = Mathf.Max(
                penetrationDepth,
                Mathf.Max(0f, -suspensionDroop)) *
                suspensionToNormal;
            float predictedNormalVelocity =
                Vector3.Dot(relativeVelocity, contactNormal);
            if (!suspensionAlreadyPlanned)
            {
                predictedNormalVelocity +=
                    normalForce * inverseMass * step;
            }
            float targetNormalVelocity =
                -permittedClosingSpeed +
                penetration * 0.2f / step;
            float deltaForce =
                (targetNormalVelocity -
                 predictedNormalVelocity) /
                Mathf.Max(0.0001f, inverseMass * step);
            float previousConstraint = normalConstraintForce;
            normalConstraintForce = Mathf.Max(
                0f,
                previousConstraint + deltaForce);
            return normalConstraintForce - previousConstraint;
        }

        private Vector3 ResolveObstacleConstraintForce(
            float step,
            VehicleForceLedger forceLedger)
        {
            if (!obstacleContact ||
                body == null ||
                obstacleNormal.sqrMagnitude < 0.5f)
            {
                telemetry.obstacleForce =
                    obstacleConstraintForce;
                return Vector3.zero;
            }
            Vector3 relativeVelocity =
                PredictedBodyPointVelocity(
                    obstaclePoint,
                    forceLedger,
                    step) -
                PredictedOtherPointVelocity(
                    obstacleBody,
                    obstaclePoint,
                    forceLedger,
                    obstacleGroundVelocity);
            float predictedNormalVelocity =
                Vector3.Dot(relativeVelocity, obstacleNormal);
            float permittedClosingSpeed =
                Mathf.Max(0f, obstacleClearance) /
                Mathf.Max(0.0001f, step);
            float penetration = Mathf.Max(0f, -obstacleClearance);
            float inverseMass = PairEffectiveInverseMass(
                obstaclePoint,
                obstacleNormal,
                obstacleBody);
            float targetNormalVelocity =
                -permittedClosingSpeed +
                penetration * 0.25f /
                Mathf.Max(0.0001f, step);
            float deltaForce =
                (targetNormalVelocity -
                 predictedNormalVelocity) /
                Mathf.Max(0.0001f, inverseMass * step);
            float previousConstraint =
                obstacleConstraintForce;
            obstacleConstraintForce = Mathf.Max(
                0f,
                previousConstraint + deltaForce);
            telemetry.obstacleForce =
                obstacleConstraintForce;
            return obstacleNormal *
                   (obstacleConstraintForce -
                    previousConstraint);
        }

        private Vector3 PredictedBodyPointVelocity(
            Vector3 point,
            VehicleForceLedger forceLedger,
            float step)
        {
            Vector3 result = body.GetPointVelocity(point);
            if (forceLedger == null)
                return result;
            Vector3 linearDelta =
                forceLedger.TotalForce /
                Mathf.Max(0.01f, body.mass) * step;
            Vector3 angularDelta =
                ApplyWorldInverseInertia(
                    body,
                    forceLedger.TotalTorque) * step;
            Vector3 arm = point - body.worldCenterOfMass;
            return result +
                   linearDelta +
                   Vector3.Cross(angularDelta, arm);
        }

        private static Vector3 PredictedOtherPointVelocity(
            Rigidbody target,
            Vector3 point,
            VehicleForceLedger forceLedger,
            Vector3 fallback)
        {
            if (target == null)
                return Vector3.zero;
            return forceLedger != null
                ? forceLedger.PredictedExternalPointVelocity(
                    target,
                    point)
                : fallback;
        }

        private float PairEffectiveInverseMass(
            Vector3 point,
            Vector3 direction,
            Rigidbody otherBody)
        {
            float result = EffectiveInverseMass(
                point,
                direction,
                body);
            if (otherBody != null && otherBody != body)
            {
                result += EffectiveInverseMass(
                    point,
                    direction,
                    otherBody);
            }
            return result;
        }

        private float EffectiveInverseMass(
            Vector3 point,
            Vector3 direction,
            Rigidbody targetBody)
        {
            if (targetBody == null ||
                targetBody.isKinematic ||
                direction.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }
            Vector3 axis = direction.normalized;
            Vector3 arm = point - targetBody.worldCenterOfMass;
            Vector3 angularImpulse = Vector3.Cross(arm, axis);
            Vector3 angularVelocityDelta =
                ApplyWorldInverseInertia(
                    targetBody,
                    angularImpulse);
            Vector3 pointVelocityDelta =
                Vector3.Cross(angularVelocityDelta, arm);
            return 1f / Mathf.Max(0.01f, targetBody.mass) +
                   Mathf.Max(
                       0f,
                       Vector3.Dot(pointVelocityDelta, axis));
        }

        private Vector3 ApplyWorldInverseInertia(
            Rigidbody targetBody,
            Vector3 value)
        {
            Quaternion inertiaWorldRotation =
                targetBody.rotation *
                targetBody.inertiaTensorRotation;
            Vector3 local =
                Quaternion.Inverse(inertiaWorldRotation) * value;
            Vector3 inertia = targetBody.inertiaTensor;
            local = new Vector3(
                local.x / Mathf.Max(0.01f, inertia.x),
                local.y / Mathf.Max(0.01f, inertia.y),
                local.z / Mathf.Max(0.01f, inertia.z));
            return inertiaWorldRotation * local;
        }

        private void ApplyReaction(
            Rigidbody target,
            Vector3 impulse,
            Vector3 point,
            VehicleForceLedger forceLedger)
        {
            if (target == null ||
                target == body ||
                target.isKinematic)
            {
                return;
            }
            if (forceLedger != null)
            {
                forceLedger.AddExternalImpulse(
                    target,
                    impulse,
                    point);
            }
            else
            {
                VehicleExternalForces.ApplyImpulse(
                    target,
                    impulse,
                    point);
            }
        }

        private float ResolveVisualDroop()
        {
            float travel = Mathf.Max(
                0.05f,
                Profile.suspensionTravel);
            if (!grounded)
                return travel;

            Vector3 normal = contactNormal.sqrMagnitude > 0.5f
                ? contactNormal.normalized
                : suspensionUpWorld;
            float suspensionProjection = Mathf.Max(
                0.05f,
                Vector3.Dot(suspensionUpWorld, normal));
            // Preserve the same contact plane until the next fixed probe.
            // On a slope, projecting only onto suspensionUp under-corrects by
            // cos²(slope) and renders the tyre inside the surface for a frame.
            float anchorMotion = Vector3.Dot(
                NeutralWheelCenterWorld - probeAnchorWorld,
                normal) / suspensionProjection;
            float resolved = suspensionDroop + anchorMotion;
            if (!VehicleWholeBodyAudit.Finite(resolved))
                return travel;
            // Deep recovery deliberately has no arbitrary -2R visual clamp:
            // pin the tyre to the recovered surface while the chassis impulse
            // resolves an exceptional spawn/teleport penetration.
            return Mathf.Min(resolved, travel);
        }

        private void CalibrateProfileToVisualEnvelope(Transform visual)
        {
            Vector3 savedSuspensionPosition =
                suspensionPivot != null
                    ? suspensionPivot.localPosition
                    : Vector3.zero;
            Quaternion savedSteeringRotation =
                steeringPivot != null
                    ? steeringPivot.localRotation
                    : Quaternion.identity;
            try
            {
                // Rebuilds call Configure after LateUpdate may already have
                // applied droop and steering. Measure the authored visual at
                // its neutral pose so repeated modular edits cannot inflate or
                // reset the collision envelope.
                if (suspensionPivot != null)
                    suspensionPivot.localPosition = Vector3.zero;
                if (steeringPivot != null)
                    steeringPivot.localRotation = Quaternion.identity;
                CalibrateProfileToVisualEnvelopeNeutral(visual);
            }
            finally
            {
                if (suspensionPivot != null)
                {
                    suspensionPivot.localPosition =
                        savedSuspensionPosition;
                }
                if (steeringPivot != null)
                {
                    steeringPivot.localRotation =
                        savedSteeringRotation;
                }
            }
        }

        private void CalibrateProfileToVisualEnvelopeNeutral(
            Transform visual)
        {
            Renderer[] renderers =
                visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return;

            Vector3 axle =
                transform.TransformDirection(axleLocal).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(
                transform.TransformDirection(rollingForwardLocal),
                axle).normalized;
            if (forward.sqrMagnitude < 0.5f)
                forward = transform.forward;
            Vector3 visualUp = Vector3.Cross(forward, axle).normalized;
            if (visualUp.sqrMagnitude < 0.5f)
                visualUp = transform.up;
            if (vehicleRoot != null &&
                Vector3.Dot(visualUp, vehicleRoot.up) < 0f)
            {
                visualUp = -visualUp;
            }

            float minimumUp = float.PositiveInfinity;
            float minimumAxle = float.PositiveInfinity;
            float maximumAxle = float.NegativeInfinity;
            Vector3 anchor = NeutralWheelCenterWorld;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;
                Bounds bounds = renderer.bounds;
                Vector3 extents = bounds.extents;
                float upExtent =
                    Mathf.Abs(visualUp.x) * extents.x +
                    Mathf.Abs(visualUp.y) * extents.y +
                    Mathf.Abs(visualUp.z) * extents.z;
                float axleExtent =
                    Mathf.Abs(axle.x) * extents.x +
                    Mathf.Abs(axle.y) * extents.y +
                    Mathf.Abs(axle.z) * extents.z;
                float upCenter =
                    Vector3.Dot(bounds.center - anchor, visualUp);
                float axleCenter =
                    Vector3.Dot(bounds.center - anchor, axle);
                minimumUp = Mathf.Min(
                    minimumUp,
                    upCenter - upExtent);
                minimumAxle = Mathf.Min(
                    minimumAxle,
                    axleCenter - axleExtent);
                maximumAxle = Mathf.Max(
                    maximumAxle,
                    axleCenter + axleExtent);
            }
            if (float.IsPositiveInfinity(minimumUp))
                return;

            float requiredRadius =
                Mathf.Max(0f, -minimumUp) + 0.01f;
            float requiredHalfWidth = Mathf.Max(
                Mathf.Abs(minimumAxle),
                Mathf.Abs(maximumAxle));
            Profile.radius = Mathf.Max(
                Profile.radius,
                requiredRadius);
            Profile.width = Mathf.Max(
                Profile.width,
                requiredHalfWidth * 2f + 0.02f);
            Profile.suspensionTravel = Mathf.Max(
                Profile.suspensionTravel,
                Mathf.Clamp(
                    Profile.radius * 0.25f,
                    0.2f,
                    0.65f));
        }

        private void LateUpdate()
        {
            if (vehicleRoot == null)
                return;
            Vector3 localUp = transform.InverseTransformDirection(
                suspensionUpWorld).normalized;
            if (suspensionPivot != null)
            {
                float visualDroop = ResolveModuleVisualPose(
                    localUp,
                    out Quaternion bogieRotation);
                telemetry.visualDroop = visualDroop;
                // A negative droop raises the wheel to the recovered contact
                // plane instead of rendering it below the terrain.
                suspensionPivot.localPosition =
                    -localUp * visualDroop;
                suspensionPivot.localRotation = bogieRotation;
            }
            if (steeringPivot != null)
            {
                steeringPivot.localRotation = Quaternion.AngleAxis(
                    steerAngle,
                    localUp);
            }
            AdvanceRollingVisuals(Time.deltaTime);
        }

        private void AdvanceRollingVisuals(float deltaTime)
        {
            if (rollingVisuals.Length == 0 ||
                deltaTime <= 0f)
            {
                return;
            }

            for (int index = 0; index < rollingVisuals.Length; index++)
            {
                if (rollingVisuals[index] == null)
                    continue;
                ModularWheelRuntime spinOwner =
                    ResolveRollingVisualOwner(
                        index < rollingVisualTyreIndices.Length
                            ? rollingVisualTyreIndices[index]
                            : 0);
                float spinDelta =
                    spinOwner.wheelAngularSpeed *
                    spinOwner.visualAxleSign *
                    Mathf.Rad2Deg * deltaTime;
                rollingVisualBaseRotations[index] =
                    Quaternion.AngleAxis(
                        spinDelta,
                        rollingVisualAxes[index]) *
                    rollingVisualBaseRotations[index];
                rollingVisuals[index].localRotation =
                    rollingVisualBaseRotations[index];
            }
        }

        private ModularWheelRuntime ResolveRollingVisualOwner(
            int elementIndex)
        {
            if (tyreElementCount < 2)
                return this;
            if (moduleTyreElements.Length != tyreElementCount ||
                moduleTyreElements.Any(item =>
                    item == null ||
                    !item.enabled ||
                    !item.gameObject.activeInHierarchy))
            {
                RefreshModuleTyreElements();
            }
            for (int index = 0;
                 index < moduleTyreElements.Length;
                 index++)
            {
                ModularWheelRuntime candidate =
                    moduleTyreElements[index];
                if (candidate != null &&
                    candidate.TyreElementIndex == elementIndex)
                {
                    return candidate;
                }
            }
            return this;
        }

        private float ResolveModuleVisualPose(
            Vector3 localUp,
            out Quaternion bogieRotation)
        {
            bogieRotation = Quaternion.identity;
            float ownDroop = ResolveVisualDroop();
            if (tyreElementIndex != 0 ||
                tyreElementCount < 2)
            {
                return ownDroop;
            }

            if (moduleTyreElements.Length != tyreElementCount ||
                moduleTyreElements.Any(item =>
                    item == null ||
                    !item.enabled ||
                    !item.gameObject.activeInHierarchy))
            {
                RefreshModuleTyreElements();
            }
            if (moduleTyreElements.Length < 2)
                return ownDroop;

            Vector3 localAxle =
                transform.InverseTransformDirection(
                    baseAxleWorld).normalized;
            Vector3 localForward =
                transform.InverseTransformDirection(
                    baseForwardWorld).normalized;
            if (localAxle.sqrMagnitude < 0.5f ||
                localForward.sqrMagnitude < 0.5f)
            {
                return ownDroop;
            }

            float meanForward = 0f;
            float meanDroop = 0f;
            for (int index = 0;
                 index < moduleTyreElements.Length;
                 index++)
            {
                ModularWheelRuntime tyre =
                    moduleTyreElements[index];
                Vector3 localCenter =
                    transform.InverseTransformPoint(
                        tyre.NeutralWheelCenterWorld);
                meanForward += Vector3.Dot(
                    localCenter,
                    localForward);
                meanDroop += tyre.ResolveVisualDroop();
            }
            meanForward /= moduleTyreElements.Length;
            meanDroop /= moduleTyreElements.Length;

            float covariance = 0f;
            float longitudinalVariance = 0f;
            for (int index = 0;
                 index < moduleTyreElements.Length;
                 index++)
            {
                ModularWheelRuntime tyre =
                    moduleTyreElements[index];
                Vector3 localCenter =
                    transform.InverseTransformPoint(
                        tyre.NeutralWheelCenterWorld);
                float forwardOffset =
                    Vector3.Dot(
                        localCenter,
                        localForward) -
                    meanForward;
                covariance += forwardOffset *
                    (tyre.ResolveVisualDroop() - meanDroop);
                longitudinalVariance +=
                    forwardOffset * forwardOffset;
            }
            if (longitudinalVariance < 0.0001f)
                return ownDroop;

            float maximumSlope =
                Mathf.Sin(12f * Mathf.Deg2Rad);
            float slope = Mathf.Clamp(
                covariance / longitudinalVariance,
                -maximumSlope,
                maximumSlope);
            float bogieAngle =
                Mathf.Asin(slope) * Mathf.Rad2Deg;
            bogieRotation = Quaternion.AngleAxis(
                bogieAngle,
                localAxle);

            // Choose the largest common translation that never moves any
            // visible tyre below its independently solved physical centre.
            // With two tyres inside the angle limit this is the exact rigid
            // bogie fit; beyond the limit one tyre may hover slightly, but
            // neither can render through the terrain.
            float safeDroop = float.PositiveInfinity;
            for (int index = 0;
                 index < moduleTyreElements.Length;
                 index++)
            {
                ModularWheelRuntime tyre =
                    moduleTyreElements[index];
                Vector3 localCenter =
                    transform.InverseTransformPoint(
                        tyre.NeutralWheelCenterWorld);
                Vector3 rotatedCenter =
                    bogieRotation * localCenter;
                float rotationRise = Vector3.Dot(
                    rotatedCenter - localCenter,
                    localUp);
                safeDroop = Mathf.Min(
                    safeDroop,
                    tyre.ResolveVisualDroop() +
                    rotationRise);
            }
            return float.IsPositiveInfinity(safeDroop)
                ? ownDroop
                : safeDroop;
        }

        private void RefreshModuleTyreElements()
        {
            if (tyreElementCount < 2 ||
                ModuleRoot == null)
            {
                moduleTyreElements =
                    Array.Empty<ModularWheelRuntime>();
                return;
            }
            moduleTyreElements = ModuleRoot
                .GetComponentsInChildren<
                    ModularWheelRuntime>(false)
                .Where(item =>
                    item != null &&
                    item.enabled &&
                    item.gameObject.activeInHierarchy &&
                    item.ModuleRoot == ModuleRoot &&
                    item.TyreElementCount ==
                    tyreElementCount)
                .OrderBy(item => item.TyreElementIndex)
                .ToArray();
        }

        private void RefreshRollingVisualAxes()
        {
            rollingVisualAxes = new Vector3[rollingVisuals.Length];
            Vector3 worldAxle =
                ModuleRoot.TransformDirection(axleLocal);
            for (int index = 0; index < rollingVisuals.Length; index++)
            {
                Transform item = rollingVisuals[index];
                Transform parent = item != null ? item.parent : null;
                Vector3 localAxis = parent != null
                    ? parent.InverseTransformDirection(worldAxle)
                    : axleLocal;
                rollingVisualAxes[index] =
                    localAxis.sqrMagnitude > 0.0001f
                        ? localAxis.normalized
                        : Vector3.right;
            }
        }

        private int ResolveRollingVisualTyreIndex(Transform visual)
        {
            int namedIndex = Mathf.Clamp(
                RollingVisualNameOrder(
                    visual != null ? visual.name : null),
                0,
                Mathf.Max(0, tyreElementCount - 1));
            if (visual == null ||
                tyreElementCount < 2 ||
                !WheelModuleGeometryCatalog.TryResolve(
                    Profile.neoXId,
                    out WheelTyreGeometry[] tyres) ||
                tyres.Length < 2)
            {
                return namedIndex;
            }

            Vector3 localPosition =
                ModuleRoot.InverseTransformPoint(visual.position);
            int closestIndex = 0;
            float closestDistance = float.PositiveInfinity;
            for (int index = 0; index < tyres.Length; index++)
            {
                float distance = (
                    localPosition -
                    tyres[index].centerLocal).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;
                closestDistance = distance;
                closestIndex = index;
            }
            return closestIndex;
        }

        private static Vector3 ReadAxis(
            float[] values,
            Vector3 fallback)
        {
            if (values == null || values.Length < 3)
                return fallback;
            Vector3 value =
                new Vector3(values[0], values[1], values[2]);
            return value.sqrMagnitude > 0.0001f
                ? value.normalized
                : fallback;
        }

        private static bool IsRollingVisualName(string value)
        {
            string name = (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            return name == "wheel" ||
                   name == "wheel2" ||
                   name == "tire" ||
                   name == "tire2" ||
                   name == "tyre" ||
                   name == "tyre2" ||
                   name.StartsWith("wheel.") ||
                   name.StartsWith("tire.") ||
                   name.StartsWith("tyre.");
        }

        private static int RollingVisualNameOrder(string value)
        {
            string name = (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            return name == "wheel2" ||
                   name == "tire2" ||
                   name == "tyre2" ||
                   name.StartsWith("wheel2.") ||
                   name.StartsWith("tire2.") ||
                   name.StartsWith("tyre2.")
                ? 1
                : 0;
        }

        private void CapturePlacementColliders()
        {
            Collider[] current = GetComponents<Collider>();
            if (current.Length == placementColliders.Length &&
                current.SequenceEqual(placementColliders))
            {
                return;
            }
            placementColliders = current;
            placementColliderStates = current
                .Select(item => item != null && item.enabled)
                .ToArray();
        }

        private void OnDestroy()
        {
            if (penetrationProbeObject == null)
                return;
            if (Application.isPlaying)
                Destroy(penetrationProbeObject);
            else
                DestroyImmediate(penetrationProbeObject);
        }

        private static Transform CreatePivot(
            string pivotName,
            Transform parent)
        {
            GameObject value = new GameObject(pivotName);
            value.transform.SetParent(parent, false);
            return value.transform;
        }
    }
}
