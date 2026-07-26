using UnityEngine;

namespace SpacecraftEditor
{
    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SpacecraftIfcsMotor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Rigidbody shipBody;
        [SerializeField] ShipAssembly assembly;
        [SerializeField] ShipHullController hullController;
        [SerializeField] MonoBehaviour flightCommandSource;

        [Header("Allocation")]
        [SerializeField, Min(0.1f)] float torqueWeight = 1.35f;
        [SerializeField] bool allowDirectMode = true;
        [SerializeField] SpacecraftAssistMode assistMode = SpacecraftAssistMode.Coupled;

        [Header("Planetary Flight")]
        [SerializeField, Min(1f)] float maximumGravitySupportAcceleration = 20f;
        [SerializeField, Min(0.1f)] float planetaryClimbSpeed = 10f;
        [SerializeField, Min(0.1f)] float planetaryDescentSpeed = 8f;
        [SerializeField, Min(0.1f)] float lowAltitudeClimbSpeed = 6f;
        [SerializeField, Min(0.1f)] float planetaryStrafeSpeed = 20f;
        [SerializeField, Min(1f)] float comfortAccelerationLimit = 35f;

        readonly SpacecraftThrusterAllocator allocator = new SpacecraftThrusterAllocator();
        ShipFlightProfile profile;
        float targetSpeed;
        float boostSeconds;
        Vector3 previousWorldVelocity;
        Vector3 measuredWorldAcceleration;
        bool hasVelocitySample;
        bool controlsEnabled;
        ISpacecraftFlightCommandSource commandSource;
        ISpacecraftPilotControlSource pilotControls;
        bool hasExternalWorldVelocity;
        Vector3 externalWorldVelocity;
        bool hasExternalWorldAttitude;
        Quaternion externalWorldAttitude;
        bool hasVelocityReference;
        Vector3 velocityReferenceWorld;
        Vector3 environmentalAccelerationWorld;
        PlanetaryFlightContext planetaryContext;
        Vector3 gravitySupportAccelerationWorld;

        public bool ControlsEnabled
        {
            get => controlsEnabled;
            set
            {
                controlsEnabled = value;
                if (flightCommandSource is KeyboardMouseFlightInput keyboardInput)
                    keyboardInput.CaptureEnabled = value;
                if (!value)
                    allocator.StopAll();
            }
        }

        public SpacecraftAssistMode AssistMode => assistMode;
        public bool LinearControlEnabled { get; set; } = true;
        public bool AngularControlEnabled { get; set; } = true;
        public float TargetSpeed => targetSpeed;
        [System.Obsolete("Use TargetSpeed. This value is an IFCS setpoint, not a physical speed limit.")]
        public float SpeedLimit => targetSpeed;
        public float BoostRatio => profile == null ? 0f : Mathf.Clamp01(boostSeconds / profile.BoostCapacitySeconds);
        public float CurrentThrottle => allocator.MaximumAppliedThrottle;
        public SpacecraftControlTelemetry Telemetry { get; private set; }
        public KeyboardMouseFlightInput FlightInput => flightCommandSource as KeyboardMouseFlightInput;
        public SpacecraftFlightCommand CurrentCommand => commandSource == null
            ? default
            : commandSource.Command;
        public Vector3 VelocityReferenceWorld => hasVelocityReference
            ? velocityReferenceWorld
            : Vector3.zero;
        public Vector3 EnvironmentalAccelerationWorld =>
            environmentalAccelerationWorld;
        public PlanetaryFlightContext PlanetaryContext =>
            planetaryContext;
        public float MaximumGravitySupportAcceleration =>
            maximumGravitySupportAcceleration;

        public void SetEnvironmentalAcceleration(
            Vector3 worldAcceleration)
        {
            environmentalAccelerationWorld = worldAcceleration;
        }

        public void SetPlanetaryFlightContext(
            PlanetaryFlightContext context)
        {
            planetaryContext = context;
            if (!planetaryContext.active)
                return;
            planetaryContext.upWorld = planetaryContext.upWorld.sqrMagnitude
                    > 0.0001f
                ? planetaryContext.upWorld.normalized
                : Vector3.up;
            if (assistMode == SpacecraftAssistMode.Direct)
                SetAssistMode(SpacecraftAssistMode.Coupled);
        }

        public void ClearPlanetaryFlightContext()
        {
            planetaryContext = default;
            gravitySupportAccelerationWorld = Vector3.zero;
        }

