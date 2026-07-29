using System;
using System.Collections.Generic;
using System.Linq;
using ModularAssembly;
using SpacecraftEditor;
using UnityEngine;

namespace UnityPlanet.ModularAssembly
{
    public enum VehicleCoreAssistMode
    {
        Standard,
        Training
    }

    [Serializable]
    public struct RobocraftTelemetry
    {
        public float totalMass;
        public Vector3 centerOfMassLocal;
        public Vector3 inertia;
        public float groundTopSpeed;
        public float airTopSpeed;
        public float hoverRatio;
        public int activeWheels;
        public int activeAirMovers;
        public string status;

        public string BuildSummary()
        {
            return
                $"RC1 质量 {totalMass:0} kg\n" +
                $"质心 {centerOfMassLocal.x:0.0}, " +
                $"{centerOfMassLocal.y:0.0}, {centerOfMassLocal.z:0.0}\n" +
                $"惯量 {inertia.x:0}, {inertia.y:0}, {inertia.z:0}\n" +
                $"地速 {groundTopSpeed:0}  空速 {airTopSpeed:0} m/s\n" +
                $"升重比 {hoverRatio:0.00}  轮胎 {activeWheels}\n" +
                $"移动件 {activeAirMovers}  {status}";
        }
    }

    [DisallowMultipleComponent]
    public sealed class RobocraftMotionCoordinator : MonoBehaviour
    {
        const float CoreDampingTorque = 1200f;
        const float TrainingUpForce = 13000f;
        const float TrainingDownForce = 3000f;
        const float TrainingPlanarForce = 2500f;
        const float TrainingTorque = 1200f;

        sealed class AirMover
        {
            public NeoXBehaviorModule module;
            public NeoXThrusterExhaustVfx vfx;
            public Vector3 localPosition;
            public Vector3 localDirection;
            public float maximumForce;
            public float responseTime;
            public float softSpeed;
            public bool atmosphereOnly;
            public bool propeller;
            public float targetThrottle;
            public float actualThrottle;
        }

        sealed class AeroSurface
        {
            public Transform source;
            public Vector3 localPosition;
            public Vector3 localChord;
            public Vector3 localNormal;
            public float area;
        }

        Rigidbody body;
        ShipAssembly assembly;
        GridAssemblyModel model;
        GridAssemblyPresenter presenter;
        readonly VehicleForceLedger ledger = new VehicleForceLedger();
        readonly VirtualRcs24Allocator rcs24 =
            new VirtualRcs24Allocator();
        readonly List<AirMover> airMovers = new List<AirMover>();
        readonly List<AeroSurface> aeroSurfaces = new List<AeroSurface>();
        readonly List<ModularWheelRuntime> wheels =
            new List<ModularWheelRuntime>();
        RobocraftPlayerDamageController damageController;
        VehicleCoreAssistMode coreAssistMode =
            VehicleCoreAssistMode.Standard;
        Vector3 gravity = Vector3.down * 9.81f;
        float airDensity = 1.225f;
        float altitude;
        bool active;
        bool damageDisabled;
        bool rebuildPending;
        Vector3 heldAimForward = Vector3.forward;
        Vector3 hoverPosition;
        bool hoverHeld;
        float[] moverForceScales = Array.Empty<float>();
        RobocraftTelemetry telemetry;

        public bool IsActive => active && !damageDisabled;
        public VehicleCoreAssistMode CoreAssistMode => coreAssistMode;
        public RobocraftTelemetry Telemetry => telemetry;
        public DirectionalAuthority24 Authority24 => rcs24.Authority;

        public void Configure(Rigidbody target, ShipAssembly shipAssembly)
        {
            body = target;
            assembly = shipAssembly;
            ModularAssemblyLabController lab =
                FindObjectOfType<ModularAssemblyLabController>();
            BindModel(lab != null ? lab.Model : null);
            presenter = GetComponent<GridAssemblyPresenter>();
            if (presenter != null)
            {
                presenter.Rebuilt -= HandlePresenterRebuilt;
                presenter.Rebuilt += HandlePresenterRebuilt;
            }
            damageController =
                GetComponent<RobocraftPlayerDamageController>()
                ?? gameObject.AddComponent<RobocraftPlayerDamageController>();
            damageController.Configure(model, presenter, body, this);
            Rebuild();
            enabled = true;
        }

        public void BeginFlight()
        {
            if (body == null)
                return;
            Rebuild();
            damageDisabled = false;
            body.useGravity = false;
            body.maxAngularVelocity = 10f;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            heldAimForward = CameraForward();
            foreach (ModularWheelRuntime wheel in wheels)
                wheel.SetFlightMode(true);
            damageController?.BeginFlight();
            active = true;
        }

