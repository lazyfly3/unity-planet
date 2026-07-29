using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SpacecraftEditor;

namespace UnityPlanet.ModularAssembly
{
    public enum VehicleMotionRequest
    {
        Ground,
        Flight
    }

    public enum VehicleMotionMode
    {
        Grounded,
        Flight,
        GroundAirborneAssist
    }

    [Serializable]
    public struct VehicleInputFrame
    {
        public Vector3 translation;
        public Vector2 look;
        public float roll;
        public bool boost;
        public bool hoverBrake;
        public bool freeLook;
        public bool modeTogglePressed;
    }

    [Serializable]
    public struct VehicleTelemetryV2
    {
        public VehicleMotionRequest requestedMode;
        public VehicleMotionMode actualMode;
        public float energy;
        public float energyCapacity;
        public float controlAuthority;
        public float liftToWeight;
        public float hoverMargin;
        public Vector3 positiveAcceleration;
        public Vector3 negativeAcceleration;
        public Vector3 localVelocity;
        public Vector3 localAngularVelocity;
        public Vector3 centerOfMass;
        public int groundedWheels;
        public bool autoHover;
        public bool energyCutoff;
        public string status;
    }

    [DefaultExecutionOrder(-700)]
    public sealed class VehicleInputRouterV2 : MonoBehaviour
    {
        const float DoubleTapWindow = 0.28f;

        [SerializeField, Range(0.005f, 0.2f)] float mouseSensitivity = 0.045f;
        [SerializeField, Range(0f, 0.3f)] float mouseDeadZone = 0.05f;
        [SerializeField, Range(1f, 3f)] float mouseExponent = 1.55f;

        Vector2 virtualStick;
        float lastSpaceDown = -10f;

        public void ResetState()
        {
            virtualStick = Vector2.zero;
            lastSpaceDown = -10f;
        }

        public VehicleInputFrame Sample()
        {
            bool freeLook = Input.GetKey(KeyCode.LeftAlt)
                            || Input.GetKey(KeyCode.RightAlt);
            bool pointerReady = Application.isFocused
                                && Cursor.lockState == CursorLockMode.Locked;
            if (pointerReady && !freeLook)
            {
                virtualStick += new Vector2(
                    Input.GetAxisRaw("Mouse X"),
                    Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;
                virtualStick = Vector2.ClampMagnitude(virtualStick, 1f);
            }
            if (Input.GetMouseButtonDown(2))
                virtualStick = Vector2.zero;

            bool hover = Input.GetKey(KeyCode.X);
            if (hover)
                virtualStick = Vector2.zero;

            bool toggle = false;
            if (Input.GetKeyDown(KeyCode.Space))
            {
                float now = Time.unscaledTime;
                if (now - lastSpaceDown <= DoubleTapWindow)
                {
                    toggle = true;
                    lastSpaceDown = -10f;
                }
                else
                {
                    lastSpaceDown = now;
                }
            }

            return new VehicleInputFrame
            {
                translation = Vector3.ClampMagnitude(new Vector3(
                    DigitalAxis(KeyCode.A, KeyCode.D),
                    DigitalAxis(KeyCode.LeftControl, KeyCode.Space),
                    DigitalAxis(KeyCode.S, KeyCode.W)), 1f),
                look = ApplyResponse(virtualStick),
                roll = DigitalAxis(KeyCode.Q, KeyCode.E),
                boost = Input.GetKey(KeyCode.LeftShift)
                        || Input.GetKey(KeyCode.RightShift),
                hoverBrake = hover,
                freeLook = freeLook,
                modeTogglePressed = toggle
            };
        }

        Vector2 ApplyResponse(Vector2 input)
        {
            float magnitude = input.magnitude;
            if (magnitude <= mouseDeadZone)
                return Vector2.zero;
            float normalized = Mathf.InverseLerp(mouseDeadZone, 1f, magnitude);
            float curved = Mathf.Pow(normalized, mouseExponent);
            return input.normalized * curved;
        }

        static float DigitalAxis(KeyCode negative, KeyCode positive)
        {
            return (Input.GetKey(positive) ? 1f : 0f)
                   - (Input.GetKey(negative) ? 1f : 0f);
        }
    }

    public sealed class VehicleForceLedger
    {
        Rigidbody body;
        Vector3 force;
        Vector3 torque;

        public Vector3 TotalForce => force;
        public Vector3 TotalTorque => torque;

        public void Begin(Rigidbody target)
        {
            body = target;
            force = Vector3.zero;
            torque = Vector3.zero;
        }

        public void AddForce(Vector3 worldForce)
        {
            force += worldForce;
        }

        public void AddAcceleration(Vector3 worldAcceleration)
        {
            if (body != null)
                force += worldAcceleration * body.mass;
        }

        public void AddTorque(Vector3 worldTorque)
        {
            torque += worldTorque;
        }

