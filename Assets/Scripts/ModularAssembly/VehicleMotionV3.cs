using System;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public enum VehicleAssistLevel
    {
        WeakCore,
        AttitudeOnly,
        Disabled
    }

    public enum VehicleControlScheme
    {
        Assisted,
        ManualGroups
    }

    [Serializable]
    public sealed class VehicleControlGroupData
    {
        public string groupName;
        public KeyCode binding;
        public bool toggleMode;
        [NonSerialized] public bool toggled;

        public VehicleControlGroupData Clone()
        {
            return new VehicleControlGroupData
            {
                groupName = groupName,
                binding = binding,
                toggleMode = toggleMode
            };
        }

        public static VehicleControlGroupData[] CreateDefaults()
        {
            KeyCode[] keys =
            {
                KeyCode.Z, KeyCode.C, KeyCode.V, KeyCode.B,
                KeyCode.N, KeyCode.M, KeyCode.Comma, KeyCode.Period
            };
            var result = new VehicleControlGroupData[8];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = new VehicleControlGroupData
                {
                    groupName = "推进组 " + (index + 1),
                    binding = keys[index],
                    toggleMode = false
                };
            }
            return result;
        }

        public static VehicleControlGroupData[] Normalize(
            VehicleControlGroupData[] source)
        {
            VehicleControlGroupData[] defaults = CreateDefaults();
            if (source == null)
                return defaults;
            for (int index = 0; index < defaults.Length; index++)
            {
                if (index >= source.Length || source[index] == null)
                    continue;
                defaults[index] = source[index].Clone();
                if (string.IsNullOrWhiteSpace(defaults[index].groupName))
                    defaults[index].groupName = "推进组 " + (index + 1);
                if (IsReserved(defaults[index].binding))
                    defaults[index].binding = KeyCode.None;
            }
            return defaults;
        }

        public static bool IsReserved(KeyCode key)
        {
            return key == KeyCode.F5 || key == KeyCode.R ||
                   key == KeyCode.Escape || key == KeyCode.Delete;
        }
    }

    [Serializable]
    public struct VehicleMassProperties
    {
        public float totalMass;
        public Vector3 centerOfMassLocal;
        public Vector3 inertiaTensor;
        public Quaternion inertiaTensorRotation;
    }

    [Serializable]
    public struct VehicleControlAuthority
    {
        public int controlRank;
        public Vector3 positiveLinearAcceleration;
        public Vector3 negativeLinearAcceleration;
        public Vector3 positiveAngularAcceleration;
        public Vector3 negativeAngularAcceleration;
        public float hoverRatio;
        public float coreAssistUsage;
        public float forwardCrossCoupling;
        public float estimatedTerminalSpeed;
        public string status;
    }

    public sealed class VehicleTelemetryV3
    {
        public VehicleMassProperties mass;
        public VehicleControlAuthority authority;
        public float energy;
        public float energyCapacity;
        public bool grounded;
        public int groundedWheels;
        public Vector3 thrustCenterLocal;
        public Vector3 liftCenterLocal;

        public string BuildSummary()
        {
            Vector3 inertia = mass.inertiaTensor;
            Vector3 linear = authority.positiveLinearAcceleration;
            Vector3 angular = authority.positiveAngularAcceleration *
                              Mathf.Rad2Deg;
            return
                $"质量 {mass.totalMass:0} kg\n" +
                $"惯量 {inertia.x:0}/{inertia.y:0}/{inertia.z:0} kg·m²\n" +
                $"升重比 {authority.hoverRatio:0.00}  " +
                $"控制秩 {authority.controlRank}/6\n" +
                $"线加速度 X{linear.x:0.0} Y{linear.y:0.0} Z{linear.z:0.0} m/s²\n" +
                $"角加速度 X{angular.x:0} Y{angular.y:0} Z{angular.z:0} °/s²\n" +
                $"预计极速 {authority.estimatedTerminalSpeed:0} m/s\n" +
                $"前进串轴 {authority.forwardCrossCoupling * 100f:0}%  " +
                $"核心占用 {authority.coreAssistUsage * 100f:0}%\n" +
                $"能源 {energy:0}/{energyCapacity:0}\n" +
                authority.status;
        }
    }

    public sealed class VehicleActuatorDescriptor
    {
        public NeoXBehaviorModule module;
        public NeoXThrusterExhaustVfx exhaust;
        public Vector3 localPosition;
        public Vector3 localDirection;
        public float maximumForce;
        public float responseTime;
        public float energyPerSecond;
        public int manualGroupId;
        public bool atmosphereOnly;
        public float targetThrottle;
        public float actualThrottle;
    }

    public struct VehicleAllocatorResult
    {
        public Vector3 forceWorld;
        public Vector3 torqueWorld;
        public Vector3 residualForceWorld;
        public Vector3 residualTorqueWorld;
        public bool saturated;
    }

    public sealed class VehicleControlAllocatorV3
    {
        readonly List<VehicleActuatorDescriptor> actuators =
            new List<VehicleActuatorDescriptor>();
        float[,] matrix = new float[6, 0];
        float[] throttle = Array.Empty<float>();
        float forceScale = 1f;
        float torqueScale = 1f;
        Vector3 centerOfMassLocal;
        int rank;

        public IReadOnlyList<VehicleActuatorDescriptor> Actuators =>
            actuators;
        public int ControlRank => rank;

        public void Rebuild(
            IEnumerable<VehicleActuatorDescriptor> source,
            Vector3 centerOfMassLocal)
        {
            this.centerOfMassLocal = centerOfMassLocal;
            actuators.Clear();
            if (source != null)
                actuators.AddRange(source.Where(item => item != null));
            throttle = new float[actuators.Count];
            matrix = new float[6, actuators.Count];
            forceScale = Mathf.Max(
                1000f,
                actuators.Sum(item => item.maximumForce));
            float armScale = actuators.Count == 0
                ? 1f
                : Mathf.Max(
                    1f,
                    actuators.Max(item =>
                        (item.localPosition - centerOfMassLocal).magnitude));
            torqueScale = Mathf.Max(1000f, forceScale * armScale);

            for (int column = 0; column < actuators.Count; column++)
            {
                VehicleActuatorDescriptor actuator = actuators[column];
                Vector3 force = actuator.localDirection *
                                actuator.maximumForce;
                Vector3 torque = Vector3.Cross(
                    actuator.localPosition - centerOfMassLocal,
                    force);
                matrix[0, column] = force.x / forceScale;
                matrix[1, column] = force.y / forceScale;
                matrix[2, column] = force.z / forceScale;
                matrix[3, column] = torque.x / torqueScale;
                matrix[4, column] = torque.y / torqueScale;
                matrix[5, column] = torque.z / torqueScale;
            }
            rank = CalculateRank(matrix);
        }

        public VehicleAllocatorResult Solve(
            Transform vehicle,
            Vector3 desiredForceWorld,
            Vector3 desiredTorqueWorld,
            float airDensity,
            VehicleControlScheme scheme,
            VehicleControlGroupData[] groups,
            float deltaTime)
        {
            if (scheme == VehicleControlScheme.ManualGroups)
            {
                for (int index = 0; index < actuators.Count; index++)
                {
                    int groupId = actuators[index].manualGroupId;
                    throttle[index] = GroupActive(groups, groupId) ? 1f : 0f;
                }
            }
            else
            {
                SolveAssisted(
                    vehicle.InverseTransformDirection(desiredForceWorld),
                    vehicle.InverseTransformDirection(desiredTorqueWorld));
            }

            Vector3 forceLocal = Vector3.zero;
            Vector3 torqueLocal = Vector3.zero;
            bool saturated = false;
            for (int index = 0; index < actuators.Count; index++)
            {
                VehicleActuatorDescriptor actuator = actuators[index];
                float atmosphereFactor = actuator.atmosphereOnly
                    ? Mathf.Clamp01(airDensity / 1.225f)
                    : 1f;
                actuator.targetThrottle =
                    Mathf.Clamp01(throttle[index]) * atmosphereFactor;
                float response = Mathf.Max(0.01f, actuator.responseTime);
                actuator.actualThrottle = Mathf.MoveTowards(
                    actuator.actualThrottle,
                    actuator.targetThrottle,
                    deltaTime / response);
                float magnitude = actuator.maximumForce *
                                  actuator.actualThrottle;
                Vector3 force = actuator.localDirection * magnitude;
                forceLocal += force;
                torqueLocal += Vector3.Cross(
                    actuator.localPosition - centerOfMassLocal,
                    force);
                saturated |= actuator.targetThrottle > 0.995f;
            }

            if (scheme == VehicleControlScheme.Assisted)
            {
                torqueLocal = FilterCommandedTorque(
                    torqueLocal,
                    vehicle.InverseTransformDirection(
                        desiredTorqueWorld));
            }
            Vector3 forceWorld = vehicle.TransformDirection(forceLocal);
            Vector3 torqueWorld = vehicle.TransformDirection(torqueLocal);
            return new VehicleAllocatorResult
            {
                forceWorld = forceWorld,
                torqueWorld = torqueWorld,
                residualForceWorld = desiredForceWorld - forceWorld,
                residualTorqueWorld = desiredTorqueWorld - torqueWorld,
                saturated = saturated
            };
        }

        static Vector3 FilterCommandedTorque(
            Vector3 availableTorque,
            Vector3 desiredTorque)
        {
            if (desiredTorque.sqrMagnitude < 0.0001f)
                return Vector3.zero;
            Vector3 direction = desiredTorque.normalized;
            float alongDesired = Vector3.Dot(
                availableTorque,
                direction);
            if (alongDesired <= 0f)
                return Vector3.zero;
            return direction * Mathf.Min(
                alongDesired,
                desiredTorque.magnitude);
        }

        public void Reset()
        {
            Array.Clear(throttle, 0, throttle.Length);
            foreach (VehicleActuatorDescriptor actuator in actuators)
            {
                actuator.targetThrottle = 0f;
                actuator.actualThrottle = 0f;
                actuator.exhaust?.SetTargetThrottle(0f);
            }
        }

        void SolveAssisted(Vector3 forceLocal, Vector3 torqueLocal)
        {
            int count = actuators.Count;
            if (count == 0)
                return;
            float[] target =
            {
                forceLocal.x / forceScale,
                forceLocal.y / forceScale,
                forceLocal.z / forceScale,
                torqueLocal.x / torqueScale,
                torqueLocal.y / torqueScale,
                torqueLocal.z / torqueScale
            };
            const float regularization = 0.004f;
            float columnEnergy = regularization;
            for (int row = 0; row < 6; row++)
            for (int column = 0; column < count; column++)
                columnEnergy += matrix[row, column] *
                                matrix[row, column];
            float step = 0.9f / Mathf.Max(0.02f, columnEnergy);

            for (int iteration = 0; iteration < 36; iteration++)
            {
                float[] error = new float[6];
                for (int row = 0; row < 6; row++)
                {
                    float value = -target[row];
                    for (int column = 0; column < count; column++)
                        value += matrix[row, column] * throttle[column];
                    error[row] = value;
                }
                for (int column = 0; column < count; column++)
                {
                    float gradient =
                        regularization * throttle[column];
                    for (int row = 0; row < 6; row++)
                        gradient += matrix[row, column] * error[row];
                    throttle[column] = Mathf.Clamp01(
                        throttle[column] - gradient * step);
                }
            }
        }

        static bool GroupActive(
            VehicleControlGroupData[] groups,
            int groupId)
        {
            if (groups == null || groupId < 0 || groupId >= groups.Length ||
                groups[groupId] == null)
                return false;
            VehicleControlGroupData group = groups[groupId];
            return group.toggleMode
                ? group.toggled
                : group.binding != KeyCode.None &&
                  Input.GetKey(group.binding);
        }

        static int CalculateRank(float[,] source)
        {
            int columns = source.GetLength(1);
            if (columns == 0)
                return 0;
            var gram = new float[6, 6];
            for (int row = 0; row < 6; row++)
            for (int other = 0; other < 6; other++)
            for (int column = 0; column < columns; column++)
                gram[row, other] +=
                    source[row, column] * source[other, column];

            int result = 0;
            for (int pivot = 0; pivot < 6; pivot++)
            {
                int best = pivot;
                for (int row = pivot + 1; row < 6; row++)
                    if (Mathf.Abs(gram[row, pivot]) >
                        Mathf.Abs(gram[best, pivot]))
                        best = row;
                if (Mathf.Abs(gram[best, pivot]) < 0.00001f)
                    continue;
                if (best != pivot)
                    for (int column = 0; column < 6; column++)
                    {
                        float temporary = gram[pivot, column];
                        gram[pivot, column] = gram[best, column];
                        gram[best, column] = temporary;
                    }
                float divisor = gram[pivot, pivot];
                for (int column = pivot; column < 6; column++)
                    gram[pivot, column] /= divisor;
                for (int row = 0; row < 6; row++)
                {
                    if (row == pivot)
                        continue;
                    float factor = gram[row, pivot];
                    for (int column = pivot; column < 6; column++)
                        gram[row, column] -=
                            factor * gram[pivot, column];
                }
                result++;
            }
            return result;
        }
    }

    public sealed class VehicleMotionCoordinatorV3 : MonoBehaviour
    {
        const float CoreUpForce = 13000f;
        const float CoreDownForce = 3000f;
        const float CorePlanarForce = 2500f;
        const float CoreTorque = 1200f;
        const float CoreRechargePerSecond = 12f;
        const float MaxAssistedAngularAcceleration = 1.2f;

        readonly VehicleControlAllocatorV3 allocator =
            new VehicleControlAllocatorV3();
        readonly List<VehicleActuatorDescriptor> actuators =
            new List<VehicleActuatorDescriptor>();
        readonly List<AeroSurface> aeroSurfaces =
            new List<AeroSurface>();
        readonly List<ModularWheelRuntime> wheels =
            new List<ModularWheelRuntime>();
        readonly List<PendingImpulse> impulses =
            new List<PendingImpulse>();
        readonly VehicleTelemetryV3 telemetry =
            new VehicleTelemetryV3();

        Rigidbody body;
        ShipAssembly assembly;
        ModularAssemblyLabController labController;
        GridAssemblyModel model;
        VehicleForceLedger ledger;
        VehicleAssistLevel assistLevel = VehicleAssistLevel.WeakCore;
        VehicleControlScheme controlScheme = VehicleControlScheme.Assisted;
        VehicleControlGroupData[] controlGroups =
            VehicleControlGroupData.CreateDefaults();
        VehicleInputFrame input;
        Vector3 gravity = Vector3.down * 9.81f;
        float airDensity = 1.225f;
        float altitude;
        float energy = 100f;
        float energyCapacity = 100f;
        Vector3 bodyDragArea = Vector3.one;
        bool active;
        bool holdActive;
        Vector3 holdPosition;
        Quaternion holdRotation;
        float groundedTimer;
        int lastViewCount = -1;
        float nextIntegrityScan;
        Vector3 aimForwardWorld = Vector3.forward;

        public bool IsActive => active;
        public VehicleAssistLevel AssistLevel => assistLevel;
        public VehicleControlScheme ControlScheme => controlScheme;
        public VehicleTelemetryV3 Telemetry => telemetry;
        public IReadOnlyList<VehicleActuatorDescriptor> Actuators =>
            actuators;

        public void Configure(Rigidbody target, ShipAssembly shipAssembly)
        {
            body = target;
            assembly = shipAssembly;
            ledger = new VehicleForceLedger();
            labController = FindObjectOfType<ModularAssemblyLabController>();
            BindModel(labController != null ? labController.Model : null);
            Rebuild();
            enabled = true;
        }

        public void BeginFlight()
        {
            if (body == null)
                return;
            Rebuild();
            energy = energyCapacity;
            telemetry.energy = energy;
            ResetControllerState();
            body.useGravity = false;
            body.maxAngularVelocity = 12f;
            foreach (ModularWheelRuntime wheel in wheels)
                wheel.SetFlightMode(true);
            active = true;
        }

        public void EndFlight()
        {
            active = false;
            ResetControllerState();
            foreach (ModularWheelRuntime wheel in wheels)
                if (wheel != null)
                    wheel.SetFlightMode(false);
        }

        public void ResetControllerState()
        {
            allocator.Reset();
            input = default;
            aimForwardWorld = transform.forward;
            holdActive = false;
            groundedTimer = 0f;
            impulses.Clear();
            foreach (VehicleControlGroupData group in controlGroups)
                if (group != null)
                    group.toggled = false;
        }

        public void SetAssistLevel(VehicleAssistLevel value)
        {
            assistLevel = value;
            if (model != null)
                model.SetVehicleControlSettings(
                    controlScheme,
                    assistLevel,
                    controlGroups);
            RefreshAuthority();
        }

        public void SetControlScheme(VehicleControlScheme value)
        {
            controlScheme = value;
            if (model != null)
                model.SetVehicleControlSettings(
                    controlScheme,
                    assistLevel,
                    controlGroups);
            allocator.Reset();
        }

        public void SetPlanetEnvironment(
            Vector3 acceleration,
            float density,
            float heightAboveGround)
        {
            gravity = acceleration;
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
            Vector3 position)
        {
            impulses.Add(new PendingImpulse
            {
                impulse = impulse,
                position = position
            });
        }

        void Update()
        {
            if (!active)
                return;
            input = CaptureInput();
            if (!input.freeLook)
            {
                Camera viewCamera = Camera.main;
                Vector3 candidate = viewCamera != null
                    ? viewCamera.transform.forward
                    : transform.forward;
                if (candidate.sqrMagnitude > 0.0001f)
                    aimForwardWorld = candidate.normalized;
            }
            if (controlScheme == VehicleControlScheme.ManualGroups)
            {
                foreach (VehicleControlGroupData group in controlGroups)
                {
                    if (group == null || !group.toggleMode ||
                        group.binding == KeyCode.None)
                        continue;
                    if (Input.GetKeyDown(group.binding))
                        group.toggled = !group.toggled;
                }
            }

            if (input.hoverBrake && !holdActive)
            {
                holdActive = true;
                holdPosition = body.position;
                holdRotation = body.rotation;
            }
            else if (!input.hoverBrake)
            {
                holdActive = false;
            }
        }

        void FixedUpdate()
        {
            if (!active || body == null || body.isKinematic)
                return;
            if (Time.time >= nextIntegrityScan)
            {
                nextIntegrityScan = Time.time + 0.5f;
                int count = GetCurrentViews().Length;
                if (count != lastViewCount)
                    Rebuild();
            }

            ledger.Begin(body);
            ledger.AddForce(body.mass * gravity);

            int groundedCount = ProbeWheels();
            bool groundMode = groundedCount >= 2 &&
                              groundedTimer >= 0.2f &&
                              input.translation.y <= 0.05f &&
                              controlScheme == VehicleControlScheme.Assisted;
            if (groundMode)
                SubmitWheelForces(groundedCount);

            SubmitBodyDrag();
            SubmitAerodynamics();

            Vector3 desiredForce = Vector3.zero;
            Vector3 desiredTorque = Vector3.zero;
            if (!groundMode &&
                controlScheme == VehicleControlScheme.Assisted)
            {
                BuildAssistedWrench(
                    out desiredForce,
                    out desiredTorque);
            }

            VehicleAllocatorResult allocation = allocator.Solve(
                transform,
                desiredForce,
                controlScheme == VehicleControlScheme.Assisted
                    ? Vector3.zero
                    : desiredTorque,
                airDensity,
                controlScheme,
                controlGroups,
                Time.fixedDeltaTime);

            Vector3 assistedAttitudeTorque =
                controlScheme == VehicleControlScheme.Assisted
                    ? ResolveAssistedAttitudeTorque(desiredTorque)
                    : Vector3.zero;

            float actuatorEnergy = 0f;
            foreach (VehicleActuatorDescriptor actuator in actuators)
                actuatorEnergy += actuator.actualThrottle *
                                  actuator.energyPerSecond;

            Vector3 coreForce = Vector3.zero;
            Vector3 coreTorqueWorld = Vector3.zero;
            float coreUsage = 0f;
            if (controlScheme == VehicleControlScheme.Assisted)
            {
                ResolveCoreAssist(
                    allocation.residualForceWorld,
                    Vector3.zero,
                    groundMode,
                    out coreForce,
                    out coreTorqueWorld,
                    out coreUsage);
            }

            float requestedEnergy =
                actuatorEnergy + CoreEnergyCost(
                    coreForce,
                    coreTorqueWorld + assistedAttitudeTorque);
            float energyScale = ResolveEnergyScale(requestedEnergy);
            SubmitActuators(energyScale);
            ledger.AddForce(coreForce * energyScale);
            ledger.AddTorque(
                (coreTorqueWorld + assistedAttitudeTorque) *
                energyScale);

            for (int index = 0; index < impulses.Count; index++)
            {
                PendingImpulse impulse = impulses[index];
                body.AddForceAtPosition(
                    impulse.impulse,
                    impulse.position,
                    ForceMode.Impulse);
            }
            impulses.Clear();
            ledger.Apply();

            telemetry.energy = energy;
            telemetry.energyCapacity = energyCapacity;
            telemetry.grounded = groundMode;
            telemetry.groundedWheels = groundedCount;
            VehicleControlAuthority authority = telemetry.authority;
            authority.coreAssistUsage = coreUsage;
            telemetry.authority = authority;
        }

        void BuildAssistedWrench(
            out Vector3 desiredForce,
            out Vector3 desiredTorque)
        {
            float boost = input.boost ? 1.65f : 1f;
            Vector3 worldUp = gravity.sqrMagnitude > 0.01f
                ? -gravity.normalized
                : Vector3.up;
            Vector3 planarForward = Vector3.ProjectOnPlane(
                aimForwardWorld,
                worldUp);
            if (planarForward.sqrMagnitude < 0.0001f)
            {
                planarForward = Vector3.ProjectOnPlane(
                    transform.forward,
                    worldUp);
            }
            planarForward.Normalize();
            Vector3 planarRight = Vector3.Cross(
                worldUp,
                planarForward).normalized;
            Vector3 targetVelocity =
                (planarRight * input.translation.x * 18f +
                 worldUp * input.translation.y * 12f +
                 planarForward * input.translation.z * 32f) *
                boost;
            Vector3 acceleration;
            if (holdActive)
            {
                Vector3 positionError = holdPosition - body.position;
                acceleration = positionError * 1.8f -
                               body.velocity * 3.2f;
            }
            else
            {
                float velocityGain =
                    input.translation.sqrMagnitude > 0.01f ? 1.8f : 0.28f;
                acceleration =
                    (targetVelocity - body.velocity) * velocityGain;
            }
            acceleration = Vector3.ClampMagnitude(acceleration, 35f);
            desiredForce = body.mass * (acceleration - gravity);

            Vector3 desiredForward =
                aimForwardWorld.sqrMagnitude > 0.0001f
                    ? aimForwardWorld.normalized
                    : transform.forward;
            Vector3 desiredUp = Vector3.ProjectOnPlane(
                worldUp,
                desiredForward);
            if (desiredUp.sqrMagnitude < 0.0001f)
                desiredUp = transform.up;
            desiredUp.Normalize();
            Quaternion targetRotation = holdActive
                ? holdRotation
                : Quaternion.LookRotation(
                    desiredForward,
                    desiredUp);
            if (!holdActive && Mathf.Abs(input.roll) > 0.01f)
            {
                targetRotation =
                    Quaternion.AngleAxis(
                        -input.roll * 35f,
                        desiredForward) *
                    targetRotation;
            }
            Quaternion rotationError =
                targetRotation * Quaternion.Inverse(body.rotation);
            rotationError.ToAngleAxis(
                out float angle,
                out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            Vector3 targetAngularVelocityWorld =
                !float.IsNaN(axis.x)
                    ? Vector3.ClampMagnitude(
                        axis.normalized *
                        angle * Mathf.Deg2Rad * 3f,
                        3.2f)
                    : Vector3.zero;
            Vector3 angularError =
                transform.InverseTransformDirection(
                    targetAngularVelocityWorld -
                    body.angularVelocity);
            Vector3 torqueLocal = Vector3.Scale(
                angularError * 5.2f,
                body.inertiaTensor);
            desiredTorque =
                transform.TransformDirection(torqueLocal);
        }

        Vector3 ResolveAssistedAttitudeTorque(
            Vector3 desiredTorqueWorld)
        {
            if (assistLevel == VehicleAssistLevel.Disabled)
                return Vector3.zero;

            Vector3 authority = Vector3.one * CoreTorque;
            Vector3 centerOfMass =
                telemetry.mass.centerOfMassLocal;
            foreach (VehicleActuatorDescriptor actuator in actuators)
            {
                Vector3 fullForce =
                    actuator.localDirection *
                    actuator.maximumForce;
                Vector3 fullTorque = Vector3.Cross(
                    actuator.localPosition - centerOfMass,
                    fullForce);
                authority.x += Mathf.Abs(fullTorque.x) * 0.65f;
                authority.y += Mathf.Abs(fullTorque.y) * 0.65f;
                authority.z += Mathf.Abs(fullTorque.z) * 0.65f;
            }

            Vector3 inertiaLimit =
                body.inertiaTensor *
                MaxAssistedAngularAcceleration;
            authority = new Vector3(
                Mathf.Min(authority.x, inertiaLimit.x),
                Mathf.Min(authority.y, inertiaLimit.y),
                Mathf.Min(authority.z, inertiaLimit.z));
            Vector3 desiredLocal =
                transform.InverseTransformDirection(
                    desiredTorqueWorld);
            desiredLocal = new Vector3(
                Mathf.Clamp(
                    desiredLocal.x,
                    -authority.x,
                    authority.x),
                Mathf.Clamp(
                    desiredLocal.y,
                    -authority.y,
                    authority.y),
                Mathf.Clamp(
                    desiredLocal.z,
                    -authority.z,
                    authority.z));
            return transform.TransformDirection(desiredLocal);
        }

        void ResolveCoreAssist(
            Vector3 residualForceWorld,
            Vector3 residualTorqueWorld,
            bool groundMode,
            out Vector3 coreForceWorld,
            out Vector3 coreTorqueWorld,
            out float usage)
        {
            Vector3 forceLocal =
                transform.InverseTransformDirection(residualForceWorld);
            Vector3 torqueLocal =
                transform.InverseTransformDirection(residualTorqueWorld);
            if (assistLevel == VehicleAssistLevel.WeakCore && !groundMode)
            {
                forceLocal.x = Mathf.Clamp(
                    forceLocal.x,
                    -CorePlanarForce,
                    CorePlanarForce);
                forceLocal.z = Mathf.Clamp(
                    forceLocal.z,
                    -CorePlanarForce,
                    CorePlanarForce);
                forceLocal.y = Mathf.Clamp(
                    forceLocal.y,
                    -CoreDownForce,
                    CoreUpForce);
            }
            else
            {
                forceLocal = Vector3.zero;
            }

            if (assistLevel != VehicleAssistLevel.Disabled)
            {
                torqueLocal.x = Mathf.Clamp(
                    torqueLocal.x,
                    -CoreTorque,
                    CoreTorque);
                torqueLocal.y = Mathf.Clamp(
                    torqueLocal.y,
                    -CoreTorque,
                    CoreTorque);
                torqueLocal.z = Mathf.Clamp(
                    torqueLocal.z,
                    -CoreTorque,
                    CoreTorque);
            }
            else
            {
                torqueLocal = Vector3.zero;
            }

            coreForceWorld = transform.TransformDirection(forceLocal);
            coreTorqueWorld = transform.TransformDirection(torqueLocal);
            usage = Mathf.Max(
                Mathf.Abs(forceLocal.x) / CorePlanarForce,
                Mathf.Abs(forceLocal.z) / CorePlanarForce,
                forceLocal.y >= 0f
                    ? forceLocal.y / CoreUpForce
                    : -forceLocal.y / CoreDownForce,
                Mathf.Max(
                    Mathf.Abs(torqueLocal.x),
                    Mathf.Abs(torqueLocal.y),
                    Mathf.Abs(torqueLocal.z)) / CoreTorque);
        }

        void SubmitActuators(float energyScale)
        {
            Vector3 assistedForce = Vector3.zero;
            foreach (VehicleActuatorDescriptor actuator in actuators)
            {
                float throttle = actuator.actualThrottle * energyScale;
                Vector3 direction =
                    transform.TransformDirection(actuator.localDirection);
                Vector3 position =
                    transform.TransformPoint(actuator.localPosition);
                Vector3 force =
                    direction * actuator.maximumForce * throttle;
                if (controlScheme == VehicleControlScheme.Assisted)
                {
                    assistedForce += force;
                }
                else
                {
                    ledger.AddForceAtPosition(force, position);
                }
                actuator.exhaust?.SetTargetThrottle(throttle);
            }
            if (controlScheme == VehicleControlScheme.Assisted)
                ledger.AddForce(assistedForce);
        }

        int ProbeWheels()
        {
            int count = 0;
            foreach (ModularWheelRuntime wheel in wheels)
                if (wheel != null && wheel.ProbeContact(transform.up))
                    count++;
            if (count >= 2 &&
                Mathf.Abs(Vector3.Dot(body.velocity, transform.up)) < 2f)
                groundedTimer += Time.fixedDeltaTime;
            else
                groundedTimer = Mathf.Max(
                    0f,
                    groundedTimer - Time.fixedDeltaTime * 2f);
            return count;
        }

        void SubmitWheelForces(int groundedCount)
        {
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel == null || !wheel.IsGrounded)
                    continue;
                wheel.ApplyForces(
                    transform.up,
                    transform.forward,
                    input.translation.z,
                    input.translation.x,
                    input.hoverBrake,
                    input.boost,
                    body.mass,
                    groundedCount,
                    ledger);
            }
        }

        void SubmitBodyDrag()
        {
            if (airDensity <= 0.001f)
                return;
            Vector3 localVelocity =
                transform.InverseTransformDirection(body.velocity);
            Vector3 localForce = new Vector3(
                -0.5f * airDensity * 0.8f * bodyDragArea.x *
                localVelocity.x * Mathf.Abs(localVelocity.x),
                -0.5f * airDensity * 0.8f * bodyDragArea.y *
                localVelocity.y * Mathf.Abs(localVelocity.y),
                -0.5f * airDensity * 0.55f * bodyDragArea.z *
                localVelocity.z * Mathf.Abs(localVelocity.z));
            ledger.AddForce(transform.TransformDirection(localForce));
        }

        void SubmitAerodynamics()
        {
            if (airDensity <= 0.001f || aeroSurfaces.Count == 0)
                return;
            Vector3 weightedLiftCenter = Vector3.zero;
            float totalLift = 0f;
            foreach (AeroSurface surface in aeroSurfaces)
            {
                if (surface.root == null)
                    continue;
                Vector3 position =
                    transform.TransformPoint(surface.localPosition);
                Vector3 velocity = body.GetPointVelocity(position);
                float speed = velocity.magnitude;
                if (speed < 0.5f)
                    continue;

                Vector3 chord =
                    transform.TransformDirection(surface.localChord);
                Vector3 normal =
                    transform.TransformDirection(surface.localNormal);
                Vector3 span = Vector3.Cross(normal, chord).normalized;
                Vector3 planarVelocity =
                    Vector3.ProjectOnPlane(velocity, span);
                if (planarVelocity.sqrMagnitude < 0.1f)
                    continue;
                Vector3 flow = planarVelocity.normalized;
                float angle = Mathf.Asin(Mathf.Clamp(
                    Vector3.Dot(flow, normal),
                    -1f,
                    1f));
                float absoluteAngle = Mathf.Abs(angle) * Mathf.Rad2Deg;
                float stall = Mathf.Lerp(
                    1f,
                    0.32f,
                    Mathf.InverseLerp(24f, 58f, absoluteAngle));
                float liftCoefficient =
                    Mathf.Sin(angle * 2f) * 1.35f * stall;
                if (surface.controlSurface)
                {
                    float side = Mathf.Sign(
                        Vector3.Dot(
                            surface.localPosition -
                            telemetry.mass.centerOfMassLocal,
                            Vector3.right));
                    liftCoefficient +=
                        (-input.look.y + input.roll * side) * 0.28f;
                }
                float dragCoefficient =
                    0.025f + 0.12f * liftCoefficient *
                    liftCoefficient +
                    0.42f * Mathf.Sin(angle) * Mathf.Sin(angle);
                float pressure =
                    0.5f * airDensity * planarVelocity.sqrMagnitude;
                Vector3 liftDirection =
                    Vector3.Cross(flow, span).normalized;
                if (Vector3.Dot(liftDirection, normal) < 0f)
                    liftDirection = -liftDirection;
                Vector3 lift =
                    liftDirection * pressure * surface.area *
                    liftCoefficient;
                Vector3 drag =
                    -flow * pressure * surface.area * dragCoefficient;
                if (controlScheme == VehicleControlScheme.Assisted)
                    ledger.AddForce(lift + drag);
                else
                    ledger.AddForceAtPosition(lift + drag, position);
                float magnitude = lift.magnitude;
                weightedLiftCenter += surface.localPosition * magnitude;
                totalLift += magnitude;
            }
            if (totalLift > 0.01f)
            {
                telemetry.liftCenterLocal =
                    weightedLiftCenter / totalLift;
            }
        }

        float ResolveEnergyScale(float requestedPerSecond)
        {
            energy = Mathf.Min(
                energyCapacity,
                energy + CoreRechargePerSecond * Time.fixedDeltaTime);
            float requested =
                Mathf.Max(0f, requestedPerSecond) * Time.fixedDeltaTime;
            if (requested <= 0.0001f)
                return 1f;
            float scale = Mathf.Clamp01(energy / requested);
            energy = Mathf.Max(0f, energy - requested * scale);
            return scale;
        }

        static float CoreEnergyCost(
            Vector3 forceWorld,
            Vector3 torqueWorld)
        {
            float forceRatio = forceWorld.magnitude /
                               Mathf.Max(1f, CoreUpForce);
            float torqueRatio = torqueWorld.magnitude /
                                Mathf.Max(1f, CoreTorque);
            return forceRatio * 10f + torqueRatio * 3f;
        }

        public void Rebuild()
        {
            if (body == null)
                return;
            GridModuleView[] views = GetCurrentViews();
            lastViewCount = views.Length;
            BuildMassProperties(views);
            BuildActuatorsAndSurfaces(views);
            BuildWheelSet();
            allocator.Rebuild(
                actuators,
                telemetry.mass.centerOfMassLocal);
            RefreshEnergyCapacity(views);
            RefreshAuthority();
        }

        void BuildMassProperties(GridModuleView[] views)
        {
            var modules = new List<MassElement>();
            float totalMass = 0f;
            Vector3 weightedCenter = Vector3.zero;
            foreach (GridModuleView view in views)
            {
                if (view == null || view.Record?.Definition == null)
                    continue;
                float mass = Mathf.Max(
                    0.01f,
                    view.Record.Definition.MassKg);
                Vector3 center =
                    transform.InverseTransformPoint(view.transform.position);
                Vector3Int footprint =
                    view.Record.Definition.Footprint;
                Vector3 size = new Vector3(
                    Mathf.Max(0.1f, footprint.x),
                    Mathf.Max(0.1f, footprint.y),
                    Mathf.Max(0.1f, footprint.z));
                Quaternion rotation =
                    Quaternion.Inverse(transform.rotation) *
                    view.transform.rotation;
                modules.Add(new MassElement
                {
                    mass = mass,
                    center = center,
                    size = size,
                    rotation = rotation
                });
                totalMass += mass;
                weightedCenter += center * mass;
            }
            if (modules.Count == 0)
            {
                totalMass = Mathf.Max(1f, body.mass);
                weightedCenter = Vector3.zero;
            }
            Vector3 centerOfMass =
                weightedCenter / Mathf.Max(0.01f, totalMass);
            double[,] tensor = new double[3, 3];
            foreach (MassElement element in modules)
                AccumulateInertia(tensor, element, centerOfMass);
            Diagonalize(
                tensor,
                out Vector3 inertia,
                out Quaternion rotationTensor);

            body.mass = Mathf.Max(1f, totalMass);
            body.centerOfMass = centerOfMass;
            body.inertiaTensor = new Vector3(
                Mathf.Max(0.01f, inertia.x),
                Mathf.Max(0.01f, inertia.y),
                Mathf.Max(0.01f, inertia.z));
            body.inertiaTensorRotation = rotationTensor;
            telemetry.mass = new VehicleMassProperties
            {
                totalMass = body.mass,
                centerOfMassLocal = centerOfMass,
                inertiaTensor = body.inertiaTensor,
                inertiaTensorRotation = rotationTensor
            };
        }

        void BuildActuatorsAndSurfaces(GridModuleView[] views)
        {
            actuators.Clear();
            aeroSurfaces.Clear();
            bodyDragArea = Vector3.one;
            Vector3 weightedThrustCenter = Vector3.zero;
            float totalThrust = 0f;

            foreach (GridModuleView view in views)
            {
                if (view == null || view.Record?.Definition == null)
                    continue;
                Vector3Int footprint = view.Record.Definition.Footprint;
                bodyDragArea += new Vector3(
                    footprint.y * footprint.z,
                    footprint.x * footprint.z,
                    footprint.x * footprint.y) * 0.12f;

                NeoXBehaviorModule rootBehavior =
                    view.GetComponent<NeoXBehaviorModule>();
                NeoXBehaviorModule[] behaviors = rootBehavior != null
                    ? new[] { rootBehavior }
                    : view.GetComponentsInChildren<
                        NeoXBehaviorModule>(true)
                        .Take(1)
                        .ToArray();
                foreach (NeoXBehaviorModule behavior in behaviors)
                {
                    if (behavior == null)
                        continue;
                    string kind = behavior.BehaviorKind.ToString();
                    if (kind == "Thruster" || kind == "Hover")
                    {
                        ResolveThrusterProfile(
                            behavior.SourceId,
                            out float force,
                            out float response,
                            out float consumption,
                            out bool atmosphereOnly);
                        var descriptor = new VehicleActuatorDescriptor
                        {
                            module = behavior,
                            localPosition =
                                transform.InverseTransformPoint(
                                    behavior.WorldExhaustPosition),
                            localDirection =
                                transform.InverseTransformDirection(
                                    -behavior.WorldExhaustDirection)
                                    .normalized,
                            maximumForce = force *
                                           Mathf.Max(0.01f, behavior.Strength),
                            responseTime = response,
                            energyPerSecond = consumption,
                            manualGroupId = view.Record.ManualGroupId,
                            atmosphereOnly = atmosphereOnly
                        };
                        descriptor.exhaust =
                            behavior.GetComponent<
                                NeoXThrusterExhaustVfx>() ??
                            behavior.gameObject.AddComponent<
                                NeoXThrusterExhaustVfx>();
                        descriptor.exhaust.Configure(behavior);
                        actuators.Add(descriptor);
                        weightedThrustCenter +=
                            descriptor.localPosition *
                            descriptor.maximumForce;
                        totalThrust += descriptor.maximumForce;
                    }
                    else if (kind == "Wing" ||
                             kind == "ControlSurface")
                    {
                        Vector3 localChord =
                            transform.InverseTransformDirection(
                                behavior.WorldMuzzleDirection);
                        Vector3 localNormal =
                            transform.InverseTransformDirection(
                                behavior.WorldMountNormal);
                        if (localChord.sqrMagnitude < 0.1f)
                            localChord = Vector3.forward;
                        if (localNormal.sqrMagnitude < 0.1f ||
                            Mathf.Abs(Vector3.Dot(
                                localChord.normalized,
                                localNormal.normalized)) > 0.9f)
                            localNormal = Vector3.up;
                        aeroSurfaces.Add(new AeroSurface
                        {
                            root = behavior.transform,
                            localPosition =
                                transform.InverseTransformPoint(
                                    view.transform.position),
                            localChord = localChord.normalized,
                            localNormal = localNormal.normalized,
                            area = Mathf.Max(
                                0.5f,
                                Mathf.Max(
                                    footprint.x * footprint.z,
                                    footprint.y * footprint.z) * 0.65f),
                            controlSurface = kind == "ControlSurface"
                        });
                    }
                }
            }
            telemetry.thrustCenterLocal = totalThrust > 0f
                ? weightedThrustCenter / totalThrust
                : telemetry.mass.centerOfMassLocal;
            telemetry.liftCenterLocal = aeroSurfaces.Count > 0
                ? aeroSurfaces.Aggregate(
                    Vector3.zero,
                    (sum, item) => sum + item.localPosition) /
                  aeroSurfaces.Count
                : telemetry.mass.centerOfMassLocal;
        }

        void BuildWheelSet()
        {
            wheels.Clear();
            wheels.AddRange(
                GetComponentsInChildren<ModularWheelRuntime>(false)
                    .Where(item =>
                        item != null &&
                        item.enabled &&
                        item.gameObject.activeInHierarchy));
            foreach (ModularWheelRuntime wheel in wheels)
            {
                wheel.BindVehicle(body, transform);
                float localZ = transform.InverseTransformPoint(
                    wheel.transform.position).z;
                wheel.SetAutomaticRole(
                    localZ >= telemetry.mass.centerOfMassLocal.z
                        ? WheelRoleOverride.SteerDrive
                        : WheelRoleOverride.DriveOnly);
            }
        }

        void RefreshEnergyCapacity(GridModuleView[] views)
        {
            float previousRatio = energyCapacity > 0f
                ? energy / energyCapacity
                : 1f;
            energyCapacity = 0f;
            foreach (GridModuleView view in views)
            {
                if (view?.Record?.Definition == null)
                    continue;
                energyCapacity +=
                    view.Record.Definition.EnergyCapacity;
                foreach (NeoXBehaviorModule module in
                         view.GetComponentsInChildren<
                             NeoXBehaviorModule>(true))
                {
                    energyCapacity = Mathf.Max(
                        energyCapacity,
                        module.EnergyCapacity);
                }
            }
            energyCapacity = Mathf.Max(100f, energyCapacity);
            energy = Mathf.Clamp(
                energyCapacity * previousRatio,
                0f,
                energyCapacity);
            telemetry.energy = energy;
            telemetry.energyCapacity = energyCapacity;
        }

        void RefreshAuthority()
        {
            Vector3 positiveForce = Vector3.zero;
            Vector3 negativeForce = Vector3.zero;
            Vector3 positiveTorque = Vector3.zero;
            Vector3 negativeTorque = Vector3.zero;
            float forwardTorque = 0f;
            float forwardForce = 0f;
            foreach (VehicleActuatorDescriptor actuator in actuators)
            {
                Vector3 force =
                    actuator.localDirection * actuator.maximumForce;
                Vector3 torque = Vector3.Cross(
                    actuator.localPosition -
                    telemetry.mass.centerOfMassLocal,
                    force);
                AddSigned(force, ref positiveForce, ref negativeForce);
                AddSigned(torque, ref positiveTorque, ref negativeTorque);
                if (force.z > 0f)
                {
                    forwardForce += force.z;
                    if (controlScheme !=
                        VehicleControlScheme.Assisted)
                    {
                        forwardTorque += torque.magnitude;
                    }
                }
            }
            if (assistLevel == VehicleAssistLevel.WeakCore)
            {
                positiveForce += new Vector3(
                    CorePlanarForce,
                    CoreUpForce,
                    CorePlanarForce);
                negativeForce += new Vector3(
                    CorePlanarForce,
                    CoreDownForce,
                    CorePlanarForce);
            }
            if (assistLevel != VehicleAssistLevel.Disabled)
            {
                positiveTorque += Vector3.one * CoreTorque;
                negativeTorque += Vector3.one * CoreTorque;
            }
            float mass = Mathf.Max(1f, telemetry.mass.totalMass);
            Vector3 inertia = telemetry.mass.inertiaTensor;
            float weight = mass * Mathf.Max(0.1f, gravity.magnitude);
            float hover = positiveForce.y / weight;
            float forwardDrag =
                0.5f * Mathf.Max(0.01f, airDensity) * 0.55f *
                Mathf.Max(0.1f, bodyDragArea.z);
            float terminal = Mathf.Sqrt(
                Mathf.Max(0f, positiveForce.z) /
                Mathf.Max(0.001f, forwardDrag));
            string status;
            if (hover < 1f)
                status = "推力不足：无法持续悬停";
            else if (allocator.ControlRank >= 6)
                status = "控制完整";
            else if (assistLevel != VehicleAssistLevel.Disabled)
                status = "辅助可控";
            else
                status = "缺少控制轴";

            telemetry.authority = new VehicleControlAuthority
            {
                controlRank = allocator.ControlRank,
                positiveLinearAcceleration = positiveForce / mass,
                negativeLinearAcceleration = negativeForce / mass,
                positiveAngularAcceleration = new Vector3(
                    positiveTorque.x / Mathf.Max(0.01f, inertia.x),
                    positiveTorque.y / Mathf.Max(0.01f, inertia.y),
                    positiveTorque.z / Mathf.Max(0.01f, inertia.z)),
                negativeAngularAcceleration = new Vector3(
                    negativeTorque.x / Mathf.Max(0.01f, inertia.x),
                    negativeTorque.y / Mathf.Max(0.01f, inertia.y),
                    negativeTorque.z / Mathf.Max(0.01f, inertia.z)),
                hoverRatio = hover,
                forwardCrossCoupling = forwardForce > 0f
                    ? Mathf.Clamp01(
                        forwardTorque /
                        (forwardForce * Mathf.Max(
                            1f,
                            bodyDragArea.magnitude)))
                    : 0f,
                estimatedTerminalSpeed = terminal,
                status = status
            };
        }

        void BindModel(GridAssemblyModel value)
        {
            if (model == value)
                return;
            if (model != null)
                model.Changed -= HandleModelChanged;
            model = value;
            if (model != null)
            {
                model.Changed += HandleModelChanged;
                controlScheme = model.ControlScheme;
                assistLevel = model.AssistLevel;
                controlGroups = VehicleControlGroupData.Normalize(
                    model.ControlGroups);
            }
        }

        GridModuleView[] GetCurrentViews()
        {
            GridAssemblyPresenter presenter =
                GetComponent<GridAssemblyPresenter>();
            if (presenter != null &&
                presenter.Views != null &&
                presenter.Views.Count > 0)
            {
                return presenter.Views.Values
                    .Where(view =>
                        view != null &&
                        view.Record != null)
                    .GroupBy(view => view.Record.RuntimeId)
                    .Select(group => group.Last())
                    .ToArray();
            }
            return GetComponentsInChildren<GridModuleView>(true)
                .Where(view =>
                    view != null &&
                    view.Record != null)
                .GroupBy(view => view.Record.RuntimeId)
                .Select(group => group.Last())
                .ToArray();
        }

        void HandleModelChanged()
        {
            controlScheme = model.ControlScheme;
            assistLevel = model.AssistLevel;
            controlGroups = VehicleControlGroupData.Normalize(
                model.ControlGroups);
            Rebuild();
        }

        void OnDestroy()
        {
            if (model != null)
                model.Changed -= HandleModelChanged;
        }

        VehicleInputFrame CaptureInput()
        {
            float horizontal =
                (Input.GetKey(KeyCode.D) ? 1f : 0f) -
                (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float vertical =
                (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
                (Input.GetKey(KeyCode.LeftControl) ||
                 Input.GetKey(KeyCode.RightControl) ? 1f : 0f);
            float forward =
                (Input.GetKey(KeyCode.W) ? 1f : 0f) -
                (Input.GetKey(KeyCode.S) ? 1f : 0f);
            return new VehicleInputFrame
            {
                translation = new Vector3(
                    horizontal,
                    vertical,
                    forward),
                look = new Vector2(
                    Input.GetAxisRaw("Mouse X"),
                    Input.GetAxisRaw("Mouse Y")) * 0.18f,
                roll =
                    (Input.GetKey(KeyCode.E) ? 1f : 0f) -
                    (Input.GetKey(KeyCode.Q) ? 1f : 0f),
                boost =
                    Input.GetKey(KeyCode.LeftShift) ||
                    Input.GetKey(KeyCode.RightShift),
                hoverBrake = Input.GetKey(KeyCode.X),
                freeLook =
                    Input.GetKey(KeyCode.LeftAlt) ||
                    Input.GetKey(KeyCode.RightAlt)
            };
        }

        static void ResolveThrusterProfile(
            string sourceId,
            out float force,
            out float response,
            out float energyPerSecond,
            out bool atmosphereOnly)
        {
            string id = sourceId ?? string.Empty;
            if (id.IndexOf(
                    "speed_rocketsmall_112",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                force = 3000f;
                response = 0.05f;
                energyPerSecond = 8f;
                atmosphereOnly = false;
            }
            else if (id.IndexOf(
                         "small_propeller_224",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                force = 5000f;
                response = 0.2f;
                energyPerSecond = 10f;
                atmosphereOnly = true;
            }
            else
            {
                force = 6000f;
                response = 0.12f;
                energyPerSecond = 18f;
                atmosphereOnly = false;
            }
        }

        static void AddSigned(
            Vector3 value,
            ref Vector3 positive,
            ref Vector3 negative)
        {
            positive += new Vector3(
                Mathf.Max(0f, value.x),
                Mathf.Max(0f, value.y),
                Mathf.Max(0f, value.z));
            negative += new Vector3(
                Mathf.Max(0f, -value.x),
                Mathf.Max(0f, -value.y),
                Mathf.Max(0f, -value.z));
        }

        static void AccumulateInertia(
            double[,] tensor,
            MassElement element,
            Vector3 centerOfMass)
        {
            Vector3 size = element.size;
            Vector3 diagonal = new Vector3(
                element.mass * (size.y * size.y + size.z * size.z) / 12f,
                element.mass * (size.x * size.x + size.z * size.z) / 12f,
                element.mass * (size.x * size.x + size.y * size.y) / 12f);
            Matrix4x4 rotation =
                Matrix4x4.Rotate(element.rotation);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                double rotated = 0d;
                for (int axis = 0; axis < 3; axis++)
                    rotated += rotation[row, axis] *
                               diagonal[axis] *
                               rotation[column, axis];
                tensor[row, column] += rotated;
            }
            Vector3 offset = element.center - centerOfMass;
            float squared = offset.sqrMagnitude;
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                double parallel = element.mass *
                                  ((row == column ? squared : 0f) -
                                   offset[row] * offset[column]);
                tensor[row, column] += parallel;
            }
        }

        static void Diagonalize(
            double[,] source,
            out Vector3 eigenvalues,
            out Quaternion rotation)
        {
            var a = (double[,])source.Clone();
            var vectors = new double[3, 3];
            for (int index = 0; index < 3; index++)
                vectors[index, index] = 1d;
            for (int iteration = 0; iteration < 18; iteration++)
            {
                int p = 0;
                int q = 1;
                double maximum = Math.Abs(a[p, q]);
                if (Math.Abs(a[0, 2]) > maximum)
                {
                    p = 0;
                    q = 2;
                    maximum = Math.Abs(a[p, q]);
                }
                if (Math.Abs(a[1, 2]) > maximum)
                {
                    p = 1;
                    q = 2;
                    maximum = Math.Abs(a[p, q]);
                }
                if (maximum < 0.000001d)
                    break;
                double angle = 0.5d * Math.Atan2(
                    2d * a[p, q],
                    a[q, q] - a[p, p]);
                double cosine = Math.Cos(angle);
                double sine = Math.Sin(angle);
                for (int row = 0; row < 3; row++)
                {
                    double ap = a[row, p];
                    double aq = a[row, q];
                    a[row, p] = cosine * ap - sine * aq;
                    a[row, q] = sine * ap + cosine * aq;
                }
                for (int column = 0; column < 3; column++)
                {
                    double ap = a[p, column];
                    double aq = a[q, column];
                    a[p, column] = cosine * ap - sine * aq;
                    a[q, column] = sine * ap + cosine * aq;
                }
                for (int row = 0; row < 3; row++)
                {
                    double vp = vectors[row, p];
                    double vq = vectors[row, q];
                    vectors[row, p] = cosine * vp - sine * vq;
                    vectors[row, q] = sine * vp + cosine * vq;
                }
            }
            Vector3 right = new Vector3(
                (float)vectors[0, 0],
                (float)vectors[1, 0],
                (float)vectors[2, 0]).normalized;
            Vector3 up = new Vector3(
                (float)vectors[0, 1],
                (float)vectors[1, 1],
                (float)vectors[2, 1]).normalized;
            Vector3 forward = new Vector3(
                (float)vectors[0, 2],
                (float)vectors[1, 2],
                (float)vectors[2, 2]).normalized;
            if (Vector3.Dot(Vector3.Cross(right, up), forward) < 0f)
                forward = -forward;
            rotation = Quaternion.LookRotation(forward, up).normalized;
            eigenvalues = new Vector3(
                (float)a[0, 0],
                (float)a[1, 1],
                (float)a[2, 2]);
        }

        struct MassElement
        {
            public float mass;
            public Vector3 center;
            public Vector3 size;
            public Quaternion rotation;
        }

        sealed class AeroSurface
        {
            public Transform root;
            public Vector3 localPosition;
            public Vector3 localChord;
            public Vector3 localNormal;
            public float area;
            public bool controlSurface;
        }

        struct PendingImpulse
        {
            public Vector3 impulse;
            public Vector3 position;
        }
    }
}