        public void EndFlight()
        {
            active = false;
            damageDisabled = false;
            ZeroAirThrottle();
            foreach (ModularWheelRuntime wheel in wheels)
                if (wheel != null)
                    wheel.SetFlightMode(false);
            damageController?.EndFlight();
        }

        public void SetCoreAssistMode(VehicleCoreAssistMode value)
        {
            coreAssistMode = value;
            model?.SetCoreAssistMode(value);
            RebuildRcs24Allocator();
            RefreshTelemetry();
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

        public void QueueVisualRecoil(Vector3 impulse, Vector3 position)
        {
            // Robocraft-style recoil is visual and must not destabilize motion.
        }

        public void SetDamageDisabled(bool value)
        {
            damageDisabled = value;
            if (value)
                ZeroAirThrottle();
        }

        public void SetLegacyDamageEnabled(bool value)
        {
            damageController?.SetExternallySuppressed(!value);
        }

        void FixedUpdate()
        {
            if (body == null)
                return;
            if (rebuildPending)
            {
                rebuildPending = false;
                Rebuild();
            }
            if (!IsActive || body.isKinematic)
                return;

            ledger.Begin(body);
            ledger.AddAcceleration(gravity);
            Vector2 move = new Vector2(
                Key(KeyCode.D, KeyCode.A),
                Key(KeyCode.W, KeyCode.S));
            float vertical =
                (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
                ((Input.GetKey(KeyCode.LeftControl) ||
                  Input.GetKey(KeyCode.RightControl)) ? 1f : 0f);
            float roll = Key(KeyCode.E, KeyCode.Q);
            bool boost =
                Input.GetKey(KeyCode.LeftShift) ||
                Input.GetKey(KeyCode.RightShift);
            bool brake = Input.GetKey(KeyCode.X);
            bool freeLook =
                Input.GetKey(KeyCode.LeftAlt) ||
                Input.GetKey(KeyCode.RightAlt);
            Vector3 up = gravity.sqrMagnitude > 0.001f
                ? -gravity.normalized
                : Vector3.up;
            int grounded = ProbeWheels(up);
            bool groundMode =
                grounded >= 2 &&
                Mathf.Abs(Vector3.Dot(body.velocity, up)) < 3f;

            if (groundMode)
            {
                ApplyWheelMovement(up, move, brake, boost, grounded);
                ApplyGroundStability(up, brake);
                SetAirThrottleZero();
            }
            else
            {
                ApplyAirMovement(
                    up,
                    move,
                    vertical,
                    roll,
                    brake,
                    boost,
                    freeLook);
            }
            ApplyAerodynamics();
            ledger.Apply();
        }

        static float Key(KeyCode positive, KeyCode negative)
        {
            return (Input.GetKey(positive) ? 1f : 0f) -
                   (Input.GetKey(negative) ? 1f : 0f);
        }

        int ProbeWheels(Vector3 up)
        {
            int result = 0;
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel != null &&
                    wheel.enabled &&
                    wheel.gameObject.activeInHierarchy &&
                    wheel.ProbeContact(up))
                    result++;
            }
            return result;
        }

        void ApplyWheelMovement(
            Vector3 up,
            Vector2 input,
            bool braking,
            bool boost,
            int grounded)
        {
            Vector3 cameraForward = CameraPlanarForward(up);
            Vector3 cameraRight =
                Vector3.Cross(up, cameraForward).normalized;
            Vector3 desired = Vector3.ClampMagnitude(
                cameraForward * input.y + cameraRight * input.x,
                1f);
            Vector3 vehicleForward =
                Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (vehicleForward.sqrMagnitude < 0.001f)
                vehicleForward = cameraForward;
            Vector3 vehicleRight =
                Vector3.Cross(up, vehicleForward).normalized;
            float forwardDot = Vector3.Dot(desired, vehicleForward);
            float throttle = desired.sqrMagnitude < 0.001f
                ? 0f
                : Mathf.Abs(forwardDot) < 0.2f
                    ? Mathf.Sign(forwardDot == 0f ? 1f : forwardDot) *
                      desired.magnitude * 0.35f
                    : forwardDot;
            float steering = desired.sqrMagnitude < 0.001f
                ? 0f
                : Mathf.Clamp(
                    Vector3.Dot(desired, vehicleRight) * 1.8f,
                    -1f,
                    1f);
            foreach (ModularWheelRuntime wheel in wheels)
            {
                if (wheel == null ||
                    !wheel.enabled ||
                    !wheel.gameObject.activeInHierarchy)
                    continue;
                wheel.ApplyForces(
                    up,
                    vehicleForward,
                    throttle,
                    steering,
                    braking,
                    boost,
                    body.mass,
                    grounded,
                    ledger);
            }
        }