        public void AddForceAtPosition(
            Vector3 worldForce,
            Vector3 worldPosition)
        {
            force += worldForce;
            if (body != null)
            {
                torque += Vector3.Cross(
                    worldPosition - body.worldCenterOfMass,
                    worldForce);
            }
        }

        public void Apply()
        {
            if (body == null || body.isKinematic)
                return;
            body.AddForce(force, ForceMode.Force);
            body.AddTorque(torque, ForceMode.Force);
        }
    }

    [DisallowMultipleComponent]
    public sealed class VehicleEnergyBusV2 : MonoBehaviour
    {
        const float CoreCapacity = 100f;
        const float CoreRechargePerSecond = 12f;
        const float RestartRatio = 0.05f;

        public float Capacity { get; private set; } = CoreCapacity;
        public float Energy { get; private set; } = CoreCapacity;
        public float RechargePerSecond { get; private set; } =
            CoreRechargePerSecond;
        public bool IsCutOff { get; private set; }

        public void Rebuild(IEnumerable<NeoXBehaviorModule> modules)
        {
            float previousRatio = Capacity > 0.001f
                ? Energy / Capacity
                : 1f;
            Capacity = CoreCapacity;
            if (modules != null)
            {
                foreach (NeoXBehaviorModule module in modules)
                {
                    if (module != null)
                        Capacity += Mathf.Max(0f, module.EnergyCapacity);
                }
            }
            Energy = Mathf.Clamp(previousRatio * Capacity, 0f, Capacity);
        }

        public void BeginSession()
        {
            Energy = Capacity;
            IsCutOff = false;
        }

        public void Tick(float deltaTime)
        {
            Energy = Mathf.Min(
                Capacity,
                Energy + RechargePerSecond * Mathf.Max(0f, deltaTime));
            if (IsCutOff && Energy >= Capacity * RestartRatio)
                IsCutOff = false;
        }

        public float ConsumeScaled(float required)
        {
            required = Mathf.Max(0f, required);
            if (required <= 0.00001f)
                return IsCutOff ? 0f : 1f;
            if (IsCutOff || Energy <= 0.00001f)
            {
                IsCutOff = true;
                return 0f;
            }

            float scale = Mathf.Clamp01(Energy / required);
            Energy = Mathf.Max(0f, Energy - required * scale);
            if (Energy <= 0.00001f)
                IsCutOff = true;
            return scale;
        }
    }

    public sealed class VehicleControlAllocatorV2
    {
        sealed class Actuator
        {
            public Vector3 localPosition;
            public Vector3 localDirection;
            public float maximumForce;
            public float energyPerSecond;
            public NeoXThrusterExhaustVfx exhaust;
        }

        const int SolverIterations = 24;
        const float CoreNozzleForce = 4000f;

        readonly List<Actuator> actuators = new List<Actuator>();
        readonly List<NeoXThrusterExhaustVfx> exhaustVisuals =
            new List<NeoXThrusterExhaustVfx>();
        float[] throttles = Array.Empty<float>();
        Vector3[] forces = Array.Empty<Vector3>();
        Vector3[] torques = Array.Empty<Vector3>();
        float referenceLength = 1f;
        Transform root;

        public Vector3 PositiveForce { get; private set; }
        public Vector3 NegativeForce { get; private set; }
        public Vector3 AppliedLocalForce { get; private set; }
        public Vector3 AppliedLocalTorque { get; private set; }
        public float ControlAuthority { get; private set; } = 1f;

        public void Rebuild(
            Transform vehicleRoot,
            IEnumerable<NeoXBehaviorModule> modules,
            Bounds localBounds,
            bool includeCoreNozzles)
        {
            root = vehicleRoot;
            ClearExhaustVisuals();
            actuators.Clear();
            exhaustVisuals.Clear();
            Vector3 halfSize = Vector3.Scale(
                localBounds.size,
                new Vector3(0.42f, 0.42f, 0.42f));
            halfSize = new Vector3(
                Mathf.Max(0.5f, halfSize.x),
                Mathf.Max(0.5f, halfSize.y),
                Mathf.Max(0.5f, halfSize.z));
            if (includeCoreNozzles)
            {
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 position = localBounds.center + new Vector3(
                        x * halfSize.x,
                        y * halfSize.y,
                        z * halfSize.z);
                    Add(position, new Vector3(-x, 0f, 0f), CoreNozzleForce, 4f);
                    Add(position, new Vector3(0f, -y, 0f), CoreNozzleForce, 4f);
                    Add(position, new Vector3(0f, 0f, -z), CoreNozzleForce, 4f);
                }
            }

