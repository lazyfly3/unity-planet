using System;
using System.Linq;
using UnityEngine;

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
        public float radius = 0.48f;
        public float width = 0.42f;
        public float massKg = 60f;
        public float maximumSteerAngle = 35f;
        public float maximumSpeed = 35f;
        public float suspensionTravel = 0.24f;
        public float longitudinalFriction = 1.35f;
        public float lateralFriction = 1.6f;
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
                result.radius = 0.92f;
                result.width = 0.72f;
                result.massKg = 120f;
                result.maximumSteerAngle = 32f;
                result.maximumSpeed = 45f;
            }
            else if (Contains(id, "wheel_l_422"))
            {
                result.radius = 1.35f;
                result.width = 0.95f;
                result.massKg = 220f;
                result.maximumSteerAngle = 26f;
                result.maximumSpeed = 40f;
            }
            else if (Contains(id, "speedwheel_small"))
            {
                result.radius = 1.02f;
                result.width = 0.76f;
                result.massKg = 100f;
                result.maximumSteerAngle = 28f;
                result.maximumSpeed = 65f;
                result.racingFront = true;
            }
            else if (Contains(id, "speedwheel_large"))
            {
                result.radius = 1.34f;
                result.width = 1f;
                result.massKg = 180f;
                result.maximumSteerAngle = 0f;
                result.maximumSpeed = 65f;
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

    public sealed class ModularWheelRuntime : MonoBehaviour
    {
        private readonly RaycastHit[] hits = new RaycastHit[24];
        private Collider[] placementColliders = Array.Empty<Collider>();
        private bool[] placementColliderStates = Array.Empty<bool>();
        private Rigidbody body;
        private Transform vehicleRoot;
        private Transform suspensionPivot;
        private Transform steeringPivot;
        private Transform[] rollingVisuals = Array.Empty<Transform>();
        private Quaternion[] rollingVisualBaseRotations =
            Array.Empty<Quaternion>();
        private WheelRoleOverride configuredRole;
        private WheelRoleOverride automaticRole;
        private RaycastHit contact;
        private bool grounded;
        private float compression;
        private float longitudinalSpeed;
        private float steerAngle;
        private float spinAngle;
        private float smoothedThrottle;
        private Vector3 smoothedContactNormal = Vector3.up;

        public WheelModuleProfile Profile { get; private set; } =
            WheelModuleProfile.ForNeoXId(string.Empty);
        public bool IsGrounded => grounded;
        public float Compression => compression;
        public Vector3 ContactPoint =>
            grounded ? contact.point : transform.position;
        float assignedSprungMass;
        float filteredNormalForce;
        public WheelRoleOverride ConfiguredRole => configuredRole;
        public WheelRoleOverride EffectiveRole =>
            configuredRole == WheelRoleOverride.Auto
                ? automaticRole
                : configuredRole;

        public void SetSprungMass(float value)
        {
            assignedSprungMass = Mathf.Max(0f, value);
        }

        public void Configure(
            ModularContentRecord record,
            string behaviorSettings)
        {
            Profile = WheelModuleProfile.ForNeoXId(record?.neoXId);
            configuredRole = WheelRoleSettings.Parse(behaviorSettings);
            CapturePlacementColliders();
        }

        public void BindVisual(Transform visual)
        {
            if (visual == null)
                return;
            suspensionPivot = suspensionPivot != null
                ? suspensionPivot
                : CreatePivot("WheelSuspension", transform);
            steeringPivot = steeringPivot != null
                ? steeringPivot
                : CreatePivot("WheelSteering", suspensionPivot);
            visual.SetParent(steeringPivot, false);

            rollingVisuals = visual
                .GetComponentsInChildren<Transform>(true)
                .Where(item => item != visual && IsRollingVisualName(item.name))
                .ToArray();
            rollingVisualBaseRotations = rollingVisuals
                .Select(item => item.localRotation)
                .ToArray();
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
                compression = 0f;
                steerAngle = 0f;
                smoothedThrottle = 0f;
            }
        }

        public bool ProbeContact(Vector3 up)
        {
            bool wasGrounded = grounded;
            grounded = false;
            if (body == null)
                return false;

            float travel = Profile.suspensionTravel;
            Vector3 origin = transform.position + up * travel;
            float radius = Mathf.Max(0.12f, Profile.radius - 0.02f);
            int count = Physics.SphereCastNonAlloc(
                origin,
                radius,
                -up,
                hits,
                travel * 2f + 0.35f,
                ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int index = 0; index < count; index++)
            {
                RaycastHit value = hits[index];
                if (value.collider == null ||
                    value.collider.attachedRigidbody == body ||
                    value.collider.transform.IsChildOf(body.transform) ||
                    Vector3.Dot(value.normal, up) < 0.2f)
                {
                    continue;
                }
                if (value.distance < nearest)
                {
                    nearest = value.distance;
                    contact = value;
                    grounded = true;
                }
            }
            float targetCompression = grounded
                ? Mathf.Clamp01(
                    2f - nearest /
                    Mathf.Max(0.01f, travel))
                : 0f;
            compression = Mathf.MoveTowards(
                compression,
                targetCompression,
                8f * Time.fixedDeltaTime);
            if (grounded)
            {
                smoothedContactNormal = wasGrounded
                    ? Vector3.Slerp(
                        smoothedContactNormal,
                        contact.normal,
                        1f - Mathf.Exp(-18f * Time.fixedDeltaTime)).normalized
                    : contact.normal;
            }
            return grounded;
        }

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
            if (!grounded || body == null)
                return;

            Vector3 normal =
                Vector3.Slerp(up, smoothedContactNormal, 0.65f).normalized;
            float wheelMass = assignedSprungMass > 0.01f
                ? assignedSprungMass
                : supportedMass / Mathf.Max(1, groundedWheelCount);
            float travel = Mathf.Max(0.05f, Profile.suspensionTravel);
            float spring = wheelMass * 9.81f / (travel * 0.55f);
            float damping = 2f * 0.8f *
                            Mathf.Sqrt(Mathf.Max(
                                0.01f,
                                spring * wheelMass));
            Vector3 pointVelocity = body.GetPointVelocity(contact.point);
            float compressionDistance = compression * travel;
            float suspensionSpeed = Vector3.Dot(pointVelocity, normal);
            float rawNormalForce = Mathf.Clamp(
                spring * compressionDistance - damping * suspensionSpeed,
                0f,
                wheelMass * 9.81f * 2.5f);
            filteredNormalForce = Mathf.MoveTowards(
                filteredNormalForce,
                rawNormalForce,
                wheelMass * 9.81f * 12f * Time.fixedDeltaTime);
            float normalForce = filteredNormalForce;
            if (forceLedger != null)
            {
                forceLedger.AddForceAtPosition(
                    normal * normalForce,
                    contact.point);
            }

            WheelRoleOverride role = EffectiveRole;
            bool steers = role == WheelRoleOverride.SteerDrive;
            bool drives = role == WheelRoleOverride.SteerDrive ||
                          role == WheelRoleOverride.DriveOnly;
            steerAngle = Mathf.MoveTowards(
                steerAngle,
                steers ? steering * Profile.maximumSteerAngle : 0f,
                120f * Time.fixedDeltaTime);

            Vector3 forward = Vector3.ProjectOnPlane(
                vehicleForward,
                normal).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.ProjectOnPlane(
                    vehicleRoot != null
                        ? vehicleRoot.up
                        : Vector3.forward,
                    normal).normalized;
            forward = Quaternion.AngleAxis(steerAngle, normal) * forward;
            Vector3 right = Vector3.Cross(normal, forward).normalized;
            longitudinalSpeed = Vector3.Dot(pointVelocity, forward);
            float lateralSpeed = Vector3.Dot(pointVelocity, right);
            float surfaceGrip = ResolveSurfaceGrip(contact.collider);
            float grip = Mathf.Max(0f, normalForce) * surfaceGrip;

            float driveForce = 0f;
            smoothedThrottle = Mathf.MoveTowards(
                smoothedThrottle,
                throttle,
                2.8f * Time.fixedDeltaTime);
            if (drives &&
                (Mathf.Abs(longitudinalSpeed) < Profile.maximumSpeed ||
                 Mathf.Sign(smoothedThrottle) != Mathf.Sign(longitudinalSpeed)))
            {
                float acceleration = boosting ? 12f : 8f;
                driveForce = smoothedThrottle * wheelMass * acceleration;
            }
            if (braking && role != WheelRoleOverride.FreeRolling)
                driveForce += -longitudinalSpeed * wheelMass * 9f;
            else if (!drives || Mathf.Abs(smoothedThrottle) < 0.01f)
            {
                float rollingResistance = Mathf.Min(
                    Mathf.Abs(longitudinalSpeed) * wheelMass * 0.45f,
                    grip * 0.035f);
                driveForce -= Mathf.Sign(longitudinalSpeed)
                              * rollingResistance;
            }
            float maximumLongitudinal =
                grip * Profile.longitudinalFriction;
            float maximumLateral =
                grip * Profile.lateralFriction;
            driveForce = Mathf.Clamp(
                driveForce,
                -maximumLongitudinal,
                maximumLongitudinal);
            float lateralForce = Mathf.Clamp(
                -lateralSpeed * wheelMass * 10f,
                -maximumLateral,
                maximumLateral);
            float frictionUsage = Mathf.Sqrt(
                Mathf.Pow(
                    driveForce /
                    Mathf.Max(0.01f, maximumLongitudinal),
                    2f)
                + Mathf.Pow(
                    lateralForce /
                    Mathf.Max(0.01f, maximumLateral),
                    2f));
            if (frictionUsage > 1f)
            {
                driveForce /= frictionUsage;
                lateralForce /= frictionUsage;
            }
            Vector3 longitudinalForce = forward * driveForce;
            Vector3 lateralGripForce = right * lateralForce;
            Vector3 assistedDrivePoint = Vector3.Lerp(
                contact.point,
                body.worldCenterOfMass,
                0.72f);
            if (forceLedger != null)
            {
                forceLedger.AddForceAtPosition(
                    longitudinalForce,
                    assistedDrivePoint);
                forceLedger.AddForceAtPosition(
                    lateralGripForce,
                    contact.point);
            }
        }

        private static float ResolveSurfaceGrip(Collider collider)
        {
            if (collider == null || collider.sharedMaterial == null)
                return 1f;
            PhysicMaterial material = collider.sharedMaterial;
            float sourceFriction = Mathf.Max(
                material.dynamicFriction,
                material.staticFriction * 0.85f);
            return Mathf.Clamp(sourceFriction / 0.6f, 0.25f, 1.75f);
        }

        private void LateUpdate()
        {
            if (vehicleRoot == null)
                return;
            Vector3 localUp = transform.InverseTransformDirection(
                vehicleRoot.up).normalized;
            if (suspensionPivot != null)
            {
                suspensionPivot.localPosition =
                    -localUp * (1f - compression) *
                    Profile.suspensionTravel;
            }
            if (steeringPivot != null)
            {
                steeringPivot.localRotation = Quaternion.AngleAxis(
                    steerAngle,
                    localUp);
            }
            if (rollingVisuals.Length > 0)
            {
                spinAngle += longitudinalSpeed /
                             Mathf.Max(0.05f, Profile.radius) *
                             Mathf.Rad2Deg * Time.deltaTime;
                Quaternion rotation = Quaternion.AngleAxis(
                    spinAngle,
                    Vector3.right);
                for (int index = 0; index < rollingVisuals.Length; index++)
                {
                    if (rollingVisuals[index] != null)
                    {
                        rollingVisuals[index].localRotation =
                            rollingVisualBaseRotations[index] * rotation;
                    }
                }
            }
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
