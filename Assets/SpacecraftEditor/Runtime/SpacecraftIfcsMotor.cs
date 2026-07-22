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

        readonly SpacecraftThrusterAllocator allocator = new SpacecraftThrusterAllocator();
        ShipFlightProfile profile;
        float speedLimit;
        float boostSeconds;
        bool controlsEnabled;
        ISpacecraftFlightCommandSource commandSource;
        ISpacecraftPilotControlSource pilotControls;
        bool hasExternalWorldVelocity;
        Vector3 externalWorldVelocity;
        bool hasExternalWorldAttitude;
        Quaternion externalWorldAttitude;

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
        public float SpeedLimit => speedLimit;
        public float BoostRatio => profile == null ? 0f : Mathf.Clamp01(boostSeconds / profile.BoostCapacitySeconds);
        public float CurrentThrottle => allocator.MaximumAppliedThrottle;
        public SpacecraftControlTelemetry Telemetry { get; private set; }
        public KeyboardMouseFlightInput FlightInput => flightCommandSource as KeyboardMouseFlightInput;

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
            if (mode == SpacecraftAssistMode.Direct && !allowDirectMode)
                mode = SpacecraftAssistMode.Coupled;
            if (assistMode == mode)
                return;
            assistMode = mode;
            allocator.StopAll();
        }

        public void ResetControllerState()
        {
            allocator.StopAll();
            if (pilotControls != null)
            {
                pilotControls.ResetVJoy();
                pilotControls.ClearTransientRequests();
            }
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
            if (allowDirectMode && directToggleRequested)
            {
                SetAssistMode(assistMode == SpacecraftAssistMode.Direct
                    ? SpacecraftAssistMode.Coupled
                    : SpacecraftAssistMode.Direct);
            }

            if (profile != null)
            {
                speedLimit = Mathf.Clamp(
                    speedLimit + pilotControls.ConsumeSpeedLimitDelta(),
                    profile.MinimumSpeedLimit,
                    profile.MaximumSpeedLimit);
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

            Vector3 localVelocity = transform.InverseTransformDirection(shipBody.velocity);
            Vector3 localAngularVelocity = transform.InverseTransformDirection(shipBody.angularVelocity);
            Vector3 desiredAcceleration = LinearControlEnabled
                ? CalculateLinearAcceleration(command, localVelocity, thrustMultiplier)
                : Vector3.zero;
            Vector3 desiredAngularAcceleration = CalculateAngularAcceleration(command, localAngularVelocity);
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
            UpdateTelemetry(desiredForce, desiredTorque, authority);
        }

        Vector3 CalculateLinearAcceleration(
            SpacecraftFlightCommand command,
            Vector3 localVelocity,
            float thrustMultiplier)
        {
            Vector3 maximumAcceleration = profile.RcsAcceleration * thrustMultiplier;
            Vector3 desiredAcceleration;
            if (hasExternalWorldVelocity)
            {
                Vector3 targetVelocity = transform.InverseTransformDirection(externalWorldVelocity);
                desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
            }
            else if (assistMode == SpacecraftAssistMode.Coupled || command.brake)
            {
                Vector3 targetVelocity = command.brake
                    ? Vector3.zero
                    : Vector3.Scale(command.translation, new Vector3(0.72f, 0.72f, 1f)) * speedLimit;
                desiredAcceleration = (targetVelocity - localVelocity) * profile.VelocityResponse;
            }
            else
            {
                desiredAcceleration = Vector3.Scale(command.translation, maximumAcceleration);
            }

            return ClampComponents(desiredAcceleration, maximumAcceleration);
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
                targetRate = new Vector3(
                    command.vjoy.x * maximumRateRadians.x,
                    command.vjoy.y * maximumRateRadians.y,
                    command.roll * maximumRateRadians.z);
            }
            Vector3 acceleration = (targetRate - localAngularVelocity) * profile.AngularRateResponse;
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
                    : transform.InverseTransformDirection(shipBody.velocity),
                localAngularVelocity = shipBody == null
                    ? Vector3.zero
                    : transform.InverseTransformDirection(shipBody.angularVelocity),
                requestedLocalForce = requestedForce,
                requestedLocalTorque = requestedTorque,
                speedLimit = speedLimit,
                controlAuthority = authority,
                boostRatio = BoostRatio,
                assistMode = assistMode
            };
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
            speedLimit = speedLimit <= 0f
                ? profile.DefaultSpeedLimit
                : Mathf.Clamp(speedLimit, profile.MinimumSpeedLimit, profile.MaximumSpeedLimit);
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
        }
    }
}