            if (modules != null && root != null)
            {
                foreach (NeoXBehaviorModule module in modules)
                {
                    if (module == null)
                        continue;
                    Vector3 position =
                        root.InverseTransformPoint(module.transform.position);
                    if (module.BehaviorKind ==
                        GridModuleBehaviorKind.Thruster)
                    {
                        Vector3 direction = root.InverseTransformDirection(
                            -module.WorldExhaustDirection).normalized;
                        Add(
                            position,
                            direction,
                            6000f * module.Strength,
                            6f * module.Strength);
                        BindExhaust(module);
                    }
                    else if (module.BehaviorKind ==
                             GridModuleBehaviorKind.Hover)
                    {
                        Vector3 direction = root.InverseTransformDirection(
                            -module.WorldExhaustDirection).normalized;
                        Add(
                            position,
                            direction,
                            1500f * module.Strength,
                            3f * module.Strength);
                        BindExhaust(module);
                    }
                    else if (module.BehaviorKind ==
                                 GridModuleBehaviorKind.Wing ||
                             module.BehaviorKind ==
                                 GridModuleBehaviorKind.ControlSurface)
                    {
                        float strength = module.Strength;
                        Add(position, Vector3.forward, 900f * strength, 2f);
                        Add(position, Vector3.up, 450f * strength, 1.5f);
                    }
                }
            }

            throttles = new float[actuators.Count];
            forces = new Vector3[actuators.Count];
            torques = new Vector3[actuators.Count];
            referenceLength = Mathf.Max(
                0.5f,
                localBounds.size.magnitude * 0.25f);
            EstimateDirectionalAuthority(localBounds.center);
        }

        public void SolveAndCollect(
            Rigidbody body,
            Vector3 desiredLocalForce,
            Vector3 desiredLocalTorque,
            VehicleEnergyBusV2 energy,
            VehicleForceLedger ledger,
            float deltaTime)
        {
            ClearExhaustVisuals();
            if (body == null || root == null || actuators.Count == 0)
                return;
            BuildWrenches(body.centerOfMass);
            Array.Clear(throttles, 0, throttles.Length);
            Solve(desiredLocalForce, desiredLocalTorque, 1.65f);

            float requestedEnergy = 0f;
            for (int index = 0; index < actuators.Count; index++)
            {
                requestedEnergy += throttles[index]
                                   * actuators[index].energyPerSecond
                                   * deltaTime;
            }
            float energyScale = energy == null
                ? 1f
                : energy.ConsumeScaled(requestedEnergy);

            AppliedLocalForce = Vector3.zero;
            AppliedLocalTorque = Vector3.zero;
            for (int index = 0; index < actuators.Count; index++)
            {
                float throttle = throttles[index] * energyScale;
                if (actuators[index].exhaust != null)
                {
                    actuators[index].exhaust.SetTargetThrottle(
                        throttle);
                }
                if (throttle <= 0.0001f)
                    continue;
                Vector3 localForce = forces[index] * throttle;
                AppliedLocalForce += localForce;
                AppliedLocalTorque += torques[index] * throttle;
                ledger.AddForceAtPosition(
                    root.TransformDirection(localForce),
                    root.TransformPoint(actuators[index].localPosition));
            }

            float requestMagnitude = WrenchMagnitude(
                desiredLocalForce,
                desiredLocalTorque,
                1.65f);
            float residualMagnitude = WrenchMagnitude(
                desiredLocalForce - AppliedLocalForce,
                desiredLocalTorque - AppliedLocalTorque,
                1.65f);
            ControlAuthority = requestMagnitude <= 0.01f
                ? 1f
                : Mathf.Clamp01(1f - residualMagnitude / requestMagnitude);
        }

        void BindExhaust(NeoXBehaviorModule module)
        {
            if (actuators.Count == 0
                || !NeoXThrusterExhaustVfx.Supports(module))
            {
                return;
            }
            NeoXThrusterExhaustVfx exhaust =
                module.GetComponent<NeoXThrusterExhaustVfx>()
                ?? module.gameObject.AddComponent<
                    NeoXThrusterExhaustVfx>();
            exhaust.Configure(module);
            actuators[actuators.Count - 1].exhaust = exhaust;
            if (!exhaustVisuals.Contains(exhaust))
                exhaustVisuals.Add(exhaust);
        }

        public void ClearExhaustVisuals()
        {
            for (int index = 0; index < exhaustVisuals.Count; index++)
            {
                if (exhaustVisuals[index] != null)
                    exhaustVisuals[index].SetTargetThrottle(0f);
            }
        }

        void Add(
            Vector3 localPosition,
            Vector3 localDirection,
            float force,
            float energyPerSecond)
        {
            if (localDirection.sqrMagnitude <= 0.0001f || force <= 0f)
                return;
            actuators.Add(new Actuator
            {
                localPosition = localPosition,
                localDirection = localDirection.normalized,
                maximumForce = force,
                energyPerSecond = Mathf.Max(0f, energyPerSecond)
            });
        }

        void BuildWrenches(Vector3 centerOfMass)
        {
            for (int index = 0; index < actuators.Count; index++)
            {
                Actuator actuator = actuators[index];
                Vector3 force =
                    actuator.localDirection * actuator.maximumForce;
                forces[index] = force;
                torques[index] = Vector3.Cross(
                    actuator.localPosition - centerOfMass,
                    force);
            }
        }