        void ApplyGroundStability(Vector3 up, bool braking)
        {
            float inertia =
                (body.inertiaTensor.x +
                 body.inertiaTensor.y +
                 body.inertiaTensor.z) / 3f;
            Vector3 level =
                Vector3.Cross(transform.up, up) *
                Mathf.Min(inertia * 4f, 18000f);
            Vector3 damping =
                -body.angularVelocity *
                Mathf.Min(inertia * (braking ? 3.2f : 1.7f), 15000f);
            ledger.AddTorque(
                Vector3.ClampMagnitude(level + damping, 18000f));
        }

        void ApplyAirMovement(
            Vector3 up,
            Vector2 move,
            float vertical,
            float roll,
            bool braking,
            bool boost,
            bool freeLook)
        {
            Vector3 cameraForward = CameraPlanarForward(up);
            Vector3 cameraRight =
                Vector3.Cross(up, cameraForward).normalized;
            rcs24.BeginStep(BuildMoverForceScales());

            Vector3 desiredWorldTorque =
                ResolveOrientationTorque(up, roll, freeLook, braking);
            rcs24.SolveRotation(new Rcs24SolveRequest
            {
                desired = transform.InverseTransformDirection(
                    desiredWorldTorque),
                strictDirection = true,
                group = "rotation"
            });

            if (braking)
            {
                if (!hoverHeld)
                {
                    hoverPosition = body.position;
                    hoverHeld = true;
                }

                float heightError =
                    Vector3.Dot(hoverPosition - body.position, up);
                float verticalSpeed = Vector3.Dot(body.velocity, up);
                float hoverForce = Mathf.Max(
                    0f,
                    body.mass *
                    (gravity.magnitude +
                     heightError * 2f -
                     verticalSpeed * 3.2f));
                rcs24.SolveHover(new Rcs24SolveRequest
                {
                    desired = transform.InverseTransformDirection(
                        up * hoverForce),
                    strictDirection = true,
                    group = "hover_vertical"
                });

                Vector3 planarError = Vector3.ProjectOnPlane(
                    hoverPosition - body.position,
                    up);
                Vector3 planarVelocity =
                    Vector3.ProjectOnPlane(body.velocity, up);
                Vector3 horizontalBrake =
                    planarError * body.mass * 0.8f -
                    planarVelocity * body.mass * 3.2f;
                if (horizontalBrake.sqrMagnitude > 0.01f)
                {
                    rcs24.SolveTranslation(new Rcs24SolveRequest
                    {
                        desired = transform.InverseTransformDirection(
                            horizontalBrake),
                        strictDirection = true,
                        group = "hover_horizontal"
                    });
                }
            }
            else
            {
                hoverHeld = false;
                Vector3 direction = Vector3.ClampMagnitude(
                    cameraForward * move.y +
                    cameraRight * move.x +
                    up * vertical,
                    1f);
                if (direction.sqrMagnitude > 0.0001f)
                {
                    Vector3 target =
                        direction * body.mass * (boost ? 18f : 10f);
                    if (vertical > 0f)
                        target += -gravity * body.mass * vertical;
                    rcs24.SolveTranslation(new Rcs24SolveRequest
                    {
                        desired = transform.InverseTransformDirection(target),
                        strictDirection = true,
                        group = "player_translation"
                    });
                }
            }

            Rcs24SolveResult output =
                rcs24.CompleteStep(Time.fixedDeltaTime);
            ledger.AddForce(transform.TransformDirection(output.localForce));
            ledger.AddTorque(transform.TransformDirection(output.localTorque));
            UpdateAirMoverOutputs(output.thrusterThrottles);
            if (body.velocity.sqrMagnitude > 0.01f)
            {
                float drag = Mathf.Lerp(
                    0.15f,
                    0.5f,
                    Mathf.Clamp01(airDensity / 1.225f));
                ledger.AddForce(
                    -body.velocity * body.velocity.magnitude * drag);
            }
        }

        float[] BuildMoverForceScales()
        {
            if (moverForceScales.Length != airMovers.Count)
                moverForceScales = new float[airMovers.Count];
            for (int i = 0; i < airMovers.Count; i++)
            {
                moverForceScales[i] =
                    AtmosphereFactor(airMovers[i]) *
                    SpeedFactor(airMovers[i]);
            }
            return moverForceScales;
        }

        void UpdateAirMoverOutputs(float[] throttles)
        {
            for (int i = 0; i < airMovers.Count; i++)
            {
                AirMover mover = airMovers[i];
                float throttle = throttles != null && i < throttles.Length
                    ? Mathf.Clamp01(throttles[i])
                    : 0f;
                mover.targetThrottle = throttle;
                mover.actualThrottle = throttle;
                mover.vfx?.SetTargetThrottle(mover.actualThrottle);
            }
        }