        public void SetTargetSpeed(float metersPerSecond)
        {
            if (profile == null)
                RefreshProfileAndAllocator();
            targetSpeed = Mathf.Clamp(
                metersPerSecond,
                profile.MinimumTargetSpeed,
                profile.MaximumTargetSpeed);
        }

        public void SetVelocityReference(Vector3 worldVelocity)
        {
            velocityReferenceWorld = worldVelocity;
            hasVelocityReference = true;
        }

        public void ClearVelocityReference()
        {
            velocityReferenceWorld = Vector3.zero;
            hasVelocityReference = false;
        }

        public void SetExternalWorldVelocityTarget(Vector3 velocity)
        {
            externalWorldVelocity = velocity;
            hasExternalWorldVelocity = true;
        }

        public void SetExternalWorldAttitudeTarget(Quaternion rotation)
        {
            externalWorldAttitude = rotation;
            hasExternalWorldAttitude = true;
        }

        public void ClearExternalTargets()
        {
            hasExternalWorldVelocity = false;
            hasExternalWorldAttitude = false;
        }

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            if (assembly != null)
                assembly.AssemblyChanged += HandleAssemblyChanged;
            if (hullController != null)
                hullController.HullChanged += HandleHullChanged;
        }

        void Start()
        {
            RefreshProfileAndAllocator();
        }

        public void Configure(
            Rigidbody body,
            ShipAssembly targetAssembly,
            ShipHullController targetHull,
            MonoBehaviour input,
            bool directModeAllowed)
        {
            if (assembly != null)
                assembly.AssemblyChanged -= HandleAssemblyChanged;
            if (hullController != null)
                hullController.HullChanged -= HandleHullChanged;

            shipBody = body;
            assembly = targetAssembly;
            hullController = targetHull;
            SetCommandSource(input);
            allowDirectMode = directModeAllowed;

            if (assembly != null)
                assembly.AssemblyChanged += HandleAssemblyChanged;
            if (hullController != null)
                hullController.HullChanged += HandleHullChanged;
            RefreshProfileAndAllocator();
        }

        public void SetAssistMode(SpacecraftAssistMode mode)
        {
            if (mode == SpacecraftAssistMode.Direct
                && (!allowDirectMode || planetaryContext.active))
                mode = SpacecraftAssistMode.Coupled;
            if (assistMode == mode)
                return;
            assistMode = mode;
            allocator.StopAll();
        }

        public void ResetControllerState()
        {
            allocator.StopAll();
            hasVelocitySample = false;
            measuredWorldAcceleration = Vector3.zero;
            if (pilotControls != null)
            {
                pilotControls.ResetVJoy();
                pilotControls.ClearTransientRequests();
            }
        }

        public void StabilizeAfterCinematic(
            Quaternion worldRotation,
            Vector3 worldVelocity)
        {
            ClearExternalTargets();
            ResetControllerState();
            if (shipBody == null)
                return;

            RigidbodyInterpolation interpolation = shipBody.interpolation;
            shipBody.interpolation = RigidbodyInterpolation.None;
            shipBody.rotation = worldRotation;
            shipBody.velocity = worldVelocity;
            shipBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            shipBody.interpolation = interpolation;
        }

        void Update()
        {
            if (pilotControls == null)
                return;

            if (pilotControls.ConsumeCoupledToggle())
            {
                SetAssistMode(assistMode == SpacecraftAssistMode.Coupled
                    ? SpacecraftAssistMode.Decoupled
                    : SpacecraftAssistMode.Coupled);
            }

            bool directToggleRequested = pilotControls.ConsumeDirectToggle();
            if (allowDirectMode
                && !planetaryContext.active
                && directToggleRequested)
            {
                SetAssistMode(assistMode == SpacecraftAssistMode.Direct
                    ? SpacecraftAssistMode.Coupled
                    : SpacecraftAssistMode.Direct);
            }

            if (profile != null)
            {
                float notches = pilotControls.ConsumeSpeedLimitDelta();
                if (Mathf.Abs(notches) > 0.001f)
                {
                    float step = SpaceflightUnitFormatter.AdaptiveTargetSpeedStep(targetSpeed);
                    targetSpeed = Mathf.Clamp(
                        targetSpeed + notches * step,
                        profile.MinimumTargetSpeed,
                        profile.MaximumTargetSpeed);
                }
            }
        }

        void FixedUpdate()
        {
            if (!controlsEnabled || shipBody == null || assembly == null || profile == null
                || assistMode == SpacecraftAssistMode.Direct)
            {
                allocator.StopAll();
                UpdateTelemetry(Vector3.zero, Vector3.zero, 1f);
                return;
            }

            if (!allocator.Matches(assembly, hullController == null ? null : hullController.CurrentHull))
                RefreshProfileAndAllocator();

            SpacecraftFlightCommand command = commandSource == null ? default : commandSource.Command;
            bool boosting = command.boost && boostSeconds > 0.001f;
            float thrustMultiplier = boosting ? profile.BoostMultiplier : 1f;
            UpdateBoostReserve(boosting);
            SpacecraftDirectionalAuthority directionalAuthority =
                allocator.RefreshDirectionalAuthority(
                    shipBody,
                    transform,
                    thrustMultiplier);

            Vector3 referenceVelocity = hasVelocityReference
                ? velocityReferenceWorld
                : Vector3.zero;
            Vector3 localVelocity = transform.InverseTransformDirection(
                shipBody.velocity - referenceVelocity);
            Vector3 localAngularVelocity = transform.InverseTransformDirection(shipBody.angularVelocity);
            Vector3 desiredAcceleration = Vector3.zero;
            if (LinearControlEnabled)
            {
                desiredAcceleration = planetaryContext.active
                    ? CalculatePlanetaryLinearAcceleration(
                        command,
                        referenceVelocity,
                        directionalAuthority,
                        thrustMultiplier)
                    : CalculateLinearAcceleration(
                        command,
                        localVelocity,
                        directionalAuthority,
                        thrustMultiplier);
            }
            Vector3 desiredAngularAcceleration = AngularControlEnabled
                ? CalculateAngularAcceleration(command, localAngularVelocity)
                : Vector3.zero;
            desiredAcceleration = ApplyEnvironmentCompensation(
                desiredAcceleration);
            Vector3 desiredForce = desiredAcceleration * shipBody.mass;
            Vector3 desiredTorque = AccelerationToTorque(desiredAngularAcceleration);

            float authority = allocator.SolveAndApply(
                shipBody,
                transform,
                desiredForce,
                desiredTorque,
                torqueWeight,
                thrustMultiplier,
                Time.fixedDeltaTime);
            UpdateMeasuredAcceleration();
            UpdateTelemetry(desiredForce, desiredTorque, authority);
        }

        Vector3 CalculateLinearAcceleration(
            SpacecraftFlightCommand command,
            Vector3 localVelocity,
            SpacecraftDirectionalAuthority directionalAuthority,
            float thrustMultiplier)
        {
            CalculateAccelerationLimits(
                directionalAuthority,
                thrustMultiplier,
                out Vector3 positiveAcceleration,
                out Vector3 negativeAcceleration);
            Vector3 maximumAcceleration = Vector3.Max(
                positiveAcceleration,
                negativeAcceleration);
            Vector3 desiredAcceleration;
            if (hasExternalWorldVelocity)
            {
                Vector3 targetVelocity = transform.InverseTransformDirection(
                    externalWorldVelocity - (hasVelocityReference
                        ? velocityReferenceWorld
                        : Vector3.zero));
                desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
            }
            else if (assistMode == SpacecraftAssistMode.Coupled || command.brake)
            {
                Vector3 targetVelocity = command.brake
                    ? Vector3.zero
                    : Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * targetSpeed;
                desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
            }
            else
            {
                desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
            }

            return ClampComponentsAsymmetric(
                desiredAcceleration,
                positiveAcceleration,
                negativeAcceleration);
        }

        Vector3 CalculatePlanetaryLinearAcceleration(
            SpacecraftFlightCommand command,
            Vector3 referenceVelocity,
            SpacecraftDirectionalAuthority directionalAuthority,
            float thrustMultiplier)
        {
            Vector3 up = planetaryContext.upWorld.sqrMagnitude > 0.0001f
                ? planetaryContext.upWorld.normalized
                : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(transform.up, up);
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            float verticalInput = command.translation.y;
            float verticalTarget = verticalInput >= 0f
                ? verticalInput
                    * (planetaryContext.altitude < 3f
                        ? lowAltitudeClimbSpeed
                        : planetaryClimbSpeed)
                : verticalInput * planetaryDescentSpeed;
            float forwardTarget =
                command.translation.z * targetSpeed;
            float strafeTarget =
                command.translation.x
                * Mathf.Min(planetaryStrafeSpeed, targetSpeed * 0.55f);
            if (command.brake)
            {
                verticalTarget = 0f;
                forwardTarget = 0f;
                strafeTarget = 0f;
            }

            Vector3 relativeVelocity =
                shipBody.velocity - referenceVelocity;
            Vector3 targetVelocity =
                forward * forwardTarget
                + right * strafeTarget
                + up * verticalTarget;
            Vector3 desiredWorldAcceleration =
                (targetVelocity - relativeVelocity)
                * profile.VelocityResponse;
            Vector3 desiredLocalAcceleration =
                transform.InverseTransformDirection(
                    desiredWorldAcceleration);
            CalculateAccelerationLimits(
                directionalAuthority,
                thrustMultiplier,
                out Vector3 positiveAcceleration,
                out Vector3 negativeAcceleration);
            return ClampComponentsAsymmetric(
                desiredLocalAcceleration,
                positiveAcceleration,
                negativeAcceleration);
        }

        Vector3 ApplyEnvironmentCompensation(
            Vector3 desiredLocalAcceleration)
        {
            gravitySupportAccelerationWorld = Vector3.zero;
            if (!planetaryContext.active)
            {
                // Known continuous forces are cancelled by physical thrusters
                // outside planetary VTOL flight.
                return desiredLocalAcceleration
                    - transform.InverseTransformDirection(
                        environmentalAccelerationWorld);
            }

            Vector3 up = planetaryContext.upWorld.sqrMagnitude > 0.0001f
                ? planetaryContext.upWorld.normalized
                : Vector3.up;
            Vector3 environmentAcceleration =
                planetaryContext.gravityAccelerationWorld
                + planetaryContext.aerodynamicAccelerationWorld;
            float support = CalculateGravitySupportAcceleration(
                planetaryContext.gravityAccelerationWorld,
                planetaryContext.aerodynamicAccelerationWorld,
                up,
                maximumGravitySupportAcceleration);
            gravitySupportAccelerationWorld = up * support;
            if (support > 0.0001f)
            {
                shipBody.AddForce(
                    gravitySupportAccelerationWorld,
                    ForceMode.Acceleration);
            }

            Vector3 requiredWorldAcceleration =
                transform.TransformDirection(desiredLocalAcceleration)
                - environmentAcceleration
                - gravitySupportAccelerationWorld;
            return transform.InverseTransformDirection(
                requiredWorldAcceleration);
        }

        public static float CalculateGravitySupportAcceleration(
            Vector3 gravityAcceleration,
            Vector3 aerodynamicAcceleration,
            Vector3 up,
            float maximumSupport)
        {
            if (up.sqrMagnitude <= 0.0001f)
                up = Vector3.up;
            else
                up.Normalize();
            float gravityDemand = Mathf.Max(
                0f,
                -Vector3.Dot(gravityAcceleration, up));
            float upwardAerodynamicSupport = Mathf.Max(
                0f,
                Vector3.Dot(aerodynamicAcceleration, up));
            float supportDemand = Mathf.Max(
                0f,
                gravityDemand - upwardAerodynamicSupport);
            return Mathf.Min(
                supportDemand,
                Mathf.Max(0f, maximumSupport));
        }

        void CalculateAccelerationLimits(
            SpacecraftDirectionalAuthority directionalAuthority,
            float thrustMultiplier,
            out Vector3 positive,
            out Vector3 negative)
        {
            Vector3 fallback = profile.RcsAcceleration * thrustMultiplier;
            positive = new Vector3(
                Mathf.Min(
                    comfortAccelerationLimit,
                    Mathf.Max(
                        fallback.x,
                        directionalAuthority.positiveAcceleration.x)),
                Mathf.Min(
                    comfortAccelerationLimit,
                    Mathf.Max(
                        fallback.y,
                        directionalAuthority.positiveAcceleration.y)),
                Mathf.Min(
                    comfortAccelerationLimit,
                    Mathf.Max(
                        fallback.z,
                        directionalAuthority.positiveAcceleration.z)));
            negative = new Vector3(
                Mathf.Min(
                    comfortAccelerationLimit,
                    Mathf.Max(
                        fallback.x,
                        directionalAuthority.negativeAcceleration.x)),
                Mathf.Min(
                    comfortAccelerationLimit,
                    Mathf.Max(
                        fallback.y,
                        directionalAuthority.negativeAcceleration.y)),
                Mathf.Min(
                    comfortAccelerationLimit,
                    Mathf.Max(
                        fallback.z,
                        directionalAuthority.negativeAcceleration.z)));
        }

        static Vector3 ClampComponentsAsymmetric(
            Vector3 value,
            Vector3 positive,
            Vector3 negative)
        {
            return new Vector3(
                Mathf.Clamp(value.x, -negative.x, positive.x),
                Mathf.Clamp(value.y, -negative.y, positive.y),
                Mathf.Clamp(value.z, -negative.z, positive.z));
        }

        Vector3 CalculateAngularAcceleration(
            SpacecraftFlightCommand command,
            Vector3 localAngularVelocity)
        {
            Vector3 maximumRateRadians = profile.MaximumAngularSpeed * Mathf.Deg2Rad;
            Vector3 targetRate;
            if (hasExternalWorldAttitude)
            {
                Quaternion error = externalWorldAttitude * Quaternion.Inverse(shipBody.rotation);
                error.ToAngleAxis(out float angleDegrees, out Vector3 worldAxis);
                if (angleDegrees > 180f)
                    angleDegrees -= 360f;
                Vector3 worldRate = worldAxis.sqrMagnitude > 0.0001f
                    ? worldAxis.normalized * (angleDegrees * Mathf.Deg2Rad * 1.8f)
                    : Vector3.zero;
                targetRate = transform.InverseTransformDirection(worldRate);
                targetRate = ClampComponents(targetRate, maximumRateRadians);
            }
            else
            {
                float aerodynamicBlend = planetaryContext.active
                    ? Mathf.Clamp01(planetaryContext.dynamicPressure / 5500f)
                    : 0f;
                float coordinatedRoll = Mathf.Abs(command.roll) > 0.05f
                    ? 0f
                    : -command.vjoy.y * 0.18f * aerodynamicBlend;
                targetRate = new Vector3(
                    command.vjoy.x * maximumRateRadians.x,
                    command.vjoy.y * maximumRateRadians.y,
                    Mathf.Clamp(command.roll + coordinatedRoll, -1f, 1f)
                        * maximumRateRadians.z);
            }
            float response = profile.AngularRateResponse;
            if (planetaryContext.active)
            {
                float aerodynamicBlend = Mathf.Clamp01(
                    planetaryContext.dynamicPressure / 5500f);
                response *= Mathf.Lerp(1f, 0.68f, aerodynamicBlend);
            }
            Vector3 acceleration =
                (targetRate - localAngularVelocity) * response;
            Vector3 maximumAccelerationRadians = profile.MaximumAngularAcceleration * Mathf.Deg2Rad;
            return ClampComponents(acceleration, maximumAccelerationRadians);
        }

        Vector3 AccelerationToTorque(Vector3 localAngularAcceleration)
        {
            Quaternion principalRotation = shipBody.inertiaTensorRotation;
            Vector3 principalAcceleration = Quaternion.Inverse(principalRotation) * localAngularAcceleration;
            Vector3 principalTorque = Vector3.Scale(shipBody.inertiaTensor, principalAcceleration);
            return principalRotation * principalTorque;
        }

        void UpdateBoostReserve(bool boosting)
        {
            if (boosting)
                boostSeconds = Mathf.Max(0f, boostSeconds - Time.fixedDeltaTime);
            else
                boostSeconds = Mathf.Min(
                    profile.BoostCapacitySeconds,
                    boostSeconds + profile.BoostRegenerationPerSecond * Time.fixedDeltaTime);
        }

        void UpdateTelemetry(Vector3 requestedForce, Vector3 requestedTorque, float authority)
        {
            Telemetry = new SpacecraftControlTelemetry
            {
                localVelocity = shipBody == null
                    ? Vector3.zero
                    : transform.InverseTransformDirection(
                        shipBody.velocity - (hasVelocityReference
                            ? velocityReferenceWorld
                            : Vector3.zero)),
                localAngularVelocity = shipBody == null
                    ? Vector3.zero
                    : transform.InverseTransformDirection(shipBody.angularVelocity),
                requestedLocalForce = requestedForce,
                requestedLocalTorque = requestedTorque,
                appliedLocalForce = allocator.AppliedLocalForce,
                currentAcceleration = transform.InverseTransformDirection(measuredWorldAcceleration),
                shipMass = shipBody == null ? 0f : shipBody.mass,
                targetSpeed = targetSpeed,
                speedLimit = targetSpeed,
                controlAuthority = authority,
                boostRatio = BoostRatio,
                assistMode = assistMode,
                directionalAuthority = allocator.DirectionalAuthority,
                planetaryFlightActive = planetaryContext.active,
                verticalSpeed = planetaryContext.active && shipBody != null
                    ? Vector3.Dot(
                        shipBody.velocity
                            - (hasVelocityReference
                                ? velocityReferenceWorld
                                : Vector3.zero),
                        planetaryContext.upWorld)
                    : 0f,
                gravitySupportAcceleration =
                    gravitySupportAccelerationWorld,
                gravitySupportLoad = planetaryContext.active
                    ? gravitySupportAccelerationWorld.magnitude
                        / Mathf.Max(
                            0.01f,
                            maximumGravitySupportAcceleration)
                    : 0f,
                airDensity = planetaryContext.airDensity,
                airSpeed = planetaryContext.airSpeed,
                dynamicPressure = planetaryContext.dynamicPressure,
                angleOfAttack = planetaryContext.angleOfAttack,
                isStalling = planetaryContext.isStalling
            };
        }

        void UpdateMeasuredAcceleration()
        {
            if (shipBody == null)
            {
                measuredWorldAcceleration = Vector3.zero;
                hasVelocitySample = false;
                return;
            }
            if (hasVelocitySample)
            {
                measuredWorldAcceleration = (shipBody.velocity - previousWorldVelocity)
                    / Mathf.Max(0.0001f, Time.fixedDeltaTime);
            }
            else
            {
                measuredWorldAcceleration = Vector3.zero;
                hasVelocitySample = true;
            }
            previousWorldVelocity = shipBody.velocity;
        }

        void ResolveReferences()
        {
            if (shipBody == null)
                shipBody = GetComponent<Rigidbody>();
            if (assembly == null)
                assembly = GetComponent<ShipAssembly>();
            if (hullController == null)
                hullController = GetComponentInChildren<ShipHullController>(true);
            if (flightCommandSource == null)
                flightCommandSource = GetComponent<KeyboardMouseFlightInput>();
            SetCommandSource(flightCommandSource);
        }

        public bool SetCommandSource(MonoBehaviour source)
        {
            if (source != null && !(source is ISpacecraftFlightCommandSource))
            {
                Debug.LogError($"{source.name} does not implement {nameof(ISpacecraftFlightCommandSource)}.", source);
                return false;
            }

            flightCommandSource = source;
            commandSource = source as ISpacecraftFlightCommandSource;
            pilotControls = source as ISpacecraftPilotControlSource;
            return commandSource != null;
        }

        void RefreshProfileAndAllocator()
        {
            ResolveReferences();
            ShipHullDefinition hull = hullController == null ? null : hullController.CurrentHull;
            profile = hull == null ? ShipFlightProfile.CreateForHull(string.Empty) : hull.FlightProfile;
            targetSpeed = targetSpeed <= 0f
                ? profile.DefaultTargetSpeed
                : Mathf.Clamp(
                    targetSpeed,
                    profile.MinimumTargetSpeed,
                    profile.MaximumTargetSpeed);
            boostSeconds = boostSeconds <= 0f ? profile.BoostCapacitySeconds : Mathf.Min(boostSeconds, profile.BoostCapacitySeconds);
            allocator.Rebuild(assembly, hull);
        }

        void HandleAssemblyChanged()
        {
            RefreshProfileAndAllocator();
        }

        void HandleHullChanged(ShipHullDefinition hull)
        {
            RefreshProfileAndAllocator();
        }

        static Vector3 ClampComponents(Vector3 value, Vector3 limit)
        {
            return new Vector3(
                Mathf.Clamp(value.x, -limit.x, limit.x),
                Mathf.Clamp(value.y, -limit.y, limit.y),
                Mathf.Clamp(value.z, -limit.z, limit.z));
        }

        void OnDisable()
        {
            if (assembly != null)
                assembly.AssemblyChanged -= HandleAssemblyChanged;
            if (hullController != null)
                hullController.HullChanged -= HandleHullChanged;
            allocator.StopAll();
            environmentalAccelerationWorld = Vector3.zero;
            ClearPlanetaryFlightContext();
        }
    }
}