        void Solve(
            Vector3 desiredForce,
            Vector3 desiredTorque,
            float torqueWeight)
        {
            Vector3 residualForce = desiredForce;
            Vector3 residualTorque = desiredTorque;
            for (int iteration = 0;
                 iteration < SolverIterations;
                 iteration++)
            {
                bool reverse = (iteration & 1) != 0;
                for (int step = 0; step < actuators.Count; step++)
                {
                    int index = reverse
                        ? actuators.Count - 1 - step
                        : step;
                    float previous = throttles[index];
                    residualForce += forces[index] * previous;
                    residualTorque += torques[index] * previous;
                    float denominator = WeightedDot(
                        forces[index],
                        torques[index],
                        forces[index],
                        torques[index],
                        torqueWeight) + 0.000001f;
                    float numerator = WeightedDot(
                        forces[index],
                        torques[index],
                        residualForce,
                        residualTorque,
                        torqueWeight);
                    float next = Mathf.Clamp01(numerator / denominator);
                    throttles[index] = next;
                    residualForce -= forces[index] * next;
                    residualTorque -= torques[index] * next;
                }
            }
        }

        float WeightedDot(
            Vector3 forceA,
            Vector3 torqueA,
            Vector3 forceB,
            Vector3 torqueB,
            float torqueWeight)
        {
            return Vector3.Dot(forceA, forceB)
                   + Vector3.Dot(
                       torqueA / referenceLength,
                       torqueB / referenceLength) * torqueWeight;
        }

        float WrenchMagnitude(
            Vector3 force,
            Vector3 torque,
            float torqueWeight)
        {
            return Mathf.Sqrt(Mathf.Max(
                0f,
                WeightedDot(
                    force,
                    torque,
                    force,
                    torque,
                    torqueWeight)));
        }

        void EstimateDirectionalAuthority(Vector3 centerOfMass)
        {
            BuildWrenches(centerOfMass);
            float totalForce = Mathf.Max(
                1000f,
                actuators.Sum(value => value.maximumForce));
            PositiveForce = new Vector3(
                EstimateForce(Vector3.right, totalForce),
                EstimateForce(Vector3.up, totalForce),
                EstimateForce(Vector3.forward, totalForce));
            NegativeForce = new Vector3(
                EstimateForce(Vector3.left, totalForce),
                EstimateForce(Vector3.down, totalForce),
                EstimateForce(Vector3.back, totalForce));
            Array.Clear(throttles, 0, throttles.Length);
        }