        Vector3 ResolveOrientationTorque(
            Vector3 up,
            float roll,
            bool freeLook,
            bool braking)
        {
            if (!freeLook)
                heldAimForward = CameraForward();
            Vector3 desiredForward = heldAimForward.sqrMagnitude > 0.001f
                ? heldAimForward.normalized
                : transform.forward;
            Quaternion target =
                Quaternion.LookRotation(desiredForward, up) *
                Quaternion.AngleAxis(-roll * 28f, Vector3.forward);
            Quaternion error = target * Quaternion.Inverse(body.rotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x))
                axis = Vector3.zero;
            Vector3 authority = AngularAuthority();
            if (coreAssistMode == VehicleCoreAssistMode.Training)
                authority += Vector3.one * TrainingTorque;
            float inertia = Mathf.Max(1f, body.inertiaTensor.magnitude);
            Vector3 wanted =
                axis * angle * Mathf.Deg2Rad * inertia * 3f -
                body.angularVelocity * inertia *
                (braking ? 3f : 1.4f);
            Vector3 local = transform.InverseTransformDirection(wanted);
            local = new Vector3(
                Mathf.Clamp(local.x, -authority.x, authority.x),
                Mathf.Clamp(local.y, -authority.y, authority.y),
                Mathf.Clamp(local.z, -authority.z, authority.z));
            Vector3 coreDamping = Vector3.ClampMagnitude(
                -body.angularVelocity * CoreDampingTorque,
                CoreDampingTorque);
            return transform.TransformDirection(local) + coreDamping;
        }

        void ApplyAerodynamics()
        {
            if (airDensity <= 0.001f)
                return;
            foreach (AeroSurface surface in aeroSurfaces)
            {
                if (surface.source == null)
                    continue;
                Vector3 position =
                    transform.TransformPoint(surface.localPosition);
                Vector3 velocity = body.GetPointVelocity(position);
                float speed = velocity.magnitude;
                if (speed < 1f)
                    continue;
                Vector3 chord =
                    transform.TransformDirection(surface.localChord).normalized;
                Vector3 normal =
                    transform.TransformDirection(surface.localNormal).normalized;
                Vector3 flow = -velocity.normalized;
                float angle = Mathf.Asin(
                    Mathf.Clamp(Vector3.Dot(flow, normal), -1f, 1f));
                float stall = Mathf.Clamp01(
                    1f -
                    Mathf.Max(0f, Mathf.Abs(angle) - 0.45f) / 0.75f);
                float pressure = 0.5f * airDensity * speed * speed;
                Vector3 span = Vector3.Cross(normal, chord).normalized;
                Vector3 liftDirection =
                    Vector3.Cross(velocity.normalized, span).normalized;
                if (Vector3.Dot(liftDirection, normal) < 0f)
                    liftDirection = -liftDirection;
                Vector3 lift =
                    liftDirection *
                    pressure * surface.area *
                    Mathf.Sin(angle * 2f) * 1.25f * stall;
                Vector3 drag =
                    -velocity.normalized *
                    pressure * surface.area *
                    (0.025f + 0.22f * angle * angle);
                ledger.AddForce(lift + drag);
                ledger.AddTorque(Vector3.ClampMagnitude(
                    Vector3.Cross(
                        position - body.worldCenterOfMass,
                        lift + drag) * 0.35f,
                    12000f));
            }
        }

        float CalculateLiftAuthority(Vector3 up)
        {
            float result =
                coreAssistMode == VehicleCoreAssistMode.Training
                    ? TrainingUpForce
                    : 0f;
            foreach (AirMover mover in airMovers)
            {
                result += Mathf.Max(
                              0f,
                              Vector3.Dot(WorldDirection(mover), up)) *
                          mover.maximumForce *
                          AtmosphereFactor(mover);
                if (mover.propeller)
                    result += mover.maximumForce *
                              AtmosphereFactor(mover) * 0.35f;
            }
            return result;
        }

        Vector3 AngularAuthority()
        {
            Vector3 result = Vector3.zero;
            foreach (AirMover mover in airMovers)
            {
                Vector3 torque = Vector3.Cross(
                    mover.localPosition - body.centerOfMass,
                    mover.localDirection * mover.maximumForce);
                float baseline = mover.maximumForce * 0.22f;
                result += new Vector3(
                    Mathf.Abs(torque.x) + baseline,
                    Mathf.Abs(torque.y) + baseline,
                    Mathf.Abs(torque.z) + baseline);
            }
            foreach (AeroSurface surface in aeroSurfaces)
            {
                float arm =
                    (surface.localPosition - body.centerOfMass).magnitude;
                result += Vector3.one *
                          surface.area * Mathf.Max(1f, arm) * 420f;
            }
            return result;
        }

