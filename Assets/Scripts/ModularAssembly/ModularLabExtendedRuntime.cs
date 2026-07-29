using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UnityPlanet.ModularAssembly
{
    public enum LabEnvironmentKind
    {
        Ground,
        ZeroGravity,
        Atmosphere
    }

    public sealed class NeoXBehaviorModule : MonoBehaviour
    {
        [SerializeField] private string sourceId;
        [SerializeField] private GridModuleBehaviorKind behaviorKind;
        [SerializeField] private int weaponGroup = 1;
        [SerializeField] private float strength = 1f;
        [SerializeField] private float energyCapacity;
        [SerializeField] private float idleEnergy;
        [SerializeField] private string appearanceId;
        [SerializeField] private Vector3 mountNormalLocal = Vector3.forward;
        [SerializeField] private Vector3 functionalForwardLocal = Vector3.forward;
        [SerializeField] private Vector3 exhaustAxisLocal = Vector3.forward;
        [SerializeField] private string muzzleSocket;
        [SerializeField] private string exhaustSocket;
        private Transform logicalRoot;
        private bool hasCachedMuzzleFallback;
        private bool hasCachedExhaustFallback;
        private Vector3 cachedMuzzleFallbackLocal;
        private Vector3 cachedExhaustFallbackLocal;

        public string SourceId => sourceId;
        public GridModuleBehaviorKind BehaviorKind => behaviorKind;
        public int WeaponGroup => weaponGroup;
        public float Strength => Mathf.Max(0.01f, strength);
        public float EnergyCapacity => Mathf.Max(0f, energyCapacity);
        public float IdleEnergy => Mathf.Max(0f, idleEnergy);
        public string AppearanceId => appearanceId;
        public Vector3 WorldMountNormal =>
            Root.TransformDirection(mountNormalLocal).normalized;
        public Vector3 WorldMuzzleDirection =>
            Root.TransformDirection(functionalForwardLocal).normalized;
        public Vector3 WorldExhaustDirection =>
            Root.TransformDirection(exhaustAxisLocal).normalized;
        public Vector3 WorldMuzzlePosition =>
            ResolveSocketPosition(
                muzzleSocket,
                WorldMuzzleDirection,
                ref hasCachedMuzzleFallback,
                ref cachedMuzzleFallbackLocal);
        public Vector3 WorldExhaustPosition =>
            ResolveSocketPosition(
                exhaustSocket,
                WorldExhaustDirection,
                ref hasCachedExhaustFallback,
                ref cachedExhaustFallbackLocal);
        private Transform Root => logicalRoot != null ? logicalRoot : transform;

        public void Configure(ModularContentRecord record)
        {
            sourceId = record?.sourceId ?? string.Empty;
            behaviorKind = record?.BehaviorKind ?? GridModuleBehaviorKind.None;
            weaponGroup = Mathf.Clamp(weaponGroup, 1, 4);
            appearanceId = record != null && record.appearanceIds != null && record.appearanceIds.Length > 0
                ? record.appearanceIds[0]
                : string.Empty;
            mountNormalLocal = ReadAxis(record?.mountNormalLocal, Vector3.forward);
            functionalForwardLocal = ReadAxis(record?.functionalForwardLocal, Vector3.forward);
            exhaustAxisLocal = ReadAxis(record?.exhaustAxisLocal, Vector3.forward);
            muzzleSocket = record?.muzzleSocket ?? string.Empty;
            exhaustSocket = record?.exhaustSocket ?? string.Empty;
            ClearSocketFallbackCache();
            switch (behaviorKind)
            {
                case GridModuleBehaviorKind.Battery:
                case GridModuleBehaviorKind.Energy:
                    energyCapacity = string.Equals(
                        record.neoXId,
                        "fire_energy_storage_422",
                        StringComparison.OrdinalIgnoreCase) ? 200f : 50f;
                    break;
                case GridModuleBehaviorKind.Shield:
                case GridModuleBehaviorKind.Radar:
                case GridModuleBehaviorKind.Repair:
                    idleEnergy = 3f;
                    break;
            }
        }

        public void SetWeaponGroup(int group)
        {
            weaponGroup = Mathf.Clamp(group, 1, 4);
        }

        public void SetLogicalRoot(Transform value)
        {
            logicalRoot = value;
            ClearSocketFallbackCache();
        }

private Vector3 ResolveSocketPosition(
            string socketName,
            Vector3 direction,
            ref bool hasCachedFallback,
            ref Vector3 cachedFallbackLocal)
        {
            Transform socket = FindChild(transform, socketName);
            if (socket != null && IsFinite(socket.position))
            {
                return socket.position;
            }

            if (hasCachedFallback)
            {
                Vector3 cachedWorld = Root.TransformPoint(cachedFallbackLocal);
                if (IsFinite(cachedWorld))
                {
                    return cachedWorld;
                }
                hasCachedFallback = false;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            Bounds combined = default;
            bool hasBounds = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || renderer is ParticleSystemRenderer
                    || !IsFinite(renderer.transform.position)
                    || !IsFinite(renderer.transform.lossyScale))
                {
                    continue;
                }

                Bounds current = renderer.bounds;
                if (!IsFinite(current)
                    || current.extents.sqrMagnitude > 65536f)
                {
                    continue;
                }
                if (!hasBounds)
                {
                    combined = current;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(current);
                }
            }

            Vector3 normalizedDirection = direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Root.forward;
            Vector3 resolved;
            if (!hasBounds || !IsFinite(combined))
            {
                resolved = Root.position + normalizedDirection * 0.5f;
            }
            else
            {
                float radius =
                    Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.right))
                    * combined.extents.x
                    + Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.up))
                    * combined.extents.y
                    + Mathf.Abs(Vector3.Dot(normalizedDirection, Vector3.forward))
                    * combined.extents.z;
                resolved = combined.center
                           + normalizedDirection * Mathf.Max(0.05f, radius);
            }

            if (!IsFinite(resolved))
            {
                resolved = Root.position + normalizedDirection * 0.5f;
            }
            cachedFallbackLocal = Root.InverseTransformPoint(resolved);
            hasCachedFallback = IsFinite(cachedFallbackLocal);
            return resolved;
        }

        private void ClearSocketFallbackCache()
        {
            hasCachedMuzzleFallback = false;
            hasCachedExhaustFallback = false;
            cachedMuzzleFallbackLocal = Vector3.zero;
            cachedExhaustFallbackLocal = Vector3.zero;
        }

        private static bool IsFinite(Bounds value)
        {
            return IsFinite(value.center) && IsFinite(value.extents);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y)
                   && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Transform FindChild(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }

        private static Vector3 ReadAxis(float[] value, Vector3 fallback)
        {
            if (value == null || value.Length < 3)
            {
                return fallback;
            }
            Vector3 axis = new Vector3(value[0], value[1], value[2]);
            return axis.sqrMagnitude > 0.001f ? axis.normalized : fallback;
        }
    }

    public sealed class RuntimeEnergyBus : MonoBehaviour
    {
        [SerializeField] private float baseCapacity = 100f;
        [SerializeField] private float rechargePerSecond = 12f;
        private NeoXBehaviorModule[] modules = Array.Empty<NeoXBehaviorModule>();

        public float Capacity { get; private set; }
        public float Energy { get; private set; }
        public float Fraction => Capacity > 0f ? Energy / Capacity : 0f;

        private void Awake()
        {
            Rebuild();
        }

        private void Update()
        {
            float idle = modules.Sum(module => module != null ? module.IdleEnergy : 0f);
            Energy = Mathf.Clamp(Energy + (rechargePerSecond - idle) * Time.deltaTime, 0f, Capacity);
        }

        public void Rebuild()
        {
            modules = GetComponentsInChildren<NeoXBehaviorModule>(true);
            Capacity = baseCapacity + modules.Sum(module => module.EnergyCapacity);
            Energy = Capacity;
        }

        public bool TryConsume(float amount, int priority)
        {
            amount = Mathf.Max(0f, amount);
            if (Energy < amount)
            {
                return false;
            }
            Energy -= amount;
            return true;
        }
    }

    public sealed class LabEnvironmentBody : MonoBehaviour
    {
        [SerializeField] private Rigidbody body;
        [SerializeField] private LabEnvironmentKind environment;
        [SerializeField] private float airDensity = 1.225f;
        private VehicleMotionCoordinatorV2 motionV2;
        private VehicleMotionCoordinatorV3 motionV3;
        private RobocraftMotionCoordinator motionRc1;

        public LabEnvironmentKind Environment
        {
            get => environment;
            set => environment = value;
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }
            if (body != null)
            {
                body.useGravity = false;
            }
            motionV2 = GetComponent<VehicleMotionCoordinatorV2>();
            motionV3 = GetComponent<VehicleMotionCoordinatorV3>();
            motionRc1 = GetComponent<RobocraftMotionCoordinator>();
        }

        private void FixedUpdate()
        {
            if (body == null || body.isKinematic)
            {
                return;
            }
            if (motionV2 == null)
                motionV2 = GetComponent<VehicleMotionCoordinatorV2>();
            if (motionV2 != null && motionV2.IsActive)
                return;
            if (motionV3 == null)
                motionV3 = GetComponent<VehicleMotionCoordinatorV3>();
            if (motionV3 != null && motionV3.IsActive)
                return;
            if (motionRc1 == null)
                motionRc1 = GetComponent<RobocraftMotionCoordinator>();
            if (motionRc1 != null && motionRc1.IsActive)
                return;

            if (environment != LabEnvironmentKind.ZeroGravity)
            {
                body.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration);
            }

            if (environment == LabEnvironmentKind.Atmosphere)
            {
                Vector3 velocity = body.velocity;
                body.AddForce(-velocity * velocity.magnitude * airDensity * 0.012f, ForceMode.Acceleration);
                body.AddTorque(-body.angularVelocity * 0.45f, ForceMode.Acceleration);
            }
        }
    }

    public sealed class LabArcadeVehicleController : MonoBehaviour
    {
        private Rigidbody body;
        private RuntimeEnergyBus energyBus;
        private NeoXBehaviorModule[] modules = Array.Empty<NeoXBehaviorModule>();
        private LabEnvironmentBody environmentBody;
        private HybridVehicleModeController hybridController;
        private VehicleMotionCoordinatorV2 motionV2;
        private VehicleMotionCoordinatorV3 motionV3;
        private RobocraftMotionCoordinator motionRc1;
        private int activeWeaponGroup = 1;
        private float nextShot;

        public int ActiveWeaponGroup => activeWeaponGroup;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            energyBus = GetComponent<RuntimeEnergyBus>() ?? gameObject.AddComponent<RuntimeEnergyBus>();
            environmentBody = GetComponent<LabEnvironmentBody>() ?? gameObject.AddComponent<LabEnvironmentBody>();
            hybridController = GetComponent<HybridVehicleModeController>();
            motionV2 = GetComponent<VehicleMotionCoordinatorV2>();
            motionV3 = GetComponent<VehicleMotionCoordinatorV3>();
            motionRc1 = GetComponent<RobocraftMotionCoordinator>();
            Rebuild();
        }

        private void Update()
        {
            if (GetComponent<WeaponSystemCoordinator>() != null)
            {
                return;
            }
            for (int group = 1; group <= 4; group++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + group))
                {
                    activeWeaponGroup = group;
                }
            }

            if (Input.GetMouseButton(0))
            {
                FireWeaponGroup();
            }
        }

        private void FixedUpdate()
        {
            if (body == null || body.isKinematic)
            {
                return;
            }
            if (motionRc1 == null)
                motionRc1 = GetComponent<RobocraftMotionCoordinator>();
            if (motionRc1 != null && motionRc1.IsActive)
                return;
            if (motionV3 == null)
                motionV3 = GetComponent<VehicleMotionCoordinatorV3>();
            if (motionV3 != null && motionV3.IsActive)
                return;
            if (motionV2 == null)
                motionV2 = GetComponent<VehicleMotionCoordinatorV2>();
            if (motionV2 != null && motionV2.IsActive)
                return;
            if (hybridController == null)
            {
                hybridController = GetComponent<HybridVehicleModeController>();
            }
            if (hybridController != null &&
                hybridController.SuppressLegacyMovement)
            {
                return;
            }

            float forward = Input.GetAxisRaw("Vertical");
            float lateral = Input.GetAxisRaw("Horizontal");
            LabEnvironmentKind environment = environmentBody.Environment;
            if (environment == LabEnvironmentKind.Ground)
            {
                ApplyGroundDrive(forward, lateral);
            }
            else
            {
                ApplyFlightBehaviors(forward, lateral, environment);
            }
        }

        public void Rebuild()
        {
            modules = GetComponentsInChildren<NeoXBehaviorModule>(false);
            if (energyBus != null)
            {
                energyBus.Rebuild();
            }
        }

        public void SetEnvironment(LabEnvironmentKind environment)
        {
            if (environmentBody != null)
            {
                environmentBody.Environment = environment;
            }
        }

        private void ApplyGroundDrive(float forward, float steer)
        {
            int mobility = modules.Count(module => module != null && IsMobility(module.BehaviorKind));
            if (mobility == 0)
            {
                return;
            }

            Vector3 origin = body.worldCenterOfMass + Vector3.up * 0.25f;
            bool grounded = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.8f, ~0, QueryTriggerInteraction.Ignore);
            if (!grounded)
            {
                return;
            }

            float drive = 1600f * mobility;
            if (Mathf.Abs(forward) > 0.01f && energyBus.TryConsume(Mathf.Abs(forward) * Time.fixedDeltaTime, 0))
            {
                body.AddForce(transform.forward * forward * drive, ForceMode.Force);
            }
            body.AddTorque(Vector3.up * steer * 650f * mobility, ForceMode.Force);

            float springError = 1.1f - hit.distance;
            body.AddForceAtPosition(Vector3.up * springError * 1800f * mobility, hit.point, ForceMode.Force);
            if (Input.GetKey(KeyCode.Space))
            {
                body.AddForce(-body.velocity * 2.5f, ForceMode.Acceleration);
            }
        }

        private void ApplyFlightBehaviors(float forward, float lateral, LabEnvironmentKind environment)
        {
            Vector3 desiredLocalForce = new Vector3(
                lateral,
                (Input.GetKey(KeyCode.Space) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftControl) ? 1f : 0f),
                forward);
            Vector3 desiredLocalTorque = Vector3.zero;
            float boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? 1.75f
                : 1f;
            if (Input.GetKey(KeyCode.X))
            {
                body.AddForce(-body.velocity * 3.5f, ForceMode.Acceleration);
                body.AddTorque(-body.angularVelocity * 3.5f, ForceMode.Acceleration);
                desiredLocalForce = Vector3.zero;
            }
            Vector3 desiredForce = transform.TransformDirection(desiredLocalForce);
            Vector3 desiredTorque = transform.TransformDirection(desiredLocalTorque);
            foreach (NeoXBehaviorModule module in modules)
            {
                if (module == null ||
                    (module.BehaviorKind != GridModuleBehaviorKind.Thruster &&
                    module.BehaviorKind != GridModuleBehaviorKind.Hover)
                   )
                {
                    continue;
                }
                Vector3 axis = -module.WorldExhaustDirection;
                Vector3 forcePosition = module.WorldExhaustPosition;
                Vector3 arm = forcePosition - body.worldCenterOfMass;
                Vector3 torqueAxis = Vector3.Cross(arm, axis);
                float forceScore = desiredForce.sqrMagnitude > 0.001f
                    ? Mathf.Max(0f, Vector3.Dot(axis, desiredForce.normalized))
                    : 0f;
                float torqueScore = desiredTorque.sqrMagnitude > 0.001f && torqueAxis.sqrMagnitude > 0.001f
                    ? Mathf.Max(0f, Vector3.Dot(torqueAxis.normalized, desiredTorque.normalized))
                    : 0f;
                float throttle = Mathf.Clamp01(forceScore + torqueScore);
                if (throttle > 0.01f &&
                    energyBus.TryConsume(throttle * boost * Time.fixedDeltaTime * 1.5f, 0))
                {
                    body.AddForceAtPosition(
                        axis * 6000f * module.Strength * throttle * boost,
                        forcePosition,
                        ForceMode.Force);
                }
            }
            if (environment == LabEnvironmentKind.Atmosphere)
            {
                int wings = modules.Count(module =>
                    module != null &&
                    (module.BehaviorKind == GridModuleBehaviorKind.Wing ||
                     module.BehaviorKind == GridModuleBehaviorKind.ControlSurface));
                if (wings > 0)
                {
                    Vector3 localVelocity = transform.InverseTransformDirection(body.velocity);
                    float lift = Mathf.Clamp(localVelocity.z * localVelocity.z * 0.8f * wings, 0f, body.mass * 35f);
                    body.AddForce(transform.up * lift, ForceMode.Force);
                    body.AddTorque(transform.forward * -lateral * 500f * wings, ForceMode.Force);
                }
                if (desiredLocalTorque.sqrMagnitude < 0.01f)
                {
                    Vector3 correction = Vector3.Cross(transform.up, Vector3.up);
                    body.AddTorque(correction * body.mass * 5f - body.angularVelocity * body.mass * 0.6f, ForceMode.Force);
                }
            }
        }

        private void FireWeaponGroup()
        {
            if (Time.time < nextShot)
            {
                return;
            }

            NeoXBehaviorModule[] weapons = modules.Where(module =>
                module.WeaponGroup == activeWeaponGroup && IsWeapon(module.BehaviorKind)).ToArray();
            if (weapons.Length == 0)
            {
                return;
            }

            float cadence = weapons.Any(module => module.BehaviorKind == GridModuleBehaviorKind.Gatling) ? 0.08f : 0.22f;
            nextShot = Time.time + cadence;
            foreach (NeoXBehaviorModule weapon in weapons)
            {
                if (!energyBus.TryConsume(IsEnergyWeapon(weapon.BehaviorKind) ? 8f : 0.5f, 1))
                {
                    continue;
                }

                GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                projectile.name = "NeoXProjectile";
                Vector3 muzzleDirection = weapon.WorldMuzzleDirection;
                Vector3 muzzlePosition = weapon.WorldMuzzlePosition;
                projectile.transform.position = muzzlePosition;
                projectile.transform.localScale = Vector3.one * 0.16f;
                Rigidbody projectileBody = projectile.AddComponent<Rigidbody>();
                projectileBody.useGravity = environmentBody.Environment == LabEnvironmentKind.Ground;
                projectileBody.mass = 0.5f;
                projectileBody.velocity = body.velocity + muzzleDirection * ProjectileSpeed(weapon.BehaviorKind);
                NeoXProjectileDamage damage = projectile.AddComponent<NeoXProjectileDamage>();
                damage.damage = weapon.BehaviorKind == GridModuleBehaviorKind.SniperCannon ? 120f :
                    weapon.BehaviorKind == GridModuleBehaviorKind.Cannon ? 80f : 35f;
                damage.explosionRadius =
                    weapon.BehaviorKind == GridModuleBehaviorKind.Rocket ||
                    weapon.BehaviorKind == GridModuleBehaviorKind.GuidedMissile ||
                    weapon.BehaviorKind == GridModuleBehaviorKind.Mortar ||
                    weapon.BehaviorKind == GridModuleBehaviorKind.Bomb ? 5f : 0f;
                damage.source = gameObject;
                Destroy(projectile, 8f);
                RobocraftMotionCoordinator rc1 =
                    GetComponent<RobocraftMotionCoordinator>();
                if (rc1 != null && rc1.IsActive)
                {
                    rc1.QueueVisualRecoil(
                        -muzzleDirection * 250f,
                        muzzlePosition);
                }
                else
                {
                    VehicleMotionCoordinatorV3 motionV3 =
                        GetComponent<VehicleMotionCoordinatorV3>();
                    if (motionV3 != null && motionV3.IsActive)
                    {
                        motionV3.QueueImpulseAtPosition(
                            -muzzleDirection * 250f,
                            muzzlePosition);
                    }
                    else if (motionV2 != null && motionV2.IsActive)
                    {
                        motionV2.QueueImpulseAtPosition(
                            -muzzleDirection * 250f,
                            muzzlePosition);
                    }
                    else
                    {
                        body.AddForceAtPosition(
                            -muzzleDirection * 250f,
                            muzzlePosition,
                            ForceMode.Impulse);
                    }
                }
            }
        }

        private static float ProjectileSpeed(GridModuleBehaviorKind kind)
        {
            switch (kind)
            {
                case GridModuleBehaviorKind.Mortar:
                case GridModuleBehaviorKind.Bomb:
                    return 55f;
                case GridModuleBehaviorKind.Rocket:
                case GridModuleBehaviorKind.GuidedMissile:
                    return 90f;
                case GridModuleBehaviorKind.SniperCannon:
                case GridModuleBehaviorKind.Laser:
                    return 320f;
                default:
                    return 180f;
            }
        }

        private static bool IsMobility(GridModuleBehaviorKind kind)
        {
            return kind == GridModuleBehaviorKind.Wheel ||
                   kind == GridModuleBehaviorKind.Track ||
                   kind == GridModuleBehaviorKind.Leg ||
                   kind == GridModuleBehaviorKind.Hover;
        }

        private static bool IsEnergyWeapon(GridModuleBehaviorKind kind)
        {
            return kind == GridModuleBehaviorKind.EnergyCannon ||
                   kind == GridModuleBehaviorKind.Laser ||
                   kind == GridModuleBehaviorKind.Flame;
        }

        private static bool IsWeapon(GridModuleBehaviorKind kind)
        {
            return kind >= GridModuleBehaviorKind.KineticRapid &&
                   kind <= GridModuleBehaviorKind.Saw;
        }
    }

    public sealed class LabZoneManager : MonoBehaviour
    {
        public static readonly Vector3 GroundCenter = new Vector3(-1000f, 0f, 0f);
        public static readonly Vector3 ZeroGravityCenter = Vector3.zero;
        public static readonly Vector3 AtmosphereCenter = new Vector3(1000f, 20f, 0f);

        private Rigidbody playerBody;
        private LabArcadeVehicleController vehicleController;
        private SpacecraftEditor.SpacecraftIfcsMotor flightMotor;
        private PlanetLabFlightEnvironmentController flightEnvironment;
        public LabEnvironmentKind Current { get; private set; }
        public PlanetLabFlightEnvironmentController FlightEnvironment =>
            flightEnvironment;

        public void Initialize()
        {
            Physics.gravity = Vector3.zero;
            Current = LabEnvironmentKind.Atmosphere;
            flightEnvironment =
                GetComponent<PlanetLabFlightEnvironmentController>()
                ?? gameObject.AddComponent<
                    PlanetLabFlightEnvironmentController>();
            flightEnvironment.Initialize();
        }

        private void Update()
        {
            if (playerBody == null)
            {
                AcquirePlayer();
                return;
            }

        }

        public void Teleport(LabEnvironmentKind environment)
        {
            AcquirePlayer();
            Current = environment;
            if (playerBody == null)
            {
                return;
            }

            playerBody.velocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.position = CenterFor(environment) + SpawnOffset(environment);
            playerBody.rotation = Quaternion.identity;
            vehicleController?.SetEnvironment(environment);
        }

        public Vector3 PrepareFlightSpawn(Rigidbody target)
        {
            Current = Current;
            if (target == null)
            {
                return CenterFor(Current) + SpawnOffset(Current);
            }

            playerBody = target;
            vehicleController = playerBody.GetComponent<LabArcadeVehicleController>() ??
                                playerBody.gameObject.AddComponent<LabArcadeVehicleController>();
            vehicleController.SetEnvironment(Current);
            flightMotor = playerBody.GetComponent<SpacecraftEditor.SpacecraftIfcsMotor>();
            Vector3 offset = SpawnOffset(Current);
            if (Current != LabEnvironmentKind.Atmosphere)
            {
                offset.y = Mathf.Max(offset.y, VerticalClearance(playerBody));
            }
            Vector3 position = CenterFor(Current) + offset;
            playerBody.velocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            playerBody.position = position;
            playerBody.rotation = Quaternion.identity;
            return position;
        }

        private static float VerticalClearance(Rigidbody target)
        {
            float clearance = 4f;
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            bool initialized = false;
            Bounds bounds = new Bounds();
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }
                if (!initialized)
                {
                    bounds = collider.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            if (initialized)
            {
                clearance = Mathf.Max(clearance, bounds.extents.y + 2f);
            }
            return clearance;
        }

        private void AcquirePlayer()
        {
            MonoBehaviour ifcs = FindObjectsOfType<MonoBehaviour>()
                .FirstOrDefault(component => component != null && component.GetType().Name == "SpacecraftIfcsMotor");
            playerBody = ifcs != null ? ifcs.GetComponentInParent<Rigidbody>() : null;
            if (playerBody == null)
            {
                playerBody = FindObjectsOfType<Rigidbody>()
                    .Where(candidate => !candidate.isKinematic && !candidate.name.Contains("Projectile"))
                    .OrderByDescending(candidate => candidate.mass)
                    .FirstOrDefault();
            }
            if (playerBody == null)
            {
                return;
            }

            vehicleController = playerBody.GetComponent<LabArcadeVehicleController>() ??
                                playerBody.gameObject.AddComponent<LabArcadeVehicleController>();
            vehicleController.SetEnvironment(Current);
            flightMotor = playerBody.GetComponent<SpacecraftEditor.SpacecraftIfcsMotor>();
        }

        private static Vector3 CenterFor(LabEnvironmentKind environment)
        {
            switch (environment)
            {
                case LabEnvironmentKind.ZeroGravity:
                    return ZeroGravityCenter;
                case LabEnvironmentKind.Atmosphere:
                    return AtmosphereCenter;
                default:
                    return GroundCenter;
            }
        }

        private static Vector3 SpawnOffset(LabEnvironmentKind environment)
        {
            return environment == LabEnvironmentKind.Atmosphere
                ? new Vector3(0f, 80f, 0f)
                : new Vector3(0f, 4f, 0f);
        }

        private static void BuildGroundZone()
        {
            Transform root = NewRoot("GroundTestZone", GroundCenter);
            CreateBox("GroundTrack", root, new Vector3(120f, 1f, 40f), new Vector3(0f, -0.5f, 0f), new Color(0.18f, 0.2f, 0.22f));
            CreateBox("Ramp", root, new Vector3(20f, 1f, 14f), new Vector3(32f, 3f, 0f), new Color(0.3f, 0.34f, 0.36f), Quaternion.Euler(0f, 0f, -16f));
            for (int index = 0; index < 5; index++)
            {
                CreateBox("Step_" + index, root, new Vector3(6f, index + 1f, 12f), new Vector3(-24f + index * 6f, (index + 1f) * 0.5f, 0f), new Color(0.32f, 0.28f, 0.2f));
            }
        }

        private static void BuildZeroGravityZone()
        {
            Transform root = NewRoot("ZeroGravityTestZone", ZeroGravityCenter);
            CreateBox("ZeroGPlatform", root, new Vector3(80f, 1f, 50f), new Vector3(0f, -0.5f, 0f), new Color(0.08f, 0.16f, 0.22f));
            for (int index = 0; index < 8; index++)
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "ZeroGMarker_" + index;
                marker.transform.SetParent(root, false);
                marker.transform.localPosition = Quaternion.Euler(0f, index * 45f, 0f) * new Vector3(35f, 8f + index, 0f);
                marker.transform.localScale = Vector3.one * 1.5f;
                UnityEngine.Object.Destroy(marker.GetComponent<Collider>());
            }
        }

        private static void BuildAtmosphereZone()
        {
            Transform root = NewRoot("AtmosphereTestZone", AtmosphereCenter);
            CreateBox("Runway", root, new Vector3(240f, 1f, 28f), new Vector3(0f, -0.5f, 0f), new Color(0.14f, 0.15f, 0.17f));
            for (int index = 0; index < 5; index++)
            {
                GameObject ring = new GameObject("FlightRing_" + index);
                ring.transform.SetParent(root, false);
                ring.transform.localPosition = new Vector3(index * 35f - 70f, 40f + index * 12f, 0f);
                LineRenderer line = ring.AddComponent<LineRenderer>();
                line.loop = true;
                line.useWorldSpace = false;
                line.widthMultiplier = 0.45f;
                line.positionCount = 32;
                for (int point = 0; point < 32; point++)
                {
                    float angle = point * Mathf.PI * 2f / 32f;
                    line.SetPosition(point, new Vector3(0f, Mathf.Sin(angle) * 10f, Mathf.Cos(angle) * 10f));
                }
            }
        }

        private static Transform NewRoot(string name, Vector3 position)
        {
            GameObject root = new GameObject(name);
            root.transform.position = position;
            return root.transform;
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 scale, Vector3 localPosition, Color color, Quaternion? rotation = null)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = scale;
            box.transform.localRotation = rotation ?? Quaternion.identity;
            box.GetComponent<Renderer>().material.color = color;
            return box;
        }
    }

    public sealed class ModularLabCatalogOverlay : MonoBehaviour
    {
        private ModularContentService contentService;
        private LabZoneManager zoneManager;
        private NeoXCatalogIntegration integration;
        private InputField search;
        private Text status;
        private GameObject catalogPanel;
        private readonly List<Button> rows = new List<Button>();
        private int page;

        public void Initialize(ModularContentService service, LabZoneManager zones)
        {
            contentService = service;
            zoneManager = zones;
            BuildUi();
            StartCoroutine(RefreshWhenReady());
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                ToggleCatalog();
            }
        }

        private IEnumerator RefreshWhenReady()
        {
            while (!contentService.IsReady)
            {
                yield return null;
            }
            integration = gameObject.AddComponent<NeoXCatalogIntegration>();
            integration.Initialize(contentService, zoneManager);
            RefreshRows();
        }

        private void BuildUi()
        {
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("NeoXCatalogCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            catalogPanel = new GameObject("NeoXCatalogPanel", typeof(RectTransform), typeof(Image));
            catalogPanel.transform.SetParent(canvas.transform, false);
            RectTransform rect = catalogPanel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(760f, 640f);
            catalogPanel.GetComponent<Image>().color = new Color(0.025f, 0.055f, 0.075f, 0.97f);

            Text title = CreateText(catalogPanel.transform, new Vector2(16f, -10f), new Vector2(360f, 38f), 22);
            title.text = "NeoX 模块目录";
            CreateButton(catalogPanel.transform, "空战模块", new Vector2(424f, -10f), () => { page = 0; RefreshRows(); });
            search = CreateInput(catalogPanel.transform, new Vector2(16f, -54f), new Vector2(728f, 36f));
            search.placeholder.GetComponent<Text>().text = "搜索中文名或 NeoX ID";
            search.onValueChanged.AddListener(_ => { page = 0; RefreshRows(); });

            for (int index = 0; index < 12; index++)
            {
                GameObject rowObject = CreateButton(
                    catalogPanel.transform,
                    string.Empty,
                    new Vector2(16f, -100f - index * 36f),
                    () => { });
                RectTransform rowRect = rowObject.GetComponent<RectTransform>();
                rowRect.sizeDelta = new Vector2(728f, 32f);
                rows.Add(rowObject.GetComponent<Button>());
            }

            CreateButton(catalogPanel.transform, "上一页", new Vector2(16f, -544f), () => { page = Mathf.Max(0, page - 1); RefreshRows(); });
            CreateButton(catalogPanel.transform, "下一页", new Vector2(176f, -544f), () => { page++; RefreshRows(); });
            CreateButton(catalogPanel.transform, "关闭 F2", new Vector2(594f, -10f), ToggleCatalog);
            status = CreateText(catalogPanel.transform, new Vector2(16f, -588f), new Vector2(728f, 40f), 13);

            GameObject zoneBar = new GameObject("NeoXZoneBar", typeof(RectTransform), typeof(Image));
            zoneBar.transform.SetParent(canvas.transform, false);
            RectTransform zoneRect = zoneBar.GetComponent<RectTransform>();
            zoneRect.anchorMin = zoneRect.anchorMax = new Vector2(0.5f, 0f);
            zoneRect.pivot = new Vector2(0.5f, 0f);
            zoneRect.anchoredPosition = new Vector2(0f, 12f);
            zoneRect.sizeDelta = new Vector2(180f, 48f);
            zoneBar.GetComponent<Image>().color = new Color(0.025f, 0.055f, 0.075f, 0.92f);
            zoneBar.SetActive(false);

            GameObject toggle = CreateButton(canvas.transform, "NeoX目录 F2", Vector2.zero, ToggleCatalog);
            RectTransform toggleRect = toggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(0.5f, 1f);
            toggleRect.pivot = new Vector2(0.5f, 1f);
            toggleRect.anchoredPosition = new Vector2(330f, -58f);
            catalogPanel.SetActive(false);
        }

        private void ToggleCatalog()
        {
            if (catalogPanel != null)
            {
                catalogPanel.SetActive(!catalogPanel.activeSelf);
                if (catalogPanel.activeSelf)
                {
                    RefreshRows();
                }
            }
        }

        private void RefreshRows()
        {
            if (!contentService.IsReady)
            {
                return;
            }
            IReadOnlyList<ModularContentRecord> searched = contentService.Catalog.Search(
                search.text,
                modulesOnly: true,
                take: 64,
                skip: page * 12);
            IReadOnlyList<ModularContentRecord> items = searched
                .Where(IsAirCombatRecord)
                .Take(12)
                .ToArray();
            for (int index = 0; index < rows.Count; index++)
            {
                Button row = rows[index];
                Text rowText = row.GetComponentInChildren<Text>();
                row.onClick.RemoveAllListeners();
                if (index < items.Count)
                {
                    ModularContentRecord record = items[index];
                    rowText.text = $"{record.chineseName}  [{record.neoXId}]    {record.category} / {record.behavior}";
                    row.gameObject.SetActive(true);
                    row.onClick.AddListener(() =>
                    {
                        integration?.Select(record);
                        catalogPanel.SetActive(false);
                    });
                }
                else
                {
                    rowText.text = string.Empty;
                    row.gameObject.SetActive(false);
                }
            }
            status.text = $"空战模块目录 | block 源模型 {contentService.Catalog.ModuleSourceCount} | 第 {page + 1} 页";
            if (!string.IsNullOrEmpty(contentService.LastError))
            {
                status.text += "\n" + contentService.LastError;
            }
        }

        private static bool IsAirCombatRecord(ModularContentRecord record)
        {
            GridModuleBehaviorKind kind = record.BehaviorKind;
            return kind != GridModuleBehaviorKind.Wheel &&
                   kind != GridModuleBehaviorKind.Track &&
                   kind != GridModuleBehaviorKind.Leg &&
                   record.IsGridPlaceable &&
                   record.IsModule;
        }

        private static InputField CreateInput(Transform parent, Vector2 position, Vector2 size)
        {
            GameObject root = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(InputField));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            root.GetComponent<Image>().color = new Color(0.1f, 0.16f, 0.19f, 1f);
            Text text = CreateText(root.transform, new Vector2(8f, -5f), new Vector2(size.x - 16f, size.y - 8f), 15);
            Text placeholder = CreateText(root.transform, new Vector2(8f, -5f), new Vector2(size.x - 16f, size.y - 8f), 15);
            placeholder.color = new Color(0.65f, 0.7f, 0.72f, 1f);
            InputField input = root.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            return input;
        }

        private static Text CreateText(Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject root = new GameObject("Text", typeof(RectTransform), typeof(Text));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Text text = root.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static GameObject CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction clicked)
        {
            GameObject root = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(150f, 36f);
            root.GetComponent<Image>().color = new Color(0.1f, 0.42f, 0.55f, 1f);
            root.GetComponent<Button>().onClick.AddListener(clicked);
            Text text = CreateText(root.transform, Vector2.zero, rect.sizeDelta, 15);
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;
            return root;
        }
    }

    public static class ModularLabExtensionBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.name, "ModularAssemblyLab", StringComparison.Ordinal))
            {
                return;
            }
            if (UnityEngine.Object.FindObjectOfType<ModularContentService>() != null)
            {
                return;
            }

            GameObject root = new GameObject("NeoXModularLabExtension");
            ModularContentService service = root.AddComponent<ModularContentService>();
            LabZoneManager zones = root.AddComponent<LabZoneManager>();
            ModularLabCatalogOverlay overlay = root.AddComponent<ModularLabCatalogOverlay>();
            zones.Initialize();
            root.GetComponent<MonoBehaviour>().StartCoroutine(service.Initialize());
            overlay.Initialize(service, zones);
        }
    }
}