        float EstimateForce(Vector3 axis, float request)
        {
            Array.Clear(throttles, 0, throttles.Length);
            Solve(axis * request, Vector3.zero, 5f);
            Vector3 force = Vector3.zero;
            Vector3 torque = Vector3.zero;
            for (int index = 0; index < actuators.Count; index++)
            {
                force += forces[index] * throttles[index];
                torque += torques[index] * throttles[index];
            }
            float along = Mathf.Max(0f, Vector3.Dot(force, axis));
            float contamination = (
                Vector3.ProjectOnPlane(force, axis).magnitude
                + torque.magnitude / referenceLength)
                / Mathf.Max(1f, along);
            return along * Mathf.Clamp01(1f - contamination * 0.35f);
        }
    }

    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleMotionCoordinatorV2 : MonoBehaviour
    {
        struct PendingImpulse
        {
            public Vector3 impulse;
            public Vector3 position;
        }

        const float StableGroundSeconds = 0.25f;
        const float AirborneAssistDelay = 0.35f;

        readonly VehicleForceLedger ledger = new VehicleForceLedger();
        readonly VehicleControlAllocatorV2 allocator =
            new VehicleControlAllocatorV2();
        readonly List<PendingImpulse> pendingImpulses =
            new List<PendingImpulse>();

        Rigidbody body;
        ShipAssembly assembly;
        VehicleInputRouterV2 input;
        VehicleEnergyBusV2 energy;
        ModularWheelRuntime[] wheels = Array.Empty<ModularWheelRuntime>();
        NeoXBehaviorModule[] modules = Array.Empty<NeoXBehaviorModule>();
        VehicleInputFrame frame;
        Vector3 gravity = Vector3.down * 9.81f;
        float airDensity = 1.225f;
        float altitude;
        float stableGroundTime;
        float airborneTime;
        bool hoverCaptured;
        Vector3 hoverPosition;
        float hoverYaw;
        bool initialized;
        bool assemblyDirty;
        bool builtInCoreThrustersEnabled = true;
        int liveModuleCount;
        float nextAssemblyAudit;
        Vector3 aerodynamicArea = new Vector3(4f, 4f, 4f);

        public bool IsActive { get; private set; }
        public VehicleMotionRequest RequestedMode { get; private set; } =
            VehicleMotionRequest.Flight;
        public VehicleMotionMode Mode { get; private set; } =
            VehicleMotionMode.Flight;
        public VehicleTelemetryV2 Telemetry { get; private set; }
        public bool BuiltInCoreThrustersEnabled =>
            builtInCoreThrustersEnabled;

        public void SetBuiltInCoreThrustersEnabled(bool enabled)
        {
            if (builtInCoreThrustersEnabled == enabled)
                return;
            builtInCoreThrustersEnabled = enabled;
            assemblyDirty = true;
            Rebuild();
        }

        public void Configure(Rigidbody target, ShipAssembly targetAssembly)
        {
            body = target != null ? target : GetComponent<Rigidbody>();
            assembly = targetAssembly != null
                ? targetAssembly
                : GetComponent<ShipAssembly>();
            input = GetComponent<VehicleInputRouterV2>()
                    ?? gameObject.AddComponent<VehicleInputRouterV2>();
            energy = GetComponent<VehicleEnergyBusV2>()
                     ?? gameObject.AddComponent<VehicleEnergyBusV2>();
            initialized = true;
            Rebuild();
        }

        public void Rebuild()
        {
            if (!initialized)
            {
                body = GetComponent<Rigidbody>();
                assembly = GetComponent<ShipAssembly>();
                input = GetComponent<VehicleInputRouterV2>()
                        ?? gameObject.AddComponent<VehicleInputRouterV2>();
                energy = GetComponent<VehicleEnergyBusV2>()
                         ?? gameObject.AddComponent<VehicleEnergyBusV2>();
                initialized = true;
            }
            modules = GetComponentsInChildren<NeoXBehaviorModule>(true);
            wheels = GetComponentsInChildren<ModularWheelRuntime>(true)
                .Take(64)
                .ToArray();
            foreach (NeoXBehaviorModule module in modules)
            {
                if (module == null)
                    continue;
                VehicleModulePhysicsNotifierV2 notifier =
                    module.GetComponent<VehicleModulePhysicsNotifierV2>()
                    ?? module.gameObject.AddComponent<
                        VehicleModulePhysicsNotifierV2>();
                notifier.Bind(this);
            }
            assembly?.Recalculate();
            energy.Rebuild(modules);
            Bounds localBounds = CalculateLocalBounds();
            aerodynamicArea = new Vector3(
                Mathf.Max(0.25f, localBounds.size.y * localBounds.size.z),
                Mathf.Max(0.25f, localBounds.size.x * localBounds.size.z),
                Mathf.Max(0.25f, localBounds.size.x * localBounds.size.y));
            allocator.Rebuild(
                transform,
                modules,
                localBounds,
                builtInCoreThrustersEnabled);
            liveModuleCount = modules.Count(
                module => module != null && module.isActiveAndEnabled);
            assemblyDirty = false;
            nextAssemblyAudit = Time.unscaledTime + 0.5f;
        }

        public void BeginFlight()
        {
            Rebuild();
            assembly?.Recalculate();
            IsActive = true;
            input.enabled = true;
            input.ResetState();
            energy.BeginSession();
            RequestedMode = wheels.Length > 0
                ? VehicleMotionRequest.Ground
                : VehicleMotionRequest.Flight;
            Mode = RequestedMode == VehicleMotionRequest.Flight
                ? VehicleMotionMode.Flight
                : VehicleMotionMode.GroundAirborneAssist;
            stableGroundTime = 0f;
            airborneTime = 0f;
            hoverCaptured = false;
            foreach (ModularWheelRuntime wheel in wheels)
                wheel.SetFlightMode(true);
        }

        public void EndFlight()
        {
            IsActive = false;
            input.ResetState();
            pendingImpulses.Clear();
            allocator.ClearExhaustVisuals();
            foreach (ModularWheelRuntime wheel in wheels)
                wheel.SetFlightMode(false);
        }

        public void SetPlanetEnvironment(
            Vector3 gravityAcceleration,
            float density,
            float heightAboveGround)
        {
            gravity = gravityAcceleration.sqrMagnitude > 0.0001f
                ? gravityAcceleration
                : Vector3.down * 9.81f;
            airDensity = Mathf.Max(0f, density);
            altitude = Mathf.Max(0f, heightAboveGround);
        }

        public void ClearPlanetEnvironment()
        {
            gravity = Vector3.down * 9.81f;
            airDensity = 1.225f;
            altitude = 0f;
        }

        public void QueueImpulseAtPosition(
            Vector3 impulse,
            Vector3 worldPosition)
        {
            pendingImpulses.Add(new PendingImpulse
            {
                impulse = impulse,
                position = worldPosition
            });
        }

        public void NotifyModulePhysicsChanged()
        {
            assemblyDirty = true;
        }

        void Update()
        {
            if (!IsActive)
                return;
            frame = input.Sample();
            if (frame.modeTogglePressed)
                ToggleRequestedMode();
        }

        void FixedUpdate()
        {
            if (!IsActive || body == null || body.isKinematic)
                return;
            AuditAssembly();

            energy.Tick(Time.fixedDeltaTime);
            ledger.Begin(body);
            ledger.AddAcceleration(gravity);
            AddBodyAerodynamics();

            Vector3 vehicleUp = transform.up;
            int grounded = 0;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel != null && wheel.ProbeContact(vehicleUp))
                    grounded++;
            }
            UpdateMode(grounded);