        Vector3 WorldDirection(AirMover mover)
        {
            return transform.TransformDirection(
                mover.localDirection).normalized;
        }

        float AtmosphereFactor(AirMover mover)
        {
            return mover.atmosphereOnly
                ? Mathf.Clamp01(airDensity / 1.225f)
                : 1f;
        }

        float SpeedFactor(AirMover mover)
        {
            float speed = Mathf.Max(
                0f,
                Vector3.Dot(body.velocity, WorldDirection(mover)));
            float ratio = speed / Mathf.Max(1f, mover.softSpeed);
            return Mathf.Clamp01(1f - ratio * ratio * 0.85f);
        }

        void SetAirThrottleZero()
        {
            rcs24.ResetState();
            foreach (AirMover mover in airMovers)
            {
                mover.targetThrottle = 0f;
                mover.actualThrottle = Mathf.MoveTowards(
                    mover.actualThrottle,
                    0f,
                    Time.fixedDeltaTime /
                    Mathf.Max(0.01f, mover.responseTime));
                mover.vfx?.SetTargetThrottle(mover.actualThrottle);
            }
        }

        void ZeroAirThrottle()
        {
            rcs24.ResetState();
            foreach (AirMover mover in airMovers)
            {
                mover.targetThrottle = 0f;
                mover.actualThrottle = 0f;
                mover.vfx?.SetTargetThrottle(0f);
            }
        }

        Vector3 CameraForward()
        {
            Camera camera = Camera.main;
            Vector3 result =
                camera != null ? camera.transform.forward : transform.forward;
            return result.sqrMagnitude > 0.001f
                ? result.normalized
                : transform.forward;
        }

        Vector3 CameraPlanarForward(Vector3 up)
        {
            Vector3 result = Vector3.ProjectOnPlane(CameraForward(), up);
            if (result.sqrMagnitude < 0.001f)
                result = Vector3.ProjectOnPlane(transform.forward, up);
            return result.sqrMagnitude > 0.001f
                ? result.normalized
                : Vector3.forward;
        }

        public void Rebuild()
        {
            if (body == null)
                return;
            GridModuleView[] views = CurrentViews();
            BuildMassProperties(views);
            BuildMovementParts(views);
            AssignSprungMasses();
            RefreshTelemetry();
            damageController?.Rebind();
        }

        void BuildMassProperties(GridModuleView[] views)
        {
            float total = 0f;
            Vector3 weighted = Vector3.zero;
            foreach (GridModuleView view in views)
            {
                if (view?.Record?.Definition == null)
                    continue;
                float mass = Mathf.Max(0.01f, view.Record.Definition.MassKg);
                Vector3 center = GridAssemblyModel.ModuleCenter(view.Record);
                total += mass;
                weighted += center * mass;
            }
            total = Mathf.Max(1f, total);
            Vector3 centerOfMass = weighted / total;
            Vector3 inertia = Vector3.zero;
            foreach (GridModuleView view in views)
            {
                if (view?.Record?.Definition == null)
                    continue;
                float mass = Mathf.Max(0.01f, view.Record.Definition.MassKg);
                Vector3 size = (Vector3)GridOrientation.RotatedSize(
                    view.Record.Definition.Footprint,
                    view.Record.Pose.orientation);
                Vector3 offset =
                    GridAssemblyModel.ModuleCenter(view.Record) - centerOfMass;
                inertia.x += mass / 12f *
                             (size.y * size.y + size.z * size.z) +
                             mass * (offset.y * offset.y + offset.z * offset.z);
                inertia.y += mass / 12f *
                             (size.x * size.x + size.z * size.z) +
                             mass * (offset.x * offset.x + offset.z * offset.z);
                inertia.z += mass / 12f *
                             (size.x * size.x + size.y * size.y) +
                             mass * (offset.x * offset.x + offset.y * offset.y);
            }
            inertia = new Vector3(
                Mathf.Max(1f, inertia.x),
                Mathf.Max(1f, inertia.y),
                Mathf.Max(1f, inertia.z));
            body.mass = total;
            body.centerOfMass = centerOfMass;
            body.inertiaTensorRotation = Quaternion.identity;
            body.inertiaTensor = inertia;
            telemetry.totalMass = total;
            telemetry.centerOfMassLocal = centerOfMass;
            telemetry.inertia = inertia;
        }