            if (Mode == VehicleMotionMode.Grounded)
                CollectGroundForces(grounded);
            else
                CollectFlightForces(
                    Mode == VehicleMotionMode.GroundAirborneAssist);

            CollectWingAerodynamics();
            ledger.Apply();
            ApplyPendingImpulses();
            UpdateTelemetry(grounded);
        }

        void ToggleRequestedMode()
        {
            if (RequestedMode == VehicleMotionRequest.Ground)
            {
                RequestedMode = VehicleMotionRequest.Flight;
                Mode = VehicleMotionMode.Flight;
                stableGroundTime = 0f;
                return;
            }

            if (!CanRequestGround())
            {
                SetStatus("Landing request rejected: no wheel surface.");
                return;
            }
            RequestedMode = VehicleMotionRequest.Ground;
            Mode = VehicleMotionMode.GroundAirborneAssist;
            stableGroundTime = 0f;
        }

        bool CanRequestGround()
        {
            if (wheels.Length == 0)
                return false;
            if (altitude <= 5f)
                return true;
            return Physics.SphereCast(
                body.worldCenterOfMass,
                0.35f,
                -transform.up,
                out _,
                5f,
                ~0,
                QueryTriggerInteraction.Ignore);
        }

        void UpdateMode(int grounded)
        {
            int requiredGrounded = Mathf.Min(2, wheels.Length);
            bool stableContact = requiredGrounded > 0
                                 && grounded >= requiredGrounded;
            if (stableContact)
            {
                stableGroundTime += Time.fixedDeltaTime;
                airborneTime = 0f;
            }
            else
            {
                stableGroundTime = 0f;
                airborneTime += Time.fixedDeltaTime;
            }

            if (RequestedMode == VehicleMotionRequest.Flight)
            {
                Mode = VehicleMotionMode.Flight;
            }
            else if (stableGroundTime >= StableGroundSeconds)
            {
                Mode = VehicleMotionMode.Grounded;
            }
            else if (airborneTime >= AirborneAssistDelay)
            {
                Mode = VehicleMotionMode.GroundAirborneAssist;
            }
        }

        void CollectGroundForces(int grounded)
        {
            allocator.ClearExhaustVisuals();
            float throttle = frame.translation.z;
            float steering = frame.translation.x;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel == null)
                    continue;
                wheel.ApplyForces(
                    transform.up,
                    transform.forward,
                    throttle,
                    steering,
                    frame.hoverBrake,
                    frame.boost,
                    body.mass,
                    grounded,
                    ledger);
            }
            if (frame.hoverBrake)
            {
                ledger.AddTorque(
                    -body.angularVelocity * body.mass * 2.8f);
            }
        }

        void CollectFlightForces(bool weakAttitudeOnly)
        {
            Vector3 desiredLocalForce = Vector3.zero;
            Vector3 desiredLocalTorque;
            if (weakAttitudeOnly)
            {
                desiredLocalTorque = LevelingTorque(2.2f, 2.8f);
                hoverCaptured = false;
            }
            else if (frame.hoverBrake)
            {
                if (!hoverCaptured)
                {
                    hoverCaptured = true;
                    hoverPosition = body.position;
                    hoverYaw = body.rotation.eulerAngles.y;
                }
                if ((body.position - hoverPosition).sqrMagnitude > 10000f)
                    hoverPosition = body.position;
                Vector3 desiredWorldAcceleration =
                    (hoverPosition - body.position) * 1.2f
                    - body.velocity * 2.4f
                    - gravity;
                desiredWorldAcceleration =
                    Vector3.ClampMagnitude(desiredWorldAcceleration, 35f);
                desiredLocalForce = transform.InverseTransformDirection(
                    desiredWorldAcceleration * body.mass);
                desiredLocalTorque = HoldLevelTorque(hoverYaw);
            }
            else
            {
                hoverCaptured = false;
                float boost = frame.boost ? 1.75f : 1f;
                Vector3 targetLocalVelocity = Vector3.Scale(
                    frame.translation,
                    new Vector3(15f, 8f, 25f)) * boost;
                Vector3 localVelocity = transform.InverseTransformDirection(
                    body.velocity);
                Vector3 desiredLocalAcceleration =
                    (targetLocalVelocity - localVelocity) * 2.8f;
                desiredLocalAcceleration =
                    Vector3.ClampMagnitude(desiredLocalAcceleration, 35f);
                Vector3 supportLocal = transform.InverseTransformDirection(
                    -gravity);
                desiredLocalForce =
                    (desiredLocalAcceleration + supportLocal) * body.mass;
                desiredLocalTorque = RateControlTorque();
            }

            allocator.SolveAndCollect(
                body,
                desiredLocalForce,
                desiredLocalTorque,
                energy,
                ledger,
                Time.fixedDeltaTime);
        }

        Vector3 RateControlTorque()
        {
            Vector3 maximumRate = new Vector3(1.4f, 1.2f, 1.4f);
            Vector3 targetRate = new Vector3(
                -frame.look.y * maximumRate.x,
                frame.look.x * maximumRate.y,
                -frame.roll * maximumRate.z);
            Vector3 localAngularVelocity =
                transform.InverseTransformDirection(body.angularVelocity);
            Vector3 acceleration =
                (targetRate - localAngularVelocity) * 4f;
            if (frame.look.sqrMagnitude < 0.0025f
                && Mathf.Abs(frame.roll) < 0.05f)
            {
                Vector3 levelAxis = transform.InverseTransformDirection(
                    Vector3.Cross(transform.up, Vector3.up));
                acceleration += levelAxis * 2.2f;
            }
            return AngularAccelerationToTorque(acceleration);
        }

        Vector3 LevelingTorque(float levelGain, float damping)
        {
            Vector3 levelAxis = transform.InverseTransformDirection(
                Vector3.Cross(transform.up, Vector3.up));
            Vector3 localAngularVelocity =
                transform.InverseTransformDirection(body.angularVelocity);
            return AngularAccelerationToTorque(
                levelAxis * levelGain
                - localAngularVelocity * damping);
        }

        Vector3 HoldLevelTorque(float yaw)
        {
            Quaternion target = Quaternion.Euler(0f, yaw, 0f);
            Quaternion error =
                target * Quaternion.Inverse(body.rotation);
            error.ToAngleAxis(out float angleDegrees, out Vector3 worldAxis);
            if (angleDegrees > 180f)
                angleDegrees -= 360f;
            Vector3 localAxis = worldAxis.sqrMagnitude > 0.0001f
                ? transform.InverseTransformDirection(worldAxis.normalized)
                : Vector3.zero;
            Vector3 localAngularVelocity =
                transform.InverseTransformDirection(body.angularVelocity);
            Vector3 acceleration =
                localAxis * angleDegrees * Mathf.Deg2Rad * 6f
                - localAngularVelocity * 4f;
            return AngularAccelerationToTorque(acceleration);
        }

        Vector3 AngularAccelerationToTorque(Vector3 localAcceleration)
        {
            Quaternion principalRotation = body.inertiaTensorRotation;
            Vector3 principalAcceleration =
                Quaternion.Inverse(principalRotation) * localAcceleration;
            Vector3 principalTorque = Vector3.Scale(
                body.inertiaTensor,
                principalAcceleration);
            return principalRotation * principalTorque;
        }

        void AddBodyAerodynamics()
        {
            if (body.velocity.sqrMagnitude <= 0.01f || airDensity <= 0f)
                return;
            Vector3 localVelocity =
                transform.InverseTransformDirection(body.velocity);
            Vector3 dragCoefficient = new Vector3(0.8f, 0.9f, 0.55f);
            Vector3 localDrag = new Vector3(
                -0.5f * airDensity * dragCoefficient.x
                * aerodynamicArea.x * localVelocity.x
                * Mathf.Abs(localVelocity.x),
                -0.5f * airDensity * dragCoefficient.y
                * aerodynamicArea.y * localVelocity.y
                * Mathf.Abs(localVelocity.y),
                -0.5f * airDensity * dragCoefficient.z
                * aerodynamicArea.z * localVelocity.z
                * Mathf.Abs(localVelocity.z));
            ledger.AddForce(transform.TransformDirection(localDrag));
        }

        void AuditAssembly()
        {
            if (!assemblyDirty && Time.unscaledTime < nextAssemblyAudit)
                return;
            nextAssemblyAudit = Time.unscaledTime + 0.5f;
            int currentLiveModules = GetComponentsInChildren<
                    NeoXBehaviorModule>(false)
                .Count(module => module != null && module.isActiveAndEnabled);
            if (assemblyDirty || currentLiveModules != liveModuleCount)
                Rebuild();
        }

        void CollectWingAerodynamics()
        {
            if (airDensity <= 0f)
                return;
            foreach (NeoXBehaviorModule module in modules)
            {
                if (module == null ||
                    (module.BehaviorKind != GridModuleBehaviorKind.Wing &&
                     module.BehaviorKind !=
                     GridModuleBehaviorKind.ControlSurface))
                {
                    continue;
                }
                Vector3 pointVelocity =
                    body.GetPointVelocity(module.transform.position);
                Vector3 localVelocity =
                    transform.InverseTransformDirection(pointVelocity);
                float forwardSpeed = Mathf.Abs(localVelocity.z);
                if (forwardSpeed < 1f)
                    continue;
                float angleOfAttack = Mathf.Atan2(
                    -localVelocity.y,
                    Mathf.Max(0.1f, forwardSpeed));
                float area = Mathf.Max(0.5f, module.Strength);
                float dynamicPressure =
                    0.5f * airDensity * forwardSpeed * forwardSpeed;
                float liftCoefficient = Mathf.Clamp(
                    Mathf.Sin(angleOfAttack * 2f) * 1.35f,
                    -1.25f,
                    1.25f);
                float dragCoefficient =
                    0.04f + 0.65f * Mathf.Sin(angleOfAttack)
                    * Mathf.Sin(angleOfAttack);
                Vector3 lift = transform.up
                               * dynamicPressure
                               * area
                               * liftCoefficient;
                Vector3 drag = -pointVelocity.normalized
                               * dynamicPressure
                               * area
                               * dragCoefficient;
                ledger.AddForceAtPosition(
                    lift + drag,
                    module.transform.position);
            }
        }

        void ApplyPendingImpulses()
        {
            if (pendingImpulses.Count == 0)
                return;
            Vector3 impulse = Vector3.zero;
            Vector3 angularImpulse = Vector3.zero;
            foreach (PendingImpulse item in pendingImpulses)
            {
                impulse += item.impulse;
                angularImpulse += Vector3.Cross(
                    item.position - body.worldCenterOfMass,
                    item.impulse);
            }
            pendingImpulses.Clear();
            body.AddForce(impulse, ForceMode.Impulse);
            body.AddTorque(angularImpulse, ForceMode.Impulse);
        }

        Bounds CalculateLocalBounds()
        {
            bool initializedBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;
                EncapsulateWorldBounds(
                    renderer.bounds,
                    ref bounds,
                    ref initializedBounds);
            }
            if (!initializedBounds)
            {
                Collider[] colliders =
                    GetComponentsInChildren<Collider>(true);
                foreach (Collider collider in colliders)
                {
                    if (collider == null || !collider.enabled ||
                        collider.isTrigger)
                    {
                        continue;
                    }
                    EncapsulateWorldBounds(
                        collider.bounds,
                        ref bounds,
                        ref initializedBounds);
                }
            }
            return bounds;
        }

        void EncapsulateWorldBounds(
            Bounds worldBounds,
            ref Bounds localBounds,
            ref bool initializedBounds)
        {
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 point = transform.InverseTransformPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z));
                if (!initializedBounds)
                {
                    localBounds = new Bounds(point, Vector3.zero);
                    initializedBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(point);
                }
            }
        }

        void UpdateTelemetry(int grounded)
        {
            float weight = Mathf.Max(
                1f,
                body.mass * Mathf.Max(0.1f, gravity.magnitude));
            float positiveLift = allocator.PositiveForce.y;
            Telemetry = new VehicleTelemetryV2
            {
                requestedMode = RequestedMode,
                actualMode = Mode,
                energy = energy.Energy,
                energyCapacity = energy.Capacity,
                controlAuthority = allocator.ControlAuthority,
                liftToWeight = positiveLift / weight,
                hoverMargin = positiveLift - weight,
                positiveAcceleration =
                    allocator.PositiveForce / Mathf.Max(1f, body.mass),
                negativeAcceleration =
                    allocator.NegativeForce / Mathf.Max(1f, body.mass),
                localVelocity =
                    transform.InverseTransformDirection(body.velocity),
                localAngularVelocity =
                    transform.InverseTransformDirection(body.angularVelocity),
                centerOfMass = body.centerOfMass,
                groundedWheels = grounded,
                autoHover = frame.hoverBrake,
                energyCutoff = energy.IsCutOff,
                status = Telemetry.status
            };
        }

        void SetStatus(string message)
        {
            VehicleTelemetryV2 value = Telemetry;
            value.status = message;
            Telemetry = value;
        }

        void OnGUI()
        {
            if (!IsActive)
                return;
            VehicleTelemetryV2 value = Telemetry;
            GUI.Box(new Rect(14f, 14f, 270f, 126f), string.Empty);
            GUI.Label(
                new Rect(26f, 22f, 250f, 112f),
                $"Motion V2  {value.actualMode}\n"
                + $"Energy  {value.energy:0}/{value.energyCapacity:0}"
                + (value.energyCutoff ? "  CUTOFF" : string.Empty) + "\n"
                + $"Lift/Weight  {value.liftToWeight:0.00}\n"
                + $"Authority  {value.controlAuthority:P0}\n"
                + $"Wheels  {value.groundedWheels}"
                + (value.autoHover ? "  AUTO HOVER" : string.Empty));
        }

        void OnTransformChildrenChanged()
        {
            if (initialized)
                assemblyDirty = true;
        }
    }

    [DisallowMultipleComponent]
    public sealed class VehicleModulePhysicsNotifierV2 : MonoBehaviour
    {
        VehicleMotionCoordinatorV2 coordinator;

        public void Bind(VehicleMotionCoordinatorV2 owner)
        {
            coordinator = owner;
        }

        void OnDisable()
        {
            if (Application.isPlaying)
                coordinator?.NotifyModulePhysicsChanged();
        }

        void OnDestroy()
        {
            if (Application.isPlaying)
                coordinator?.NotifyModulePhysicsChanged();
        }
    }
}