        void BuildMovementParts(GridModuleView[] views)
        {
            ZeroAirThrottle();
            airMovers.Clear();
            aeroSurfaces.Clear();
            wheels.Clear();
            foreach (GridModuleView view in views)
            {
                if (view == null || !view.gameObject.activeInHierarchy)
                    continue;
                ModularWheelRuntime wheel =
                    view.GetComponentInChildren<ModularWheelRuntime>(true);
                if (wheel != null &&
                    wheel.enabled &&
                    wheel.gameObject.activeInHierarchy)
                {
                    wheel.BindVehicle(body, transform);
                    wheels.Add(wheel);
                }
                NeoXBehaviorModule module =
                    view.GetComponentInChildren<NeoXBehaviorModule>(true);
                if (module == null)
                    continue;
                if (module.BehaviorKind == GridModuleBehaviorKind.Thruster)
                    AddAirMover(module);
                if (module.BehaviorKind == GridModuleBehaviorKind.Wing ||
                    module.BehaviorKind ==
                    GridModuleBehaviorKind.ControlSurface)
                {
                    Vector3Int footprint =
                        view.Record.Definition.Footprint;
                    aeroSurfaces.Add(new AeroSurface
                    {
                        source = view.transform,
                        localPosition = transform.InverseTransformPoint(
                            view.transform.position),
                        localChord = transform.InverseTransformDirection(
                            view.transform.forward).normalized,
                        localNormal = transform.InverseTransformDirection(
                            view.transform.up).normalized,
                        area = Mathf.Max(
                            0.5f,
                            Mathf.Max(
                                footprint.x * footprint.z,
                                footprint.y * footprint.z) * 0.65f)
                    });
                }
            }
            RebuildRcs24Allocator();
        }

        void AddAirMover(NeoXBehaviorModule module)
        {
            string id = module.SourceId ?? string.Empty;
            float force;
            float response;
            float speed;
            bool atmosphereOnly;
            bool propeller;
            if (id.IndexOf(
                    "speed_rocketsmall_112",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                force = 3000f;
                response = 0.05f;
                speed = 100f;
                atmosphereOnly = false;
                propeller = false;
            }
            else if (id.IndexOf(
                         "small_propeller_224",
                         StringComparison.OrdinalIgnoreCase) >= 0)
            {
                force = 5000f;
                response = 0.2f;
                speed = 80f;
                atmosphereOnly = true;
                propeller = true;
            }
            else
            {
                force = 120000f;
                response = 0.12f;
                speed = 140f;
                atmosphereOnly = false;
                propeller = false;
            }
            Vector3 direction =
                -transform.InverseTransformDirection(
                    module.WorldExhaustDirection).normalized;
            if (direction.sqrMagnitude < 0.001f)
                direction = transform.InverseTransformDirection(
                    module.transform.forward).normalized;
            NeoXThrusterExhaustVfx vfx =
                module.GetComponent<NeoXThrusterExhaustVfx>()
                ?? module.gameObject.AddComponent<NeoXThrusterExhaustVfx>();
            vfx.Configure(module);
            airMovers.Add(new AirMover
            {
                module = module,
                vfx = vfx,
                localPosition = transform.InverseTransformPoint(
                    module.WorldExhaustPosition),
                localDirection = direction,
                maximumForce = force,
                responseTime = response,
                softSpeed = speed,
                atmosphereOnly = atmosphereOnly,
                propeller = propeller
            });
        }

        void RebuildRcs24Allocator()
        {
            if (body == null)
                return;
            Rcs24ThrusterInput[] inputs =
                new Rcs24ThrusterInput[airMovers.Count];
            for (int i = 0; i < airMovers.Count; i++)
            {
                AirMover mover = airMovers[i];
                inputs[i] = new Rcs24ThrusterInput
                {
                    sourceIndex = i,
                    localPosition = mover.localPosition,
                    localDirection = mover.localDirection,
                    maximumForce = mover.maximumForce,
                    responseTime = mover.responseTime
                };
            }
            moverForceScales = new float[airMovers.Count];
            rcs24.Rebuild(
                inputs,
                body.centerOfMass,
                coreAssistMode == VehicleCoreAssistMode.Training,
                TrainingUpForce,
                TrainingDownForce,
                TrainingPlanarForce);
        }

        void AssignSprungMasses()
        {
            if (wheels.Count == 0)
                return;
            float baseMass = body.mass / wheels.Count;
            Vector3 center = body.centerOfMass;
            float scale = Mathf.Max(
                1f,
                wheels.Max(wheel =>
                    (transform.InverseTransformPoint(
                         wheel.transform.position) - center).magnitude));
            float[] masses =
                Enumerable.Repeat(baseMass, wheels.Count).ToArray();
            for (int iteration = 0; iteration < 32; iteration++)
            {
                float massError = masses.Sum() - body.mass;
                Vector3 moment = Vector3.zero;
                for (int i = 0; i < wheels.Count; i++)
                {
                    Vector3 arm =
                        transform.InverseTransformPoint(
                            wheels[i].transform.position) - center;
                    moment += arm * masses[i];
                }
                for (int i = 0; i < wheels.Count; i++)
                {
                    Vector3 arm =
                        transform.InverseTransformPoint(
                            wheels[i].transform.position) - center;
                    float gradient =
                        massError +
                        Vector3.Dot(moment, arm) / (scale * scale);
                    masses[i] = Mathf.Max(
                        baseMass * 0.08f,
                        masses[i] -
                        gradient * 0.08f / wheels.Count);
                }
            }
            float normalization =
                body.mass / Mathf.Max(0.01f, masses.Sum());
            for (int i = 0; i < wheels.Count; i++)
                wheels[i].SetSprungMass(masses[i] * normalization);
        }

        void RefreshTelemetry()
        {
            float airSpeed = airMovers.Count == 0
                ? 0f
                : airMovers.Max(item => item.softSpeed);
            telemetry.groundTopSpeed = wheels.Count == 0
                ? 0f
                : wheels.Max(item => item.Profile.maximumSpeed);
            telemetry.airTopSpeed = airSpeed;
            telemetry.hoverRatio =
                CalculateLiftAuthority(Vector3.up) /
                Mathf.Max(1f, body.mass * gravity.magnitude);
            telemetry.activeWheels = wheels.Count;
            telemetry.activeAirMovers = airMovers.Count;
            telemetry.status =
                wheels.Count > 0 && airMovers.Count > 0
                    ? "可陆空混合"
                    : wheels.Count > 0
                        ? "可地面驾驶"
                        : telemetry.hoverRatio >= 1f
                            ? "可持续飞行"
                            : coreAssistMode ==
                              VehicleCoreAssistMode.Training
                                ? "仅新手辅助可飞"
                                : "缺少升力";
        }

        GridModuleView[] CurrentViews()
        {
            presenter = presenter != null
                ? presenter
                : GetComponent<GridAssemblyPresenter>();
            return presenter == null
                ? Array.Empty<GridModuleView>()
                : presenter.Views.Values
                    .Where(view =>
                        view != null &&
                        view.Record != null &&
                        view.gameObject.activeInHierarchy)
                    .GroupBy(view => view.Record.RuntimeId)
                    .Select(group => group.Last())
                    .ToArray();
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
                coreAssistMode = model.CoreAssistMode;
            }
        }

        void HandleModelChanged()
        {
            coreAssistMode = model != null
                ? model.CoreAssistMode
                : VehicleCoreAssistMode.Standard;
            rebuildPending = true;
        }

        void HandlePresenterRebuilt()
        {
            rebuildPending = true;
            damageController?.Rebind();
        }

        void OnDestroy()
        {
            if (model != null)
                model.Changed -= HandleModelChanged;
            if (presenter != null)
                presenter.Rebuilt -= HandlePresenterRebuilt;
        }
    }

    public sealed class RobocraftModuleDamageReceiver :
        MonoBehaviour,
        ISpaceDamageable
    {
        RobocraftPlayerDamageController owner;
        string runtimeId;

        public float Integrity =>
            owner == null ? 0f : owner.GetIntegrity(runtimeId);
        public float MaximumIntegrity =>
            owner == null ? 0f : owner.GetMaximumIntegrity(runtimeId);
        public bool IsDestroyed =>
            owner == null || owner.IsDestroyed(runtimeId);

        public void Initialize(
            RobocraftPlayerDamageController controller,
            string id)
        {
            owner = controller;
            runtimeId = id;
        }

        public void ApplyDamage(SpaceDamageInfo damage)
        {
            owner?.ApplyDamage(runtimeId, damage);
        }
    }

    [DisallowMultipleComponent]
    public sealed class RobocraftPlayerDamageController : MonoBehaviour
    {
        const int MaxDebris = 24;
        GridAssemblyModel model;
        GridAssemblyPresenter presenter;
        Rigidbody ownerBody;
        RobocraftMotionCoordinator coordinator;
        ModularBlueprintData flightBlueprint;
        readonly Dictionary<string, float> health =
            new Dictionary<string, float>();
        readonly Queue<GameObject> debris = new Queue<GameObject>();
        bool sessionActive;
        bool externallySuppressed;

        public void SetExternallySuppressed(bool value)
        {
            externallySuppressed = value;
        }

        public void Configure(
            GridAssemblyModel sourceModel,
            GridAssemblyPresenter sourcePresenter,
            Rigidbody body,
            RobocraftMotionCoordinator motion)
        {
            model = sourceModel;
            presenter = sourcePresenter;
            ownerBody = body;
            coordinator = motion;
            if (presenter != null)
            {
                presenter.Rebuilt -= Rebind;
                presenter.Rebuilt += Rebind;
            }
        }

        public void BeginFlight()
        {
            if (model == null)
                return;
            flightBlueprint = model.CaptureBlueprint();
            sessionActive = true;
            health.Clear();
            foreach (GridModuleRecord record in model.Records)
                health[record.RuntimeId] =
                    record.Definition.MaxIntegrity;
            Rebind();
        }

        public void EndFlight()
        {
            sessionActive = false;
            externallySuppressed = false;
            coordinator?.SetDamageDisabled(false);
            if (flightBlueprint != null && model != null)
                model.RestoreBlueprint(flightBlueprint, out _);
            flightBlueprint = null;
            health.Clear();
            while (debris.Count > 0)
            {
                GameObject item = debris.Dequeue();
                if (item != null)
                    Destroy(item);
            }
        }

        public void Rebind()
        {
            if (!sessionActive || presenter == null)
                return;
            foreach (KeyValuePair<string, GridModuleView> pair in
                     presenter.Views)
            {
                if (pair.Value == null)
                    continue;
                RobocraftModuleDamageReceiver receiver =
                    pair.Value.GetComponent<RobocraftModuleDamageReceiver>()
                    ?? pair.Value.gameObject.AddComponent<
                        RobocraftModuleDamageReceiver>();
                receiver.Initialize(this, pair.Key);
                if (!health.ContainsKey(pair.Key))
                    health[pair.Key] =
                        pair.Value.Record.Definition.MaxIntegrity;
            }
        }

        public void ApplyDamage(string runtimeId, SpaceDamageInfo damage)
        {
            if (!sessionActive ||
                externallySuppressed ||
                !health.TryGetValue(runtimeId, out float value))
                return;
            value -= Mathf.Max(0f, damage.amount);
            health[runtimeId] = value;
            if (value > 0f)
                return;
            GridModuleRecord record = model.Find(runtimeId);
            if (record == null)
                return;
            if (record.Definition.Category == GridModuleCategory.Core)
            {
                SpawnDebris(model.Records
                    .Where(item =>
                        item.Definition.Category != GridModuleCategory.Core)
                    .Select(item => item.Clone())
                    .ToList());
                presenter.ClearVisuals();
                coordinator?.SetDamageDisabled(true);
                return;
            }
            model.TryRemove(runtimeId, out _);
            health.Remove(runtimeId);
            foreach (List<GridModuleRecord> component in
                     model.GetDisconnectedComponents())
            {
                SpawnDebris(component);
                model.RemoveIds(
                    component.Select(item => item.RuntimeId));
                foreach (GridModuleRecord item in component)
                    health.Remove(item.RuntimeId);
            }
        }

        public float GetIntegrity(string runtimeId)
        {
            return health.TryGetValue(runtimeId, out float value)
                ? Mathf.Max(0f, value)
                : 0f;
        }

        public float GetMaximumIntegrity(string runtimeId)
        {
            GridModuleRecord record =
                model == null ? null : model.Find(runtimeId);
            return record == null ? 0f : record.Definition.MaxIntegrity;
        }

        public bool IsDestroyed(string runtimeId)
        {
            return !health.ContainsKey(runtimeId) ||
                   GetIntegrity(runtimeId) <= 0f;
        }

        void SpawnDebris(List<GridModuleRecord> records)
        {
            if (records == null || records.Count == 0)
                return;
            GameObject root = new GameObject("RC1_DetachedCluster");
            root.transform.SetPositionAndRotation(
                transform.position,
                transform.rotation);
            float mass = 0f;
            foreach (GridModuleRecord record in records)
            {
                GameObject instance = Instantiate(
                    record.Definition.Prefab,
                    root.transform);
                instance.transform.localPosition =
                    GridAssemblyModel.ModuleCenter(record);
                instance.transform.localRotation =
                    GridOrientation.Rotation(record.Pose.orientation);
                foreach (MonoBehaviour behaviour in
                         instance.GetComponentsInChildren<MonoBehaviour>(true))
                    behaviour.enabled = false;
                mass += record.Definition.MassKg;
            }
            Rigidbody debrisBody = root.AddComponent<Rigidbody>();
            debrisBody.useGravity = false;
            debrisBody.mass = Mathf.Max(1f, mass);
            debrisBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            if (ownerBody != null)
            {
                debrisBody.velocity = ownerBody.velocity;
                debrisBody.angularVelocity = ownerBody.angularVelocity;
            }
            root.AddComponent<RobocraftDebrisGravity>();
            debris.Enqueue(root);
            while (debris.Count > MaxDebris)
            {
                GameObject oldest = debris.Dequeue();
                if (oldest != null)
                    Destroy(oldest);
            }
        }

        void OnDestroy()
        {
            if (presenter != null)
                presenter.Rebuilt -= Rebind;
        }
    }

    public sealed class RobocraftDebrisGravity : MonoBehaviour
    {
        Rigidbody body;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            if (body != null && !body.isKinematic)
                body.AddForce(
                    Vector3.down * 9.81f,
                    ForceMode.Acceleration);
        }
    }
}
